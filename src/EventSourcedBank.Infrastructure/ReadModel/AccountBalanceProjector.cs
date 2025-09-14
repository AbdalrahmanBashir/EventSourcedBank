using Dapper;
using EventSourcedBank.Domain.Abstractions;
using EventSourcedBank.Infrastructure.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using System.Reflection.Metadata;
using System.Text.Json;
using static Dapper.SqlMapper;

namespace EventSourcedBank.Infrastructure.ReadModel
{
    // A background service that builds and maintains the account balance read model.
    public sealed class AccountBalanceProjector : BackgroundService
    {
        private const string ProjectorName = "account_balance_projector_v1";
        private readonly string _writeConnectionString;
        private readonly string _readConnectionString;
        private readonly IEventTypeMap _eventTypeMap;

        // Initializes a new instance of the AccountBalanceProjector class.
        public AccountBalanceProjector(IConfiguration cfg, IEventTypeMap eventTypeMap)
        {
            _writeConnectionString = cfg.GetConnectionString("EventStore") 
                ?? throw new InvalidOperationException("Missing 'EventStore' connection string.");
            _readConnectionString = cfg.GetConnectionString("ReadModel")
                ?? throw new InvalidOperationException("Missing 'ReadModel' connection string.");
            _eventTypeMap = eventTypeMap;
        }

        // The main execution loop for the background service.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Ensures a checkpoint record exists for this projector before starting.
            await EnsureCheckpointRow(stoppingToken);

            const int batchSize = 100;

            // The projector runs in a continuous loop until the application is requested to stop.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // The position of the last processed event is retrieved from the checkpoint.
                    long lastPos = await GetCheckpoint(stoppingToken);

                    // The next batch of events is loaded from the event store.
                    var events = await LoadNextBatch(lastPos, batchSize, stoppingToken);

                    // If no new events are found, the service waits briefly before polling again.
                    if (events.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(400), stoppingToken);
                        continue;
                    }
                    // The batch of events is processed and projected into the read model.
                    var maxPos = await ProjectBatch(events, stoppingToken);

                    // The checkpoint is updated to the position of the last event in the processed batch.
                    await StoreCheckpoint(maxPos, stoppingToken);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) 
                {
                    // In case of other errors, the service waits before retrying the loop.
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
            
        }

        // Creates a checkpoint row for this projector if one does not already exist.
        private async Task EnsureCheckpointRow(CancellationToken ct)
        {
            const string sql = @"
            INSERT INTO readmodel.projector_checkpoints (projector_name, position)
            VALUES (@name, 0)
            ON CONFLICT (projector_name) DO NOTHING;";

            await using var conn = new NpgsqlConnection(_readConnectionString);
            await conn.ExecuteAsync(sql, new { name = ProjectorName });
        }

        // Retrieves the current checkpoint position from the read model database.
        private async Task<long> GetCheckpoint(CancellationToken ct)
        {
            const string sql = @"SELECT position FROM readmodel.projector_checkpoints WHERE projector_name = @name;";
            await using var conn = new NpgsqlConnection(_readConnectionString);
            return await conn.ExecuteScalarAsync<long>(sql, new { name = ProjectorName });
        }

        // Stores the latest processed event position in the checkpoint table.
        private async Task StoreCheckpoint(long pos, CancellationToken ct)
        {
            const string sql = @"
            UPDATE readmodel.projector_checkpoints
            SET position = @pos
            WHERE projector_name = @name;";
            await using var conn = new NpgsqlConnection(_readConnectionString);
            await conn.ExecuteAsync(sql, new { name = ProjectorName, pos });
        }

        // Loads the next batch of events from the event store based on the global position.
        private async Task<IReadOnlyList<(long gp, Guid streamId, int version, string type, string json)>> LoadNextBatch(long afterPosition, int batch, CancellationToken ct)
        {
            const string sql = @"
            SELECT global_position, stream_id, version, event_type, event_data::text AS event_data
            FROM event_store.events
            WHERE global_position > @p
            ORDER BY global_position ASC
            LIMIT @batch;";

            await using var conn = new NpgsqlConnection(_writeConnectionString);
            var rows = await conn.QueryAsync<(long, Guid, int, string, string)>(sql, new { p = afterPosition, batch });
            return rows.ToList();
        }

        // Processes a batch of events and applies them to the read model within a single transaction.
        private async Task<long> ProjectBatch(IReadOnlyList<(long gp, Guid streamId, int version, string type, string json)> batch, CancellationToken ct)
        {
            long maxPos = 0;

            await using var conn = new NpgsqlConnection(_readConnectionString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                foreach (var (gp, streamId, version, typeName, json) in batch)
                {
                    maxPos = gp;

                    var clrType = _eventTypeMap.GetType(typeName);
                    var evt = (IDomainEvent?)JsonSerializer.Deserialize(json, clrType, _eventTypeMap.JsonOptions);
                    if (evt is null) throw new InvalidOperationException($"Failed to deserialize {typeName}");

                    // Dispatch by name for speed (avoid many 'is' checks)
                    switch (typeName)
                    {
                        case nameof(BankAccountOpened):
                            await Apply_BankAccountOpened(conn, tx, streamId, version, json, ct);
                            break;

                        case nameof(MoneyDeposited):
                            await Apply_MoneyDeposited(conn, tx, streamId, version, json, ct);
                            break;

                        case nameof(MoneyWithdrawn):
                            await Apply_MoneyWithdrawn(conn, tx, streamId, version, json, ct);
                            break;

                        case nameof(AccountFrozen):
                            await Apply_Status(conn, tx, streamId, version, "Frozen", ct);
                            break;

                        case nameof(AccountUnfrozen):
                            await Apply_Status(conn, tx, streamId, version, "Open", ct);
                            break;

                        case nameof(AccountClosed):
                            await Apply_Status(conn, tx, streamId, version, "Closed", ct);
                            break;

                        case nameof(OverdraftLimitChanged):
                            await Apply_OverdraftChanged(conn, tx, streamId, version, json, ct);
                            break;

                        case nameof(AccountHolderNameChanged):
                            await Apply_HolderNameChanged(conn, tx, streamId, version, json, ct);
                            break;

                        case nameof(FeeApplied):
                            await Apply_FeeApplied(conn, tx, streamId, version, json, ct);
                            break;

                        default:
                            // ignore unknown event types (or log)
                            break;
                    }
                }

                await tx.CommitAsync(ct);
                return maxPos;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        // Handles the BankAccountOpened event by creating a new record in the read model.
        private static async Task Apply_BankAccountOpened(NpgsqlConnection conn, NpgsqlTransaction tx, Guid accountId, int version, string json, CancellationToken ct)
        {
            // Extract fields from JSON in SQL avoids a second CLR parse
            const string sql = @"
                INSERT INTO readmodel.account_balance (account_id, holder_name, status, balance_amount, balance_currency, overdraft_limit, available_to_withdraw, version)
                SELECT
                    @accountId,
                    (jsonb_extract_path_text(@json::jsonb, 'accountHolder'))::text,
                    'Open',
                    (jsonb_extract_path_text(@json::jsonb, 'initialBalance','amount'))::numeric,
                    (jsonb_extract_path_text(@json::jsonb, 'initialBalance','currency'))::text,
                    (jsonb_extract_path_text(@json::jsonb, 'overdraftLimit'))::numeric,
                    ((jsonb_extract_path_text(@json::jsonb, 'initialBalance','amount'))::numeric
                        + (jsonb_extract_path_text(@json::jsonb, 'overdraftLimit'))::numeric),
                    @version
                ON CONFLICT (account_id) DO UPDATE
                    SET holder_name = EXCLUDED.holder_name,
                        status = EXCLUDED.status,
                        balance_amount = EXCLUDED.balance_amount,
                        balance_currency = EXCLUDED.balance_currency,
                        overdraft_limit = EXCLUDED.overdraft_limit,
                        available_to_withdraw = EXCLUDED.available_to_withdraw,
                        version = GREATEST(readmodel.account_balance.version, EXCLUDED.version),
                        updated_at = now()
                WHERE readmodel.account_balance.version < EXCLUDED.version;"
            ;

            await conn.ExecuteAsync(sql, new { accountId, json, version }, tx);
        }

        // Handles the MoneyDeposited event by updating the account balance.
        private static async Task Apply_MoneyDeposited(NpgsqlConnection conn, NpgsqlTransaction tx, Guid accountId, int version, string json, CancellationToken ct)
        {
            const string sql = @"
                UPDATE readmodel.account_balance
                SET
                    balance_amount = balance_amount + (jsonb_extract_path_text(@json::jsonb, 'amount','amount'))::numeric,
                    available_to_withdraw = (balance_amount + (jsonb_extract_path_text(@json::jsonb, 'amount','amount'))::numeric) + overdraft_limit,
                    version = @version,
                    updated_at = now()
                WHERE account_id = @accountId AND version < @version;"
            ;

            await conn.ExecuteAsync(sql, new { accountId, json, version }, tx);
        }

        // Handles the MoneyWithdrawn event by updating the account balance.
        private static async Task Apply_MoneyWithdrawn(NpgsqlConnection conn, NpgsqlTransaction tx, Guid accountId, int version, string json, CancellationToken ct)
        {
            const string sql = @"
                UPDATE readmodel.account_balance
                SET
                    balance_amount = balance_amount - (jsonb_extract_path_text(@json::jsonb, 'amount','amount'))::numeric,
                    available_to_withdraw = (balance_amount - (jsonb_extract_path_text(@json::jsonb, 'amount','amount'))::numeric) + overdraft_limit,
                    version = @version,
                    updated_at = now()
                WHERE account_id = @accountId AND version < @version;"
            ;

            await conn.ExecuteAsync(sql, new { accountId, json, version }, tx);
        }

        // Handles the FeeApplied event by updating the account balance.
        private static async Task Apply_FeeApplied(NpgsqlConnection conn, NpgsqlTransaction tx, Guid accountId, int version, string json, CancellationToken ct)
        {
            const string sql = @"
                UPDATE readmodel.account_balance
                SET
                    balance_amount = balance_amount - (jsonb_extract_path_text(@json::jsonb, 'feeAmount','amount'))::numeric,
                    available_to_withdraw = (balance_amount - (jsonb_extract_path_text(@json::jsonb, 'feeAmount','amount'))::numeric) + overdraft_limit,
                    version = @version,
                    updated_at = now()
                WHERE account_id = @accountId AND version < @version;"
            ;

            await conn.ExecuteAsync(sql, new { accountId, json, version }, tx);
        }

        // Handles events that change the account's status.
        private static async Task Apply_Status(NpgsqlConnection conn, NpgsqlTransaction tx, Guid accountId, int version, string newStatus, CancellationToken ct)
        {
            const string sql = @"
                UPDATE readmodel.account_balance
                SET status = @newStatus, version = @version, updated_at = now()
                WHERE account_id = @accountId AND version < @version;"
            ;

            await conn.ExecuteAsync(sql, new { accountId, newStatus, version }, tx);
        }

        // Handles the OverdraftLimitChanged event.
        private static async Task Apply_OverdraftChanged(NpgsqlConnection conn, NpgsqlTransaction tx, Guid accountId, int version, string json, CancellationToken ct)
        {
            const string sql = @"
                UPDATE readmodel.account_balance
                SET overdraft_limit = (jsonb_extract_path_text(@json::jsonb, 'newOverdraftLimit'))::numeric,
                    available_to_withdraw = balance_amount + (jsonb_extract_path_text(@json::jsonb, 'newOverdraftLimit'))::numeric,
                    version = @version,
                    updated_at = now()
                WHERE account_id = @accountId AND version < @version;"
            ;

            await conn.ExecuteAsync(sql, new { accountId, json, version }, tx);
        }

        // Handles the AccountHolderNameChanged event.
        private static async Task Apply_HolderNameChanged(NpgsqlConnection conn, NpgsqlTransaction tx, Guid accountId, int version, string json, CancellationToken ct)
        {
            const string sql = @"
                UPDATE readmodel.account_balance
                SET holder_name = (jsonb_extract_path_text(@json::jsonb, 'newAccountHolderName'))::text,
                    version = @version,
                    updated_at = now()
                WHERE account_id = @accountId AND version < @version;"
            ;

            await conn.ExecuteAsync(sql, new { accountId, json, version }, tx);
        }

    }
}
