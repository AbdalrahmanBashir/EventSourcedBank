namespace EventSourcedBank.Api.Dtos
{
    public sealed record AccountBalanceDto(
    Guid Id,
    string HolderName,
    string Status,
    decimal Balance,
    string Currency,
    decimal OverdraftLimit,
    decimal AvailableToWithdraw,
    int Version,
    DateTime UpdatedAt);

    public sealed record OverdrawnAccountDto(
        Guid Id,
        string HolderName,
        string Status,
        decimal Balance,
        string Currency,
        decimal OverdraftLimit,
        decimal AvailableToWithdraw,
        int Version,
        DateTime UpdatedAt,
        decimal OverdraftUsagePercent);

    public sealed record AccountsSummaryDto(
        IReadOnlyList<StatusCountDto> StatusCounts,
        IReadOnlyList<CurrencyTotalDto> TotalsByCurrency);

    public sealed record StatusCountDto(string Status, long Count);
    public sealed record CurrencyTotalDto(string Currency, decimal TotalBalance, decimal TotalAvailable);

    public sealed record AccountsListQuery(
        string? Status,
        string? Holder,
        decimal? MinBalance,
        decimal? MaxBalance,
        string? Currency,
        string? SortBy,
        string? SortDir,
        int? Limit,
        int? Offset);

    public sealed record OverdrawnQuery(decimal? MinUsagePercent, int? Limit, int? Offset);
}
