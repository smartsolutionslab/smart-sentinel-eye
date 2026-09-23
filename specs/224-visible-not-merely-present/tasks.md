# Tasks 224 — Visible, not merely present

**Spec**: `spec.md` · **Plan**: `plan.md` · **Issue**: #2304 · **Phase**: 3 (Tasks)
**Date**: 2026-09-23 · **Branch**: `2304-visible-not-merely-present`
**Baseline**: `70b52c96` (`origin/develop`)
**Phase 4a colour**: **characterisation — observed GREEN** (constitution §Testing,
second obligation; ADR-0144). Declared here, at phase 3, as ADR-0144 requires.
**Agents**: phase 4 `test-writer` → `frontend-engineer`; phase 6 `frontend-reviewer`.

---

## Reading order for whoever picks this up

1. `spec.md` §1 — **four of the issue's claims are stale**; the issue's numbers
   are not the numbers.
2. `spec.md` §1.2 — the table of **32 in / 6 out**. It is the authority.
3. `plan.md` §6 R6 — **the obvious way to do this is wrong**. Do not sed.
4. `spec.md` §6.1 — the four rules the characterisation colour imposes.

---

## Task list

Format: `[ID] [P?] [Story] description`. Only P1 exists.

### Phase 4a — capture the characterisation (agent: `test-writer`)

- **[T001]** [P1] **Capture the baseline, before touching anything.** On
  `70b52c96` with a clean tree, run
  `pnpm --filter @smart-sentinel-eye/shared test` and record the **verbatim**
  output — per-file and total pass counts. This is the "observed green" the
  characterisation colour requires and it must be quoted in the PR body.
  **No source edit in this task.**
  *Depends on*: `pnpm install` at the workspace root.
  *Done when*: the output is captured and the total is a number, not "all green".

- **[T002]** [P1] **Re-verify the 32/6 split against the tree you actually have.**
  Run the two greps below and confirm they match `spec.md` §1.2 exactly. If they
  do not, the suite moved again — **update `spec.md` §1.2 first**, then continue.
  ```sh
  grep -rnE "expect\((screen\.|.*(getBy|getAllBy|queryBy|queryAllBy|findBy|findAllBy)).*\)\.(toBeDefined|toBeTruthy)\(\)" --include=*.tsx apps/shared/src   # expect 31
  grep -rcE "\.(toBeDefined|toBeTruthy)\(\)" --include=*.tsx --include=*.ts apps/shared/src   # expect 38 total
  ```
  The 32nd site is **multi-line** (`CameraViewerMedia.test.tsx:385`, a
  three-line `expect(subject, "message").toBeDefined()`) and the first grep will
  not find it. That is why this task exists rather than a single count.
  *Depends on*: T001.

### Phase 4b — the change (agent: `frontend-engineer`)

- **[T003]** [P1] **Add the dependency.** `apps/shared/package.json` →
  `devDependencies` gains `"@testing-library/jest-dom": "7.0.1"` in its
  alphabetical position beside `@testing-library/react`. Exact version, no range.
  Run `pnpm install` from the workspace root; commit the `pnpm-lock.yaml` diff.
  *Depends on*: T002.
  *Done when*: `apps/shared/node_modules/@testing-library/jest-dom` exists and the
  lockfile resolves 7.0.1.

- **[T004]** [P1] **Create the setup file** at `apps/shared/src/test/setup.ts`,
  containing exactly:
  ```ts
  import '@testing-library/jest-dom/vitest';
  ```
  **Do not** copy `management-web`'s `configure({ asyncUtilTimeout })` — see
  `plan.md` §2b. **Do not** place it outside `src/`: `rootDir` is `src` and the
  type augmentation would be excluded (`plan.md` §2b, spec §AS-6).
  *Depends on*: T003.

- **[T005]** [P1] **Wire it.** `apps/shared/vitest.config.ts` gains
  `setupFiles: ['./src/test/setup.ts'],` inside `test:`, between `environment` and
  `include`. `environment: 'node'` **stays**; the 19 per-file
  `// @vitest-environment jsdom` pragmas **stay**. No `projects` split.
  *Depends on*: T004.

- **[T006]** [P1] **Prove the wiring before converting anything.** Run the full
  `apps/shared` suite unchanged. It must be **identical to T001's capture** — 38
  weak assertions still in place, all green, and the 14 node-environment files
  untouched by the new setup import (spec §AS-3). Then run
  `pnpm --filter @smart-sentinel-eye/shared typecheck`.
  **This is the gate that separates a wiring failure from a conversion failure.**
  Converting first would confuse the two.
  *Depends on*: T005.
  *Done when*: suite identical to T001; typecheck clean.

- **[T007]** [P1] **Convert the 32, site by site**, against `spec.md` §1.2's
  table. `.toBeDefined()` → `.toBeVisible()` (23 sites) and `.toBeTruthy()` →
  `.toBeVisible()` (9 sites). **Nothing else on any line moves** — not the query,
  not its argument, not the custom message at `CameraViewerMedia.test.tsx:385`,
  not the surrounding `await`/`act`.
  **Do not run a repo-wide sed** (`plan.md` §6 R6): it hits all 38 and four of the
  six out-of-scope sites throw `received value must be an HTMLElement`.
  Seven files: `CameraViewer.test.tsx` (7), `CameraViewerCameraSwap.test.tsx`
  (10), `CameraViewerMedia.test.tsx` (4), `ErrorBoundary.test.tsx` (5),
  `FrameCapture.test.tsx` (2), `OverlayGeometryFields.test.tsx` (2),
  `ConfirmDialog.test.tsx` (2).
  *Depends on*: T006.

- **[T008]** [P1] **Verify the edit's extent.**
  `grep -rcE "\.(toBeDefined|toBeTruthy)\(\)" --include=*.tsx --include=*.ts apps/shared/src`
  → **exactly 6**, and they are precisely spec §1.2's out-of-scope table:
  `OverlayEditorKeyboard.test.tsx:113,567,583`,
  `OverlayGeometryFields.test.tsx:489`, `api/cameras.api.test.ts:373`,
  `realtime/layoutHub.test.ts:251`. Any other number, or any other file, means
  the edit over- or under-reached.
  Also confirm `git diff --name-only` lists **no non-test `.tsx`** (invariant I1).
  *Depends on*: T007.

- **[T009]** [P1] **Re-run green and compare to T001.** Suite, typecheck, lint
  (`--max-warnings 0`). Per-file pass counts **identical** to T001's capture.
  **If any of the 32 is red: stop.** `spec.md` §6.1 rule 4 — an assertion that
  has to be edited to pass is evidence the behaviour moved. Do not soften the
  matcher, do not add a `waitFor`, do not change the query. Report it; it is its
  own issue.
  *Depends on*: T008.

- **[T010]** [P1] **Workspace regression.** `pnpm -r --filter "./apps/**" test`,
  `pnpm typecheck`, `pnpm lint` at the root. `kiosk-web` and `management-web` are
  untouched and must stay green.
  *Depends on*: T009.

### Phase 5 — verify (agent: whoever runs `/verify`)

- **[T011]** [P1] **Run the counterfactual (spec §AS-2) — this is the only thing
  that proves the slice did anything.** Temporarily add
  `style={{ display: 'none' }}` to `ViewerOverlay`'s wrapper `<div>` in
  `apps/shared/src/ui/composites/CameraViewer.tsx` (**line 418** at `70b52c96`,
  the `absolute inset-0 flex flex-col …` wrapper), re-run
  `CameraViewer.test.tsx`, observe at least one converted §IV stream-state
  assertion fail with jest-dom's *"element is not visible"* message, **quote the
  failure verbatim**, and **revert the edit**.
  Without T011 the PR shows only that 32 lines were retyped — see `plan.md` §7.
  *Depends on*: T010.
  *Done when*: the failure text is in `verification.md` and the tree is clean.

- **[T012]** [P1] **Write `verification.md`.** Record: T001's baseline capture,
  T009's post-change capture, T008's grep result, T011's quoted failure, and
  **the three things this slice does not prove** (spec §10: Tailwind classes,
  `aria-hidden`, occlusion). §IV: no leg's timing touched — state it, do not omit
  it.
  *Depends on*: T011.

### Phase 6 — review (agent: `frontend-reviewer`)

- **[T013]** [P1] **Review against invariants I1–I6** (`plan.md` §3), not against
  a summary. Specifically: read `git diff -U0` and confirm every test-file hunk is
  a one-token matcher swap, and that the six out-of-scope sites are untouched.
  *Depends on*: T012.

### Phase 7 — PR

- **[T014]** [P1] **Open the PR against `develop`** (`--base develop`, ADR-0028).
  Body carries T001's green capture, T009's green capture and T011's quoted
  failure; references #2304 with a closing keyword; states **Phase 4a:
  characterisation, observed green** and why; states the four stale issue claims
  (spec §1.2–1.5) so the issue's numbers are not re-quoted downstream; states
  §10's limits. Park it — do not wait for CI (ADR-0144).
  *Depends on*: T013.

---

## Dependencies

Strictly linear: **T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009
→ T010 → T011 → T012 → T013 → T014.**

Two orderings are load-bearing rather than incidental:

- **T006 before T007.** Prove the *wiring* green while the weak assertions are
  still in place. If the suite is only run after both the wiring and the
  conversion land, a red result has two candidate causes and the characterisation
  colour cannot tell them apart.
- **T008 before T009.** Check the edit's *extent* before its *result*. A green
  suite with 38 sites still weak is indistinguishable from a green suite with 32
  converted, unless the count is checked separately.

## Parallelism (ADR-0109)

**No `[P]` markers. Zero parallel tasks, deliberately.**

ADR-0109 marks tasks `[P]` when they own disjoint files. Here the four
configuration artefacts form a chain — `package.json` → `pnpm install` →
`vitest.config.ts` → `setup.ts` — and every one of them must be in place before a
single assertion may be converted, because `.toBeVisible()` does not exist until
they are.

The seven test-file edits in T007 *are* file-disjoint and could in principle fan
out. They are not, on purpose: each is a two-token change, the whole of T007 is
under ten minutes, and splitting it across agents would put the §1.2 table — the
one artefact that keeps the edit from over-reaching (`plan.md` §6 R6) — into seven
hands instead of one.

**There is no foundational task blocking a fan-out here**, because there is
nothing to fan out to. The orchestrator should dispatch this as a single
`frontend-engineer` pass and spend the parallelism budget on another issue.

## Files touched

| File | Task | Kind |
|---|---|---|
| `apps/shared/package.json` | T003 | +1 devDependency |
| `pnpm-lock.yaml` | T003 | generated |
| `apps/shared/vitest.config.ts` | T005 | +1 key |
| `apps/shared/src/test/setup.ts` | T004 | **new**, 1 line |
| `apps/shared/src/ui/composites/CameraViewer.test.tsx` | T007 | 7 matchers |
| `apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx` | T007 | 10 matchers |
| `apps/shared/src/ui/composites/CameraViewerMedia.test.tsx` | T007 | 4 matchers |
| `apps/shared/src/ui/composites/ErrorBoundary.test.tsx` | T007 | 5 matchers |
| `apps/shared/src/ui/composites/FrameCapture.test.tsx` | T007 | 2 matchers |
| `apps/shared/src/ui/composites/OverlayGeometryFields.test.tsx` | T007 | 2 matchers |
| `apps/shared/src/ui/primitives/ConfirmDialog.test.tsx` | T007 | 2 matchers |
| `specs/224-visible-not-merely-present/verification.md` | T012 | **new** |

**No production source file appears in this table.** That is the whole basis of
the characterisation colour; if one shows up in the diff, phase 6 blocks.

## Out of scope (do not let these ride along)

| Not this slice | Where it goes |
|---|---|
| The **50** `queryBy…` + `.toBeNull()` sites across 13 files | A separate issue. A different transform with a different justification — spec §7. |
| An ESLint guard so `.toBeDefined()` cannot return | **Recommended follow-up issue**, spec §12. It is new behaviour (a rule that fails the build) and would collide with this slice's characterisation colour. |
| Making `environment: 'jsdom'` global and deleting the 19 pragmas | Behaviour change to 14 files. Not this issue. |
| Copying `management-web`'s `asyncUtilTimeout` config | ADR-0150's business, on its own evidence. |

## Gate (phase 3, ADR-0037)

Tasks are atomic. **Issue #2304 is on Project #13** — verified 2026-09-23:
`gh issue view 2304 --json projectItems` returns
`[{"status":{"name":"Todo"},"title":"Smart Sentinel Eye"}]`, with labels
`tech-debt` and `agent:ready` and no `agent:blocked`. **No per-task issues** —
feature-level only, per the repo's practice since spec 028. `/speckit-taskstoissues`
is deliberately not run.
