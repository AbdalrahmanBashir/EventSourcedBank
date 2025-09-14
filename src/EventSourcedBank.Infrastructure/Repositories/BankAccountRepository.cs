using EventSourcedBank.Domain.BankAccount;
using EventSourcedBank.Infrastructure.Abstractions;

namespace EventSourcedBank.Infrastructure.Repositories
{
    public class BankAccountRepository : IBankAccountRepository
    {
        private readonly IEventStore _eventStore;

        public BankAccountRepository(IEventStore eventStore)
        {
            _eventStore = eventStore;
        }
        public async Task<BankAccountAggregate?> Get(Guid id, CancellationToken ct = default)
        {
            var history = await _eventStore.LoadAsync(id, ct);
            if (history.Count == 0) return null;

            return BankAccountAggregate.FromHistory(history);
        }

        public async Task Save(BankAccountAggregate aggregate, CancellationToken ct = default)
        {
            var pending = aggregate.DequeueUncommittedEvents();

            if (pending.Count == 0) return;

            var expectedVersion = aggregate.Version - pending.Count;
            await _eventStore.AppendAsync(aggregate.Id, expectedVersion, pending, ct);
        }
    }
}
