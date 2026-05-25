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

        if (!AllowedSortFields.Contains(query.Sort, StringComparer.Ordinal))
        {
            return Result<CameraListPageDto, ListCamerasError>.Failure(
                new ListCamerasError.InvalidSortField(query.Sort, AllowedSortFields));
        }

        if (!AllowedSortOrders.Contains(query.Order, StringComparer.Ordinal))
        {
            return Result<CameraListPageDto, ListCamerasError>.Failure(
                new ListCamerasError.InvalidSortOrder(query.Order));
        }

        if (query.Offset < 0 || query.Limit <= 0)
        {
            return Result<CameraListPageDto, ListCamerasError>.Failure(
                new ListCamerasError.InvalidPagination("Offset must be non-negative and limit must be positive."));
        }

        if (query.Limit > ListCamerasDefaults.MaximumLimit)
        {
            return Result<CameraListPageDto, ListCamerasError>.Failure(
                new ListCamerasError.LimitExceeded(query.Limit, ListCamerasDefaults.MaximumLimit));
        }

        bool descending = query.Order == "desc";
        IQueryable<Camera> source = SortBy(cameras.Cameras, query.Sort, descending);

        int total = await source.CountAsync(cancellationToken).ConfigureAwait(false);

        List<CameraSummaryDto> items = await source
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(camera => new CameraSummaryDto(
                camera.Id.Value,
                camera.Name.Value,
                camera.Url.Value,
                camera.RegisteredAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<CameraListPageDto, ListCamerasError>.Success(
            new CameraListPageDto(items, total, query.Offset, query.Limit));
    }

    private static IQueryable<Camera> SortBy(IQueryable<Camera> source, string field, bool descending) =>
        (field, descending) switch
        {
            ("name", false) => source.OrderBy(camera => camera.Name.NormalizedValue),
            ("name", true) => source.OrderByDescending(camera => camera.Name.NormalizedValue),
            ("registeredAt", false) => source.OrderBy(camera => camera.RegisteredAt),
            ("registeredAt", true) => source.OrderByDescending(camera => camera.RegisteredAt),
            _ => throw new InvalidOperationException($"Unhandled sort field '{field}'."),
        };
}
