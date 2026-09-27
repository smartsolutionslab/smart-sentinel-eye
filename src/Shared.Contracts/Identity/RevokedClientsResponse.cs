namespace SmartSentinelEye.Shared.Contracts.Identity;

/// <summary>
/// HTTP response for Identity's <c>GET /registered-clients/revoked</c>
/// (spec 270, ADR-0160, FR-007/FR-008). Lists every disabled
/// <c>RegisteredClient</c> — every kind, every fab — with the latest
/// <c>DisabledAt</c> per client id. Consumed by each service's
/// revocation-snapshot refresher (<c>ServiceDefaults.Revocation</c>), which
/// polls this endpoint every 5 seconds and holds the result as an in-process
/// <c>RevokedClientSnapshot</c>.
/// </summary>
public sealed record RevokedClientsResponse(IReadOnlyList<RevokedClientEntry> Clients);

/// <summary>
/// One disabled client id and the latest instant a
/// <c>ClientDisabledDomainEvent</c> was raised for it. A client id can be
/// disabled more than once across its lifetime (disable, re-register, disable
/// again — <c>RegisteredClientRepository</c> permits reuse of a disabled row's
/// client id), and only the later instant is carried here (ADR-0160 §1).
/// </summary>
public sealed record RevokedClientEntry(string ClientId, DateTimeOffset DisabledAt);
