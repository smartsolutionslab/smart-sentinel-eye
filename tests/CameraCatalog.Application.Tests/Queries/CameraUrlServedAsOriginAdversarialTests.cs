using System.Globalization;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Queries;

/// <summary>
/// Spec 438 US2 (#3016) - adversarial cover for the origin-only camera reads. Every address the write path admits,
/// across scheme case, IPv6 hosts and zones, ports and secret-bearing tails, must reach both reads as an origin that
/// holds none of the path, query or fragment, and never as the placeholder (a valid camera shown as unparseable is
/// a different defect). The listing must mask every row of every page without disturbing the count or the order.
/// </summary>
public class CameraUrlServedAsOriginAdversarialTests
{
    private const string Marker = "SECRETMARK";

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static readonly OperatorIdentifier AnOperator = OperatorIdentifier.From(Guid.CreateVersion7());

    private static readonly DateTimeOffset FirstMoment =
        DateTimeOffset.Parse("2026-10-23T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly string[] Schemes = ["rtsp", "RTSP", "Rtsp"];

    private static readonly string[] Hosts =
    [
        "10.0.5.12",
        "cam-7.fab.local",
        "CAM.Example",
        "xn--bcher-kva.example",
        "[fe80::1]",
        "[fe80::1%25eth0]",
        "[::ffff:10.0.0.1]",
    ];

    private static readonly string[] Ports = ["", ":554", ":8554", ":65535"];

    private static readonly string[] Tails =
    [
        $"/{Marker}",
        $"/a/{Marker}?token={Marker}",
        $"?{Marker}",
        $"#{Marker}",
        $"/%40{Marker}",
        $"/x;{Marker}",
        $"/{Marker}%3Fq%23f",
        $"?a=%40{Marker}#{Marker}",
        $"/%2F{Marker}%2F..",
        $"/{Marker}:{Marker}%40host",
    ];

    public static TheoryData<string> AdmittedSecretBearingAddresses() => [.. Addresses()];

    [Fact]
    public void Every_generated_address_is_admitted_by_the_write_path_so_no_row_below_is_vacuous()
    {
        var refused = Addresses()
            .Where(url => !IsAdmitted(url))
            .ToList();

        refused.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AdmittedSecretBearingAddresses))]
    public async Task Reading_one_camera_serves_an_origin_without_the_tail_for_every_admitted_shape(string url)
    {
        var camera = Register(RtspUrl.From(url), "Line-7", FirstMoment);

        var result = await new GetCameraQueryHandler(new InMemoryCameraQuerySource([camera]))
            .HandleAsync(new GetCameraQuery([Munich], camera.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        AssertServedAsOrigin(result.Value.RtspUrl);
    }

    [Theory]
    [MemberData(nameof(AdmittedSecretBearingAddresses))]
    public async Task Listing_serves_an_origin_without_the_tail_for_every_admitted_shape(string url)
    {
        var camera = Register(RtspUrl.From(url), "Line-7", FirstMoment);

        var result = await new ListCamerasQueryHandler(new InMemoryCameraQuerySource([camera]))
            .HandleAsync(ListQuery(offset: 0, limit: ListCamerasDefaults.DefaultLimit), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        AssertServedAsOrigin(result.Value.Items.Single().RtspUrl);
    }

    [Fact]
    public async Task Walking_every_page_masks_every_row_and_keeps_the_count_and_the_order()
    {
        // A legacy row on every fifth position, so a page boundary falls next to one.
        List<Camera> cameras = [];
        for (var index = 0; index < 23; index++)
        {
            var url = index % 5 == 0
                ? RtspUrl.FromStored($"rtsp://cam:12345#{Marker}{index}@cam/s")
                : RtspUrl.From($"rtsp://10.0.5.{index + 1}:8554/{Marker}{index}?t={Marker}");
            cameras.Add(Register(url, $"Cam-{index:D2}", FirstMoment.AddMinutes(index)));
        }

        var handler = new ListCamerasQueryHandler(new InMemoryCameraQuerySource(cameras));
        List<string> walkedNames = [];
        List<string> walkedAddresses = [];

        for (var offset = 0; offset < cameras.Count; offset += 7)
        {
            var page = await handler.HandleAsync(
                ListQuery(offset, limit: 7) with { Sort = "name", Order = "asc" },
                CancellationToken.None);

            page.IsSuccess.ShouldBeTrue();
            page.Value.Count.ShouldBe(cameras.Count);
            walkedNames.AddRange(page.Value.Items.Select(row => row.Name));
            walkedAddresses.AddRange(page.Value.Items.Select(row => row.RtspUrl));
        }

        walkedNames.ShouldBe([.. cameras.Select(camera => camera.Name.Value).Order(StringComparer.Ordinal)]);
        walkedAddresses.ShouldAllBe(address => !address.Contains(Marker, StringComparison.OrdinalIgnoreCase));
        walkedAddresses.Count(address => address == UrlRedaction.Unparseable).ShouldBe(5);
        walkedAddresses
            .Where(address => address != UrlRedaction.Unparseable)
            .ShouldAllBe(address => address.StartsWith("rtsp://10.0.5.", StringComparison.Ordinal) && address.EndsWith(":8554", StringComparison.Ordinal));
    }

    private static void AssertServedAsOrigin(string served)
    {
        served.ShouldNotBe(UrlRedaction.Unparseable);
        served.ShouldNotContain(Marker, Case.Insensitive);
        served.ShouldStartWith("rtsp://", Case.Sensitive);
        served.IndexOfAny(['/', '?', '#', '@', '%', ';'], "rtsp://".Length).ShouldBe(-1);
    }

    private static IEnumerable<string> Addresses() =>
        from scheme in Schemes
        from host in Hosts
        from port in Ports
        from tail in Tails
        select $"{scheme}://{host}{port}{tail}";

    private static bool IsAdmitted(string url)
    {
        try
        {
            RtspUrl.From(url);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static Camera Register(RtspUrl url, string name, DateTimeOffset at) =>
        Camera.Register(Munich, CameraName.From(name), url, AnOperator, new FixedClock(at));

    private static ListCamerasQuery ListQuery(int offset, int limit) =>
        new(
            Fabs: [Munich],
            Sort: ListCamerasDefaults.DefaultSort,
            Order: ListCamerasDefaults.DefaultOrder,
            Offset: offset,
            Limit: limit,
            IncludeRetired: false);
}
