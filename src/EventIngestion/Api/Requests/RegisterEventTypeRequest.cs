namespace SmartSentinelEye.EventIngestion.Api.Requests;

/// <summary>
/// Wire shape for <c>POST /event-types</c>. A primitive is correct here —
/// this is a wire shape, and §II's scope is the domain model (spec 143 plan.md §5).
/// </summary>
public sealed record RegisterEventTypeRequest(string Kind);
