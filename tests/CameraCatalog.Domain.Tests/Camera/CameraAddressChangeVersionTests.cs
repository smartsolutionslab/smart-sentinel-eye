using System.Globalization;
using System.Reflection;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.CameraCatalog.Domain.Tests.Camera.Builders;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 403 (#3008): the address-changed event carries the version the change
/// was made against, which is the camera's version at the time (the aggregate
/// still holds the loaded value when the event is raised; the persistence
/// interceptor bumps it inside the commit).
/// </summary>
public class CameraAddressChangeVersionTests
{
    private static readonly DateTimeOffset ChangedAt =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Operator = OperatorIdentifier.From(Guid.CreateVersion7());

    private const string OriginalUrl = "rtsp://10.0.5.12/h264";
    private const string CorrectedUrl = "rtsp://10.0.5.44/h264";

    [Fact]
    public void A_camera_at_version_five_raises_the_change_with_previous_version_five()
    {
        var camera = new CameraBuilder().WithUrl(OriginalUrl).Build();
        SetVersion(camera, 5);
        camera.ClearPendingEvents();

        camera.ChangeAddress(RtspUrl.From(CorrectedUrl), Operator, new FixedClock(ChangedAt));

        var changed = camera.PendingEvents.OfType<CameraAddressChangedDomainEvent>().ShouldHaveSingleItem();
        changed.PreviousVersion.ShouldBe(AggregateVersion.From(5));
    }

    [Fact]
    public void A_freshly_registered_camera_raises_its_first_change_with_previous_version_zero()
    {
        var camera = new CameraBuilder().WithUrl(OriginalUrl).Build();
        camera.ClearPendingEvents();

        camera.ChangeAddress(RtspUrl.From(CorrectedUrl), Operator, new FixedClock(ChangedAt));

        var changed = camera.PendingEvents.OfType<CameraAddressChangedDomainEvent>().ShouldHaveSingleItem();
        changed.PreviousVersion.ShouldBe(AggregateVersion.From(0));
    }

    [Fact]
    public void Changing_to_the_current_address_raises_nothing_whatever_the_version()
    {
        var camera = new CameraBuilder().WithUrl(OriginalUrl).Build();
        SetVersion(camera, 5);
        camera.ClearPendingEvents();

        camera.ChangeAddress(RtspUrl.From(OriginalUrl), Operator, new FixedClock(ChangedAt));

        camera.PendingEvents.OfType<CameraAddressChangedDomainEvent>().ShouldBeEmpty();
    }

    private static void SetVersion(Domain.Camera.Camera camera, int version)
    {
        var setter = camera.GetType()
            .GetProperty(nameof(IVersionedAggregate.Version), BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(nonPublic: true)!;

        setter.Invoke(camera, [AggregateVersion.From(version)]);
    }

    private sealed class FixedClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
