using System.Collections.Concurrent;
using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IReverseIndex"/> for handler tests. Same shape
/// as the real Infrastructure impl will use but kept here so tests
/// don't depend on the Infrastructure project.
/// </summary>
public sealed class InMemoryReverseIndex : IReverseIndex
{
    private readonly ConcurrentDictionary<string, HashSet<Guid>> _byName = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, string> _labelByOverlay = new();
    private readonly ConcurrentDictionary<Guid, long> _versionByOverlay = new();

    public void UpsertOverlayReferences(Guid overlayIdentifier, string labelText)
    {
        ArgumentNullException.ThrowIfNull(labelText);
        // Drop the overlay's old entries, then re-insert from the new label.
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
        foreach (KeyValuePair<string, HashSet<Guid>> kv in _byName)
        {
            lock (kv.Value) { kv.Value.Remove(overlayIdentifier); }
        }
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
