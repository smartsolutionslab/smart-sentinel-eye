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
        Ensure.That(user).IsNotNull();

        string raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out Guid value) && value != Guid.Empty ? OperatorIdentifier.From(value) : throw new UnattributableOperatorException();
    }

    /// <summary>
    /// Whether this principal is a browser kiosk, read from the token's
    /// <c>azp</c> claim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The kiosk latency segments are named for the kiosk —
    /// <c>kiosk-receive-to-decoded</c> — and constitution §IV reads them as the
    /// kiosk decode leg. Spec 043 mounted the shared <c>CameraViewer</c> on the
    /// management camera page, which turned an operator's desktop into a
    /// reporter for those same segments, mixing two populations into one series
    /// nobody could separate (#1893).
    /// </para>
    /// <para>
    /// Decided from the validated token rather than from a field the browser
    /// sends, because the server is the enforcement point for every other guard
    /// on that endpoint, and because it needs no change to a composite ADR-0122
    /// deliberately kept generic. Fails closed: a token with no <c>azp</c> is
    /// not a kiosk.
    /// </para>
    /// </remarks>
    public static bool IsBrowserKiosk(this ClaimsPrincipal user)
    {
        Ensure.That(user).IsNotNull();

        return string.Equals(
            user.FindFirst("azp")?.Value,
            AuthenticationDefaults.KioskClientId,
            StringComparison.Ordinal);
    }
}
