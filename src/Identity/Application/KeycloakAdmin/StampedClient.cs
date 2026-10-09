namespace SmartSentinelEye.Identity.Application.KeycloakAdmin;

/// <summary>
/// A Keycloak client carrying an <c>sse.kind</c> attribute, as listed by the
/// admin API (spec 320 plan §3). <see cref="Kind"/> is read as-is — the
/// caller filters to the kinds it cares about (<see cref="OrphanedClientSweep.SweptKinds"/>),
/// not this record.
/// </summary>
public sealed record StampedClient(string ClientId, string Kind, bool Enabled);
