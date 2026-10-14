using System.Globalization;
using System.Net;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Queries;

/// <summary>
/// Characterisation (spec 353, ADR-0144 phase 4a: behaviour-preserving, so green
/// on the unrefactored handler and green unmodified after). Pins the order in
/// which <see cref="ListCamerasQueryHandler"/> refuses bad parameters (sort
/// field, sort order, pagination, limit ceiling) with every message in full, and
/// that the fab, retired and name-fragment filters are applied before the count.
///
/// <para>
/// Counterfactual this file was seen to catch: moving the sort-order check above
/// the sort-field check turns the four-fault row from CATALOG_INVALID_SORT_FIELD
/// into CATALOG_INVALID_SORT_ORDER; removing the retired filter makes the
/// without-retired total read 3 instead of 2.
/// </para>
///
/// <para>
/// Pinned as it is, not asserted as desirable: an offset below zero together
/// with an oversized limit answers CATALOG_INVALID_PAGINATION, never
/// CATALOG_LIMIT_EXCEEDED, because the pagination check runs first.
/// </para>
///
/// <para>
/// Every expected message and name is a hand-written literal, never built with
/// the <c>ListCamerasFailures</c> factories under test.
/// </para>
/// </summary>
public class ListCamerasRefusalOrderCharacterisationTests
{
    private const string SortFieldMessage = "Unknown sort field 'colour'. Allowed: name, registeredAt.";
    private const string SortOrderMessage = "Unknown sort order 'sideways'. Allowed: asc, desc.";
    private const string PaginationMessage = "Offset must be non-negative and limit must be positive.";

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("11111111-2222-7333-8444-555555555555"));

    [Theory]
    [InlineData("colour", "sideways", -1, 500, "CATALOG_INVALID_SORT_FIELD", SortFieldMessage)]
    [InlineData("colour", "sideways", 0, 50, "CATALOG_INVALID_SORT_FIELD", SortFieldMessage)]
    [InlineData("colour", "asc", -1, 50, "CATALOG_INVALID_SORT_FIELD", SortFieldMessage)]
    [InlineData("colour", "asc", 0, 201, "CATALOG_INVALID_SORT_FIELD", SortFieldMessage)]
    [InlineData("colour", "asc", 0, 50, "CATALOG_INVALID_SORT_FIELD", SortFieldMessage)]
    [InlineData("name", "sideways", -1, 500, "CATALOG_INVALID_SORT_ORDER", SortOrderMessage)]
    [InlineData("name", "sideways", 0, 201, "CATALOG_INVALID_SORT_ORDER", SortOrderMessage)]
    [InlineData("registeredAt", "sideways", 0, 50, "CATALOG_INVALID_SORT_ORDER", SortOrderMessage)]
    [InlineData("name", "asc", -1, 500, "CATALOG_INVALID_PAGINATION", PaginationMessage)]
    [InlineData("name", "desc", -1, 50, "CATALOG_INVALID_PAGINATION", PaginationMessage)]
    [InlineData("name", "asc", 0, 0, "CATALOG_INVALID_PAGINATION", PaginationMessage)]
    [InlineData("name", "asc", 3, -5, "CATALOG_INVALID_PAGINATION", PaginationMessage)]
    [InlineData("name", "asc", 0, 201, "CATALOG_LIMIT_EXCEEDED", "Requested limit 201 exceeds the maximum allowed (200).")]
    [InlineData("registeredAt", "desc", 4, 999, "CATALOG_LIMIT_EXCEEDED", "Requested limit 999 exceeds the maximum allowed (200).")]
    public async Task Bad_parameters_are_refused_in_the_order_sort_field_sort_order_pagination_limit(
        string sort,
        string order,
        int offset,
        int limit,
        string expectedCode,
        string expectedMessage)
    {
        var result = await NewHandler().HandleAsync(
            Query(sort, order, offset, limit),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(expectedCode);
        result.Error.Message.ShouldBe(expectedMessage);
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Name", "asc", "CATALOG_INVALID_SORT_FIELD", "Unknown sort field 'Name'. Allowed: name, registeredAt.")]
    [InlineData("name", "Asc", "CATALOG_INVALID_SORT_ORDER", "Unknown sort order 'Asc'. Allowed: asc, desc.")]
    public async Task Sort_names_are_matched_case_sensitively(
        string sort,
        string order,
        string expectedCode,
        string expectedMessage)
    {
        var result = await NewHandler().HandleAsync(Query(sort, order, 0, 50), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(expectedCode);
        result.Error.Message.ShouldBe(expectedMessage);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 200)]
    [InlineData(10, 200)]
    public async Task The_paging_boundaries_are_inclusive_at_offset_zero_limit_one_and_limit_two_hundred(
        int offset,
        int limit)
    {
        var result = await NewHandler().HandleAsync(
            Query("name", "asc", offset, limit),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(0);
        result.Value.Offset.ShouldBe(offset);
        result.Value.Limit.ShouldBe(limit);
    }

    [Fact]
    public async Task Without_retired_cameras_the_total_and_the_page_describe_only_live_matches_in_the_held_fab()
    {
        var result = await NewHandler(Cameras()).HandleAsync(
            Combined(includeRetired: false, offset: 1, limit: 1),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.Offset.ShouldBe(1);
        result.Value.Limit.ShouldBe(1);
        result.Value.Items.Select(item => item.Name).ShouldBe(["line-2-INLET"]);
    }

    [Fact]
    public async Task Without_retired_cameras_the_whole_page_lists_the_live_matches_in_name_order()
    {
        var result = await NewHandler(Cameras()).HandleAsync(
            Combined(includeRetired: false, offset: 0, limit: 10),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.Items.Select(item => item.Name).ShouldBe(["Line-1-Inlet", "line-2-INLET"]);
    }

    [Fact]
    public async Task With_retired_cameras_the_total_counts_the_retired_match_and_the_rows_say_which_it_is()
    {
        var result = await NewHandler(Cameras()).HandleAsync(
            Combined(includeRetired: true, offset: 0, limit: 10),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(3);
        result.Value.Items.Select(item => $"{item.Name}|{item.Status}|{item.Fab}").ShouldBe(
        [
            "Line-0-Inlet|Decommissioned|munich",
            "Line-1-Inlet|Registered|munich",
            "line-2-INLET|Registered|munich",
        ]);
    }

    [Fact]
    public async Task With_retired_cameras_the_second_page_of_one_starts_at_the_second_match()
    {
        var result = await NewHandler(Cameras()).HandleAsync(
            Combined(includeRetired: true, offset: 1, limit: 1),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(3);
        result.Value.Items.Select(item => item.Name).ShouldBe(["Line-1-Inlet"]);
    }

    [Fact]
    public async Task Holding_both_fabs_without_a_fragment_counts_every_live_camera_of_both()
    {
        var query = Combined(includeRetired: false, offset: 0, limit: 50) with
        {
            Fabs = [FabIdentifier.From("munich"), FabIdentifier.From("dresden")],
            NameFragment = null,
        };

        var result = await NewHandler(Cameras()).HandleAsync(query, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(7);
    }

    [Fact]
    public async Task Holding_only_the_other_fab_counts_nothing_of_the_first()
    {
        var query = Combined(includeRetired: true, offset: 0, limit: 50) with
        {
            Fabs = [FabIdentifier.From("dresden")],
            NameFragment = null,
        };

        var result = await NewHandler(Cameras()).HandleAsync(query, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.Items.Select(item => item.Name).ShouldBe(["Line-6-Inlet", "Line-7-Inlet"]);
    }

    [Fact]
    public async Task A_whitespace_only_fragment_filters_nothing()
    {
        var query = Combined(includeRetired: false, offset: 0, limit: 50) with { NameFragment = "   " };

        var result = await NewHandler(Cameras()).HandleAsync(query, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(5);
    }

    private static ListCamerasQuery Query(string sort, string order, int offset, int limit) =>
        new(
            Fabs: [FabIdentifier.From("munich")],
            Sort: sort,
            Order: order,
            Offset: offset,
            Limit: limit,
            IncludeRetired: false);

    private static ListCamerasQuery Combined(bool includeRetired, int offset, int limit) =>
        new(
            Fabs: [FabIdentifier.From("munich")],
            Sort: "name",
            Order: "asc",
            Offset: offset,
            Limit: limit,
            IncludeRetired: includeRetired,
            NameFragment: " iNlEt ");

    private static List<Camera> Cameras()
    {
        var retired = Register("munich", "Line-0-Inlet", "2026-05-20T10:00:00Z");
        retired.Retire(
            AnAdmin,
            new FixedClock(DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture)));

        return
        [
            Register("munich", "Line-1-Inlet", "2026-05-21T10:00:00Z"),
            Register("munich", "line-2-INLET", "2026-05-22T10:00:00Z"),
            Register("munich", "Line-3-Outlet", "2026-05-23T10:00:00Z"),
            Register("munich", "Line-4-Gate", "2026-05-24T10:00:00Z"),
            Register("munich", "Line-5-Dock", "2026-05-25T10:00:00Z"),
            retired,
            Register("dresden", "Line-6-Inlet", "2026-05-27T10:00:00Z"),
            Register("dresden", "Line-7-Inlet", "2026-05-28T10:00:00Z"),
        ];
    }

    private static Camera Register(string fab, string name, string registeredAtIso) =>
        Camera.Register(
            FabIdentifier.From(fab),
            CameraName.From(name),
            RtspUrl.From("rtsp://10.0.5.10/h264"),
            AnAdmin,
            new FixedClock(DateTimeOffset.Parse(registeredAtIso, CultureInfo.InvariantCulture)));

    private static ListCamerasQueryHandler NewHandler(params IEnumerable<Camera> cameras) =>
        new(new InMemoryCameraQuerySource(cameras.ToList()));
}
