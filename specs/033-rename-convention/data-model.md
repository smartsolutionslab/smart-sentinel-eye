# Data Model: A name is mutable exactly when it is not an address

**Feature**: `033-rename-convention` · 2026-08-24

**No schema change.** The generated column, the partial unique index and the
value object's normalisation all already exist. What changes is which questions
the domain can ask and answer.

---

## `Camera` — one attribute becomes changeable

| Field | Before | After |
|---|---|---|
| `Id` | immutable | **unchanged** — this is the whole point (SC-001) |
| `Fab` | immutable | **unchanged**, and FR-014 forbids a path to changing it |
| `Name` | set at registration, never again | **changeable while active** |
| `Status` | `Registered` → `Decommissioned` | unchanged |
| `Version` | advances on change | advances on a rename that changes something |

A rename is an ordinary attribute edit **because** the camera is addressed by
its identifier. Had the endpoint been keyed on the name, as rules and variables
are, the same operation would be an identity change and this feature would not
exist.

---

## The uniqueness rule, and its three callers

One rule — *at most one active camera per fab per normalised name* — enforced in
two places, now asked by three callers.

```
                    ┌─ register  (does anyone hold this name?)
uniqueness rule ────┼─ retire    (releases a name)
                    └─ rename    (does anyone ELSE hold this name?)   ← new
```

| Layer | What it holds | What it is for |
|---|---|---|
| `ux_cameras_fab_name_normalized_active` on `(fab, name_normalized)` `WHERE status <> 'Decommissioned'` | the invariant | guarantees it under a race the check cannot see |
| `ICameraRepository.ExistsByNameAsync` | the same rule, a layer up | produces an answer an operator can act on |

**These are not two opinions; they are a guard and a backstop**
([research.md](./research.md) §3). The check gives the message, the index gives
the guarantee. Concluding the index makes the check redundant is spec 028's
defect exactly, and it was found on this predicate.

### Why the third caller needs a different question

`ExistsByNameAsync(fab, name)` asks *does any active camera in this fab hold this
name*. For registration that is right. For a rename it finds **the camera being
renamed** — which is active, in that fab, and holding that name whenever the
rename is a no-op or a case-only change.

So the question becomes *does any camera **other than this one**…*. That is a
widening of the contract, not a new concept, and its in-memory double changes in
the same commit — they have diverged before, here.

---

## The name a rename frees

```
line-3-inlet ──rename──▶ line-4-inlet
      │
      └── immediately registrable again in that fab
```

Falls out of the index: it keys on the *current* name and filters out retired
rows, so once the rename commits nothing active holds the old name.

**Chosen, not inherited** (FR-011). It is asserted by a test that registers a new
camera under the freed name, because spec 028's research read this same index,
concluded a requirement needed no code, and was wrong about the layer above it.

---

## Two failures that look alike and are not

The first camera operation that can fail two conflict ways at once:

| | version moved | name taken |
|---|---|---|
| Cause | someone else changed this camera | someone else holds that name |
| Code | ends `_STALE` (ADR-0119) | **must not** end `_STALE` |
| Does re-reading help? | **yes** — re-read, reapply | no — the version is fine |
| Does retrying help? | yes, after re-reading | **never**, until the name is released |

Conflating them tells an operator to retry something that cannot succeed. The
architecture test from spec 031 catches a wrong `_STALE` suffix; it does not
catch the two sharing a *status*, so that distinction is asserted directly.

---

## What is deliberately not modelled

- **No name history on the aggregate.** The audit trail is the history
  (FR-012/FR-013); duplicating it on the camera would create a second answer to
  the same question.
- **No rewriting of past events.** `CameraRegisteredV1` and `CameraRetiredV1`
  carry the name as a record of what it was at that moment. A rename appends.
- **No reservation of the freed name.** It is released immediately and anyone
  may take it, which is the same rule retirement already follows.
