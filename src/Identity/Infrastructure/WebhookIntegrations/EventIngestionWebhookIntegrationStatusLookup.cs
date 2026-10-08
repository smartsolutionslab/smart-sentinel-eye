using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.WebhookIntegrations;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure.WebhookIntegrations;

/// <summary>
/// Asks EventIngestion whether a webhook integration is revoked, over its
/// already-published <c>GET /webhook-integrations</c> (spec 318, #2628, plan
/// §2) — the bounded cross-context exception spec 017 plan §III established
/// for LayoutComposition→CameraCatalog, applied here for
/// Identity→EventIngestion. No shared DTO: as <c>CameraCatalogFabGuard</c>
/// does, rows are read as <see cref="JsonElement"/> by camelCase property
/// name, so a missing field fails this lookup rather than a broader contract.
///
/// <para>
/// Everything that is not a clean <c>200</c> with the two fields this adapter
/// reads — a non-2xx answer, a transport failure, a resilience timeout, or a
/// malformed body — answers <see cref="WebhookIntegrationStatus.Unverifiable"/>
/// rather than throwing, so the fail-closed decision lives in the handler's
/// own reviewable <c>switch</c> (plan §4). Only the caller's own cancellation
/// propagates.
/// </para>
/// </summary>
public sealed class EventIngestionWebhookIntegrationStatusLookup(
    HttpClient httpClient, ILogger<EventIngestionWebhookIntegrationStatusLookup> logger)
    : IWebhookIntegrationStatusLookup
{
    public async Task<WebhookIntegrationStatus> GetStatusAsync(
        FabIdentifier fab, string integrationName, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(integrationName).IsNotNull();

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                $"/webhook-integrations?fabId={fab.Value}&includeRevoked=true", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.WebhookIntegrationStatusUnverifiable(
                    integrationName, fab, $"HTTP {(int)response.StatusCode}", exception: null);
                return WebhookIntegrationStatus.Unverifiable;
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseStatus(body, integrationName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Covers HttpRequestException (unreachable), a resilience-policy
            // TaskCanceledException that is not the caller's own cancellation,
            // and every JSON-shape failure ParseStatus can raise.
            logger.WebhookIntegrationStatusUnverifiable(integrationName, fab, ex.GetType().Name, ex);
            return WebhookIntegrationStatus.Unverifiable;
        }
    }

    private static WebhookIntegrationStatus ParseStatus(string body, string integrationName)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("EventIngestion's webhook-integrations response was not a JSON array.");
        }

        bool matched = false;
        bool revoked = false;
        foreach (JsonElement row in document.RootElement.EnumerateArray())
        {
            string name = row.GetProperty("name").GetString()
                ?? throw new JsonException("A webhook-integrations row's 'name' was JSON null.");
            JsonElement revokedAt = row.GetProperty("revokedAt");

            if (!string.Equals(name, integrationName, StringComparison.Ordinal))
            {
                continue;
            }

            matched = true;
            revoked |= revokedAt.ValueKind != JsonValueKind.Null;
        }

        if (!matched)
        {
            return WebhookIntegrationStatus.NotRegistered;
        }

        // Names are globally unique (EventIngestion's own index), revoked rows
        // included, so at most one row can match. If duplicates ever appeared,
        // picking Revoked when any matching row is revoked is still the closed
        // direction (plan §2).
        return revoked ? WebhookIntegrationStatus.Revoked : WebhookIntegrationStatus.Active;
    }
}
