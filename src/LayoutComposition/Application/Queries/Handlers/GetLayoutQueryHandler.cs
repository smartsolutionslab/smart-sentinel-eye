using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;

public sealed class GetLayoutQueryHandler(ILayoutQuerySource layouts)
    : IQueryHandler<GetLayoutQuery, Result<LayoutDto, GetLayoutError>>
{
    public async Task<Result<LayoutDto, GetLayoutError>> HandleAsync(
        GetLayoutQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        (var fabs, var identifier) = query;

        // FR-006: the fab is part of the lookup rather than a check afterwards,
        // so a layout outside the caller's fabs and one that never existed take
        // the same path out of here and produce the same response.
        var layout = await layouts.Layouts
            .SingleOrDefaultAsync(
                candidate => candidate.Id == identifier && fabs.Contains(candidate.Fab),
                cancellationToken);

        if (layout is null)
        {
            return Failure(GetLayoutFailures.LayoutNotFound(identifier.Value));
        }

        return Success(layout.ToLayoutDto());
    }
}
