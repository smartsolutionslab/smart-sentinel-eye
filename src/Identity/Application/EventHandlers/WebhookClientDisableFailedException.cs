namespace SmartSentinelEye.Identity.Application.EventHandlers;

/// <summary>
/// Thrown by <see cref="WebhookIntegrationRevokedIntegrationEventHandler"/> when
/// <c>DisableWebhookClientCommand</c> fails for a reason other than
/// <c>WebhookClientNotFound</c> — in practice, Keycloak unavailable (spec 328, #2629).
/// A dedicated type lets <c>WebhookIntegrationRevokedFailurePolicy</c> single out
/// exactly this failure for the retry-with-cooldown ladder, rather than Wolverine's
/// default three-attempt budget catching every exception on the chain.
///
/// <para>
/// Derives <see cref="InvalidOperationException"/> — the type the handler threw
/// before this change — so every existing observer, including
/// <c>KeycloakUnavailable_throws_so_Wolverine_retries</c>, stays true unmodified.
/// </para>
/// </summary>
public sealed class WebhookClientDisableFailedException(string integrationName, string errorCode)
    : InvalidOperationException($"DisableWebhookClientCommand failed for '{integrationName}': {errorCode}");
