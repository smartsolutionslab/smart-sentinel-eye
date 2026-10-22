using System.Globalization;
using System.Reflection;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.EventHandlers;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;
using StreamAggregate = SmartSentinelEye.StreamDistribution.Domain.Stream.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests;

/// <summary>
/// Spec 436 / ADR-0182, red first (#2783): a retire that loses the optimistic-concurrency race to the health
/// sweep is retried on freshly loaded state instead of dead-lettered on its first attempt.
///
/// <para>
/// Pinned: (A) the <c>CameraRetiredV1</c> chain answers <see cref="DbUpdateConcurrencyException"/> with
/// inline retries of 100 ms, 500 ms and 2 s and only then the error queue; (B) any other exception on that chain,
/// and (C) the same exception on the two other camera event chains, still go to the error queue on the first
/// attempt; (D) each retry runs the handler with a new scope, so the second attempt's repository is a new
/// instance that loads again and saves a <c>Retired</c> stream.
/// </para>
///
/// <para>
/// Not pinned: the host is built from this test's own options (the module's real Wolverine call needs a broker and a
/// database), and <c>InvokeAsync</c> runs <c>Executor.InvokeAsync</c>'s inline loop whereas the listener path runs
/// <c>ExecuteAsync</c>; both share <c>Handler.HandleAsync</c> and the rule set, and (A) pins the latter.
/// </para>
/// </summary>
public class RetireStreamFailurePolicyTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-08-24T11:00:00Z", CultureInfo.InvariantCulture);

    private static IHost BuildHost(Action<IServiceCollection>? configureServices = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        configureServices?.Invoke(builder.Services);
        builder.UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(CameraRegisteredIntegrationEventHandler).Assembly);
            opts.Policies.Add<RetireStreamFailurePolicy>();
            opts.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
            opts.StubAllExternalTransports();
        });

        return builder.Build();
    }

    private static async Task<HandlerGraph> DiscoverAsync()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var graph = host.Services.GetRequiredService<HandlerGraph>();
        await host.StopAsync();
        return graph;
    }

    private static IContinuation ContinuationFor(HandlerGraph graph, Type messageType, Exception exception, int attempt)
    {
        var chain = graph.ChainFor(messageType);
        chain.ShouldNotBeNull();
        var rules = (FailureRuleCollection)Internal("CombineRules").Invoke(chain.Failures, [graph.Failures])!;
        Envelope envelope = new() { Attempts = attempt };
        return (IContinuation)Internal("DetermineExecutionContinuation").Invoke(rules, [exception, envelope])!;
    }

    private static MethodInfo Internal(string name) =>
        typeof(FailureRuleCollection).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Wolverine no longer has FailureRuleCollection.{name}.");

    private static TimeSpan DelayOf(IContinuation continuation)
    {
        continuation.GetType().Name.ShouldBe("RetryInlineContinuation");
        var delay = continuation.GetType().GetProperty(
                "Delay",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RetryInlineContinuation no longer exposes Delay.");
        return (TimeSpan)delay.GetValue(continuation)!;
    }

    [Fact]
    public async Task A_lost_concurrency_race_on_a_retire_is_retried_after_100_ms_500_ms_and_2_s_then_dead_lettered()
    {
        var graph = await DiscoverAsync();

        var first = ContinuationFor(graph, typeof(CameraRetiredV1), new DbUpdateConcurrencyException(), 1);
        var second = ContinuationFor(graph, typeof(CameraRetiredV1), new DbUpdateConcurrencyException(), 2);
        var third = ContinuationFor(graph, typeof(CameraRetiredV1), new DbUpdateConcurrencyException(), 3);
        var fourth = ContinuationFor(graph, typeof(CameraRetiredV1), new DbUpdateConcurrencyException(), 4);

        DelayOf(first).ShouldBe(TimeSpan.FromMilliseconds(100));
        DelayOf(second).ShouldBe(TimeSpan.FromMilliseconds(500));
        DelayOf(third).ShouldBe(TimeSpan.FromSeconds(2));
        fourth.GetType().Name.ShouldBe("MoveToErrorQueue", "after the ladder the default dead-letter applies");
    }

    [Fact]
    public async Task An_unreachable_gateway_on_a_retire_is_still_dead_lettered_on_the_first_attempt()
    {
        var graph = await DiscoverAsync();

        ContinuationFor(graph, typeof(CameraRetiredV1), new InvalidOperationException(), 1)
            .GetType().Name.ShouldBe("MoveToErrorQueue");
    }

    [Theory]
    [InlineData(typeof(CameraRegisteredV1))]
    [InlineData(typeof(CameraAddressChangedV1))]
    public async Task A_lost_concurrency_race_on_the_other_camera_events_is_still_dead_lettered_on_the_first_attempt(
        Type messageType)
    {
        var graph = await DiscoverAsync();

        ContinuationFor(graph, messageType, new DbUpdateConcurrencyException(), 1)
            .GetType().Name.ShouldBe("MoveToErrorQueue");
    }

    [Fact]
    public async Task A_retire_that_loses_the_race_is_retried_with_a_new_repository_that_reloads_and_saves_the_retired_stream()
    {
        Script script = new(failingSaves: 1);
        FakeGateway gateway = new();

        using var host = BuildHost(services => Register(services, script, gateway));
        await host.StartAsync();

        await host.Services.GetRequiredService<IMessageBus>().InvokeAsync(Retired(script.Camera));

        script.Repositories.Count.ShouldBe(2, "each attempt runs in a new scope with its own repository");
        script.Repositories.ShouldAllBe(repository => repository.Loads == 1);
        script.Repositories[0].SavedState.ShouldBe(StreamState.Retired);
        script.Repositories[1].SavedState.ShouldBe(StreamState.Retired);
        script.Repositories[1].SaveSucceeded.ShouldBeTrue();
        gateway.Removed.Count.ShouldBe(1, "the path is removed once, by the attempt that committed");
    }

    [Fact]
    public async Task A_retire_that_keeps_losing_the_race_throws_after_four_attempts_each_on_its_own_repository()
    {
        Script script = new(failingSaves: int.MaxValue);
        FakeGateway gateway = new();

        using var host = BuildHost(services => Register(services, script, gateway));
        await host.StartAsync();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => host.Services.GetRequiredService<IMessageBus>().InvokeAsync(Retired(script.Camera)));

        script.Repositories.Count.ShouldBe(4);
        gateway.Removed.ShouldBeEmpty();
    }

    private static CameraRetiredV1 Retired(CameraIdentifier camera) =>
        new(
            camera.Value,
            "munich",
            "Gate 1",
            Moment,
            Guid.CreateVersion7(),
            new EventMetadata(Guid.CreateVersion7(), Moment, "munich", null));

    private static void Register(IServiceCollection services, Script script, FakeGateway gateway) =>
        services
            .AddSingleton(script)
            .AddSingleton<IRtspGateway>(gateway)
            .AddSingleton<IClock, FixedClock>()
            .AddScoped<IStreamRepository, ScriptedRepository>()
            .AddScoped<
                ICommandHandler<RetireStreamCommand, Result<Option<StreamIdentifier>, RetireStreamError>>,
                RetireStreamCommandHandler>()
            .AddScoped<CameraRetiredIntegrationEventHandler>();

    public sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Moment;
    }

    public sealed class Script(int failingSaves)
    {
        public CameraIdentifier Camera { get; } = CameraIdentifier.From(Guid.CreateVersion7());

        public List<ScriptedRepository> Repositories { get; } = [];

        public int Saves { get; set; }

        public int FailingSaves { get; } = failingSaves;
    }

    public sealed class FakeGateway : IRtspGateway
    {
        public List<MediaMtxPath> Removed { get; } = [];

        public Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken)
        {
            Removed.Add(path);
            return Task.CompletedTask;
        }

        public Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>> GetAllPathsHealthAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<MediaMtxPath, string>> ListConfiguredPathSourcesAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// One instance per scope, loading a fresh Healthy stream from the shared seed; its save consults the shared script.
    /// </summary>
    public sealed class ScriptedRepository : IStreamRepository
    {
        private readonly Script script;
        private StreamAggregate? loaded;

        public ScriptedRepository(Script script)
        {
            this.script = script;
            script.Repositories.Add(this);
        }

        public int Loads { get; private set; }

        public StreamState? SavedState { get; private set; }

        public bool SaveSucceeded { get; private set; }

        public Task<Option<StreamAggregate>> GetByCameraAsync(
            CameraIdentifier camera,
            CancellationToken cancellationToken)
        {
            Loads++;
            loaded = new StreamBuilder().ForCamera(camera).At(Moment).Build();
            return Task.FromResult(Option<StreamAggregate>.Some(loaded));
        }

        public Task SaveAsync(CancellationToken cancellationToken)
        {
            SavedState = loaded?.State;
            script.Saves++;

            if (script.Saves <= script.FailingSaves)
            {
                throw new DbUpdateConcurrencyException("the health sweep saved first");
            }

            SaveSucceeded = true;
            return Task.CompletedTask;
        }

        public Task<Option<StreamAggregate>> GetByIdentifierAsync(
            StreamIdentifier stream,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Option<StreamAggregate>> GetByPathAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(StreamAggregate stream) => throw new NotSupportedException();

        public Task<bool> IsUnchangedSinceLoadAsync(StreamAggregate stream, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StreamState> ReadCommittedStateAsync(StreamIdentifier stream, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
