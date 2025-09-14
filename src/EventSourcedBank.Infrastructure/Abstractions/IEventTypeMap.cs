using System.Text.Json;

namespace EventSourcedBank.Infrastructure.Abstractions
{
    public interface IEventTypeMap
    {
        string GetName(Type eventType);
        Type GetType(string eventName);
        JsonSerializerOptions JsonOptions { get; }
    }
}
