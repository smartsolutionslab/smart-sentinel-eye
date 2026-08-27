using System.Security.Claims;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// #1893 — which principals may record into the kiosk latency segments.
///
/// <para>
/// The segments are named for the kiosk and constitution §IV reads them as the
/// kiosk decode leg. A desktop browser reporting into them makes the series a
/// mixture of two populations, and the failure is quiet in the direction that
/// matters: a desktop that decodes comfortably drags the distribution down, so
/// the dashboard reports a leg inside budget while the wall may not be.
/// </para>
/// </summary>
public class BrowserKioskPrincipalTests
{
    [Fact]
    public void A_kiosk_token_is_a_browser_kiosk()
    {
        Principal(("azp", AuthenticationDefaults.KioskClientId)).IsBrowserKiosk().ShouldBeTrue();
    }

    [Fact]
    public void A_management_token_is_not()
    {
        Principal(("azp", "smart-sentinel-eye-web")).IsBrowserKiosk().ShouldBeFalse();
    }

    /// <summary>
    /// Fails closed. An unrecognised caller is not given the benefit of the
    /// doubt, because the cost of a wrong "yes" is a leg that reads healthy
    /// while describing something else.
    /// </summary>
    [Fact]
    public void A_token_with_no_azp_is_not()
    {
        Principal(("sub", Guid.CreateVersion7().ToString())).IsBrowserKiosk().ShouldBeFalse();
    }

    [Fact]
    public void An_unrecognised_client_is_not()
    {
        Principal(("azp", "some-other-client")).IsBrowserKiosk().ShouldBeFalse();
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));
}
