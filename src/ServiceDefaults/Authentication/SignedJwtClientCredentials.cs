namespace SmartSentinelEye.ServiceDefaults.Authentication;

/// <summary>
/// STUB (spec 440 T060, red commit): the shape is the plan's (§2.2); the behaviour
/// that uses it is not written yet. Replaced by the backend slice (T080).
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
