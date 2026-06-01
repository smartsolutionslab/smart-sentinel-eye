namespace SmartSentinelEye.StreamDistribution.Application.Auth;

/// <summary>
/// The validated principal extracted from a WHEP bearer token. <c>Scopes</c>
/// is the split form of the JWT <c>scope</c> claim so callers can check
/// for <c>sse.management</c> without parsing.
/// </summary>
public sealed record WhepAuthSubject(string Subject, IReadOnlyList<string> Scopes);
