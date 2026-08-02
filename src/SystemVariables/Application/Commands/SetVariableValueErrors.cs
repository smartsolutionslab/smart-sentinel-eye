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
