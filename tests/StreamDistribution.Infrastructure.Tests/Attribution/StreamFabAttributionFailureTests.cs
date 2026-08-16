using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.StreamDistribution.Infrastructure.Attribution;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Attribution;

/// <summary>
/// Spec 016 T023 — the failure path, asserted because it is chosen rather
/// than inherited.
///
/// <para>
/// plan.md §III flagged that folding attribution into
/// <c>MediaMtxReconciler</c> would have it adopt that class's existing
/// <c>try/catch</c> by accident. It is a separate service precisely so this
/// decision is made on its own terms: a CameraCatalog that is unreachable, or
/// refuses the token, must leave streams unattributed and let the host start.
/// Those streams are then visible to nobody, which is FR-009 working rather
/// than a second failure — and video keeps flowing throughout.
/// </para>
/// </summary>
public class StreamFabAttributionFailureTests
{
    [Fact]
    public async Task An_unreachable_camera_catalog_does_not_block_host_start()
    {
        StreamFabAttributionService service = new(
            new ThrowingScopeFactory(new HttpRequestException("camera-catalog unreachable")),
            NullLogger<StreamFabAttributionService>.Instance);

        // No throw: StartAsync completing is the host starting.
        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_refused_token_does_not_block_host_start()
    {
        StreamFabAttributionService service = new(
            new ThrowingScopeFactory(new InvalidOperationException("Keycloak returned an empty token response.")),
            NullLogger<StreamFabAttributionService>.Instance);

        await service.StartAsync(CancellationToken.None);
    }

    /// <summary>
    /// Cancellation is the one thing not swallowed — a shutdown mid-pass is
    /// the host stopping, not an attribution failure, and reporting it as one
    /// would put a warning in the log every time the service is restarted.
    /// </summary>
    [Fact]
    public async Task A_cancelled_pass_is_not_reported_as_a_failure()
    {
        StreamFabAttributionService service = new(
            new ThrowingScopeFactory(new OperationCanceledException()),
            NullLogger<StreamFabAttributionService>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(
            () => service.StartAsync(CancellationToken.None));
    }

    private sealed class ThrowingScopeFactory(Exception failure) : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public IServiceProvider ServiceProvider => this;

        public IServiceScope CreateScope() => this;

        public object GetService(Type serviceType) => throw failure;

        public void Dispose()
        {
        }
    }
}
