using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Resolution;

/// <summary>
/// Pure implementation of <see cref="IResolver"/>. Stateless;
/// thread-safe; cheap to call on every variable change.
/// </summary>
public sealed class Resolver : IResolver
{
    public string Resolve(string labelText, IReadOnlyDictionary<string, VariableSnapshotEntry> snapshot)
    {
        ArgumentNullException.ThrowIfNull(labelText);
        ArgumentNullException.ThrowIfNull(snapshot);
        return PlaceholderParser.Substitute(labelText, name =>
            snapshot.TryGetValue(name, out VariableSnapshotEntry? entry)
                ? entry.Value.Render(entry.BooleanLabels ?? BooleanLabels.Default)
                : null);
    }
}
