namespace SmartSentinelEye.ScenarioSimulator.Scenario;

/// <summary>
/// Root configuration for the Scenario Simulator (ADR-0111), bound from
/// configuration via <c>IOptions</c>. The <see cref="Active"/> scenario key
/// (set by the <c>ScenarioSimulator__Active</c> env var) selects one entry
/// from <see cref="Scenarios"/>; each entry is loaded from a JSON file in the
/// <c>Scenarios/</c> folder (e.g. <c>rolling-mill.json</c>) merged into config
/// at startup.
/// </summary>
public sealed class ScenarioOptions
{
    public const string SectionName = "ScenarioSimulator";

    /// <summary>Key of the scenario to play (e.g. <c>rolling-mill</c>).</summary>
    public string Active { get; set; } = "rolling-mill";

    /// <summary>All known scenarios, keyed by scenario key.</summary>
    public Dictionary<string, ScenarioDefinition> Scenarios { get; set; } = new();
}

/// <summary>
/// One scenario: a named list of assets (stations on the line). "rolling-mill"
/// vs "loading-bay" is just a different definition — one source feeds both
/// camera seeding (M1) and, later, sensor events (M2).
/// </summary>
public sealed class ScenarioDefinition
{
    public string Name { get; set; } = string.Empty;

    public List<AssetDefinition> Assets { get; set; } = new();
}

/// <summary>
/// An asset is a station on the line with a stable <see cref="Key"/> (e.g.
/// <c>station-4</c>) that correlates its camera and (M2) its sensors. The
/// shared key is what makes an overlay land on the right camera tile.
/// </summary>
public sealed class AssetDefinition
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public CameraDefinition Camera { get; set; } = new();

    /// <summary>
    /// M2-only sensor profiles. Carried through the scenario file so M1 and M2
    /// share one asset identity, but unused until M2 is implemented. See
    /// <see cref="SensorDefinition"/> and the stubbed extension point.
    /// </summary>
    public List<SensorDefinition> Sensors { get; set; } = new();
}

/// <summary>Camera leg of an asset: the MediaMTX path and which loop clip.</summary>
public sealed class CameraDefinition
{
    /// <summary>
    /// MediaMTX path on camera-sim, e.g. <c>station-4-roughing</c>. Becomes
    /// the RTSP URL <c>rtsp://camera-sim:8554/{Path}</c> registered in the
    /// catalog and the camera-sim path provisioned on <c>CameraRegisteredV1</c>.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>True to loop the clip forever (the only mode in M1).</summary>
    public bool Loop { get; set; } = true;
}

/// <summary>
/// M2 sensor profile (kind / unit / behaviour). Bound and carried by M1 so the
/// asset identity is shared, but NOT acted upon until M2. Intentionally a thin
/// bag of strings for now — M2 will model behaviour properly.
/// </summary>
public sealed class SensorDefinition
{
    public string Kind { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Behaviour { get; set; } = string.Empty;
}
