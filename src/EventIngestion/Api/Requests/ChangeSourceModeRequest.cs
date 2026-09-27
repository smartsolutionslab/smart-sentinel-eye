namespace SmartSentinelEye.EventIngestion.Api.Requests;

/// <summary>
/// Wire shape for <c>PUT /event-sources/{source}/mode</c>. A primitive is
/// correct here — this is a wire shape, and §II's scope is the domain model.
/// </summary>
public sealed record ChangeSourceModeRequest(string Mode);
