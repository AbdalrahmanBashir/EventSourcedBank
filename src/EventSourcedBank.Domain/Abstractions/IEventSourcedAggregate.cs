namespace EventSourcedBank.Domain.Abstractions
{
    public interface IEventSourcedAggregate
    {
        Guid Id { get; }
        int Version { get; }
        IReadOnlyCollection<IDomainEvent> DequeueUncommittedEvents();
    }
}
