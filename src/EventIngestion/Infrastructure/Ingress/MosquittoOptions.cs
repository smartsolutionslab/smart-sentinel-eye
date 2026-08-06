namespace SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

/// <summary>
/// Options for the MQTT subscriber (spec 006 + ADR-0095/ADR-0100).
///
/// <para>
/// <see cref="Host"/> and <see cref="Port"/> carry no usable defaults on
/// purpose. They are resolved from the Aspire-injected
/// <c>services:mosquitto:mqtt:0</c> endpoint at registration, which throws
/// when absent. An earlier <c>localhost:1883</c> default meant the
/// subscriber dialled a port nothing listened on and the managed client
/// retried there forever — the broker logged no connection at all and the
/// service still reported healthy.
/// </para>
/// </summary>
public sealed class MosquittoOptions
{
    public const string SectionName = "Mosquitto";

    /// <summary>Broker host, from the Aspire mosquitto endpoint.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Broker port, from the Aspire mosquitto endpoint.</summary>
    public int Port { get; set; }

    /// <summary>
    /// Subscriber service account. The go-auth plugin enforces
    /// <c>azp == username</c>, so this must equal the Keycloak clientId
    /// AND the <c>user</c> row in <c>acl.txt</c> granting the read.
    /// </summary>
    public string Username { get; init; } = "event-ingestion";

    /// <summary>
    /// Secret for the <see cref="Username"/> Keycloak client. The minted
    /// access token is presented as the MQTT password (ADR-0100).
    /// </summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>Realm the subscriber mints its token from.</summary>
    public string Realm { get; init; } = "smart-sentinel-eye";

    /// <summary>Keycloak base URL, from the Aspire keycloak reference.</summary>
    public string KeycloakUrl { get; set; } = string.Empty;

    /// <summary>Use TLS (port 8883 in prod, 1883 plaintext in dev).</summary>
    public bool UseTls { get; init; }

    /// <summary>MQTT client identifier (must be unique per fab).</summary>
    public string ClientId { get; init; } = "event-ingestion";

    /// <summary>
    /// Wildcard topic the subscriber listens on. Per spec FR-007 the
    /// taxonomy is <c>fab/{fabId}/{source}/{deviceId}</c>; this client
    /// subscribes to every device on every source for every fab.
    /// </summary>
    public string SubscribeTopic { get; init; } = "fab/+/+/+";
}
