using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

public abstract record ListVariablesError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidStateFilter(string Raw)
        : ListVariablesError(
            "VARIABLE_INVALID_STATE_FILTER",
            $"'{Raw}' is not a valid variable state (Defined | Archived).",
            HttpStatusCode.BadRequest);
}
