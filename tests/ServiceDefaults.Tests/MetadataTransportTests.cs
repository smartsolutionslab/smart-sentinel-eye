using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Spec 340 (#2238) — outside Development, the realm's discovery document and
/// JWKS must be fetched only over TLS, and a misconfigured host must refuse to
/// start rather than silently run with substitutable signing keys.
///
/// <para>
/// R1 to R4 run over the same set of non-Development environment names —
/// <c>Production</c>, <c>Staging</c>, and a custom name — because the decision
/// is "fail-closed everywhere except Development", not "fail-closed only in
/// Production" (spec §3.2, assumption A1).
/// </para>
///
/// <para>
/// R3 and R4 set <c>RevocationList:ClientId</c>/<c>ClientSecret</c> so the
/// existing revocation <c>ValidateOnStart</c> check (spec 270) does not throw
/// first for an unrelated reason — see <c>RevocationListOptionsValidationTests</c>.
/// </para>
///
/// <para>
/// G1 is the Development twin (spec §6): it must pass both before and after
/// this change, unmodified, because Aspire wires Keycloak over plain HTTP by
/// design in Development (FR-007).
/// </para>
/// </summary>
public class MetadataTransportTests
{
    public static readonly TheoryData<string> NonDevelopmentEnvironments = new(
        Environments.Production,
        Environments.Staging,
        "CustomEnvironment");

    [Theory]
    [MemberData(nameof(NonDevelopmentEnvironments))]
    public void The_effective_RequireHttpsMetadata_is_true_outside_Development(string environmentName)
    {
        JwtBearerOptions options = BearerOptionsFor(environmentName, authority: "https://keycloak.invalid");

        options.RequireHttpsMetadata.ShouldBeTrue(
            $"outside Development ('{environmentName}' here) the realm's discovery document and "
            + "JWKS must be fetched over TLS, or an on-path attacker could substitute the signing "
            + "keys a token is checked against (spec 340 FR-001/FR-002).");
    }

    [Theory]
    [MemberData(nameof(NonDevelopmentEnvironments))]
    public void Startup_validation_throws_when_the_authority_is_http(string environmentName)
    {
        using ServiceProvider provider = BuildProvider(
            environmentName,
            authority: "http://keycloak.invalid",
            withRevocationCredentials: true);

        Exception exception = Should.Throw<Exception>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        // Not asserted as a specific exception type (plan §3.2): in the pinned
        // JwtBearer 10.0.12, JwtBearerPostConfigureOptions throws its own
        // InvalidOperationException for "RequireHttpsMetadata plus an http
        // authority" before this file's explicit Validate ever runs. Either
        // origin is acceptable; what must hold is that startup refuses, and
        // says why.
        exception.Message.ShouldContain(
            "https",
            Case.Insensitive,
            customMessage: $"outside Development ('{environmentName}' here) a plaintext metadata "
            + "authority must fail startup, and the message must say HTTPS is required — not 500 "
            + "on the first authenticated request (spec 340 FR-003/FR-006).");
    }

    [Theory]
    [MemberData(nameof(NonDevelopmentEnvironments))]
    public void Startup_validation_throws_when_a_later_configure_reopens_plaintext(string environmentName)
    {
        HostApplicationBuilder builder = EmptyBuilder(
            environmentName,
            authority: "https://keycloak.invalid",
            withRevocationCredentials: true);
        builder.AddBearerAuthentication();
        // Simulates a caller re-opening the flag after AddBearerAuthentication
        // ran — the case the explicit Validate predicate exists to catch,
        // since the framework's own PostConfigure check already passed (the
        // authority is https at that point).
        builder.Services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.RequireHttpsMetadata = false);

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        OptionsValidationException exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate(),
            $"a later Configure<JwtBearerOptions> flipping the flag back to false must still fail "
            + $"startup outside Development ('{environmentName}' here) — the policy is derived from "
            + "the environment, not whatever the last Configure delegate left behind (spec 340 FR-003).");

        exception.Message.ShouldContain("RequireHttpsMetadata");
        exception.Message.ShouldContain(environmentName);
    }

    [Fact]
    public void Development_startup_validation_passes_with_an_http_authority()
    {
        // Credentials set for the same reason as R3/R4: IStartupValidator.Validate()
        // runs every registered ValidateOnStart check, including spec 270's
        // unrelated revocation one, so leaving it unset would fail this test for
        // the wrong reason (plan §4).
        using ServiceProvider provider = BuildProvider(
            Environments.Development,
            authority: "http://keycloak.invalid",
            withRevocationCredentials: true);

        Should.NotThrow(
            () => provider.GetRequiredService<IStartupValidator>().Validate(),
            "Aspire wires Keycloak over plain HTTP by design in Development; this must stay "
            + "byte-for-byte unchanged (spec 340 FR-007).");

        JwtBearerOptions options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        options.RequireHttpsMetadata.ShouldBeFalse(
            "Development stays permissive; no new validation may fail for an http:// authority "
            + "there (spec 340 FR-007, G1).");
    }

    /// <summary>
    /// The options the real callers receive, built through the actual
    /// extension method. An <em>empty</em> builder deliberately, mirroring
    /// <c>BearerAudienceTests.BearerOptions</c>: the default
    /// <c>Host.CreateApplicationBuilder</c> reads <c>appsettings.json</c> and
    /// the environment, either of which could supply an answer this test
    /// would then wrongly credit to <c>AddBearerAuthentication</c>.
    /// </summary>
    private static JwtBearerOptions BearerOptionsFor(string environmentName, string authority)
    {
        using ServiceProvider provider = BuildProvider(
            environmentName, authority, withRevocationCredentials: false);
        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    private static ServiceProvider BuildProvider(
        string environmentName, string authority, bool withRevocationCredentials)
    {
        HostApplicationBuilder builder = EmptyBuilder(environmentName, authority, withRevocationCredentials);
        builder.AddBearerAuthentication();
        return builder.Services.BuildServiceProvider();
    }

    /// <summary>
    /// The environment-explicit empty builder, with the same self-check
    /// <c>DefaultEndpointsTests.ProbeHost</c> uses: an ambient
    /// <c>DOTNET_ENVIRONMENT</c> must not decide which path a theory case
    /// actually exercises.
    /// </summary>
    private static HostApplicationBuilder EmptyBuilder(
        string environmentName, string authority, bool withRevocationCredentials)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = environmentName });

        if (!string.Equals(builder.Environment.EnvironmentName, environmentName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Asked for environment '{environmentName}' but the host built "
                + $"'{builder.Environment.EnvironmentName}'.");
        }

        builder.Configuration["ConnectionStrings:keycloak"] = authority;

        if (withRevocationCredentials)
        {
            builder.Configuration["RevocationList:ClientId"] = "test-revocation-client";
            builder.Configuration["RevocationList:ClientSecret"] = "test-revocation-secret";
        }

        return builder;
    }
}
