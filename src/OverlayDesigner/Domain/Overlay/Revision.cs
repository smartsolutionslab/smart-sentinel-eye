using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Per-edit revision within an Overlay chain. Owned by the
/// <see cref="Overlay"/> aggregate; mutators are package-internal so
/// the aggregate is the sole entry point and the at-most-one-Published
/// invariant lives inside one transaction.
/// </summary>
public sealed class Revision
{
    public OverlayRevisionIdentifier Id { get; private set; }

    public OverlayRevisionNumber Number { get; private set; }

    public OverlayRevisionState State { get; private set; } = null!;

    public Label Label { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public OperatorIdentifier CreatedBy { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    private Revision() { }

    internal static Revision NewDraft(
        OverlayRevisionNumber number,
        Label label,
        DateTimeOffset createdAt,
        OperatorIdentifier createdBy) =>
        new()
        {
            Id = OverlayRevisionIdentifier.New(),
            Number = number,
            State = OverlayRevisionState.Draft,
            Label = label,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
        };

    internal static Revision Branch(
        OverlayRevisionNumber number,
        Label label,
        DateTimeOffset createdAt,
        OperatorIdentifier createdBy) =>
        // Copy the base revision's Label: it is mapped as an EF-owned entity
        // keyed on its owner revision, so the branched revision must own its
        // own instance. Sharing the same CLR Label across two revisions makes
        // EF try to re-key the owned entity onto a new principal and throws.
        NewDraft(number, label with { }, createdAt, createdBy);

    internal void Publish(DateTimeOffset publishedAt)
    {
        if (State != OverlayRevisionState.Draft)
        {
            throw new InvalidOperationException(
                $"Revision {Number} cannot transition {State} -> Published.");
        }
        State = OverlayRevisionState.Published;
        PublishedAt = publishedAt;
    }

    internal void Revert()
    {
        if (State != OverlayRevisionState.Published)
        {
            throw new InvalidOperationException(
                $"Revision {Number} cannot transition {State} -> Draft (Revert).");
        }
        State = OverlayRevisionState.Draft;
        PublishedAt = null;
    }

    internal void EditLabel(Label newLabel)
    {
        ArgumentNullException.ThrowIfNull(newLabel);
        if (State != OverlayRevisionState.Draft)
        {
            throw new InvalidOperationException(
                $"Revision {Number} is {State}; only Draft revisions are editable.");
        }
        Label = newLabel;
    }

    internal void Archive(DateTimeOffset archivedAt)
    {
        if (State == OverlayRevisionState.Archived)
        {
            // Idempotent — re-archiving an Archived revision is a no-op.
            return;
        }
        State = OverlayRevisionState.Archived;
        ArchivedAt = archivedAt;
    }
}
