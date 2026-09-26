using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authorization;

/// <summary>
/// Spec 265 (#2486, SC-10) — the registration itself offers no back door once
/// the <c>sse.management</c> bundle is withdrawn from every policy.
///
/// <para>
/// <b>What this guards against.</b> <c>AuthenticationDefaults.AddBearerAuthentication</c>
/// used to register a second policy, <c>"admin"</c>, requiring the same
/// bundle — a registered policy that no client could ever satisfy
/// legitimately, and that no endpoint, hub or attribute named (grepped; only
/// the definition and one stale doc <c>cref</c> referenced it). It had
/// carried <c>[Obsolete("… Removed in spec 009.")]</c> since issue #844.
/// </para>
///
/// <para>
/// <b>Real registration, not a copy.</b> Mirrors <c>BearerAudienceTests</c>:
/// <see cref="Host.CreateEmptyApplicationBuilder"/> with no ambient
/// configuration, then the real <c>AddBearerAuthentication()</c> extension.
/// The authority URL is never dialled — <c>AddJwtBearer</c> only fetches
/// metadata on the first request.
/// </para>
///
/// <para>
/// <b>The literal <c>"admin"</c>, not <c>AuthenticationDefaults.AdminPolicy</c>.</b>
/// That constant no longer exists, so a test referencing it would not compile.
/// </para>
/// </summary>
public class RegisteredPolicyTests
{
    /// <summary>
    /// Checks the one specific, previously-registered policy name — not a
    /// general "no extra policy exists" guarantee. <see cref="AuthorizationOptions"/>
    /// exposes no public enumerator over the policies it holds, so a full
    /// inventory of every registered policy is not obtainable without
    /// reflection; this is an accepted limit, not an oversight.
    /// </summary>
    [Fact]
    public async Task The_host_registers_no_admin_policy()
    {
        using ServiceProvider provider = BuildServiceProvider();
        IAuthorizationPolicyProvider policyProvider =
            provider.GetRequiredService<IAuthorizationPolicyProvider>();

        AuthorizationPolicy? policy = await policyProvider.GetPolicyAsync("admin");

        policy.ShouldBeNull(
            "AuthenticationDefaults registers an 'admin' policy requiring sse.management — a "
            + "scope no client holds and no code should honour (#2486). It has been "
            + "[Obsolete] since #844.");
    }

    /// <summary>
    /// Control, kept green before and after: without it, the assertion above
    /// would also pass against a registration that registered nothing at all
    /// (e.g. a broken builder), which would make it worthless as a guard.
    /// </summary>
    [Fact]
    public async Task The_host_still_registers_a_policy_per_catalogued_scope()
    {
        using ServiceProvider provider = BuildServiceProvider();
        IAuthorizationPolicyProvider policyProvider =
            provider.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (string scope in Scope.All)
        {
            AuthorizationPolicy? policy = await policyProvider.GetPolicyAsync(scope);

            policy.ShouldNotBeNull($"AddScopePolicies should register a policy for '{scope}'.");
        }
    }

    private static ServiceProvider BuildServiceProvider()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";
        builder.AddBearerAuthentication();

        return builder.Services.BuildServiceProvider();
    }
}
