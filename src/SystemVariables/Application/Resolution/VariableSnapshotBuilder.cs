using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Resolution;

/// <summary>
/// <b>Phase 4a scaffold (spec 148).</b> This type exists so
/// <c>VariableSnapshotBuilderTests</c> and <c>ResolveOverlayTextQueryHandler</c>
/// compile and can be exercised, observed red, before the extraction lands.
/// It deliberately does not implement the four-outcome resolution policy —
/// per plan.md "The extraction", that move is T002: relocate
/// <c>GetOverlaySnapshotQueryHandler.BuildSnapshotAsync</c> and
/// <c>FindInAnyFabAsync</c> here verbatim, mapping each of their exits onto a
/// <see cref="PlaceholderOutcome"/>, and keeping the
/// <c>catch (ArgumentException) { continue; }</c> guard as an unreachable
/// tripwire. Implementing that here would make T003's characterisation of
/// "was this actually red" false.
/// </summary>
public sealed class VariableSnapshotBuilder(IVariableRepository variables) : IVariableSnapshotBuilder
{
    public Task<IReadOnlyList<PlaceholderResolution>> BuildAsync(
        IReadOnlyList<FabIdentifier> fabs, string labelText, CancellationToken cancellationToken)
    {
        _ = variables;
        _ = fabs;
        _ = labelText;
        _ = cancellationToken;
        throw new NotImplementedException(
            "spec 148 T002 (plan.md \"The extraction\"): move GetOverlaySnapshotQueryHandler's " +
            "BuildSnapshotAsync + FindInAnyFabAsync here, verbatim, mapped onto PlaceholderOutcome.");
    }
}
