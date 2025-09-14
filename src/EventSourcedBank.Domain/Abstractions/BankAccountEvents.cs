using EventSourcedBank.Domain.ValueObjects;

namespace EventSourcedBank.Domain.Abstractions
{
   
    public enum AccountStatus{ New = 0, Open = 1, Closed = 2, Frozen = 3 }

    public record BankAccountOpened(Guid AccountId, string AccountHolder, decimal OverdraftLimit, Money InitialBalance, DateTimeOffset OccurredOn) : IDomainEvent;
    public record MoneyDeposited(Guid AccountId, Money Amount, DateTimeOffset OccurredOn) : IDomainEvent;

    public record MoneyWithdrawn(Guid AccountId, Money Amount, DateTimeOffset OccurredOn) : IDomainEvent;

    public record AccountFrozen(Guid AccountId, DateTimeOffset OccurredOn) : IDomainEvent;

    public record AccountUnfrozen(Guid AccountId, DateTimeOffset OccurredOn) : IDomainEvent;

    public record AccountClosed(Guid AccountId, DateTimeOffset OccurredOn) : IDomainEvent;

    public record OverdraftLimitChanged(Guid AccountId, decimal NewOverdraftLimit, DateTimeOffset OccurredOn) : IDomainEvent;

    public record AccountHolderNameChanged(Guid AccountId, string NewAccountHolderName, DateTimeOffset OccurredOn) : IDomainEvent;

    public record FeeApplied(Guid AccountId, Money FeeAmount, string Reason, DateTimeOffset OccurredOn) : IDomainEvent;

}
