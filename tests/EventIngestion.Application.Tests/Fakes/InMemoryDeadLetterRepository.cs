using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Hand-written fake for <see cref="IDeadLetterRepository"/> (ADR-0054, no
/// mocking framework). <see cref="PromoteHeldAsync"/> counts the rows its own
/// predicate (plan.md §6.4/§7, FR-007) matches — <c>reason ==
/// UnknownEventType &amp;&amp; state == Held</c> — but, unlike the real
/// repository's set-based <c>UPDATE</c>, cannot flip a matched row's
/// <see cref="DeadLetter.State"/>: the aggregate deliberately has no
/// <c>Promote</c> method (plan.md §2.1/§7 A7 — promotion never loads an
/// aggregate), so this fake's job is recording <em>that</em> and <em>with
/// what arguments</em> the handler called it, which is what every handler
/// unit test in this file needs. Whether a promoted row actually leaves
/// <c>Held</c> is <c>HeldEventTypePromotionIntegrationTests</c>'s job,
/// against the real table the repository's <c>UPDATE</c> writes to.
/// </summary>
public sealed class InMemoryDeadLetterRepository : IDeadLetterRepository
{
    private readonly List<DeadLetter> deadLetters = [];

    public IReadOnlyList<DeadLetter> DeadLetters => deadLetters;

    public int PromoteHeldAsyncCalls { get; private set; }

    public List<(FabIdentifier Fab, Kind Kind)> PromoteHeldAsyncCallArgs { get; } = [];

    public void Add(DeadLetter deadLetter) => deadLetters.Add(deadLetter);

    public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<int> PromoteHeldAsync(FabIdentifier fab, Kind kind, CancellationToken cancellationToken)
    {
        PromoteHeldAsyncCalls++;
        PromoteHeldAsyncCallArgs.Add((fab, kind));

        int matched = deadLetters.Count(deadLetter =>
            deadLetter.Fab == fab
            && deadLetter.Kind == kind
            && deadLetter.Reason == DeadLetterReason.UnknownEventType
            && deadLetter.State == HoldState.Held);

        return Task.FromResult(matched);
    }
}
