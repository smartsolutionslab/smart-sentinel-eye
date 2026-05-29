namespace SmartSentinelEye.Identity.Application.KeycloakAdmin;

/// <summary>
/// Application-side seam over Keycloak's Admin REST API
/// (ADR-0041 + spec 008 plan §"Keycloak Admin client (HTTP impl)").
/// The Infrastructure layer wraps a hand-rolled <c>HttpClient</c>;
/// unit tests substitute <c>FakeKeycloakAdminClient</c>.
///
/// <para>
/// The implementation **must** be idempotent on
/// <see cref="CreateClientAsync"/> when called with an existing
/// <c>clientId</c> — the Identity command handlers rely on the
/// idempotency check to surface
/// <c>RegisterDeviceError.DeviceAlreadyRegistered</c> /
/// <c>EnrollKioskError.KioskAlreadyEnrolled</c> as typed
/// failures rather than letting the underlying 409 leak.
/// </para>
/// </summary>
public interface IKeycloakAdminClient
{
    /// <summary>
    /// Creates a Keycloak client + returns the just-minted
    /// client secret. Throws
    /// <see cref="KeycloakClientAlreadyExistsException"/> when the
    /// client already exists (the command handler maps that to a
    /// typed error).
    /// </summary>
    Task<KeycloakClientCredentials> CreateClientAsync(
        KeycloakClientRepresentation representation,
        string fabGroupPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generates a new client secret for the given client (used
    /// by the webhook rotation flow). Throws
    /// <see cref="KeycloakClientNotFoundException"/> when the
    /// client doesn't exist.
    /// </summary>
    Task<KeycloakClientCredentials> RotateClientSecretAsync(
        string clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the Keycloak client as disabled. Idempotent — calling
    /// on an already-disabled client is a no-op.
    /// </summary>
    Task DisableClientAsync(string clientId, CancellationToken cancellationToken);
}

/// <summary>
/// Thrown by <see cref="IKeycloakAdminClient.CreateClientAsync"/>
/// when the requested <c>clientId</c> already exists. Mapped to
/// a typed <c>*AlreadyRegistered</c> / <c>*AlreadyEnrolled</c>
/// error at the handler.
/// </summary>
public sealed class KeycloakClientAlreadyExistsException : Exception
{
    public string ClientId { get; }

    public KeycloakClientAlreadyExistsException(string clientId)
        : base($"Keycloak client '{clientId}' already exists.")
    {
        ClientId = clientId;
    }
}

public sealed class KeycloakClientNotFoundException : Exception
{
    public string ClientId { get; }

    public KeycloakClientNotFoundException(string clientId)
        : base($"Keycloak client '{clientId}' not found.")
    {
        ClientId = clientId;
    }
}
