namespace SmartSentinelEye.Identity.Application.DTOs;

/// <summary>
/// Response shape from <c>POST /kiosks/enroll</c>. The plaintext
/// <see cref="ClientSecret"/> is shown to the caller exactly once
/// (the aggregate's <c>ClientSecret</c> VO enforces single-reveal
/// at the boundary).
/// </summary>
public sealed record KioskCredentialsDto(
    Guid RegisteredClientIdentifier,
    string ClientId,
    string Fab,
    string ClientSecret);
