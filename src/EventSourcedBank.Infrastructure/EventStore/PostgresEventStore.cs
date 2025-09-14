using Dapper;
using EventSourcedBank.Domain.Abstractions;
using EventSourcedBank.Domain.Exceptions;
using EventSourcedBank.Infrastructure.Abstractions;
using Npgsql;
using System.Text.Json;
using static Dapper.SqlMapper;

namespace EventSourcedBank.Infrastructure.EventStore
{
    // Implements the IEventStore interface using PostgreSQL as the backing data store.
    public class PostgresEventStore : IEventStore
    {
        private readonly string _connectionString;
        private readonly IEventTypeMap _eventTypeMap;

        // Initializes a new instance of the PostgresEventStore class.
        public PostgresEventStore(string connectionString, IEventTypeMap eventTypeMap)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _eventTypeMap = eventTypeMap ?? throw new ArgumentNullException(nameof(eventTypeMap));
        }

        // Appends a list of domain events to a specific stream, handling optimistic concurrency checks.
        public async Task AppendAsync(Guid streamId, int expectedVersion, IEnumerable<IDomainEvent> events, CancellationToken ct = default)
        {
            var eventList = events.ToList();
            if (!eventList.Any())
                return; // No events are appended if the list is empty.

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                // The current version of the stream is fetched to check for concurrency conflicts.
                const string currentVersionSql = @"
                    SELECT COALESCE(MAX(version), -1) 
                    FROM event_store.events
                    WHERE stream_id = @streamId;";

                var currentVersion = await conn.ExecuteScalarAsync<int>(
                    new CommandDefinition(currentVersionSql, new { streamId }, transaction: tx, cancellationToken: ct));

                if (currentVersion != expectedVersion)
                    throw new ConcurrencyException($"Stream {streamId} expected v{expectedVersion}, actual v{currentVersion}");

                // Each event is serialized and inserted into the database in order.
                const string insertSql = @"
                    INSERT INTO event_store.events
                    (event_id, stream_id, version, event_type, event_data, metadata, occurred_on)
                    VALUES (@eventId, @streamId, @version, @eventType, @eventData::jsonb, @metadata::jsonb, @occurredOn);";

                for (int i = 0; i < eventList.Count; i++)
                {
                    var e = eventList[i];
                    var v = expectedVersion + 1 + i;

                    var eventType = _eventTypeMap.GetName(e.GetType());
                    var payload = JsonSerializer.Serialize(e, e.GetType(), _eventTypeMap.JsonOptions);
                    var occurredOn = ExtractOccurredOn(e);

                    var metadata = JsonSerializer.Serialize(new
                    {
                        clrType = e.GetType().FullName,
                        assembly = e.GetType().Assembly.GetName().Name
                    }, _eventTypeMap.JsonOptions);

                    var args = new
                    {
                        eventId = Guid.NewGuid(),
                        streamId,
                        version = v,
                        eventType,
                        eventData = payload,
                        metadata,
                        occurredOn
                    };

                    await conn.ExecuteAsync(
                        new CommandDefinition(insertSql, args, transaction: tx, cancellationToken: ct));


                }
                await tx.CommitAsync(ct);
            }
            catch (PostgresException pg) when (pg.SqlState == "23505")
            {
                // A unique key constraint violation on the version implies a concurrency conflict.
                throw new ConcurrencyException($"Concurrency conflict on stream {streamId}: {pg.MessageText}");
            }
        }

        // Loads all domain events for a specific stream from the database in ascending order of their version.
        public async Task<IReadOnlyList<IDomainEvent>> LoadAsync(Guid streamId, CancellationToken ct = default)
        {
            const string sql = @"
            SELECT event_type, event_data::text AS event_data
            FROM event_store.events
            WHERE stream_id = @streamId
            ORDER BY version ASC;";

            await using var conn = new NpgsqlConnection(_connectionString);

            // The query results are mapped to a strongly-typed tuple for processing.
            var rows = await conn.QueryAsync<(string event_type, string event_data)>(
                new CommandDefinition(sql, new { streamId }, cancellationToken: ct));

            var events = new List<IDomainEvent>();
            foreach (var (eventTypeName, eventJson) in rows)
            {
                // The event type name is resolved to a .NET CLR type.
                var clrType = _eventTypeMap.GetType(eventTypeName);

                // The JSON data is deserialized back into a domain event object.
                var domainEvent = (IDomainEvent?)JsonSerializer.Deserialize(eventJson, clrType, _eventTypeMap.JsonOptions);
                if (domainEvent is null)
                    throw new InvalidOperationException($"Failed to deserialize {eventTypeName}");
                events.Add(domainEvent);
            }
            return events;
        }

        // Extracts the OccurredOn timestamp from a domain event using reflection.
        private static DateTimeOffset ExtractOccurredOn(IDomainEvent e)
        {
            return e.OccurredOn;
        }
    }
}
