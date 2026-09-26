using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authorization;

/// <summary>
/// Spec 200 US2 / #2486 (SC-10) — the registration itself offers no back door
/// once the <c>sse.management</c> bundle is withdrawn from every policy.
///
/// <para>
/// <b>What is broken today.</b> <c>AuthenticationDefaults.AddBearerAuthentication</c>
/// registers a second policy, <c>"admin"</c>, requiring the same bundle — a
/// registered policy that no client can ever satisfy legitimately, and that no
/// endpoint, hub or attribute names (grepped; only the definition and one stale
/// doc <c>cref</c> reference it). It has carried
/// <c>[Obsolete("… Removed in spec 009.")]</c> since issue #844.
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
/// The constant is deleted in the phase-4b change that makes this pass, and a
/// test that referenced it would stop compiling.
/// </para>
/// </summary>
public class RegisteredPolicyTests
{
    [Fact]
    public async Task The_host_registers_no_admin_policy()
    {
        IAuthorizationPolicyProvider provider = BuildPolicyProvider();

        AuthorizationPolicy? policy = await provider.GetPolicyAsync("admin");

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
        IAuthorizationPolicyProvider provider = BuildPolicyProvider();

        foreach (string scope in Scope.All)
        {
            AuthorizationPolicy? policy = await provider.GetPolicyAsync(scope);

            policy.ShouldNotBeNull($"AddScopePolicies should register a policy for '{scope}'.");
        }
    }

    private static IAuthorizationPolicyProvider BuildPolicyProvider()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";
        builder.AddBearerAuthentication();

        ServiceProvider provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IAuthorizationPolicyProvider>();
    }
}
