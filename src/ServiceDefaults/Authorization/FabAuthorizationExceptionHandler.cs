using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SmartSentinelEye.ServiceDefaults.Authorization;

/// <summary>
/// Converts <see cref="FabAuthorizationException"/> from
/// <see cref="DefaultFabAuthorizationGuard.EnsureAccessAsync"/>
/// into a typed <c>403 RESOURCE_FAB_NOT_AUTHORIZED</c>
/// problem-details response (spec 008 FR-019). Registered as an
/// <see cref="IExceptionHandler"/> in
/// <c>ServiceDefaults.AddBearerAuthentication</c>; any per-context
/// API picks it up automatically via <c>UseExceptionHandler</c>.
/// </summary>
public sealed class FabAuthorizationExceptionHandler : IExceptionHandler
{
    public const string ErrorCode = "RESOURCE_FAB_NOT_AUTHORIZED";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is not FabAuthorizationException fabException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        ProblemDetails problem = new()
        {
            Title = ErrorCode,
            Detail = $"Caller is not a member of fab '{fabException.FabId}'.",
            Status = StatusCodes.Status403Forbidden,
        };
        await httpContext.Response
            .WriteAsJsonAsync(problem, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
