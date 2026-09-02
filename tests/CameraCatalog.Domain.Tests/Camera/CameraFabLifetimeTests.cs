using System.Globalization;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.CameraCatalog.Domain.Tests.Camera.Builders;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 015 T005 — a camera's fab is fixed at registration.
///
/// <para>
/// Written narrower than T005 asked for, because at the time <b>the aggregate
/// had no decommission behaviour</b>: `CameraStatus` carried a `Decommissioned`
/// value and nothing in the context ever transitioned to it (#1433). There was
/// no state change to survive, so a test claiming otherwise would have asserted
/// against a transition that could not happen.
/// </para>
///
/// <para>
/// Spec 028 added `Retire`, so the survival case now exists and has landed
/// here, as the original note said it should.
/// </para>
/// </summary>
public class CameraFabLifetimeTests
{
    [Fact]
    public void A_camera_carries_the_fab_it_was_registered_in()
    {
        Domain.Camera.Camera camera = new CameraBuilder().WithFab("dresden").Build();

        // dresden, not munich: the builder defaults to munich, so asserting
        // that would pass even if WithFab were ignored entirely.
        camera.Fab.Value.ShouldBe("dresden");
    }

    [Fact]
    public void Two_cameras_registered_in_different_fabs_keep_their_own()
    {
        Domain.Camera.Camera munich = new CameraBuilder().WithFab("munich").Build();
        Domain.Camera.Camera dresden = new CameraBuilder().WithFab("dresden").Build();

        munich.Fab.Value.ShouldBe("munich");
        dresden.Fab.Value.ShouldBe("dresden");
        munich.Fab.ShouldNotBe(dresden.Fab);
    }

    /// <summary>
    /// The case spec 015 T005 originally asked for and could not have: a
    /// camera's fab survives registration → retirement. It was impossible
    /// while nothing could reach <c>Decommissioned</c> (#1433); spec 028's
    /// <c>Retire</c> is what makes it assertable, so it lands here as the note
    /// above said it should.
    /// </summary>
    [Fact]
    public void A_retired_camera_keeps_the_fab_it_was_registered_in()
    {
        Domain.Camera.Camera camera = new CameraBuilder().WithFab("dresden").Build();

        camera.Retire(OperatorIdentifier.From(Guid.CreateVersion7()), new FixedClock());

        camera.Status.ShouldBe(CameraStatus.Decommissioned);
        camera.Fab.Value.ShouldBe("dresden");
    }

    [Fact]
    public void Registering_without_a_fab_is_refused()
    {
        // The guard in Register, not a nullable column discovered later. A
        // camera with no fab is invisible to every operator (FR-005), which is
        // a worse failure than being refused at the boundary.
        Should.Throw<ArgumentException>(() => Domain.Camera.Camera.Register(
            null!,
            CameraName.From("Line-1-North"),
            RtspUrl.From("rtsp://10.0.5.12/h264"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FixedClock()));
    }

    [Fact]
    public void The_aggregate_exposes_no_way_to_move_a_camera_between_fabs()
    {
        // Structural: relocation is out of scope by decision (FR-004) — a
        // camera is bolted to a wall in one building, and moving the record
        // would silently change which plant's video an operator reaches.
        // Asserts the setter is not public rather than that no method mentions
        // "Fab", which would match the property getter and pass vacuously.
        System.Reflection.PropertyInfo fab =
            typeof(Domain.Camera.Camera).GetProperty("Fab")!;

        fab.ShouldNotBeNull();
        fab.SetMethod?.IsPublic.ShouldNotBe(true);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            DateTimeOffset.Parse("2026-05-25T10:00:00Z", CultureInfo.InvariantCulture);
    }
}
