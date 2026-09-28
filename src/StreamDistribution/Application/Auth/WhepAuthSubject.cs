namespace SmartSentinelEye.StreamDistribution.Application.Auth;

/// <summary>
/// The validated principal extracted from a WHEP bearer token. <c>Scopes</c>
/// is the split form of the JWT <c>scope</c> claim so callers can check what
/// the token grants without parsing. <c>Fabs</c> is the caller's fab
/// membership (the token's <c>groups</c> claim, with the <c>/fabs/</c>
/// prefix stripped — the same parsing
/// <c>SmartSentinelEye.ServiceDefaults.Authorization.FabClaims</c> gives
/// every other fab-checked endpoint) — ADR-0161.
/// </summary>
public sealed record WhepAuthSubject(string Subject, IReadOnlyList<string> Scopes, IReadOnlyList<string> Fabs);
