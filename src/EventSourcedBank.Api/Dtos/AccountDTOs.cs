using System.ComponentModel.DataAnnotations;

namespace EventSourcedBank.Api.Dtos
{
    public record AccountDto(
    Guid Id,
    string HolderName,
    string Status,
    decimal Balance,
    string Currency,
    decimal OverdraftLimit,
    decimal AvailableToWithdraw,
    int Version);

    public record OpenAccountRequest(
    [param: Required] string HolderName,
    [param: Range(typeof(decimal), "0", "79228162514264337593543950335")] decimal OverdraftLimit,
    [param: Required] string Currency,
    [param: Range(typeof(decimal), "0", "79228162514264337593543950335")] decimal InitialBalance);

    public record MoneyChangeRequest(
        [param: Range(typeof(decimal), "0.01", "79228162514264337593543950335")] decimal Amount,
        [param: Required] string Currency);

    public record ChangeOverdraftLimitRequest(
        [param: Range(typeof(decimal), "0", "79228162514264337593543950335")] decimal NewLimit);

    public record ChangeHolderNameRequest(
        [param: Required] string NewName);

    public record ApplyFeeRequest(
        [param: Range(typeof(decimal), "0.01", "79228162514264337593543950335")] decimal Amount,
        [param: Required] string Currency,
        [param: Required] string Reason);
}
