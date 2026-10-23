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
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Infrastructure;

/// <summary>
/// Composition root for the OverlayDesigner Infrastructure layer
/// (ADR-0051). Wires EF Core, the Wolverine outbox, the overlay
/// repository, domain-event handlers, and the standard ServiceDefaults.
///
/// <para>
/// The overlay-published / archived domain-event handlers only publish
/// the <c>OverlayRevisionPublished/ArchivedV1</c> integration events;
/// the SignalR push lives in LayoutComposition (the hub owner), which
/// subscribes to those events. This context therefore has no dependency
/// on LayoutComposition.
/// </para>
/// </summary>
public static class OverlayDesignerInfrastructureModule
{
    public const string ContextName = "overlay-designer";
    public const string OutboxSchema = "wolverine_overlay_designer";

    public static IHostApplicationBuilder AddOverlayDesignerInfrastructure(this IHostApplicationBuilder builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.AddOverlayDesignerPersistence();
        builder.EnrichPostgresDbContext<OverlayDesignerDbContext>();

        builder.Services
            .AddOverlayStoreAndDomainEventHandlers()
            .AddClockAndIdempotencyStore()
            .AddCommandHandlers();

        builder.AddWolverineForContext<OverlayDesignerDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: OverlayDesignerPersistenceModule.DatabaseConnectionName,
            // ADR-0185: publishing and archiving a revision are admin
            // changes that must outlive the broker.
            durableOutboxMessageTypes:
            [
                typeof(OverlayRevisionPublishedV3),
                typeof(OverlayRevisionArchivedV1),
            ]);

        return builder;
    }

    private static IServiceCollection AddOverlayStoreAndDomainEventHandlers(this IServiceCollection services) =>
        services
            .AddScoped<IOverlayRepository, OverlayRepository>()
            .AddScoped<IOverlayQuerySource, OverlayQuerySource>()
            .AddScoped<IDomainEventHandler<OverlayRevisionPublishedDomainEvent>, OverlayRevisionPublishedDomainEventHandler>()
            .AddScoped<IDomainEventHandler<OverlayRevisionArchivedDomainEvent>, OverlayRevisionArchivedDomainEventHandler>()
            .AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

    private static IServiceCollection AddClockAndIdempotencyStore(this IServiceCollection services) =>
        services
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton(TimeProvider.System)
            // ADR-0142. Scoped alongside the DbContext it writes through; TimeProvider
            // above drives the executor's wait for an in-flight attempt.
            .AddScoped<IIdempotencyStore, IdempotencyStore<OverlayDesignerDbContext>>()
            .AddIdempotencyReservationSweep<OverlayDesignerDbContext>();

    private static void AddCommandHandlers(this IServiceCollection services) =>
        services
            .AddScoped<
                ICommandHandler<CreateOverlayDraftCommand, Result<OverlayIdentifier, CreateOverlayDraftError>>,
                CreateOverlayDraftCommandHandler>()
            .AddScoped<
                ICommandHandler<PublishRevisionCommand, Result<OverlayRevisionNumber, PublishRevisionError>>,
                PublishRevisionCommandHandler>()
            .AddScoped<
                ICommandHandler<ArchiveRevisionCommand, Result<OverlayRevisionNumber, ArchiveRevisionError>>,
                ArchiveRevisionCommandHandler>()
            .AddScoped<
                ICommandHandler<BranchDraftRevisionCommand, Result<OverlayRevisionNumber, BranchDraftRevisionError>>,
                BranchDraftRevisionCommandHandler>()
            .AddScoped<
                ICommandHandler<EditDraftRevisionCommand, Result<OverlayRevisionNumber, EditDraftRevisionError>>,
                EditDraftRevisionCommandHandler>()
            .AddScoped<
                ICommandHandler<RevertRevisionCommand, Result<OverlayRevisionNumber, RevertRevisionError>>,
                RevertRevisionCommandHandler>();
}
