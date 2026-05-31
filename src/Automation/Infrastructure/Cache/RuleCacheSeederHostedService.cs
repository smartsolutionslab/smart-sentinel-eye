using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Infrastructure.Persistence;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Infrastructure.Cache;

/// <summary>
/// Cold-start seeder for <see cref="IRuleCache"/> (spec 007
/// NFR-003). Queries the <c>rules</c> table for Active rows and
/// pushes them into the singleton cache before the
/// <c>FabEventIngestedV1</c> Wolverine subscriber starts pulling
/// events.
///
/// <para>
/// Best-effort: a DB outage at startup is logged but does NOT
/// crash the host. The cache stays empty until the next process
/// restart; live <see cref="IRuleCache.Upsert"/> calls from the
/// Publish handler still work once the DB recovers.
/// </para>
/// </summary>
public sealed class RuleCacheSeederHostedService(
    IDbContextFactory<AutomationDbContext> dbContextFactory,
    IRuleCache cache,
    ILogger<RuleCacheSeederHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AutomationDbContext context = await dbContextFactory
                .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            List<RuleAggregate> active = await context.Rules
                .Where(r => r.State == RuleState.Active)
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (RuleAggregate rule in active)
            {
                cache.Upsert(rule);
            }

            logger.LogInformation("Seeded rule cache with {Count} Active rule(s).", active.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Rule cache seeding failed; cache will start empty and self-heal on the next Publish.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
