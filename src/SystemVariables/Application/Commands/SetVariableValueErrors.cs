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
}
