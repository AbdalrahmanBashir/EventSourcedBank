using EventSourcedBank.Infrastructure.Abstractions;
using EventSourcedBank.Infrastructure.EventStore;
using EventSourcedBank.Infrastructure.ReadModel;
using EventSourcedBank.Infrastructure.Repositories;
using EventSourcedBank.Infrastructure.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.ComponentModel;
using System.Reflection.Emit;

namespace EventSourcedBank.Infrastructure.Extensions
{
    // Provides an extension method for IServiceCollection to register infrastructure-layer services.
    public static class ServiceCollectionExtensions
    {
        // Adds all necessary infrastructure services to the dependency injection container.
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
        {
            // The connection string for the event store is retrieved from the application configuration.
            var connectionString = config.GetConnectionString("EventStore")
                ?? throw new InvalidOperationException("Missing 'EventStore' connection string.");

            // Registers the EventTypeMap as a singleton to manage event type-to-name mappings.
            services.AddSingleton<IEventTypeMap, EventTypeMap>();

            // Registers the PostgresEventStore as a singleton, providing it with the connection string and event type map.
            services.AddSingleton<IEventStore>(sp =>
               new PostgresEventStore(connectionString, sp.GetRequiredService<IEventTypeMap>()));

            // Registers the AccountBalanceProjector as a hosted service to run in the background.
            services.AddHostedService<AccountBalanceProjector>();

            // Registers the BankAccountRepository with a scoped lifetime for handling aggregate persistence.
            services.AddScoped<IBankAccountRepository, BankAccountRepository>();

            return services;
        }
    }
}
