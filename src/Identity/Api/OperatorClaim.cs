using System.Security.Claims;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Api;

/// <summary>
/// Extracts the calling operator's identity from the JWT.
/// Falls back to a fresh Guid v7 when no <c>sub</c> claim is
/// present so audit trails always carry a value; in v1 the
/// resulting "synthetic" id is harmless because OIDC always
/// emits <c>sub</c>.
/// </summary>
internal static class OperatorClaim
{
    public static OperatorIdentifier From(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        string? raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out Guid value) && value != Guid.Empty
            ? OperatorIdentifier.From(value)
            : OperatorIdentifier.From(Guid.CreateVersion7());
    }
}
