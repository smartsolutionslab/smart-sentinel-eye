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
using SmartSentinelEye.ServiceDefaults.Persistence;
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
        builder.EnrichPostgresDbContext<AutomationDbContext>();

        builder.Services
            .AddPersistenceServices()
            .AddRuleEvaluation()
            .AddCommandHandlers()
            .AddQueryHandlers();

        builder.AddWolverineForContext<AutomationDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: AutomationPersistenceModule.DatabaseConnectionName);

        return builder;
    }

    private static IServiceCollection AddPersistenceServices(this IServiceCollection services) =>
        services
            .AddScoped<IRuleRepository, RuleRepository>()
            .AddScoped<IDomainEventDispatcher, DomainEventDispatcher>()
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton(TimeProvider.System)
            // ADR-0142. Scoped alongside the DbContext it writes through; TimeProvider
            // above drives the executor's wait for an in-flight attempt.
            .AddScoped<IIdempotencyStore, IdempotencyStore<AutomationDbContext>>()
            .AddIdempotencyReservationSweep<AutomationDbContext>();

    private static IServiceCollection AddRuleEvaluation(this IServiceCollection services) =>
        services
            // Rule cache + evaluator + cold-start seeder.
            .AddSingleton<IRuleCache, InMemoryRuleCache>()
            .AddScoped<RuleEvaluator>()
            .AddHostedService<RuleCacheSeederHostedService>()
            // Wolverine subscriber on FabEventIngestedV1 (spec 006 -> 007 bridge).
            // Discovered by Wolverine via assembly scanning; registered as scoped
            // so a fresh RuleEvaluator (and its cache snapshot) is picked up per
            // message.
            .AddScoped<FabEventIngestedV1Handler>();

    // Hand-rolled command handler registrations (ADR-0042 + ADR-0057).
    private static IServiceCollection AddCommandHandlers(this IServiceCollection services) =>
        services
            .AddScoped<CreateRuleCommandHandler>()
            .AddScoped<
                ICommandHandler<CreateRuleCommand, Result<RuleIdentifier, CreateRuleError>>,
                CreateRuleCommandHandler>()
            .AddScoped<PublishRuleCommandHandler>()
            .AddScoped<
                ICommandHandler<PublishRuleCommand, Result<RuleIdentifier, PublishRuleError>>,
                PublishRuleCommandHandler>()
            .AddScoped<ArchiveRuleCommandHandler>()
            .AddScoped<
                ICommandHandler<ArchiveRuleCommand, Result<RuleIdentifier, ArchiveRuleError>>,
                ArchiveRuleCommandHandler>();

    // Read side (spec 007 T059 + T089).
    private static void AddQueryHandlers(this IServiceCollection services) =>
        services
            .AddScoped<IRuleQuerySource, RuleQuerySource>()
            .AddScoped<GetRuleQueryHandler>()
            .AddScoped<
                IQueryHandler<GetRuleQuery, Result<RuleDto, GetRuleError>>,
                GetRuleQueryHandler>()
            .AddScoped<ListRulesQueryHandler>()
            .AddScoped<
                IQueryHandler<ListRulesQuery, Result<IReadOnlyList<RuleDto>, ListRulesError>>,
                ListRulesQueryHandler>()
            .AddScoped<DryRunRuleQueryHandler>()
            .AddScoped<
                IQueryHandler<DryRunRuleQuery, Result<DryRunResultDto, DryRunRuleError>>,
                DryRunRuleQueryHandler>();
}
