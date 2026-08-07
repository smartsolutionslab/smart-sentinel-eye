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
    public Dictionary<string, ScenarioDefinition> Scenarios { get; set; } = [];
}

/// <summary>
/// One scenario: a named list of assets (stations on the line). "rolling-mill"
/// vs "loading-bay" is just a different definition — one source feeds both
/// camera seeding (M1) and, later, sensor events (M2).
/// </summary>
public sealed class ScenarioDefinition
{
    public string Name { get; set; } = string.Empty;

    public List<AssetDefinition> Assets { get; set; } = [];

    /// <summary>
    /// M2 billet-timeline cadence (dwell per station, emit tick, loop gap). The
    /// billet visits <see cref="Assets"/> in order; <c>null</c> until M2 seeds it.
    /// </summary>
    public TimelineDefinition Timeline { get; set; }

    /// <summary>
    /// M2 single 2×2 rolling-mill wall (ADR-0112): the four assets' tiles compose
    /// one published layout. <c>null</c> until M2 seeds it.
    /// </summary>
    public WallDefinition Wall { get; set; }
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
    /// share one asset identity. See <see cref="SensorDefinition"/>.
    /// </summary>
    public List<SensorDefinition> Sensors { get; set; } = [];

    /// <summary>
    /// M2 overlay seeded for this asset (label + normalized geometry). Drives the
    /// Phase-A overlay create so the per-asset banner is data-driven. <c>null</c>
    /// for assets that seed no overlay.
    /// </summary>
    public OverlayDefinition Overlay { get; set; }

    /// <summary>
    /// M2 highlight rule seeded for this asset (trigger kind + comparison +
    /// threshold + duration). Drives the Phase-B rule create. <c>null</c> for
    /// assets that seed no rule.
    /// </summary>
    public HighlightDefinition Highlight { get; set; }

    /// <summary>
    /// M2 tile coordinate of this asset on the single 2×2 wall
    /// (S4→(0,0), S7→(0,1), CB→(1,0), CO→(1,1)). <c>null</c> for assets not on
    /// the wall.
    /// </summary>
    public TileDefinition Tile { get; set; }
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
/// M2 sensor profile: <see cref="Kind"/> / <see cref="Unit"/> / named
/// <see cref="Behaviour"/> plus the numeric parameters that behaviour reads. The
/// behaviour strategy (ramp/burst/steady/decay/step) is code; its parameters are
/// config. Each behaviour only reads the subset it needs; the rest stay
/// <c>null</c> (e.g. <c>ramp</c> reads <see cref="Min"/>/<see cref="Max"/>;
/// <c>step</c> reads <see cref="Before"/>/<see cref="After"/>/
/// <see cref="StepAtFraction"/>).
/// </summary>
public sealed class SensorDefinition
{
    public string Kind { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Behaviour { get; set; } = string.Empty;

    /// <summary>
    /// Event source for this sensor's MQTT topic (<c>plc</c> | <c>inference</c>).
    /// Defaults to <c>plc</c>; vision-derived kinds set <c>inference</c>.
    /// </summary>
    public string Source { get; set; } = "plc";

    /// <summary><c>ramp</c> start value.</summary>
    public double? Min { get; set; }

    /// <summary><c>ramp</c> end value.</summary>
    public double? Max { get; set; }

    /// <summary><c>burst</c> spike value.</summary>
    public double? Peak { get; set; }

    /// <summary><c>steady</c> centre value.</summary>
    public double? Mean { get; set; }

    /// <summary><c>steady</c>/<c>burst</c> ± noise band.</summary>
    public double? Jitter { get; set; }

    /// <summary><c>decay</c> initial value.</summary>
    public double? Start { get; set; }

    /// <summary><c>decay</c> asymptote.</summary>
    public double? Floor { get; set; }

    /// <summary><c>step</c> value before the jump.</summary>
    public double? Before { get; set; }

    /// <summary><c>step</c> value after the jump.</summary>
    public double? After { get; set; }

    /// <summary><c>step</c> jump point as a fraction (0..1) of the dwell.</summary>
    public double? StepAtFraction { get; set; }
}

/// <summary>
/// M2 overlay seeded per asset: the banner label text, its normalized (0..1)
/// geometry within the tile, and a font size. Drives the Phase-A overlay create.
/// </summary>
public sealed class OverlayDefinition
{
    public string Label { get; set; } = string.Empty;

    /// <summary>Left edge, normalized 0..1 within the tile.</summary>
    public double X { get; set; }

    /// <summary>Top edge, normalized 0..1 within the tile.</summary>
    public double Y { get; set; }

    /// <summary>Width, normalized 0..1.</summary>
    public double Width { get; set; }

    /// <summary>Height, normalized 0..1.</summary>
    public double Height { get; set; }

    /// <summary>Font size in pixels for the banner label.</summary>
    public double FontSize { get; set; } = 24;
}

/// <summary>
/// M2 highlight rule seeded per asset: the trigger kind, a comparison operator
/// (<c>gte</c> | <c>lte</c> | <c>eq</c> | …) over <c>$.payload.value</c>, a
/// numeric threshold, and the highlight duration. The comparison-operator switch
/// is code; these are its parameters.
/// </summary>
public sealed class HighlightDefinition
{
    /// <summary>Sensor kind the rule triggers on (matches the rule TriggerKind).</summary>
    public string TriggerKind { get; set; } = string.Empty;

    /// <summary>Comparison operator name (e.g. <c>gte</c>, <c>lte</c>).</summary>
    public string Comparison { get; set; } = string.Empty;

    /// <summary>Threshold compared against <c>$.payload.value</c>.</summary>
    public double Threshold { get; set; }

    /// <summary>Highlight duration in milliseconds.</summary>
    public int DurationMs { get; set; }
}

/// <summary>M2 tile coordinate of an asset on the single 2×2 wall.</summary>
public sealed class TileDefinition
{
    public int Row { get; set; }

    public int Col { get; set; }
}

/// <summary>M2 billet-timeline cadence (all milliseconds).</summary>
public sealed class TimelineDefinition
{
    /// <summary>How long the billet dwells at each station.</summary>
    public int DwellMs { get; set; }

    /// <summary>Emit cadence while dwelling at a station.</summary>
    public int TickMs { get; set; }

    /// <summary>Pause after the last station before the next run.</summary>
    public int LoopGapMs { get; set; }
}

/// <summary>M2 single seeded wall: stable name + grid dimensions.</summary>
public sealed class WallDefinition
{
    /// <summary>Stable layout name (idempotency key), e.g. <c>rolling-mill-wall</c>.</summary>
    public string Name { get; set; } = string.Empty;

    public int Rows { get; set; }

    public int Cols { get; set; }
}
