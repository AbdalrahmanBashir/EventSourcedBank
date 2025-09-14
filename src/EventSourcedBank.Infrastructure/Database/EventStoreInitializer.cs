using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace EventSourcedBank.Infrastructure.Database
{
    // Provides an extension method to initialize the event store database schema and tables.
    public static class EventStoreInitializer
    {
        // Contains the SQL script for creating the event store schema, events table, and necessary indexes.
        private const string Sql = @"
        Create Schema if not exists event_store;  


        Create Table if not exists event_store.events (
            event_id    UUID PRIMARY KEY,
            stream_id   UUID NOT NULL,
            version     INT  NOT NULL,
            event_type  TEXT NOT NULL,
            event_data  JSONB NOT NULL,
            metadata    JSONB NOT NULL DEFAULT '{}'::jsonb,
            occurred_on TIMESTAMPTZ NOT NULL,
            recorded_at TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_events_stream_version
            ON event_store.events (stream_id, version);


        CREATE INDEX IF NOT EXISTS ix_events_stream
            ON event_store.events (stream_id);";

        // Ensures that the event store schema and tables are created in the database during application startup.
        public static async Task EnsureEventStoreCreatedAsync(this IHost app, CancellationToken ct = default)
        {
            // A dependency scope is created to resolve services.
            using var scope = app.Services.CreateScope();

            // The application configuration is retrieved to access the connection string.
            var db = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            // The specific connection string for the event store is obtained.
            var connectionString = db.GetConnectionString("EventStore");

            // A new connection to the PostgreSQL database is established.
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            // A new transaction is started to ensure the operations are atomic.
            await using var tx = await conn.BeginTransactionAsync(ct);

            // An advisory lock is acquired to prevent race conditions during schema creation.
            await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(727271);", conn, tx))
                await lockCmd.ExecuteNonQueryAsync(ct);

            // The SQL script to create the schema and table is executed.
            await using (var cmd = new NpgsqlCommand(Sql, conn, tx))
                await cmd.ExecuteNonQueryAsync(ct);

            // The advisory lock is released after the schema setup is complete.
            await using (var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(727271);", conn, tx))
                await unlock.ExecuteNonQueryAsync(ct);

            // The transaction is committed to save the changes to the database.
            await tx.CommitAsync(ct);

        }

    }
}
