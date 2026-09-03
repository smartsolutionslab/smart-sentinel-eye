namespace SmartSentinelEye.Identity.Application.DTOs;

/// <summary>
/// The server-held half of a replayed credential response (ADR-0142). Each
/// endpoint pairs it with whatever its own answer adds from the replayed
/// request.
/// </summary>
/// <param name="Version">
/// The aggregate's version <b>now</b>, which is the version the original answer
/// carried unless something else has since changed the client. When it has, the
/// original version is also stale and its secret is dead, so the current pair is
/// the useful answer rather than a faithful one — see
/// <c>ReplayRegisteredClientQueryHandler</c>.
/// </param>
public sealed record ReplayedClientDto(
    Guid RegisteredClientIdentifier,
    int Version,
    string ClientId,
    string Fab,
    string ClientSecret);
