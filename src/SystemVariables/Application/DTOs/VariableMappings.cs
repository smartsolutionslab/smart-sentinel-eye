using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.DTOs;

internal static class VariableMappings
{
    public static VariableDto ToDto(this Variable variable) =>
        new(
            VariableIdentifier: variable.Id.Value,
            Version: variable.Version,
            Fab: variable.Fab.Value,
            Name: variable.Name.Value,
            Type: variable.Type.Value,
            State: variable.State.Value,
            Value: variable.Value is VariableValue.Unset ? null : variable.Value.ToWireString(),
            TruthyLabel: variable.BooleanLabels?.TruthyLabel,
            FalsyLabel: variable.BooleanLabels?.FalsyLabel,
            CreatedAt: variable.Creation.At,
            CreatedBy: variable.Creation.By.Value);
}
