# Tasks 471 — The list-page scaffolding management-web pages repeat lives in one place

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3190 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **the three new pieces → red first (T002–T003); the four pages →
behaviour-preserving, characterisation (existing suites, unmodified, `git diff` on them empty)**.

One PR, one commit per step; every commit builds. The commit carrying the red tests leaves the new suite failing
until the next one (CLAUDE.md: "builds is not passes"; accepted pattern). Scripts, from the repo root:
`npm run -w management-web test | lint | typecheck`, `npm run format:check`. Scratchpad files are not committed.

Pages and their suites (the safety net, **not edited**): `features/overlays/OverlaysPage{,FocusRefetch,RetryRecovery}.test.tsx`,
`features/layouts/LayoutsPage{,FocusRefetch,RetryRecovery}.test.tsx`, `features/rules/RulesPage{,RetryRecovery}.test.tsx`,
`features/systemVariables/SystemVariablesPage{,FocusRefetch,RetryRecovery}.test.tsx`.

## Phase 1 — Record the "before" (T-A)

- [x] **T001 [US1]** (frontend) On the unchanged tree record into the scratchpad: (a) vitest JSON for the 11
  suites above → sorted `file :: full name :: status`; (b) their sorted assertion lines (`expect(`, `.toHaveBeen…`,
  `.toBe…`, `.toEqual`, `.toThrow`); (c) line counts of the four pages (expect 404 / 390 / 272 / 257 = 1323);
  (d) the whole management-web suite (666 tests) and its duration; (e) a general-pattern grep of the four pages for
  `headingRef`, `useRef<ComponentRef<'h1'>>`, `archiveFor`, `discardFor`, `STATE_FILTERS` (SC-003 baseline).
  Depends on nothing.

## Phase 2 — The shared pieces, red first

### User story 2 (P2) — the scaffolding exists once (FR-002, FR-004)

- [x] **T002 [US2]** (test-writer) Create, **before** the modules exist, in `apps/management-web/src/features/`:
  `PageHeading.test.tsx` (renders an `<h1>` with `tabIndex=-1`, class `text-2xl font-semibold focus:outline-none`,
  the given `ref` and children); `useHeadingFocus.test.tsx` (`focusHeading` focuses the element `headingRef` is
  attached to; is stable across renders; is a no-op before mount); `StateFilter.test.tsx` (a `role="group"` with
  `aria-label="Filter by state"`; one button per option, `aria-pressed` true only for the option equal to `value`;
  click calls `onChange` with that option's value including `undefined`; `className` default `flex gap-2` and used
  verbatim when given; `labelled(['A','B'])` → `[{label:'A',value:'A'},…]`); `useConfirmTarget.test.tsx` (`target`
  starts `null`; `ask(x)` sets it; `cancel()` clears; `confirm(run)()` calls `run` with the target **then** clears it,
  in that order; `confirm(run)()` with a null target does nothing; the handler from the latest render uses the latest
  target). Run `npm run -w management-web test -- PageHeading useHeadingFocus StateFilter useConfirmTarget`;
  **quote the red output verbatim** (expected: cannot find module). Test-only commit
  `test(471): list-page pieces (red)`. Depends on T001.
- [x] **T003 [US2]** (frontend) Create `PageHeading.tsx`, `useHeadingFocus.ts`, `StateFilter.tsx` (exporting
  `StateFilter` and `labelled`) and `useConfirmTarget.ts` exactly as plan §R2–R4. T002's tests pass **unmodified**;
  `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` on the new files. Commit
  `feat(471): …` is wrong for a refactor: use `refactor(471): list-page heading, filter and confirm-target pieces`.
  Depends on T002.
- [x] **T004 [US2]** (test-adversary) **Counterfactuals, one break at a time, not committed:** drop `tabIndex` from
  `PageHeading`; make `focusHeading` a no-op; invert `pressed` in `StateFilter`; clear the target **before** running in
  `useConfirmTarget`. Run the matching test each time, **quote each failure**, restore (`git diff` empty). Depends on T003.

## Phase 3 — One commit per page

### User story 1 (P1) — the four pages behave exactly as before (FR-001, FR-003, FR-005)

Each task: replace only what plan §R2–R4 names, at the position of the statement it replaces (R6); run that page's
suites (`npm run -w management-web test -- <Page>`) **before** committing; `eslint`/`tsc`/`prettier` on the page; the
page's test files unchanged (`git diff --stat` shows only the page). Commit `refactor(471): <Page> uses the shared list-page pieces`.
Fixed order, one branch, one index; each depends on the previous.

- [x] **T005 [US1]** `overlays/OverlaysPage.tsx`: `useHeadingFocus` + `PageHeading`; `StateFilter` with
  `labelled(['All','Draft','Published','Archived'])` and `className="mb-4 flex gap-2"`; `useConfirmTarget` for
  `archiveFor` and `discardFor` (target types kept inline, incl. `liveRevision`). Subject strings, `pending`,
  consequence copy and mutation arguments untouched.
- [x] **T006 [US1]** `layouts/LayoutsPage.tsx`: same four substitutions; the two `LayoutEditorDialog`s untouched.
- [x] **T007 [US1]** `rules/RulesPage.tsx`: `useHeadingFocus` + `PageHeading`; `StateFilter` with its existing
  `{label, value}` array (no `labelled`), value `stateFilter`, `onChange={setStateFilter}`, default `className`;
  `useConfirmTarget` for `archiveFor`. `dryRunFor` stays. Query arguments untouched.
- [x] **T008 [US1]** `systemVariables/SystemVariablesPage.tsx`: `useHeadingFocus` + `PageHeading`; `StateFilter` with
  `labelled(['All','Defined','Archived'])`, default stays `'Defined'`; `useConfirmTarget` for `archiveFor`.
  `pendingEdit` and the `variablesArgs`/`currentData` logic untouched.

## Phase 4 — Gates and PR

- [x] **T009** (frontend) **T-A after:** repeat T001's records. (a) names+status `diff` empty; (b) assertion lines
  `diff` empty; `git diff origin/develop...HEAD --stat -- '**/*Page*.test.tsx'` is empty (FR-001); (c) the four
  pages' new line counts, their sum vs 1323, **and** the added lines of the four modules (report both; do not report
  only the drop); (d) management-web suite: 666 + the new tests, all passing, duration before/after; (e) the SC-003
  greps return nothing in the four pages, using the same general patterns as T001(e). Depends on T008.
- [x] **T010** (test-adversary) **Page-level counterfactual:** make `focusHeading` a no-op; the retry-recovery suites
  of the pages must fail (quote which); restore (`git diff` empty). Depends on T009.
- [x] **T011** (frontend) `npm run -w management-web lint|typecheck|test`, `npm run format:check`, and
  `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`). `git diff --stat origin/develop...HEAD` shows only
  `apps/management-web/src/features/` (pieces, their tests, the four pages) and `specs/471-…` / `.specify/feature.json`
  (FR-006). Tick this file's tasks. Depends on T010.
- [ ] **T012** (orchestrator) PR to `develop` (`gh pr create --base develop`): body states Phase 4a (red for the
  pieces with T002's output quoted, characterisation for the pages), T004 and T010 failures, the T-A diffs, the
  **honest line accounting** (pages down by N, new code up by M), the suite duration, and **Closes #3190**. Park;
  do not wait on CI. Depends on T011.
- [ ] **T013** (orchestrator) After merge: confirm #3190 closed (closing keyword, then check the state); comment that
  `OverlaysPage.test.tsx` / `LayoutsPage.test.tsx` splits and the migration of the four
  `Overlays|Layouts` `FocusRefetch`/`RetryRecovery` helpers (#3196 note) are now unblocked; note the follow-up to
  adopt `useHeadingFocus`/`PageHeading` in `AuditPage`, `WallsPage` and the two management-cameras pages. Depends on
  T012 merged.

## Dependencies and parallelism

Strictly sequential: T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010 → T011 → T012 → T013.
The pages share one branch and one index (the memory note "one branch, one index"), so no step can run in parallel.
T002 must be seen red before T003 exists. T004 and T010 are not committed.

## Explicitly not tasks

No `refetchOnFocus` wrapper, no `RetryBanner` wrapper (plan §R5). No page frame component. No change to
`ArchiveConfirmation`, `AuditPage`, `WallsPage` or the management-cameras pages. No edit to any page test. No move to
`apps/shared`. No ADR. No `shard-N.filter` entry (Vitest).
