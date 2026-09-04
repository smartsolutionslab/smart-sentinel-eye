# Tasks: A wall shows one fab

**Spec**: `specs/067-a-wall-shows-one-fab/spec.md` · **Plan**: `plan.md`
**Issue**: #2069 · **Lane**: supervised (ADR-0144 bars the autonomous lane from
architectural decisions; #2069 says so in its own body).

**Phase 4a colour: RED** (behaviour-changing, plan.md declaration 3), with
**one declared characterisation control** — `CellPage.test.tsx:681`, whose
assertions must not be edited.

---

## Parallelism (ADR-0109)

**No foundational task.** Nothing in `Shared.Kernel`, `Shared.Contracts`,
`AppHost` or an Aspire resource changes, so nothing blocks a fan-out. Both hub
records already exist in `LayoutComposition.Infrastructure`; both DTO fields
already exist on the wire.

**Two lanes, disjoint file sets, running concurrently.** The lists in plan.md →
*Files touched* do not intersect, and neither lane compiles against the other.

| Lane | Agent | Tasks |
|---|---|---|
| backend | `test-writer` → `backend-engineer` | T001, T002, T003 → T005 |
| frontend | `test-writer` → `frontend-engineer` | T004 → T006 → T007 → T008 |

**Strictly sequential within each lane:** colour before change. Every red is
*observed and captured verbatim* before any production code moves. That is
ADR-0144's phase-4 split, not a scheduling preference.

**Do not split the PR.** The client filter fails closed; shipped without the
server field it drops every frame and freezes every wall (plan.md, Risk 3).
One branch, one PR, both lanes.

**Practical note for the orchestrator:** T001, T002 and T003 need the Docker
stack (`AspireFixture`) and are the long poles. Start them first; T004 finishes
inside their runtime.

---

## Task list

### T001 [P] [US1] — the server contract US1 leans on (phase 4a) — `test-writer`

**Characterisation, expected GREEN. This is a correction to the
reconnaissance**, which described this as red today.

`GET /system-variables/snapshot` does not change (spec FR-009). Only the caller
stops being silent. So **no assertion at this level can go red-then-green for
US1** — its red lives in T004. What this file does is pin the contract the
frontend fix depends on, so a later "simplification" of the endpoint cannot
break the fix silently.

New file: `tests/Integration.Tests/SystemVariables/OpeningLabelResolvesInTheWallsFabTests.cs`.
No hub connection at all.

Arrange, as `op-multi@smart-sentinel-eye.test` / `Operator1234`: a variable
`oeeline1` in **both** fabs at different values (`?fabId=` on each write —
ADR-0114 refuses a multi-fab write that names none), a published overlay whose
text is `OEE {{oeeline1}}`, and the reverse index populated. That is roughly 30
lines mirroring `ResolvedTextReachesItsFabTests.AMunichOverlayBoundToAVariableAsync`.
**Keep it local — do not hoist a shared helper.** There is no third caller, and
ADR-0036 forbids the speculative abstraction.

Assert:

1. `?overlayIdentifier=X` with no `fabId` → resolves **dresden**'s value.
   *This is the defect, stated as a fact about the endpoint.* `"dresden" <
   "munich"` ordinally, and the test should say so in a comment so a later
   reader does not think the fab was chosen for a reason.
2. `?overlayIdentifier=X&fabId=munich` → resolves **munich**'s value. The
   control that proves the endpoint was always capable.
3. As `op-dresden@dresden.test`, `&fabId=munich` → **403**. The existing guard.

**The class remarks must state that every assertion here is green today**, and
that the fix is client-side. A file of three greens read as proof of a fix is
exactly the record-drift CLAUDE.md has had to correct before.

Command: `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~OpeningLabelResolvesInTheWallsFab"`

---

### T002 [P] [US2] — the frame that does not say whose it is (phase 4a) — `test-writer`

**RED.**

`tests/Integration.Tests/SystemVariables/ResolvedTextReachesItsFabTests.cs`.
The file already holds two connections over one write and explains in its class
remarks why the arrival is the positive control for the silence. Follow that
convention exactly.

Add a **third** connection: `op-multi@smart-sentinel-eye.test` /
`Operator1234`. Add a **dresden**-defined `oeeline1` alongside the existing
munich arrangement, and write to the dresden one.

Assert, over that one write:

- the `op-dresden` connection receives the frame — **positive control**;
- the `op-multi` connection **also** receives it — the hazard. Green today and
  green after: a connection holding both groups correctly receives both, and
  the fix does not change that;
- the `admin` (munich-only) connection does **not** — existing scoping control;
- **the red:** the payload `op-multi` received carries `fab == "dresden"`.

**Expected red:** the JSON has no `fab` property, so
`payload.GetProperty("fab")` throws `KeyNotFoundException: The given key was not
present in the dictionary` (or, written defensively with `TryGetProperty`, a
Shouldly failure on `null`). Capture whichever verbatim.

**Write in the class remarks that the fix does not stop the frame arriving.**
It makes the frame self-describing so the wall can refuse it. The refusal is
asserted in T004 and nowhere else. A reader who expects this test to go silent
after the fix will read a correct green as a regression.

Command: `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~ResolvedTextReachesItsFab"`

---

### T003 [P] [US2] — the highlight frame, which nothing else would catch (phase 4a) — `test-writer`

**RED.**

`tests/Integration.Tests/Automation/EventReachesItsEffectsTests.cs`. That file
already drives a real rule to fire and reads a real `OverlayHighlightChanged`
frame off a real hub (`ListenForHighlightAsync`, ~`:377-400`). Extend what it
captures from the frame and assert `fab == "munich"`.

**Why this task exists at all:** without it, the highlight's new field is
asserted only by a TypeScript interface, which proves nothing about what the
server sends. One assertion on an existing arrangement is far cheaper than a
new Automation fixture.

**Expected red:** no `fab` property on the highlight payload — same shape of
failure as T002.

If the assertion fights that file's arrangement (its `TaskCompletionSource<int>`
carries only `durationMs`), widen the captured type to a small record rather
than adding a second listener.

Command: `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~EventReachesItsEffects"`

---

### T004 [P] [US1] [US2] — the wall refusing what is not its own (phase 4a) — `test-writer`

**RED. This is where both stories' behaviour change is actually asserted.**

`apps/kiosk-web/src/features/cell/CellPage.test.tsx`. The harness already mocks
the hub (`:55-64`, capturing the callbacks into `capturedCallbacks`) and the
snapshot hook (`:37-46`), so nothing new is scaffolded. Give `chain()` a
`fab: 'munich'`.

Five assertions — **four red, one control**:

1. **RED (US2)** — a `ResolvedOverlayTextChanged` frame carrying
   `fab: 'dresden'` does **not** upsert the snapshot cache: the tile's
   `data-overlay-text` is unchanged.
2. **RED (US2)** — and it does **not advance the version mark**. Fire
   `{ fab: 'dresden', version: 5 }`, then `{ fab: 'munich', version: 2 }`, and
   assert the tile shows the **munich** text. This is the assertion that fails
   if the filter is placed after the guard, and it is the only one that does.
3. **RED (US2)** — an `OverlayHighlightChanged` carrying `fab: 'dresden'`
   leaves `data-highlighted="false"` on every tile bound to that overlay.
4. **RED (US1)** — `useGetOverlaySnapshotQuery` is called with the layout's
   fab. Assert on `getSnapshotMock`'s first argument.
5. **CONTROL (US2), must be green after** — a frame carrying `fab: 'munich'`
   with a higher version still updates the label without a re-fetch. Without
   it, assertions 1-3 pass against a page that applies nothing at all.

**Expected reds:** 1 and 3 fail as *"expected the label/highlight not to
change"* (the page applies every frame today); 2 fails showing the dresden
text; 4 fails because the mock is called with a bare string.

**The existing test at `:681`** — *"Renders the text a
ResolvedOverlayTextChanged frame carries, without a re-fetch"* — is the
declared characterisation control (plan.md, declaration 3). Its arrangement
will need the new arg shape in T006; **its two `expect(label()).toBe(...)`
assertions must not be edited.** If they have to be, the cache key moved:
block, do not adjust.

Command: `npm run test -w apps/kiosk-web -- CellPage`

---

### T005 [US1] [US2] — the wire contract and two false comments (phase 4b) — `backend-engineer`

Depends on **T002 and T003 observed red and captured.**

1. `ResolvedOverlayTextChangedHubMessage.cs:8` → add `string Fab` **second**,
   after `Overlay`.
2. `OverlayHighlightChangedHubMessage.cs:9` → same.
3. `SignalRLayoutLifecycleBroadcaster.cs:106` and `:124` → pass
   `Fab: notification.Fab`. It is the same value that selects the group two
   lines later; nothing new is computed.
4. `GetOverlaySnapshotQueryHandler.cs:40-44` **and** `:67-73` → correct both
   comments. **The reconnaissance named only `:67-73`; there are two.** The
   ordering itself **stays** — it is right for a console caller who names no
   fab (ADR-0115 §2), and T001 asserts it. What was wrong was the
   justification: *"a kiosk holds exactly one fab"*, which `op-multi`
   falsifies. Replace it with what is true — a caller who names no fab gets an
   arbitrary-but-stable answer, and the kiosk now names one (ADR-0145).

**No behaviour changes in SystemVariables.** If a SystemVariables test goes
red in this task, something other than a comment was edited.

**Do not** add a version guard to the highlight path, re-key `IReverseIndex`,
or touch `PublishedLayoutDto`. All three are out of scope with stated reasons.

---

### T006 [US1] — the wall's fab, derived and named (phase 4b) — `frontend-engineer`

Depends on **T004 observed red and captured.** Runs in parallel with T005.

1. `apps/shared/src/api/layouts.api.ts:35-43` → `Layout` gains `fab: string`.
   **`PublishedLayout` does not** — out of scope, spec.
2. `apps/shared/src/api/systemVariables.api.ts:142-155` → `getOverlaySnapshot`
   takes `{ overlayIdentifier: string; fabId: string }`; both go into `params`;
   `providesTags` reads `arg.overlayIdentifier` for its tag id so the existing
   `invalidateTags([{ type: 'OverlaySnapshot', id: overlay }])` calls keep
   matching.
3. `apps/kiosk-web/src/features/cell/CellPage.tsx` → pass the layout's `fab`
   into `Tile`; `Tile` calls `useGetOverlaySnapshotQuery` (`:283`) with the
   object arg; **`upsertQueryData` (`:148`) takes the identical object.**

**Read plan.md Risk 1 before starting.** The query argument *is* the cache key.
If `:283` and `:148` disagree, the push writes where nothing reads: the tile
goes quiet, and every fab assertion still passes. T004's control (5) and the
existing `:681` test are what catch it.

---

### T007 [US2] — the filter, ahead of the version guard (phase 4b) — `frontend-engineer`

Depends on **T006** — same file (`CellPage.tsx`), so sequential, same owner.

1. `apps/shared/src/realtime/layoutHub.ts:55-59` and `:68-71` → both message
   interfaces gain `fab: string`.
2. `CellPage.tsx:140` `onResolvedOverlayTextChanged` → test the fab **first**,
   before `overlayTextVersionsRef` is read or written.
3. `CellPage.tsx:155` `onOverlayHighlightChanged` → test the fab before
   `startHighlight`.

One expression each — `if (message.fab !== wallFab) return;` — which fails
closed, because an absent `fab` is `undefined` and never equals a fab name
(FR-006). **Do not** build a helper, a hook, or a filtering layer;
`CellPage.tsx` is already 434 lines.

**Order is FR-005 and is asserted by T004(2) alone.** Getting it wrong produces
silence, not a wrong label, and silence looks like a working wall.

---

### T008 [US1] [US2] — the fixtures that keep up with two widened types (phase 4b) — `frontend-engineer`

Depends on **T006 and T007.**

Adding required fields breaks these literals at typecheck. Add the field;
**change no assertion.**

| File | Line | What |
|---|---|---|
| `apps/kiosk-web/src/features/revocation/useLayoutLifecycle.test.tsx` | 60, 83 | both hub message literals |
| `apps/kiosk-web/src/features/cell/CellPage.test.tsx` | 114, 422-570 | the `chain()` builder; 8 highlight literals |
| `apps/shared/src/api/systemVariables.api.test.ts` | 34, 49 | `.initiate('ovl-1')` → the object arg |
| `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` | 43 | the `chain()` builder |
| `apps/management-web/src/App.test.tsx` | 141 | only if its argument-ignoring mock stops typechecking |

A reviewer must be able to read every hunk here as *"a field appeared"*.
Anything more is a finding.

Command: `npm run typecheck && npm run test`

---

### T009 [US1] [US2] — verify, and say what was not measured (phase 5) — orchestrator

Run the spec's *Independent end-to-end test procedure* against a booted stack.
**Step 6 is not optional** — it is the step that fails if T007 landed before
T006's cache key, or if the filter sits after the version guard.

The verification note records: the label read at step 4 before and after, the
wall's non-movement at step 5, and the munich update at step 6.

**On latency:** cite `Event → overlay state` (≤ 200 ms) as the leg touched, and
state that **no figure was taken and none is claimed**. §IV records this leg as
*"recorded, not yet readable"*; this change does not move that cell. A
verification note implying a measurement nobody made is the defect §IV's own
table exists to prevent.

---

## Dependency graph

```
T001 [P] ─ characterisation, green ─┐
T002 [P] ─ RED ─┐                   │
T003 [P] ─ RED ─┴─ backend  ─ T005 ─┤
                                    ├─ T009 (verify)
T004 [P] ─ RED ─── frontend ─ T006 ─ T007 ─ T008 ─┘
```

- T001-T004 are all `[P]`: four files, three projects, no overlap.
- T005 ‖ T006: disjoint file sets, different languages.
- T006 → T007 → T008: same file, then dependent fixtures. One owner.
- **Nothing is merged until both lanes are green.** Plan.md, Risk 3.

---

## Commits (ADR-0030 Conventional Commits · ADR-0086 **no `Co-Authored-By`**)

Suggested sequence, each building on its own:

```
docs(adr): a kiosk's fab is derived from its wall          # this commit
docs(specs): 067 — a wall shows one fab
test(integration): a multi-fab screen is told, and cannot tell   # T001-T003
test(kiosk): a wall applies only its own plant's frames          # T004
fix(layouts): a pushed frame names the fab it belongs to         # T005
fix(kiosk): the opening label resolves in the wall's fab         # T006
fix(kiosk): a foreign frame is dropped before the version mark   # T007
test(web): fixtures carry the fab their types now require        # T008
```

Each commit must build **on its own** — `develop` is rebase-merge only
(ADR-0087), so a commit that compiles only with its successor breaks
`git bisect` forever. T007's commit therefore includes the `layoutHub.ts`
interface change it depends on.

---

## Phase 3 gate (CLAUDE.md, as corrected 2026-08-28)

**Satisfied.** The gate is *the feature's issue on Project #13* — feature-level,
not per-task, since spec 028. **#2069 is on Project #13 with status
`In Progress`** (verified 2026-09-04 with `gh project item-list 13 --owner
smartsolutionslab --limit 2000`). Nothing to add, and
`/speckit-taskstoissues` is **not** to be run for this feature.

For the record, #2080 (ADR-007 / §VI) is also on the board, status `Todo`, and
is deliberately untouched by this work.
