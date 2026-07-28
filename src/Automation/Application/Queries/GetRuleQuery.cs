using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Queries;

/// <summary>Fetches a single rule by name (spec 007 T089).</summary>
public sealed record GetRuleQuery(string Name) : IQuery<Result<RuleDto, GetRuleError>>;
