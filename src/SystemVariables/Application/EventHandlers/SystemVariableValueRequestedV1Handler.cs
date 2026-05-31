using Microsoft.Extensions.Logging;
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
    ILogger<SystemVariableValueRequestedV1Handler> logger)
{
    /// <summary>
    /// Automation's actions are not attributed to a specific
    /// operator; we attach a synthetic well-known identifier so the
    /// downstream audit row still has a non-null
    /// <c>ChangedBy</c>. The literal Guid is fixed across instances.
    /// </summary>
    public static readonly OperatorIdentifier AutomationOperator =
        OperatorIdentifier.From(new Guid("a07a07a0-7000-7000-8000-000000000007"));

    public async Task Handle(SystemVariableValueRequestedV1 message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        bool reserved = await dedup.TryReserveAsync(
            message.Name, message.CausingEventIdentifier, cancellationToken)
            .ConfigureAwait(false);
        if (!reserved)
        {
            Log.DedupHit(logger, message.Name, message.CausingEventIdentifier);
            return;
        }

        VariableName name;
        try
        {
            name = VariableName.From(message.Name);
        }
        catch (ArgumentException ex)
        {
            Log.InvalidVariableName(logger, ex, message.Name, message.CausingEventIdentifier);
            return;
        }

        Result<VariableIdentifier, SetVariableValueError> result = await setHandler
            .HandleAsync(new SetVariableValueCommand(name, message.Value, AutomationOperator),
                cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            Log.SetVariableValueFailed(logger, message.Name, message.Value, message.CausingEventIdentifier, result.Error.Code);
        }
    }
}
