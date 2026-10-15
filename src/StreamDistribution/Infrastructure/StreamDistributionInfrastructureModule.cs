using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Telemetry;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.ServiceDefaults.Resilience;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.EventHandlers;
using SmartSentinelEye.StreamDistribution.Application.Queries;
using SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Attribution;
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
        Ensure.That(builder).IsNotNull();

        builder.AddStreamDistributionPersistence();
        builder.EnrichPostgresDbContext<StreamDistributionDbContext>();

        BindMediaMtxOptions(builder);
        BindMediaMtxApiOptions(builder);
        BindWhepAuthOptions(builder);
        BindStreamFabAttribution(builder);

        AddStreamPersistenceAndDomainEventDispatch(builder.Services);
        AddClockAndMediaMtxAccessServices(builder.Services);
        AddCommandHandlers(builder.Services);
        AddQueryIntegrationEventAndDomainEventHandlers(builder.Services);

        builder.Services.AddMediaMtxGateway();

        AddStreamHostedServices(builder.Services);

        builder.AddWolverineForContext<StreamDistributionDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: StreamDistributionPersistenceModule.DatabaseConnectionName);

        return builder;
    }

    internal static IHttpClientBuilder AddMediaMtxGateway(this IServiceCollection services)
    {
        // Typed HttpClient for MediaMTX. The retry schedule comes from
        // ServiceDefaults, which applies AddStandardResilienceHandler to every
        // client through ConfigureHttpClientDefaults — so asking for it again
        // here does not reinforce it, it nests a second pipeline inside the
        // first. That is what this call site used to do: four attempts became
        // sixteen, and the two-second health sweep inherited both budgets.
        var builder = services.AddHttpClient<IRtspGateway, MediaMtxRtspGateway>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediaMtxOptions>>().Value;
            client.BaseAddress = new Uri(options.ManagementUrl);

            // ADR-0171: mediamtx.yml no longer excludes the `api` action from
            // the authorize hook, so every one of this client's own calls into
            // MediaMTX's control API now triggers a callback to this same
            // service's /streams/authorize. MediaMTX forwards whatever Basic
            // auth this client presents as the hook's user and password. The
            // username carries no security meaning for a shared-secret scheme,
            // so it is a fixed, readable label.
            var apiSecret = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediaMtxApiOptions>>().Value.Secret;
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"stream-distribution:{apiSecret}")));
        })
            // Its two non-idempotent methods are idempotent in fact (ADR-0143).
            // add/ is idempotent in fact (spec 309 FR-002): an existing path is
            // treated as success by the gateway, so a retry after a lost
            // response lands in the same place; patch/ sets the source to a
            // fixed value, so applying it twice lands in the same place too.
            // Without this a MediaMTX blip during provisioning would leave a
            // stream unprovisioned on the first attempt and never try again,
            // which the two-second health sweep would then report as a broken
            // camera.
            .RetryEveryMethod();

        DemoteRoutineMediaMtxProbeLogs(services);

        return builder;
    }

    private static void DemoteRoutineMediaMtxProbeLogs(IServiceCollection services)
    {
        // Keyed on the request's target host, not the pipeline name: every
        // StreamDistribution client shares one literal "{name}-standard"
        // pipeline name (ConfigureHttpClientDefaults wires it that way), so a
        // pipeline-name key would hit every client in the process rather than
        // just this one. TelemetryOptions is one shared instance per
        // container, so the provider composes with whatever was already
        // configured instead of replacing it.
        services.AddOptions<TelemetryOptions>()
            .Configure<Microsoft.Extensions.Options.IOptions<MediaMtxOptions>>((options, mediaMtxOptions) =>
            {
                var previousProvider = options.SeverityProvider;
                var mediaMtxHost = new Uri(mediaMtxOptions.Value.ManagementUrl).Host;

                options.SeverityProvider = arguments =>
                {
                    var isRoutineMediaMtxProbe =
                        arguments.Event.EventName == "ExecutionAttempt"
                        && arguments.Event.Severity == ResilienceEventSeverity.Information
                        && string.Equals(
                            arguments.Context.GetRequestMessage()?.RequestUri?.Host,
                            mediaMtxHost,
                            StringComparison.OrdinalIgnoreCase);

                    if (isRoutineMediaMtxProbe)
                    {
                        return ResilienceEventSeverity.Debug;
                    }

                    return previousProvider?.Invoke(arguments) ?? arguments.Event.Severity;
                };
            });
    }

    private static void AddStreamPersistenceAndDomainEventDispatch(IServiceCollection services)
    {
        services
            .AddScoped<IStreamRepository, StreamRepository>()
            .AddScoped<IStreamQuerySource, StreamQuerySource>()
            .AddScoped<
                IDomainEventHandler<Domain.Stream.Events.StreamProvisionedDomainEvent>,
                StreamProvisionedDomainEventHandler>()
            .AddScoped<
                IDomainEventHandler<Domain.Stream.Events.StreamHealthChangedDomainEvent>,
                StreamHealthChangedDomainEventHandler>()
            .AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
    }

    private static void AddClockAndMediaMtxAccessServices(IServiceCollection services)
    {
        services
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IStreamWhepUrlBuilder, MediaMtxWhepUrlBuilder>()
            .AddSingleton<IWhepAuthValidator, WhepAuthValidator>()
            .AddSingleton<IMediaMtxApiAuthenticator, MediaMtxApiAuthenticator>();
    }

    private static void AddCommandHandlers(IServiceCollection services)
    {
        // Hand-rolled command/query handler registrations (ADR-0042 + ADR-0057).
        services
            .AddScoped<
                ICommandHandler<ProvisionStreamCommand, Result<StreamIdentifier, ProvisionStreamError>>,
                ProvisionStreamCommandHandler>()
            .AddScoped<
                ICommandHandler<ReportStreamHealthCommand, Result<StreamState, ReportStreamHealthError>>,
                ReportStreamHealthCommandHandler>()
            .AddScoped<
                ICommandHandler<AuthorizeWhepCommand, Result<MediaMtxPath, AuthorizeWhepError>>,
                AuthorizeWhepCommandHandler>()
            .AddScoped<
                ICommandHandler<AuthorizeMediaMtxApiCommand, Result<bool, AuthorizeMediaMtxApiError>>,
                AuthorizeMediaMtxApiCommandHandler>()
            .AddScoped<
                ICommandHandler<RetireStreamCommand, Result<Option<StreamIdentifier>, RetireStreamError>>,
                RetireStreamCommandHandler>()
            .AddScoped<
                ICommandHandler<RepointStreamCommand, Result<Option<StreamIdentifier>, RepointStreamError>>,
                RepointStreamCommandHandler>();
    }

    private static void AddQueryIntegrationEventAndDomainEventHandlers(IServiceCollection services)
    {
        // Query handlers are resolved as concrete classes by the API
        // endpoints; no IQueryHandler<,> dispatcher registration needed
        // because they're not invoked through Wolverine.
        services
            .AddScoped<GetStreamQueryHandler>()
            .AddScoped<ListStreamsQueryHandler>()
            .AddScoped<CameraRegisteredIntegrationEventHandler>()
            .AddScoped<CameraRetiredIntegrationEventHandler>()
            .AddScoped<CameraAddressChangedIntegrationEventHandler>()
            .AddScoped<StreamHealthChangedDomainEventHandler>()
            .AddScoped<StreamProvisionedDomainEventHandler>();
    }

    private static void AddStreamHostedServices(IServiceCollection services)
    {
        services
            .AddHostedService<StreamHealthWatcher>()
            .AddHostedService<MediaMtxReconciler>()
            .AddHostedService<StreamFabAttributionService>();
    }

    private static void BindMediaMtxOptions(IHostApplicationBuilder builder)
    {
        // Aspire injects MediaMTX endpoints as `services:mediamtx:api:0`
        // (HTTP control plane) and `services:mediamtx:whep:0` (browser WHEP).
        // Fallback through ConnectionStrings:mediamtx for non-Aspire hosts.
        var managementUrl =
            builder.Configuration["services:mediamtx:api:0"]
            ?? builder.Configuration.GetConnectionString("mediamtx-api")
            ?? throw new InvalidOperationException("MediaMTX management URL not configured (services:mediamtx:api:0).");
        var whepBaseUrl =
            builder.Configuration["services:mediamtx:whep:0"]
            ?? builder.Configuration.GetConnectionString("mediamtx-whep")
            ?? throw new InvalidOperationException("MediaMTX WHEP URL not configured (services:mediamtx:whep:0).");

        builder.Services.Configure<MediaMtxOptions>(options =>
        {
            options.ManagementUrl = managementUrl;
            options.WhepBaseUrl = whepBaseUrl;
        });
    }

    /// <summary>
    /// ADR-0171. The shared secret that authorizes both directions of the
    /// MediaMTX control API: this service's own calls out to MediaMTX
    /// (<see cref="AddMediaMtxGateway"/>) and MediaMTX's hook callback for
    /// those same calls (<see cref="AuthorizeMediaMtxApiCommandHandler"/>).
    /// </summary>
    private static void BindMediaMtxApiOptions(IHostApplicationBuilder builder)
    {
        // ValidateOnStart, not a runtime guard: an empty secret otherwise fails
        // closed silently — every api call and every WHEP viewer sharing the
        // hook keeps working, while MediaMtxApiAuthenticator.Authorizes refuses
        // every password including the real one, which only a 401 against a
        // live MediaMTX would surface. A misconfiguration should be a startup
        // failure, not a total, unexplained denial discovered in production.
        builder.Services.AddOptions<MediaMtxApiOptions>()
            .Configure(options => builder.Configuration.GetSection(MediaMtxApiOptions.SectionName).Bind(options))
            .Validate(
                options => !string.IsNullOrEmpty(options.Secret),
                "MediaMtxApi:Secret must be configured — an empty secret refuses every control-API call, "
                + "including this service's own, closed.")
            .ValidateOnStart();
    }

    /// <summary>
    /// The startup attribution pass and its two HTTP clients (ADR-0116). The
    /// camera-catalog client is registered by resource name so Aspire service
    /// discovery rewrites <c>http://camera-catalog</c> to whatever endpoint
    /// that resource publishes, in dev and on k3s alike — the same shape
    /// SystemVariables' <c>ReverseIndexSeederHostedService</c> already uses.
    /// </summary>
    private static void BindStreamFabAttribution(IHostApplicationBuilder builder)
    {
        var keycloakBaseUrl =
            builder.Configuration.GetConnectionString("keycloak")
            ?? builder.Configuration["services:keycloak:http:0"]
            ?? builder.Configuration["services:keycloak:https:0"]
            ?? string.Empty;

        builder.Services.Configure<StreamFabAttributionOptions>(options =>
        {
            builder.Configuration.GetSection(StreamFabAttributionOptions.SectionName).Bind(options);
            options.KeycloakUrl = keycloakBaseUrl;
            options.Realm = builder.Configuration["Keycloak:Realm"] ?? options.Realm;
        });

        // Idempotent POST — see ADR-0143; a second token supersedes the first.
        builder.Services.AddHttpClient(CameraCatalogTokenProvider.HttpClientName).RetryEveryMethod();
        builder.Services.AddSingleton<CameraCatalogTokenProvider>();

        // On the lookup only. The token provider's own client mints the token
        // this handler attaches, so authorising it would make the mint depend on
        // itself.
        builder.Services.AddTransient<CameraCatalogAuthorizationHandler>();

        // Nested mint: this handler mints a token through an opted-in client from inside
        // each attempt of this client's pipeline, so in a Keycloak outage one call can drive
        // several mints (about 9 token POSTs per call, measured on #2959). A failed mint counts
        // against THIS upstream's breaker when it surfaces as the attempt's timeout or as the
        // HttpRequestException from EnsureSuccessStatusCode on a 401; only a Keycloak 503
        // feeds Keycloak's own breaker. A credential Keycloak rejects is retried by the outer
        // call as well (4 token POSTs per outer GET, not 1). Accepted: the mint is
        // single-flight and the outer attempt timeout cancels it.
        builder.Services.AddHttpClient<ICameraFabLookup, CameraCatalogFabLookup>(client =>
        {
            // S1075 flags the literal URI, S5332 the clear-text scheme. Neither
            // applies: "camera-catalog" is a logical resource name and Aspire
            // rewrites the whole URI, scheme included.
#pragma warning disable S1075, S5332
            client.BaseAddress = new Uri("http://camera-catalog");
#pragma warning restore S1075, S5332
        }).AddHttpMessageHandler<CameraCatalogAuthorizationHandler>();
    }

    /// <summary>
    /// Spec 340 (#2238): outside Development, the WHEP metadata authority
    /// must be an absolute https URI, or startup refuses. Without this, a
    /// plaintext or malformed authority would surface only on the first WHEP
    /// open, as <c>IdentityProviderUnavailable</c> — indistinguishable from an
    /// unreachable realm. Same shape as <see cref="BindMediaMtxApiOptions"/>
    /// directly above.
    /// </summary>
    internal static void BindWhepAuthOptions(IHostApplicationBuilder builder)
    {
        // Derive the Keycloak authority the same way AuthenticationDefaults does.
        var keycloakBaseUrl =
            builder.Configuration.GetConnectionString("keycloak")
            ?? builder.Configuration["services:keycloak:http:0"]
            ?? builder.Configuration["services:keycloak:https:0"]
            ?? throw new InvalidOperationException("Keycloak base URL not configured for WHEP auth validator.");

        var realm = builder.Configuration["Keycloak:Realm"] ?? "smart-sentinel-eye";

        builder.Services.AddOptions<WhepAuthOptions>()
            .Configure(options =>
            {
                options.Authority = $"{keycloakBaseUrl.TrimEnd('/')}/realms/{realm}";
            })
            .Validate(
                options => !AuthenticationDefaults.RequiresHttpsMetadata(builder.Environment) || IsHttpsAbsolute(options.Authority),
                $"WhepAuthOptions:Authority must be an absolute https URI outside Development "
                + $"('{builder.Environment.EnvironmentName}' here): the WHEP hook's realm metadata must be "
                + "fetched over HTTPS, or serve Keycloak over TLS, or run under the Development environment.")
            .ValidateOnStart();
    }

    private static bool IsHttpsAbsolute(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
