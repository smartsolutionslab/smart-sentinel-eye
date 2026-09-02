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

    /// <summary>
    /// The plant this integration belongs to (spec 018 amendment, #1545). Set
    /// at registration from the registering operator's own fab and never
    /// reassigned — an integration's plant is the plant of whoever created it,
    /// the same rule a stream's fab follows.
    ///
    /// <para>
    /// Spec 018 originally deferred this (FR-016) on the reasoning that the
    /// per-delivery credential check already proved entitlement. Review found
    /// that it does so only in <see cref="BearerValidationMode.Jwt"/>: the
    /// <see cref="BearerValidationMode.StaticHash"/> path matched the token and
    /// never looked at the fab, so a token issued for one plant could file
    /// events into another. There was nothing to compare against. This is it.
    /// </para>
    /// </summary>
    public FabIdentifier Fab { get; private set; } = null!;

    public Kind DefaultKind { get; private set; } = null!;

    public BearerTokenHash TokenHash { get; private set; } = null!;

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>
    /// How <c>IngestWebhook</c> validates incoming bearers for this
    /// integration. <see cref="BearerValidationMode.StaticHash"/>
    /// until <see cref="MarkAsRotated"/> flips it to
    /// <see cref="BearerValidationMode.Jwt"/> (spec 008 FR-016).
    /// </summary>
    public BearerValidationMode ValidationMode { get; private set; } = BearerValidationMode.StaticHash;

    /// <summary>
    /// Keycloak <c>clientId</c> backing this integration after a
    /// rotation. <c>null</c> while the integration is still on the
    /// legacy hash-compare path.
    /// </summary>
    public string? KeycloakClientId { get; private set; }

    public DateTimeOffset? RotatedAt { get; private set; }

    private WebhookIntegration() { }

    /// <summary>
    /// Mints a new integration + returns the plaintext token (shown
    /// to the caller exactly once; the aggregate stores only the
    /// hash). Raises <see cref="WebhookIntegrationRegisteredDomainEvent"/>.
    /// </summary>
    public static (WebhookIntegration integration, string plainToken) Register(
        WebhookIntegrationName name,
        FabIdentifier fab,
        Kind defaultKind,
        IClock clock)
    {
        Ensure.That(name).IsNotNull();
        Ensure.That(fab).IsNotNull();
        Ensure.That(defaultKind).IsNotNull();
        Ensure.That(clock).IsNotNull();

        (BearerTokenHash hash, string plaintext) = BearerTokenHash.Generate();
        DateTimeOffset now = clock.UtcNow;
        WebhookIntegration integration = new()
        {
            Id = WebhookIntegrationIdentifier.New(),
            Name = name,
            Fab = fab,
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
        Ensure.That(clock).IsNotNull();
        if (IsRevoked)
        {
            return; // idempotent
        }

        RevokedAt = clock.UtcNow;
        Raise(new WebhookIntegrationRevokedDomainEvent(Name, RevokedAt.Value));
    }

    /// <summary>
    /// Flips this integration onto the Keycloak-JWT validation path
    /// (spec 008 FR-016). Subsequent ingest calls must present a
    /// valid access token for <paramref name="keycloakClientId"/>.
    /// Idempotent on the same clientId; replays from the outbox or
    /// at-least-once delivery are absorbed silently.
    /// </summary>
    public void MarkAsRotated(string keycloakClientId, IClock clock)
    {
        Ensure.That(keycloakClientId).IsNotNull().IsNotNullOrWhiteSpace();
        Ensure.That(clock).IsNotNull();

        if (ValidationMode == BearerValidationMode.Jwt &&
            string.Equals(KeycloakClientId, keycloakClientId, StringComparison.Ordinal))
        {
            return; // idempotent
        }

        ValidationMode = BearerValidationMode.Jwt;
        KeycloakClientId = keycloakClientId;
        RotatedAt = clock.UtcNow;
        Raise(new WebhookIntegrationRotatedDomainEvent(Name, keycloakClientId, RotatedAt.Value));
    }
}
