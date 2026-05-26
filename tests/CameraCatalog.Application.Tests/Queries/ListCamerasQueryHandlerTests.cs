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
            Sort: ListCamerasDefaults.DefaultSort,
            Order: ListCamerasDefaults.DefaultOrder,
            Offset: ListCamerasDefaults.DefaultOffset,
            Limit: ListCamerasDefaults.DefaultLimit);

    private static ListCamerasQueryHandler NewHandler(params Camera[] cameras) =>
        new(new InMemoryCameraQuerySource(cameras.ToList()));

    private static Camera RegisterCameraAt(string registeredAtIso, string name) =>
        RegisterCameraAt(registeredAtIso, name, "rtsp://10.0.5.10/h264");

    private static Camera RegisterCameraAt(string registeredAtIso, string name, string rtspUrl)
    {
        DateTimeOffset moment = DateTimeOffset.Parse(registeredAtIso, CultureInfo.InvariantCulture);
        return Camera.Register(
            CameraName.From(name),
            RtspUrl.From(rtspUrl),
            AnAdmin,
            new FixedClock(moment));
    }
}
