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
    private readonly List<Label> labels = [];

    public OverlayRevisionIdentifier Id { get; private set; }

    public OverlayRevisionNumber Number { get; private set; }

    public OverlayRevisionState State { get; private set; } = null!;

    /// <summary>
    /// The ordered, non-empty set of labels this revision carries (spec 150).
    /// Replaced atomically via <see cref="ReplaceLabels"/> — there is no
    /// per-label mutator (FR-006).
    /// </summary>
    public IReadOnlyList<Label> Labels => labels;

    public Creation Creation { get; private set; } = null!;

    public PublishedAt? PublishedAt { get; private set; }

    public ArchivedAt? ArchivedAt { get; private set; }

    private Revision() { }

    internal static Revision NewDraft(
        OverlayRevisionNumber number,
        IReadOnlyList<Label> labels,
        DateTimeOffset createdAt,
        OperatorIdentifier createdBy)
    {
        Revision revision = new()
        {
            Id = OverlayRevisionIdentifier.New(),
            Number = number,
            State = OverlayRevisionState.Draft,
            Creation = Creation.From(CreatedAt.From(createdAt), createdBy),
        };
        revision.labels.AddRange(CloneWithOrdinals(labels));
        return revision;
    }

    internal static Revision Branch(
        OverlayRevisionNumber number,
        IReadOnlyList<Label> labels,
        DateTimeOffset createdAt,
        OperatorIdentifier createdBy) =>
        // Copy the base revision's labels: each is mapped as an EF-owned
        // entity keyed on its owner revision, so the branched revision must
        // own its own instances. Sharing the same CLR Label across two
        // revisions makes EF try to re-key the owned entity onto a new
        // principal and throws. NewDraft already clones per element (FR-007),
        // so Branch is a straight delegation to it.
        NewDraft(number, labels, createdAt, createdBy);

    /// <summary>
    /// Deep-copies every label — including each label's owned
    /// <c>Position</c>/<c>Size</c>, since a <c>with</c> expression is shallow
    /// and (spec 060) those are themselves owned entities keyed on the
    /// label — and reassigns dense zero-based ordinals from the given order
    /// (FR-005, FR-007). This is the highest-risk line in the backend change:
    /// without the per-element clone, EF tries to re-key an owned entity onto
    /// a new principal and throws.
    /// </summary>
    private static IEnumerable<Label> CloneWithOrdinals(IReadOnlyList<Label> source)
    {
        for (int i = 0; i < source.Count; i++)
        {
            Label label = source[i];
            yield return (label with { Position = label.Position with { }, Size = label.Size with { } })
                .AtOrdinal(i);
        }
    }

    internal void Publish(DateTimeOffset publishedAt)
    {
        if (State != OverlayRevisionState.Draft)
        {
            throw new InvalidOperationException(
                $"Revision {Number} cannot transition {State} -> Published.");
        }
        State = OverlayRevisionState.Published;
        PublishedAt = PublishedAt.From(publishedAt);
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

    /// <summary>
    /// Atomically replaces this Draft revision's entire label set (FR-006).
    /// Mirrors <c>LayoutComposition.Revision.ReplaceTiles</c>: the owning
    /// aggregate validates the set invariants before calling; only the
    /// Draft-state guard lives here (a programmer error to edit a non-Draft
    /// revision — throws as before).
    /// </summary>
    internal void ReplaceLabels(IReadOnlyList<Label> newLabels)
    {
        Ensure.That(newLabels).IsNotNull();
        if (State != OverlayRevisionState.Draft)
        {
            throw new InvalidOperationException(
                $"Revision {Number} is {State}; only Draft revisions are editable.");
        }
        labels.Clear();
        labels.AddRange(CloneWithOrdinals(newLabels));
    }

    internal void Archive(DateTimeOffset archivedAt)
    {
        if (State == OverlayRevisionState.Archived)
        {
            // Idempotent — re-archiving an Archived revision is a no-op.
            return;
        }
        State = OverlayRevisionState.Archived;
        ArchivedAt = ArchivedAt.From(archivedAt);
    }
}
