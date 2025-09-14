using EventSourcedBank.Domain.BankAccount;

namespace EventSourcedBank.Infrastructure.Abstractions
{
    public interface IBankAccountRepository
    {
        Task<BankAccountAggregate?> Get(Guid id, CancellationToken ct = default);
        Task Save(BankAccountAggregate aggregate, CancellationToken ct = default);
    }
}
