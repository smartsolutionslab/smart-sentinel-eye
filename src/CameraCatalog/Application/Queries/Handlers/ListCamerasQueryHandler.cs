using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;

public sealed class ListCamerasQueryHandler(ICameraQuerySource cameras)
    : IQueryHandler<ListCamerasQuery, Result<CameraListPageDto, ListCamerasError>>
{
    private static readonly string[] AllowedSortFields = ["name", "registeredAt"];
    private static readonly string[] AllowedSortOrders = ["asc", "desc"];

    public async Task<Result<CameraListPageDto, ListCamerasError>> HandleAsync(
        ListCamerasQuery query,
        CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();
        (IReadOnlyList<FabIdentifier>? fabs, string? sort, string? order, int offset, int limit, bool includeRetired) = query;

        if (!AllowedSortFields.Contains(sort, StringComparer.Ordinal))
        {
            return Failure(ListCamerasFailures.InvalidSortField(sort, AllowedSortFields));
        }

        if (!AllowedSortOrders.Contains(order, StringComparer.Ordinal))
        {
            return Failure(ListCamerasFailures.InvalidSortOrder(order));
        }

        if (offset < 0 || limit <= 0)
        {
            return Failure(ListCamerasFailures.InvalidPagination("Offset must be non-negative and limit must be positive."));
        }

        if (limit > ListCamerasDefaults.MaximumLimit)
        {
            return Failure(ListCamerasFailures.LimitExceeded(limit, ListCamerasDefaults.MaximumLimit));
        }

        bool descending = order == "desc";

        // FR-005: only cameras in fabs the caller holds, and filtered before
        // the count so the total reflects what they can actually page through.
        IQueryable<Camera> visible = cameras.Cameras.Where(camera => fabs.Contains(camera.Fab));

        // FR-007, and filtered here rather than at the endpoint so the count
        // and the page agree: a total that included retired cameras while the
        // rows excluded them would page past the end of the list.
        //
        // Spelled as the partial unique index spells it
        // (`status <> 'Decommissioned'`) so the query and the constraint that
        // makes name reuse work cannot drift apart.
        if (!includeRetired)
        {
            visible = visible.Where(camera => camera.Status != CameraStatus.Decommissioned);
        }

        IQueryable<Camera> source = SortBy(visible, sort, descending);

        int total = await source.CountAsync(cancellationToken);

        List<CameraSummaryDto> items = await source
            .Skip(offset)
            .Take(limit)
            .Select(camera => new CameraSummaryDto(
                camera.Id.Value,
                camera.Version,
                camera.Fab.Value,
                camera.Name.Value,
                camera.Url.Value,
                camera.RegisteredAt,
                camera.Status.Value))
            .ToListAsync(cancellationToken);

        return Success(
            new CameraListPageDto(items, total, offset, limit));
    }

    // EF Core's converter exposes Name as a plain string column at query time, so
    // `OrderBy(c => c.Name)` translates to `ORDER BY name` in Postgres. For the
    // in-memory tests CameraName.IComparable orders by NormalizedValue, keeping
    // the unit and integration tests on the same lambda.
    private static IQueryable<Camera> SortBy(IQueryable<Camera> source, string field, bool descending) =>
        (field, descending) switch
        {
            // Fab breaks the tie: a multi-fab listing can hold two rows of one
            // name, and ordering by name alone leaves their relative order to
            // the database — so a page boundary could show one row twice and
            // the other never.
            ("name", false) => source.OrderBy(camera => camera.Name).ThenBy(camera => camera.Fab),
            ("name", true) => source.OrderByDescending(camera => camera.Name).ThenBy(camera => camera.Fab),
            // Same reason: two cameras can share a registration instant.
            ("registeredAt", false) => source.OrderBy(camera => camera.RegisteredAt).ThenBy(camera => camera.Fab),
            ("registeredAt", true) => source.OrderByDescending(camera => camera.RegisteredAt).ThenBy(camera => camera.Fab),
            _ => throw new InvalidOperationException($"Unhandled sort field '{field}'."),
        };
}
