namespace SmartSentinelEye.ScenarioSimulator.Configuration;

/// <summary>
/// Endpoints + credentials the simulator needs, resolved from Aspire-injected
/// config in the composition root. Dev-only; values come from AppHost wiring,
/// never from prod secrets.
/// </summary>
public sealed class SimulatorOptions
{
    public const string SectionName = "ScenarioSimulator:Runtime";

    /// <summary>Base URL of the camera-catalog REST API (POST /cameras).</summary>
    public string CameraCatalogUrl { get; set; } = string.Empty;

    /// <summary>HTTP control-plane base URL of the camera-sim MediaMTX (v3 API).</summary>
    public string CameraSimApiUrl { get; set; } = string.Empty;

    /// <summary>Base URL of the overlay-designer REST API (POST /overlays).</summary>
    public string OverlayDesignerUrl { get; set; } = string.Empty;

    /// <summary>Base URL of the automation REST API (POST /rules).</summary>
    public string AutomationUrl { get; set; } = string.Empty;

    /// <summary>Base URL of the layout-composition REST API (POST /layouts).</summary>
    public string LayoutCompositionUrl { get; set; } = string.Empty;

    /// <summary>
    /// Mosquitto MQTT broker <c>host:port</c> the billet timeline publishes to,
    /// e.g. <c>localhost:1883</c>. Resolved from the AppHost mosquitto endpoint.
    /// </summary>
    public string MqttHost { get; set; } = string.Empty;

    /// <summary>
    /// RTSP host:port the camera catalog stores as the source and that the main
    /// MediaMTX pulls from, e.g. <c>camera-sim:8554</c>. The RtspUrl is
    /// <c>rtsp://{RtspHost}/{path}</c>.
    /// </summary>
    public string RtspHost { get; set; } = "camera-sim:8554";

    /// <summary>Keycloak base URL (realm token endpoint is derived from it).</summary>
    public string KeycloakUrl { get; set; } = string.Empty;

    public string Realm { get; set; } = "smart-sentinel-eye";

    /// <summary>Confidential client used for the client_credentials grant.</summary>
    public string ClientId { get; set; } = "scenario-simulator";

    public string ClientSecret { get; set; } = string.Empty;
}
