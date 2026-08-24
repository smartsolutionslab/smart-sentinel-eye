using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Persistence;

/// <summary>
/// Converts EF Core's <see cref="DbUpdateConcurrencyException"/> into a
/// <c>409 AGGREGATE_VERSION_STALE</c> problem-details response
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
///
/// <para>
/// The code ends <c>_STALE</c> per ADR-0119, and it was renamed from
/// <c>AGGREGATE_VERSION_CONFLICT</c> to get there. That name meant no client
/// recognised this as a lost update, so an operator losing the true database
/// race was told to <b>try again</b> — and because this handler is registered
/// in ServiceDefaults, that was true of <em>every</em> mutating endpoint in
/// <em>every</em> context, not just the one #1857 was filed about.
/// </para>
/// </summary>
public sealed class ConcurrencyConflictExceptionHandler : IExceptionHandler
{
    public const string ErrorCode = "AGGREGATE_VERSION_STALE";

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
