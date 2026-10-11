using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// ADR-0171 security review. <c>IHostedService.StartAsync</c> runs before
/// Kestrel binds its listener (<c>WebApplicationBuilder</c>'s documented
/// ordering), and since ADR-0171 every one of <see cref="MediaMtxReconciler"/>'s
/// own gateway calls authorizes through a callback to this same host's
/// <c>/streams/authorize</c> — so a reconcile pass run inline inside
/// <c>StartAsync</c> would have MediaMTX call back to a port nothing is
/// listening on yet, fail, and be swallowed by the pass's own catch block,
/// silently no-opping on every boot.
///
/// <para>
/// <see cref="MediaMtxReconcilerIntegrationTests"/> cannot catch this: it
/// calls <see cref="MediaMtxReconciler.ReconcileOnceAsync"/> directly and
/// never exercises <see cref="MediaMtxReconciler.StartAsync"/> at all. This
/// file is the one that does — against a fake
/// <see cref="IHostApplicationLifetime"/> rather than a real Kestrel, since
/// the property under test is the ordering itself, not anything the real
/// listener would add.
/// </para>
/// </summary>
public class MediaMtxReconcilerStartupOrderingTests
{
    [Fact]
    public async Task StartAsync_returns_without_running_the_reconcile_pass()
    {
        ScopeCreationProbe scopeFactory = new();
        using FakeHostApplicationLifetime lifetime = new();
        MediaMtxReconciler reconciler = new(scopeFactory, lifetime, NullLogger<MediaMtxReconciler>.Instance);

        Task startAsync = reconciler.StartAsync(CancellationToken.None);

        (await Task.WhenAny(startAsync, Task.Delay(TimeSpan.FromSeconds(2)))).ShouldBe(
            startAsync, "StartAsync must return immediately rather than block on the reconcile pass");
        await startAsync;

        scopeFactory.Created.ShouldBeFalse(
            "the reconcile pass must not have run yet — ApplicationStarted has not fired");
    }

    [Fact]
    public async Task The_reconcile_pass_runs_once_ApplicationStarted_fires()
    {
        ScopeCreationProbe scopeFactory = new();
        using FakeHostApplicationLifetime lifetime = new();
        MediaMtxReconciler reconciler = new(scopeFactory, lifetime, NullLogger<MediaMtxReconciler>.Instance);

        await reconciler.StartAsync(CancellationToken.None);
        lifetime.FireApplicationStarted();

        await scopeFactory.WaitForScopeCreationAsync(TimeSpan.FromSeconds(2));

        scopeFactory.Created.ShouldBeTrue(
            "the reconcile pass must run once ApplicationStarted fires, not stay deferred forever");
    }

    /// <summary>
    /// A failure deferred to <c>ApplicationStarted</c> must still be swallowed,
    /// exactly as it was when the pass ran inline — this fix changes when the
    /// pass runs, not what happens when it fails.
    /// </summary>
    [Fact]
    public async Task A_reconcile_failure_after_ApplicationStarted_is_swallowed_not_thrown()
    {
        ScopeCreationProbe scopeFactory = new();
        using FakeHostApplicationLifetime lifetime = new();
        MediaMtxReconciler reconciler = new(scopeFactory, lifetime, NullLogger<MediaMtxReconciler>.Instance);

        await reconciler.StartAsync(CancellationToken.None);

        // The registered ApplicationStarted callback runs synchronously on
        // this thread when the token is cancelled (CancellationTokenSource's
        // default), and the probe's CreateScope throws synchronously too — so
        // by the time FireApplicationStarted returns, the whole deferred pass
        // has already run and its failure already been caught. Asserting that
        // nothing escaped this call is the proof, not an assumption.
        Should.NotThrow(lifetime.FireApplicationStarted);

        await scopeFactory.WaitForScopeCreationAsync(TimeSpan.FromSeconds(2));
        scopeFactory.Created.ShouldBeTrue();
    }

    /// <summary>
    /// Throws from <see cref="IServiceScopeFactory.CreateScope"/> itself —
    /// synchronously, before any real async work — so a test observing
    /// <see cref="Created"/> right after firing the lifetime token sees the
    /// true result of that single call stack rather than a race against a
    /// background continuation.
    /// </summary>
    private sealed class ScopeCreationProbe : IServiceScopeFactory
    {
        private readonly TaskCompletionSource<bool> createdSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Created { get; private set; }

        public Task<bool> WaitForScopeCreationAsync(TimeSpan timeout) =>
            createdSignal.Task.WaitAsync(timeout);

        public IServiceScope CreateScope()
        {
            Created = true;
            createdSignal.TrySetResult(true);
            throw new InvalidOperationException("probe: the reconcile pass reached the scope factory");
        }
    }

    /// <summary>
    /// A minimal, real <see cref="IHostApplicationLifetime"/> rather than a
    /// mock: the production code calls <c>.Register(...)</c> on
    /// <see cref="ApplicationStarted"/>, which needs a real
    /// <see cref="CancellationToken"/> backing it, not a stubbed property.
    /// </summary>
    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource started = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly CancellationTokenSource stopped = new();

        public CancellationToken ApplicationStarted => started.Token;

        public CancellationToken ApplicationStopping => stopping.Token;

        public CancellationToken ApplicationStopped => stopped.Token;

        public void FireApplicationStarted() => started.Cancel();

        public void StopApplication() => stopping.Cancel();

        public void Dispose()
        {
            started.Dispose();
            stopping.Dispose();
            stopped.Dispose();
        }
    }
}
