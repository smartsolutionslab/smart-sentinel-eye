using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.LayoutComposition.Domain.Wall.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Wall;

/// <summary>
/// Spec 258 US1 (ADR-0157, ADR-0158, plan.md §2). Covers FR-001..FR-004 and
/// PD-5/PD-6: scene-set validation (<see cref="Domain.Wall.Wall.ValidateScenes"/>),
/// the <c>Showing ∈ Scenes</c> invariant, the Next wrap/skip/no-op behaviours,
/// and <c>EditScenes</c>' Reconfigured switch (US1-15).
///
/// <para>
/// Illegal state transitions (a target outside the scene set, or outside the
/// publishable set) are programmer errors and throw
/// <see cref="InvalidOperationException"/>, mirroring <c>Layout</c>'s own
/// two-tier pattern: the command handler validates first and maps to an
/// operator-facing <c>Result</c> failure (T011, a parallel test file); this
/// aggregate throws only as the backstop for a caller that skipped that check.
/// </para>
/// </summary>
public class WallTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static LayoutIdentifier NewScene() => LayoutIdentifier.New();

    private static OperatorIdentifier NewOperator() => OperatorIdentifier.From(Guid.CreateVersion7());

    private static HashSet<LayoutIdentifier> AllPublishable(IReadOnlyList<LayoutIdentifier> scenes) =>
        scenes.ToHashSet();

    private static SceneSwitchCause.Operator OperatorCause() => new(NewOperator());

    [Fact]
    public void Create_shows_the_first_scene_at_SceneVersion_Initial()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();

        Domain.Wall.Wall wall = Domain.Wall.Wall.Create(
            Munich, WallName.From("Line 3 rotation"), [a, b], NewOperator(), new LayoutBuilder.TestClock(FixedMoment));

        wall.Showing.ShouldBe(a);
        wall.SceneVersion.ShouldBe(SceneVersion.Initial);
        wall.Scenes.ShouldBe([a, b]);
    }

    [Fact]
    public void Create_raises_WallConfiguredDomainEvent_naming_the_first_scene_as_Showing()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        OperatorIdentifier by = NewOperator();

        Domain.Wall.Wall wall = Domain.Wall.Wall.Create(
            Munich, WallName.From("Line 3 rotation"), [a, b], by, new LayoutBuilder.TestClock(FixedMoment));

        WallConfiguredDomainEvent raised = wall.PendingEvents.OfType<WallConfiguredDomainEvent>().Single();
        raised.Wall.ShouldBe(wall.Id);
        raised.Fab.ShouldBe(Munich);
        raised.Scenes.ShouldBe([a, b]);
        raised.Showing.ShouldBe(a);
        raised.By.ShouldBe(by);
    }

    [Fact]
    public void ValidateScenes_flags_a_single_scene_as_TooFew()
    {
        Option<SceneSetViolation> violation = Domain.Wall.Wall.ValidateScenes([NewScene()]);

        violation.HasValue.ShouldBeTrue();
        violation.Value.ShouldBe(SceneSetViolation.TooFew);
    }

    [Fact]
    public void ValidateScenes_flags_nine_distinct_scenes_as_TooMany()
    {
        IReadOnlyList<LayoutIdentifier> nine = [.. Enumerable.Range(0, 9).Select(_ => NewScene())];

        Option<SceneSetViolation> violation = Domain.Wall.Wall.ValidateScenes(nine);

        violation.HasValue.ShouldBeTrue();
        violation.Value.ShouldBe(SceneSetViolation.TooMany);
    }

    [Fact]
    public void ValidateScenes_flags_a_duplicate_scene()
    {
        LayoutIdentifier a = NewScene();

        Option<SceneSetViolation> violation = Domain.Wall.Wall.ValidateScenes([a, a]);

        violation.HasValue.ShouldBeTrue();
        violation.Value.ShouldBe(SceneSetViolation.Duplicate);
    }

    [Fact]
    public void ValidateScenes_accepts_a_valid_set_of_two_to_eight_unique_scenes()
    {
        IReadOnlyList<LayoutIdentifier> eight = [.. Enumerable.Range(0, 8).Select(_ => NewScene())];

        Domain.Wall.Wall.ValidateScenes([NewScene(), NewScene()]).HasValue.ShouldBeFalse();
        Domain.Wall.Wall.ValidateScenes(eight).HasValue.ShouldBeFalse();
    }

    [Fact]
    public void SwitchTo_Next_wraps_from_the_last_scene_back_to_the_first()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Domain.Wall.Wall wall = new WallBuilder().WithScenes([a, b, c]).At(FixedMoment).Build();
        wall.ClearPendingEvents();
        wall.SwitchTo(new SceneTarget.Layout(c), AllPublishable([a, b, c]), OperatorCause(), new LayoutBuilder.TestClock(FixedMoment));
        wall.ClearPendingEvents();

        Option<WallSceneSwitchedDomainEvent> result = wall.SwitchTo(
            new SceneTarget.Next(), AllPublishable([a, b, c]), OperatorCause(), new LayoutBuilder.TestClock(FixedMoment));

        result.HasValue.ShouldBeTrue();
        wall.Showing.ShouldBe(a);
    }

    [Fact]
    public void SwitchTo_Next_skips_a_scene_that_is_not_publishable()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Domain.Wall.Wall wall = new WallBuilder().WithScenes([a, b, c]).At(FixedMoment).Build();
        wall.ClearPendingEvents();

        // b has since lost its Published revision (PD-6): Next must skip it and land on c.
        IReadOnlySet<LayoutIdentifier> publishable = new HashSet<LayoutIdentifier> { a, c };
        Option<WallSceneSwitchedDomainEvent> result = wall.SwitchTo(
            new SceneTarget.Next(), publishable, OperatorCause(), new LayoutBuilder.TestClock(FixedMoment));

        result.HasValue.ShouldBeTrue();
        wall.Showing.ShouldBe(c);
    }

    [Fact]
    public void SwitchTo_Next_is_a_no_op_when_nothing_else_is_publishable()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Domain.Wall.Wall wall = new WallBuilder().WithScenes([a, b]).At(FixedMoment).Build();
        wall.ClearPendingEvents();

        IReadOnlySet<LayoutIdentifier> publishable = new HashSet<LayoutIdentifier> { a };
        Option<WallSceneSwitchedDomainEvent> result = wall.SwitchTo(
            new SceneTarget.Next(), publishable, OperatorCause(), new LayoutBuilder.TestClock(FixedMoment));

        result.HasValue.ShouldBeFalse();
        wall.Showing.ShouldBe(a);
        wall.SceneVersion.ShouldBe(SceneVersion.Initial);
        wall.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void SwitchTo_Layout_already_Showing_is_a_no_op()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Domain.Wall.Wall wall = new WallBuilder().WithScenes([a, b]).At(FixedMoment).Build();
        wall.ClearPendingEvents();

        Option<WallSceneSwitchedDomainEvent> result = wall.SwitchTo(
            new SceneTarget.Layout(a), AllPublishable([a, b]), OperatorCause(), new LayoutBuilder.TestClock(FixedMoment));

        result.HasValue.ShouldBeFalse();
        wall.Showing.ShouldBe(a);
        wall.SceneVersion.ShouldBe(SceneVersion.Initial);
        wall.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void SwitchTo_Layout_not_in_the_scene_set_throws()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier outside = NewScene();
        Domain.Wall.Wall wall = new WallBuilder().WithScenes([a, b]).At(FixedMoment).Build();

        Action act = () => wall.SwitchTo(
            new SceneTarget.Layout(outside), AllPublishable([a, b]), OperatorCause(), new LayoutBuilder.TestClock(FixedMoment));

        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void SwitchTo_Layout_in_the_set_but_not_publishable_throws()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Domain.Wall.Wall wall = new WallBuilder().WithScenes([a, b]).At(FixedMoment).Build();

        IReadOnlySet<LayoutIdentifier> publishable = new HashSet<LayoutIdentifier> { a };
        Action act = () => wall.SwitchTo(
            new SceneTarget.Layout(b), publishable, OperatorCause(), new LayoutBuilder.TestClock(FixedMoment));

        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void SceneVersion_advances_by_exactly_one_on_a_real_switch_and_never_on_a_no_op()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Domain.Wall.Wall wall = new WallBuilder().WithScenes([a, b]).At(FixedMoment).Build();
        wall.ClearPendingEvents();

        wall.SwitchTo(new SceneTarget.Layout(a), AllPublishable([a, b]), OperatorCause(), new LayoutBuilder.TestClock(FixedMoment));
        wall.SceneVersion.ShouldBe(SceneVersion.Initial);

        wall.SwitchTo(new SceneTarget.Layout(b), AllPublishable([a, b]), OperatorCause(), new LayoutBuilder.TestClock(FixedMoment));
        wall.SceneVersion.ShouldBe(SceneVersion.Initial.Next());
    }

    [Fact]
    public void SwitchTo_a_real_change_raises_and_returns_WallSceneSwitchedDomainEvent()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Domain.Wall.Wall wall = new WallBuilder().WithScenes([a, b]).At(FixedMoment).Build();
        wall.ClearPendingEvents();
        SceneSwitchCause.Operator cause = OperatorCause();

        Option<WallSceneSwitchedDomainEvent> result = wall.SwitchTo(
            new SceneTarget.Layout(b), AllPublishable([a, b]), cause, new LayoutBuilder.TestClock(FixedMoment));

        result.HasValue.ShouldBeTrue();
        WallSceneSwitchedDomainEvent raised = wall.PendingEvents.OfType<WallSceneSwitchedDomainEvent>().Single();
        raised.ShouldBe(result.Value);
        raised.Wall.ShouldBe(wall.Id);
        raised.Previous.ShouldBe(a);
        raised.Current.ShouldBe(b);
        raised.SceneVersion.ShouldBe(SceneVersion.Initial.Next());
        raised.Cause.ShouldBe(cause);
    }

    [Fact]
    public void EditScenes_that_drops_the_Showing_scene_moves_the_pointer_to_the_new_first_scene_and_raises_Reconfigured()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Domain.Wall.Wall wall = new WallBuilder().WithScenes([a, b, c]).At(FixedMoment).Build();
        // Move Showing to b so the edit below genuinely drops the current pointer.
        wall.SwitchTo(new SceneTarget.Layout(b), AllPublishable([a, b, c]), OperatorCause(), new LayoutBuilder.TestClock(FixedMoment));
        wall.ClearPendingEvents();
        OperatorIdentifier editor = NewOperator();

        wall.EditScenes([a, c], editor, new LayoutBuilder.TestClock(FixedMoment));

        wall.Showing.ShouldBe(a);
        WallSceneSwitchedDomainEvent switched = wall.PendingEvents.OfType<WallSceneSwitchedDomainEvent>().Single();
        switched.Previous.ShouldBe(b);
        switched.Current.ShouldBe(a);
        SceneSwitchCause.Reconfigured cause = switched.Cause.ShouldBeOfType<SceneSwitchCause.Reconfigured>();
        cause.By.ShouldBe(editor);
        wall.PendingEvents.OfType<WallConfiguredDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void EditScenes_that_keeps_Showing_in_the_new_set_raises_no_Reconfigured_switch()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        LayoutIdentifier c = NewScene();
        Domain.Wall.Wall wall = new WallBuilder().WithScenes([a, b]).At(FixedMoment).Build();
        wall.ClearPendingEvents();

        wall.EditScenes([a, c], NewOperator(), new LayoutBuilder.TestClock(FixedMoment));

        wall.Showing.ShouldBe(a);
        wall.PendingEvents.OfType<WallSceneSwitchedDomainEvent>().ShouldBeEmpty();
        wall.PendingEvents.OfType<WallConfiguredDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Create_rejects_a_null_name()
    {
        Action act = () => Domain.Wall.Wall.Create(
            Munich, name: null!, [NewScene(), NewScene()], NewOperator(), new LayoutBuilder.TestClock(FixedMoment));
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void Create_rejects_a_null_clock()
    {
        Action act = () => Domain.Wall.Wall.Create(
            Munich, WallName.From("Line 3 rotation"), [NewScene(), NewScene()], NewOperator(), clock: null!);
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void EditScenes_rejects_a_null_clock()
    {
        Domain.Wall.Wall wall = new WallBuilder().At(FixedMoment).Build();
        Action act = () => wall.EditScenes([NewScene(), NewScene()], NewOperator(), clock: null!);
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void SwitchTo_rejects_a_null_clock()
    {
        Domain.Wall.Wall wall = new WallBuilder().At(FixedMoment).Build();
        Action act = () => wall.SwitchTo(
            new SceneTarget.Next(), AllPublishable(wall.Scenes), OperatorCause(), clock: null!);
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void ValidateScenes_rejects_a_null_scene_list()
    {
        Action act = () => Domain.Wall.Wall.ValidateScenes(null!);
        act.ShouldThrow<ArgumentNullException>();
    }
}
