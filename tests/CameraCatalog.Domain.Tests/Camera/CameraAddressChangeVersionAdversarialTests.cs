using System.Globalization;
using System.Reflection;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.CameraCatalog.Domain.Tests.Camera.Builders;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 403 (#3008), adversarial pass on the publisher's half: what <c>PreviousVersion</c> a raised address
/// change carries at the boundaries, and what it carries when one load makes more than one change.
///
/// <para>
/// The two-changes-in-one-load facts are green and pin a latent hazard, not a live defect: no production path
/// calls <c>ChangeAddress</c> twice on one load today (one call site, one change per request). If one ever does,
/// both events carry the same version, StreamDistribution applies an equal version (only a redelivery was
/// supposed to repeat one), and an outbox reordering lands the earlier address. Plan §9 risk 4 says a second
/// behaviour in one command would not break ordering; for a second address change it would.
/// </para>
/// </summary>
public class CameraAddressChangeVersionAdversarialTests
{
    private static readonly DateTimeOffset ChangedAt =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Operator = OperatorIdentifier.From(Guid.CreateVersion7());

    private const string OriginalUrl = "rtsp://10.0.5.12/h264";
    private const string FirstUrl = "rtsp://10.0.5.44/h264";
    private const string SecondUrl = "rtsp://10.0.5.45/h264";

    [Fact]
    public void A_camera_at_int_max_raises_its_change_with_int_max_and_does_not_overflow_here()
    {
        var camera = CameraAt(int.MaxValue);

        camera.ChangeAddress(RtspUrl.From(FirstUrl), Operator, Clock());

        Changes(camera).ShouldHaveSingleItem().PreviousVersion.ShouldBe(AggregateVersion.From(int.MaxValue));
    }

    [Fact]
    public void The_raised_version_is_a_snapshot_and_does_not_follow_a_later_bump()
    {
        var camera = CameraAt(5);
        camera.ChangeAddress(RtspUrl.From(FirstUrl), Operator, Clock());

        SetVersion(camera, 6);

        Changes(camera).ShouldHaveSingleItem().PreviousVersion.ShouldBe(AggregateVersion.From(5));
    }

    [Fact]
    public void A_rename_in_the_same_load_does_not_change_the_version_the_address_change_carries()
    {
        var camera = CameraAt(5);

        camera.Rename(CameraName.From("line-9-outlet"), Operator, Clock());
        camera.ChangeAddress(RtspUrl.From(FirstUrl), Operator, Clock());

        Changes(camera).ShouldHaveSingleItem().PreviousVersion.ShouldBe(AggregateVersion.From(5));
    }

    [Fact]
    public void A_retired_camera_raises_no_change_at_any_version()
    {
        var camera = CameraAt(5);
        camera.Retire(Operator, Clock());
        camera.ClearPendingEvents();

        Should.Throw<InvalidOperationException>(
            () => camera.ChangeAddress(RtspUrl.From(FirstUrl), Operator, Clock()));

        Changes(camera).ShouldBeEmpty();
    }

    [Fact]
    public void A_no_op_after_a_real_change_in_the_same_load_raises_only_the_real_one()
    {
        var camera = CameraAt(5);

        camera.ChangeAddress(RtspUrl.From(FirstUrl), Operator, Clock());
        camera.ChangeAddress(RtspUrl.From(FirstUrl), Operator, Clock());

        Changes(camera).ShouldHaveSingleItem().Url.Value.ShouldBe(FirstUrl);
    }

    /// <summary>Latent hazard, green: see the class remarks.</summary>
    [Fact]
    public void Latent_two_address_changes_in_one_load_carry_the_same_version()
    {
        var camera = CameraAt(5);

        camera.ChangeAddress(RtspUrl.From(FirstUrl), Operator, Clock());
        camera.ChangeAddress(RtspUrl.From(SecondUrl), Operator, Clock());

        var changes = Changes(camera);
        changes.Select(change => change.Url.Value).ShouldBe([FirstUrl, SecondUrl]);
        changes.Select(change => change.PreviousVersion.Value).ShouldBe([5, 5]);
    }

    /// <summary>Latent hazard, green: A to B and back to A in one load is two events with one version.</summary>
    [Fact]
    public void Latent_a_change_and_its_reversal_in_one_load_are_indistinguishable_by_version()
    {
        var camera = CameraAt(5);

        camera.ChangeAddress(RtspUrl.From(FirstUrl), Operator, Clock());
        camera.ChangeAddress(RtspUrl.From(OriginalUrl), Operator, Clock());

        var changes = Changes(camera);
        changes.Count.ShouldBe(2);
        changes[0].PreviousVersion.ShouldBe(changes[1].PreviousVersion);
        changes[^1].Url.Value.ShouldBe(OriginalUrl);
    }

    private static List<CameraAddressChangedDomainEvent> Changes(Domain.Camera.Camera camera) =>
        [.. camera.PendingEvents.OfType<CameraAddressChangedDomainEvent>()];

    private static Domain.Camera.Camera CameraAt(int version)
    {
        var camera = new CameraBuilder().WithUrl(OriginalUrl).Build();
        SetVersion(camera, version);
        camera.ClearPendingEvents();
        return camera;
    }

    private static void SetVersion(Domain.Camera.Camera camera, int version)
    {
        var setter = camera.GetType()
            .GetProperty(nameof(IVersionedAggregate.Version), BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(nonPublic: true)!;

        setter.Invoke(camera, [AggregateVersion.From(version)]);
    }

    private static FixedClock Clock() => new(ChangedAt);

    private sealed class FixedClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
