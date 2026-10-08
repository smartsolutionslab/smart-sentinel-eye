using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.WebhookIntegrations;

/// <summary>
/// Asks EventIngestion whether a webhook integration is revoked, over its
/// already-published HTTP API (spec 318, plan §2) — the bounded
/// cross-context exception spec 017 plan §III established for
/// LayoutComposition→CameraCatalog, applied here for Identity→EventIngestion.
/// No project reference crosses the boundary; the implementation lives in
/// Identity.Infrastructure and speaks HTTP by Aspire resource name.
/// </summary>
public interface IWebhookIntegrationStatusLookup
{
    Task<WebhookIntegrationStatus> GetStatusAsync(
        FabIdentifier fab, string integrationName, CancellationToken cancellationToken);
}
