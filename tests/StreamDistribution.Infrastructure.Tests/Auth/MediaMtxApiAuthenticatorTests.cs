using Microsoft.Extensions.Options;
using SmartSentinelEye.StreamDistribution.Infrastructure.Auth;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Auth;

/// <summary>
/// ADR-0171. <see cref="MediaMtxApiAuthenticator"/> is the one place the
/// configured secret and a caller-presented password are actually compared.
/// </summary>
public class MediaMtxApiAuthenticatorTests
{
    private const string ConfiguredSecret = "the-configured-secret";

    private static MediaMtxApiAuthenticator MakeAuthenticator(string secret = ConfiguredSecret) =>
        new(Options.Create(new MediaMtxApiOptions { Secret = secret }));

    [Fact]
    public void Authorizes_returns_true_for_the_matching_secret()
    {
        var authenticator = MakeAuthenticator();

        authenticator.Authorizes(ConfiguredSecret).ShouldBeTrue();
    }

    [Theory]
    [InlineData("wrong-secret")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("the-configured-secret ")]
    public void Authorizes_returns_false_for_anything_else(string? presented)
    {
        var authenticator = MakeAuthenticator();

        authenticator.Authorizes(presented).ShouldBeFalse();
    }

    /// <summary>
    /// A WHEP viewer's bearer token is never the control-API secret — this is
    /// the cross-check that ADR-0171's two authorization paths (WHEP bearer
    /// tokens vs. the api shared secret) do not accidentally admit each
    /// other's credential.
    /// </summary>
    [Fact]
    public void Authorizes_returns_false_for_a_whep_bearer_token()
    {
        var authenticator = MakeAuthenticator();

        authenticator.Authorizes("Bearer.xyz").ShouldBeFalse();
    }

    /// <summary>
    /// An unconfigured secret (empty string, the options default) must never
    /// be satisfied by an empty presented password — two empty strings would
    /// otherwise compare equal and authorize every caller.
    /// </summary>
    [Fact]
    public void Authorizes_returns_false_when_no_secret_is_configured()
    {
        var authenticator = MakeAuthenticator(secret: string.Empty);

        authenticator.Authorizes(string.Empty).ShouldBeFalse();
    }
}
