using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.StreamDistribution.Application.EventHandlers;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 389 / issue #2999, ADR-0144), captured green before the camera URL value objects start
/// refusing a raw '@' after the authority. An integration event published before that change can carry an
/// address the new rule refuses; the two handlers turn the refusal into an exception, so what bounds the replay
/// is Wolverine's failure policy. Spec 389 relies on the default (the first failure goes to the error queue) and adds
/// no rule of its own.
///
/// <para>
/// Pinned: (1) with Wolverine's real handler discovery over the StreamDistribution Application assembly, each of
/// the two chains combined with the graph's rules send an <see cref="InvalidOperationException"/> to the error queue
/// on every attempt, with no retry (the plan's "three attempts" is not what the default does). (2) No StreamDistribution
/// source, and not the shared <c>AddWolverineForContext</c> that StreamDistribution calls without
/// <c>configureMore</c>, mentions a handler policy, an exception rule or an attempt count.
/// </para>
///
/// <para>
/// Not pinned: the discovery in (1) is built from this test's own options, because the module's real call needs a
/// broker and a database at start, so (1) does not see the module's configuration. (2) is a source scan, so a
/// rule reached by an indirection it does not name is not seen.
/// </para>
///
/// <para>
/// Runs without a broker or a database for the reason <c>WallSceneSwitchFailurePolicyTests</c> gives:
/// <see cref="WolverineOptions.StubAllExternalTransports"/> and no Postgres persistence.
/// </para>
/// </summary>
public partial class CameraEventFailurePolicyCharacterisationTests
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

    [Theory]
    [InlineData(typeof(CameraRegisteredV1))]
    [InlineData(typeof(CameraAddressChangedV1))]
    public async Task A_refused_address_is_moved_to_the_error_queue_on_every_attempt_without_a_retry(Type messageType)
    {
        var graph = await DiscoverAsync();
        var chain = graph.ChainFor(messageType);
        chain.ShouldNotBeNull();
        var rules = Combine(chain.Failures, graph.Failures);

        List<string> continuations = [];
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Envelope envelope = new() { Attempts = attempt };
            var continuation = Determine(rules, new InvalidOperationException(), envelope);
            continuations.Add(continuation.GetType().Name);
        }

        continuations.ShouldBe(
            ["MoveToErrorQueue", "MoveToErrorQueue", "MoveToErrorQueue"],
            "with no rule, Wolverine dead-letters a failed message on its first attempt, so a replay of a refused address ends there");
    }

    [Fact]
    public void StreamDistribution_registers_no_wolverine_failure_rule()
    {
        var root = RepositoryRoot();
        var offenders = SourcesToScan(root)
            .Where(file => ForbiddenRegistration().IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .ToList();

        offenders.ShouldBeEmpty("a failure rule would replace the default for the two camera event handlers too");
    }

    // Both are internal to Wolverine and are what its own pipeline calls to pick the continuation for a failed
    // attempt; there is no public way to ask a chain what it would do. A rename in Wolverine fails these tests loudly.
    private static FailureRuleCollection Combine(FailureRuleCollection chainRules, FailureRuleCollection graphRules) =>
        (FailureRuleCollection)Internal("CombineRules").Invoke(chainRules, [graphRules])!;

    private static IContinuation Determine(FailureRuleCollection rules, Exception exception, Envelope envelope) =>
        (IContinuation)Internal("DetermineExecutionContinuation").Invoke(rules, [exception, envelope])!;

    private static MethodInfo Internal(string name) =>
        typeof(FailureRuleCollection).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Wolverine no longer has FailureRuleCollection.{name}.");

    private static IEnumerable<string> SourcesToScan(string root)
    {
        var streamDistribution = Directory
            .EnumerateFiles(Path.Combine(root, "src", "StreamDistribution"), "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                var normalised = file.Replace('\\', '/');
                return !normalised.Contains("/obj/") && !normalised.Contains("/bin/");
            });

        return streamDistribution.Append(Path.Combine(root, "src", "ServiceDefaults", "WolverineDefaults.cs"));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartSentinelEye.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(@"Policies\s*\.\s*Add\b|OnException\b|OnAnyException\b|MaximumAttempts\b|IHandlerPolicy\b")]
    private static partial Regex ForbiddenRegistration();
}
