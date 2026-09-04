# Tasks: The first write of a run

**Feature**: `066-the-first-write-of-a-run` · #2014

**Spec**: `spec.md` · **Plan**: `plan.md`

**Engineer**: `frontend-engineer` (every file is under `e2e/`).

**Phase 4a colour**: **RED** — behaviour-changing. The red is deterministic and
does not need a cold stack; see T001.

---

## Parallelism

**T001–T004 are strictly ordered and own the same two files.** They are the P1
slice: the red, the shared budget, the fix, the seeds. Nothing runs beside them.

**T005–T011 are the US2 sweep and each owns exactly one file** (ADR-0109), so
they are all `[P]` once T002 exists. They may be fanned out to as many workers as
there are files — but they all *import* T002's module, so **T002 is foundational
and blocks every one of them**.

**T012 writes no code.**

---

## Task list

### T001 [US1] — the red, on demand (phase 4a)

**Agent**: `test-writer`. **File**: `e2e/system-variables.spec.ts`.

Add one test, `'a define the service is slow to answer still appears in the
list'`, exactly as designed in `plan.md` § *Phase 4a design*:

- declare `const SLOW_WRITE_DELAY_MS = 20_000;` in the file;
- intercept with a **URL predicate**, not a glob (`url.pathname.endsWith(
  '/system-variables/system-variables')`), because the list `GET` is the same URL
  (`systemVariables.api.ts:88`, `url: ''`) and carries query parameters;
- **guard on `POST` and `route.fallback()` otherwise** — delaying the read would
  make the test red for the wrong reason and it would stay red after the fix;
- assert the row with **no explicit timeout**, the shape every exposed site uses
  today.

Run it against a **warm** stack and capture the output **verbatim**:

```sh
pnpm exec playwright test e2e/system-variables.spec.ts --project=chromium --workers=1 -g "slow to answer"
```

Expected shape — `Timed out 15000ms waiting for expect(locator).toBeVisible()`,
`Locator: getByText('E2E_Slow_…')`, `Received: <element(s) not found>`.

**The verbatim failure is this task's deliverable** and is quoted in the PR body
(ADR-0139). A test that arrives green is a phase-4 failure: check that
`SLOW_WRITE_DELAY_MS` really exceeded the local `expect` budget and that `CI` was
not set in the environment (CI's budget is 30 s and would swallow a 20 s delay).

**Do not** attempt the red by booting a cold stack, and **do not** attempt it by
restarting the `system-variables` service alone — spec 023 §5 measured that a
single-service restart does not reproduce the cost.

**Depends on**: nothing.

---

### T002 [US1] — the shared budget (phase 4b, foundational)

**Agent**: `frontend-engineer`. **File**: `e2e/support/cold-stack.ts` (new).

Export `FIRST_WRITE_TIMEOUT_MS = 90_000` and
`FIRST_WRITE_TEST_TIMEOUT_MS = 180_000`, with the doc comments from `plan.md`.
Both values are taken from existing precedent (`seed-live-video-wall.setup.ts:26,44`),
not invented. The comment must cite `specs/023-first-event-cold-start/verification.md §3`
and must say the mechanism is **unexplained** — a comment that names a cause we
do not have is how spec 023's §4 warning gets repeated.

No env var, no override, no config surface (ADR-0036).

**Depends on**: T001 (the red must be observed first).
**Blocks**: T003–T011.

---

### T003 [US1] — the file the issue was raised about goes green (phase 4b)

**Agent**: `frontend-engineer`. **File**: `e2e/system-variables.spec.ts`.

At each of the **six** write-result assertions — the five existing first-of-kind
sites plus T001's new test — apply the budget and raise the containing test's
timeout:

```ts
test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
...
await expect(...).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
```

Sites: the define in `'…defines a system variable…'`; the define in
`'…defines a Boolean system variable…'`; in `'…sets a variable value…'` **both**
the `row` visibility after the define **and** `row.getByText('Line 1 running')`
after the set (two different message types — `SystemVariableDefinedV1` and
`SystemVariableValueChangedV1`); the `row` in the fab-scoping test; and T001's
assertion.

**Leave at the default budget**, deliberately:
`await expect(page.getByRole('alert')).toHaveCount(0)`, `toHaveCount(0)` on
`#variable-fab-id`, and the `'System variables'` heading assertions. Widening a
wait for something that must not appear turns every failure into a stall
(FR-005).

**The only line T001's test may have changed is its assertion's timeout.** If
anything else about that test moved, the red did not prove what the PR will claim.

Re-run T001's command; it now passes in ~22 s. Run the whole file.

**Depends on**: T002.

---

### T004 [US1] — the two seeds stop restating the number (phase 4b)

**Agent**: `frontend-engineer`. **Files**:
`e2e/support/seed-live-video-wall.setup.ts`,
`e2e/support/seed-bound-overlay-wall.setup.ts`.

Replace `90_000` with `FIRST_WRITE_TIMEOUT_MS` and `180_000` with
`FIRST_WRITE_TEST_TIMEOUT_MS`, and delete the two near-identical six-line
comments that the constant now carries once. **Same values — behaviour-preserving,
and it must stay that way**: if either seed's behaviour changes, the value was
transcribed wrong.

Not `[P]` with T003 only because it is the same commit's argument; the files are
disjoint and it may be done alongside if the engineer prefers.

**Depends on**: T002.

---

### T005 [P] [US2] — `e2e/cameras.spec.ts`

**Agent**: `frontend-engineer`. One site: `:31`, the register-camera row
(camera-catalog, `CameraRegisteredV1`). `:60`/`:61` in the fab test is a second
register in a **different test**, so it takes the budget too; a repeat write
inside one test would not.

**Depends on**: T002.

---

### T006 [P] [US2] — `e2e/camera-detail.spec.ts`

**Agent**: `frontend-engineer`. Four distinct write kinds:
`:19` (register, in the `registerCamera` helper — so every test in the file
inherits it, and the helper's containing tests each need `test.setTimeout`),
`:59` (correct address, `PATCH`), `:103` (retire), `:162` (rename).

The confirmation-dialog assertions (`:94`–`:97`) are reads of a dialog, not write
results — default budget.

**Depends on**: T002.

---

### T007 [P] [US2] — `e2e/overlays.spec.ts`

**Agent**: `frontend-engineer`. One site: `:31`, the draft row (overlay-designer).

**Depends on**: T002.

---

### T008 [P] [US2] — `e2e/layouts.spec.ts`

**Agent**: `frontend-engineer`. The widest file — five sites across **three**
services: `:44` (register camera → camera-catalog), `:56` (overlay draft) and
`:58` (overlay publish → overlay-designer), `:77` (layout draft) and the layout
publish that follows (→ layout-composition). `:109`/`:116` in the concurrency
test are first-of-kind for that test.

**Depends on**: T002.

---

### T009 [P] [US2] — `e2e/rules.spec.ts`

**Agent**: `frontend-engineer`. Two sites: `:32`/`:45` (create draft) and `:112`
(publish → automation). `:120`–`:122`, the refusal alert, stays at the default —
that is the conflict path and it must fail fast.

**Depends on**: T002.

---

### T010 [P] [US2] — `e2e/support/seed-published-layout.setup.ts`

**Agent**: `frontend-engineer`. Three sites: `:39`, `:49`, `:54`. Also raise
`setup.setTimeout(120_000)` at `:22` to `FIRST_WRITE_TEST_TIMEOUT_MS` — 120 s
does not contain three 90 s budgets plus a sign-in, so this file would otherwise
carry budgets it cannot honour.

**Depends on**: T002.

---

### T011 [P] [US2] — the two kiosk files that write

**Agent**: `frontend-engineer`. **Files**: `e2e/kiosk-reconciliation.spec.ts`
(`:74`, set value) and `e2e/kiosk-shows-a-label-over-video.spec.ts` (`:318`, set
value). Both already call `test.setTimeout(300_000)`/`(180_000)`, so only the
assertion budget is added.

Two files in one task because each is a single line and they are the same edit;
split if the orchestrator would rather fan out.

**Depends on**: T002.

---

### T012 [US3] — raise the local/CI asymmetry as its own issue (no code)

**Agent**: `frontend-engineer` (or the orchestrator).

Open an issue linking #2014 that records: `playwright.config.ts` gives CI
`expect: { timeout: 30_000 }` and `retries: 2` against 15 s / 0 locally, so **CI
cannot observe this class of problem at all** — the developer's machine is the
only place it is visible, which is backwards.

It must state that **removing retries is weakening a gate and ADR-0144 forbids
this lane from doing it**, and offer the two candidates worth weighing instead:
a CI signal that reports retried tests (so an absorbed flake is visible rather
than silent), and making `expect.timeout` uniform across environments.

Add it to Project #13:

```sh
gh project item-add 13 --owner smartsolutionslab --url <issue-url>
```

**Depends on**: nothing.

---

## Dependency graph

```
T001 (red, 4a)
  └─ T002 (e2e/support/cold-stack.ts)  ── FOUNDATIONAL
       ├─ T003 ── T004                  (US1, ordered, same argument)
       └─ T005 [P] T006 [P] T007 [P] T008 [P] T009 [P] T010 [P] T011 [P]   (US2)

T012 (US3, no code) — independent of everything
```

**The orchestrator's fan-out point is after T002.** Seven disjoint files, seven
workers, no shared state — each imports a module nobody else edits.

**The P1 slice is T001 → T004** and is independently shippable: the file the
issue names stops failing on a cold stack, and the shared affordance exists.
US2 can land in the same PR or a follow-up.

---

## Commits (ADR-0030 Conventional Commits, ADR-0086 **no `Co-Authored-By`**)

Each commit must build and pass on its own — rebase-merge lands them individually
(ADR-0087), so a commit that only works at the tip breaks `git bisect`.

| # | Tasks | Message |
|---|---|---|
| 1 | T001 | `test(e2e): a define the service is slow to answer has no budget for it` |
| 2 | T002, T003, T004 | `fix(e2e): the first write of a run gets a budget for being first` |
| 3 | T005–T011 | `fix(e2e): every first write of its kind gets the same budget` |

Commit 1 lands the red test **failing**, which is deliberate and is the ADR-0139
artifact — but a red commit on `develop` breaks `git bisect` for everyone after
it. **Squash commits 1 and 2 into one commit if the branch is not to carry a red
tip**, and quote T001's captured output in the PR body instead. The captured
output, not the commit, is what the gate requires.

---

## Phase 3 gate (ADR-0037)

- [x] Tasks atomic, each naming its file and its sites.
- [x] `[P]` markers on disjoint files only (ADR-0109).
- [x] Foundational task (T002) identified and called out for the orchestrator.
- [x] Phase 4a colour declared — **red**, with the exact reproduction.
- [x] Spec references ADRs by number (0108, 0088, 0067, 0113, 0036, 0144, 0139,
      0030, 0086, 0087, 0109).
- [ ] **#2014 is on Project #13** — add by hand; `/speckit-tasks` adds nothing:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2014
```

- [ ] Human confirmation to advance to phase 4.
