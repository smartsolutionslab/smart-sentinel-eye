using SmartSentinelEye.Identity.Application.WebhookIntegrations;
using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Fakes;

/// <summary>
/// Stands in for <see cref="IWebhookIntegrationStatusLookup"/> (spec 318).
/// Defaults to <see cref="WebhookIntegrationStatus.Active"/>, so every
/// existing construction site of <c>RotateWebhookClientCommandHandler</c>
/// keeps rotating exactly as it does today unless a test sets
/// <see cref="Status"/> explicitly.
/// </summary>
///
/// <para>
/// #2628: the create branch's own TOCTOU fix calls this lookup <b>twice</b> —
/// once pre-flight, once after its own commit — and a single fixed
/// <see cref="Status"/> cannot express "first call Active, second call
/// Revoked", so none of that race path was actually exercised at the unit
/// level. <see cref="EnqueueStatuses"/> lets a test queue one answer per
/// call; once the queue runs out, every further call repeats the last
/// dequeued value (falling back to <see cref="Status"/> if nothing was ever
/// queued, so every pre-existing single-status test is unaffected).
/// </para>
public sealed class FakeWebhookIntegrationStatusLookup : IWebhookIntegrationStatusLookup
{
    private readonly Queue<WebhookIntegrationStatus> queued = [];
    private WebhookIntegrationStatus? lastDequeued;

    public WebhookIntegrationStatus Status { get; set; } = WebhookIntegrationStatus.Active;

    public List<(FabIdentifier Fab, string IntegrationName)> Calls { get; } = [];

    /// <summary>
    /// Queues the answers successive <see cref="GetStatusAsync"/> calls return,
    /// in order — e.g. <c>EnqueueStatuses(Active, Revoked)</c> for "the
    /// pre-flight check passes, the post-commit re-check finds the race".
    /// </summary>
    public void EnqueueStatuses(params WebhookIntegrationStatus[] statuses)
    {
        foreach (WebhookIntegrationStatus status in statuses)
        {
            queued.Enqueue(status);
        }
    }

    public Task<WebhookIntegrationStatus> GetStatusAsync(
        FabIdentifier fab, string integrationName, CancellationToken cancellationToken)
    {
        Calls.Add((fab, integrationName));

        if (queued.Count > 0)
        {
            lastDequeued = queued.Dequeue();
        }

        return Task.FromResult(lastDequeued ?? Status);
    }
}
