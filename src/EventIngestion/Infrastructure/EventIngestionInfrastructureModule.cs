using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.EventHandlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Event.Events;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;
using SmartSentinelEye.EventIngestion.Infrastructure.Ingress;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.ServiceDefaults.Resilience;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
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
        Ensure.That(builder).IsNotNull();

        AddPersistence(builder);
        AddRepositoriesAndQuerySources(builder.Services);
        AddCrossCuttingServices(builder.Services);
        AddDomainEventHandlers(builder.Services);
        AddCommandHandlers(builder.Services);
        AddQueryHandlers(builder.Services);
        AddIngestChannel(builder.Services);
        AddIngestTelemetryAndOptions(builder);
        AddMqttIngress(builder);

        builder.AddWolverineForContext<EventIngestionDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: EventIngestionPersistenceModule.DatabaseConnectionName,
            // ADR-0185 / spec 442: the revocation is an admin change that must outlive
            // the broker; FabEventIngestedV1 (hot path) is deliberately not listed.
            durableOutboxMessageTypes: [typeof(WebhookIntegrationRevokedV1)]);

        return builder;
    }

    private static void AddPersistence(IHostApplicationBuilder builder)
    {
        builder.AddEventIngestionPersistence();
        builder.EnrichPostgresDbContext<EventIngestionDbContext>();
    }

    private static void AddRepositoriesAndQuerySources(IServiceCollection services)
    {
        services
            .AddScoped<IEventRepository, EventRepository>()
            .AddScoped<IWebhookIntegrationRepository, WebhookIntegrationRepository>()
            .AddScoped<IDeadLetterRepository, DeadLetterRepository>()
            .AddScoped<IRegisteredEventTypeRepository, RegisteredEventTypeRepository>()
            .AddScoped<IEventQuerySource, EventQuerySource>()
            .AddScoped<IDeadLetterQuerySource, DeadLetterQuerySource>()
            .AddScoped<IWebhookIntegrationQuerySource, WebhookIntegrationQuerySource>()
            .AddScoped<IRegisteredEventTypeQuerySource, RegisteredEventTypeQuerySource>()
            .AddScoped<ISourceModeRepository, SourceModeRepository>()
            .AddScoped<ISourceModeQuerySource, SourceModeQuerySource>()
            .AddScoped<IEventTypeAdmissionSource, EventTypeAdmissionSource>()
            .AddScoped<EventTypeAdmission>();
    }

    private static void AddCrossCuttingServices(IServiceCollection services)
    {
        services
            .AddScoped<IDomainEventDispatcher, DomainEventDispatcher>()
            .AddSingleton<IClock, SystemClock>();

        // ADR-0142. Scoped alongside the DbContext it writes through.
        services
            .AddScoped<IIdempotencyStore, IdempotencyStore<EventIngestionDbContext>>()
            .AddIdempotencyReservationSweep<EventIngestionDbContext>()
            .AddSingleton(TimeProvider.System);

        // Spec 019: the write paths ask whether a fab can store anything before
        // touching the ingest channel. Registered here rather than in the
        // persistence module because MigrationRunner composes that one too and
        // has no use for it — and, registered there, it would need an IClock
        // that only the services register.
        //
        // Singleton so the provisioned-fab set is shared: read from the catalog
        // once per TTL, not once per request.
        services.AddSingleton<IFabStorageReadiness, CatalogFabStorageReadiness>();
    }

    private static void AddDomainEventHandlers(IServiceCollection services)
    {
        // Domain event handler — translates EventIngestedDomainEvent
        // into FabEventIngestedV1 on the integration bus.
        services.AddScoped<
            IDomainEventHandler<EventIngestedDomainEvent>,
            EventIngestedDomainEventHandler>();

        // Domain event handler — translates WebhookIntegrationRevokedDomainEvent
        // into WebhookIntegrationRevokedV1 on the integration bus (spec 264,
        // #2206). Without this the dispatcher resolves an empty set and the
        // revocation never reaches Identity.
        services.AddScoped<
            IDomainEventHandler<WebhookIntegrationRevokedDomainEvent>,
            WebhookIntegrationRevokedDomainEventHandler>();
    }

    private static void AddCommandHandlers(IServiceCollection services)
    {
        AddIngestAndWebhookCommandHandlers(services);
        AddEventTypeCommandHandlers(services);
        AddSourceModeCommandHandlers(services);
    }

    private static void AddIngestAndWebhookCommandHandlers(IServiceCollection services)
    {
        // Hand-rolled command handler registrations (ADR-0042 + ADR-0057).
        services
            .AddScoped<IngestEventCommandHandler>()
            .AddScoped<IngestEventBatchCommandHandler>()
            .AddScoped<
                ICommandHandler<IngestEventCommand, Result<EventIdentifier, IngestEventError>>,
                IngestEventCommandHandler>()
            .AddScoped<RegisterWebhookIntegrationCommandHandler>()
            .AddScoped<
                ICommandHandler<
                    RegisterWebhookIntegrationCommand,
                    Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError>>,
                RegisterWebhookIntegrationCommandHandler>()
            .AddScoped<RevokeWebhookIntegrationCommandHandler>()
            .AddScoped<
                ICommandHandler<
                    RevokeWebhookIntegrationCommand,
                    Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError>>,
                RevokeWebhookIntegrationCommandHandler>();
    }

    private static void AddEventTypeCommandHandlers(IServiceCollection services)
    {
        services
            .AddScoped<RegisterEventTypeCommandHandler>()
            .AddScoped<
                ICommandHandler<
                    RegisterEventTypeCommand,
                    Result<RegisteredEventTypeIdentifier, RegisterEventTypeError>>,
                RegisterEventTypeCommandHandler>()
            .AddScoped<RetireEventTypeCommandHandler>()
            .AddScoped<
                ICommandHandler<
                    RetireEventTypeCommand,
                    Result<RegisteredEventTypeIdentifier, RetireEventTypeError>>,
                RetireEventTypeCommandHandler>();
    }

    private static void AddSourceModeCommandHandlers(IServiceCollection services)
    {
        // Spec 269: registered alongside T009's /event-sources mapping, the
        // first (and only) route that reaches them — see the comment this
        // replaces, above the repository/query-source registrations.
        services
            .AddScoped<DeclareSourceModeCommandHandler>()
            .AddScoped<
                ICommandHandler<
                    DeclareSourceModeCommand,
                    Result<SourceModeIdentifier, DeclareSourceModeError>>,
                DeclareSourceModeCommandHandler>()
            .AddScoped<ChangeSourceModeCommandHandler>()
            .AddScoped<
                ICommandHandler<
                    ChangeSourceModeCommand,
                    Result<SourceModeIdentifier, ChangeSourceModeError>>,
                ChangeSourceModeCommandHandler>();

        // Spec 317 (#2325), FR-013/US4: the way back to undeclared.
        services
            .AddScoped<UndeclareSourceModeCommandHandler>()
            .AddScoped<
                ICommandHandler<
                    UndeclareSourceModeCommand,
                    Result<SourceModeIdentifier, UndeclareSourceModeError>>,
                UndeclareSourceModeCommandHandler>();
    }

    private static void AddQueryHandlers(IServiceCollection services)
    {
        // Query handlers.
        services
            .AddScoped<GetEventQueryHandler>()
            .AddScoped<ListEventsQueryHandler>()
            .AddScoped<ListDeadLettersQueryHandler>()
            .AddScoped<ListWebhookIntegrationsQueryHandler>()
            .AddScoped<ListEventTypesQueryHandler>()
            .AddScoped<ListSourceModesQueryHandler>();
    }

    private static void AddIngestChannel(IServiceCollection services)
    {
        // Bounded channel + ingress. Issue #2863: the gauge registration's
        // IDisposable is its own singleton — rather than discarded — so the
        // container disposes it when it disposes itself. That matters only
        // if this factory ever runs more than once per process (e.g. a
        // future WebApplicationFactory-style test); today's single
        // registration is kept alive for the app's lifetime either way.
        services
            .AddSingleton<BoundedIngestChannel>(_ => new BoundedIngestChannel())
            .AddSingleton<IDisposable>(
                provider => IngestChannelSaturation.RegisterGauges(provider.GetRequiredService<BoundedIngestChannel>()));

        // IIngestChannel's factory runs exactly once per app lifetime — the
        // guarantee the gauges rely on to keep reading one channel instance —
        // and forces the IDisposable singleton above to resolve, since
        // nothing else in the container depends on a bare IDisposable.
        services.AddSingleton<IIngestChannel>(provider =>
        {
            var channel = provider.GetRequiredService<BoundedIngestChannel>();
            _ = provider.GetRequiredService<IDisposable>();
            return channel;
        });

        // Spec 020: direct submissions store before answering, so they no longer
        // pass through the channel — and the 429 that used to mean "the channel
        // is full" needs something to bound. This is it.
        //
        // Spec 175: the bound is configurable, so it is read from
        // IngestWriteOptions rather than always taking the parameterless
        // constructor's default of 64.
        services.AddSingleton(provider =>
            new IngestWriteLimiter(provider.GetRequiredService<IOptions<IngestWriteOptions>>().Value.Concurrency));
    }

    private static void AddIngestTelemetryAndOptions(IHostApplicationBuilder builder)
    {
        // Spec 103 FR-004 (IngestVolume). ADR-0175 (IngestChannelSaturation).
        // A meter nobody registers records into nothing and raises no error,
        // so this line is the whole difference between an instrument and a
        // no-op. IngestVolumeRegistrationTests fails loudly if its meter
        // goes; what no unit test can see is whether the exporter then ships
        // the figure to a sink, which is spec 103 §5's job.
        //
        // Here rather than beside the other three meters in ServiceDefaults:
        // that project is referenced by every context and references none of
        // them, so it cannot see these meter names. Registering them there
        // would mean the same string in two projects, which is how a rename
        // silently unregisters a meter. ADR-0051 puts a context's own wiring in
        // the context's own DI extension anyway.
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(IngestVolume.MeterName)
                .AddMeter(IngestChannelSaturation.MeterName));

        // FR-005: how long a failing write keeps being retried before the
        // delivery is recorded and released. Bound out here rather than in the
        // loop because the right answer is how long a plant's outages last.
        builder.Services.AddOptions<IngestRetryOptions>()
            .Bind(builder.Configuration.GetSection(IngestRetryOptions.SectionName));

        // Spec 175: the direct-write concurrency bound, read by the
        // IngestWriteLimiter factory above.
        builder.Services.AddOptions<IngestWriteOptions>()
            .Bind(builder.Configuration.GetSection(IngestWriteOptions.SectionName));
    }

    private static void AddMqttIngress(IHostApplicationBuilder builder)
    {
        // Resolve the broker + Keycloak from the Aspire-injected endpoints, and
        // fail fast when they are absent. Defaulting these (it used to be
        // localhost:1883) turns a wiring gap into a subscriber that silently
        // retries a dead address forever while /health stays green.
        (var mqttHost, var mqttPort) = ResolveBroker(builder.Configuration);
        var keycloakUrl = ResolveKeycloak(builder.Configuration);

        builder.Services.AddOptions<MosquittoOptions>()
            .Bind(builder.Configuration.GetSection(MosquittoOptions.SectionName))
            .PostConfigure(opts =>
            {
                opts.Host = mqttHost;
                opts.Port = mqttPort;
                opts.KeycloakUrl = keycloakUrl;
            });
        // Singleton over a named client (#2037): the cache is only worth having if
        // the provider outlives a request, and asking the factory per mint keeps
        // handler rotation that a captive typed client would have pinned.
        // A token mint is a POST, but an idempotent one: a second token simply
        // supersedes the first. Losing its retries would make a transient
        // Keycloak blip at startup fail the broker connection outright (ADR-0143).
        builder.Services.AddHttpClient(MqttTokenProvider.HttpClientName).RetryEveryMethod();
        builder.Services
            .AddSingleton<MqttTokenProvider>()
            .AddSingleton<MosquittoConnectionFactory>()
            .AddHostedService<MqttSubscriberHostedService>()
            .AddHostedService<PersistenceLoopHostedService>();
    }

    /// <summary>
    /// Broker host + port from the Aspire mosquitto endpoint, e.g.
    /// <c>tcp://localhost:52643</c>. Throws rather than defaulting: the
    /// managed MQTT client retries a bad address indefinitely without
    /// surfacing anything, so a wrong value is far more expensive than a
    /// failed startup.
    /// </summary>
    private static (string Host, int Port) ResolveBroker(IConfiguration configuration)
    {
        var endpoint =
            configuration["services:mosquitto:mqtt:0"]
            ?? configuration[$"{MosquittoOptions.SectionName}:Endpoint"]
            ?? throw new InvalidOperationException(
                "Mosquitto endpoint not found. Looked for services:mosquitto:mqtt:0 and "
                + $"{MosquittoOptions.SectionName}:Endpoint.");

        var value = endpoint;
        var scheme = value.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            value = value[(scheme + 3)..];
        }

        var parts = value.Split(':');
        if (parts.Length != 2 || parts[0].Length == 0 || !int.TryParse(parts[1], out var port))
        {
            throw new InvalidOperationException($"Mosquitto endpoint '{endpoint}' is not host:port.");
        }

        return (parts[0], port);
    }

    /// <summary>
    /// Keycloak base URL the subscriber mints its MQTT token from. Accepts
    /// the same three keys as <c>AddBearerAuthentication</c>, so the token
    /// issuer matches the JWKS the broker plugin validates against.
    /// </summary>
    private static string ResolveKeycloak(IConfiguration configuration) =>
        configuration.GetConnectionString("keycloak")
        ?? configuration["services:keycloak:http:0"]
        ?? configuration["services:keycloak:https:0"]
        ?? throw new InvalidOperationException(
            "Keycloak base URL not found. Looked for ConnectionStrings:keycloak, "
            + "services:keycloak:http:0, services:keycloak:https:0.");
}
