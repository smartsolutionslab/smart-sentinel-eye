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
    /// Reads the client's existing secret without changing it — what an
    /// idempotent replay needs (ADR-0142).
    ///
    /// <para>
    /// <b>Reading rather than storing is the whole point.</b> Replaying a
    /// registration means returning the secret again, and the alternative was to
    /// persist the plaintext in our own database so it could be replayed from
    /// there. Keycloak is already the system of record for it, so this hands
    /// back what Keycloak holds and adds no second place for a secret to live.
    /// </para>
    ///
    /// <para>
    /// Distinct from <see cref="RotateClientSecretAsync"/>, and the distinction
    /// is load-bearing: rotating on a replay would hand the retry a *different*
    /// secret and silently invalidate the one the first attempt already
    /// delivered.
    /// </para>
    ///
    /// <para>
    /// Throws <see cref="KeycloakClientNotFoundException"/> when the client does
    /// not exist.
    /// </para>
    /// </summary>
    Task<KeycloakClientCredentials> ReadClientSecretAsync(
        string clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the Keycloak client as disabled. Idempotent — calling
    /// on an already-disabled client is a no-op.
    /// </summary>
    Task DisableClientAsync(string clientId, CancellationToken cancellationToken);

    /// <summary>
    /// The names of the sub-groups directly under <paramref name="parentPath"/>
    /// — for <c>/fabs</c>, the fabs that exist (spec 019 FR-001).
    ///
    /// <para>
    /// A read, unlike everything else on this interface, and the only one whose
    /// caller is outside Identity: <c>MigrationRunner</c> composes it to decide
    /// which event partitions to provision. It stays here rather than being
    /// reimplemented there because Keycloak is Identity's to speak to, and a
    /// second client would be a second thing to get wrong.
    /// </para>
    ///
    /// <para>
    /// Returns names, not paths — <c>munich</c>, not <c>/fabs/munich</c>.
    /// Throws when the realm cannot be reached; never returns empty to mean
    /// "could not tell".
    /// </para>
    /// </summary>
    Task<IReadOnlyList<string>> GetSubGroupNamesAsync(
        string parentPath, CancellationToken cancellationToken);

    /// <summary>
    /// The client ids of every kiosk **this system enrolled**.
    ///
    /// <para>
    /// Identified by the <c>sse.kind</c> attribute enrolment stamps, rather
    /// than by a naming convention written down a second time — a sweep whose
    /// idea of "a kiosk" drifts from what enrolment creates silently covers
    /// less than it appears to.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes the realm's inherited privileges off a client's service account.
    ///
    /// <para>
    /// <b>Why this exists.</b> Keycloak grants every account created after the
    /// realm is imported a default composite that includes
    /// <c>offline_access</c> — the privilege that mints credentials which never
    /// expire. So each kiosk this system enrols is born holding it, and "only a
    /// wall display may mint a long-lived credential" would be true of the realm
    /// file and false of the running system.
    /// </para>
    ///
    /// <para>
    /// <b>Idempotent.</b> An account already stripped is left alone, so a
    /// startup sweep can run on every boot and a retry after a partial failure
    /// is safe.
    /// </para>
    ///
    /// <para>
    /// <b>Only ever call this for an account enrolment created.</b> It removes
    /// every directly-assigned realm privilege; against a person's account that
    /// would be destructive.
    /// </para>
    /// </summary>
    Task StripInheritedRealmRolesAsync(
        string clientId, CancellationToken cancellationToken);
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
