using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Per-edit revision within a Layout chain. Owned by the
/// <see cref="Layout"/> aggregate; mutators are package-internal so the
/// aggregate is the sole entry point — keeps the
/// at-most-one-Published-per-chain invariant inside the aggregate
/// transaction.
/// </summary>
public sealed class Revision
{
    public LayoutRevisionIdentifier Id { get; private set; }

    public LayoutRevisionNumber Number { get; private set; }

    public LayoutRevisionState State { get; private set; } = null!;

    public CameraIdentifier Camera { get; private set; }

    /// <summary>
    /// Optional overlay binding (spec 004). Null means "no overlay
    /// composited over this cell"; non-null is a latest-Published
    /// reference into the OverlayDesigner bounded context.
    /// </summary>
    public OverlayIdentifier? Overlay { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public OperatorIdentifier CreatedBy { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    private Revision() { }

    internal static Revision NewDraft(
        LayoutRevisionNumber number,
        CameraIdentifier camera,
        OverlayIdentifier? overlay,
        DateTimeOffset createdAt,
        OperatorIdentifier createdBy) =>
        new()
        {
            Id = LayoutRevisionIdentifier.New(),
            Number = number,
            State = LayoutRevisionState.Draft,
            Camera = camera,
            Overlay = overlay,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
        };

    internal static Revision Branch(
        LayoutRevisionNumber number,
        CameraIdentifier camera,
        OverlayIdentifier? overlay,
        DateTimeOffset createdAt,
        OperatorIdentifier createdBy) =>
        NewDraft(number, camera, overlay, createdAt, createdBy);

    internal void Publish(DateTimeOffset publishedAt)
    {
        if (State != LayoutRevisionState.Draft)
        {
            throw new InvalidOperationException(
                $"Revision {Number} cannot transition {State} -> Published.");
        }
        State = LayoutRevisionState.Published;
        PublishedAt = publishedAt;
    }

    internal void Revert()
    {
        if (State != LayoutRevisionState.Published)
        {
            throw new InvalidOperationException(
                $"Revision {Number} cannot transition {State} -> Draft (Revert).");
        }
        State = LayoutRevisionState.Draft;
        PublishedAt = null;
    }

    internal void EditCamera(CameraIdentifier camera)
    {
        if (State != LayoutRevisionState.Draft)
        {
            throw new InvalidOperationException(
                $"Revision {Number} is {State}; only Draft revisions are editable.");
        }
        Camera = camera;
    }

    internal void AttachOverlay(OverlayIdentifier? overlay)
    {
        if (State != LayoutRevisionState.Draft)
        {
            throw new InvalidOperationException(
                $"Revision {Number} is {State}; only Draft revisions are editable.");
        }
        Overlay = overlay;
    }

    internal void Archive(DateTimeOffset archivedAt)
    {
        if (State == LayoutRevisionState.Archived)
        {
            // Idempotent — re-archiving an already-Archived revision is a no-op.
            return;
        }
        State = LayoutRevisionState.Archived;
        ArchivedAt = archivedAt;
    }
}
