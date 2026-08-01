using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Persistence;

/// <summary>
/// Converts EF Core's <see cref="DbUpdateConcurrencyException"/> into a
/// <c>409 AGGREGATE_VERSION_CONFLICT</c> problem-details response
/// (ADR-0113), mirroring
/// <see cref="Authorization.FabAuthorizationExceptionHandler"/>.
///
/// <para>
/// This covers the rare true race — two transactions overlapping in the
/// database. The common case, a client acting on a version it was shown
/// earlier, is caught in the command handler before any mutation and
/// surfaces as a typed <c>Result</c> failure instead. ADR-0047 assigns
/// infrastructure signals like this one to middleware, which is why the
/// 18 mutating handlers need no <c>try</c>/<c>catch</c> of their own.
/// </para>
/// </summary>
public sealed class ConcurrencyConflictExceptionHandler : IExceptionHandler
{
    public const string ErrorCode = "AGGREGATE_VERSION_CONFLICT";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        Ensure.That(httpContext).IsNotNull();

        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        ProblemDetails problem = new()
        {
            Title = ErrorCode,
            Detail = "The resource was modified by another writer. Re-read it and reapply the change.",
            Status = StatusCodes.Status409Conflict,
        };
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken: cancellationToken);

        return true;
    }
}
