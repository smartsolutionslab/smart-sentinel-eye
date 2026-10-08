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
public sealed class FakeWebhookIntegrationStatusLookup : IWebhookIntegrationStatusLookup
{
    public WebhookIntegrationStatus Status { get; set; } = WebhookIntegrationStatus.Active;

    public List<(FabIdentifier Fab, string IntegrationName)> Calls { get; } = [];

    public Task<WebhookIntegrationStatus> GetStatusAsync(
        FabIdentifier fab, string integrationName, CancellationToken cancellationToken)
    {
        Calls.Add((fab, integrationName));
        return Task.FromResult(Status);
    }
}
