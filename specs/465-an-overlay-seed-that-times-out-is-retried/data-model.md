# Data model — spec 465

Nothing persisted. One in-memory addition.

## `InMemoryReverseIndex`

| Member | Type | Meaning |
|---|---|---|
| `byName` | `ConcurrentDictionary<string, HashSet<Guid>>` | existing: variable name → overlays |
| `labelsByOverlay` | `ConcurrentDictionary<Guid, IReadOnlyList<string>>` | existing: overlay → label texts |
| `touchedByEvent` | `ConcurrentDictionary<Guid, byte>` | **new:** overlays an event has upserted or removed since the process started |

Transitions:

- `UpsertOverlayReferences` / `RemoveOverlay` (event handlers): add to `touchedByEvent`, then act as today.
- `TrySeedOverlayReferences` (seeder only): if the overlay is in `touchedByEvent`, do nothing and return
  `false`; else write as an upsert without marking and return `true`. Runs under the same lock as the
  two event writes, so "checked untouched, then written" cannot interleave with an event.
- Never cleared. Bounded by the number of overlays; lost on restart, like the index.

## Seeder state

`attempt` (int, from 1) and `delay` (TimeSpan: 2 s → ×2 → 30 s) are locals of the loop. Nothing is shared
or persisted.
