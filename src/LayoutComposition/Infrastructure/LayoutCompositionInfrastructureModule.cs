using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;
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
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddLayoutCompositionPersistence();

        builder.Services.AddScoped<ILayoutRepository, LayoutRepository>();
        builder.Services.AddScoped<ILayoutQuerySource, LayoutQuerySource>();
        builder.Services.AddScoped<
            IDomainEventHandler<LayoutRevisionPublishedDomainEvent>,
            LayoutRevisionPublishedDomainEventHandler>();
        builder.Services.AddScoped<
            IDomainEventHandler<LayoutRevisionArchivedDomainEvent>,
            LayoutRevisionArchivedDomainEventHandler>();
        builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<ILayoutLifecycleBroadcaster, SignalRLayoutLifecycleBroadcaster>();

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

        builder.AddWolverineForContext<LayoutCompositionDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: LayoutCompositionPersistenceModule.DatabaseConnectionName,
            configureMore: opts =>
            {
                // Include the Application assembly so the domain-event
                // handlers + any future Wolverine subscribers are bound.
                // Tracked by tech-debt #200 for a convention-based fix.
                opts.Discovery.IncludeAssembly(
                    typeof(LayoutRevisionPublishedDomainEventHandler).Assembly);
            });

        return builder;
    }
}
