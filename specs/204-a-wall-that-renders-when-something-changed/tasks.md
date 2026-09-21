# Tasks — Spec 204, a wall that renders when something changed

- **Issue:** #2303 · **Branch:** `2303-dead-skew-state` · **Worktree:** `D:/Github/sse-2303`
- **Base:** `origin/develop` @ `9ff472fe` · **Baseline:** 163 kiosk tests, all green
- **Phase 4a colour: RED.** This is behaviour-changing — a wall that re-rendered
  every cycle stops, and a label held for a stale age starts being held for the
  live one. A test that arrives green is a phase-4 failure (ADR-0139,
  constitution §Testing). There is no characterisation half.

## Parallelism (ADR-0109)

**Almost none, and that is the finding.** Every task touches one of three files
in `apps/kiosk-web/src/features/cell/`, and two of them are the same file.
`[P]` is applied only where files are genuinely disjoint. **T001 is
foundational and blocks everything**: it is the baseline the red observations
are measured against.

```
T001 ──┬── T002 ─┐
       ├── T003 ─┼── T005 ── T006 ── T007 ── T008 ── T009
       └── T004 ─┘
       (T002 [P] T004 — different files)
```

## Phase 4a — tests first (`test-writer`). The engineer may not edit any file below.

| ID | [P] | Story | Task |
|---|---|---|---|
| **T001** | | US1 | Re-establish the baseline in this worktree and record it. `pnpm install --frozen-lockfile`, then `cd apps/kiosk-web && npx vitest run`. Record the exact totals (expected: 163 passed / 12 files). **Do not quote #2303's 141** — that figure is from 2026-09-13. Blocks T002-T004. |
| **T002** | `[P]` | US1 | **SC-1 — the render-count assertion.** New `describe` block in `CellPage.test.tsx`. Two-tile 1×2 layout. The `CameraViewer` double must count its renders **per camera** and capture `onLagMeasured` **per camera** (the existing module-level `reportLag` single is overwritten by the last tile and cannot drive a two-tile wall — extend it to a `Map`, do not replace it). Converge the wall, then run 5 settle cycles feeding lags whose spread **jitters by a fraction of a millisecond** (e.g. `100 + cycle*0.31` / `110 + cycle*0.73`). Assert tile renders across those 5 cycles is **0**. **Must be observed red at 8** — a round-number spread makes React's `Object.is` bail-out hide the defect and the test asserts nothing (spec.md, Correction 1). |
| **T003** | | US1 | **SC-3 + SC-4 + SC-5 — label ageing on its own mechanism.** Same file, same block. (a) SC-3: converged two-tile wall, first settled lag 40 ms; report 180 ms; change the label text **through the RTK snapshot cache** (`useSnapshotFromTheRealCache` + `store.dispatch(systemVariablesApi.util.upsertQueryData(...))`, the #2012 pattern already in this file) — **never** via `onOverlayHighlightChanged`, which re-renders the page and makes the test pass today (plan §3, R1). Assert the label is still the old one after 40 ms and the new one after 180 ms. (b) SC-4: second report of 105 against a first of 100 — inside the ±33 ms deadband, so nothing else re-renders — then a text change; assert the hold uses 105. (c) SC-5: 1×1 layout, 150 ms lag, text change, hold is 150 ms. **All three must be observed red.** Sequential after T002 — same file. |
| **T004** | `[P]` | US1 | **SC-7 + §5 re-anchoring.** In `useWallAlignment.test.ts`: confirm *"Reports the induced skew, naming the tile that set it"* still covers SC-7, and re-anchor the four `skewMilliseconds` assertions (`:51`, `:111`, `:257`, `:271`) onto the `reportKioskLatency` spy per plan §5's table. **Re-anchor, never delete** — deleting a test to reach green is a blocked outcome (ADR-0144). These four are expected **red** after the engineer removes the member and green once re-anchored; write them against the post-fix interface. Disjoint file from T002/T003 → `[P]`. |

**Gate 4a:** T002, T003 and T004's new assertions run and **fail**, and the
verbatim vitest output — file, test name, expected/received — is returned to
the orchestrator and quoted in the PR body. A green test here is a failure of
this phase.

## Phase 4b — implementation (`frontend-engineer`)

The engineer receives T001-T004's verbatim output as its brief and **may not
edit any test file**.

| ID | [P] | Story | Task |
|---|---|---|---|
| **T005** | | US1 | `apps/kiosk-web/src/features/cell/useWallAlignment.ts` — remove the dead state: the `useState` at `:103`, the write at `:181`, the `skewMilliseconds` member at `:70` (and its doc comment), the publish at `:252`. **Keep `const skew = skewAcross(heldLags)` at `:180`** — it feeds the `wall_skew` report at `:206`. Keep the comment at `:172-178` (it explains the deadband, not the skew). Depends on T001-T004. |
| **T006** | | US1 | `apps/kiosk-web/src/features/cell/CellPage.tsx` — change `TileProps.frameAgeMilliseconds: number \| null` to `frameAgeFor: (tileKey: string) => number \| null` plus `tileKey: string`; at `:369` pass `frameAgeFor={alignment.frameAgeFor}` and `tileKey={cell.key}` instead of evaluating the getter; inside `Tile`, `const frameAgeMilliseconds = frameAgeFor(tileKey);` before the `useLabelDelay` call at `:498`. `useLabelDelay`'s call site is otherwise unchanged. Carry over the doc comment on the prop, amended to say the tile reads its own age at the moment it uses it. Same file as T003's edits → sequential, not `[P]`. |
| **T007** | | US1 | Green the suite: `cd apps/kiosk-web && npx vitest run` — 163 pre-existing + the new tests, all passing. Then `pnpm --filter @smart-sentinel-eye/kiosk-web typecheck`, `... lint`, and `pnpm format:check` at the root. **No new suppression, no lowered threshold, no deleted test** (ADR-0144). If `react-hooks/refs` fires inside `Tile` (R4), the suppression carries the reasoning already written at `CellPage.tsx:51-53` — it is not a bare disable. |
| **T008** | | US1 | Prove SC-F: `grep -rn 'skewMilliseconds' apps/` returns nothing. Quote the empty result. |

**Gate 4b:** every test green, quality commands clean, SC-F proven. Commits
follow ADR-0030 (Conventional Commits, no `Co-Authored-By` — ADR-0086), each
building on its own (ADR-0087 rebase-merge lands them individually).

## Phase 5 — verification (`/verify`)

| ID | Story | Task |
|---|---|---|
| **T009** | US1 | Observe it, don't only assert it. Boot the Aspire stack, open a **published two-tile layout** on the kiosk, and record ~20 s of a settled wall in the React DevTools Profiler. **Before:** a commit roughly every 2 s naming every tile. **After:** no commits between genuine events. Then change a system variable and confirm the label still appears and is still held. Cite constitution §IV's `Overlay composite + render ≤ 50 ms` leg and ADR-0123's cadence floor. **Write the figures down in `verification.md` as observed** — a measurement reported only to the orchestrator is invisible to every later grep. |

**The one thing T009 may not do:** claim a measured millisecond of latency
improvement. spec.md "Latency budget" forbids it in advance — a two-tile
developer wall cannot resolve one reconciliation pass per 2 s out of the noise,
and the first run after machine churn looks like a regression anyway. The
render count is the evidence; the cadence argument is ADR-0123's, not a new
measurement.

## Phase 6 — QA

| ID | Story | Task |
|---|---|---|
| **T010** | US1 | `/code-review` (frontend-reviewer). Specific checks, beyond the general pass: (a) plan §5's re-anchoring table — four assertions moved, none dropped; (b) the `wall_skew` report at `:206` still fires with the laggiest held tile's camera; (c) no `React.memo` crept in (R2); (d) T002's spread genuinely jitters (R1/Correction 1); (e) T003 drives its text change through the RTK cache, not a highlight (R1). |
| **T011** | US1 | `/security-review` **skipped** — no trust boundary, no endpoint, no scope, no token handling changed. Record `Phase 6 security: skipped — no trust boundary touched` in the PR body. |

## Phase 7 — PR

| ID | Story | Task |
|---|---|---|
| **T012** | US1 | Re-check R6 (no branch conflict on `apps/kiosk-web/src/features/cell/`), rebase on `origin/develop`, `gh pr create --base develop`. Body must carry: the §IV leg cited (`Overlay composite + render`), the **verbatim red output** from gate 4a, the before/after render counts, `Closes #2303`, and the spec.md assumption 1 deviation (the issue's literal "second lag report reaches `useLabelDelay`" test is not implementable against the fix; SC-3/SC-4 assert the stronger property instead) flagged for the reviewer. Park the PR, rebase after every merge (ADR-0144). |

## Gates and flags for the orchestrator

- **Phase 4a is RED.** Declared here, per ADR-0144's "ambiguity resolves to
  red". A green new test is a blocked outcome, not a shortcut.
- **T002 → T003 → T006 are sequential on `CellPage.tsx` / `CellPage.test.tsx`.**
  Only T002∥T004 may fan out. Do not dispatch a second frontend agent onto this
  directory.
- **No ADR, no constitution edit** — none needed (plan §7), and the lane may
  not write one.
- **Issue #2303 verified 2026-09-21:** OPEN, labels `bug` + `agent:ready`, no
  `agent:blocked`, Project #13 status **Todo**. The feature-level issue is
  already on the board, so the phase-3 gate is met without `item-add` and
  without `/speckit-taskstoissues`.
