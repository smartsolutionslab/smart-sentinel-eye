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

    public DateTimeOffset CreatedAt { get; private set; }

    public OperatorIdentifier CreatedBy { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    private Revision() { }

    internal static Revision NewDraft(
        LayoutRevisionNumber number,
        CameraIdentifier camera,
        DateTimeOffset createdAt,
        OperatorIdentifier createdBy) =>
        new()
        {
            Id = LayoutRevisionIdentifier.New(),
            Number = number,
            State = LayoutRevisionState.Draft,
            Camera = camera,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
        };

    internal static Revision Branch(
        LayoutRevisionNumber number,
        CameraIdentifier camera,
        DateTimeOffset createdAt,
        OperatorIdentifier createdBy) =>
        NewDraft(number, camera, createdAt, createdBy);

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
