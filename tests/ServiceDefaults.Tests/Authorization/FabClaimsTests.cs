using System.Security.Claims;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authorization;

/// <summary>
/// Keycloak emits group memberships in more than one shape, and this parse
/// decides which fabs a caller is treated as belonging to — so the awkward
/// shapes are covered explicitly rather than assumed.
/// </summary>
public class FabClaimsTests
{
    [Fact]
    public void Repeated_single_value_claims_are_all_read()
    {
        ClaimsPrincipal user = With("/fabs/munich", "/fabs/dresden");

        FabClaims.AssignedFabs(user).ShouldBe(["munich", "dresden"], ignoreOrder: true);
    }

    [Fact]
    public void One_space_separated_claim_is_split()
    {
        ClaimsPrincipal user = With("/fabs/munich /fabs/dresden");

        FabClaims.AssignedFabs(user).ShouldBe(["munich", "dresden"], ignoreOrder: true);
    }

    [Fact]
    public void Tab_separated_values_are_split_too()
    {
        ClaimsPrincipal user = With("/fabs/munich\t/fabs/dresden");

        FabClaims.AssignedFabs(user).ShouldBe(["munich", "dresden"], ignoreOrder: true);
    }

    // Keycloak groups are not exclusively fabs; a realm-role or org group
    // must not be mistaken for one.
    [Fact]
    public void Groups_that_are_not_fabs_are_ignored()
    {
        ClaimsPrincipal user = With("/operators", "/fabs/munich", "/tenants/acme");

        FabClaims.AssignedFabs(user).ShouldBe(["munich"]);
    }

    [Fact]
    public void A_caller_in_no_group_has_no_fabs()
    {
        FabClaims.AssignedFabs(new ClaimsPrincipal(new ClaimsIdentity())).ShouldBeEmpty();
    }

    [Fact]
    public void A_caller_with_only_non_fab_groups_has_no_fabs()
    {
        FabClaims.AssignedFabs(With("/operators")).ShouldBeEmpty();
    }

    // The same fab arriving twice — one repeated claim, one inside a
    // space-separated claim — must not make the caller look multi-fab, which
    // is what decides between inference and refusal (ADR-0114).
    [Fact]
    public void The_same_fab_twice_is_reported_once()
    {
        ClaimsPrincipal user = With("/fabs/munich", "/fabs/munich /operators");

        FabClaims.AssignedFabs(user).ShouldBe(["munich"]);
    }

    [Fact]
    public void A_null_principal_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => FabClaims.AssignedFabs(null));
    }

    private static ClaimsPrincipal With(params string[] groups) =>
        new(new ClaimsIdentity(
            groups.Select(g => new Claim(DefaultFabAuthorizationGuard.GroupClaimType, g))));
}
