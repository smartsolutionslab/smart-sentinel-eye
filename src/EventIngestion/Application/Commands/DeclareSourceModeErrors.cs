using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

public abstract record DeclareSourceModeError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record SourceModeAlreadyDeclared(string Fab, string Source)
        : DeclareSourceModeError(
            "SOURCE_MODE_ALREADY_DECLARED",
            $"A mode is already declared for source '{Source}' in fab '{Fab}'.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="DeclareSourceModeError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class DeclareSourceModeFailures
{
    public static DeclareSourceModeError SourceModeAlreadyDeclared(string fab, string source) =>
        new DeclareSourceModeError.SourceModeAlreadyDeclared(fab, source);
}
