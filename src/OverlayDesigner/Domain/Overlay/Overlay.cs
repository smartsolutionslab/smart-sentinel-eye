using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Aggregate root for a logical Overlay chain (spec 004). Mirrors the
/// LayoutComposition.Layout shape: 1..N revisions, at most one
/// Published at a time, branch-on-edit semantics. A partial unique
/// index in Postgres backs the at-most-one-Published invariant.
/// </summary>
public sealed class Overlay : AggregateRoot<OverlayIdentifier>
{
    private readonly List<Revision> _revisions = new();

    public OverlayName Name { get; private set; } = null!;

    public IReadOnlyList<Revision> Revisions => _revisions;

    public DateTimeOffset CreatedAt { get; private set; }

    public OperatorIdentifier CreatedBy { get; private set; }

    private Overlay() { }

    /// <summary>
    /// Mints a new logical Overlay chain with its first revision in
    /// <c>Draft</c>. No domain event is raised — drafts are not
    /// observable to kiosks until Publish.
    /// </summary>
    public static Overlay CreateDraft(
        OverlayName name,
        Label label,
        OperatorIdentifier createdBy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(clock);

        DateTimeOffset now = clock.UtcNow;
        Overlay overlay = new()
        {
            Id = OverlayIdentifier.New(),
            Name = name,
            CreatedAt = now,
            CreatedBy = createdBy,
        };
        overlay._revisions.Add(
            Revision.NewDraft(OverlayRevisionNumber.One, label, now, createdBy));
        return overlay;
    }

    /// <summary>
    /// Branches a new Draft revision off the chain's current Published
    /// revision; pre-fills the Label from the prior revision so the
    /// editor mutates a known-good baseline (spec 004 US4).
    /// </summary>
    public Revision BranchDraft(OperatorIdentifier by, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Revision baseRevision = CurrentPublishedOrNull()
            ?? throw new InvalidOperationException(
                "BranchDraft requires a currently-Published revision to copy from.");

        OverlayRevisionNumber next = MaxRevisionNumber().Next();
        Revision draft = Revision.Branch(next, baseRevision.Label, clock.UtcNow, by);
        _revisions.Add(draft);
        return draft;
    }

    /// <summary>
    /// In-place Label edit on an existing Draft revision (spec 004 FR-005).
    /// </summary>
    public void EditDraft(
        OverlayRevisionNumber number, Label label, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(clock);
        Revision target = RequireRevision(number);
        target.EditLabel(label);
    }

    /// <summary>
    /// Publishes a Draft revision. Atomically archives the previously-
    /// Published sibling in the same transaction (FR-003); raises
    /// <see cref="OverlayRevisionPublishedDomainEvent"/> and, when
    /// applicable, <see cref="OverlayRevisionArchivedDomainEvent"/>.
    /// </summary>
    public void Publish(OverlayRevisionNumber number, OperatorIdentifier by, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Revision target = RequireRevision(number);
        Revision? prior = CurrentPublishedOrNull();
        DateTimeOffset now = clock.UtcNow;

        target.Publish(now);
        if (prior is not null && prior.Number != number)
        {
            prior.Archive(now);
            Raise(new OverlayRevisionArchivedDomainEvent(Id, prior.Number, now, by));
        }
        Raise(new OverlayRevisionPublishedDomainEvent(
            Id, number, Name, target.Label, now, by));
    }

    /// <summary>
    /// Reverts a Published revision to Draft. Raises an Archived
    /// domain event so connected kiosks treat the revision as gone
    /// (the new Draft is invisible to kiosks until republished).
    /// </summary>
    public void Revert(OverlayRevisionNumber number, OperatorIdentifier by, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Revision target = RequireRevision(number);
        target.Revert();
        Raise(new OverlayRevisionArchivedDomainEvent(Id, number, clock.UtcNow, by));
    }

    /// <summary>
    /// Archives a Draft or Published revision. Idempotent on Archived
    /// (no event raised, no state change).
    /// </summary>
    public void ArchiveRevision(
        OverlayRevisionNumber number, OperatorIdentifier by, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Revision target = RequireRevision(number);
        if (target.State == OverlayRevisionState.Archived) return;
        bool wasObservable = target.State == OverlayRevisionState.Published;
        DateTimeOffset now = clock.UtcNow;
        target.Archive(now);
        if (wasObservable)
        {
            Raise(new OverlayRevisionArchivedDomainEvent(Id, number, now, by));
        }
    }

    private Revision? CurrentPublishedOrNull() =>
        _revisions.SingleOrDefault(r => r.State == OverlayRevisionState.Published);

    private OverlayRevisionNumber MaxRevisionNumber() =>
        OverlayRevisionNumber.From(_revisions.Max(r => r.Number.Value));

    private Revision RequireRevision(OverlayRevisionNumber number) =>
        _revisions.SingleOrDefault(r => r.Number == number)
            ?? throw new InvalidOperationException(
                $"Overlay {Id} has no revision {number}.");
}
