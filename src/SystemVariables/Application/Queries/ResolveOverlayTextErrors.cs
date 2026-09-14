using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

/// <summary>
/// Failure cases for <see cref="ResolveOverlayTextQuery"/>. No "not found"
/// case exists here — see plan.md "The endpoint": resolving text that
/// references nothing is a 200 with an empty <c>placeholders</c> list, since
/// there is no resource that could be absent.
/// </summary>
public abstract record ResolveOverlayTextError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    /// <summary>Text is absent, empty, or longer than the 256-character bound
    /// the <c>Label</c> value object itself enforces (spec 148 US1 scenarios 6-7).</summary>
    public sealed record InvalidInput(string Detail)
        : ResolveOverlayTextError("VARIABLE_INVALID_INPUT", Detail, HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="ResolveOverlayTextError"/> as the base rather than the
/// variant (ADR-0047's invariance rule) — copied from
/// <c>GetOverlaySnapshotFailures</c>.
/// </summary>
public static class ResolveOverlayTextFailures
{
    public static ResolveOverlayTextError InvalidInput(string detail) =>
        new ResolveOverlayTextError.InvalidInput(detail);
}
