using Dapper;
using EventSourcedBank.Api.Dtos;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace EventSourcedBank.Api.Controllers
{
    // Exposes endpoints for querying the account information read model.
    [ApiController]
    [Route("api/read/accounts")]
    public sealed class AccountsReadController : ControllerBase
    {
        private readonly string _readConnString;

        // Initializes a new instance of the AccountsReadController class.
        public AccountsReadController(IConfiguration configuration)
        {
            _readConnString = configuration.GetConnectionString("ReadModel")!;
        }

        // Retrieves the projected state of a single bank account by its unique identifier.
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(AccountBalanceDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(Guid id)
        {
            const string sql = @"
                SELECT account_id            AS Id,
                       holder_name           AS HolderName,
                       status                AS Status,
                       balance_amount        AS Balance,
                       balance_currency      AS Currency,
                       overdraft_limit       AS OverdraftLimit,
                       available_to_withdraw AS AvailableToWithdraw,
                       version               AS Version,
                       updated_at            AS UpdatedAt
                FROM readmodel.account_balance
                WHERE account_id = @id;"
            ;

            await using var conn = new NpgsqlConnection(_readConnString);
            var dto = await conn.QuerySingleOrDefaultAsync<AccountBalanceDto>(sql, new { id });
            return dto is null ? NotFound() : Ok(dto);
        }

        // Retrieves a paginated and filterable list of all bank accounts.
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<AccountBalanceDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] AccountsListQuery query)
        {
            // Whitelist sort columns to prevent injection
            var sortColumn = query.SortBy?.ToLowerInvariant() switch
            {
                "updated" => "updated_at",
                "balance" => "balance_amount",
                "available" => "available_to_withdraw",
                "overdraft" => "overdraft_limit",
                "holder" => "holder_name",
                "status" => "status",
                _ => "updated_at"
            };
            var sortDir = (query.SortDir?.ToLowerInvariant() == "asc") ? "ASC" : "DESC";

            var sql = $@"
                    SELECT account_id            AS Id,
                           holder_name           AS HolderName,
                           status                AS Status,
                           balance_amount        AS Balance,
                           balance_currency      AS Currency,
                           overdraft_limit       AS OverdraftLimit,
                           available_to_withdraw AS AvailableToWithdraw,
                           version               AS Version,
                           updated_at            AS UpdatedAt
                    FROM readmodel.account_balance
                    WHERE (@status IS NULL OR status = @status)
                      AND (@holder IS NULL OR holder_name ILIKE '%' || @holder || '%')
                      AND (@minBalance IS NULL OR balance_amount >= @minBalance)
                      AND (@maxBalance IS NULL OR balance_amount <= @maxBalance)
                      AND (@currency IS NULL OR balance_currency = @currency)
                    ORDER BY {sortColumn} {sortDir}, account_id
                    LIMIT @limit OFFSET @offset;"
            ;

            var args = new
            {
                status = query.Status,
                holder = query.Holder,
                minBalance = query.MinBalance,
                maxBalance = query.MaxBalance,
                currency = query.Currency,
                limit = Math.Clamp(query.Limit ?? 50, 1, 500),
                offset = Math.Max(query.Offset ?? 0, 0)
            };

            await using var conn = new NpgsqlConnection(_readConnString);
            var rows = await conn.QueryAsync<AccountBalanceDto>(sql, args);
            return Ok(rows);
        }

        // Retrieves a ranked list of overdrawn accounts, ordered by overdraft usage.
        [HttpGet("overdrawn")]
        [ProducesResponseType(typeof(IEnumerable<OverdrawnAccountDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOverdrawn([FromQuery] OverdrawnQuery q)
        {
            const string sql = @"
                SELECT
                    account_id            AS Id,
                    holder_name           AS HolderName,
                    status                AS Status,
                    balance_amount        AS Balance,
                    balance_currency      AS Currency,
                    overdraft_limit       AS OverdraftLimit,
                    available_to_withdraw AS AvailableToWithdraw,
                    version               AS Version,
                    updated_at            AS UpdatedAt,
                    CASE
                        WHEN balance_amount < 0 AND overdraft_limit > 0
                            THEN ROUND((ABS(balance_amount) / overdraft_limit) * 100, 2)
                        WHEN balance_amount < 0 AND overdraft_limit = 0
                            THEN 100.00
                        ELSE 0.00
                    END AS OverdraftUsagePercent
                FROM readmodel.account_balance
                WHERE balance_amount < 0
                  AND (
                        @minUsagePercent IS NULL
                        OR (
                             CASE
                               WHEN overdraft_limit > 0 THEN (ABS(balance_amount) / overdraft_limit) * 100
                               WHEN balance_amount < 0 AND overdraft_limit = 0 THEN 100
                               ELSE 0
                             END
                           ) >= @minUsagePercent
                      )
                ORDER BY OverdraftUsagePercent DESC, updated_at DESC
                LIMIT @limit OFFSET @offset;"
            ;

            var args = new
            {
                minUsagePercent = q.MinUsagePercent,
                limit = Math.Clamp(q.Limit ?? 50, 1, 500),
                offset = Math.Max(q.Offset ?? 0, 0)
            };

            await using var conn = new NpgsqlConnection(_readConnString);
            var rows = await conn.QueryAsync<OverdrawnAccountDto>(sql, args);
            return Ok(rows);
        }

        // Retrieves a summary of all accounts, including status counts and financial totals by currency.
        [HttpGet("summary")]
        [ProducesResponseType(typeof(AccountsSummaryDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSummary()
        {
            const string sql = @"
                -- counts by status
                SELECT status, COUNT(*) AS Count
                FROM readmodel.account_balance
                GROUP BY status;

                -- totals by currency (note: cross-currency totals are not meaningful)
                SELECT balance_currency AS Currency,
                       SUM(balance_amount)        AS TotalBalance,
                       SUM(available_to_withdraw) AS TotalAvailable
                FROM readmodel.account_balance
                GROUP BY balance_currency;"
            ;

            await using var conn = new NpgsqlConnection(_readConnString);
            using var grid = await conn.QueryMultipleAsync(sql);

            var statusCounts = (await grid.ReadAsync<StatusCountDto>()).ToList();
            var currencyTotals = (await grid.ReadAsync<CurrencyTotalDto>()).ToList();

            return Ok(new AccountsSummaryDto(statusCounts, currencyTotals));
        }

    }
}
