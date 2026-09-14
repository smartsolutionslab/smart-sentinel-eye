using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Resolution;

/// <summary>
/// The complete set of outcomes for one placeholder name, once resolution
/// against the caller's fabs has run (spec 148 plan.md "The one new
/// Application type worth naming"). Four cases, and four is complete for
/// this input: <see cref="VariableSnapshotBuilder"/> keeps the
/// <c>catch (ArgumentException)</c> guard from the code it is extracted
/// from as an unreachable tripwire rather than a fifth outcome.
/// </summary>
public enum PlaceholderOutcome
{
    /// <summary>Found, not Archived, not Unset. <see cref="PlaceholderResolution.Entry"/> is non-null.</summary>
    Resolved,

    /// <summary>Missing from every fab the caller holds.</summary>
    Unknown,

    /// <summary>Found, but its value is <see cref="VariableValue.Unset"/>.</summary>
    Unset,

    /// <summary>Found, but <c>State == VariableState.Archived</c>.</summary>
    Archived,
}

/// <summary>
/// One referenced name's resolution outcome. All four cases below
/// <see cref="PlaceholderOutcome.Resolved"/> render the literal
/// <c>{{name}}</c> (FR-011) — the outcome is what distinguishes them, since
/// the resolved text alone cannot.
/// </summary>
public sealed record PlaceholderResolution(
    string Name,
    PlaceholderOutcome Outcome,
    FabIdentifier? Fab,
    VariableSnapshotEntry? Entry);
