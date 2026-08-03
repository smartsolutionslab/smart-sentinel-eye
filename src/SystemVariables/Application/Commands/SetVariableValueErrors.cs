using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Commands;

public abstract record SetVariableValueError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record VariableNotFound(string Name)
        : SetVariableValueError(
            "VARIABLE_NOT_FOUND",
            $"System variable '{Name}' does not exist.",
            HttpStatusCode.NotFound);

    public sealed record VariableArchived(string Name)
        : SetVariableValueError(
            "VARIABLE_ARCHIVED",
            $"System variable '{Name}' is archived and cannot be updated.",
            HttpStatusCode.Conflict);

    public sealed record VariableTypeMismatch(string ExpectedType, string Reason)
        : SetVariableValueError(
            "VARIABLE_TYPE_MISMATCH",
            $"Value does not match declared type '{ExpectedType}': {Reason}",
            HttpStatusCode.BadRequest);

    /// <summary>
    /// The caller acted on a version of the variable that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other Conflict cases here.
    /// </summary>
    public sealed record VariableStale(string Name, int ExpectedVersion, int ActualVersion)
        : SetVariableValueError(
            "VARIABLE_STALE",
            $"System variable '{Name}' has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="SetVariableValueError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class SetVariableValueFailures
{
    public static SetVariableValueError VariableNotFound(string name) =>
        new SetVariableValueError.VariableNotFound(name);

    public static SetVariableValueError VariableArchived(string name) =>
        new SetVariableValueError.VariableArchived(name);

    public static SetVariableValueError VariableTypeMismatch(string expectedType, string reason) =>
        new SetVariableValueError.VariableTypeMismatch(expectedType, reason);

    public static SetVariableValueError VariableStale(string name, int expectedVersion, int actualVersion) =>
        new SetVariableValueError.VariableStale(name, expectedVersion, actualVersion);
}
