using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartSentinelEye.StreamDistribution.Infrastructure.Auth;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Auth;

/// <summary>
/// Spec 340 (#2238) — the WHEP hook's half of the uniformity claim:
/// <see cref="WhepAuthValidator"/> and <see cref="StreamDistributionInfrastructureModule"/>
/// must refuse plaintext realm metadata outside Development, through the same
/// policy <c>MetadataTransportTests</c> pins for the nine REST APIs.
///
/// <para>
/// R5 to R7 and twins G2/G3 run over the same non-Development environment set
/// as the bearer side — the uniformity proof the owner's decision asks for
/// (spec §6).
/// </para>
///
/// <para>
/// R5/G2 use <c>http://127.0.0.1:1/realms/smart-sentinel-eye</c> (plan §4):
/// port 1 is never listening, so a socket-level refusal is what proves the
/// retriever never got that far when it instead fails on scheme alone with
/// IDX20108.
/// </para>
/// </summary>
public sealed class WhepMetadataTransportTests
{
    private const string UnreachableAuthority = "http://127.0.0.1:1/realms/smart-sentinel-eye";

    public static readonly TheoryData<string> NonDevelopmentEnvironments = new(
        Environments.Production,
        Environments.Staging,
        "CustomEnvironment");

    [Theory]
    [MemberData(nameof(NonDevelopmentEnvironments))]
    public async Task The_metadata_source_fails_with_IDX20108_before_connecting_outside_Development(
        string environmentName)
    {
        var metadata = WhepAuthValidator.MetadataSourceFor(
            Options.Create(new WhepAuthOptions { Authority = UnreachableAuthority }),
            EnvironmentFor(environmentName));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => metadata.GetConfigurationAsync(CancellationToken.None));

        exception.InnerException.ShouldNotBeNull().Message.ShouldContain(
            "IDX20108",
            customMessage: $"outside Development ('{environmentName}' here) the retriever must refuse "
            + "a plaintext authority on scheme alone — IDX20108 — rather than dial 127.0.0.1:1 and "
            + "report a socket error that reads like an ordinary unreachable realm (spec 340 R5).");
    }

    [Theory]
    [MemberData(nameof(NonDevelopmentEnvironments))]
    public void Startup_validation_throws_when_the_whep_authority_is_http(string environmentName)
    {
        using var provider = WhepOptionsProvider(environmentName, authority: "http://keycloak.invalid");

        var exception = Should.Throw<Exception>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        exception.Message.ShouldContain(
            "https",
            Case.Insensitive,
            customMessage: $"outside Development ('{environmentName}' here) the WHEP metadata "
            + "authority must use HTTPS, and startup must refuse — not leave every WHEP open "
            + "answering IdentityProviderUnavailable, which reads like an unreachable realm rather "
            + "than a misconfiguration (spec 340 R6/FR-005/FR-006).");
    }

    [Theory]
    [MemberData(nameof(NonDevelopmentEnvironments))]
    public void Startup_validation_throws_when_the_whep_authority_is_malformed(string environmentName)
    {
        using var provider = WhepOptionsProvider(environmentName, authority: "not a uri at all");

        Should.Throw<Exception>(
            () => provider.GetRequiredService<IStartupValidator>().Validate(),
            $"a malformed authority is not an https authority either, and outside Development "
            + $"('{environmentName}' here) startup must refuse rather than defer the discovery to "
            + "whatever garbled address results (spec 340 R7/FR-005).");
    }

    [Fact]
    public async Task Development_metadata_source_fails_with_a_connection_error_not_IDX20108()
    {
        var metadata = WhepAuthValidator.MetadataSourceFor(
            Options.Create(new WhepAuthOptions { Authority = UnreachableAuthority }),
            EnvironmentFor(Environments.Development));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => metadata.GetConfigurationAsync(CancellationToken.None));

        exception.InnerException.ShouldNotBeNull().Message.ShouldNotContain(
            "IDX20108",
            customMessage: "Aspire wires Keycloak over plain http in Development by design; the "
            + "retriever must still attempt the connection rather than refuse on scheme alone "
            + "(spec 340 FR-007, G2).");
    }

    [Fact]
    public void Development_startup_validation_passes_with_an_http_whep_authority()
    {
        using var provider = WhepOptionsProvider(
            Environments.Development, authority: "http://keycloak.invalid");

        Should.NotThrow(
            () => provider.GetRequiredService<IStartupValidator>().Validate(),
            "Development stays permissive — this must pass both before the fix (vacuously, no "
            + "validator registered yet) and after it, unmodified (spec 340 FR-007, G3).");
    }

    /// <summary>
    /// The now-<c>internal</c> seam (plan §4): composes the WHEP options binding
    /// directly, the same way <c>MediaMtxApiOptionsValidationTests</c>-style
    /// coverage would reach <c>BindMediaMtxApiOptions</c>, without pulling in the
    /// whole <c>AddStreamDistributionInfrastructure</c> module (no database, no
    /// MediaMTX, no Wolverine).
    /// </summary>
    private static ServiceProvider WhepOptionsProvider(string environmentName, string authority)
    {
        var builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = environmentName });

        if (!string.Equals(builder.Environment.EnvironmentName, environmentName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Asked for environment '{environmentName}' but the host built "
                + $"'{builder.Environment.EnvironmentName}'.");
        }

        builder.Configuration["ConnectionStrings:keycloak"] = authority;

        StreamDistributionInfrastructureModule.BindWhepAuthOptions(builder);

        return builder.Services.BuildServiceProvider();
    }

    private static IHostEnvironment EnvironmentFor(string environmentName) =>
        Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environmentName })
            .Environment;
}
