namespace SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

/// <summary>
/// Options for the MQTT subscriber (spec 006 + ADR-0095). Reads
/// connection details from configuration / Aspire-injected
/// service references.
/// </summary>
public sealed class MosquittoOptions
{
    public const string SectionName = "Mosquitto";

    /// <summary>Broker host (e.g. <c>localhost</c>).</summary>
    public string Host { get; init; } = "localhost";

    /// <summary>Broker port (1883 plaintext / 8883 TLS).</summary>
    public int Port { get; init; } = 1883;

    /// <summary>Subscriber service account name.</summary>
    public string Username { get; init; } = "event-ingestion";

    /// <summary>Subscriber service account password.</summary>
    public string Password { get; init; } = string.Empty;

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
