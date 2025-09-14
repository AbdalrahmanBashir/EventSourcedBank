using EventSourcedBank.Domain.Abstractions;

namespace EventSourcedBank.Infrastructure.Abstractions
{
    public interface IEventStore
    {
        Task<IReadOnlyList<IDomainEvent>> LoadAsync(Guid streamId, CancellationToken ct = default);
        Task AppendAsync(Guid streamId, int expectedVersion, IEnumerable<IDomainEvent> events, CancellationToken ct = default);
    }
}
