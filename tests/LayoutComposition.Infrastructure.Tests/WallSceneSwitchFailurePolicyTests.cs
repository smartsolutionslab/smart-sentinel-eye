using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.LayoutComposition.Infrastructure;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Tests;

/// <summary>
/// Spec 296 FR-011, T114. Proves — by actually running Wolverine's handler
/// discovery, not by reading source text — that
/// <c>WallSceneSwitchFailurePolicy</c> maps a lost optimistic-concurrency
/// race on <c>WallSceneSwitchRequestedV1</c> to the dead-letter queue with
/// no retry, on the first attempt, and that no other discovered chain
/// gained a failure rule.
///
/// <para>
/// <b>How this runs without a live Postgres or RabbitMQ</b> (ADR-0103: no
/// Testcontainers, and this isn't one — nothing here is a container).
/// <see cref="WolverineOptions.StubAllExternalTransports"/> plus never
/// calling <c>PersistMessagesWithPostgresql</c> lets <c>UseWolverine</c>
/// compile real handler chains in-process; <c>WolverineFx.RuntimeCompilation</c>
/// flows in transitively via this context's own Infrastructure project
/// referencing <c>ServiceDefaults</c>. Confirmed practical by a throwaway
/// probe before writing this file (two stand-in handlers, a policy scoped
/// to one of them via <c>IHandlerPolicy</c>): without the scoped
/// registration both chains' <c>Failures</c> come back empty; with it,
/// only the targeted chain's does, and <c>TryCreateContinuation</c> for the
/// matched exception returns a real <c>MoveToErrorQueue</c> continuation —
/// so the assertions below are not vacuous.
/// </para>
///
/// <para>
/// <b>Pins the implementation shape</b> (plan.md §3.3 leaves the exact
/// Wolverine API to the engineer): a public, parameterless
/// <c>WallSceneSwitchFailurePolicy : IHandlerPolicy</c> in
/// <c>SmartSentinelEye.LayoutComposition.Infrastructure</c>, registered via
/// <c>opts.Policies.Add&lt;WallSceneSwitchFailurePolicy&gt;()</c> inside
/// <c>LayoutCompositionInfrastructureModule</c>'s <c>AddWolverineForContext</c>
/// call. This is the one mechanism this investigation found that is
/// testable without a live broker — the alternative plan.md also names, a
/// <c>configureMore</c> lambda wired through the same call, cannot be
/// exercised here because <c>AddWolverineForContext</c> itself calls
/// <c>PersistMessagesWithPostgresql</c> and <c>UseRabbitMq(...).AutoProvision()</c>,
/// both of which need a reachable broker/database at <c>StartAsync</c>.
/// </para>
///
/// <para>
/// <b>Counterfactual (spec A3 / tasks.md T114):</b> delete
/// <c>WallSceneSwitchFailurePolicy</c>'s body, or its registration, and
/// <see cref="A_lost_concurrency_race_is_mapped_to_the_dead_letter_queue_with_no_retry"/>
/// fails — <c>Failures</c> comes back empty and there is nothing for
/// <c>TryCreateContinuation</c> to build.
/// </para>
/// </summary>
public class WallSceneSwitchFailurePolicyTests
{
    private static async Task<HandlerGraph> DiscoverAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.UseWolverine(opts =>
        {
            // Anchored on a type that already exists today, so this test's
            // own discovery setup does not itself depend on the handler
            // T120-T122 add — only WallSceneSwitchFailurePolicy and the
            // handler it targets need to exist for the assertions below.
            opts.Discovery.IncludeAssembly(typeof(Application.Queries.GetWallQuery).Assembly);
            opts.Policies.Add<WallSceneSwitchFailurePolicy>();
            opts.StubAllExternalTransports();
        });

        using IHost host = builder.Build();
        await host.StartAsync();
        HandlerGraph graph = host.Services.GetRequiredService<HandlerGraph>();
        await host.StopAsync();
        return graph;
    }

    [Fact]
    public async Task A_lost_concurrency_race_is_mapped_to_the_dead_letter_queue_with_no_retry()
    {
        HandlerGraph graph = await DiscoverAsync();
        HandlerChain? chain = graph.ChainFor<WallSceneSwitchRequestedV1>();
        chain.ShouldNotBeNull();

        FailureRule rule = chain.Failures.ShouldHaveSingleItem();
        rule.Match.Matches(new DbUpdateConcurrencyException()).ShouldBeTrue();
        rule.Match.Matches(new InvalidOperationException()).ShouldBeFalse(
            "scoped to the one exception type FR-011 names, not every exception");

        Envelope envelope = new(new WallSceneSwitchRequestedV1(
            Guid.CreateVersion7(), "Next", null, Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, Guid.CreateVersion7(),
            new EventMetadata(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "munich", null)))
        {
            Attempts = 1,
        };
        bool created = rule.TryCreateContinuation(
            new DbUpdateConcurrencyException(), envelope, out IContinuation continuation);

        created.ShouldBeTrue();
        // MoveToErrorQueue (Wolverine.ErrorHandling) is internal to Wolverine,
        // so the type name is the only accessible handle on which concrete
        // continuation this produced.
        continuation.ShouldNotBeNull();
        continuation.GetType().Name.ShouldBe(
            "MoveToErrorQueue",
            "FR-011: a lost concurrency race is dead-lettered on the first attempt, never retried");
    }

    [Fact]
    public async Task No_other_discovered_chain_gained_a_failure_rule()
    {
        HandlerGraph graph = await DiscoverAsync();

        HandlerChain[] others = [.. graph.AllChains()
            .Where(chain => chain.MessageType != typeof(WallSceneSwitchRequestedV1))];
        others.ShouldNotBeEmpty("otherwise this proves nothing about scoping");

        foreach (HandlerChain chain in others)
        {
            chain.Failures.ShouldBeEmpty(
                $"{chain.MessageType.Name}'s chain gained a failure rule nothing asked it to have");
        }
    }
}
