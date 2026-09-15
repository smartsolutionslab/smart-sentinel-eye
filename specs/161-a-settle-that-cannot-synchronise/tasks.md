# Tasks — Spec 161, a settle that cannot synchronise

**Phase:** 3 (Tasks) — ADR-0037
**Issue:** [#2392](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2392) · **Branch:** `feat/2392-a-settle-that-cannot-synchronise`
**Spec:** `spec.md` · **Plan:** `plan.md`
**Engineer:** `frontend-engineer` · **Reviewer:** `frontend-reviewer`
**Phase 4a colour:** **BEHAVIOUR-CHANGING (RED first)** for T006–T008;
**BEHAVIOUR-PRESERVING (characterisation, GREEN)** for T003–T005. See `plan.md` §6.
**Latency (§IV): N/A** — no production file changes, no leg moves, no measurement owed.
**Delivery:** one PR → `develop`. **Not stacked on #2395** (`spec.md` §0).
**New ADR required:** **no** — ADR-0150 decides this. One scope question is
escalated rather than decided; see the gate note below.

---

## GATE NOTE — read before T006

`spec.md` §6 carries a `[DECISION REQUIRED]`: ship **Selector A only** (literal
ADR-0150 §2, a reserved-name guard with no current population) or **A+B** (adding
the shape selector, which has teeth and is what §3's measurement validates).

**These tasks are written for A+B.** If the human answers **(A)**, delete T007,
drop the `must-flag-shape` cases from T006's fixture, and remove the
`TIMER_SETTLE_LOOP` selector from T008. Nothing else changes.

**The lane must not answer this itself** (ADR-0144).

---

## `[P]` markers — ADR-0109

Three tasks own disjoint files and can genuinely run in parallel. The rest are
serial because they share a file or a dependency.

**T005 is the foundational task and blocks T008.** The rule cannot be registered
over a tree that violates it 26 times, so both cleanups must land first. That is
the fan-out point the orchestrator should plan around: T003/T004/T005 are three
independent file-sets, and T006/T007 (the guard and fixtures) are independent of
all of them and can start immediately.

---

## Evidence first — before anything is edited

### T001 — [Evidence] Capture the unmodified baseline

```sh
cd apps/shared
npx vitest run src/ui/composites/CameraViewerCameraSwap.test.tsx
npx vitest run src/ui/composites/CameraViewer.test.tsx \
               src/ui/composites/CameraViewerMedia.test.tsx \
               src/ui/composites/FrameCapture.test.tsx \
               src/ui/composites/OverlayEditorBackdrop.test.tsx
cd ../management-web && npx vitest run src/features/overlays/OverlayEditorDialog.test.tsx
```

Expect `7 passed (7)`, `32 passed (32)`, and a count for the last — capture it.
This is the "must not break" line. Capture **verbatim**.

**Depends on:** nothing.

---

### T002 — [Evidence, COUNTERFACTUAL] Prove the characterisation can fail

Green is not evidence unless red is reachable. Two probes, both reverted.

**(a) The macrotask settle is inert.** In `CameraViewerCameraSwap.test.tsx`
replace `flushConnect`'s whole body with nothing —
`async function flushConnect() {}` — and run the suite **twice**.

Expect **`7 passed (7)` both times.** This is the discharge of ADR-0150's
Implementation-Notes instruction, and the licence for T003.

**(b) The microtask drains are NOT inert.** Blank the body of `flushConnect` in
`CameraViewer.test.tsx`, `CameraViewerMedia.test.tsx`, `FrameCapture.test.tsx`
and `OverlayEditorBackdrop.test.tsx`, and run all four.

Expect **`5 failed | 27 passed (32)`**, the five being:

- `Retries rejected connections with exponential backoff capped at fifteen seconds`
- `Suspends retries while stream health is Offline and reconnects on recovery`
- `Aborts the session and releases it on unmount`
- `Closes the session and issues the WHEP DELETE once a frame is captured`
- `Closes the session when the editor unmounts mid-capture`

**`git checkout --` both probes before proceeding. If anything from either probe
reaches the index, that is a review block.**

**If (a) is red**, stop and report: the file is not inert on this machine and
T003 must become a conversion to `waitUntil`, not a deletion.
**If (b) is green**, stop and report: the probe measures nothing, so (a) proves
nothing either, and the whole premise needs re-deriving.

**Depends on:** T001.

---

## Cleanup — the tree must satisfy the rule before the rule exists

### T003 — [P] [US-1] Delete the inert macrotask settle

**File:** `apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx` — **only**.

- Delete `flushConnect` (`:258-268`) and its docblock (`:236-257`).
- Delete the six call sites: `:284` (inside `realWait`), `:400`, `:466`, `:515`,
  `:541`, `:678`.
- Rewrite `realWait`'s docblock — it currently explains why it *ends with*
  `flushConnect`, and that sentence becomes false.

**Do not** convert these to `waitUntil`. T002(a) measured them as contributing
nothing; adding a wait where none is needed reintroduces the "condition already
true on entry" defect that ADR-0150 §3 names and spec 159's review caught.

**Done when:** `npx vitest run src/ui/composites/CameraViewerCameraSwap.test.tsx`
→ **7 passed (7)**, with **no assertion edited**.

**Depends on:** T002. **Disjoint from:** T004, T005, T006, T007.

### T004 — [P] [US-1] Rename the sound drains in `apps/shared`

**Files (4):** `CameraViewer.test.tsx`, `CameraViewerMedia.test.tsx`,
`FrameCapture.test.tsx`, `OverlayEditorBackdrop.test.tsx` — all under
`apps/shared/src/ui/composites/`.

`flushConnect` → **`flushMicrotasks`** (helper + all 30 call sites: 12 / 6 / 10 / 2).
**Body unchanged.** The name already exists for this exact body at
`apps/shared/src/streaming/WhepClient.test.ts:180` — reuse, not a new convention.

Add one line above each helper saying why it is sound:

> N microtask rounds bound an N-deep microtask chain — no wall-clock dependence,
> so this is a bound and not an assumption (ADR-0150). Not a substitute for
> `waitFor` when the work crosses into the timer phase.

**Done when:** those four suites → **32 passed (32)**, **unmodified assertions**,
and `grep -rn "flushConnect" apps/shared/src/ui/composites/` returns nothing.

**Depends on:** T002. **Disjoint from:** T003, T005, T006, T007.

### T005 — [P] [US-1] Rename the sound drain in `apps/management-web`

**File:** `apps/management-web/src/features/overlays/OverlayEditorDialog.test.tsx` — **only**.

Same rename, same added comment. Helper `:524`, 4 call sites (`:626`, `:630`,
`:654`, `:664`). Note this helper is declared **inside** the `describe`, not at
module scope — keep it there.

**Done when:** that suite matches T001's captured count, unmodified.

**Depends on:** T002. **Disjoint from:** T003, T004, T006, T007.

---

## Phase 4a RED — written by `test-writer`, before T008

### T006 — [P] [US-1] The fixtures

**New files:**
`scripts/fixtures/settle-rule/must-flag.fixture.txt`,
`scripts/fixtures/settle-rule/must-not-flag.fixture.txt`.

`.txt` deliberately (`plan.md` §4d): a `.tsx` fixture under `apps/*/src/` would be
linted by the very rule it tests and would fail the build it is proving.

**`must-flag`** — contents per `plan.md` §5, each case on a recorded line:
the counted timer-yield loop; `await flushConnect();` + `expect(…)`;
`await flushConnect();` + **comment** + `expect(…)`; `await flushConnect();` +
`expect(x).not.toBe(y)`.

**`must-not-flag`** — `waitUntil`'s `while`-loop deadline poll; un-looped
`realWait`; `await flushMicrotasks();` + `expect(…)`; `await flushMicrotasks();`
+ a non-assertion; `const pc = await goLive();` + `expect(pc.closed)…`; a counted
`fireEvent.keyDown` loop with no `await`; a bare un-looped
`await new Promise((r) => setTimeout(r, 0));`.

**Depends on:** nothing. **Disjoint from:** T003, T004, T005.

### T007 — [US-1] The guard

**New file:** `scripts/settle-rule.test.mjs`. Mirror `scripts/lint-scope.test.mjs`
— same imports, same `new ESLint({ cwd })` + `calculateConfigForFile` shape. Do
not invent a second harness.

Three tests (`plan.md` §5):

1. **flags the defect** — lint `must-flag` through the **real** app config (write
   the fixture to a temp path matching `src/**/*.test.tsx`, lint, clean up).
   Assert the **exact count** and the **line numbers**, and that each message
   names a sanctioned idiom. *An exact count is what stops `selector: "*"` from
   passing.*
2. **does not flag the sound or the sanctioned** — lint `must-not-flag`; assert
   **exactly zero** problems. *This is the discrimination proof.*
3. **registered at `error` in all three apps** — `calculateConfigForFile` on a
   real `*.test.tsx` in each of `apps/shared`, `apps/management-web`,
   `apps/kiosk-web`; assert severity `2`. *Ask ESLint; do not grep the config.*

**Run `pnpm test:guards` and return the output VERBATIM.** Expected red: test 1
reports **0** problems where it asserts N; test 3 finds `no-restricted-syntax`
**undefined**. **Test 2 will be green** — a non-existent rule flags nothing — and
that is expected, not evidence. Say so in the returned output and in the PR body.

**The engineer may not edit this file or the fixtures to reach green** (ADR-0144).

**Depends on:** T006.

---

## Phase 4b — the rule

### T008 — [US-1] Register the rule in all three app configs

**Files (3):** `apps/shared/eslint.config.js`,
`apps/management-web/eslint.config.js`, `apps/kiosk-web/eslint.config.js`.

Append the block from `plan.md` §3.1 — `files: ['src/**/*.test.{ts,tsx}']`,
`no-restricted-syntax` at **`error`**, both selectors, **and the
WHAT-THIS-RULE-CANNOT-SEE comment**. The comment is a deliverable, not decoration:
ADR-0150 §3 makes recording the limits an explicit obligation, on the record of
§II drifting twice and §IV recording a built leg as unbuilt (US-2).

**Done when:** `pnpm test:guards` → all three tests green, T007's red now passing,
**with T006/T007 unmodified**.

**Depends on:** T003, T004, T005 (the tree must be clean first), **and** T007
(the red must be observed first).

---

## Verification

### T009 — [US-1, US-2] Full gate

```sh
pnpm format:check && pnpm lint && pnpm typecheck && pnpm test
```

All four green. `pnpm test` includes `pnpm test:guards`.

**Depends on:** T008.

### T010 — [US-1] The independent end-to-end procedure

Run `spec.md` §9 start to finish, as a reviewer with no knowledge of the
implementation would. The load-bearing steps are **2** (the rule flags the real
historical file, at the four lines that actually failed CI run 34956788262) and
**3** (it does *not* flag the sound suites).

Capture step 2's ESLint output verbatim for the PR body: it is the only evidence
a later reader can check that this rule would have caught the bug that motivated
it.

Confirm `apps/shared/src/ui/composites/ScratchPre159.test.tsx` is **deleted** and
never reached the index.

**Depends on:** T009.

---

## Dependency graph

```
T001 ──> T002 ──┬──> T003 [P] ──┐
                ├──> T004 [P] ──┤
                └──> T005 [P] ──┤
                                ├──> T008 ──> T009 ──> T010
T006 [P] ──> T007 ──────────────┘
```

Fan-out: **T006 immediately** (independent of everything), then **T003, T004,
T005 together** after T002. T008 is the join.

---

## Definition of done

- `spec.md` SC-001 … SC-008 all satisfied.
- The A-vs-A+B gate answered by a human, and the answer recorded in the PR body.
- Phase 4a red quoted verbatim in the PR body, with the note that test 2's green
  in that run is expected and proves nothing on its own (ADR-0139).
- T002's two counterfactuals quoted — both the inert result and the 5-failed
  result. The second is what makes the first mean anything.
- No file outside `plan.md` §4 changed. **No `ci.yml`, no production source, no
  ADR, no constitution edit.**
- Commits: Conventional Commits (ADR-0030), **no `Co-Authored-By`** (ADR-0086),
  each building on its own (ADR-0087).
- The feature issue #2392 on Project #13:
  `gh project item-add 13 --owner smartsolutionslab --url <issue-url>`
  (verify with `--limit 2000`; `item-list` defaults to 30).
