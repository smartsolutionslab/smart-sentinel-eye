using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

/// <summary>
/// Aggregate root for a registered webhook integration (spec 006
/// FR-023). Token plaintext is returned exactly once from
/// <see cref="Register"/>; storage is SHA-256 hashed.
/// </summary>
public sealed class WebhookIntegration : AggregateRoot<WebhookIntegrationIdentifier>
{
    public WebhookIntegrationName Name { get; private set; } = null!;

    public Kind DefaultKind { get; private set; } = null!;

    public BearerTokenHash TokenHash { get; private set; } = null!;

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    private WebhookIntegration() { }

    /// <summary>
    /// Mints a new integration + returns the plaintext token (shown
    /// to the caller exactly once; the aggregate stores only the
    /// hash). Raises <see cref="WebhookIntegrationRegisteredDomainEvent"/>.
    /// </summary>
    public static (WebhookIntegration integration, string plainToken) Register(
        WebhookIntegrationName name,
        Kind defaultKind,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(defaultKind);
        ArgumentNullException.ThrowIfNull(clock);

        (BearerTokenHash hash, string plaintext) = BearerTokenHash.Generate();
        DateTimeOffset now = clock.UtcNow;
        WebhookIntegration integration = new()
        {
            Id = WebhookIntegrationIdentifier.New(),
            Name = name,
            DefaultKind = defaultKind,
            TokenHash = hash,
            RegisteredAt = now,
            RevokedAt = null,
        };
        integration.Raise(new WebhookIntegrationRegisteredDomainEvent(name, defaultKind, now));
        return (integration, plaintext);
    }

    public bool IsRevoked => RevokedAt.HasValue;

    public void Revoke(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (IsRevoked) return; // idempotent
        RevokedAt = clock.UtcNow;
        Raise(new WebhookIntegrationRevokedDomainEvent(Name, RevokedAt.Value));
    }
}
