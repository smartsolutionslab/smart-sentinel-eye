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
        ArgumentNullException.ThrowIfNull(query);
        var (sort, order, offset, limit) = query;

        if (!AllowedSortFields.Contains(sort, StringComparer.Ordinal))
        {
            return Result<CameraListPageDto, ListCamerasError>.Failure(
                new ListCamerasError.InvalidSortField(sort, AllowedSortFields));
        }

        if (!AllowedSortOrders.Contains(order, StringComparer.Ordinal))
        {
            return Result<CameraListPageDto, ListCamerasError>.Failure(
                new ListCamerasError.InvalidSortOrder(order));
        }

        if (offset < 0 || limit <= 0)
        {
            return Result<CameraListPageDto, ListCamerasError>.Failure(
                new ListCamerasError.InvalidPagination("Offset must be non-negative and limit must be positive."));
        }

        if (limit > ListCamerasDefaults.MaximumLimit)
        {
            return Result<CameraListPageDto, ListCamerasError>.Failure(
                new ListCamerasError.LimitExceeded(limit, ListCamerasDefaults.MaximumLimit));
        }

        bool descending = order == "desc";
        IQueryable<Camera> source = SortBy(cameras.Cameras, sort, descending);

        int total = await source.CountAsync(cancellationToken).ConfigureAwait(false);

        List<CameraSummaryDto> items = await source
            .Skip(offset)
            .Take(limit)
            .Select(camera => new CameraSummaryDto(
                camera.Id.Value,
                camera.Name.Value,
                camera.Url.Value,
                camera.RegisteredAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<CameraListPageDto, ListCamerasError>.Success(
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
