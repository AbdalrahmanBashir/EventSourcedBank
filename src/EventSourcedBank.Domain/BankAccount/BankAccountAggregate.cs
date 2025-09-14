using EventSourcedBank.Domain.Abstractions;
using EventSourcedBank.Domain.ValueObjects;

namespace EventSourcedBank.Domain.BankAccount
{
    // Represents the aggregate root for a bank account, handling all state changes and business logic.
    public class BankAccountAggregate : IEventSourcedAggregate
    {
        // Holds a list of domain events that have not yet been committed to the event store.
        private readonly List<IDomainEvent> _uncommitted = new();
        public Guid Id { get; private set; } = Guid.Empty;  // Gets the unique identifier of the bank account.
        public string HolderName { get; private set; } = default!; // Gets the name of the account holder.
        public AccountStatus Status { get; private set; } = AccountStatus.New;  // Gets the current status of the account.
        public Money Balance { get; private set; } = default!;  // Gets the current balance of the account.
        public decimal OverdraftLimit { get; private set; }  // Gets the configured overdraft limit for the account.
        // Gets the current version of the aggregate, representing the number of applied events.
        public int Version { get; private set; } = -1;

        // Creates a new bank account aggregate instance.
        public static BankAccountAggregate Open(
            Guid accountId,
            string accountHolder,
            decimal overdraftLimit,
            Money initialBalance,
            DateTimeOffset? occurredOn = null)
        {
            if (accountId == Guid.Empty) throw new ArgumentException("Account id cannot be empty.", nameof(accountId));
            if (string.IsNullOrWhiteSpace(accountHolder)) throw new ArgumentException("Account holder is required.", nameof(accountHolder));
            if (overdraftLimit < 0) throw new ArgumentOutOfRangeException(nameof(overdraftLimit), "Overdraft limit cannot be negative.");
            if (initialBalance.Amount < 0) throw new InvalidOperationException("Initial balance cannot be negative.");

            var agg = new BankAccountAggregate();
            agg.Raise(new BankAccountOpened(accountId, accountHolder, overdraftLimit, initialBalance, occurredOn ?? DateTimeOffset.UtcNow));
            return agg;
        }

        // Reconstructs the aggregate state from a sequence of historical domain events.
        public static BankAccountAggregate FromHistory(IEnumerable<IDomainEvent> history)
        {
            var agg = new BankAccountAggregate();
            agg.Replay(history);
            return agg;
        }

        // Replays a stream of events to rebuild the aggregate's current state.
        private void Replay(IEnumerable<IDomainEvent> history)
        {
            foreach (var e in history)
                Apply(e);
        }

        // Commands behavior

        // Deposits a specified amount of money into the account.
        public void Deposit(Money amount, DateTimeOffset? occurredOn = null)
        {
            EnsureOpenedOrFrozen();  // deposits allowed when Frozen
            if (amount.Amount <= 0) throw new InvalidOperationException("Deposit amount must be positive.");
            EnsureCurrencyMatches(amount);

            Raise(new MoneyDeposited(Id, amount, occurredOn ?? DateTimeOffset.UtcNow));
        }

        // Withdraws a specified amount of money from the account if funds are sufficient.
        public void Withdraw(Money amount, DateTimeOffset? occurredOn = null)
        {
            EnsureOpened(); // withdrawals are blocked when Frozen
            if (amount.Amount <= 0) throw new InvalidOperationException("Withdrawal amount must be positive.");
            EnsureCurrencyMatches(amount);

            if (Balance.Amount + OverdraftLimit < amount.Amount)
                throw new InvalidOperationException("Insufficient funds including overdraft limit.");

            Raise(new MoneyWithdrawn(Id, amount, occurredOn ?? DateTimeOffset.UtcNow));
        }

        // Freezes the account, preventing most operations except deposits.
        public void Freeze(DateTimeOffset? occurredOn = null)
        {
            EnsureOpened();
            Raise(new AccountFrozen(Id, occurredOn ?? DateTimeOffset.UtcNow));
        }

        // Unfreezes a frozen account, restoring it to an open status.
        public void Unfreeze(DateTimeOffset? occurredOn = null)
        {
            if (Status != AccountStatus.Frozen) throw new InvalidOperationException("Account is not frozen.");
            Raise(new AccountUnfrozen(Id, occurredOn ?? DateTimeOffset.UtcNow));
        }

        // Closes the account permanently, provided the balance is zero.
        public void Close(DateTimeOffset? occurredOn = null)
        {
            if (Status == AccountStatus.Closed) return;
            if (Status == AccountStatus.Frozen) throw new InvalidOperationException("Unfreeze the account before closing.");
            EnsureOpened();
            if (Balance.Amount != 0m) throw new InvalidOperationException("Balance must be zero to close account.");

            Raise(new AccountClosed(Id, occurredOn ?? DateTimeOffset.UtcNow));
        }

        // Changes the overdraft limit for the account.
        public void ChangeOverdraftLimit(decimal newLimit, DateTimeOffset? occurredOn = null)
        {
            EnsureOpened();  // limit changes blocked when Frozen.
            if (newLimit < 0) throw new ArgumentOutOfRangeException(nameof(newLimit), "Overdraft limit cannot be negative.");

            // Don’t allow setting a limit lower than current overdraft usage.
            if (Balance.Amount < 0 && newLimit < Math.Abs(Balance.Amount))
                throw new InvalidOperationException("New limit is lower than current overdraft usage.");

            if (newLimit == OverdraftLimit) return;

            Raise(new OverdraftLimitChanged(Id, newLimit, occurredOn ?? DateTimeOffset.UtcNow));
        }

        // Changes the name of the account holder.
        public void ChangeAccountHolderName(string newName, DateTimeOffset? occurredOn = null)
        {
            if (Status == AccountStatus.Closed) throw new InvalidOperationException("Cannot change a closed account.");
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("New holder name is required.", nameof(newName));
            if (newName == HolderName) return;

            Raise(new AccountHolderNameChanged(Id, newName, occurredOn ?? DateTimeOffset.UtcNow));
        }

        //  Applies a fee to the account, deducting from the balance.
        public void ApplyFee(Money fee, string reason, DateTimeOffset? occurredOn = null)
        {
            if (Status == AccountStatus.Closed) throw new InvalidOperationException("Cannot apply fees to a closed account.");
            if (fee.Amount <= 0) throw new InvalidOperationException("Fee must be positive.");
            EnsureCurrencyMatches(fee);

            Raise(new FeeApplied(Id, fee, reason, occurredOn ?? DateTimeOffset.UtcNow));
        }

        // Applies an event to the aggregate and adds it to the list of uncommitted events.
        private void Raise(IDomainEvent e)
        {
            Apply(e);
            _uncommitted.Add(e);
        }


        // Returns all uncommitted events and clears the internal list.
        public IReadOnlyCollection<IDomainEvent> DequeueUncommittedEvents()
        {
            var copy = _uncommitted.ToArray();
            _uncommitted.Clear();
            return copy;
        }

        // Routes a domain event to the appropriate state transition method.
        private void Apply(IDomainEvent @event)
        {
            switch (@event)
            {
                case BankAccountOpened e: When(e); break;
                case MoneyDeposited e: When(e); break;
                case MoneyWithdrawn e: When(e); break;
                case AccountFrozen e: When(e); break;
                case AccountUnfrozen e: When(e); break;
                case AccountClosed e: When(e); break;
                case OverdraftLimitChanged e: When(e); break;
                case AccountHolderNameChanged e: When(e); break;
                case FeeApplied e: When(e); break;
                default:
                    throw new ArgumentOutOfRangeException($"Unknown event type {@event.GetType().Name}");
            }
            Version++;
        }

        // Applies the state changes for a new bank account opening.
        private void When(BankAccountOpened e)
        {
            Id = e.AccountId;
            HolderName = e.AccountHolder;
            OverdraftLimit = e.OverdraftLimit;
            Balance = e.InitialBalance;
            Status = AccountStatus.Open;
        }

        // Applies the state change for a money deposit by increasing the balance.
        private void When(MoneyDeposited e) => Balance = Balance.Add(e.Amount);

        // // Applies the state change for a money withdrawal by decreasing the balance.
        private void When(MoneyWithdrawn e) => Balance = Balance.Subtract(e.Amount);

        // Applies the state change for an account freeze by updating the status.
        private void When(AccountFrozen _) => Status = AccountStatus.Frozen;

        // Applies the state change for an account unfreeze by updating the status.
        private void When(AccountUnfrozen _) => Status = AccountStatus.Open;

        // Applies the state change for an account closure by updating the status.
        private void When(AccountClosed _) => Status = AccountStatus.Closed;

        // Applies the state change for an overdraft limit update.
        private void When(OverdraftLimitChanged e) => OverdraftLimit = e.NewOverdraftLimit;

        // Applies the state change for an account holder name update.
        private void When(AccountHolderNameChanged e) => HolderName = e.NewAccountHolderName;

        // Applies the state change for a fee application by decreasing the balance.
        private void When(FeeApplied e) => Balance = Balance.Subtract(e.FeeAmount);

        // Ensures the account is in an 'Open' state before proceeding with an operation.
        private void EnsureOpened()
        {
            if (Status != AccountStatus.Open)
                throw new InvalidOperationException("Account must be open for this operation.");
        }

        // Ensures the account is either 'Open' or 'Frozen' before proceeding.
        private void EnsureOpenedOrFrozen()
        {
            if (Status != AccountStatus.Open && Status != AccountStatus.Frozen)
                throw new InvalidOperationException("Account must be open or frozen for this operation.");
        }

        // Ensures that the currency of a transaction matches the account's currency.
        private void EnsureCurrencyMatches(Money money)
        {
            // After opening, Balance is initialized, so we can compare currencies
            if (Status != AccountStatus.New && money.Currency != Balance.Currency)
                throw new InvalidOperationException("Currency mismatch.");
        }

        // Calculates the total funds available for withdrawal, including the overdraft limit.
        public decimal AvailableToWithdraw() => Balance.Amount + OverdraftLimit;
    }
}
