using System.Text.Json;

namespace SmartSentinelEye.EventIngestion.Api.Requests;

/// <summary>
/// HTTP body shape for <c>POST /events/manual</c>. Spec 006 US3.
/// EventId is server-minted; ingestedAt is server-minted.
/// </summary>
public sealed record IngestManualEventRequest(
    string DeviceId,
    string Kind,
    DateTimeOffset OccurredAt,
    JsonElement Payload);
