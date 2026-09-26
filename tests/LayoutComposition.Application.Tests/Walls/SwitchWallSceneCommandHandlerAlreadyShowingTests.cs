using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Walls;

/// <summary>
/// Spec 263 US1 phase-6 remediation (issue #2608): switching to the scene
/// already <c>Showing</c> must stay the no-op <c>Wall.SwitchTo</c> guarantees
/// even once that scene has since lost its Published revision — a retry,
/// resync, or re-issued switch to the status quo is not a state change, so
/// its outcome must not depend on publishability at all. Coverage gap found
/// by code review; <c>SwitchWallSceneCommandHandlerTests.cs</c> (T011) is
/// left untouched.
/// </summary>
public class SwitchWallSceneCommandHandlerAlreadyShowingTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static LayoutIdentifier NewScene() => LayoutIdentifier.New();

    private static SwitchWallSceneCommandHandler Handler(
        InMemoryWallRepository walls, FakeLayoutPublicationLookup lookup, FakeClock clock) =>
        new(walls, lookup, clock, NullLogger<SwitchWallSceneCommandHandler>.Instance);

    [Fact]
    public async Task Switching_to_the_already_Showing_scene_succeeds_even_once_it_lost_its_Published_revision()
    {
        LayoutIdentifier a = NewScene();
        LayoutIdentifier b = NewScene();
        Wall wall = new WallBuilder().WithFab(Munich).WithScenes([a, b]).At(FixedMoment).Build();
        wall.ClearPendingEvents();
        InMemoryWallRepository walls = new();
        walls.Add(wall);
        // `a` (Showing) is archived without any wall switch: it no longer has a
        // Published revision, but the wall was never told to switch away from it.
        FakeLayoutPublicationLookup lookup = new FakeLayoutPublicationLookup()
            .WithLayout(a, Munich, isPublished: false)
            .WithLayout(b, Munich);
        SwitchWallSceneCommandHandler handler = Handler(walls, lookup, new FakeClock(FixedMoment));

        Result<WallDto, SwitchWallSceneError> result = await handler.HandleAsync(
            new SwitchWallSceneCommand([Munich], wall.Id, wall.Version, new SceneTarget.Layout(a), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Showing.ShouldBe(a.Value);
        result.Value.SceneVersion.ShouldBe(0);
        wall.PendingEvents.ShouldBeEmpty();
    }
}
