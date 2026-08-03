using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Queries;

/// <summary>
/// Evaluates a stored rule against a sample event without persisting
/// anything or publishing an integration event (spec 007 T089).
///
/// <para>
/// <paramref name="SampleEvent"/> is the canonical evaluation root the real
/// pipeline builds — <c>{"source":…,"kind":…,"device":…,"payload":{…}}</c> —
/// so what the author tries here is what the rule will actually see.
/// </para>
/// </summary>
public sealed record DryRunRuleQuery(IReadOnlyList<FabIdentifier> Fabs, string Name, string? SampleEvent)
    : IQuery<Result<DryRunResultDto, DryRunRuleError>>;
