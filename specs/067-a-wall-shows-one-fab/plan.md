# Implementation Plan: A wall shows one fab

**Spec**: `specs/067-a-wall-shows-one-fab/spec.md` · **Issue**: #2069
**Branch**: `fix/2069-a-wall-shows-one-fab` · **Base**: `origin/develop` @ `d0faa47`
**Lane**: supervised (ADR-0144 bars the autonomous lane from architectural
decisions, and #2069 says so in its own body).

---

## The three declarations (ADR-0144)

### Declaration 1 — which engineer

**Both.** `backend-engineer` **and** `frontend-engineer`, running concurrently
on disjoint file sets.

This is not a preference. The change spans two languages and two review
skills — a SignalR wire contract in C# and an RTK Query cache key in
TypeScript — and the TypeScript half carries the only real risk in the feature
(Risk 1 below). Handing both to one agent puts the risky half behind the
trivial one.

| | Owns | Files |
|---|---|---|
| `backend-engineer` | the wire contract and the two false comments | 4 source, 3 test |
| `frontend-engineer` | the derivation, the filter, the cache key | 4 source, 5 test |

**The two sets do not intersect.** Verified file by file in *Files touched*
below. Neither compiles against the other, so they proceed in parallel; but
they **must land in one PR** — see Risk 3.

### Declaration 2 — is the honest answer a new ADR?

**Yes, and it is written: ADR-0145, in this branch.**

ADR-0114 deferred exactly this question and said a later decision should be its
own, *"rather than arriving by accretion"* (`0114:101-103`). A reader looking
for the answer will look there, so the answer has to exist somewhere they can
be pointed at.

**Exactly one new ADR.** ADR-0114, ADR-007 and the constitution are **not**
amended, not read as authority, and not depended on. The ADR-007 / §VI
contradiction is #2080 and is somebody else's change.

### Declaration 3 — behaviour-changing or behaviour-preserving

**Behaviour-changing → phase 4a colour is RED.**

Three behaviours move: a label resolves in a different fab, a frame carries a
new field, and a wall drops frames it used to apply. A test arriving green is a
phase-4 failure.

**One exception, declared here so a reviewer does not read it as drift.** The
existing test `CellPage.test.tsx` → *"Renders the text a
ResolvedOverlayTextChanged frame carries, without a re-fetch"* (`:681`) is a
**characterisation control**. Its arrangement changes (arg shape) and its
**assertions must not** (`expect(label()).toBe('OEE 41.0')`,
`toBe('OEE 82.5')`). If either assertion has to be edited to pass, the cache
key moved and the push path is broken — block, do not adjust.

---

## Architecture

### Bounded contexts and layers

Two contexts and two frontend apps, and **no new boundary is crossed**.

| Context / app | Layer | What changes |
|---|---|---|
| `LayoutComposition` | Infrastructure (Broadcasting) | two hub message records gain `string Fab`; the broadcaster passes `notification.Fab` |
| `SystemVariables` | Application (Queries) | **comments only** — no behaviour |
| `apps/shared` | api + realtime seam types | `Layout.fab`; both hub message interfaces gain `fab`; `getOverlaySnapshot` takes a fab |
| `apps/kiosk-web` | feature/cell | derives the wall's fab and filters on it |
| `apps/management-web` | — | **nothing**, beyond a test fixture keeping up with a widened type |

### Entities, value objects, invariants

**None.** No domain model is touched, so constitution §II does not engage and
`PrimitiveBoundaryTests` has nothing to say. The `string Fab` added to the hub
records is a **wire shape in Infrastructure**, in the same category as
`Shared.Contracts`' primitives and for the same reason: SignalR serialises it
and `FabIdentifier` lives in a context the record must not drag onto the wire.
The broadcaster reads `notification.Fab`, which is already a `string` at that
seam.

**The one invariant this feature adds is a client-side one**, and it is
ADR-0145's: *a rendered wall applies only frames belonging to its layout's fab.*
It cannot be enforced by an architecture test — nothing in NetArchTest reaches
TypeScript — so it is enforced by the tests in tasks.md and by nothing else.
Say so in review rather than assuming a guard exists.

### Messaging

**Unchanged.** No integration event, no queue, no outbox, no saga.
`ResolvedOverlayTextChangedV1` already carries `EventMetadata.Fab` — spec 063
put it there. The gap is entirely between that event and the browser: the
broadcaster reads the fab, uses it, and then does not forward it. The domain →
integration event path is untouched.

### Boundary rules

- No cross-context project reference is added. `LayoutComposition.Infrastructure`
  already owns both hub records.
- `apps/shared` keeps its position as the only place a wire type is declared;
  `kiosk-web` imports, never redeclares.
- `ServiceDefaults.Authorization` is not modified. `FabResolution` is *called*
  by an endpoint that already calls it, with a parameter it already accepts.

---

## Phase 4b design — the shape the change lands in

### Backend (7 files, `backend-engineer`)

1. `ResolvedOverlayTextChangedHubMessage.cs:8` → `(Guid Overlay, string Fab, string ResolvedText, long Version)`.
2. `OverlayHighlightChangedHubMessage.cs:9` → `(Guid Overlay, string Fab, int DurationMs)`.
3. `SignalRLayoutLifecycleBroadcaster.cs:106` and `:124` → pass `Fab: notification.Fab`.
   Both notifications already expose it; it is the value that already selects
   the group two lines later.
4. `GetOverlaySnapshotQueryHandler.cs:40-44` and `:67-73` → correct both
   comments. The ordering **stays**: it is right for a console caller who names
   no fab (ADR-0115 §2), and it is what US1's control scenario asserts. What
   was wrong was the justification, not the code.

**Field position.** `Fab` goes **second**, next to `Overlay`, not appended. No
C# test constructs either record (`grep -rn "HubMessage" tests/` → nothing) and
the JS side reads by name, so nothing breaks; putting the addressing fields
together is what a later reader will expect.

### Frontend (9 files, `frontend-engineer`)

5. `apps/shared/src/api/layouts.api.ts:35-43` → `Layout` gains `fab: string`.
   `PublishedLayout` deliberately does **not** — spec, *Out of scope*.
6. `apps/shared/src/realtime/layoutHub.ts:55-59` and `:68-71` → both message
   interfaces gain `fab: string`.
7. `apps/shared/src/api/systemVariables.api.ts:142-155` → `getOverlaySnapshot`
   takes `{ overlayIdentifier: string; fabId: string }`, sends both as params,
   and `providesTags` reads `arg.overlayIdentifier` for its tag id so
   `invalidateTags([{ type: 'OverlaySnapshot', id: overlay }])` keeps matching.
8. `apps/kiosk-web/src/features/cell/CellPage.tsx` —
   - derive `const wallFab = data?.fab` from the already-fetched layout;
   - in `onResolvedOverlayTextChanged` (`:140`), test the fab **first**, before
     `overlayTextVersionsRef` is read or written;
   - in `onOverlayHighlightChanged` (`:155`), test the fab before
     `startHighlight`;
   - pass `fab` to `Tile`, which calls `useGetOverlaySnapshotQuery` (`:283`)
     with the new object arg;
   - `upsertQueryData('getOverlaySnapshot', …)` (`:148`) takes the **identical**
     object arg — see Risk 1.

**The filter is one expression, and it fails closed:**
`if (message.fab !== wallFab) return;` — an absent `fab` is `undefined`, which
never equals a fab name.

### What does not change, and must be seen not to

- `GET /system-variables/snapshot`'s server behaviour, including its ordinal
  ordering for a caller who names no fab.
- Management-web's snapshot behaviour (its `useGetOverlaySnapshotQuery` mock at
  `App.test.tsx:141` ignores its arguments entirely).
- The hub's group membership. A multi-fab console still joins both groups and
  still receives both plants' frames — correctly.
- `IReverseIndex`'s keying. Out of scope, spec.

---

## Risks

### Risk 1 — the snapshot cache key moves, and the push path breaks silently

**The feature's one real hazard, and the reconnaissance did not name it.**

`getOverlaySnapshot`'s query argument *is* its RTK Query cache key. Today it is
the bare `overlayIdentifier` string, and **five call sites depend on that**:

| File | Line | Depends on |
|---|---|---|
| `apps/shared/src/api/systemVariables.api.ts` | 151-154 | `providesTags` uses the raw arg as the tag `id` |
| `apps/kiosk-web/.../CellPage.tsx` | 148 | `upsertQueryData('getOverlaySnapshot', message.overlay, …)` |
| `apps/kiosk-web/.../CellPage.tsx` | 283 | `useGetOverlaySnapshotQuery(overlayIdentifier ?? '')` |
| `apps/kiosk-web/.../CellPage.test.tsx` | 665, 687 | `.select(overlayIdentifier)` and an `upsertQueryData` |
| `apps/shared/src/api/systemVariables.api.test.ts` | 34, 49 | `.initiate('ovl-1')` |

If `:283` moves to an object arg and `:148` does not follow with the **same**
object, the push writes to a cache entry nothing reads: the tile never updates,
every fab assertion passes, and the wall goes quiet instead of wrong. That is a
worse outcome than the defect.

**Mitigation, in order:** the regression control in T004 (a matching frame still
updates the label) is what catches it; `CellPage.test.tsx:681` is the same
assertion at the existing level and must pass **with its assertions
unmodified**; and step 6 of the spec's manual procedure catches it with a human
watching. Do not remove any of the three.

### Risk 2 — the filter placed after the version guard

The failure it produces is *silence*, not a wrong label, and silence looks like
"working" on a wall nobody is changing. The order is FR-005, it is asserted in
T004, and it is the reason US2's conflict scenario asserts a **lower**-versioned
munich frame afterwards.

### Risk 3 — the halves shipping separately

The client filter fails closed. Deployed without the server field, `message.fab`
is `undefined`, nothing matches, and **every wall freezes**. One branch, one PR,
both halves. Do not split this into a backend PR and a frontend PR, and do not
stack them.

### Risk 4 — widening two TypeScript interfaces breaks unrelated test fixtures

Mechanical, but it means the engineers touch **test** files. Adding required
fields makes these literals fail typecheck:

| File | Line | Literal |
|---|---|---|
| `apps/kiosk-web/src/features/revocation/useLayoutLifecycle.test.tsx` | 60, 83 | both hub messages |
| `apps/kiosk-web/src/features/cell/CellPage.test.tsx` | 114, 422-570 | the `chain()` `Layout` builder; 8 highlight literals |
| `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` | 43 | the `chain()` `Layout` builder |

These are **compile fixups**: add the field, change no assertion. A reviewer
should be able to read every one of them as "a field appeared". Anything more
than that in these files is a finding.

### Risk 5 — `Layout.Fab` is only as good as the data (spec 017 FR-018)

Recorded in ADR-0145 and in the spec's *Known caveat*. Not solved here, and not
claimed.

---

## Files touched

**`backend-engineer`** — 4 source, 3 test, disjoint from the list below:

```
src/LayoutComposition/Infrastructure/Broadcasting/ResolvedOverlayTextChangedHubMessage.cs
src/LayoutComposition/Infrastructure/Broadcasting/OverlayHighlightChangedHubMessage.cs
src/LayoutComposition/Infrastructure/Broadcasting/SignalRLayoutLifecycleBroadcaster.cs
src/SystemVariables/Application/Queries/Handlers/GetOverlaySnapshotQueryHandler.cs   (comments only)
tests/Integration.Tests/SystemVariables/OpeningLabelResolvesInTheWallsFabTests.cs    (new)
tests/Integration.Tests/SystemVariables/ResolvedTextReachesItsFabTests.cs
tests/Integration.Tests/Automation/EventReachesItsEffectsTests.cs
```

**`frontend-engineer`** — 4 source, 5 test:

```
apps/shared/src/api/layouts.api.ts
apps/shared/src/api/systemVariables.api.ts
apps/shared/src/realtime/layoutHub.ts
apps/kiosk-web/src/features/cell/CellPage.tsx
apps/kiosk-web/src/features/cell/CellPage.test.tsx
apps/kiosk-web/src/features/revocation/useLayoutLifecycle.test.tsx
apps/shared/src/api/systemVariables.api.test.ts
apps/management-web/src/features/layouts/LayoutsPage.test.tsx
apps/management-web/src/App.test.tsx                                (only if its mock stops typechecking)
```

**Documents, already written in this branch:**

```
docs/adr/0145-a-kiosks-fab-is-derived-from-its-wall.md
specs/067-a-wall-shows-one-fab/{spec,plan,tasks}.md
```

---

## The red, per level, and what each honestly proves

### Integration — `Integration.Tests` (Aspire fixture, ADR-0103)

**T001, US1 — `OpeningLabelResolvesInTheWallsFabTests.cs` (new).** No hub at
all. As `op-multi@smart-sentinel-eye.test` / `Operator1234`, with `oeeline1`
defined in **both** fabs at different values and a published overlay
referencing it:

- `GET /system-variables/snapshot?overlayIdentifier=X` → resolves **dresden**.
  Green today; it is the control, and it is what proves the ordering is
  ordinal-first rather than accidental.
- `…&fabId=munich` → resolves **munich**. **Also green today** — and that is
  the point: it proves the endpoint was always capable and only the caller was
  silent. Recorded as a control, not claimed as the red.
- `…&fabId=dresden` as `op-dresden` → 403. Existing guard, regression control.

**This file's red is therefore not at this level.** Say so in the file's
remarks rather than letting a later reader mistake three greens for proof of a
fix. US1's actual red is T004's frontend assertion that `getOverlaySnapshot` is
*called* with the layout's fab.

**T002, US2 — `ResolvedTextReachesItsFabTests.cs`, a third connection.** The
file already holds two connections over one write and explains in its class
remarks why the arrival is the positive control for the silence. Add
`op-multi@smart-sentinel-eye.test` / `Operator1234` as a third, and a
**dresden** value change:

- the `op-dresden` connection receives the frame — positive control;
- the `op-multi` connection **also** receives it — the hazard, green today and
  green after, because that is correct group behaviour;
- the `admin` (munich-only) connection does **not** — existing scoping control;
- **the red:** the payload op-multi received carries `fab == "dresden"`. There
  is no `fab` property today, so this fails on the property lookup.

Note in the file that the fix does **not** stop the frame arriving. It makes it
self-describing so the wall can refuse it. The refusal is asserted in T004 and
nowhere else at this level.

**T003, US2 — `EventReachesItsEffectsTests.cs`.** That file already listens
for a real `OverlayHighlightChanged` frame off a real rule firing
(`ListenForHighlightAsync:377-400`). One added assertion — the frame carries
`fab == "munich"` — gives the highlight field a red without building an
Automation arrangement from scratch. Without it, the highlight's `Fab` is
asserted only by a TypeScript interface, which proves nothing about what the
server sends.

### Component — `kiosk-web` (Vitest)

**T004, US1 + US2 — `CellPage.test.tsx`.** The harness already mocks the hub
(`:55-64`, capturing the callbacks) and the snapshot hook (`:37-46`), so every
assertion below is reachable without new scaffolding. Four reds and one
control, all listed in tasks.md.

### Not tested, and why

- **No e2e.** `e2e/cameras.spec.ts:39-44` declines a second Keycloak account
  through the browser and states the reason. Repeating that judgement here.
- **No unit test on the broadcaster.** Nothing constructs those records today,
  and a test asserting that a record has a field it was just given asserts the
  compiler. The wire is proved at the integration level, where a real client
  reads real JSON.

---

## Constitution and ADR alignment

| Rule | Bearing |
|---|---|
| §II value objects (ADR-0139/0140) | Not engaged — no domain model touched. The hub records are Infrastructure wire shapes. |
| §III bounded-context isolation | No new reference; nothing crosses via anything but existing seams. |
| §IV latency budget | `Event → overlay state`. Cited in spec; no re-measurement claimed. |
| §VII observability (ADR-0117) | Unchanged — no leg's Measured or Dashboard cell moves. |
| §Testing — new behaviour starts red | Declaration 3. One declared characterisation control. |
| ADR-0105 guards | `Ensure.That` already present at both broadcaster entry points; nothing added. |
| ADR-0141 `Option<T>` | Not engaged — no Domain or Application parameter added. |
| ADR-0049 `CancellationToken` | Existing signatures unchanged. |
| ADR-0084 metrics (300 LOC/file) | `CellPage.tsx` is 434 lines and the limit is a C# SonarAnalyzer rule, not a TS one — but the file is large and the filter must be one line in an existing callback, not a new helper layer. |
| ADR-0113 concurrency | Not engaged. No write path. |
| ADR-0030 / ADR-0086 commits | Conventional Commits, **no `Co-Authored-By`**. |

---

## What is explicitly not being built

The four *Out of scope* items in spec.md, unchanged: the `(fab, overlay)`
version key, `PublishedLayoutDto.Fab`, a highlight version guard, and anything
touching ADR-007 or §VI (#2080). Each is named there with its reason. None is
a prerequisite for either user story.
