# Tasks — Spec 156, a recovery control that survives its own activation

**Phase:** 3 (Tasks) — ADR-0037
**Issue:** #2372 · **Branch:** `fix/2372-a-recovery-control-that-survives`
**Spec:** `spec.md` · **Plan:** `plan.md`
**Engineer:** `frontend-engineer` (TSX only; `apps/management-web` + `apps/shared`)
**Reviewer:** `frontend-reviewer`
**Phase 4a colour: RED.** Behaviour-changing. See §Phase 4a.
**Latency (§IV): N/A** — no leg. See spec §Latency budget.
**Delivery: one PR.** See §One PR, not a split.

`[P]` = disjoint files, safe to run concurrently (ADR-0109).

---

## Foundational — these block the rest

### T001 — Widen `ButtonProps` so Save can take a ref

**File:** `apps/shared/src/ui/primitives/Button.tsx`

`ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement>` →
`extends ComponentPropsWithRef<'button'>`. Type-only; the component body does not
change. React 19.2.8 already passes `ref` through `{...rest}` at runtime — this
only stops TypeScript refusing it (plan §3b).

**Done when:** `<Button ref={someRef}>` type-checks, `pnpm -r typecheck` is clean
across both apps, and no existing `Button` call site changed.

**If it ripples:** stop and take plan §3b's named fallback (`useId()` id +
`document.getElementById`), which keeps the change inside the two dialogs. Do not
invent a third option.

**Blocks:** T006, T007.

### T002 — Make the test harness able to express an in-flight re-read

**Files:** `apps/management-web/src/features/overlays/OverlayEditorDialogChainRecovery.test.tsx` (new),
`apps/management-web/src/features/layouts/LayoutEditorDialogChainRecovery.test.tsx` (new)

New files, not additions to the existing dialog test files — plan §5 rule 2, and
it is house convention already (three `LayoutEditorDialog*.test.tsx`, five
`OverlayEditorDialog*.test.tsx`).

Each file mocks its chain query with a mutable state object that carries
**`isFetching`**, which `OverlayEditorDialog.test.tsx:24-28` does not have, and
gives `refetchChainMock` an implementation that advances that object so a test can
step `failed → in flight → resolved` or `failed → in flight → failed`.

**Done when:** a test can drive all three steps and assert on each, *and* the
counterfactual holds — with the harness present and the fix absent, a test
asserting the in-flight state fails because the control is gone, not because
`isFetching` was never true. A harness that cannot make `isFetching` true would
let a vacuous test pass against an implementation that never renders the state at
all (plan §6).

**Blocks:** T003, T004.

---

## Phase 4a — tests first, observed RED

Written by `test-writer` against the **dialogs**, never against
`ChainRecoveryNotice` (spec NFR-002): the extraction in plan §2 stays revisable
without rewriting a test. Verbatim failure output is the engineer's brief and is
quoted in the PR body (ADR-0139).

### T003 [US1] — Overlay dialog: the five scenarios, red

**File:** `apps/management-web/src/features/overlays/OverlayEditorDialogChainRecovery.test.tsx`
**Depends on:** T002

One test per spec acceptance scenario:

1. **The discriminating one** (below).
2. Retry refused again — FR-006.
3. Two refusals in a row are both announced — FR-009.
4. Reload keeps its own focus and gets no move — FR-008.
5. A normal successful open moves no focus and announces nothing — FR-007.

**Depends on:** T002 · **Blocks:** T006

### T004 [P] [US2] — Layout dialog: the same five, red

**File:** `apps/management-web/src/features/layouts/LayoutEditorDialogChainRecovery.test.tsx`
**Depends on:** T002 · **Blocks:** T007

Disjoint file from T003; `[P]`. The tests mirror T003 with `layout` for
`overlay`. Mirroring is deliberate — spec FR-012, and the drift between these two
dialogs is the whole reason the issue exists, so the parity is expressed as a
test rather than as a review habit.

### The discriminating test

```
Retry keeps focus while the re-read is in flight, and hands it to Save when it succeeds
```

Shape — the assertion that separates the fix from every near-miss is **#2**:

```
given   edit mode, chain query { isError: true, isFetching: false }
        the Retry button exists inside a role="alert"
        focus it

when    the operator activates Retry
        (refetch advances the mock to { isError: false, isFetching: true, data: undefined })

then    1. getByRole('button', { name: 'Retry' })     is in the document
        2. document.activeElement                      IS that button          <-- discriminating
        3. that button                                 has aria-disabled="true"
        4. the status region                           reads /re-reading the overlay/i
        5. queryByRole('alert')                        is null

when    the re-read resolves { isError: false, isFetching: false, data: { version: 7 } }

then    6. queryByRole('button', { name: 'Retry' })    is null
        7. document.activeElement                      IS the Save button
        8. the Save button                             is not disabled
        9. the status region                           reads /was read/i
```

**Why #2 is the discriminating assertion.** It fails today for the exact reason
the issue names, and it is the one a plausible wrong fix still fails. Move focus
"on success" and #2 still fails — `isError` is already false at request time
(spec §1), so the button is gone before the response exists. Keep the button but
natively `disable` it and #2 still fails — the browser blurs it
(`OverlayEditor.tsx:649-657`). Only keeping it mounted *and* not natively
disabling it satisfies it.

**Assertion #3 must read `aria-disabled`, not `toBeDisabled()`.** Testing
Library's `toBeDisabled` is satisfied by `aria-disabled` on some versions and
not others; asserting the attribute says which mechanism was used, and the
mechanism is the requirement.

**Assertion #5 exists so the alert is not merely left mounted.** A fix that keeps
the whole `<p role="alert">` through the in-flight window would pass #1 and #2 and
be wrong: the failure path's announcement depends on that node being *inserted*
when the read fails, and a node that never unmounts is never re-inserted.

jsdom implements focus, so all nine assertions are unit-testable. This is
**unlike** spec 154's blur-on-native-disable, which had to be reasoned about
rather than tested — here the behaviour under test is focus retention, which
jsdom does model.

**Expected red, all five tests, both files.** Anything arriving green is a phase
4a failure, not a shortcut (CLAUDE.md §the autonomous lane).

---

## Phase 4b — implementation

### T005 — `ChainRecoveryNotice` composite

**File:** `apps/shared/src/ui/composites/ChainRecoveryNotice.tsx` (new)
**Depends on:** T003, T004 (red output in hand)

Per plan §3a: always-mounted `role="status"` (`sr-only`, `data-testid`); the
chain-read arm rendering the control when `readFailed || reReading` and the
`role="alert"` only when `readFailed`; the submit-refused arm moved verbatim with
its comments; `type="button"`; `aria-disabled={reReading}` with a handler that
refuses while re-reading.

The announcement uses the **`key`-token remount** (`OverlayEditor.tsx:501-508`),
not the ZWSP toggle — plan §3a says why, and the choice is what makes T003's
assertion 3-of-the-repeat-scenario honest.

The focus latch is plan §4: a ref set by the control's handler, an effect on
`reReading`'s **falling edge**, and one shared code path for Retry and Reload
distinguished by a `moveFocus` boolean on the latch. **A plain effect on
`readFailed` going false steals focus to Save on every normal dialog open** —
that is FR-007, and it is the trap this task exists to avoid.

**Done when:** it compiles, lints, and the dialogs can be wired to it. It gets no
test of its own — the dialog tests are the contract (NFR-002).

**Blocks:** T006, T007.

### T006 [US1] — Wire `OverlayEditorDialog`

**File:** `apps/management-web/src/features/overlays/OverlayEditorDialog.tsx`
**Depends on:** T001, T003, T005

Three edits, and only three (plan §3c):

1. Add `isFetching: chainFetching` to the `useGetOverlayQuery` destructure
   (`:165-169`). The layout dialog already reads it; this one does not.
2. Add `saveRef` and put it on the Save `<Button>` (`:302`).
3. Replace `:270-296` with `<ChainRecoveryNotice … />`.

**`:302`'s `disabled` predicate is NOT touched.** Adding `chainFetching` to it is
a real improvement and a **different bug** — spec §Found and not folded item 1,
filed by T009. FR-010.

**Not touched, and named because this file carries merged work from #2341, #2364
and spec 153:** the label-validation alert `:267`; the resolve-preview query and
its `currentData` reasoning `:161-179`; the `getToken` ref `:104-116`; the close
effect `:120-126`; `onSubmit` and its version read `:181-205`; the error-code
keying `:206-228`. The "one alert at a time" comment at `:270-275` **moves into
the composite unchanged** — it is not rewritten and not dropped.

**Done when:** T003's five tests are green and the whole `management-web` suite
is green.

### T007 [P] [US2] — Wire `LayoutEditorDialog`

**File:** `apps/management-web/src/features/layouts/LayoutEditorDialog.tsx`
**Depends on:** T001, T004, T005

Two edits (plan §3d): `saveRef` on Save (`:416`, `disabled` untouched — it
already includes `chainFetching` and is correct), and `:354-380` replaced with
the composite. `chainFetching` is already destructured at `:89`.

**Hard rule, plan §5: no edit above line 236 of this file.** PR #2377 (issue
#2371, open) changes `:89-108`. Put `saveRef` and anything else below the
`staleConflict` / `backendError` derivations at `:236-240`. Following this, a
rebase onto a merged #2377 is clean.

`[P]` with T006 — different file, different test file. Both depend on T005, so
they fan out after it, not before.

**Not touched:** the camera-filter `aria-live` region `:340` (a different region
doing a different job, already always-mounted and already correct), the
truncation notice `:310`, `GridDesigner`, the close effect.

**Done when:** T004's five tests are green and the whole suite is green.

---

## Guards — what stops this regressing

### T008 — The parity guard is the mirrored suite

**Files:** the two new test files
**Depends on:** T006, T007

No new mechanism. T003 and T004 are the same five scenarios against the two
dialogs, so a change to one that is not made to the other **fails a test rather
than surviving a review**. That is the guard the issue actually asked for
("both dialogs fixed together, since they will otherwise drift").

Two specific assertions carry the load and must be present in both files:

- **The status region is queried and found empty *before* any retry.** That is
  what proves it is always-mounted rather than mounted on demand — the #2346
  requirement. Asserting only its populated state would pass against the broken
  shape.
- **`queryByRole('alert')` is null during the in-flight window** (assertion #5).
  That proves the alert is still insertion-announced on failure.

**No repo-wide guard is added, and that is a decision, not an omission.** A lint
rule or architecture test for "focusable control inside a conditionally-rendered
live region" would fail on **14 sites** the moment it existed (spec §The census),
so it could only ship as a suppression list or a count ratchet. This repo has
been bitten by exactly that: a guard narrowed to its issue's own examples fails
on precisely the number it filed and proves nothing. The census issue (T009) is
where such a guard belongs — after the sites it would fire on are gone.

### T009 — File what was found and not folded

**Depends on:** T006, T007 (so the references are real)

Three issues, each labelled `bug` where it is one, none labelled `agent:ready`
(the label is the human gate — ADR-0144):

1. **`OverlayEditorDialog` does not disable Save during a chain re-read.**
   `:302` lacks the `chainFetching` term `LayoutEditorDialog.tsx:416` has, so
   during a Reload the operator can submit the pre-Reload version while the
   re-read that would correct it is in flight. Concurrency (ADR-0113), not
   accessibility. Quote both predicates.
2. **The other 14 census sites.** Carry spec §The census is 16 sites verbatim as
   the table, name spec 156 as the worked reference, and note that the
   page-level sites differ in having no chain version and no Save to hand focus
   to — so the pattern transfers but the target of the focus move does not.
   Note that a repo-wide guard becomes possible once these are done (T008).
3. **No shared live-region helper**, and the repeated-announcement problem is
   solved twice, privately, in `OverlayEditor.tsx` (`:387-398` ZWSP toggle,
   `:501-508` key-token). Worth extracting once there are more call sites than
   the two this spec creates — **not now** (no speculative generality).

### T010 — Phase 3 gate: #2372 on Project #13

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2372
```

Feature-level, not per-task — no `/speckit-taskstoissues` for this spec
(CLAUDE.md §Workflow; per-task `[TNNN]` issues stopped after spec 028).

**Blocked at the time of writing:** Projects v2 GraphQL is rate-limited
(`API rate limit already exceeded for user ID 217957015`) and `gh project` is
GraphQL-only, so membership could be neither verified nor added. Re-run this
before the phase 3 gate is called satisfied. `item-add` prints nothing on
success, and `item-list` defaults to 30 — verify with `--limit 2000` and filter
on `content.url`, never on a number field.

---

## Dependency graph

```
T001 (Button ref) ──────────────┐
                                ├──> T006 (overlay wire) ─┐
T002 (harness) ─┬─> T003 (US1 red) ──┤                    ├─> T008 (guards)
                └─> T004 (US2 red) ──┤                    │        │
                                     │                    │        v
                     T005 (composite)┴──> T007 (layout) ──┘     T009 (file)
                                                                     │
                                                                     v
                                                                  T010 (board)
```

- **T001 and T002 are foundational** and can run at once — different files,
  neither reads the other. Everything else waits on them.
- **T003 ∥ T004** — disjoint new test files.
- **T006 ∥ T007** — disjoint dialogs, disjoint test files; both wait on T005.
- T005 is the serialising point: one composite, two consumers.

## One PR, not a split

The composite has two consumers and no reason to exist without them. Splitting
US1 and US2 would land either a composite nothing imports or a dialog pair that
is half-fixed — and "half-fixed" is exactly the drift state the issue was filed
to end. The diff is two dialogs (≈25 lines each, mostly moved), one new
composite, one type widening, and two new test files.

`OverlayEditorDialog.tsx` and `LayoutEditorDialog.tsx` both shrink, which is the
direction a 309-line and a 424-line file should be moving.

## Phase 5 / 6 notes for whoever picks this up

- **Phase 5 verify:** the spec's manual procedure needs a screen reader and a
  deliberately-broken overlays API. If that is not available, the honest note is
  "unit-observed, not screen-reader observed" — say which, rather than implying
  the second. Focus behaviour *is* observable without one: Tab from the recovery
  control mid-re-read and confirm the next stop is inside the form, not the top
  of the dialog.
- **Phase 6:** the review question that matters most is FR-007 — ask the
  implementation to demonstrate that a normal dialog open moves no focus. It is
  the failure mode a correct-looking implementation most easily has.
- **Before merge:** rebase against `develop` whatever the order with PR #2377
  (ADR-0087 renames SHAs), and re-read FR-008 afterwards — #2377 changes what
  clears `backendError` on close, which is adjacent to Reload's lifetime even
  though it does not change it.
