using System.Globalization;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.CameraCatalog.Domain.Tests.Camera.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 028 T004 — the transition <c>CameraStatus.Decommissioned</c> existed
/// for without ever being reachable (#1433).
/// </summary>
public class CameraRetirementTests
{
    private static readonly DateTimeOffset RetiredAt =
        DateTimeOffset.Parse("2026-08-24T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Operator =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public void Retiring_a_camera_moves_it_to_the_terminal_state()
    {
        Domain.Camera.Camera camera = new CameraBuilder().Build();

        camera.Retire(Operator, new FixedClock(RetiredAt));

        camera.Status.ShouldBe(CameraStatus.Decommissioned);
    }

    [Fact]
    public void Retiring_a_camera_raises_the_retirement()
    {
        Domain.Camera.Camera camera = new CameraBuilder().WithFab("dresden").WithName("line-3-inlet").Build();
        camera.ClearPendingEvents();

        camera.Retire(Operator, new FixedClock(RetiredAt));

        CameraRetiredDomainEvent retired = camera.PendingEvents
            .OfType<CameraRetiredDomainEvent>()
            .ShouldHaveSingleItem();

        retired.Camera.ShouldBe(camera.Id);
        retired.Fab.Value.ShouldBe("dresden");
        retired.Name.Value.ShouldBe("line-3-inlet");
        retired.RetiredAt.ShouldBe(RetiredAt);
        retired.RetiredBy.ShouldBe(Operator);
    }

    /// <summary>
    /// FR-005, and the assertion that has to be about the <b>event</b> rather
    /// than the return. A second retire that quietly succeeds while raising
    /// again announces two retirements: every consumer sees the camera retired
    /// twice and the audit trail records it, while the endpoint still answers
    /// 204 and looks entirely correct.
    /// </summary>
    [Fact]
    public void Retiring_an_already_retired_camera_raises_nothing_further()
    {
        Domain.Camera.Camera camera = new CameraBuilder().Build();
        camera.Retire(Operator, new FixedClock(RetiredAt));
        camera.ClearPendingEvents();

        camera.Retire(Operator, new FixedClock(RetiredAt.AddMinutes(5)));

        camera.PendingEvents.ShouldBeEmpty();
        camera.Status.ShouldBe(CameraStatus.Decommissioned);
    }

    /// <summary>
    /// Retirement records that the hardware is gone; it does not erase the
    /// history that it was there. The name in particular is kept — it is what
    /// the retirement event reports as released, and what an audit trail needs
    /// to explain a name that later belongs to a different camera.
    /// </summary>
    [Fact]
    public void A_retired_camera_keeps_what_it_was()
    {
        Domain.Camera.Camera camera = new CameraBuilder()
            .WithFab("dresden")
            .WithName("line-3-inlet")
            .WithUrl("rtsp://10.0.0.9:554/h264")
            .Build();

        camera.Retire(Operator, new FixedClock(RetiredAt));

        camera.Fab.Value.ShouldBe("dresden");
        camera.Name.Value.ShouldBe("line-3-inlet");
        camera.Url.Value.ShouldBe("rtsp://10.0.0.9:554/h264");
    }

    /// <summary>
    /// Terminality is asserted as behaviour, not as the absence of an API.
    /// Registering afresh is how replacement hardware arrives, and it produces
    /// a <em>different</em> camera — the retired one stays retired.
    ///
    /// <para>
    /// There is deliberately no test enumerating the aggregate's public methods
    /// to prove nothing un-retires. It would fail the moment a legitimate
    /// behaviour is added — camera editing is already filed as #1435 — and a
    /// test that misfires on unrelated work gets weakened or deleted, taking
    /// whatever it did guard with it.
    /// </para>
    /// </summary>
    [Fact]
    public void Registering_a_replacement_leaves_the_retired_camera_retired()
    {
        Domain.Camera.Camera retired = new CameraBuilder().WithName("line-3-inlet").Build();
        retired.Retire(Operator, new FixedClock(RetiredAt));

        Domain.Camera.Camera replacement = new CameraBuilder().WithName("line-3-inlet").Build();

        replacement.Id.ShouldNotBe(retired.Id);
        replacement.Status.ShouldBe(CameraStatus.Registered);
        retired.Status.ShouldBe(CameraStatus.Decommissioned);
    }

    private sealed class FixedClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
