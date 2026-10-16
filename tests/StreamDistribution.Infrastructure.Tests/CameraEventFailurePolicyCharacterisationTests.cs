using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.StreamDistribution.Application.EventHandlers;
using Wolverine;
using Wolverine.Runtime.Handlers;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 389 / issue #2999, ADR-0144), captured green before the camera URL value objects start
/// refusing a raw '@' after the authority. An integration event published before that change can carry an
/// address the new rule refuses; the two handlers turn the refusal into an exception, so what bounds the replay
/// is Wolverine's failure policy. Spec 389 relies on the default (three attempts, then the error queue) and adds
/// no rule of its own. This pins that reliance, by running Wolverine's real handler discovery over the
/// StreamDistribution Application assembly, so that a later rule scoped to these two messages (or to every
/// message) cannot change the replay behaviour without a test noticing.
///
/// <para>
/// Runs without a broker or a database for the reason <c>WallSceneSwitchFailurePolicyTests</c> gives:
/// <see cref="WolverineOptions.StubAllExternalTransports"/> and no Postgres persistence.
/// </para>
/// </summary>
public class CameraEventFailurePolicyCharacterisationTests
{
    private static async Task<HandlerGraph> DiscoverAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(CameraRegisteredIntegrationEventHandler).Assembly);
            opts.StubAllExternalTransports();
        });

        using var host = builder.Build();
        await host.StartAsync();
        var graph = host.Services.GetRequiredService<HandlerGraph>();
        await host.StopAsync();
        return graph;
    }

    [Fact]
    public async Task A_registered_camera_event_carries_no_failure_rule_of_its_own()
    {
        var graph = await DiscoverAsync();

        var chain = graph.ChainFor<CameraRegisteredV1>();

        chain.ShouldNotBeNull();
        chain.Failures.ShouldBeEmpty("the default bounded policy is what limits a replay of a refused address");
    }

    [Fact]
    public async Task An_address_changed_event_carries_no_failure_rule_of_its_own()
    {
        var graph = await DiscoverAsync();

        var chain = graph.ChainFor<CameraAddressChangedV1>();

        chain.ShouldNotBeNull();
        chain.Failures.ShouldBeEmpty("the default bounded policy is what limits a replay of a refused address");
    }

    [Fact]
    public async Task No_context_wide_rule_matches_the_exception_a_refused_address_raises()
    {
        var graph = await DiscoverAsync();

        graph.Failures.MaximumAttempts.ShouldBeNull("the default attempt count is what bounds a replay");
        foreach (var rule in graph.Failures)
        {
            rule.Match.Matches(new InvalidOperationException()).ShouldBeFalse(
                "a rule that catches this exception would replace the default for these two messages too");
        }
    }
}
