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
        Ensure.That(query).IsNotNull();

        var (fabs, rawLimit) = query;

        int limit = rawLimit <= 0 ? DefaultLimit : Math.Min(rawLimit, MaximumLimit);

        // FR-009 and FR-011 in one term: a delivery from a fab the caller does
        // not hold drops out, and so does one with no fab at all — an
        // unattributed row satisfies no IN clause, so it reaches nobody.
        List<DeadLetter> rows = await deadLetters.DeadLetters
            .Where(deadLetter => fabs.Contains(deadLetter.Fab))
            .OrderByDescending(deadLetter => deadLetter.RejectedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        IReadOnlyList<DeadLetterDto> dtos = rows
            .Select(deadLetter => new DeadLetterDto(deadLetter.Id.Value, deadLetter.Topic.Value, deadLetter.RawPayload.Value, deadLetter.Error.Value, deadLetter.RejectedAt))
            .ToArray();

        return Success(dtos);
    }
}
