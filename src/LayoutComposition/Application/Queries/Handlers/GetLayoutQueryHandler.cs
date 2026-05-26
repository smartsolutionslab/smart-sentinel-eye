using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;

public sealed class GetLayoutQueryHandler(ILayoutQuerySource layouts)
    : IQueryHandler<GetLayoutQuery, Result<LayoutDto, GetLayoutError>>
{
    public async Task<Result<LayoutDto, GetLayoutError>> HandleAsync(
        GetLayoutQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Layout? layout = await layouts.Layouts
            .SingleOrDefaultAsync(candidate => candidate.Id == query.Layout, cancellationToken)
            .ConfigureAwait(false);

        if (layout is null)
        {
            return Result<LayoutDto, GetLayoutError>.Failure(
                new GetLayoutError.LayoutNotFound(query.Layout.Value));
        }

        return Result<LayoutDto, GetLayoutError>.Success(Map(layout));
    }

    internal static LayoutDto Map(Layout layout) =>
        new(
            LayoutIdentifier: layout.Id.Value,
            Name: layout.Name.Value,
            CreatedAt: layout.CreatedAt,
            CreatedBy: layout.CreatedBy.Value,
            Revisions: layout.Revisions
                .OrderBy(r => r.Number.Value)
                .Select(MapRevision)
                .ToList());

    internal static LayoutRevisionDto MapRevision(Revision revision) =>
        new(
            RevisionIdentifier: revision.Id.Value,
            RevisionNumber: revision.Number.Value,
            State: revision.State.Value,
            CameraIdentifier: revision.Camera.Value,
            CreatedAt: revision.CreatedAt,
            CreatedBy: revision.CreatedBy.Value,
            PublishedAt: revision.PublishedAt,
            ArchivedAt: revision.ArchivedAt);
}
