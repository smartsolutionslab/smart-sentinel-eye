using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ServiceDefaults.Revocation;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.ServiceDefaults.Tests.Revocation;

/// <summary>
/// Phase-6 review of spec 270 (ADR-0160), S6: a missing or empty
/// <c>RevocationList:ClientId</c>/<c>ClientSecret</c> must fail the eight
/// non-Identity APIs at startup rather than admit every revoked token
/// forever behind a health check whose <c>Degraded</c> tag nobody may be
/// watching. The validation is scoped to the HTTP source only — Identity
/// replaces <see cref="IRevokedClientSource"/> with its own in-process
/// reader and never sets this credential (plan.md §3), so it must not be
/// validated there.
/// </summary>
public sealed class RevocationListOptionsValidationTests
{
    [Fact]
    public void Resolving_the_options_throws_when_the_http_source_is_in_use_and_the_secret_is_unset()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";

        builder.AddBearerAuthentication();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RevocationListOptions>>().Value,
            "the eight non-Identity APIs mint this token via the default HttpRevokedClientSource, "
            + "so a missing RevocationList:ClientSecret must fail startup rather than silently "
            + "admit every revoked token forever.");
    }

    /// <summary>
    /// Mirrors Identity's own composition: <see cref="IRevokedClientSource"/>
    /// is registered before <c>AddBearerAuthentication()</c> runs, so the
    /// shared <c>TryAddSingleton</c> for <see cref="HttpRevokedClientSource"/>
    /// yields to it — the same effect
    /// <c>IdentityInfrastructureModule</c>'s <c>services.Replace(...)</c> has
    /// on the finished container.
    /// </summary>
    [Fact]
    public void Resolving_the_options_does_not_validate_when_the_source_is_not_the_http_one()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";
        builder.Services.AddSingleton<IRevokedClientSource>(new NeverCalledSource());

        builder.AddBearerAuthentication();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        Should.NotThrow(
            () => provider.GetRequiredService<IOptions<RevocationListOptions>>().Value,
            "Identity replaces IRevokedClientSource with its own in-process reader and never sets "
            + "this credential (plan.md §3); the validation must short-circuit for it.");
    }

    private sealed class NeverCalledSource : IRevokedClientSource
    {
        public Task<IReadOnlyList<RevokedClientEntry>> FetchAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not expected to be called by this test.");
    }
}
