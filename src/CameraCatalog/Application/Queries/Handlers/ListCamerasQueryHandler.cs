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
        (IReadOnlyList<FabIdentifier>? fabs, string? sort, string? order, int offset, int limit) = query;

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
        IQueryable<Camera> source = SortBy(visible, sort, descending);

        int total = await source.CountAsync(cancellationToken);

        List<CameraSummaryDto> items = await source
            .Skip(offset)
            .Take(limit)
            .Select(camera => new CameraSummaryDto(
                camera.Id.Value,
                camera.Name.Value,
                camera.Url.Value,
                camera.RegisteredAt))
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
            ("name", false) => source.OrderBy(camera => camera.Name),
            ("name", true) => source.OrderByDescending(camera => camera.Name),
            ("registeredAt", false) => source.OrderBy(camera => camera.RegisteredAt),
            ("registeredAt", true) => source.OrderByDescending(camera => camera.RegisteredAt),
            _ => throw new InvalidOperationException($"Unhandled sort field '{field}'."),
        };
}
