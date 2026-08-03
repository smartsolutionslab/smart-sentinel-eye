using System.Security.Claims;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Authorization;

/// <summary>
/// Reads which fabs a caller belongs to out of their Keycloak <c>groups</c>
/// claim.
///
/// <para>
/// Separate from <see cref="IFabAuthorizationGuard"/> on purpose. The guard
/// answers one question — may this caller touch fab X — and enumeration is a
/// different one; folding it in would grow every implementation and test
/// double with a method most callers never use.
/// </para>
///
/// <para>
/// Promoted here from a private copy in <c>AuditEndpoints</c>. A second,
/// ad-hoc variant lives in <c>EventsEndpoints.Writes</c>, and Automation
/// would have been the third hand-written parse of a security-relevant claim
/// — which is how the three drift apart.
/// </para>
/// </summary>
public static class FabClaims
{
    /// <summary>
    /// The fabs the caller belongs to, with the <c>/fabs/</c> prefix
    /// stripped. Empty when the caller belongs to none.
    ///
    /// <para>
    /// Keycloak emits group memberships either as repeated single-value
    /// claims or as one space-separated claim, so both are handled. Entries
    /// that are not fab groups are ignored rather than treated as fabs.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> AssignedFabs(ClaimsPrincipal user)
    {
        Ensure.That(user).IsNotNull();

        return
        [
            .. user.FindAll(DefaultFabAuthorizationGuard.GroupClaimType)
                .SelectMany(claim => claim.Value.Split(
                    [' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                .Where(token => token.StartsWith(
                    DefaultFabAuthorizationGuard.FabGroupPrefix, StringComparison.Ordinal))
                .Select(token => token[DefaultFabAuthorizationGuard.FabGroupPrefix.Length..])
                .Distinct(StringComparer.Ordinal),
        ];
    }
}
