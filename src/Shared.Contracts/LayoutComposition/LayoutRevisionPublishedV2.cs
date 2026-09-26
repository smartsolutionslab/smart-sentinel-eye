namespace SmartSentinelEye.Shared.Contracts.LayoutComposition;

/// <summary>
/// Integration event published when a Layout revision transitions to the
/// Published state (spec 010, ADR-0112 §3). Supersedes
/// <c>LayoutRevisionPublishedV1</c> in a clean V2 cut — the revision now
/// carries a grid of tiles instead of a single camera. Versioned per
/// ADR-0073 (<c>V&lt;N&gt;</c> suffix marks the shape change);
/// subscribers consume via Wolverine RabbitMQ with per-module queue
/// isolation (ADR-0088).
///
/// <para>
/// Spec 258 (ADR-0156 §2) extends <see cref="LayoutTileV2"/> in place with
/// <c>RowSpan</c>/<c>ColSpan</c> rather than cutting a V3: every existing
/// published tile means exactly what the trailing default (<c>1</c>)
/// reproduces, so this is additive, not a shape change (ADR-0073 / ADR-0040
/// — this is the same pre-production, single-repo-consumer situation
/// ADR-0112 §3 already decided).
/// </para>
///
/// Primitive types (Guid, string, int, DateTimeOffset) are used at the
/// wire boundary — value-object types stay inside their owning context
/// per ADR-0040. <c>Layout</c> is the chain identifier;
/// <c>RevisionNumber</c> identifies which revision within the chain was
/// published; <c>Tiles</c> is the published grid's tile set
/// (<c>GridRows × GridCols</c>).
/// </summary>
public sealed record LayoutRevisionPublishedV2(
    Guid Layout,
    int RevisionNumber,
    string Name,
    IReadOnlyList<LayoutTileV2> Tiles,
    int GridRows,
    int GridCols,
    DateTimeOffset PublishedAt,
    Guid PublishedBy,
    EventMetadata Metadata) : IIntegrationEvent;

/// <summary>
/// A single tile on the published grid: a required camera, an optional
/// overlay (<c>null</c> when unbound), at zero-indexed <c>(Row, Col)</c>,
/// claiming a <c>RowSpan × ColSpan</c> rectangle from that origin (spec 258,
/// ADR-0156 §2; default <c>1×1</c> reproduces every tile published before
/// this feature). Primitives only at the wire (ADR-0040).
/// </summary>
public sealed record LayoutTileV2(
    Guid Camera,
    Guid? Overlay,
    int Row,
    int Col,
    int RowSpan = 1,
    int ColSpan = 1);
