using System.Collections.Concurrent;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Resolution;

/// <summary>
/// Singleton in-memory implementation of <see cref="IReverseIndex"/>
/// (spec 005 plan.md). Backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// per axis; concurrent reads + writes are safe.
///
/// <para>
/// Rebuilt on cold start by <c>ReverseIndexSeederHostedService</c>
/// which calls overlay-designer's HTTP API. Held in memory only —
/// SystemVariables.Domain remains the authoritative store for
/// variables and OverlayDesigner.Domain remains authoritative for
/// overlay labels.
/// </para>
/// </summary>
public sealed class InMemoryReverseIndex : IReverseIndex
{
    private readonly ConcurrentDictionary<string, HashSet<Guid>> byName = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<string>> labelsByOverlay = new();

    public void UpsertOverlayReferences(Guid overlayIdentifier, IReadOnlyList<string> labelTexts)
    {
        Ensure.That(labelTexts).IsNotNull();
        RemoveOverlayInternal(overlayIdentifier);
        labelsByOverlay[overlayIdentifier] = labelTexts;
        foreach (string name in labelTexts.SelectMany(PlaceholderParser.ExtractNames).Distinct(StringComparer.Ordinal))
        {
            HashSet<Guid> set = byName.GetOrAdd(name, _ => []);
            lock (set) { set.Add(overlayIdentifier); }
        }
    }

    public void RemoveOverlay(Guid overlayIdentifier)
    {
        RemoveOverlayInternal(overlayIdentifier);
        labelsByOverlay.TryRemove(overlayIdentifier, out _);
    }

    private void RemoveOverlayInternal(Guid overlayIdentifier)
    {
        // S3267 prefers `Select(kv => kv.Value)` but the per-value
        // lock makes that impossible — we need each HashSet pinned
        // before mutating it.
#pragma warning disable S3267
        foreach (KeyValuePair<string, HashSet<Guid>> kv in byName)
        {
            lock (kv.Value) { kv.Value.Remove(overlayIdentifier); }
        }
#pragma warning restore S3267
    }

    public IReadOnlyCollection<Guid> LookupOverlays(string variableName)
    {
        if (!byName.TryGetValue(variableName, out HashSet<Guid>? set))
        {
            return Array.Empty<Guid>();
        }

        lock (set) { return set.ToArray(); }
    }

    public IReadOnlyList<string>? LookupLabelTexts(Guid overlayIdentifier) =>
        labelsByOverlay.TryGetValue(overlayIdentifier, out IReadOnlyList<string>? labels) ? labels : null;

    public IReadOnlyCollection<Guid> AllOverlays() => labelsByOverlay.Keys.ToArray();
}
