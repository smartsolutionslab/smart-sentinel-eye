using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.EventHandlers;
using SmartSentinelEye.OverlayDesigner.Application.Queries;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Infrastructure;

/// <summary>
/// Composition root for the OverlayDesigner Infrastructure layer
/// (ADR-0051). Wires EF Core, the Wolverine outbox, the overlay
/// repository, domain-event handlers, and the standard ServiceDefaults.
///
/// <para>
/// Does NOT register <see cref="LayoutComposition.Domain.Layout.ILayoutLifecycleBroadcaster"/> —
/// that's consumed from the LayoutComposition Api process's container
/// registration via the shared abstraction (spec 004 plan.md; cross-
/// context exception documented in <c>OverlayDesigner.Application</c>'s
/// csproj).
/// </para>
/// </summary>
public static class OverlayDesignerInfrastructureModule
{
    public const string ContextName = "overlay-designer";
    public const string OutboxSchema = "wolverine_overlay_designer";

    public static IHostApplicationBuilder AddOverlayDesignerInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddOverlayDesignerPersistence();

        builder.Services.AddScoped<IOverlayRepository, OverlayRepository>();
        builder.Services.AddScoped<IOverlayQuerySource, OverlayQuerySource>();
        builder.Services.AddScoped<
            IDomainEventHandler<OverlayRevisionPublishedDomainEvent>,
            OverlayRevisionPublishedDomainEventHandler>();
        builder.Services.AddScoped<
            IDomainEventHandler<OverlayRevisionArchivedDomainEvent>,
            OverlayRevisionArchivedDomainEventHandler>();
        builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();

        builder.Services.AddScoped<
            ICommandHandler<CreateOverlayDraftCommand, Result<OverlayIdentifier, CreateOverlayDraftError>>,
            CreateOverlayDraftCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<PublishRevisionCommand, Result<OverlayRevisionNumber, PublishRevisionError>>,
            PublishRevisionCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<ArchiveRevisionCommand, Result<OverlayRevisionNumber, ArchiveRevisionError>>,
            ArchiveRevisionCommandHandler>();

        builder.AddWolverineForContext<OverlayDesignerDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: OverlayDesignerPersistenceModule.DatabaseConnectionName,
            configureMore: opts =>
            {
                opts.Discovery.IncludeAssembly(
                    typeof(OverlayRevisionPublishedDomainEventHandler).Assembly);
            });

        return builder;
    }
}
