using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber that flips a webhook integration's
/// bearer-validation path from legacy hash-compare to Keycloak-JWT
/// validate (spec 008 FR-016). Idempotent: a replay or at-least-once
/// re-delivery against an already-rotated integration carrying the
/// same clientId is a no-op.
/// </summary>
public sealed class WebhookIntegrationRotatedV1Handler(IWebhookIntegrationRepository integrations, IClock clock, ILogger<WebhookIntegrationRotatedV1Handler> logger)
{
    public async Task Handle(WebhookIntegrationRotatedV1 message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();

        var (integrationName, clientId, _, metadata) = message;

        WebhookIntegrationName name;
        try
        {
            name = WebhookIntegrationName.From(integrationName);
        }
        catch (ArgumentException ex)
        {
            logger.InvalidRotationName(ex, integrationName);
            return;
        }

        Option<FabIdentifier> fab = ParseFab(metadata, integrationName);
        if (!fab.HasValue)
        {
            return;
        }

        // AS-5/AS-6 (spec 182): scoped so an integration registered in
        // another fab is never materialised — a first rotation in one fab
        // cannot take over an unrotated integration registered in another.
        Option<WebhookIntegration> found = await integrations.GetWithinFabAsync(fab.Value, name, cancellationToken);
        if (!found.HasValue)
        {
            await LogRefusalAsync(name, integrationName, fab.Value, cancellationToken);
            return;
        }

        WebhookIntegration integration = found.Value;
        integration.MarkAsRotated(KeycloakClientIdentifier.From(clientId), clock);
        await integrations.SaveAsync(cancellationToken);

        logger.WebhookIntegrationFlippedToJwt(integrationName, clientId);
    }

    /// <summary>
    /// A null or unparsable fab is refused rather than resolved by name
    /// alone: this handler mutates a security-relevant validation mode.
    /// <c>metadata</c> is read through <c>?.</c> even though
    /// <see cref="EventMetadata"/> is declared non-nullable on the contract
    /// — a deserialized message missing <c>Metadata</c> yields a genuine
    /// <see langword="null"/> at runtime regardless of that annotation
    /// (mirrors <c>SystemVariableValueRequestedV1Handler.Handle</c>).
    /// </summary>
    private Option<FabIdentifier> ParseFab(EventMetadata metadata, string integrationName)
    {
        if (string.IsNullOrWhiteSpace(metadata?.Fab))
        {
            logger.RotationFabInvalid(exception: null, integrationName, "(none)");
            return Option<FabIdentifier>.None;
        }

        try
        {
            return Option<FabIdentifier>.Some(FabIdentifier.From(metadata.Fab));
        }
        catch (ArgumentException ex)
        {
            logger.RotationFabInvalid(ex, integrationName, metadata.Fab);
            return Option<FabIdentifier>.None;
        }
    }

    /// <summary>
    /// The scoped lookup above already refused the mutation; this only picks
    /// the signal. A plain read against the unscoped name — a lookup this
    /// handler is already entitled to, and one that never reaches the caller
    /// — distinguishes "no such integration anywhere" (benign, e.g. a replay
    /// against a deleted integration) from "exists, in a different fab" (the
    /// actual cross-fab attempt), so the latter reaches Warning instead of
    /// being indistinguishable from the former at Information. The victim's
    /// real fab is still never logged — only the caller's own claimed one —
    /// so this does not reopen the enumeration oracle AS-4/AS-9 close.
    /// </summary>
    private async Task LogRefusalAsync(
        WebhookIntegrationName name, string integrationName, FabIdentifier fab, CancellationToken cancellationToken)
    {
        Option<WebhookIntegration> existsInAnyFab = await integrations.GetByNameAsync(name, cancellationToken);
        if (existsInAnyFab.HasValue)
        {
            logger.RotationFabMismatch(integrationName, fab.Value);
        }
        else
        {
            logger.RotationTargetMissing(integrationName);
        }
    }
}
