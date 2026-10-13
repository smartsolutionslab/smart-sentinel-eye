using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using SmartSentinelEye.StreamDistribution.Infrastructure.Auth;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Auth;

/// <summary>
/// Issue #2099 — the trust-anchor composition, pinned mechanically rather than
/// by review.
///
/// <para>
/// Since the seam moved construction into <c>MetadataSourceFor</c>, only the
/// resolution test runs that code, and it asserts the type rather than the
/// address. This binds the address itself, in the same way
/// <see cref="WhepAudienceTests"/> binds <c>CreateParameters</c> — through the
/// <c>internal</c> factory, so no private field is reflected on.
/// </para>
///
/// <para>
/// The authority carries a <b>trailing slash</b> deliberately: the
/// <c>TrimEnd('/')</c> is the part a future edit is most likely to drop
/// silently, and without it the manager would be addressed at a URL with a
/// doubled separator.
/// </para>
///
/// <para>
/// <c>RequireHttps</c> is <b>not</b> asserted here. It is reachable only by
/// reflecting on the <see cref="HttpDocumentRetriever"/> the manager holds,
/// which is the reflection #2099 removed. Since spec 340 (#2238) it is
/// environment-conditional — <c>false</c> in Development, <c>true</c>
/// everywhere else — and is covered separately by
/// <c>WhepMetadataTransportTests</c>. It stays load-bearing on the dev/CI
/// http authority, so the Docker-backed <c>WhepAuthIntegrationTests</c> fails
/// loudly if it is ever lost.
/// </para>
/// </summary>
public sealed class WhepMetadataSourceTests
{
    [Fact]
    public void The_metadata_source_is_addressed_at_the_authoritys_discovery_document()
    {
        // A Development environment, because this test binds the address only
        // (see the class remarks above). The transport policy itself is covered
        // separately, by the spec 340 suite.
        var environment = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }).Environment;

        var metadata =
            WhepAuthValidator.MetadataSourceFor(
                Options.Create(new WhepAuthOptions
                {
                    Authority = "https://keycloak.invalid/realms/smart-sentinel-eye/",
                }),
                environment);

        metadata.MetadataAddress.ShouldBe(
            "https://keycloak.invalid/realms/smart-sentinel-eye/.well-known/openid-configuration",
            customMessage: "the WHEP hook no longer fetches its issuer and signing keys from the "
            + "configured authority's discovery document. Either the well-known path has drifted "
            + "or the authority's trailing slash is no longer trimmed, and both give a URL the "
            + "realm does not serve — every WHEP open then fails on discovery (#2099).");
    }
}
