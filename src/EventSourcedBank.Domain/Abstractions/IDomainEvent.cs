namespace EventSourcedBank.Domain.Abstractions
{
    public interface IDomainEvent
    {
        DateTimeOffset OccurredOn { get; }
    }
}
