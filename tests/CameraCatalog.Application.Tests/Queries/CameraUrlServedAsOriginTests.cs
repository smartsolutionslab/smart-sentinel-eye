using System.Globalization;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Queries;

/// <summary>
/// Spec 438 US2 (#3016, ADR-0179) - the camera read and the camera listing serve the stored address as its
/// origin (scheme, host, port), never its path, query or fragment, and never a legacy row's userinfo. The full
/// address is read only through the write-scoped, audited route.
///
/// <para>
/// Red on unchanged code: both handlers copy <c>Url.Value</c> into the DTO verbatim.
/// </para>
/// </summary>
public class CameraUrlServedAsOriginTests
{
    private const string PathMarker = "PATHMARK";
    private const string QueryMarker = "QUERYMARK";
    private const string FragmentMarker = "FRAGMARK";

    private const string SecretBearing =
        $"rtsp://10.0.5.12:8554/{PathMarker}/h264?token={QueryMarker}#{FragmentMarker}";

    // Admitted by RtspUrl.FromStored (a row stored before the raw-@ rule) and refused by From.
    private const string LegacyWithUserInfo = "rtsp://cam:12345#USERPWMARK@cam/s";

    private static readonly OperatorIdentifier AnOperator = OperatorIdentifier.From(Guid.CreateVersion7());

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-10-23T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void The_legacy_fixture_is_one_the_origin_rule_refuses_so_the_expectation_below_is_attainable()
    {
        UrlRedaction.ToOrigin(LegacyWithUserInfo).ShouldBe(UrlRedaction.Unparseable);
    }

    [Fact]
    public async Task Reading_one_camera_serves_its_address_as_the_origin_keeping_the_port()
    {
        var camera = Register(RtspUrl.From(SecretBearing));

        var result = await GetHandler(camera).HandleAsync(QueryFor(camera), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RtspUrl.ShouldBe("rtsp://10.0.5.12:8554");
    }

    [Fact]
    public async Task Reading_one_camera_serves_no_path_query_or_fragment()
    {
        var camera = Register(RtspUrl.From(SecretBearing));

        var result = await GetHandler(camera).HandleAsync(QueryFor(camera), CancellationToken.None);

        result.Value.RtspUrl.ShouldNotContain(PathMarker);
        result.Value.RtspUrl.ShouldNotContain(QueryMarker);
        result.Value.RtspUrl.ShouldNotContain(FragmentMarker);
    }

    [Fact]
    public async Task Reading_one_camera_with_a_legacy_stored_address_serves_it_unparseable()
    {
        var camera = Register(RtspUrl.FromStored(LegacyWithUserInfo));

        var result = await GetHandler(camera).HandleAsync(QueryFor(camera), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RtspUrl.ShouldBe(UrlRedaction.Unparseable);
    }

    [Fact]
    public async Task Listing_serves_each_cameras_address_as_the_origin_keeping_the_port()
    {
        var camera = Register(RtspUrl.From(SecretBearing));

        var result = await ListHandler(camera).HandleAsync(ListQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Single().RtspUrl.ShouldBe("rtsp://10.0.5.12:8554");
    }

    [Fact]
    public async Task Listing_serves_no_path_query_or_fragment()
    {
        var camera = Register(RtspUrl.From(SecretBearing));

        var result = await ListHandler(camera).HandleAsync(ListQuery(), CancellationToken.None);

        var served = result.Value.Items.Single().RtspUrl;
        served.ShouldNotContain(PathMarker);
        served.ShouldNotContain(QueryMarker);
        served.ShouldNotContain(FragmentMarker);
    }

    [Fact]
    public async Task Listing_a_camera_with_a_legacy_stored_address_serves_it_unparseable_and_still_lists_the_rest()
    {
        var legacy = Register(RtspUrl.FromStored(LegacyWithUserInfo), "Cam-Legacy");
        var healthy = Register(RtspUrl.From("rtsp://10.0.5.13/h264"), "Cam-Healthy");

        var result = await ListHandler(legacy, healthy).HandleAsync(ListQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var served = result.Value.Items.ToDictionary(row => row.Name, row => row.RtspUrl);
        served.Count.ShouldBe(2);
        served["Cam-Legacy"].ShouldBe(UrlRedaction.Unparseable);
        served["Cam-Healthy"].ShouldBe("rtsp://10.0.5.13");
    }

    private static Camera Register(RtspUrl url, string name = "Line-7") =>
        Camera.Register(
            FabIdentifier.From("munich"),
            CameraName.From(name),
            url,
            AnOperator,
            new FixedClock(FixedMoment));

    private static GetCameraQuery QueryFor(Camera camera) =>
        new([FabIdentifier.From("munich")], camera.Id);

    private static ListCamerasQuery ListQuery() =>
        new(
            Fabs: [FabIdentifier.From("munich")],
            Sort: ListCamerasDefaults.DefaultSort,
            Order: ListCamerasDefaults.DefaultOrder,
            Offset: ListCamerasDefaults.DefaultOffset,
            Limit: ListCamerasDefaults.DefaultLimit,
            IncludeRetired: false);

    private static GetCameraQueryHandler GetHandler(params Camera[] cameras) =>
        new(new InMemoryCameraQuerySource(cameras.ToList()));

    private static ListCamerasQueryHandler ListHandler(params Camera[] cameras) =>
        new(new InMemoryCameraQuerySource(cameras.ToList()));
}
