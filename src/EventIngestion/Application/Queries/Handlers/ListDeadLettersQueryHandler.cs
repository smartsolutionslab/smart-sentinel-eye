using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries.Handlers;

public sealed class ListDeadLettersQueryHandler(IDeadLetterQuerySource deadLetters)
    : IQueryHandler<ListDeadLettersQuery, Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError>>
{
    public const int DefaultLimit = 100;
    public const int MaximumLimit = 1_000;

    public async Task<Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError>> HandleAsync(
        ListDeadLettersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = query.Limit <= 0 ? DefaultLimit : Math.Min(query.Limit, MaximumLimit);

        List<DeadLetter> rows = await deadLetters.DeadLetters
            .OrderByDescending(d => d.RejectedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<DeadLetterDto> dtos = rows
            .Select(d => new DeadLetterDto(d.Id.Value, d.Topic, d.RawPayload, d.Error, d.RejectedAt))
            .ToArray();

        return Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError>.Success(dtos);
    }
}
