using System.Security.Cryptography;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authentication;

/// <summary>
/// Spec 440 T-E (ADR-0186 Decisions 1-2, FR-009): the assertion migration-runner
/// presents instead of a secret. Read back with a verifier written here, not with
/// the library the production code signs with.
/// </summary>
public sealed class ClientAssertionTests : IDisposable
{
    private const string ClientIdentifier = "migration-runner";

    private const string TokenEndpoint =
        "https://keycloak.test/realms/smart-sentinel-eye/protocol/openid-connect/token";

    private readonly RSA key = RSA.Create(2048);

    private readonly FixedClock clock = new(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero));

    public void Dispose() => key.Dispose();

    [Fact]
    public void The_assertion_is_signed_with_RS256_and_the_header_names_the_keys_thumbprint()
    {
        var assertion = AssertionInspector.Parse(Create());

        assertion.Header.GetProperty("alg").GetString().ShouldBe("RS256");
        assertion.Header.GetProperty("kid").GetString().ShouldBe(AssertionInspector.Thumbprint(key));
    }

    [Fact]
    public void The_signature_verifies_with_the_public_half_of_the_key()
    {
        var assertion = AssertionInspector.Parse(Create());

        assertion.SignatureVerifiesWith(key).ShouldBeTrue();
    }

    [Fact]
    public void The_signature_does_not_verify_with_another_key()
    {
        using var other = RSA.Create(2048);
        var assertion = AssertionInspector.Parse(Create());

        assertion.SignatureVerifiesWith(other).ShouldBeFalse();
    }

    [Fact]
    public void The_issuer_and_the_subject_are_both_the_client()
    {
        var assertion = AssertionInspector.Parse(Create());

        assertion.Claim("iss").ShouldBe(ClientIdentifier);
        assertion.Claim("sub").ShouldBe(ClientIdentifier);
    }

    [Fact]
    public void The_audience_is_the_realms_token_endpoint()
    {
        var assertion = AssertionInspector.Parse(Create());

        var audience = assertion.Payload.GetProperty("aud");
        var audiences = audience.ValueKind == System.Text.Json.JsonValueKind.Array
            ? audience.EnumerateArray().Select(value => value.GetString()).ToArray()
            : [audience.GetString()];

        audiences.ShouldBe([TokenEndpoint]);
    }

    [Fact]
    public void The_assertion_lives_at_most_sixty_seconds_from_now()
    {
        var assertion = AssertionInspector.Parse(Create());

        var issuedAt = assertion.NumericClaim("iat");
        var expires = assertion.NumericClaim("exp");

        issuedAt.ShouldBe(clock.GetUtcNow().ToUnixTimeSeconds());
        (expires - issuedAt).ShouldBeInRange(1, 60);
    }

    [Fact]
    public void Two_assertions_carry_two_different_identifiers()
    {
        var first = AssertionInspector.Parse(Create());
        var second = AssertionInspector.Parse(Create());

        first.Claim("jti").ShouldNotBeNullOrWhiteSpace();
        second.Claim("jti").ShouldNotBe(
            first.Claim("jti"),
            "Keycloak rejects a reused jti as a replay (ADR-0186); every mint must sign a fresh one.");
    }

    private string Create() => ClientAssertion.Create(ClientIdentifier, TokenEndpoint, key, clock);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
