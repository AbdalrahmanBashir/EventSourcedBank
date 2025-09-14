using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace EventSourcedBank.Infrastructure.Database
{
    // Provides extension methods to initialize the read model database and update the event store schema.
    public static class ReadModelInitializer
    {
        
        // Ensures that the schema and tables for the read model are created during application startup.
        public static async Task EnsureReadModelCreatedAsync(this IHost app, CancellationToken ct= default)
        {
            // A dependency scope is created to resolve services.
            using var scope = app.Services.CreateScope();
            var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            // The connection string for the read model database is retrieved from configuration.
            var cs = cfg.GetConnectionString("ReadModel")
                ?? throw new InvalidOperationException("Missing 'ReadModel' connection string.");

            // This SQL script defines the schema for query-optimized tables and projector checkpoints.
            const string sql = @"
                CREATE SCHEMA IF NOT EXISTS readmodel;

                CREATE TABLE IF NOT EXISTS readmodel.account_balance (
                    account_id UUID PRIMARY KEY,
                    holder_name TEXT NOT NULL,
                    status TEXT NOT NULL,
                    balance_amount NUMERIC(18,2) NOT NULL,
                    balance_currency TEXT NOT NULL,
                    overdraft_limit NUMERIC(18,2) NOT NULL,
                    available_to_withdraw NUMERIC(18,2) NOT NULL,
                    version INT NOT NULL,
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );

                CREATE TABLE IF NOT EXISTS readmodel.projector_checkpoints (
                    projector_name TEXT PRIMARY KEY,
                    position BIGINT NOT NULL
                );
            ";

            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync();

            // The SQL command is executed to create the necessary database objects.
            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                 await cmd.ExecuteScalarAsync();

            // The transaction is committed to save the schema changes.
            await tx.CommitAsync();

        }

        // Ensures the events table has a global position column for ordered event processing.
        public static async Task EnsureEventStoreGlobalPositionAsync(this  IHost app, CancellationToken ct= default)
        {

            using var scope = app.Services.CreateScope();
            var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            // The connection string for the event store database is retrieved.
            var cs = cfg.GetConnectionString("EventStore") 
                ?? throw new InvalidOperationException("Missing 'EventStore' connection string.");

            // This SQL script adds an auto-incrementing global position column to the events table.
            const string sql = @"
                ALTER TABLE event_store.events
                    ADD COLUMN IF NOT EXISTS global_position BIGSERIAL;

                CREATE UNIQUE INDEX IF NOT EXISTS ux_events_global_position
                    ON event_store.events (global_position);
            ";

            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // The SQL command is executed to alter the event store table.
            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                await cmd.ExecuteScalarAsync();

            // The transaction is committed to apply the table alteration.
            await tx.CommitAsync();
        }

    }
}
