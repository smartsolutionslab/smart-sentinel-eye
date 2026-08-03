using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Commands;

public abstract record ArchiveVariableError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record VariableNotFound(string Name)
        : ArchiveVariableError(
            "VARIABLE_NOT_FOUND",
            $"System variable '{Name}' does not exist.",
            HttpStatusCode.NotFound);

    /// <summary>
    /// The caller acted on a version of the variable that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other Conflict cases here.
    /// </summary>
    public sealed record VariableStale(string Name, int ExpectedVersion, int ActualVersion)
        : ArchiveVariableError(
            "VARIABLE_STALE",
            $"System variable '{Name}' has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="ArchiveVariableError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class ArchiveVariableFailures
{
    public static ArchiveVariableError VariableNotFound(string name) =>
        new ArchiveVariableError.VariableNotFound(name);

    public static ArchiveVariableError VariableStale(string name, int expectedVersion, int actualVersion) =>
        new ArchiveVariableError.VariableStale(name, expectedVersion, actualVersion);
}
