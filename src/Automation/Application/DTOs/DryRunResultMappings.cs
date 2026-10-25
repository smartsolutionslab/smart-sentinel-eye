namespace SmartSentinelEye.Automation.Application.DTOs;

// A dry run has no source model, so its two outcomes are named factories rather than an extension.
internal static class DryRunResultMappings
{
    public static DryRunResultDto NoMatch() => new(Matched: false, EvaluatedValue: null);

    public static DryRunResultDto Matched(string? evaluatedValue) => new(Matched: true, EvaluatedValue: evaluatedValue);
}
