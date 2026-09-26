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
    /// Written as the literal <c>"sse.management"</c>, not the
    /// <c>acceptLegacyBundle</c> clause that used to live in
    /// <see cref="RequireScopeExtensions.AddScopePolicies"/> — that clause is
    /// gone, so a test referencing it by name would no longer compile.
    /// </para>
    ///
    /// <para>
    /// Collects every scope the bundle passes rather than stopping at the
    /// first, so a failing run would have named them all: before this fix,
    /// that was every entry in <see cref="Scope.All"/> except
    /// <see cref="Scope.Sse.Events.Publish"/> (reserved for MQTT-publishing
    /// devices, ADR-0100) — the grandfather clause laid bare rather than one
    /// odd policy.
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
            $"the sse.management bundle was withdrawn (spec 265, #2486) and must not "
            + $"substitute for any scope, but it still passes: {string.Join(", ", passed)}.");
    }

    private static readonly string[] RepresentativeScopesForExclusivity =
    [
        Scope.Sse.Cameras.Write,
        Scope.Sse.Streams.Read,
        Scope.Sse.Layouts.Read,
        Scope.Sse.Overlays.Write,
        Scope.Sse.Variables.Read,
        Scope.Sse.Rules.Write,
        Scope.Sse.Events.Write,
        Scope.Sse.Webhooks.Write,
        Scope.Sse.Identity.DeviceClients.Write,
        Scope.Sse.Identity.KioskClients.Write,
        Scope.Sse.Audit.Read,
    ];

    /// <summary>
    /// Spec 265 (#2486) — closes the gap the withdrawn bundle exposed three
    /// times under three different spellings (the WHEP handler's own copy,
    /// <c>AuthenticationDefaults.AdminPolicy</c>, and this extension's own
    /// <c>acceptLegacyBundle</c> clause). <see cref="A_principal_carrying_only_the_legacy_bundle_fails_every_policy"/>
    /// only proves the exact strings <c>"sse.management"</c> and
    /// <c>"admin"</c> fail; it does not prove a policy accepts nothing but its
    /// own exact scope. This does, for a representative sample of
    /// <see cref="Scope.All"/> against a set of near-miss strings a future
    /// regression might plausibly reintroduce.
    /// </summary>
    [Fact]
    public async Task A_principal_carrying_only_a_hostile_near_miss_fails_the_real_scopes_policy()
    {
        IAuthorizationService authorization = BuildAuthorizationService();

        List<string> unexpectedlyPassed = [];
        foreach (string scope in RepresentativeScopesForExclusivity)
        {
            foreach (string nearMiss in HostileNearMisses(scope))
            {
                ClaimsPrincipal user = UserWithScopes(nearMiss);
                AuthorizationResult result = await authorization.AuthorizeAsync(user, null, scope);
                if (result.Succeeded)
                {
                    unexpectedlyPassed.Add($"{scope} via \"{nearMiss}\"");
                }
            }
        }

        unexpectedlyPassed.ShouldBeEmpty(
            "a scope policy must accept only its own exact scope string, but a near-miss "
            + $"passed: {string.Join(", ", unexpectedlyPassed)}.");
    }

    /// <summary>
    /// The withdrawn bundle itself, a sibling bundle-shaped guess, two
    /// wildcard shapes, a case variant, a trailing-character variant, and
    /// every proper dot-prefix of <paramref name="scope"/> shorter than the
    /// whole thing (e.g. for <c>sse.identity.kiosks.write</c>:
    /// <c>sse.identity</c> and <c>sse.identity.kiosks</c>).
    /// </summary>
    private static List<string> HostileNearMisses(string scope)
    {
        List<string> nearMisses =
        [
            "sse.management",
            "sse.admin",
            "sse.*",
            "*",
            scope.ToUpperInvariant(),
            scope + "x",
        ];

        string[] segments = scope.Split('.');
        for (int length = 2; length < segments.Length; length++)
        {
            nearMisses.Add(string.Join('.', segments[..length]));
        }

        return nearMisses;
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
