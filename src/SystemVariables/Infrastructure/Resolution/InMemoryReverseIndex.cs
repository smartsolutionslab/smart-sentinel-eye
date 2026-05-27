using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, HashSet<Guid>> _byName = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, string> _labelByOverlay = new();
    private readonly ConcurrentDictionary<Guid, long> _versionByOverlay = new();

    public void UpsertOverlayReferences(Guid overlayIdentifier, string labelText)
    {
        ArgumentNullException.ThrowIfNull(labelText);
        RemoveOverlayInternal(overlayIdentifier);
        _labelByOverlay[overlayIdentifier] = labelText;
        foreach (string name in PlaceholderParser.ExtractNames(labelText))
        {
            HashSet<Guid> set = _byName.GetOrAdd(name, _ => new HashSet<Guid>());
            lock (set) { set.Add(overlayIdentifier); }
        }
    }

    public void RemoveOverlay(Guid overlayIdentifier)
    {
        RemoveOverlayInternal(overlayIdentifier);
        _labelByOverlay.TryRemove(overlayIdentifier, out _);
    }

    private void RemoveOverlayInternal(Guid overlayIdentifier)
    {
        // S3267 prefers `Select(kv => kv.Value)` but the per-value
        // lock makes that impossible — we need each HashSet pinned
        // before mutating it.
#pragma warning disable S3267
        foreach (KeyValuePair<string, HashSet<Guid>> kv in _byName)
        {
            lock (kv.Value) { kv.Value.Remove(overlayIdentifier); }
        }
#pragma warning restore S3267
    }

    public IReadOnlyCollection<Guid> LookupOverlays(string variableName)
    {
        if (!_byName.TryGetValue(variableName, out HashSet<Guid>? set)) return Array.Empty<Guid>();
        lock (set) { return set.ToArray(); }
    }

    public string? LookupLabelText(Guid overlayIdentifier) =>
        _labelByOverlay.TryGetValue(overlayIdentifier, out string? label) ? label : null;

    public IReadOnlyCollection<Guid> AllOverlays() => _labelByOverlay.Keys.ToArray();

    public long NextVersionFor(Guid overlayIdentifier) =>
        _versionByOverlay.AddOrUpdate(overlayIdentifier, 1, (_, current) => current + 1);

    public long CurrentVersionFor(Guid overlayIdentifier) =>
        _versionByOverlay.TryGetValue(overlayIdentifier, out long v) ? v : 0;
}
