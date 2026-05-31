using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Set variable {Variable} '{Name}' = '{Value}' by {Operator}.")]
    public static partial void SetVariable(ILogger logger, VariableIdentifier variable, VariableName name, string value, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Defined variable {Variable} '{Name}' ({Type}) by {Operator}.")]
    public static partial void DefinedVariable(ILogger logger, VariableIdentifier variable, VariableName name, VariableType type, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Archived variable {Variable} '{Name}' by {Operator}.")]
    public static partial void ArchivedVariable(ILogger logger, VariableIdentifier variable, VariableName name, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No overlays reference variable '{Name}'; skipping push fan-out.")]
    public static partial void NoOverlaysReferenceVariable(ILogger logger, VariableName name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pushed ResolvedOverlayTextChanged to {Count} overlays after '{Name}' changed.")]
    public static partial void PushedResolvedTextAfterChange(ILogger logger, int count, VariableName name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pushed ResolvedOverlayTextChanged to {Count} overlays after '{Name}' was archived.")]
    public static partial void PushedResolvedTextAfterArchive(ILogger logger, int count, VariableName name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Reverse-index dropped overlay {Overlay} after archive.")]
    public static partial void ReverseIndexDroppedOverlay(ILogger logger, Guid overlay);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Reverse-index upserted for overlay {Overlay} v{Revision}; label='{Text}'.")]
    public static partial void ReverseIndexUpserted(ILogger logger, Guid overlay, int revision, string text);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dedup hit for variable '{Name}' caused by {CausingEvent}; no-op.")]
    public static partial void DedupHit(ILogger logger, string name, Guid causingEvent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid variable name '{Name}' in V1; dropping (caused by {CausingEvent}).")]
    public static partial void InvalidVariableName(ILogger logger, Exception exception, string name, Guid causingEvent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SetVariableValue failed for '{Name}' = '{Value}' (caused by {CausingEvent}): {Code}.")]
    public static partial void SetVariableValueFailed(ILogger logger, string name, string value, Guid causingEvent, string code);
}
