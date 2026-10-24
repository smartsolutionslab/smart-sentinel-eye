using System.Text;
using System.Text.Json;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Redaction;

/// <summary>
/// Per-row decision of the in-place redaction of camera URLs stored before spec 377 (ADR-0184): which stored
/// values are more than their origin, what replaces them, and how many bytes the row's
/// <c>payload_size_bytes</c> shrinks by. The register is <see cref="AuditPayloadRedaction.UrlFields"/>, so
/// a field the capture handler redacts is a field this rewrites.
/// </summary>
public static class StoredCameraUrlRewrite
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Keys =
        AuditPayloadRedaction.UrlFields.ToDictionary(entry => entry.Key.Name, entry => entry.Value);

    public static IReadOnlyCollection<string> EventKinds { get; } = [.. Keys.Keys];

    public static IReadOnlyList<string> KeysFor(string eventKind) =>
        Keys.TryGetValue(eventKind, out var keys)
            ? keys
            : throw new InvalidOperationException($"Event kind {eventKind} has no registered URL fields.");

    public static Option<Plan> PlanFor(string eventKind, IReadOnlyDictionary<string, string?> storedValues)
    {
        Ensure.That(storedValues).IsNotNull();

        List<Change> changes = [];

        foreach (var key in KeysFor(eventKind))
        {
            if (storedValues.GetValueOrDefault(key) is not { } old)
            {
                continue;
            }

            var origin = UrlRedaction.ToOrigin(old);

            if (!string.Equals(origin, old, StringComparison.Ordinal))
            {
                changes.Add(new(key, old, origin));
            }
        }

        return changes.Count == 0
            ? Option<Plan>.None
            : Option<Plan>.Some(new(changes, changes.Sum(change => Bytes(change.Old) - Bytes(change.New))));
    }

    private static int Bytes(string value) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value));

    public sealed record Plan(IReadOnlyList<Change> Changes, int SizeDelta);

    public sealed record Change(string Key, string Old, string New);
}
