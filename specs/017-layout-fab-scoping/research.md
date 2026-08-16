# Research: Fab-scope layout composition

**Feature**: `017-layout-fab-scoping` | **Date**: 2026-08-16

The spec left exactly one mechanism open — how LayoutComposition learns a
camera's fab (FR-014) — and flagged it as the feature's only new coupling.
That is §1 below. Three further findings are recorded because reading the code
changed the design rather than confirming it.

---

## 1. How does LayoutComposition learn a camera's fab? (FR-014)

**Decision**: **validate synchronously against CameraCatalog using the
caller's own token**, narrowed to the layout's fab. A tile is accepted when
its camera appears in `GET /cameras?fabId=<the layout's fab>` as that
operator; refused otherwise.

**Rationale**, in the order that decided it:

1. **It needs no new credential.** The alternative below requires reading the
   whole catalogue across every fab, which is exactly what forced ADR-0116's
   cross-fab service account in spec 016. Here the operator already holds the
   fab the layout is in, so their own token answers the question — and it
   answers no more than that.
2. **It reuses CameraCatalog's fab scoping instead of re-implementing it.**
   "Is this camera in that fab" is a question CameraCatalog already answers
   correctly, as of spec 015. A local copy of the rule is a second place for
   it to be wrong.
3. **It cannot go stale.** There is no projection to seed, no backfill, and no
   window in which a freshly registered camera is unusable.
4. **The failure mode is narrow and honest.** CameraCatalog down means layouts
   cannot be created or edited. Reading layouts, the hub pushes, and video are
   all unaffected. Authoring is not a 24/7-critical path; watching is.
5. **It is not on the latency budget.** Constitution §IV covers event →
   overlay; this is an authoring write.

**Alternatives considered**:

- ***A local `camera → fab` projection fed by `CameraRegisteredV1`.*** The
  tempting one, and the one this context's existing habits suggest — it
  already consumes four integration events. Rejected on the **seeding**
  problem: the projection starts empty, `CameraRegisteredV1` does not re-fire
  for cameras that already exist, and FR-015 refuses a tile whose camera
  cannot be resolved. Every pre-existing camera would therefore be unusable
  until the projection were seeded — and seeding means reading the whole
  catalogue across all fabs, i.e. a second ADR-0116 credential. It trades a
  narrow synchronous dependency for a broad standing one.
- ***Denormalise the fab onto the tile at creation.*** Does not help: writing
  the fab onto the tile still requires knowing it, which is the question.
- ***Trust the client to send the camera's fab.*** Rejected outright — it is a
  trust-boundary input that would make FR-014 self-certifying.

**The cost, stated rather than buried**: this is a **synchronous request-path
dependency from LayoutComposition to CameraCatalog**, the first one this
context has. It does not breach ADR-0016 — the call is over the published HTTP
API, carries no value objects across the boundary and creates no project
reference — but it is new, and §III of [plan.md](./plan.md) records it as a
bounded exception rather than a clean sheet.

**#1435 bites here for the second time.** CameraCatalog still has no
read-by-identifier route, so validating one tile means fetching a page of the
catalogue rather than asking about one camera. With ≤4 tiles per layout
(ADR-0112) and a 200-camera page that is one request per write in every
realistic case, so it is a clumsiness rather than a problem — but it is the
same missing endpoint that shaped spec 016, and landing #1435 would turn this
into a precise lookup. Recorded as an improvement, not a blocker.

---

## 2. How is "which fabs reference this overlay" answered? (Half B)

**Decision**: a **query over this context's own tables**, run when the frame is
about to be sent. Nothing is stored, and no overlay gains a fab.

```text
layout_revision_tiles.overlay_id = <the overlay>
  → layout_revisions.state = 'Published'
    → layouts.fab          (distinct)
```

**Rationale**: every column involved already exists — a tile has carried a
nullable `overlay_id` since spec 010 — so the only new input is `layouts.fab`
from Half A. The answer follows current usage automatically: archiving the
last referencing layout narrows the audience with nothing to invalidate.

This is what makes closing #1397 in one feature reasonable despite the two
halves being different problems. Half B is not an open design question; it is a
join that becomes available the moment Half A lands.

**Alternatives considered**:

- ***A stored `overlay → fabs` index, maintained on layout writes.*** Rejected.
  It would need invalidating on every publish, archive, revert and draft edit —
  four places to forget — to answer a question a join answers exactly.
- ***Give the overlay a fab.*** Forbidden by ADR-0115, and wrong on its own
  terms: an overlay is a template, and the same one can legitimately be used by
  two plants.

**Where the resolution lives**: in the **Application-layer handlers**
(`OverlayRevisionPublishedV1Handler`, `OverlayRevisionArchivedV1Handler`), not
in the broadcaster. The broadcaster's job today is to map a notification to a
hub message and send it; giving it a database query would make it the only
piece of Infrastructure/Broadcasting that reads state. The notification records
carry the answer instead — `Fab` for the layout frames, `Fabs` for the overlay
frames.

---

## 3. The backfill *can* be SQL here, unlike spec 016

**Finding**: layouts and their tiles are in this context's own database, so a
migration can attribute pre-existing rows without reading another context's
data. Spec 016 could not do this **only** because cameras were in a separate
database — not because runtime attribution is better.

So this follows spec 015's precedent exactly
(`20260810164633_FabScopeCameras`): add nullable, `UPDATE ... SET fab =
'munich' WHERE fab IS NULL` inside a `DO $$` block that `RAISE WARNING`s with
the count, then tighten to `NOT NULL` in the same migration.

**Warns rather than fails**, for spec 015's reason: refusing would block a
deployment whose layouts really are Munich's, which is every deployment that
exists. The warning is for the one case this cannot detect.

**Consequence for FR-018**: after the backfill guesses `munich`, a pre-existing
layout may hold a tile whose camera is in another fab — a state FR-014 forbids
going forward. Those tiles are **not** retro-validated. The mismatch is the
migration's own guess, not an operator's choice, and failing over it would
block the deployment the migration exists to fix.

---

## 4. LayoutComposition already consumes four integration events

**Finding**, and the reason §III below is a smaller step than it looks:
`Application/EventHandlers/` already subscribes to `OverlayHighlightRequestedV1`,
`OverlayRevisionPublishedV1`, `OverlayRevisionArchivedV1` and
`ResolvedOverlayTextChangedV1`. Cross-context *messaging* is routine here.

What is new is a cross-context **HTTP call on the request path**. That
distinction is the whole of the §III argument: this context has always
listened to others; it has never asked one a question and waited.

---

## 5. Two of six frames are already scoped, and the mechanism works

**Finding**: `LayoutLifecycleHub.FabGroup(fab)` exists, and `OnConnectedAsync`
joins one group per fab from the same `groups` claim `FabClaims` reads — so the
hub and the server-side guard cannot disagree. `ResolvedOverlayTextChanged`
(#1396) and `OverlayHighlightChanged` (#1398) already use it.

This feature therefore **adds no delivery mechanism**. It supplies the missing
input — which fab(s) a frame belongs to — to four call sites that currently say
`Clients.All`. Verified by reading
`Infrastructure/Broadcasting/SignalRLayoutLifecycleBroadcaster.cs`, not
assumed from the issue.

---

## Not researched, deliberately

**Whether the fab mechanism is right.** ADR-0114 settled it and four specs have
applied it. Re-opening it at the fifth would be re-deciding a closed question.

**Whether an overlay should have a fab.** ADR-0115 settled it, and this feature
is built on that answer rather than around it.
