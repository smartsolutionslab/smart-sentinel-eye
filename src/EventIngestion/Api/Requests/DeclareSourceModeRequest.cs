namespace SmartSentinelEye.EventIngestion.Api.Requests;

/// <summary>
/// Wire shape for <c>POST /event-sources</c>. Primitives are correct here —
/// this is a wire shape, and §II's scope is the domain model.
/// </summary>
public sealed record DeclareSourceModeRequest(string Source, string Mode);
