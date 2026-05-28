using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.ServiceDefaults;
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
/// Does NOT register <see cref="LayoutComposition.Domain.Layout.ILayoutLifecycleBroadcaster"/> —
/// that's consumed from LayoutComposition.Api's container via the
/// shared abstraction (spec 005 plan.md; cross-context allow-rule).
/// </para>
/// </summary>
public static class SystemVariablesInfrastructureModule
{
    public const string ContextName = "system-variables";
    public const string OutboxSchema = "wolverine_system_variables";

    public static IHostApplicationBuilder AddSystemVariablesInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddSystemVariablesPersistence();

        builder.Services.AddScoped<IVariableRepository, VariableRepository>();
        builder.Services.AddScoped<IVariableQuerySource, VariableQuerySource>();
        builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();

        // Reverse-index + resolver are process-wide singletons (the
        // index is mutated concurrently; the resolver is stateless).
        builder.Services.AddSingleton<IReverseIndex, InMemoryReverseIndex>();
        builder.Services.AddSingleton<IResolver, Resolver>();

        // Domain event handlers (in-process, fan out to V1 + broadcaster).
        builder.Services.AddScoped<
            IDomainEventHandler<VariableValueChangedDomainEvent>,
            VariableValueChangedDomainEventHandler>();
        builder.Services.AddScoped<
            IDomainEventHandler<VariableArchivedDomainEvent>,
            VariableArchivedDomainEventHandler>();

        // Hand-rolled command handler registrations (ADR-0042 + ADR-0057).
        builder.Services.AddScoped<
            ICommandHandler<DefineVariableCommand, Result<VariableIdentifier, DefineVariableError>>,
            DefineVariableCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<SetVariableValueCommand, Result<VariableIdentifier, SetVariableValueError>>,
            SetVariableValueCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<ArchiveVariableCommand, Result<VariableIdentifier, ArchiveVariableError>>,
            ArchiveVariableCommandHandler>();

        // Startup seeder for the reverse-index. Best-effort — the
        // Wolverine subscribers self-heal as overlay V1 events arrive.
        builder.Services.AddHttpClient("overlay-designer");
        builder.Services.AddHostedService<ReverseIndexSeederHostedService>();

        builder.AddWolverineForContext<SystemVariablesDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: SystemVariablesPersistenceModule.DatabaseConnectionName);

        return builder;
    }
}
