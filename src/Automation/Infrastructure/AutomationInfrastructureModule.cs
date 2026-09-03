using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Automation.Application.Commands;
using SmartSentinelEye.Automation.Application.Commands.Handlers;
using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Application.EventHandlers;
using SmartSentinelEye.Automation.Application.Queries;
using SmartSentinelEye.Automation.Application.Queries.Handlers;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Infrastructure.Cache;
using SmartSentinelEye.Automation.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Idempotency;
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
        Ensure.That(builder).IsNotNull();

        builder.AddAutomationPersistence();

        builder.Services.AddScoped<IRuleRepository, RuleRepository>();
        builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton(TimeProvider.System);

        // ADR-0142. Scoped alongside the DbContext it writes through; TimeProvider
        // above drives the executor's wait for an in-flight attempt.
        builder.Services.AddScoped<IIdempotencyStore, IdempotencyStore<AutomationDbContext>>();

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

        // Read side (spec 007 T059 + T089).
        builder.Services.AddScoped<IRuleQuerySource, RuleQuerySource>();
        builder.Services.AddScoped<GetRuleQueryHandler>();
        builder.Services.AddScoped<
            IQueryHandler<GetRuleQuery, Result<RuleDto, GetRuleError>>,
            GetRuleQueryHandler>();
        builder.Services.AddScoped<ListRulesQueryHandler>();
        builder.Services.AddScoped<
            IQueryHandler<ListRulesQuery, Result<IReadOnlyList<RuleDto>, ListRulesError>>,
            ListRulesQueryHandler>();
        builder.Services.AddScoped<DryRunRuleQueryHandler>();
        builder.Services.AddScoped<
            IQueryHandler<DryRunRuleQuery, Result<DryRunResultDto, DryRunRuleError>>,
            DryRunRuleQueryHandler>();

        builder.AddWolverineForContext<AutomationDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: AutomationPersistenceModule.DatabaseConnectionName);

        return builder;
    }
}
