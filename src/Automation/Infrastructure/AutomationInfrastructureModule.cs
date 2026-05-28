using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Automation.Application.Commands;
using SmartSentinelEye.Automation.Application.Commands.Handlers;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Application.EventHandlers;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Infrastructure.Cache;
using SmartSentinelEye.Automation.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Infrastructure;

/// <summary>
/// Composition root for the Automation Infrastructure layer
/// (ADR-0051). Wires persistence, the rule cache + cold-start
/// seeder, the rule evaluator, the FabEventIngestedV1 Wolverine
/// subscriber (registered via assembly scanning), command handlers,
/// and the Wolverine outbox.
/// </summary>
public static class AutomationInfrastructureModule
{
    public const string ContextName = "automation";
    public const string OutboxSchema = "wolverine_automation";

    public static IHostApplicationBuilder AddAutomationInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAutomationPersistence();

        builder.Services.AddScoped<IRuleRepository, RuleRepository>();
        builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();

        // Rule cache + evaluator + cold-start seeder.
        builder.Services.AddSingleton<IRuleCache, InMemoryRuleCache>();
        builder.Services.AddScoped<RuleEvaluator>();
        builder.Services.AddHostedService<RuleCacheSeederHostedService>();

        // Wolverine subscriber on FabEventIngestedV1 (spec 006 -> 007 bridge).
        // Discovered by Wolverine via assembly scanning; registered as scoped
        // so a fresh RuleEvaluator (and its cache snapshot) is picked up per
        // message.
        builder.Services.AddScoped<FabEventIngestedV1Handler>();

        // Hand-rolled command handler registrations (ADR-0042 + ADR-0057).
        builder.Services.AddScoped<CreateRuleCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<CreateRuleCommand, Result<RuleIdentifier, CreateRuleError>>,
            CreateRuleCommandHandler>();
        builder.Services.AddScoped<PublishRuleCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<PublishRuleCommand, Result<RuleIdentifier, PublishRuleError>>,
            PublishRuleCommandHandler>();
        builder.Services.AddScoped<ArchiveRuleCommandHandler>();
        builder.Services.AddScoped<
            ICommandHandler<ArchiveRuleCommand, Result<RuleIdentifier, ArchiveRuleError>>,
            ArchiveRuleCommandHandler>();

        builder.AddWolverineForContext<AutomationDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: AutomationPersistenceModule.DatabaseConnectionName);

        return builder;
    }
}
