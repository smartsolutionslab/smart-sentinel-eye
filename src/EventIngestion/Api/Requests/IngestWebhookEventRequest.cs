using System.Text.Json;

namespace SmartSentinelEye.EventIngestion.Api.Requests;

/// <summary>
/// Body of <c>POST /events/webhook/{integrationName}</c>. Spec 006
/// US4. Integration's default kind is used unless the body overrides
/// it. EventId is server-minted; ingestedAt is server-minted.
/// </summary>
public sealed record IngestWebhookEventRequest(
    string? Kind,
    DateTimeOffset? OccurredAt,
    JsonElement Payload);
