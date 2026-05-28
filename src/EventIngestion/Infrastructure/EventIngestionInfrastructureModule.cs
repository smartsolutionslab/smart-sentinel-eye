using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.EventHandlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Event.Events;
using SmartSentinelEye.EventIngestion.Infrastructure.Ingress;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure;

/// <summary>
/// Composition root for the EventIngestion Infrastructure layer
/// (ADR-0051). Wires the persistence stack, the bounded channel, the
/// MQTT subscriber, the persistence loop, and the Wolverine outbox.
/// </summary>
public static class EventIngestionInfrastructureModule
{
    public const string ContextName = "event-ingestion";
    public const string OutboxSchema = "wolverine_event_ingestion";

    public static IHostApplicationBuilder AddEventIngestionInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEventIngestionPersistence();

        builder.Services.AddScoped<IEventRepository, EventRepository>();
        builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();

        // Domain event handler — translates EventIngestedDomainEvent
        // into FabEventIngestedV1 on the integration bus.
        builder.Services.AddScoped<
            IDomainEventHandler<EventIngestedDomainEvent>,
            EventIngestedDomainEventHandler>();

        // Hand-rolled command handler registrations (ADR-0042 + ADR-0057).
        builder.Services.AddScoped<IngestEventCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<IngestEventCommand, Result<EventIdentifier, IngestEventError>>,
            IngestEventCommandHandler>();

        // Bounded channel + ingress.
        builder.Services.AddSingleton<IIngestChannel>(_ => new BoundedIngestChannel());
        builder.Services.AddOptions<MosquittoOptions>()
            .Bind(builder.Configuration.GetSection(MosquittoOptions.SectionName));
        builder.Services.AddSingleton<MosquittoConnectionFactory>();
        builder.Services.AddHostedService<MqttSubscriberHostedService>();
        builder.Services.AddHostedService<PersistenceLoopHostedService>();

        builder.AddWolverineForContext<EventIngestionDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: EventIngestionPersistenceModule.DatabaseConnectionName);

        return builder;
    }
}
