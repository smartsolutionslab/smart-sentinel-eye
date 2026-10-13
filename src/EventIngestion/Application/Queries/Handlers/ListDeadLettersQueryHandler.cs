using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
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

        var (fabs, rawLimit, reason, state) = query;

        var limit = rawLimit <= 0 ? DefaultLimit : Math.Min(rawLimit, MaximumLimit);

        var filtered = Filter(
            deadLetters.DeadLetters,
            fabs,
            reason,
            state);

        var rows = await filtered
            .OrderByDescending(deadLetter => deadLetter.RejectedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        IReadOnlyList<DeadLetterDto> dtos = rows
            .Select(ToDto)
            .ToArray();

        return Success(dtos);
    }

    private static IQueryable<DeadLetter> Filter(
        IQueryable<DeadLetter> source,
        IReadOnlyList<FabIdentifier> fabs,
        Option<DeadLetterReason> reason,
        Option<HoldState> state)
    {
        // FR-009 and FR-011 in one term: a delivery from a fab the caller does
        // not hold drops out, and so does one with no fab at all — an
        // unattributed row satisfies no IN clause, so it reaches nobody.
        var filtered = source
            .Where(deadLetter => fabs.Contains(deadLetter.Fab));

        // Spec 317 (#2325) FR-008: ?reason= and ?state= narrow the listing,
        // present or absent independently and combined with AND when both
        // are given.
        if (reason.HasValue)
        {
            var reasonValue = reason.Value;
            filtered = filtered.Where(deadLetter => deadLetter.Reason == reasonValue);
        }

        if (state.HasValue)
        {
            var stateValue = state.Value;
            filtered = filtered.Where(deadLetter => deadLetter.State == stateValue);
        }

        return filtered;
    }

    private static DeadLetterDto ToDto(DeadLetter deadLetter) =>
        new(
            deadLetter.Id.Value,
            deadLetter.Topic.Value,
            deadLetter.RawPayload.Value,
            deadLetter.Error.Value,
            deadLetter.RejectedAt,
            deadLetter.Fab?.Value,
            deadLetter.Reason.Value,
            deadLetter.Kind?.Value,
            deadLetter.State.Value);
}
