using EventSourcedBank.Domain.Abstractions;
using EventSourcedBank.Infrastructure.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventSourcedBank.Infrastructure.Serialization
{
    public class EventTypeMap : IEventTypeMap
    {
        private readonly Dictionary<string, Type> _nameToType;
        private readonly Dictionary<Type, string> _typeToName;
        public EventTypeMap()
        {
            var types = new[]
            {
                // Register all event types
                typeof(BankAccountOpened),
                typeof(MoneyDeposited),
                typeof(MoneyWithdrawn),
                typeof(AccountFrozen),
                typeof(AccountUnfrozen),
                typeof(AccountClosed),
                typeof(OverdraftLimitChanged),
                typeof(AccountHolderNameChanged),
                typeof(FeeApplied)
            };

            _nameToType = types.ToDictionary(t => t.Name, t => t);
            _typeToName = types.ToDictionary(t => t, t => t.Name);
            JsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,   // write camelCase
                PropertyNameCaseInsensitive = true,                  // read Pascal/camel
                WriteIndented = false,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        
        public JsonSerializerOptions JsonOptions { get; }

        public string GetName(Type eventType)
        {
            if (_typeToName.TryGetValue(eventType, out var name))
                return name;
            throw new ArgumentException($"Event type '{eventType.FullName}' is not registered in the event type map.");
        }

        public Type GetType(string eventName)
        {
            if (_nameToType.TryGetValue(eventName, out var type))
                return type;
            throw new ArgumentException($"Event name '{eventName}' is not registered in the event type map.");
        }

        
    }
}
