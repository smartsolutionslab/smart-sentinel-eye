using System.Text;
using System.Text.Json;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.Event;

/// <summary>
/// Opaque JSON payload carried inside every ingested event
/// (spec 006 FR-005). Validated only for "is valid JSON" and "≤ 64 KB
/// when canonical-form encoded as UTF-8". Schema is the producer's
/// contract with downstream consumers; EventIngestion does not look
/// inside.
/// </summary>
public sealed record Payload : IValueObject<string>
{
    public const int MaximumBytes = 64 * 1024;

    public string Value { get; }

    private Payload(string canonicalJson) => Value = canonicalJson;

    /// <summary>Builds a Payload from a parsed <see cref="JsonDocument"/>.</summary>
    public static Payload From(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            document.WriteTo(writer);
        }
        if (buffer.Length > MaximumBytes)
        {
            throw new ArgumentException(
                $"Payload must be no more than {MaximumBytes} bytes; got {buffer.Length}.",
                nameof(document));
        }
        return new Payload(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>Parses raw JSON text into a Payload (rejects malformed JSON).</summary>
    public static Payload From(string rawJson)
    {
        ArgumentNullException.ThrowIfNull(rawJson);
        if (Encoding.UTF8.GetByteCount(rawJson) > MaximumBytes)
        {
            throw new ArgumentException(
                $"Payload must be no more than {MaximumBytes} bytes.", nameof(rawJson));
        }
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(rawJson);
            return From(parsed);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                "Payload is not valid JSON: " + ex.Message, nameof(rawJson), ex);
        }
    }

    public sealed override string ToString() => Value;
}
