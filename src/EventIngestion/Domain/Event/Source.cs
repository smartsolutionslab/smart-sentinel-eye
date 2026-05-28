using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.Event;

/// <summary>
/// One of the four ingest sources (spec 006 FR-001). Wire-string
/// matches the lowercase MQTT topic segment / HTTP route segment so
/// downstream consumers can match on the same token without
/// referencing this VO.
/// </summary>
public sealed record Source(string Value) : IValueObject<string>
{
    public static Source Plc { get; } = new("plc");

    public static Source Inference { get; } = new("inference");

    public static Source Manual { get; } = new("manual");

    public static Source Webhook { get; } = new("webhook");

    public static Source From(string value) =>
        value switch
        {
            "plc" => Plc,
            "inference" => Inference,
            "manual" => Manual,
            "webhook" => Webhook,
            _ => throw new ArgumentException(
                $"Unknown event Source '{value}'. Expected: plc | inference | manual | webhook.",
                nameof(value)),
        };

    public sealed override string ToString() => Value;
}
