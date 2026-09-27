using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

public abstract record ChangeSourceModeError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record SourceModeNotDeclared(string Fab, string Source)
        : ChangeSourceModeError(
            "SOURCE_MODE_NOT_DECLARED",
            $"No mode is declared for source '{Source}' in fab '{Fab}'.",
            HttpStatusCode.NotFound);

    /// <summary>
    /// Code ends <c>_STALE</c>, not <c>_STALE_VERSION</c> (ADR-0119,
    /// <c>StaleCodeConventionTests</c>).
    /// </summary>
    public sealed record SourceModeStale(string Source, int ExpectedVersion, int ActualVersion)
        : ChangeSourceModeError(
            "SOURCE_MODE_STALE",
            $"Source mode '{Source}' has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="ChangeSourceModeError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class ChangeSourceModeFailures
{
    public static ChangeSourceModeError SourceModeNotDeclared(string fab, string source) =>
        new ChangeSourceModeError.SourceModeNotDeclared(fab, source);

    public static ChangeSourceModeError SourceModeStale(string source, int expectedVersion, int actualVersion) =>
        new ChangeSourceModeError.SourceModeStale(source, expectedVersion, actualVersion);
}
