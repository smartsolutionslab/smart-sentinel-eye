namespace SmartSentinelEye.ScenarioSimulator.Cues;

/// <summary>
/// Normalized geometry of a cue's bounding box within the frame, each axis
/// 0..1 (spec 289 plan.md §3.1). Rendering the box is out of scope (Q5-a);
/// it exists so a future overlay can draw it.
/// </summary>
public sealed record CueBox(double X, double Y, double Width, double Height);

/// <summary>
/// One typed detection declared at a clip playback offset (spec 289 FR-001,
/// plan.md §3.1). Deserialized from the <c>&lt;clip&gt;.cues.json</c>
/// sidecar with <c>System.Text.Json</c>, case-insensitively — the sidecars
/// are content, not configuration, so they are not bound through
/// <c>IConfiguration</c>.
/// </summary>
public sealed record CueDefinition(
    int AtMs,
    string Class,
    double Confidence,
    string Kind = "ObjectDetected",
    string Source = "inference",
    string? Label = null,
    string? Zone = null,
    CueBox? Box = null,
    string? TrackIdentifier = null);

/// <summary>
/// The whole <c>&lt;clip&gt;.cues.json</c> sidecar: which clip it declares
/// cues for, the clip's own duration (content-checked against the file —
/// <c>ScenarioFileTests</c> compares it to the <c>mvhd</c> box), and the
/// cues themselves in ascending <see cref="CueDefinition.AtMs"/> order.
/// </summary>
public sealed record ClipManifest(string Clip, int DurationMs, IReadOnlyList<CueDefinition> Cues);
