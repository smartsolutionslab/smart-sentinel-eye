using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands;

/// <summary>
/// Sets the current value of an existing system variable (spec 005 US2).
/// The provided <paramref name="WireValue"/> is parsed against the
/// variable's declared type at handle time (FR-007/FR-008). Type
/// mismatch returns <see cref="SetVariableValueError.VariableTypeMismatch"/>.
/// </summary>
/// <param name="RootIngestedAt">
/// When the plant-floor event that caused this set was accepted by
/// EventIngestion — the near end of §IV's <c>event → overlay state</c> leg.
/// <c>None</c> for an operator setting a value by hand, which has no such root.
///
/// <para>
/// Carried rather than re-stamped, and carried all the way: the leg ends when
/// LayoutComposition pushes the re-resolved label, and nothing on that side can
/// know when the journey began unless this travels with it. Before #2173 it was
/// dropped here, so the leg was timed at the value write — a prefix roughly
/// 750 ms short of the effect, reported under the leg's name.
/// </para>
/// </param>
public sealed record SetVariableValueCommand(
    FabIdentifier Fab,
    VariableName Name,
    string WireValue,
    OperatorIdentifier ChangedBy,
    Option<int> ExpectedVersion,
    Option<DateTimeOffset> RootIngestedAt)
    : ICommand<Result<VariableIdentifier, SetVariableValueError>>;
