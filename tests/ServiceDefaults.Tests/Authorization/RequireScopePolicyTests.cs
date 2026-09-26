using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authorization;

public class RequireScopePolicyTests
{
    private static IAuthorizationService BuildAuthorizationService()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddAuthorizationBuilder().AddScopePolicies(Scope.All);
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal UserWithScopes(params string[] scopeClaims)
    {
        ClaimsIdentity identity = new("test");
        foreach (string scopeClaim in scopeClaims)
        {
            identity.AddClaim(new Claim("scope", scopeClaim));
        }
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal UnauthenticatedWithScope(string scope)
    {
        // A ClaimsIdentity with no authenticationType reports
        // IsAuthenticated == false, so RequireAuthenticatedUser() denies it
        // regardless of which claims it carries.
        ClaimsIdentity identity = new();
        identity.AddClaim(new Claim("scope", scope));
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task Principal_with_the_exact_scope_passes_that_scopes_policy()
    {
        IAuthorizationService authorization = BuildAuthorizationService();
        ClaimsPrincipal user = UserWithScopes(Scope.Sse.Rules.Write);

        AuthorizationResult result =
            await authorization.AuthorizeAsync(user, null, Scope.Sse.Rules.Write);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Principal_without_the_scope_fails_that_scopes_policy()
    {
        IAuthorizationService authorization = BuildAuthorizationService();
        ClaimsPrincipal user = UserWithScopes(Scope.Sse.Cameras.Read);

        AuthorizationResult result =
            await authorization.AuthorizeAsync(user, null, Scope.Sse.Rules.Write);

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Space_separated_multi_scope_claim_is_parsed_and_the_target_is_found()
    {
        IAuthorizationService authorization = BuildAuthorizationService();
        ClaimsPrincipal user = UserWithScopes(
            "openid profile sse.cameras.read sse.rules.write sse.audit.read");

        AuthorizationResult result =
            await authorization.AuthorizeAsync(user, null, Scope.Sse.Rules.Write);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Target_present_across_multiple_separate_scope_claims_passes()
    {
        IAuthorizationService authorization = BuildAuthorizationService();
        ClaimsPrincipal user = UserWithScopes("openid profile", Scope.Sse.Rules.Write);

        AuthorizationResult result =
            await authorization.AuthorizeAsync(user, null, Scope.Sse.Rules.Write);

        result.Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// Spec 200 US2 / #2486 (SC-9) — the policy half of withdrawing the
    /// <c>sse.management</c> grandfather clause. The WHEP handler's own copy
    /// was fixed by spec 258
    /// (<c>AuthorizeWhepCommandHandlerTests.Authorize_with_only_the_legacy_management_bundle_returns_Forbidden</c>);
    /// this is every policy <see cref="RequireScopeExtensions.AddScopePolicies"/>
    /// registers.
    ///
    /// <para>
    /// Written as the literal <c>"sse.management"</c>, not
    /// <see cref="RequireScopeExtensions.LegacyManagementBundle"/> — the constant
    /// is deleted in the phase-4b change that makes this pass, and a test that
    /// referenced it would stop compiling.
    /// </para>
    ///
    /// <para>
    /// Collects every scope the bundle passes rather than stopping at the
    /// first, so a failing run names them all: today that is every entry in
    /// <see cref="Scope.All"/> except <see cref="Scope.Sse.Events.Publish"/>
    /// (reserved for MQTT-publishing devices, ADR-0100), which is the
    /// grandfather clause laid bare rather than one odd policy. Replaces
    /// <c>Legacy_management_bundle_passes_a_normal_sse_policy</c> and
    /// <c>Legacy_management_bundle_does_not_pass_the_events_publish_policy</c> —
    /// a deliberate inversion of a green test, written as the specification of
    /// the change (ADR-0139).
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_principal_carrying_only_the_legacy_bundle_fails_every_policy()
    {
        IAuthorizationService authorization = BuildAuthorizationService();
        ClaimsPrincipal user = UserWithScopes("sse.management");

        List<string> passed = [];
        foreach (string scope in Scope.All)
        {
            AuthorizationResult result = await authorization.AuthorizeAsync(user, null, scope);
            if (result.Succeeded)
            {
                passed.Add(scope);
            }
        }

        passed.ShouldBeEmpty(
            $"the sse.management bundle was withdrawn (spec 200 US2 / #2486) and must not "
            + $"substitute for any scope, but it still passes: {string.Join(", ", passed)}.");
    }

    [Fact]
    public async Task Events_publish_policy_passes_for_the_exact_publish_scope()
    {
        IAuthorizationService authorization = BuildAuthorizationService();
        ClaimsPrincipal user = UserWithScopes(Scope.Sse.Events.Publish);

        AuthorizationResult result =
            await authorization.AuthorizeAsync(user, null, Scope.Sse.Events.Publish);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Principal_without_a_scope_claim_fails()
    {
        IAuthorizationService authorization = BuildAuthorizationService();
        ClaimsPrincipal user = UserWithScopes();

        AuthorizationResult result =
            await authorization.AuthorizeAsync(user, null, Scope.Sse.Rules.Write);

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Unauthenticated_principal_fails_even_when_a_scope_claim_is_present()
    {
        IAuthorizationService authorization = BuildAuthorizationService();
        ClaimsPrincipal user = UnauthenticatedWithScope(Scope.Sse.Rules.Write);

        AuthorizationResult result =
            await authorization.AuthorizeAsync(user, null, Scope.Sse.Rules.Write);

        result.Succeeded.ShouldBeFalse();
    }
}
