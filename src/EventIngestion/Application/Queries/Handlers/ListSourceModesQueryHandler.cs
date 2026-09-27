using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
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

        List<SourceMode> rows = await sourceModes.SourceModes
            .Where(sourceMode => query.Fabs.Contains(sourceMode.Fab))
            .ToListAsync(cancellationToken);

        IReadOnlyList<SourceModeDto> dtos = rows
            .Select(sourceMode => new SourceModeDto(
                sourceMode.Id.Value,
                sourceMode.Fab.Value,
                sourceMode.Source.Value,
                sourceMode.Mode.Value,
                sourceMode.Declaration.DeclaredAt,
                sourceMode.Declaration.DeclaredBy,
                sourceMode.Version))
            .OrderBy(dto => dto.Fab, StringComparer.Ordinal)
            .ThenBy(dto => dto.Source, StringComparer.Ordinal)
            .ToArray();

        return Success(dtos);
    }
}
