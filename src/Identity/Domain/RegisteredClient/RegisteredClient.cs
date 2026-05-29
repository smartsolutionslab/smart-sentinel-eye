using SmartSentinelEye.Identity.Domain.RegisteredClient.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Domain.RegisteredClient;

/// <summary>
/// Local audit + dedup record mirroring a Keycloak service-account
/// client (spec 008). Keycloak remains the system of record for
/// the credentials themselves; this aggregate carries enough state
/// to (a) prevent duplicate <c>(ClientKind, ClientId)</c>
/// registrations and (b) reconstruct who registered what when.
///
/// <para>
/// State machine: <c>Active</c> ↔ <c>Disabled</c>. <c>Rotate</c>
/// stays in <c>Active</c> and is only valid for
/// <see cref="ClientKind.WebhookIntegration"/> (FR-014).
/// </para>
/// </summary>
public sealed class RegisteredClient : AggregateRoot<RegisteredClientIdentifier>
{
    public ClientId ClientId { get; private set; } = null!;

    public ClientKind Kind { get; private set; } = null!;

    public FabIdentifier Fab { get; private set; } = null!;

    public DateTimeOffset RegisteredAt { get; private set; }

    public OperatorIdentifier RegisteredBy { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }

    public DateTimeOffset? LastRotatedAt { get; private set; }

    private RegisteredClient() { }

    /// <summary>
    /// Mints a new audit row in <c>Active</c> state. Raises
    /// <see cref="ClientRegisteredDomainEvent"/>.
    /// </summary>
    public static RegisteredClient Register(
        ClientId clientId,
        ClientKind kind,
        FabIdentifier fab,
        OperatorIdentifier registeredBy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(fab);
        ArgumentNullException.ThrowIfNull(clock);

        DateTimeOffset now = clock.UtcNow;
        RegisteredClient registered = new()
        {
            Id = RegisteredClientIdentifier.New(),
            ClientId = clientId,
            Kind = kind,
            Fab = fab,
            RegisteredAt = now,
            RegisteredBy = registeredBy,
        };
        registered.Raise(new ClientRegisteredDomainEvent(
            registered.Id, clientId, kind, fab, now, registeredBy));
        return registered;
    }

    /// <summary>
    /// Flips the row to <c>Disabled</c>. Idempotent —
    /// re-disabling is a no-op (no event raised). The actual
    /// Keycloak client is disabled by the command handler before
    /// this is invoked.
    /// </summary>
    public void Disable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (DisabledAt is not null) return;
        DisabledAt = clock.UtcNow;
        Raise(new ClientDisabledDomainEvent(Id, ClientId, DisabledAt.Value));
    }

    /// <summary>
    /// Records a credential rotation for a webhook integration
    /// (FR-014). Only valid while <c>Active</c> and on
    /// <see cref="ClientKind.WebhookIntegration"/>. Raises
    /// <see cref="ClientRotatedDomainEvent"/>.
    /// </summary>
    public void Rotate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (DisabledAt is not null)
        {
            throw new InvalidOperationException(
                $"Cannot rotate disabled client {Id}; re-register instead.");
        }
        if (Kind != ClientKind.WebhookIntegration)
        {
            throw new InvalidOperationException(
                $"Rotate is only valid for WebhookIntegration clients; got {Kind}.");
        }
        LastRotatedAt = clock.UtcNow;
        Raise(new ClientRotatedDomainEvent(Id, ClientId, LastRotatedAt.Value));
    }
}
