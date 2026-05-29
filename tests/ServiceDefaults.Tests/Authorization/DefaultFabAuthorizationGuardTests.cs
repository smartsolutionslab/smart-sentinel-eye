using System.Security.Claims;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authorization;

public class DefaultFabAuthorizationGuardTests
{
    private static ClaimsPrincipal UserIn(params string[] groups)
    {
        ClaimsIdentity identity = new("test");
        foreach (string g in groups)
        {
            identity.AddClaim(new Claim(DefaultFabAuthorizationGuard.GroupClaimType, g));
        }
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task Passes_when_caller_is_in_the_target_fab()
    {
        DefaultFabAuthorizationGuard guard = new();
        await guard.EnsureAccessAsync(UserIn("/fabs/munich"), "munich", CancellationToken.None);
    }

    [Fact]
    public async Task Multi_fab_user_passes_for_each_owned_fab()
    {
        DefaultFabAuthorizationGuard guard = new();
        ClaimsPrincipal user = UserIn("/fabs/munich", "/fabs/berlin");

        await guard.EnsureAccessAsync(user, "munich", CancellationToken.None);
        await guard.EnsureAccessAsync(user, "berlin", CancellationToken.None);
    }

    [Fact]
    public async Task Throws_FabAuthorizationException_when_caller_lacks_the_group()
    {
        DefaultFabAuthorizationGuard guard = new();
        ClaimsPrincipal user = UserIn("/fabs/munich");

        FabAuthorizationException ex = await Should.ThrowAsync<FabAuthorizationException>(
            () => guard.EnsureAccessAsync(user, "berlin", CancellationToken.None));
        ex.FabId.ShouldBe("berlin");
    }

    [Fact]
    public async Task Accepts_groups_emitted_as_a_single_space_separated_claim()
    {
        DefaultFabAuthorizationGuard guard = new();
        ClaimsIdentity identity = new("test");
        identity.AddClaim(new Claim(
            DefaultFabAuthorizationGuard.GroupClaimType,
            "/fabs/munich /fabs/berlin"));
        ClaimsPrincipal user = new(identity);

        await guard.EnsureAccessAsync(user, "munich", CancellationToken.None);
        await guard.EnsureAccessAsync(user, "berlin", CancellationToken.None);
    }
}
