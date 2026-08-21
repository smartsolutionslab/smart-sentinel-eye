using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Commands;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber on <see cref="SystemVariableValueRequestedV1"/>
/// (spec 007 → 005 bridge). Dispatches the existing
/// <see cref="SetVariableValueCommand"/> if the
/// <c>(variableName, causingEventIdentifier)</c> dedup row reserves
/// fresh. Wolverine outbox redelivery becomes a no-op.
///
/// <para>
/// Malformed inputs (invalid VariableName) are logged + dropped;
/// the V1 contract is "Automation requested the variable be set"
/// and a typo at authoring time should already have been caught
/// at <c>POST /rules</c>. The Automation team is the audit owner
/// for those drops.
/// </para>
/// </summary>
public sealed class SystemVariableValueRequestedV1Handler(
    IVariableValueRequestDedupStore dedup,
    SetVariableValueCommandHandler setHandler,
    ILatencyBudget latency,
    ILogger<SystemVariableValueRequestedV1Handler> logger)
{
    /// <summary>
    /// Automation's actions are not attributed to a specific
    /// operator; we attach a synthetic well-known identifier so the
    /// downstream audit row still has a non-null
    /// <c>ChangedBy</c>. The literal Guid is fixed across instances.
    /// </summary>
    public static readonly OperatorIdentifier AutomationOperator = OperatorIdentifier.From(new Guid("a07a07a0-7000-7000-8000-000000000007"));

    public async Task Handle(SystemVariableValueRequestedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();

        var (name, value, _, causingEventIdentifier, metadata) = message;

        // FR-006: a request that names no fab applies to none. Dropped before
        // the dedup reservation, so it consumes no idempotency key — and said
        // out loud, because a silent drop here is the shape of #1252.
        if (string.IsNullOrWhiteSpace(metadata?.Fab))
        {
            logger.ValueRequestWithoutFab(name, causingEventIdentifier);
            return;
        }

        FabIdentifier fab;
        try
        {
            fab = FabIdentifier.From(metadata.Fab);
        }
        catch (ArgumentException ex)
        {
            logger.ValueRequestWithUnusableFab(ex, metadata.Fab, name, causingEventIdentifier);
            return;
        }

        bool reserved = await dedup.TryReserveAsync(fab, name, causingEventIdentifier, cancellationToken);
        if (!reserved)
        {
            logger.DedupHit(fab, name, causingEventIdentifier);
            return;
        }

        VariableName variableName;
        try
        {
            variableName = VariableName.From(name);
        }
        catch (ArgumentException ex)
        {
            logger.InvalidVariableName(ex, name, causingEventIdentifier);
            return;
        }

        Result<VariableIdentifier, SetVariableValueError> result = await setHandler.HandleAsync(
            new SetVariableValueCommand(
                fab,
                variableName,
                value,
                AutomationOperator,
                Option<int>.None),
            cancellationToken);

        if (result.IsSuccess)
        {
            // The far end of the `event → overlay state` leg (ADR-0015,
            // ≤ 200 ms): the effect is now applied. Recorded only on success,
            // because a refused effect never arrived and timing it would put a
            // fast failure into a distribution that is supposed to describe
            // journeys that completed.
            latency.RecordEventToOverlayState(metadata.RootIngestedAt);
            return;
        }

        // FR-005 / SC-006: a variable that exists in another fab but not this
        // one gets its own message naming both. Sharing SetVariableValueFailed
        // with malformed input is exactly how #1252 stayed hidden for a
        // release — the fab-scoping bug and a typo looked identical in the log.
        if (result.Error is SetVariableValueError.VariableNotFound)
        {
            logger.VariableNotInFab(fab, name, causingEventIdentifier);
            return;
        }

        logger.SetVariableValueFailed(name, value, causingEventIdentifier, result.Error.Code);
    }
}
