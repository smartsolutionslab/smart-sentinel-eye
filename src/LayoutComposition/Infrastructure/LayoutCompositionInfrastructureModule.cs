using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Tiles;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;
using SmartSentinelEye.LayoutComposition.Infrastructure.Cameras;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
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

        builder.Services.AddScoped<ILayoutRepository, LayoutRepository>();
        builder.Services.AddScoped<ILayoutQuerySource, LayoutQuerySource>();
        builder.Services.AddScoped<IDomainEventHandler<LayoutRevisionPublishedDomainEvent>, LayoutRevisionPublishedDomainEventHandler>();
        builder.Services.AddScoped<IDomainEventHandler<LayoutRevisionArchivedDomainEvent>, LayoutRevisionArchivedDomainEventHandler>();
        builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<ILayoutLifecycleBroadcaster, SignalRLayoutLifecycleBroadcaster>();

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
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CallerTokenForwardingHandler>();
        builder.Services.AddHttpClient<ICameraFabGuard, CameraCatalogFabGuard>(client =>
        {
#pragma warning disable S1075, S5332
            client.BaseAddress = new Uri("http://camera-catalog");
#pragma warning restore S1075, S5332
        }).AddHttpMessageHandler<CallerTokenForwardingHandler>();

        // Hand-rolled command handler registrations (ADR-0042 + ADR-0057).
        builder.Services.AddScoped<
            ICommandHandler<CreateLayoutDraftCommand, Result<LayoutIdentifier, CreateLayoutDraftError>>,
            CreateLayoutDraftCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<PublishRevisionCommand, Result<LayoutRevisionNumber, PublishRevisionError>>,
            PublishRevisionCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<ArchiveRevisionCommand, Result<LayoutRevisionNumber, ArchiveRevisionError>>,
            ArchiveRevisionCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<BranchDraftRevisionCommand, Result<LayoutRevisionNumber, BranchDraftRevisionError>>,
            BranchDraftRevisionCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<EditDraftRevisionCommand, Result<LayoutRevisionNumber, EditDraftRevisionError>>,
            EditDraftRevisionCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<RevertRevisionCommand, Result<LayoutRevisionNumber, RevertRevisionError>>,
            RevertRevisionCommandHandler>();

        // Cross-context lifecycle relays: the publishing contexts emit an
        // integration event; the SignalR broadcast lives here with the hub
        // owner (no cross-context dependency). Wolverine binds a listener
        // queue per handled message type (ADR-0088).
        //   - Spec 007: Automation's overlay-highlight request.
        //   - Spec 004: OverlayDesigner's overlay-revision publish/archive.
        //   - Spec 005: SystemVariables' resolved-overlay-text change.
        builder.Services.AddScoped<OverlayHighlightRequestedV1Handler>();
        builder.Services.AddScoped<OverlayRevisionPublishedV1Handler>();
        builder.Services.AddScoped<OverlayRevisionArchivedV1Handler>();
        builder.Services.AddScoped<ResolvedOverlayTextChangedV1Handler>();

        builder.AddWolverineForContext<LayoutCompositionDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: LayoutCompositionPersistenceModule.DatabaseConnectionName);

        return builder;
    }
}
