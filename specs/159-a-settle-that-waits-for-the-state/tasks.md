# Tasks — Spec 159, a settle that waits for the state

**Phase:** 3 (Tasks) — ADR-0037
**Issue:** [#2386](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2386) · **Branch:** `fix/2386-camera-swap-tests-wait-for-the-condition`
**Spec:** `spec.md` · **Plan:** `plan.md`
**Engineer:** `frontend-engineer` · **Reviewer:** `frontend-reviewer`
**Phase 4a colour: BEHAVIOUR-PRESERVING (characterisation, observed GREEN) + a
mandatory counterfactual.** See §Phase 4a.
**Latency (§IV): N/A** — no production file changes, no leg moves, no measurement
owed.
**Delivery: one PR.**

### `[P]` markers: there are none, deliberately

ADR-0109 marks a task `[P]` when it owns disjoint files. **Every task below edits
the same single file**, so the disjoint-file precondition fails and the whole
sequence is serial. The orchestrator has nothing to fan out here; recorded so the
absence reads as a finding rather than an oversight.

All line numbers are against `apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx`
at `c608e968`.

---

## Evidence first — these two run before anything is edited

### T001 — [Evidence] Capture the unmodified baseline

```sh
cd apps/shared && npx vitest run src/ui/composites/CameraViewerCameraSwap.test.tsx
```

Expect **7 passed**. Capture verbatim. This is the "must not break" line.

**Depends on:** nothing.

### T002 — [Evidence, COUNTERFACTUAL] Reproduce CI's red locally, before any edit

This is the artefact that replaces the red this change cannot have. **It must be
produced against the file as it stands, before T003 touches it** — afterwards it
is unobtainable.

```sh
cd apps/shared
sed '242s/i < 10/i < 1/' src/ui/composites/CameraViewerCameraSwap.test.tsx \
  > src/ui/composites/ScratchBudget.test.tsx
npx vitest run src/ui/composites/ScratchBudget.test.tsx
rm src/ui/composites/ScratchBudget.test.tsx
```

Expect **exactly 4 failed | 3 passed**, and the four must be, by name:

- `Never resolves to camera A when every stream read for camera B fails`
- `Never resolves to camera A when the gateway refuses camera B's stream read with 403`
- `Reads "Stream is offline" rather than Connecting when the new camera answers Offline`
- `Reads Viewer error, not Idle, on a first mount whose stream read fails`

— the same four as CI run 34956788262. Capture verbatim, including the assertion
messages.

**`ScratchBudget.test.tsx` is never committed.** It is created, run, and deleted
inside the task. If it reaches the index, that is a review block.

**If the four names do not match**, stop and report: the diagnosis in `spec.md`
does not hold on this machine and the plan's premise needs re-deriving.

**Depends on:** T001.

---

## US1 (P1) — a frontend PR is reddened only by its own change

### T003 — [Foundational] One waiting idiom, and `goLive` on a real condition

Blocks T004–T009.

1. **Add `waitUntil`** near `waitForNewPeerConnection` (`:272`):

   ```ts
   async function waitUntil(condition: () => boolean, description: string, timeoutMs = 4000): Promise<void>
   ```

   Poll on `await act(async () => { await new Promise((resolve) => setTimeout(resolve, 10)); })`,
   throw `Timed out after ${timeoutMs}ms waiting for ${description}.` at the
   deadline (FR-007).

2. **Re-express `waitForNewPeerConnection` (`:272-289`) in terms of it**,
   **preserving its existing timeout message verbatim** — the had/have counts are
   diagnostically better than a generic line and must not be lost. Its docblock
   (`:262-271`) stays; it is the reasoning this whole spec extends.

3. **`FakePeerConnection` (`:43`) gains `remoteDescriptionSet = false`**, set
   `true` in `setRemoteDescription()` (`:66`).

4. **`goLive` (`:293-303`) replaces its `flushConnect()` at `:296`** with
   `await waitUntil(() => pc.remoteDescriptionSet, 'the WHEP answer to be applied')`.

**Done when:** the unmodified suite is still 7/7 green.

**If step 4 makes any test red**, the wait is too early: add the WHEP `POST`
having been issued as a second condition (§3c). **Do not restore the count.**

**Depends on:** T002.

### T004 — Test 2: `…every stream read for camera B fails` (`:409-443`)

- `:422` — replace `await flushConnect()` with
  `await waitUntil(() => screen.queryByText(/could not reach the streaming service/i) !== null, 'the tile to report a failed read for camera B')`.
  The three assertions at `:427-432` then run unchanged.
- `realWait` (`:252-257`) — **drop its trailing `await flushConnect()` at `:256`**;
  the wall-clock elapse stays exactly as it is (FR-005).
- after `:439`'s `await realWait(5200)` — add
  `await waitUntil(() => screen.queryByText('Viewer error') !== null, 'the tile to still report a failed read after the poll')`
  before the three assertions at `:440-442`.

That added wait is not padding: it asserts the property `spec.md` verified in
RTK's `writePendingCacheEntry` — that `error` **survives** a pending refetch, so
the tile does not flash back to `Connecting…` every 5 s. Previously assumed;
now observed.

- `:416`'s `flushConnect()` **stays** — see T009.

**Depends on:** T003.

### T005 — Test 3: `…the gateway refuses with 403` (`:445-461`)

- `:456` — replace `await flushConnect()` with
  `await waitUntil(() => screen.queryByText('Viewer error') !== null, 'the tile to report a failed read for camera B')`.
- **Add** `expect(screen.getByText('Viewer error')).toBeDefined();` before the two
  existing assertions at `:459-460`.

This test currently asserts **only absences**, which are also true before anything
renders — it could pass against a component that renders nothing at all. FR-002's
positive is a strengthening, and it is the reason this conversion is not merely a
delay.

- `:452`'s `flushConnect()` **stays** — see T009.

**Depends on:** T003.

### T006 — Test 4: `Reads "Stream is offline" rather than Connecting` (`:463-495`)

- `:473` — replace `await flushConnect()` with
  `await waitUntil(() => screen.queryByText('Stream is offline') !== null, "camera B's offline state to reach the tile")`.
  Assertions at `:479-495` unchanged.
- `:470`'s `flushConnect()` **stays** — see T009.

**Depends on:** T003.

### T007 — Test 5: the warm-cache premise that can evaporate in silence (`:497-540`)

The higher-value half of this spec. `:507`'s `flushConnect()` is what makes camera
B warm in the RTK Query cache; if it runs out early the cache is **not** warm,
`warm.unmount()` (`:508`) discards an in-flight entry, and the test goes on to
exercise the *cold* ordering while its name and its comment claim the warm one.
It still passes. A guard that silently stops guarding is worse than a red.

- `:507` — replace with a wait on the premise itself:

  ```ts
  await waitUntil(
    () => streamsApi.endpoints.getStream.select(CAM_B)(store.getState()).data !== undefined,
    'camera B to be warm in the RTK Query cache',
  );
  ```

  `streamsApi` (`:38`) and `store` (`:214`) are both already in scope.

- `:520` — replace with
  `await waitUntil(() => screen.queryByText('Stream is offline') !== null, "camera B's warm offline state to land in the same commit as the swap")`.

**Nothing else in this test changes.** Its effect-ordering guard and the docblock
at `useWhepSession.ts:152-176` that it protects are untouched.

**Depends on:** T003.

### T008 — Test 6: `Reads Viewer error, not Idle, on a first mount` (`:542-559`)

- `:553` — replace `await flushConnect()` with
  `await waitUntil(() => screen.queryByText('Viewer error') !== null, 'the first-mount read failure to reach the tile')`.
  Assertions at `:555-558` unchanged.

This is the test that proves the diagnosis: no swap, no effect, no state machine —
the label is a pure render-time derivation of RTK Query's own state
(`CameraViewer.tsx:387`). If any conversion in this spec is obviously right, it is
this one.

**Depends on:** T003.

### T009 — Say why the surviving `flushConnect()` calls survive

Five call sites keep their fixed count. Only `:571` precedes a purely negative
assertion — a fresh `getToken` closure must change **nothing** — for which no
condition wait can be expressed and a bounded drive is the correct instrument.
The other four (`:350`, `:416`, `:452`, `:470`) also precede assertions about
state that DOES have to arrive — camera A's session closed, the "Connecting…"
label — but `flushConnect`'s drive is not what makes those safe: the
`view.rerender(...)` immediately before each of them is itself act-wrapped, so
React flushes the outgoing effect's cleanup and the incoming effect's
synchronous body before `rerender` returns, and that state is already settled
by the time `flushConnect` even runs.

| Site | What follows |
|---|---|
| `:350` | no second POST to camera A; instance count stays 1 — negative |
| `:416` | camera A's session closed; `Connecting…` present — already settled by `rerender`'s act |
| `:452` | as `:416` |
| `:470` | as `:416` |
| `:571` | **the whole test** — a new `getToken` closure must change nothing — negative |

Add **one** comment on `flushConnect`'s docblock (`:226-239`) recording the
division above — and cite #2386. Do not comment all five sites; one statement
of the rule, per CLAUDE.md's no-drive-by-comments rule.

`flushConnect`'s budget of **10** is not load-bearing for any of these five
sites: an empty loop (`i < 0`, zero yields) still leaves all seven tests
passing. Do not record the count as though it matters here — it exists for
`waitForNewPeerConnection`'s own bounded-drive use elsewhere in the file.

**Depends on:** T004–T008.

---

## The gate

### T010 — [SC-001/SC-002/SC-003/SC-004] Run the discriminator

```sh
cd apps/shared
# SC-003
npx vitest run src/ui/composites/CameraViewerCameraSwap.test.tsx          # 7 passed

# SC-001 — reduce EVERY remaining fixed-count loop to a single yield
sed 's/i < 10; i += 1/i < 1; i += 1/' src/ui/composites/CameraViewerCameraSwap.test.tsx \
  > src/ui/composites/ScratchBudget.test.tsx
for n in 1 2 3 4 5; do npx vitest run src/ui/composites/ScratchBudget.test.tsx; done   # 7 passed, five times
rm src/ui/composites/ScratchBudget.test.tsx

# SC-004
cd ../.. && pnpm --filter @smart-sentinel-eye/shared test
pnpm format:check && pnpm lint && pnpm typecheck
```

Check the `sed` actually matched every remaining loop before trusting the run —
if T003–T008 left a loop with a different bound, widen the expression rather than
reporting a pass it did not cover. A harness that silently edits nothing returns
green and proves nothing; that is this issue's own failure mode.

**Quote all of it in the PR**, alongside T001's and T002's captures. The
before-counterfactual and the after-harness are the phase-4 evidence
(ADR-0139 / CLAUDE.md phase 4a).

**`ScratchBudget.test.tsx` must not be in the diff.** Verify with
`git status --porcelain`.

**Depends on:** T009.

### T011 — [Follow-up, not implemented here] File the lint-rule issue

A rule forbidding `flushConnect()` (or any fixed-count settle) immediately before
an assertion is the enforcement ADR-0139 would ask about — and it is exactly what
the lane may not decide on its own (ADR-0144: no ADRs). File an issue describing
it, citing this spec and #2386, noting there is **one** call site today so the
repo's own "extract at the second site" practice argues for waiting. Do not
implement it. Add the issue to Project #13.

**Depends on:** nothing (may be done any time).

### T012 — [Phase 5] Confirmation on CI

After the PR opens, the frontend bucket must be green — and, because one green run
is what already misled here, the **next** frontend PR's bucket must be green too
(SC-005). Write the verification note citing both.

This is confirmation, not the commit gate. The gate was T010.

**Depends on:** T010, the PR.

---

## Dependencies

```
T001 → T002 → T003 → { T004, T005, T006, T007, T008 } → T009 → T010 → T012
T011 (independent)
```

Serial throughout, for the reason in §`[P]` markers above.

---

## Phase 4a

**Colour: BEHAVIOUR-PRESERVING → characterisation, observed GREEN, plus a
counterfactual.**

Declared by the architect, per CLAUDE.md. The reasoning, since the shape is
unusual — the artifact being changed is itself test code:

- **No production file changes** (FR-008), so no behaviour moves and nothing owes
  a red.
- The usual characterisation rule ("the covering tests pass unmodified after") is
  **not available**: the covering test *is* the file being modified. T002's
  counterfactual stands in its place.
- **Ambiguity resolves to red** per CLAUDE.md — and it was resolved, not assumed.
  `spec.md` §*The question this spec had to settle* disproves a component race on
  four independent grounds, including one test with no swap, no effect and no
  state machine in it. **If the engineer finds an assertion that cannot pass at
  budget 1, that conclusion is wrong for that assertion: stop and report.** A
  genuine race makes this behaviour-changing and owes a red.

**What "observed" means here, concretely:**

| Artefact | When | Expected |
|---|---|---|
| Unmodified suite (T001) | before the edit | 7 passed |
| Budget-1 harness on the **unfixed** file (T002) | before the edit | **exactly 4 failed**, the CI-named four |
| Unmodified suite (T010) | after the fix | 7 passed |
| Budget-1 harness on the **fixed** file (T010) | after the fix | 7 passed, **five consecutive runs** |

All four quoted verbatim in the PR body.

## Blocked outcomes — report, do not work around

- Any weakening to reach green: a deleted or loosened assertion, `test.retry`,
  a vitest `retry`, `it.skip`, a raised timeout offered *as the fix* (ADR-0144).
- Any production file in the diff (FR-008 / SC-006).
- An ADR turning out to be needed (it is not — `spec.md` §*Is a new ADR needed?*
  examines three candidates; the lint rule is the one that would, and it is
  deferred to T011 for exactly that reason).
