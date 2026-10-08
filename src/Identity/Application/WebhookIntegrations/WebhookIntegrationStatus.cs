namespace SmartSentinelEye.Identity.Application.WebhookIntegrations;

/// <summary>
/// What EventIngestion reports about a webhook integration, as seen by a
/// rotation (spec 318, issue #2628).
///
/// <para>
/// <see cref="Unverifiable"/> is a value, not an exception. Turning a
/// transport failure, a non-2xx answer, a resilience timeout or malformed
/// JSON into this value lets the fail-closed decision live in the handler's
/// own reviewable code — a <c>switch</c> — rather than a catch filter that
/// has to tell a Polly <c>TimeoutRejectedException</c> apart from a caller
/// cancellation (plan §4).
/// </para>
/// </summary>
public enum WebhookIntegrationStatus
{
    /// <summary>Registered in this fab and not revoked.</summary>
    Active,

    /// <summary>Registered in this fab and revoked.</summary>
    Revoked,

    /// <summary>No integration by this name is registered in this fab.</summary>
    NotRegistered,

    /// <summary>
    /// EventIngestion's answer could not be trusted (unreachable, non-2xx,
    /// a resilience timeout, or a body missing the fields the adapter reads).
    /// </summary>
    Unverifiable,
}
