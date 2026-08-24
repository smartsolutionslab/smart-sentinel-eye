using System.Globalization;
using System.Net;
using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Queries;

public class ListCamerasQueryHandlerTests
{
    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task List_with_defaults_returns_all_cameras_newest_first()
    {
        Camera oldest = RegisterCameraAt("2026-05-20T10:00:00Z", "Cam-A");
        Camera middle = RegisterCameraAt("2026-05-22T10:00:00Z", "Cam-B");
        Camera newest = RegisterCameraAt("2026-05-24T10:00:00Z", "Cam-C");

        ListCamerasQueryHandler handler = NewHandler(oldest, middle, newest);

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(3);
        result.Value.Items.Select(item => item.Name).ShouldBe(["Cam-C", "Cam-B", "Cam-A"]);
    }

    [Fact]
    public async Task List_sorted_by_name_ascending_orders_case_insensitively()
    {
        Camera apple = RegisterCameraAt("2026-05-22T10:00:00Z", "apple");
        Camera banana = RegisterCameraAt("2026-05-21T10:00:00Z", "Banana");
        Camera cherry = RegisterCameraAt("2026-05-20T10:00:00Z", "cherry");

        ListCamerasQueryHandler handler = NewHandler(banana, cherry, apple);

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { Sort = "name", Order = "asc" },
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(item => item.Name).ShouldBe(["apple", "Banana", "cherry"]);
    }

    [Fact]
    public async Task List_with_offset_and_limit_returns_the_requested_page_slice()
    {
        Camera a = RegisterCameraAt("2026-05-20T10:00:00Z", "Cam-1");
        Camera b = RegisterCameraAt("2026-05-21T10:00:00Z", "Cam-2");
        Camera c = RegisterCameraAt("2026-05-22T10:00:00Z", "Cam-3");
        Camera d = RegisterCameraAt("2026-05-23T10:00:00Z", "Cam-4");
        Camera e = RegisterCameraAt("2026-05-24T10:00:00Z", "Cam-5");

        ListCamerasQueryHandler handler = NewHandler(a, b, c, d, e);

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { Offset = 1, Limit = 2 },
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(2);
        result.Value.Items.Select(item => item.Name).ShouldBe(["Cam-4", "Cam-3"]);
        result.Value.Count.ShouldBe(5);
        result.Value.Offset.ShouldBe(1);
        result.Value.Limit.ShouldBe(2);
    }

    [Fact]
    public async Task List_with_unknown_sort_field_returns_InvalidSortField()
    {
        ListCamerasQueryHandler handler = NewHandler();

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { Sort = "createdBy" },
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        ListCamerasError.InvalidSortField invalid = result.Error.ShouldBeOfType<ListCamerasError.InvalidSortField>();
        invalid.Requested.ShouldBe("createdBy");
        invalid.Code.ShouldBe("CATALOG_INVALID_SORT_FIELD");
        invalid.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_with_unknown_sort_order_returns_InvalidSortOrder()
    {
        ListCamerasQueryHandler handler = NewHandler();

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { Order = "sideways" },
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ListCamerasError.InvalidSortOrder>();
        result.Error.Code.ShouldBe("CATALOG_INVALID_SORT_ORDER");
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(0, 0)]
    [InlineData(0, -5)]
    public async Task List_with_invalid_pagination_returns_InvalidPagination(int offset, int limit)
    {
        ListCamerasQueryHandler handler = NewHandler();

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { Offset = offset, Limit = limit },
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ListCamerasError.InvalidPagination>();
        result.Error.Code.ShouldBe("CATALOG_INVALID_PAGINATION");
    }

    [Fact]
    public async Task List_with_limit_above_maximum_returns_LimitExceeded()
    {
        ListCamerasQueryHandler handler = NewHandler();

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { Limit = ListCamerasDefaults.MaximumLimit + 1 },
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        ListCamerasError.LimitExceeded exceeded = result.Error.ShouldBeOfType<ListCamerasError.LimitExceeded>();
        exceeded.Requested.ShouldBe(ListCamerasDefaults.MaximumLimit + 1);
        exceeded.Maximum.ShouldBe(ListCamerasDefaults.MaximumLimit);
    }

    [Fact]
    public async Task List_maps_each_camera_to_its_summary_DTO_shape()
    {
        Camera camera = RegisterCameraAt("2026-05-24T10:00:00Z", "Line-7", "rtsp://10.0.5.77/h264");
        ListCamerasQueryHandler handler = NewHandler(camera);

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        CameraSummaryDto summary = result.Value.Items.Single();
        summary.CameraIdentifier.ShouldBe(camera.Id.Value);
        summary.Name.ShouldBe("Line-7");
        summary.RtspUrl.ShouldBe("rtsp://10.0.5.77/h264");
        summary.RegisteredAt.ShouldBe(camera.RegisteredAt);
    }

    private static ListCamerasQuery DefaultQuery() =>
        new(
            Fabs: [FabIdentifier.From("munich")],
            Sort: ListCamerasDefaults.DefaultSort,
            Order: ListCamerasDefaults.DefaultOrder,
            Offset: ListCamerasDefaults.DefaultOffset,
            Limit: ListCamerasDefaults.DefaultLimit,
            IncludeRetired: false);

    private static ListCamerasQueryHandler NewHandler(params Camera[] cameras) =>
        new(new InMemoryCameraQuerySource(cameras.ToList()));

    private static Camera RegisterCameraAt(string registeredAtIso, string name) =>
        RegisterCameraAt(registeredAtIso, name, "rtsp://10.0.5.10/h264");

    private static Camera RegisterCameraAt(string registeredAtIso, string name, string rtspUrl)
    {
        DateTimeOffset moment = DateTimeOffset.Parse(registeredAtIso, CultureInfo.InvariantCulture);
        return Camera.Register(FabIdentifier.From("munich"), 
            CameraName.From(name),
            RtspUrl.From(rtspUrl),
            AnAdmin,
            new FixedClock(moment));
    }

    // ---- spec 015 T020: the listing is fab-scoped and says which fab ----

    [Fact]
    public async Task The_summary_carries_the_fab()
    {
        Camera camera = Camera.Register(
            FabIdentifier.From("dresden"),
            CameraName.From("Line-1-North"),
            RtspUrl.From("rtsp://10.0.5.12/h264"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FixedClock(DateTimeOffset.Parse("2026-05-25T10:00:00Z", CultureInfo.InvariantCulture)));

        ListCamerasQueryHandler handler = NewHandler(camera);

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { Fabs = [FabIdentifier.From("dresden")] }, CancellationToken.None);

        // dresden, not munich: everything else defaults to munich, so asserting
        // the default would pass even if the mapper ignored the camera's fab.
        result.Value.Items.ShouldHaveSingleItem().Fab.ShouldBe("dresden");
    }

    [Fact]
    public async Task The_listing_omits_cameras_in_fabs_the_caller_does_not_hold()
    {
        Camera own = Camera.Register(
            FabIdentifier.From("munich"), CameraName.From("Cam-Own"),
            RtspUrl.From("rtsp://10.0.5.12/h264"),
            OperatorIdentifier.From(Guid.CreateVersion7()), new FixedClock(DateTimeOffset.Parse("2026-05-25T10:00:00Z", CultureInfo.InvariantCulture)));
        Camera foreign = Camera.Register(
            FabIdentifier.From("dresden"), CameraName.From("Cam-Foreign"),
            RtspUrl.From("rtsp://10.0.5.13/h264"),
            OperatorIdentifier.From(Guid.CreateVersion7()), new FixedClock(DateTimeOffset.Parse("2026-05-25T10:00:00Z", CultureInfo.InvariantCulture)));

        ListCamerasQueryHandler handler = NewHandler(own, foreign);

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery(), CancellationToken.None);

        result.Value.Items.Select(item => item.Name).ShouldBe(["Cam-Own"]);
        // The count must reflect what the caller can page through, not what
        // exists — a count of 2 with one row returned reads as a broken page.
        result.Value.Count.ShouldBe(1);
    }


    // ---- spec 028 T019: retired cameras stay out of the way (FR-007) ----

    [Fact]
    public async Task The_default_listing_omits_retired_cameras()
    {
        Camera staying = RegisterCameraAt("2026-05-24T10:00:00Z", "Cam-Staying");
        // A distinct instant, not cosmetic: on a tie the sort falls through to
        // ThenBy(Fab), and FabIdentifier is not IComparable, so LINQ-to-objects
        // throws where Postgres would just ORDER BY fab.
        Camera going = RegisterCameraAt("2026-05-23T10:00:00Z", "Cam-Going");
        going.Retire(AnAdmin, new FixedClock(DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture)));

        ListCamerasQueryHandler handler = NewHandler(staying, going);

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldHaveSingleItem().Name.ShouldBe("Cam-Staying");

        // The count, not only the page: a total that still counted the retired
        // camera would page a client past the end of its own list.
        result.Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Asking_for_retired_cameras_includes_them_and_each_row_says_which_it_is()
    {
        Camera staying = RegisterCameraAt("2026-05-24T10:00:00Z", "Cam-Staying");
        Camera going = RegisterCameraAt("2026-05-23T10:00:00Z", "Cam-Going");
        going.Retire(AnAdmin, new FixedClock(DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture)));

        ListCamerasQueryHandler handler = NewHandler(staying, going);

        Result<CameraListPageDto, ListCamerasError> result = await handler.HandleAsync(
            DefaultQuery() with { IncludeRetired = true },
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);

        // Included is only half of it — an opted-in client has to be able to
        // tell the two apart, which is what the status on each row is for.
        result.Value.Items
            .ToDictionary(row => row.Name, row => row.Status)
            .ShouldBe(new Dictionary<string, string>
            {
                ["Cam-Staying"] = "Registered",
                ["Cam-Going"] = "Decommissioned",
            });
    }
}
