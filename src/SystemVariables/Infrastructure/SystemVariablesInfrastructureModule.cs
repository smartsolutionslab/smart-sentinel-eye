using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.ServiceDefaults.Resilience;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Commands;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Domain.Variable.Events;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;
using SmartSentinelEye.SystemVariables.Infrastructure.Resolution;

namespace SmartSentinelEye.SystemVariables.Infrastructure;

/// <summary>
/// Composition root for the SystemVariables Infrastructure layer
/// (ADR-0051). Wires EF Core, the Wolverine outbox + cross-context
/// subscribers, the variable repository, the in-memory reverse-index
/// (singleton) and its seeder hosted service, domain-event handlers,
/// and the standard ServiceDefaults.
///
/// <para>
/// The variable-changed / archived domain-event handlers publish a
/// <c>ResolvedOverlayTextChangedV3</c> per affected overlay; the SignalR
/// push lives in LayoutComposition (the hub owner), which subscribes to
/// that event. This context therefore has no dependency on
/// LayoutComposition.
/// </para>
/// </summary>
public static class SystemVariablesInfrastructureModule
{
    public const string ContextName = "system-variables";
    public const string OutboxSchema = "wolverine_system_variables";

    public static IHostApplicationBuilder AddSystemVariablesInfrastructure(this IHostApplicationBuilder builder)
    {
        Ensure.That(builder).IsNotNull();

        builder
            .AddSystemVariablesPersistence()
            .EnrichPostgresDbContext<SystemVariablesDbContext>();

        builder.Services
            .AddPersistenceClockAndIdempotencyStore()
            .AddOverlayTextResolution()
            .AddDomainEventHandlers()
            .AddCommandHandlers()
            .AddValueRequestBridge()
            // Issue #2426. Scoped alongside the DbContext it writes through —
            // it holds no state of its own, so a singleton would only pin a
            // captive scoped DbContext for no benefit.
            .AddScoped<IOverlayTextVersions, OverlayTextVersionStore>();

        BindReverseIndexSeeder(builder);

        builder.AddWolverineForContext<SystemVariablesDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: SystemVariablesPersistenceModule.DatabaseConnectionName,
            // ADR-0185 / spec 442: definition and archival are admin changes that must
            // outlive the broker; the value-changed hot path is deliberately not listed.
            durableOutboxMessageTypes:
            [
                typeof(SystemVariableDefinedV1),
                typeof(SystemVariableArchivedV1),
            ]);

        return builder;
    }

    private static IServiceCollection AddPersistenceClockAndIdempotencyStore(this IServiceCollection services) =>
        services
            .AddScoped<IVariableRepository, VariableRepository>()
            .AddScoped<IVariableQuerySource, VariableQuerySource>()
            .AddScoped<IDomainEventDispatcher, DomainEventDispatcher>()
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton(TimeProvider.System)
            // ADR-0142. Scoped alongside the DbContext it writes through; TimeProvider
            // above drives the executor's wait for an in-flight attempt.
            .AddScoped<IIdempotencyStore, IdempotencyStore<SystemVariablesDbContext>>()
            .AddIdempotencyReservationSweep<SystemVariablesDbContext>();

    private static IServiceCollection AddOverlayTextResolution(this IServiceCollection services) =>
        services
            // Reverse-index + resolver are process-wide singletons (the
            // index is mutated concurrently; the resolver is stateless).
            .AddSingleton<IReverseIndex, InMemoryReverseIndex>()
            .AddSingleton<IResolver, Resolver>()
            // Scoped, not singleton (spec 148): VariableSnapshotBuilder depends on
            // IVariableRepository, which is itself scoped — a singleton capturing
            // it would be a captive dependency.
            .AddScoped<IVariableSnapshotBuilder, VariableSnapshotBuilder>();

    // Domain event handlers (in-process, fan out to V1 + broadcaster).
    private static IServiceCollection AddDomainEventHandlers(this IServiceCollection services) =>
        services
            .AddScoped<
                IDomainEventHandler<VariableDefinedDomainEvent>,
                VariableDefinedDomainEventHandler>()
            .AddScoped<
                IDomainEventHandler<VariableValueChangedDomainEvent>,
                VariableValueChangedDomainEventHandler>()
            .AddScoped<
                IDomainEventHandler<VariableArchivedDomainEvent>,
                VariableArchivedDomainEventHandler>();

    // Hand-rolled command handler registrations (ADR-0042 + ADR-0057).
    private static IServiceCollection AddCommandHandlers(this IServiceCollection services) =>
        services
            .AddScoped<
                ICommandHandler<DefineVariableCommand, Result<VariableIdentifier, DefineVariableError>>,
                DefineVariableCommandHandler>()
            .AddScoped<SetVariableValueCommandHandler>()
            .AddScoped<
                ICommandHandler<SetVariableValueCommand, Result<VariableIdentifier, SetVariableValueError>>,
                SetVariableValueCommandHandler>()
            .AddScoped<
                ICommandHandler<ArchiveVariableCommand, Result<VariableIdentifier, ArchiveVariableError>>,
                ArchiveVariableCommandHandler>();

    // Spec 007 bridge: subscribe to SystemVariableValueRequestedV1
    // from Automation. Dedup on (fab, variableName, causingEventIdentifier).
    private static IServiceCollection AddValueRequestBridge(this IServiceCollection services) =>
        services
            .AddScoped<IVariableValueRequestDedupStore, VariableValueRequestDedupStore>()
            .AddScoped<SystemVariableValueRequestedV1Handler>();

    /// <summary>
    /// The startup reverse-index seed and the credential it presents (spec 126,
    /// #2158). Mirrors the attribution wiring StreamDistribution had until spec 423 —
    /// same shape of call, same shape of wiring.
    /// </summary>
    private static void BindReverseIndexSeeder(IHostApplicationBuilder builder)
    {
        var keycloakBaseUrl =
            builder.Configuration.GetConnectionString("keycloak")
            ?? builder.Configuration["services:keycloak:http:0"]
            ?? builder.Configuration["services:keycloak:https:0"]
            ?? string.Empty;

        builder.Services.Configure<ReverseIndexSeederOptions>(options =>
        {
            builder.Configuration.GetSection(ReverseIndexSeederOptions.SectionName).Bind(options);
            options.KeycloakUrl = keycloakBaseUrl;
            options.Realm = builder.Configuration["Keycloak:Realm"] ?? options.Realm;
        });

        // Idempotent POST — see ADR-0143; a second token supersedes the first.
        // Without the opt-in, a Keycloak blip at host start would leave the index
        // empty for the life of the process, which is the defect this fixes.
        builder.Services.AddHttpClient(OverlayDesignerTokenProvider.HttpClientName).RetryEveryMethod();
        // Singleton, which is what makes the token cache mean anything (#2037).
        builder.Services.AddSingleton<OverlayDesignerTokenProvider>();
        // On the seed client only. The token provider's own client mints the
        // token this handler attaches, so authorising it would make the mint
        // depend on itself.
        builder.Services.AddTransient<OverlayDesignerAuthorizationHandler>();

        // Nested mint: this handler mints a token inside each attempt of this client's
        // pipeline, so a Keycloak outage multiplies calls; see UpstreamAuthority for what
        // that costs and why it is accepted.
        // Named rather than typed, because the seeder is a hosted service and so
        // a singleton: a typed client injected into one is held for the process
        // lifetime, which pins a single HttpMessageHandler and defeats the
        // factory's rotation. The base address belongs here rather than at the
        // call site — S1075 flags the literal URI and S5332 the clear-text
        // scheme, and neither applies: Aspire service discovery treats
        // "overlay-designer" as a logical resource name and rewrites the whole
        // URI, scheme included, in dev and on k3s alike.
#pragma warning disable S1075, S5332
        builder.Services.AddHttpClient("overlay-designer", client =>
                client.BaseAddress = new Uri("http://overlay-designer"))
#pragma warning restore S1075, S5332
            .AddHttpMessageHandler<OverlayDesignerAuthorizationHandler>();

        builder.Services.AddHostedService<ReverseIndexSeederHostedService>();
    }
}
