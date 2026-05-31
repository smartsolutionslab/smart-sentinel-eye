using System.Security.Claims;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Maps the authenticated principal to the acting
/// <see cref="OperatorIdentifier"/>. Reads the Keycloak <c>sub</c> claim
/// (falling back to the standard NameIdentifier claim); when neither is a
/// usable Guid a fresh operator id is minted so the action stays
/// attributable. Shared by the management endpoints — previously this
/// logic was duplicated as a private <c>OperatorFromClaims</c> helper in
/// each context's endpoint class.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static OperatorIdentifier ToOperatorIdentifier(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        string raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out Guid value) && value != Guid.Empty
            ? OperatorIdentifier.From(value)
            : OperatorIdentifier.From(Guid.CreateVersion7());
    }
}
