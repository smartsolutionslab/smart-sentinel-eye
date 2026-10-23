using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Tiles;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.LayoutComposition.Domain.Wall.Events;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;
using SmartSentinelEye.LayoutComposition.Infrastructure.Cameras;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Infrastructure;

/// <summary>
/// Composition root for the LayoutComposition Infrastructure layer
/// (ADR-0051). Wires EF Core, the Wolverine outbox, the layout
/// repository, domain-event handlers, a no-op broadcaster (real
/// SignalR impl lands in PR E), and the standard ServiceDefaults.
/// </summary>
public static class LayoutCompositionInfrastructureModule
{
    public const string ContextName = "layout-composition";
    public const string OutboxSchema = "wolverine_layout_composition";

    public static IHostApplicationBuilder AddLayoutCompositionInfrastructure(this IHostApplicationBuilder builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.AddLayoutCompositionPersistence();
        builder.EnrichPostgresDbContext<LayoutCompositionDbContext>();

        AddRepositories(builder.Services);
        AddDomainEventHandlers(builder.Services);
        AddClocks(builder.Services);
        AddIdempotencyAndBroadcasting(builder.Services);
        AddCameraFabGuard(builder.Services);
        AddCommandHandlers(builder.Services);
        AddIntegrationEventHandlers(builder.Services);

        builder.AddWolverineForContext<LayoutCompositionDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: LayoutCompositionPersistenceModule.DatabaseConnectionName,
            // FR-011: a lost optimistic-concurrency race on
            // WallSceneSwitchRequestedV1 must not be retried (ADR-0113). No
            // other handler's chain is touched (WallSceneSwitchFailurePolicy
            // scopes itself to that one message type).
            //
            // ADR-0185 / spec 442: revision publish/archive and wall configuration are
            // admin changes that must outlive the broker; WallSceneChangedV1 (hot path,
            // mixed-caller) is deliberately not listed.
            durableOutboxMessageTypes:
            [
                typeof(LayoutRevisionPublishedV2),
                typeof(LayoutRevisionArchivedV1),
                typeof(WallConfiguredV1),
            ],
            configureMore: opts => opts.Policies.Add<WallSceneSwitchFailurePolicy>());

        return builder;
    }

    private static void AddRepositories(IServiceCollection services) =>
        services
            .AddScoped<ILayoutRepository, LayoutRepository>()
            .AddScoped<ILayoutQuerySource, LayoutQuerySource>()
            .AddScoped<IWallRepository, WallRepository>()
            .AddScoped<ILayoutPublicationLookup, LayoutPublicationLookup>()
            .AddScoped<IWallSwitchRequestDedupStore, WallSwitchRequestDedupStore>();

    private static void AddDomainEventHandlers(IServiceCollection services) =>
        services
            .AddScoped<IDomainEventHandler<LayoutRevisionPublishedDomainEvent>, LayoutRevisionPublishedDomainEventHandler>()
            .AddScoped<IDomainEventHandler<LayoutRevisionArchivedDomainEvent>, LayoutRevisionArchivedDomainEventHandler>()
            .AddScoped<IDomainEventHandler<WallConfiguredDomainEvent>, WallConfiguredDomainEventHandler>()
            .AddScoped<IDomainEventHandler<WallSceneSwitchedDomainEvent>, WallSceneSwitchedDomainEventHandler>()
            .AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

    private static void AddClocks(IServiceCollection services) =>
        services
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton(TimeProvider.System);

    // ADR-0142. Scoped alongside the DbContext it writes through; TimeProvider
    // (AddClocks) drives the executor's wait for an in-flight attempt.
    private static void AddIdempotencyAndBroadcasting(IServiceCollection services)
    {
        services
            .AddScoped<IIdempotencyStore, IdempotencyStore<LayoutCompositionDbContext>>()
            .AddIdempotencyReservationSweep<LayoutCompositionDbContext>()
            .AddSignalR();
        services.AddSingleton<ILayoutLifecycleBroadcaster, SignalRLayoutLifecycleBroadcaster>();
    }

    // FR-014: a tile's camera must be in its layout's fab, and only
    // CameraCatalog knows a camera's fab. The first synchronous call from
    // this context to another (plan.md §III) — write path only, carrying
    // the caller's own token, so a CameraCatalog outage stops layout
    // authoring and nothing else.
    //
    // Registered by resource name so Aspire service discovery rewrites
    // "http://camera-catalog" — scheme included — to whatever that
    // resource publishes, in dev and on k3s alike. S1075/S5332 flag the
    // literal; neither applies, because it is a logical name rather than
    // an address.
    private static void AddCameraFabGuard(IServiceCollection services)
    {
        services
            .AddHttpContextAccessor()
            .AddTransient<CallerTokenForwardingHandler>();
        services.AddHttpClient<ICameraFabGuard, CameraCatalogFabGuard>(client =>
        {
#pragma warning disable S1075, S5332
            client.BaseAddress = new Uri("http://camera-catalog");
#pragma warning restore S1075, S5332
        }).AddHttpMessageHandler<CallerTokenForwardingHandler>();
    }

    // Hand-rolled command handler registrations (ADR-0042 + ADR-0057).
    private static void AddCommandHandlers(IServiceCollection services) =>
        services
            .AddScoped<
                ICommandHandler<CreateLayoutDraftCommand, Result<LayoutIdentifier, CreateLayoutDraftError>>,
                CreateLayoutDraftCommandHandler>()
            .AddScoped<
                ICommandHandler<PublishRevisionCommand, Result<LayoutRevisionNumber, PublishRevisionError>>,
                PublishRevisionCommandHandler>()
            .AddScoped<
                ICommandHandler<ArchiveRevisionCommand, Result<LayoutRevisionNumber, ArchiveRevisionError>>,
                ArchiveRevisionCommandHandler>()
            .AddScoped<
                ICommandHandler<BranchDraftRevisionCommand, Result<LayoutRevisionNumber, BranchDraftRevisionError>>,
                BranchDraftRevisionCommandHandler>()
            .AddScoped<
                ICommandHandler<EditDraftRevisionCommand, Result<LayoutRevisionNumber, EditDraftRevisionError>>,
                EditDraftRevisionCommandHandler>()
            .AddScoped<
                ICommandHandler<RevertRevisionCommand, Result<LayoutRevisionNumber, RevertRevisionError>>,
                RevertRevisionCommandHandler>()
            .AddScoped<
                ICommandHandler<CreateWallCommand, Result<WallIdentifier, CreateWallError>>,
                CreateWallCommandHandler>()
            .AddScoped<
                ICommandHandler<EditWallScenesCommand, Result<WallDto, EditWallScenesError>>,
                EditWallScenesCommandHandler>()
            .AddScoped<
                ICommandHandler<SwitchWallSceneCommand, Result<WallDto, SwitchWallSceneError>>,
                SwitchWallSceneCommandHandler>();

    // Cross-context lifecycle relays: the publishing contexts emit an
    // integration event; the SignalR broadcast lives here with the hub
    // owner (no cross-context dependency). Wolverine binds a listener
    // queue per handled message type (ADR-0088).
    //   - Spec 007: Automation's overlay-highlight request.
    //   - Spec 004: OverlayDesigner's overlay-revision publish/archive.
    //   - Spec 005: SystemVariables' resolved-overlay-text change.
    //   - Spec 296: Automation's rule-driven wall-switch request.
    //
    // WallSceneChangedV1Handler is the same-context relay (spec 258 US1,
    // phase-6 remediation): this context both publishes WallSceneChangedV1
    // and subscribes to it, so the broadcast only happens once Wolverine's
    // outbox has actually released the message — after the commit that
    // raised it, never before.
    private static void AddIntegrationEventHandlers(IServiceCollection services) =>
        services
            .AddScoped<OverlayHighlightRequestedV1Handler>()
            .AddScoped<OverlayRevisionPublishedV3Handler>()
            .AddScoped<OverlayRevisionArchivedV1Handler>()
            .AddScoped<ResolvedOverlayTextChangedV3Handler>()
            .AddScoped<WallSceneSwitchRequestedV1Handler>()
            .AddScoped<WallSceneChangedV1Handler>();
}
