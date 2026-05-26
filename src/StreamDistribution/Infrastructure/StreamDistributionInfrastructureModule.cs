using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using SmartSentinelEye.StreamDistribution.Application.EventHandlers;
using SmartSentinelEye.StreamDistribution.Application.Queries;
using SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Auth;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.HealthWatcher;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

namespace SmartSentinelEye.StreamDistribution.Infrastructure;

/// <summary>
/// Composition root for the Stream Distribution Infrastructure layer
/// (ADR-0051). Wires EF Core, the MediaMTX HTTP gateway, the Wolverine
/// outbox, the WHEP auth validator, the periodic health watcher, the
/// repositories, IClock, IEventBus, and the migrator.
/// </summary>
public static class StreamDistributionInfrastructureModule
{
    public const string ContextName = "stream-distribution";
    public const string OutboxSchema = "wolverine_stream_distribution";

    public static IHostApplicationBuilder AddStreamDistributionInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddStreamDistributionPersistence();

        BindMediaMtxOptions(builder);
        BindWhepAuthOptions(builder);

        builder.Services.AddScoped<IStreamRepository, StreamRepository>();
        builder.Services.AddScoped<IStreamQuerySource, StreamQuerySource>();
        builder.Services.AddScoped<
            IDomainEventHandler<Domain.Stream.Events.StreamProvisionedDomainEvent>,
            StreamProvisionedDomainEventHandler>();
        builder.Services.AddScoped<
            IDomainEventHandler<Domain.Stream.Events.StreamHealthChangedDomainEvent>,
            StreamHealthChangedDomainEventHandler>();
        builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();
        builder.Services.AddSingleton<IStreamWhepUrlBuilder, MediaMtxWhepUrlBuilder>();
        builder.Services.AddSingleton<IWhepAuthValidator, WhepAuthValidator>();

        // Hand-rolled command/query handler registrations (ADR-0042 + ADR-0057).
        builder.Services.AddScoped<
            ICommandHandler<ProvisionStreamCommand, Result<StreamIdentifier, ProvisionStreamError>>,
            ProvisionStreamCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<ReportStreamHealthCommand, Result<StreamState, ReportStreamHealthError>>,
            ReportStreamHealthCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<AuthorizeWhepCommand, Result<MediaMtxPath, AuthorizeWhepError>>,
            AuthorizeWhepCommandHandler>();
        // Query handlers are resolved as concrete classes by the API
        // endpoints; no IQueryHandler<,> dispatcher registration needed
        // because they're not invoked through Wolverine.
        builder.Services.AddScoped<GetStreamQueryHandler>();
        builder.Services.AddScoped<ListStreamsQueryHandler>();
        builder.Services.AddScoped<CameraRegisteredIntegrationEventHandler>();
        builder.Services.AddScoped<StreamHealthChangedDomainEventHandler>();
        builder.Services.AddScoped<StreamProvisionedDomainEventHandler>();

        // Typed HttpClient for MediaMTX with the spec's retry policy
        // (1 s → 30 s exp backoff). Microsoft.Extensions.Http.Resilience
        // applies the AddStandardResilienceHandler defaults; the budget
        // mirrors the spec's outage-retry schedule.
        builder.Services.AddHttpClient<IRtspGateway, MediaMtxRtspGateway>((sp, client) =>
        {
            MediaMtxOptions options = sp.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<MediaMtxOptions>>().Value;
            client.BaseAddress = new Uri(options.ManagementUrl);
        }).AddStandardResilienceHandler();

        builder.Services.AddHostedService<StreamHealthWatcher>();
        builder.Services.AddHostedService<MediaMtxReconciler>();

        builder.AddWolverineForContext<StreamDistributionDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: StreamDistributionPersistenceModule.DatabaseConnectionName,
            configureMore: opts =>
            {
                // The Wolverine handler discovery scan defaults to the entry
                // assembly (StreamDistribution.Api). Include the Application
                // assembly so CameraRegisteredIntegrationEventHandler and the
                // domain-event handlers are bound to the integration bus.
                opts.Discovery.IncludeAssembly(
                    typeof(CameraRegisteredIntegrationEventHandler).Assembly);
            });

        return builder;
    }

    private static void BindMediaMtxOptions(IHostApplicationBuilder builder)
    {
        // Aspire injects MediaMTX endpoints as `services:mediamtx:api:0`
        // (HTTP control plane) and `services:mediamtx:whep:0` (browser WHEP).
        // Fallback through ConnectionStrings:mediamtx for non-Aspire hosts.
        string managementUrl =
            builder.Configuration["services:mediamtx:api:0"]
            ?? builder.Configuration.GetConnectionString("mediamtx-api")
            ?? throw new InvalidOperationException(
                "MediaMTX management URL not configured (services:mediamtx:api:0).");
        string whepBaseUrl =
            builder.Configuration["services:mediamtx:whep:0"]
            ?? builder.Configuration.GetConnectionString("mediamtx-whep")
            ?? throw new InvalidOperationException(
                "MediaMTX WHEP URL not configured (services:mediamtx:whep:0).");

        builder.Services.Configure<MediaMtxOptions>(options =>
        {
            options.ManagementUrl = managementUrl;
            options.WhepBaseUrl = whepBaseUrl;
        });
    }

    private static void BindWhepAuthOptions(IHostApplicationBuilder builder)
    {
        // Derive the Keycloak authority the same way AuthenticationDefaults does.
        string keycloakBaseUrl =
            builder.Configuration.GetConnectionString("keycloak")
            ?? builder.Configuration["services:keycloak:http:0"]
            ?? builder.Configuration["services:keycloak:https:0"]
            ?? throw new InvalidOperationException(
                "Keycloak base URL not configured for WHEP auth validator.");

        string realm =
            builder.Configuration["Keycloak:Realm"] ?? "smart-sentinel-eye";

        builder.Services.Configure<WhepAuthOptions>(options =>
        {
            options.Authority = $"{keycloakBaseUrl.TrimEnd('/')}/realms/{realm}";
        });
    }
}
