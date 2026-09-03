using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

/// <summary>
/// Lists system variables in the fabs the caller holds, optionally filtered
/// by state (spec 014 FR-008).
///
/// <para>
/// Spans all of the caller's fabs when they name none — the deliberate
/// asymmetry with the write path, which must choose. A listing that refused
/// a multi-fab operator would make the endpoint unusable for exactly the
/// people it exists for.
/// </para>
///
/// <para>
/// <b>Archived variables are excluded unless asked for</b> (#2015), which is
/// the convention <c>GET /cameras</c> already follows with
/// <c>includeRetired</c>. Before this the archive flow hid nothing: a variable
/// could be archived and the listing was byte-for-byte the same, so the one
/// remedy an operator had for a mistaken or decommissioned variable changed
/// nothing they could see. 1618 had accumulated against the dev database.
/// </para>
/// </summary>
/// <param name="State">
/// An exact state to list. When given it wins outright, so
/// <c>state=Archived</c> is how the archived ones are read back and
/// <paramref name="IncludeArchived"/> does not enter into it.
/// </param>
/// <param name="IncludeArchived">
/// Widens the default listing to every state. Only consulted when
/// <paramref name="State"/> is absent — the two say different things, and a
/// caller naming a state has already been specific.
/// </param>
public sealed record ListVariablesQuery(
    IReadOnlyList<FabIdentifier> Fabs,
    VariableState? State,
    bool IncludeArchived = false)
    : IQuery<Result<IReadOnlyList<VariableDto>, ListVariablesError>>;
