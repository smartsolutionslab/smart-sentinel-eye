using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Authentication;

/// <summary>
/// Signs the RFC 7523 client assertion a <c>private_key_jwt</c> client presents in place of
/// a secret (ADR-0186). Written against the BCL alone: the token is three base64url
/// segments, and a signing library would add a dependency for a few lines of JSON.
///
/// <para>
/// Single-use by construction: a fresh <c>jti</c> every call and a lifetime of
/// <see cref="Lifetime"/>, because Keycloak refuses a reused identifier as a replay.
/// </para>
/// </summary>
internal static class ClientAssertion
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    [SuppressMessage(
        "Security Hotspot",
        "S5542:Encryption algorithms should be used with secure mode and padding scheme",
        Justification = "RS256 is RSASSA-PKCS1-v1_5 by definition (RFC 7518 section 3.3), and this is a signature, "
            + "not encryption; the realm verifies this algorithm, so PSS (PS256) is not an available choice.")]
    public static string Create(string clientIdentifier, string tokenEndpoint, RSA key, TimeProvider clock)
    {
        Ensure.That(clientIdentifier).IsNotNull().IsNotNullOrWhiteSpace();
        Ensure.That(tokenEndpoint).IsNotNull().IsNotNullOrWhiteSpace();
        Ensure.That(key).IsNotNull();
        Ensure.That(clock).IsNotNull();

        var issuedAt = clock.GetUtcNow();

        var header = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["kid"] = Thumbprint(key),
        });
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = clientIdentifier,
            ["sub"] = clientIdentifier,
            ["aud"] = tokenEndpoint,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = issuedAt.Add(Lifetime).ToUnixTimeSeconds(),
        });

        var signingInput = $"{Encode(header)}.{Encode(payload)}";
        var signature = key.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary>RFC 7638: SHA-256 over the canonical JSON of the public members, in key order.</summary>
    private static string Thumbprint(RSA key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var canonical =
            $$"""{"e":"{{Base64Url.EncodeToString(parameters.Exponent!)}}","kty":"RSA","n":"{{Base64Url.EncodeToString(parameters.Modulus!)}}"}""";

        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Encode(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
}
