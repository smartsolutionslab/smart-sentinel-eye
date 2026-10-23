using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authentication;

/// <summary>
/// Reads a compact JWS the way a verifier would, without the library that wrote it:
/// the oracle for what <c>ClientAssertion</c> produced must not be the code that
/// produced it (an-assertion-must-not-check-its-own-input).
/// </summary>
internal sealed class AssertionInspector
{
    private readonly string signingInput;
    private readonly byte[] signature;

    private AssertionInspector(JsonElement header, JsonElement payload, string signingInput, byte[] signature)
    {
        Header = header;
        Payload = payload;
        this.signingInput = signingInput;
        this.signature = signature;
    }

    public JsonElement Header { get; }

    public JsonElement Payload { get; }

    public static AssertionInspector Parse(string compactJws)
    {
        var parts = compactJws.Split('.');
        parts.Length.ShouldBe(3, "a signed JWT is header.payload.signature.");

        return new AssertionInspector(
            JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0])).RootElement.Clone(),
            JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1])).RootElement.Clone(),
            $"{parts[0]}.{parts[1]}",
            Base64Url.DecodeFromChars(parts[2]));
    }

    public bool SignatureVerifiesWith(RSA publicKey) =>
        publicKey.VerifyData(
            Encoding.ASCII.GetBytes(signingInput),
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

    public string Claim(string name) => Payload.GetProperty(name).GetString() ?? string.Empty;

    public long NumericClaim(string name) => Payload.GetProperty(name).GetInt64();

    /// <summary>RFC 7638 thumbprint of an RSA public key, computed independently.</summary>
    public static string Thumbprint(RSA key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var canonical =
            $$"""{"e":"{{Base64Url.EncodeToString(parameters.Exponent!)}}","kty":"RSA","n":"{{Base64Url.EncodeToString(parameters.Modulus!)}}"}""";

        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string NewPrivateKeyBase64(out RSA key)
    {
        key = RSA.Create(2048);
        return Convert.ToBase64String(key.ExportPkcs8PrivateKey());
    }
}
