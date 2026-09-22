# Tasks 216 — The deadline a loaded runner outran

**Spec:** [`spec.md`](./spec.md) · **Plan:** [`plan.md`](./plan.md)
**Issue:** [#2520](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2520) · **Same defect, closed by the same PR:** [#2419](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2419)
**Phase:** 3 — ready for a **frontend-engineer**. Frontend-only: no Aspire stack, no Docker, no backend build.
**Phase 4a colour:** **RED** for T005's guard · **CHARACTERISATION (green)** for everything else — §"The two colours"

---

## What the gate settled (2026-09-22), so no task re-opens it

1. **#2520 and #2419 are one defect** (same file, line, element; 5 + 2 = 7
   occurrences). The PR closes both. #2419's *"a tighter `waitFor` timeout is not
   a fix"* was a starting hypothesis — its own text says *"that is where to
   start, not a conclusion"* — written before the mechanism was measured. Spec
   §"The objection in #2419" sets out the comparison; **do not re-litigate it,
   and do not drop it from the PR body either**, because the next reader needs
   the reasoning and not just the verdict.
2. **`apps/shared` is out of scope.** No flake has ever been reported there; a
   different test-setup shape is not on its own a reason to change it
   (ADR-0036). There is no task for it below.
3. **US2 (`apps/kiosk-web`) is optional and defaults to OFF**, by the same
   criterion applied consistently — it has no reported flake either. **Do not
   start T010 unless the reviewer explicitly asks for it.**

US1 alone closes both issues.

---

## Parallelism, stated once

**`[P]` is the disjoint-files rule (ADR-0109), and here it marks almost
nothing.** Every US1 task touches one of three files in one package, in an order
fixed by a red gate. Marking them `[P]` would tell the orchestrator to fan out
work that cannot be fanned out.

The genuine parallelism is **T001 ∥ T002** — two independent measurements, no
writes.

**Do not fan out US1.** It is three small file edits gated on one red
observation; a second agent costs more in coordination than the whole change is
worth.

---

## Dependency order

```
T001 [P] ─┐
T002 [P] ─┴─> T003 ─> T004 ─> T005 (RED gate) ─> T006 ─> T007 ─> T008 ─> T009 ─┬─> T012 ─> T013
                                                                               │
                                              (only if the reviewer asks)      │
                                              T010 ─> T011 ──────────────────-─┘
```

**T005 is a hard gate.** Its verbatim red output is the artefact phase 4b
consumes and the PR body quotes (ADR-0139, ADR-0144). Do not start T006 without
it.

---

## The two colours

| Task | Colour | Why |
|---|---|---|
| **T005** | **RED** | Asserts behaviour nothing asserts today — that the advisory survives a late refusal. Must be seen failing against the unchanged tree. |
| T002, T009, T011 | **CHARACTERISATION, green** | Existing suites captured passing before, passing **unmodified** after. An assertion that has to be edited is evidence the change moved behaviour — **block, do not adjust**. |
| T001, T003, T004, T006–T008, T010, T012, T013 | neither | Measurements, edits and bookkeeping. |

**Why "make the flaky test pass" is not the red task:** there is no failing test
to fix — it passes locally, always. Treating the flake itself as the red
observation would mean waiting for CI to fail on its own schedule. That is the
whole reason T001's injection harness exists.

---

## US1 (P1) — the frontend bucket stops failing for reasons that are not the diff

### T001 [P] [US1] — reproduce the defect deterministically, before changing anything

**Files:** none written. Work in a scratch copy or revert afterwards.

In `apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx`,
temporarily replace the failing test's mock with

```tsx
fetchMock = vi.fn(async () => {
  await new Promise((r) => setTimeout(r, 1500));
  return failedResolveResponse();
});
```

and run `npx vitest run src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx`.

**Done when:** the run fails with
`TestingLibraryElementError: Unable to find an element by: [data-testid="placeholder-preview-error"]`
at line 239, and the verbatim output is saved. **Revert the edit.**

**If it passes:** the premise is wrong — stop, comment on #2520 with the observed
output, and hand back to phase 1. Do not fix a defect the tree does not have.
(Spec 214's premise check found an issue's claim stale; ~11 of ~20 board issues
were stale by delivery. MEMORY: *verify the issue premise before planning*.)

### T002 [P] [US1] — capture the characterisation baseline

**Files:** none written.

```sh
pnpm -r --filter "./apps/**" test
```

**Done when:** the exact pass count **and** wall-clock for each package —
`management-web`, `kiosk-web`, `shared` — is written down as an *observed* figure
in the phase-5 note, not reported only to the orchestrator (MEMORY:
*self-review catches contradictions, never omissions*).

At HEAD `management-web` is **334 tests / 38 files**. The other two have not been
measured by phase 1 and **must not be quoted from this document** — measure them.
(All three are captured even though only one package changes: the suite-runtime
delta is the signal for spec A-3, and a baseline you did not take is a
comparison you cannot make.)

**If any package is red before the change:** stop. That is a pre-existing break,
not this work (MEMORY: *`typecheck:e2e` fails on a clean `develop`*).

### T003 [US1] — read the three files being changed

**Files read:**
`apps/management-web/src/test/setup.ts` (one line today),
`apps/management-web/vite.config.ts`,
`apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx` **in full**,
`apps/management-web/eslint.config.js`'s `no-restricted-syntax` block.

**Done when:** you can state (a) which two tests in the file already use
`vi.useFakeTimers()` and why neither calls `waitFor` inside the fake-timer block,
and (b) why a single non-looping `await new Promise((r) => setTimeout(r, N))` is
outside both shipped ADR-0150 selectors.

*Read before write; mirror the file's existing patterns rather than inventing one
(CLAUDE.md).*

### T004 [US1] — add the deadline and the comment

**File:** `apps/management-web/src/test/setup.ts` — add
`import { configure } from '@testing-library/react';` and
`configure({ asyncUtilTimeout: 10_000 });` with the comment plan §1 specifies.
**Both halves of that comment:** the ADR-0150 reasoning **and** the fake-timer
landmine (FR-004).

**File:** `apps/management-web/vite.config.ts` — add `testTimeout: 30_000` to the
`test` block with plan §2's comment (FR-002).

**Done when:** both files are edited and `npx vitest run` passes for the package
with the **same count** as T002. The guard does not exist yet; do not look for it.

**Both edits, one commit.** FR-001 without FR-002 converts the failure into
`Test timed out in 5000ms` — and *more* often, not less: spec §Claim 8 measured
3-in-5 that way against 2-in-5 unchanged.

### T005 [US1] — write the guard, and observe it RED ⟨GATE⟩

**File:** `apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx`

Add the test plan §3 sketches, **after** the existing should-fix-5 test. Named
constant `SLOW_RESPONSE_MS = 1500`. **Do not edit any existing test.**

The red observation, in this order:

1. With T004's two edits **temporarily reverted** (`git stash` them), run the
   file. **Expect red**, with `Unable to find an element by:
   [data-testid="placeholder-preview-error"]` naming the **new** test.
2. Save the verbatim output. This is what the PR body quotes.
3. Restore T004. Run again. **Expect green.**

**Done when:** both outputs are saved and the guard is green with T004 applied.

**Do not adjust the guard to make step 1 red.** If step 1 is green, the guard
guards nothing — construct the counterfactual until it fails, or report that the
mechanism is not what the spec claims (MEMORY: *prove a guard by counterfactual*).

Note on shape: this guard asserts on a **DOM element the component renders**, not
on the configured timeout value. Asserting `asyncUtilTimeout === 10_000` would be
an assertion checking its own input (MEMORY) and would pass whether or not the
bound does anything.

### T006 [US1] — verify the guard cannot be satisfied by a smaller bound

**Files:** none permanently written.

Set `asyncUtilTimeout` to 1000 (RTL's default) with the guard in place. Run.
**Expect red.** Restore 10 000.

**Done when:** SC-2's counterfactual output is saved. This is the only evidence
that distinguishes a guard from a test that happens to pass.

### T007 [US1] — run the contended reproduction, before and after

**Files:** none written.

With busy Node processes saturating the machine — phase 1 used 24 on 8 cores:

```sh
node -e "const e=Date.now()+900000;let x=0;while(Date.now()<e){for(let i=0;i<1e6;i++)x+=Math.sqrt(i)}" &
```

1. On the tree **without** T004/T005, run the affected file **ten times**. Record
   failures and their messages. Phase 1 observed 2/5 and 2/5 in two batches.
2. With T004/T005 applied, run it **ten times**. Expect zero failures **and no
   `Test timed out`**.

**Done when:** both counts are recorded.

**If step 2 shows any failure:** stop and report. A partial cure is spec A-1
failing, and the escalation is ADR-0150's CI-budget job, **not** a larger number.

*This is the only evidence in the whole delivery that speaks to the actual
defect. A green local run and a green CI bucket both say nothing — the test
already passes ~95 % of the time.*

### T008 [US1] — lint, typecheck, format

**Files:** none written.

`pnpm lint && pnpm typecheck && pnpm format:check` at the repo root.

**Done when:** all three are clean **and the guard carries no `eslint-disable`**
(SC-8). If ADR-0150's selectors flag it, that is a finding about the selector —
report it, do not suppress it.

*`pnpm typecheck` runs `typecheck:e2e`, which has failed on a clean `develop`
before (MEMORY). If it fails, confirm against a stash before attributing it here.*

### T009 [US1] — characterisation, after

**Files:** none written.

Re-run `pnpm -r --filter "./apps/**" test`. Compare against T002.

**Done when:** `management-web`'s count matches T002's **plus exactly one** (the
guard), the other packages match exactly, and `git diff` on the test file shows
**only an addition**.

**If an existing assertion had to change:** block. That is evidence the deadline
change moved behaviour, and it is a different spec (constitution §Testing;
CLAUDE.md house rules).

---

## US2 (P2) — `apps/kiosk-web`, **only if the reviewer asks**

**Default: skip T010 and T011 entirely.** `kiosk-web` has no reported flake, and
the gate's criterion for dropping `apps/shared` applies here identically. These
two tasks exist so that *if* the reviewer wants the consistency, the work is
specified — not because the spec recommends doing it.

### T010 [US2] — the same two edits in `apps/kiosk-web`

**Files:** `apps/kiosk-web/src/test/setup.ts`, `apps/kiosk-web/vite.config.ts`

Identical `configure({ asyncUtilTimeout: 10_000 })` + comment, identical
`testTimeout: 30_000`. **No test file edited.** Leave
`useSessionExpiry.test.ts:178`'s per-site `{ timeout: 10_000 }` exactly as it is
— removing it is a separate change and would destroy T011's baseline.

Blocked on T009.

### T011 [US2] — kiosk-web characterisation

`pnpm --filter ./apps/kiosk-web test`. **Done when:** the count matches T002's
kiosk figure exactly.

---

## Wrap-up

### T012 — commits and the PR

**Commits** (ADR-0030 Conventional Commits; **no `Co-Authored-By`** per ADR-0086;
each commit must build on its own, because rebase-merge lands them individually —
ADR-0087):

| Commit | Contents |
|---|---|
| `test(management-web): bound waitFor by a deadline a loaded runner cannot outrun` | T004 + T005 — the guard and the bound it needs are one change |
| `test(kiosk-web): adopt the same async deadline` | T010, **only if US2 was authorised** |

**PR to `develop`** — `gh pr create --base develop` (CLAUDE.md; never `main`).

The body must carry:

- T005's **verbatim red output**, and the green after (ADR-0139 phase-4 gate).
- T006's counterfactual output.
- T007's contended counts, before and after.
- T002/T009's characterisation counts for every package touched.
- The spec §"The objection in #2419" comparison, in short: why *"a timeout is not
  a fix"* was the right caution against an unbounded race and why the measured
  defect is the bounded kind — two independent harnesses, both reproducing CI's
  exact failure, both cured by the same change.
- One line stating plainly that **a green `frontend` bucket is not evidence
  here**, and why.
- `Closes #2520` and `Closes #2419` — a closing keyword for **each**.

**After the merge, verify both issues actually closed.** A PR mention auto-closes
roughly one time in three (MEMORY), and this PR is asking for two.

### T013 — the board

Add the feature issue to Project #13 by hand; `/speckit-tasks` adds nothing:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2520
```

Needs the `project` scope (`gh auth refresh -s project,read:project`).
`item-add` prints nothing on success, and `item-list` defaults to 30 items —
verify with `--limit 2000`, or a filled board reads as empty (CLAUDE.md; MEMORY).

**Feature-level only. No per-task issues** — that stopped after spec 028.

---

## What is explicitly not a task

- **`apps/shared`.** Dropped at the gate; no reported occurrence (ADR-0036).
  Spec §US3 keeps the inspection so nobody re-derives that the packages differ.
- **Removing the redundant per-site `{ timeout: 10_000 }` annotations.** Out of
  scope; it would destroy T002/T011's baselines.
- **Shimming `globalThis.jest = vi`, or bumping `@testing-library/dom`.** Plan
  §Alternatives C and D — each wants an ADR, which ADR-0144 forbids this lane
  from writing.
- **Touching any production file.** If the change needs one, spec §Claim 2 is
  wrong and phase 1 reopens.
- **Chasing #2247 / #2409.** Answered in spec §Claim 6: different defect, already
  closed, no shared cause.
