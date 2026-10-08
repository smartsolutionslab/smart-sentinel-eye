using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// Same codes as <see cref="ChangeSourceModeError"/> (plan.md §6.5) —
/// generics are invariant, so a parallel <c>*Failures</c> static is needed
/// rather than reusing the variant (ADR-0047), as spec 269 did for its own
/// siblings.
/// </summary>
public abstract record UndeclareSourceModeError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record SourceModeNotDeclared(string Fab, string Source)
        : UndeclareSourceModeError(
            "SOURCE_MODE_NOT_DECLARED",
            $"No mode is declared for source '{Source}' in fab '{Fab}'.",
            HttpStatusCode.NotFound);

    /// <summary>
    /// Code ends <c>_STALE</c>, not <c>_STALE_VERSION</c> (ADR-0119,
    /// <c>StaleCodeConventionTests</c>).
    /// </summary>
    public sealed record SourceModeStale(string Source, int ExpectedVersion, int ActualVersion)
        : UndeclareSourceModeError(
            "SOURCE_MODE_STALE",
            $"Source mode '{Source}' has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="UndeclareSourceModeError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class UndeclareSourceModeFailures
{
    public static UndeclareSourceModeError SourceModeNotDeclared(string fab, string source) =>
        new UndeclareSourceModeError.SourceModeNotDeclared(fab, source);

    public static UndeclareSourceModeError SourceModeStale(string source, int expectedVersion, int actualVersion) =>
        new UndeclareSourceModeError.SourceModeStale(source, expectedVersion, actualVersion);
}
