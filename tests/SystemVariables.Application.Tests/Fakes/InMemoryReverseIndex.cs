using System.Collections.Concurrent;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IReverseIndex"/> for handler tests. Same shape
/// as the real Infrastructure impl will use but kept here so tests
/// don't depend on the Infrastructure project.
/// </summary>
public sealed class InMemoryReverseIndex : IReverseIndex
{
    private readonly ConcurrentDictionary<string, HashSet<Guid>> byName = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<string>> labelsByOverlay = new();

    public void UpsertOverlayReferences(Guid overlayIdentifier, IReadOnlyList<string> labelTexts)
    {
        Ensure.That(labelTexts).IsNotNull();
        // Drop the overlay's old entries, then re-insert from the new labels.
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
        foreach (KeyValuePair<string, HashSet<Guid>> kv in byName)
        {
            lock (kv.Value) { kv.Value.Remove(overlayIdentifier); }
        }
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
