using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Attribution;

/// <summary>
/// Spec 016 US3 — one-shot startup pass that gives a fab to streams
/// provisioned before this feature (FR-008, FR-010).
///
/// <para>
/// Separate from <see cref="Reconciler.MediaMtxReconciler"/> on purpose
/// (research.md §1). The reconciler makes MediaMTX's paths match the streams
/// table; this makes the streams table match the camera catalogue. Their
/// failures mean different things — a failed reconcile leaves video
/// unreconciled, a failed attribution leaves streams invisible to every
/// operator — and folding them together would give both the same
/// <c>try/catch</c>.
/// </para>
///
/// <para>
/// Nothing is ever guessed. A stream whose camera cannot be resolved keeps
/// its null fab and is counted as unresolved; defaulting it to the one fab
/// that happened to be live is the error this whole feature exists to avoid.
/// </para>
/// </summary>
public sealed class StreamFabAttributionService(
    IServiceScopeFactory scopeFactory,
    ILogger<StreamFabAttributionService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await AttributeOnceAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Chosen, not inherited (plan.md §III). An unreachable or refusing
            // CameraCatalog must not stop this host from serving video: the
            // streams it could not attribute stay null and are therefore
            // visible to nobody, which is FR-009 doing its job rather than a
            // second failure. The next restart retries.
            logger.AttributionPassFailed(ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task AttributeOnceAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        IDbContextFactory<StreamDistributionDbContext> factory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<StreamDistributionDbContext>>();
        ICameraFabLookup cameras = scope.ServiceProvider.GetRequiredService<ICameraFabLookup>();

        await using StreamDistributionDbContext context =
            await factory.CreateDbContextAsync(cancellationToken);

        List<Domain.Stream.Stream> unattributed = await context.Streams
            .Where(stream => stream.Fab == null)
            .ToListAsync(cancellationToken);

        if (unattributed.Count == 0)
        {
            // Silent on purpose: this is the steady state, reached after the
            // first pass and never left. A line per restart saying nothing
            // happened trains an operator to skip the one that matters.
            return;
        }

        IReadOnlyDictionary<Guid, string> fabsByCamera =
            await cameras.FabsByCameraAsync(cancellationToken);

        int attributed = Attribute(unattributed, fabsByCamera);

        await context.SaveChangesAsync(cancellationToken);

        // Both counts, always. An operator seeing an empty listing needs to
        // tell "attribution has not run" from "it ran and could not resolve
        // these" — FR-008 and FR-010 are one message because they answer one
        // question.
        logger.AttributionPassComplete(attributed, unattributed.Count - attributed);
    }

    /// <summary>
    /// Gives each stream the fab of its own camera, and returns how many were
    /// filled. A stream whose camera is not in the map keeps its null fab
    /// (FR-010) — it is never defaulted to whichever fab happened to be there.
    /// </summary>
    public static int Attribute(
        IReadOnlyCollection<Domain.Stream.Stream> unattributed,
        IReadOnlyDictionary<Guid, string> fabsByCamera)
    {
        int attributed = 0;
        foreach (Domain.Stream.Stream stream in unattributed)
        {
            if (!fabsByCamera.TryGetValue(stream.Camera.Value, out string? fab))
            {
                continue;
            }

            stream.AttributeToFab(FabIdentifier.From(fab));
            attributed++;
        }

        return attributed;
    }
}
