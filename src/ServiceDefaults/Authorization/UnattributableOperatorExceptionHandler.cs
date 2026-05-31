using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SmartSentinelEye.ServiceDefaults.Authorization;

/// <summary>
/// Converts <see cref="UnattributableOperatorException"/> (thrown by
/// <see cref="ClaimsPrincipalExtensions.ToOperatorIdentifier"/> when an
/// authenticated request carries no usable <c>sub</c> claim) into a typed
/// <c>401 OPERATOR_UNIDENTIFIED</c> problem-details response. The system
/// fails closed rather than attribute an audited write to a fabricated
/// operator. Registered as an <see cref="IExceptionHandler"/> in
/// <c>ServiceDefaults.AddBearerAuthentication</c>; every per-context API
/// picks it up via <c>UseExceptionHandler</c>.
/// </summary>
public sealed class UnattributableOperatorExceptionHandler : IExceptionHandler
{
    public const string ErrorCode = "OPERATOR_UNIDENTIFIED";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is not UnattributableOperatorException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ProblemDetails problem = new()
        {
            Title = ErrorCode,
            Detail = "The request could not be attributed to an operator; the token carries no usable 'sub' claim.",
            Status = StatusCodes.Status401Unauthorized,
        };
        await httpContext.Response
            .WriteAsJsonAsync(problem, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
