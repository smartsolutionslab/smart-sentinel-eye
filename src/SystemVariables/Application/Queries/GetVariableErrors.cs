using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

public abstract record GetVariableError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record VariableNotFound(string Name)
        : GetVariableError(
            "VARIABLE_NOT_FOUND",
            $"System variable '{Name}' does not exist.",
            HttpStatusCode.NotFound);
}
