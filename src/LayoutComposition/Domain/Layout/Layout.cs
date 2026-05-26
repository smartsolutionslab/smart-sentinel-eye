using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Aggregate root for a logical Layout chain (spec 003). One per logical
/// layout the operator sees by name; owns a collection of
/// <see cref="Revision"/> sub-entities. The chain invariant
/// <em>at-most-one-Published-revision-per-chain</em> (FR-002) is
/// enforced inside this aggregate's transaction; the partial unique
/// index in Postgres is a belt-and-braces backstop.
/// </summary>
public sealed class Layout : AggregateRoot<LayoutIdentifier>
{
    private readonly List<Revision> _revisions = new();

    public LayoutName Name { get; private set; } = null!;

    public IReadOnlyList<Revision> Revisions => _revisions;

    public DateTimeOffset CreatedAt { get; private set; }

    public OperatorIdentifier CreatedBy { get; private set; }

    private Layout() { }

    /// <summary>
    /// Mints a new logical Layout chain with its first revision in
    /// <c>Draft</c> state. No domain event is raised — drafts are not
    /// observable to kiosks; the first observable transition is Publish.
    /// </summary>
    public static Layout CreateDraft(
        LayoutName name,
        CameraIdentifier camera,
        OperatorIdentifier createdBy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(clock);

        DateTimeOffset now = clock.UtcNow;
        Layout layout = new()
        {
            Id = LayoutIdentifier.New(),
            Name = name,
            CreatedAt = now,
            CreatedBy = createdBy,
        };
        layout._revisions.Add(
            Revision.NewDraft(LayoutRevisionNumber.One, camera, now, createdBy));
        return layout;
    }

    /// <summary>
    /// Branches a new Draft revision off the chain's current Published
    /// revision (spec 003 US4). Pre-fills the camera from the prior
    /// revision so the editor can mutate from a known-good baseline.
    /// </summary>
    public Revision BranchDraft(OperatorIdentifier by, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Revision baseRevision = CurrentPublishedOrNull()
            ?? throw new InvalidOperationException(
                "BranchDraft requires a currently-Published revision to copy from.");

        LayoutRevisionNumber next = MaxRevisionNumber().Next();
        Revision draft = Revision.Branch(next, baseRevision.Camera, clock.UtcNow, by);
        _revisions.Add(draft);
        return draft;
    }

    /// <summary>
    /// In-place edit of an existing Draft revision (spec 003 FR-005).
    /// Drafts can be mutated without spawning further revisions.
    /// </summary>
    public void EditDraft(
        LayoutRevisionNumber number, CameraIdentifier camera, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Revision target = RequireRevision(number);
        target.EditCamera(camera);
    }

    /// <summary>
    /// Publishes a Draft revision. Atomically archives the previously-
    /// Published sibling revision (FR-003), preserving the
    /// at-most-one-Published invariant. Raises both
    /// <see cref="LayoutRevisionPublishedDomainEvent"/> and (when
    /// applicable) <see cref="LayoutRevisionArchivedDomainEvent"/>.
    /// </summary>
    public void Publish(LayoutRevisionNumber number, OperatorIdentifier by, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Revision target = RequireRevision(number);
        Revision? prior = CurrentPublishedOrNull();
        DateTimeOffset now = clock.UtcNow;

        target.Publish(now);
        if (prior is not null && prior.Number != number)
        {
            prior.Archive(now);
            Raise(new LayoutRevisionArchivedDomainEvent(Id, prior.Number, now, by));
        }
        Raise(new LayoutRevisionPublishedDomainEvent(
            Id, number, Name, target.Camera, now, by));
    }

    /// <summary>
    /// Reverts a Published revision to Draft so the admin can edit it in
    /// place without spawning a new revision. Raises an Archived event
    /// for downstream subscribers so kiosks force-disconnect.
    /// </summary>
    public void Revert(LayoutRevisionNumber number, OperatorIdentifier by, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Revision target = RequireRevision(number);
        target.Revert();
        Raise(new LayoutRevisionArchivedDomainEvent(Id, number, clock.UtcNow, by));
    }

    /// <summary>
    /// Archives a Draft or Published revision. Idempotent on Archived
    /// (no event raised, no state change).
    /// </summary>
    public void ArchiveRevision(
        LayoutRevisionNumber number, OperatorIdentifier by, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Revision target = RequireRevision(number);
        if (target.State == LayoutRevisionState.Archived) return;
        bool wasObservable = target.State == LayoutRevisionState.Published;
        DateTimeOffset now = clock.UtcNow;
        target.Archive(now);
        if (wasObservable)
        {
            Raise(new LayoutRevisionArchivedDomainEvent(Id, number, now, by));
        }
    }

    private Revision? CurrentPublishedOrNull() =>
        _revisions.SingleOrDefault(r => r.State == LayoutRevisionState.Published);

    private LayoutRevisionNumber MaxRevisionNumber() =>
        LayoutRevisionNumber.From(_revisions.Max(r => r.Number.Value));

    private Revision RequireRevision(LayoutRevisionNumber number) =>
        _revisions.SingleOrDefault(r => r.Number == number)
            ?? throw new InvalidOperationException(
                $"Layout {Id} has no revision {number}.");
}
