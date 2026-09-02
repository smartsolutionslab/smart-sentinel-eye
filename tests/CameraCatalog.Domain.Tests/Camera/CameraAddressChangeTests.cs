using System.Globalization;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.CameraCatalog.Domain.Tests.Camera.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 029 T012 — correcting a camera's address (FR-003, FR-005).
///
/// <para>
/// Two of these assert the things that look identical from the endpoint and
/// differ only downstream: that a no-op change raises nothing, and that a
/// retired camera is refused by the <em>aggregate</em> rather than by whatever
/// happens to call it.
/// </para>
/// </summary>
public class CameraAddressChangeTests
{
    private static readonly DateTimeOffset ChangedAt =
        DateTimeOffset.Parse("2026-08-24T11:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Operator =
        OperatorIdentifier.From(Guid.CreateVersion7());

    private const string OriginalUrl = "rtsp://10.0.5.12/h264";
    private const string CorrectedUrl = "rtsp://10.0.5.44/h264";

    [Fact]
    public void Correcting_the_address_replaces_it()
    {
        Domain.Camera.Camera camera = new CameraBuilder().WithUrl(OriginalUrl).Build();

        camera.ChangeAddress(RtspUrl.From(CorrectedUrl), Operator, new FixedClock(ChangedAt));

        camera.Url.Value.ShouldBe(CorrectedUrl);
    }

    [Fact]
    public void Correcting_the_address_raises_one_event_carrying_both_addresses()
    {
        Domain.Camera.Camera camera = new CameraBuilder().WithFab("munich").WithUrl(OriginalUrl).Build();
        camera.ClearPendingEvents();

        camera.ChangeAddress(RtspUrl.From(CorrectedUrl), Operator, new FixedClock(ChangedAt));

        CameraAddressChangedDomainEvent changed = camera.PendingEvents
            .OfType<CameraAddressChangedDomainEvent>()
            .ShouldHaveSingleItem();

        changed.Camera.ShouldBe(camera.Id);
        changed.Fab.Value.ShouldBe("munich");
        changed.ChangedBy.ShouldBe(Operator);
        changed.ChangedAt.ShouldBe(ChangedAt);

        // Both addresses, not just the new one. Without the previous value the
        // audit trail records that something happened rather than what, and a
        // subscriber cannot tell a real move from a redelivery.
        changed.PreviousUrl.Value.ShouldBe(OriginalUrl);
        changed.Url.Value.ShouldBe(CorrectedUrl);
    }

    /// <summary>
    /// Idempotency as no <em>event</em>, not merely no error. Raising here
    /// would put a second row in the audit trail for a change that did not
    /// happen and would tell stream distribution to re-point a path that never
    /// moved — while the endpoint answered 204 either way. The count is the
    /// only place the difference shows, which is why it is what is asserted.
    /// </summary>
    [Fact]
    public void Re_submitting_the_address_it_already_has_raises_nothing()
    {
        Domain.Camera.Camera camera = new CameraBuilder().WithUrl(OriginalUrl).Build();
        camera.ClearPendingEvents();

        camera.ChangeAddress(RtspUrl.From(OriginalUrl), Operator, new FixedClock(ChangedAt));

        camera.PendingEvents.ShouldBeEmpty();
        camera.Url.Value.ShouldBe(OriginalUrl);
    }

    /// <summary>
    /// FR-005, asserted on the aggregate. A guard that lived only in the
    /// command handler would be bypassed by the next caller, and a rule
    /// enforced in one layer but not another is exactly the defect spec 028
    /// shipped and had to fix.
    /// </summary>
    [Fact]
    public void A_retired_cameras_address_cannot_be_changed()
    {
        Domain.Camera.Camera camera = new CameraBuilder().WithUrl(OriginalUrl).Build();
        camera.Retire(Operator, new FixedClock(ChangedAt));
        camera.ClearPendingEvents();

        Should.Throw<InvalidOperationException>(() =>
            camera.ChangeAddress(RtspUrl.From(CorrectedUrl), Operator, new FixedClock(ChangedAt)));

        // Refused means unchanged — not merely "an exception was thrown".
        camera.Url.Value.ShouldBe(OriginalUrl);
        camera.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Correcting_the_address_leaves_everything_that_records_what_happened_alone()
    {
        Domain.Camera.Camera camera = new CameraBuilder()
            .WithFab("munich")
            .WithName("line-3-inlet")
            .WithUrl(OriginalUrl)
            .Build();

        CameraIdentifier identifier = camera.Id;
        DateTimeOffset registeredAt = camera.RegisteredAt;
        OperatorIdentifier registeredBy = camera.RegisteredBy;

        camera.ChangeAddress(RtspUrl.From(CorrectedUrl), Operator, new FixedClock(ChangedAt));

        // FR-008 and FR-009. The guarantee is that the aggregate exposes no way
        // to change these, so this test is really asserting that correcting the
        // address did not quietly become a general-purpose setter.
        camera.Id.ShouldBe(identifier);
        camera.Fab.Value.ShouldBe("munich");
        camera.Name.Value.ShouldBe("line-3-inlet");
        camera.RegisteredAt.Value.ShouldBe(registeredAt);
        camera.RegisteredBy.ShouldBe(registeredBy);
        camera.Status.ShouldBe(CameraStatus.Registered);
    }

    private sealed class FixedClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
