using Microsoft.AspNetCore.Http;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Maps an <see cref="ApiError"/> to an RFC-7807 Problem Details result
/// (ADR-0089). Every command/query failure type derives from
/// <see cref="ApiError"/>, so endpoint failure branches share this one
/// mapping instead of repeating
/// <c>Results.Problem(title/detail/statusCode)</c> at each call site.
/// </summary>
public static class ApiErrorResults
{
    public static IResult ToProblem(this ApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: (int)error.Status);
    }
}
