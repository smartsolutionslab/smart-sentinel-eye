using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Queries;

/// <summary>
/// Lists rules, newest first (spec 007 T089). Every filter is optional; a
/// null filter means "no constraint" rather than "match null", so an
/// unfiltered call returns the whole catalogue *within the caller's fabs*.
///
/// <para>
/// <paramref name="Fabs"/> is not a filter the caller chooses — it is the
/// set they are entitled to see (spec 013 FR-005). An empty set returns
/// nothing, which is why an operator assigned to no fab sees no rules rather
/// than every rule.
/// </para>
/// </summary>
public sealed record ListRulesQuery(
    IReadOnlyList<FabIdentifier> Fabs, string? State, string? TriggerSource, string? TriggerKind)
    : IQuery<Result<IReadOnlyList<RuleDto>, ListRulesError>>;
