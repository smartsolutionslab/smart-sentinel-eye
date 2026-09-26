using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// Aggregate root for a rotating display surface (spec 258 US1, PD-2). Owns
/// both the ordered scene set and the pointer to the scene currently
/// showing, because the invariant <c>Showing ∈ Scenes</c> spans both — one
/// aggregate gives one transaction, one <see cref="AggregateRoot{TIdentifier}.Version"/>
/// for <c>If-Match</c> (ADR-0043/0113).
///
/// <para>
/// Illegal state transitions (a target outside the scene set, or outside the
/// publishable set) are programmer errors and throw
/// <see cref="InvalidOperationException"/>, mirroring <see cref="Layout.Layout"/>'s
/// own two-tier pattern: the command handler validates first and maps to an
/// operator-facing <c>Result</c> failure; this aggregate throws only as the
/// backstop for a caller that skipped that check.
/// </para>
///
/// <para>
/// <see cref="Creation"/> reuses <see cref="Layout.Creation"/> rather than
/// declaring a second, identical composite: both aggregates live in this one
/// bounded context, so this is not the cross-context duplication constitution
/// §III requires elsewhere (e.g. Automation's own <c>FabIdentifier</c>) — it is
/// the same duplication <see cref="Layout.Revision"/> already declines by
/// reusing its owning chain's <see cref="Layout.Creation"/>.
/// </para>
/// </summary>
public sealed class Wall : AggregateRoot<WallIdentifier>
{
    /// <summary>PD-5: a wall with one scene has nothing to switch between.</summary>
    public const int MinScenes = 2;

    /// <summary>PD-5: a UI-scale guess with no measurement behind it; unrelated to any latency budget.</summary>
    public const int MaxScenes = 8;

    private readonly List<LayoutIdentifier> scenes = [];

    /// <summary>The fab this wall belongs to, fixed at creation (spec 017 precedent).</summary>
    public FabIdentifier Fab { get; private set; } = null!;

    public WallName Name { get; private set; } = null!;

    /// <summary>The ordered, deduplicated set of scenes (FR-002).</summary>
    public IReadOnlyList<LayoutIdentifier> Scenes => scenes;

    /// <summary>Invariant: always a member of <see cref="Scenes"/> (FR-003).</summary>
    public LayoutIdentifier Showing { get; private set; }

    public SceneVersion SceneVersion { get; private set; } = SceneVersion.Initial;

    public ShowingSince ShowingSince { get; private set; } = null!;

    public Creation Creation { get; private set; } = null!;

    private Wall() { }

    /// <summary>
    /// The single source of truth for FR-002's count and duplicate
    /// invariants. Handlers call it first to map a violation to a
    /// <c>WALL_*</c> <c>400</c>; the aggregate calls it again as a backstop.
    /// </summary>
    public static Option<SceneSetViolation> ValidateScenes(IReadOnlyList<LayoutIdentifier> scenes)
    {
        Ensure.That(scenes).IsNotNull();

        if (scenes.Count < MinScenes)
        {
            return Option<SceneSetViolation>.Some(SceneSetViolation.TooFew);
        }
        if (scenes.Count > MaxScenes)
        {
            return Option<SceneSetViolation>.Some(SceneSetViolation.TooMany);
        }
        if (scenes.Distinct().Count() != scenes.Count)
        {
            return Option<SceneSetViolation>.Some(SceneSetViolation.Duplicate);
        }
        return Option<SceneSetViolation>.None;
    }

    private static void RequireValidScenes(IReadOnlyList<LayoutIdentifier> scenes)
    {
        Option<SceneSetViolation> violation = ValidateScenes(scenes);
        if (violation.HasValue)
        {
            throw new InvalidOperationException(
                $"Scene set of {scenes.Count} scene(s) violates {violation.Value}.");
        }
    }

    /// <summary>
    /// Mints a new Wall showing its first scene. Raises
    /// <see cref="WallConfiguredDomainEvent"/> — a wall is observable to the
    /// kiosk from the moment it exists, unlike a Layout draft.
    /// </summary>
    public static Wall Create(
        FabIdentifier fab,
        WallName name,
        IReadOnlyList<LayoutIdentifier> scenes,
        OperatorIdentifier createdBy,
        IClock clock)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();
        Ensure.That(scenes).IsNotNull();
        Ensure.That(clock).IsNotNull();
        RequireValidScenes(scenes);

        DateTimeOffset now = clock.UtcNow;
        LayoutIdentifier first = scenes[0];
        Wall wall = new()
        {
            Id = WallIdentifier.New(),
            Fab = fab,
            Name = name,
            Showing = first,
            SceneVersion = SceneVersion.Initial,
            ShowingSince = ShowingSince.From(now),
            Creation = Creation.From(CreatedAt.From(now), createdBy),
        };
        wall.scenes.AddRange(scenes);
        wall.Raise(new WallConfiguredDomainEvent(fab, wall.Id, name, [.. wall.Scenes], first, now, createdBy));
        return wall;
    }

    /// <summary>
    /// Replaces the scene set atomically (US1-15). If <c>Showing</c> is not a
    /// member of the new set, the pointer moves to the new first scene and a
    /// <see cref="WallSceneSwitchedDomainEvent"/> with
    /// <see cref="SceneSwitchCause.Reconfigured"/> is raised alongside the
    /// unconditional <see cref="WallConfiguredDomainEvent"/>.
    /// </summary>
    public void EditScenes(IReadOnlyList<LayoutIdentifier> newScenes, OperatorIdentifier by, IClock clock)
    {
        Ensure.That(newScenes).IsNotNull();
        Ensure.That(clock).IsNotNull();
        RequireValidScenes(newScenes);

        if (scenes.SequenceEqual(newScenes))
        {
            // Identical to the current set (same layouts, same order): nothing
            // changed, so raise nothing. Otherwise an idempotent-retried PUT
            // (ADR-0143) would duplicate WallConfiguredV1 and its audit row for
            // no actual state change, mirroring SwitchTo's own no-op guard.
            return;
        }

        DateTimeOffset now = clock.UtcNow;
        scenes.Clear();
        scenes.AddRange(newScenes);

        if (!scenes.Contains(Showing))
        {
            LayoutIdentifier previous = Showing;
            LayoutIdentifier next = scenes[0];
            Showing = next;
            SceneVersion = SceneVersion.Next();
            ShowingSince = ShowingSince.From(now);
            Raise(new WallSceneSwitchedDomainEvent(
                Fab, Id, previous, next, SceneVersion, new SceneSwitchCause.Reconfigured(by), now));
        }

        Raise(new WallConfiguredDomainEvent(Fab, Id, Name, [.. scenes], Showing, now, by));
    }

    /// <summary>
    /// Applies a switch request (FR-004, PD-6). Publishability is computed
    /// by the handler through <c>ILayoutPublicationLookup</c> and passed in —
    /// the domain stays free of I/O, and a race between that read and this
    /// commit is the same window PD-6's "showing scene became unpublished"
    /// case already covers.
    /// </summary>
    public Option<WallSceneSwitchedDomainEvent> SwitchTo(
        SceneTarget target,
        IReadOnlySet<LayoutIdentifier> publishable,
        SceneSwitchCause cause,
        IClock clock)
    {
        Ensure.That(target).IsNotNull();
        Ensure.That(publishable).IsNotNull();
        Ensure.That(cause).IsNotNull();
        Ensure.That(clock).IsNotNull();

        LayoutIdentifier? resolved = target switch
        {
            SceneTarget.Layout layout => ResolveLayoutTarget(layout.Value, publishable),
            SceneTarget.Next => ResolveNextTarget(publishable),
            _ => throw new InvalidOperationException($"Unknown SceneTarget '{target}'."),
        };

        if (resolved is not { } next || next == Showing)
        {
            // Already showing it, or Next found nothing else publishable (PD-6):
            // both are a no-op. Nothing changes and nothing is raised (FR-004).
            return Option<WallSceneSwitchedDomainEvent>.None;
        }

        DateTimeOffset now = clock.UtcNow;
        LayoutIdentifier previous = Showing;
        Showing = next;
        SceneVersion = SceneVersion.Next();
        ShowingSince = ShowingSince.From(now);

        WallSceneSwitchedDomainEvent raised = new(Fab, Id, previous, next, SceneVersion, cause, now);
        Raise(raised);
        return Option<WallSceneSwitchedDomainEvent>.Some(raised);
    }

    private LayoutIdentifier ResolveLayoutTarget(LayoutIdentifier target, IReadOnlySet<LayoutIdentifier> publishable)
    {
        if (!scenes.Contains(target))
        {
            throw new InvalidOperationException($"Wall {Id} has no scene {target}.");
        }
        if (!publishable.Contains(target))
        {
            throw new InvalidOperationException($"Scene {target} on wall {Id} is not publishable.");
        }
        return target;
    }

    /// <summary>
    /// Walks cyclically from <see cref="Showing"/>, skipping any scene not in
    /// <paramref name="publishable"/> (PD-6). Returns <see langword="null"/>
    /// if the walk comes full circle back to <see cref="Showing"/> without
    /// finding another publishable scene.
    /// </summary>
    private LayoutIdentifier? ResolveNextTarget(IReadOnlySet<LayoutIdentifier> publishable)
    {
        int startIndex = scenes.IndexOf(Showing);
        for (int offset = 1; offset <= scenes.Count; offset++)
        {
            LayoutIdentifier candidate = scenes[(startIndex + offset) % scenes.Count];
            if (candidate == Showing)
            {
                return null;
            }
            if (publishable.Contains(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
