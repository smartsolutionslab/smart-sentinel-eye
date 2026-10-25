using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries.Handlers;

public sealed class ListSourceModesQueryHandler(ISourceModeQuerySource sourceModes)
    : IQueryHandler<ListSourceModesQuery, Result<IReadOnlyList<SourceModeDto>, ListSourceModesError>>
{
    public async Task<Result<IReadOnlyList<SourceModeDto>, ListSourceModesError>> HandleAsync(
        ListSourceModesQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var rows = await sourceModes.SourceModes
            .Where(sourceMode => query.Fabs.Contains(sourceMode.Fab))
            .ToListAsync(cancellationToken);

        IReadOnlyList<SourceModeDto> dtos = rows
            .Select(sourceMode => sourceMode.ToDto())
            .OrderBy(dto => dto.Fab, StringComparer.Ordinal)
            .ThenBy(dto => dto.Source, StringComparer.Ordinal)
            .ToArray();

        return Success(dtos);
    }
}
