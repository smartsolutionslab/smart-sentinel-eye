using System.Security.Claims;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Maps the authenticated principal to the acting
/// <see cref="OperatorIdentifier"/> from the Keycloak <c>sub</c> claim
/// (falling back to the standard NameIdentifier claim). Shared by the
/// management endpoints.
///
/// <para>
/// Fails closed: an authenticated request carrying no usable <c>sub</c>
/// cannot be attributed to a real operator, so rather than fabricate one
/// (which would corrupt the audit trail) it throws
/// <see cref="UnattributableOperatorException"/> — mapped to a 401 by
/// <see cref="Authorization.UnattributableOperatorExceptionHandler"/>.
/// OIDC always emits <c>sub</c>, so in practice this only rejects a
/// malformed or non-OIDC token.
/// </para>
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
            : throw new UnattributableOperatorException();
    }
}

/// <summary>
/// Thrown by <see cref="ClaimsPrincipalExtensions.ToOperatorIdentifier"/>
/// when an authenticated request carries no usable <c>sub</c> claim, so the
/// action cannot be attributed to a real operator. Mapped to a
/// <c>401 OPERATOR_UNIDENTIFIED</c> by
/// <see cref="Authorization.UnattributableOperatorExceptionHandler"/>.
/// </summary>
public sealed class UnattributableOperatorException : Exception
{
    public UnattributableOperatorException()
        : base("The authenticated principal carries no usable 'sub' claim; the operator cannot be identified.")
    {
    }
}
