using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Infrastructure;

/// <summary>
/// Composition root for the AuditObservability Infrastructure
/// layer (ADR-0051). Wires persistence, the query handlers, the
/// audit-write subscriber, and the Wolverine outbox + bus
/// subscriptions.
/// </summary>
public static class AuditObservabilityInfrastructureModule
{
    public const string ContextName = "audit-observability";
    public const string OutboxSchema = "wolverine_audit";

    public static IHostApplicationBuilder AddAuditObservabilityInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAuditObservabilityPersistence();

        builder.Services.AddScoped<IAuditEventRepository, AuditEventRepository>();
        builder.Services.AddScoped<IAuditEventQuerySource, AuditEventQuerySource>();

        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddSingleton(V1ResourceMap.Default);
        builder.Services.AddScoped<AuditingMessageHandler>();

        builder.Services.AddScoped<SearchAuditQueryHandler>();
        builder.Services.AddScoped<GetResourceTimelineQueryHandler>();
        builder.Services.AddScoped<GetAuditEventQueryHandler>();

        builder.AddWolverineForContext<AuditObservabilityDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: AuditObservabilityPersistenceModule.DatabaseConnectionName);

        return builder;
    }
}
