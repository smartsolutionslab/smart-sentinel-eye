namespace SmartSentinelEye.Automation.Api.Requests;

/// <summary>
/// Body for <c>POST /rules/{name}/dry-run</c> (spec 007 T090).
/// <see cref="SampleEvent"/> is the canonical evaluation root the live
/// pipeline builds — <c>{"source":…,"kind":…,"device":…,"payload":{…}}</c> —
/// passed through as a raw JSON string so the author can paste exactly what
/// a device emits.
/// </summary>
public sealed record DryRunRuleRequest(string SampleEvent);
