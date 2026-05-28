namespace SmartSentinelEye.EventIngestion.Api.Requests;

/// <summary>
/// Body of <c>POST /webhook-integrations</c>. The response contains
/// the integration identifier + the freshly-generated bearer token
/// plaintext, shown to the caller exactly once.
/// </summary>
public sealed record RegisterWebhookIntegrationRequest(string Name, string DefaultKind);
