using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Queries;

/// <summary>
/// Lists rules, newest first (spec 007 T089). Every filter is optional; a
/// null filter means "no constraint" rather than "match null", so an
/// unfiltered call returns the whole catalogue.
/// </summary>
public sealed record ListRulesQuery(string? State, string? TriggerSource, string? TriggerKind)
    : IQuery<Result<IReadOnlyList<RuleDto>, ListRulesError>>;
