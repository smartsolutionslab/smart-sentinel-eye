namespace SmartSentinelEye.ServiceDefaults.Authentication;

/// <summary>
/// The four values a Keycloak <c>client_credentials</c> grant needs, read fresh
/// from whichever options object a context binds.
///
/// <para>
/// Carries primitives on purpose. Constitution §II bans them on a domain model;
/// this is neither a domain model nor a domain concept — it is the shape of a
/// form post, assembled from configuration and immediately URL-encoded. The
/// exemption is the one <c>Shared.Contracts</c> already has, for the same
/// reason: at a wire boundary, a string is what the wire actually carries.
/// </para>
/// </summary>
/// <param name="Authority">Keycloak base URL, with or without a trailing slash.</param>
/// <param name="Realm">Realm the token is minted from.</param>
/// <param name="ClientIdentifier">The service account's <c>client_id</c>.</param>
/// <param name="ClientSecret">Its secret.</param>
public sealed record ClientCredentials(
    string Authority,
    string Realm,
    string ClientIdentifier,
    string ClientSecret);
