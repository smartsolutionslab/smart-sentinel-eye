namespace SmartSentinelEye.ServiceDefaults.Authentication;

/// <summary>
/// What a Keycloak <c>client_credentials</c> grant needs when the client authenticates by
/// signed JWT (<c>private_key_jwt</c>, ADR-0186): the sibling of <see cref="ClientCredentials"/>
/// with a private key where the secret was. Primitives for the same reason — a wire boundary.
/// </summary>
/// <param name="Authority">Keycloak base URL, with or without a trailing slash.</param>
/// <param name="Realm">Realm the token is minted from.</param>
/// <param name="ClientIdentifier">The client whose assertion is signed.</param>
/// <param name="PrivateKeyPkcs8Base64">RSA private key, base64 of PKCS#8 DER, one line.</param>
public sealed record SignedJwtClientCredentials(
    string Authority,
    string Realm,
    string ClientIdentifier,
    string PrivateKeyPkcs8Base64);
