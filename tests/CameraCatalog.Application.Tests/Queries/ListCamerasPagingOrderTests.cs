using System.Globalization;
using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Queries;

/// <summary>
/// Spec 131 / #2144 — offset paging resumes by position, so it is only correct
/// if the sort key is a <b>total</b> order. Where it is not, the database
/// chooses, and two page requests are two executions with no ordering
/// guarantee between them.
///
/// <para>
/// <b>How the licence is modelled.</b> Page 1 and page 2 are separate requests
/// — two HTTP calls, two transactions — so each is given its own handler over
/// the same cameras in a <i>different physical order</i>. LINQ-to-Objects sorts
/// stably, so physical order is exactly the tie order, and supplying it is the
/// only way a unit environment can express the freedom a real database has.
/// With a total sort key there are no ties left for the freedom to act on, and
/// both requests are determined by the query alone.
/// </para>
///
/// <para>
/// The alternative — page a real Postgres and hope the planner breaks the tie
/// differently — is a flake, not evidence: the same query over an unchanged
/// heap generally answers the same way, so the test would be green today and
/// prove nothing. What the fixture <i>can</i> prove is that the new ORDER BY
/// column translates to SQL, which is Phase 5's job.
/// </para>
/// </summary>
public class ListCamerasPagingOrderTests
{
    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task Paging_a_retired_camera_and_its_live_replacement_returns_each_exactly_once()
    {
        // The partial unique index is filtered on `status <> 'Decommissioned'`
        // precisely so retiring releases the name, so this pair is legal and
        // ties on (Name, Fab) — the pair the sort used to stop at.
        Camera other = RegisterCameraAt("2026-05-20T10:00:00Z", "Cam-A");
        Camera retired = RegisterCameraAt("2026-05-21T10:00:00Z", "Shared-Name");
        retired.Retire(AnAdmin, new FixedClock(DateTimeOffset.Parse("2026-05-22T10:00:00Z", CultureInfo.InvariantCulture)));
        Camera replacement = RegisterCameraAt("2026-05-23T10:00:00Z", "Shared-Name");

        IReadOnlyList<Guid> paged = await PageThroughAsync(
            ByName(),
            [other, retired, replacement],
            [other, replacement, retired]);

        // Sorted on both sides, so this is multiset equality: it fails on a row
        // returned twice as loudly as on a row never returned, and the message
        // prints both sequences rather than only what is missing.
        paged.Order().ShouldBe(
            new[] { other.Id.Value, retired.Id.Value, replacement.Id.Value }.Order());
    }

    [Fact]
    public async Task Paging_two_cameras_registered_in_the_same_instant_returns_each_exactly_once()
    {
        // No index claims this pair is unique at all: registeredAt ties among
        // live rows too, so `includeRetired` is not what makes it reachable.
        Camera earliest = RegisterCameraAt("2026-05-20T10:00:00Z", "Cam-Early");
        Camera first = RegisterCameraAt("2026-05-21T10:00:00Z", "Cam-Tied-One");
        Camera second = RegisterCameraAt("2026-05-21T10:00:00Z", "Cam-Tied-Two");

        IReadOnlyList<Guid> paged = await PageThroughAsync(
            ByRegisteredAt(),
            [earliest, first, second],
            [earliest, second, first]);

        paged.Order().ShouldBe(
            new[] { earliest.Id.Value, first.Id.Value, second.Id.Value }.Order());
    }

    /// <summary>
    /// Pages twice with a limit of 2, so the boundary falls inside the tie.
    /// The limit is the caller's own query parameter and well inside
    /// <see cref="ListCamerasDefaults.MaximumLimit"/> — no production number
    /// moves to make the boundary reachable.
    /// </summary>
    private static async Task<IReadOnlyList<Guid>> PageThroughAsync(
        ListCamerasQuery query,
        Camera[] asTheFirstRequestSeesThem,
        Camera[] asTheSecondRequestSeesThem)
    {
        List<Guid> paged = [];
        paged.AddRange(await PageAsync(query with { Offset = 0 }, asTheFirstRequestSeesThem));
        paged.AddRange(await PageAsync(query with { Offset = 2 }, asTheSecondRequestSeesThem));
        return paged;
    }

    private static async Task<IEnumerable<Guid>> PageAsync(ListCamerasQuery query, Camera[] cameras)
    {
        ListCamerasQueryHandler handler = new(new InMemoryCameraQuerySource([.. cameras]));

        Result<CameraListPageDto, ListCamerasError> result =
            await handler.HandleAsync(query, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        return result.Value.Items.Select(item => item.CameraIdentifier);
    }

    private static ListCamerasQuery ByName() =>
        new(
            Fabs: [FabIdentifier.From("munich")],
            Sort: "name",
            Order: "asc",
            Offset: 0,
            Limit: 2,
            IncludeRetired: true);

    private static ListCamerasQuery ByRegisteredAt() =>
        ByName() with { Sort = "registeredAt" };

    private static Camera RegisterCameraAt(string registeredAtIso, string name) =>
        Camera.Register(
            FabIdentifier.From("munich"),
            CameraName.From(name),
            RtspUrl.From("rtsp://10.0.5.10/h264"),
            AnAdmin,
            new FixedClock(DateTimeOffset.Parse(registeredAtIso, CultureInfo.InvariantCulture)));
}
