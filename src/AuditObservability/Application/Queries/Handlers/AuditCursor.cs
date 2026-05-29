using System.Globalization;
using System.Text;

namespace SmartSentinelEye.AuditObservability.Application.Queries.Handlers;

/// <summary>
/// Opaque base64-encoded <c>(occurredAtTicks, auditIdentifier)</c>
/// tuple used by every audit query handler for stable
/// cursor pagination. Concurrent inserts on a chunk past the
/// cursor's <c>OccurredAt</c> don't shift the window because the
/// predicate is strict-greater/less-than on the composite.
/// </summary>
internal static class AuditCursor
{
    public static string Encode(DateTimeOffset occurredAt, Guid auditIdentifier)
    {
        string raw = $"{occurredAt.UtcTicks:D19}.{auditIdentifier:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    public static (DateTimeOffset OccurredAt, Guid AuditIdentifier)? TryDecode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;
        try
        {
            string raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            int dot = raw.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0) return null;
            long ticks = long.Parse(raw[..dot], CultureInfo.InvariantCulture);
            Guid id = Guid.ParseExact(raw[(dot + 1)..], "N");
            return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }
}
