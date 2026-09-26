using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.LayoutComposition.Domain.Wall.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Wall;

/// <summary>
/// Spec 263 US1 phase-6 remediation (issue #2608): <c>EditScenes</c> must not
/// raise <see cref="WallConfiguredDomainEvent"/> for a resubmission of the
/// identical scene set (same layouts, same order) — <c>PUT</c> is
/// RFC-9110-idempotent and thus auto-retried by this repo's default
/// resilience handler (ADR-0143), so an unconditional raise would duplicate
/// the integration event and its audit row for no actual state change.
/// Coverage gap found by code review; <c>WallTests.cs</c> (T010) is left
/// untouched.
/// </summary>
public class WallEditScenesNoOpTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static LayoutIdentifier NewScene() => LayoutIdentifier.New();

    private static OperatorIdentifier NewOperator() => OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public void EditScenes_resubmitting_the_identical_scene_set_raises_no_events()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Domain.Wall.Wall wall = new WallBuilder().WithScenes([a, b]).At(FixedMoment).Build();
        wall.ClearPendingEvents();
        Domain.Wall.SceneVersion versionBefore = wall.SceneVersion;

        wall.EditScenes([a, b], NewOperator(), new LayoutBuilder.TestClock(FixedMoment));

        wall.PendingEvents.ShouldBeEmpty();
        wall.Scenes.ShouldBe([a, b]);
        wall.Showing.ShouldBe(a);
        wall.SceneVersion.ShouldBe(versionBefore);
    }
}
