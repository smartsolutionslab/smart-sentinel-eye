# Tasks 480 — `LayoutGrid` and its `Tile` are broken up by responsibility

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3195 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation.** The 14 covering files stay unmodified (`git diff`
on them empty). Anything the probes (T002) show the family does not notice, and the identity/count facts (SC-006), get tests in **new**
files, written first and **green on the unsplit file** (T003), with their failure messages read (T004).
**Latency (§IV): this is the wall's render path.** Every extraction commit message and the PR name the legs it touches (event → overlay
state, label hold, overlay draw, composite + render, or none) and state them unchanged (FR-010).

One PR. The grid and its family live in `apps/kiosk-web` (run Vitest from `apps/kiosk-web`); `shared`, `management-web` and
`management-cameras` re-run at the gate and at the end. Scripts, from the repo root: `npm run -w <app> test | lint | typecheck`,
`npm run format:check`. Scratchpad files are not committed.
**Commit hygiene (lessons from specs 470–479):** anchor the working directory in every command and print `pwd`; `git add` only the files
of the step (a foreign edit in the tree is never staged; rebase with `--autostash`); `prettier --write` every generated file before
committing; **a counterfactual must fail for the right reason: read the failure message, not the count**; stage a **new untracked unit**
before its counterfactuals; node scripts are files in the scratchpad run with the cwd at the folder they edit (never `node -e` with
`/tmp` paths); count hook calls with a regex that allows **nested** generics; grep for other uses before removing an import; rewrite
every "above"/"below" pointer when the code it points at moves; a hook that now receives refs through an object gets them in its
dependency array (stated in the commit, never suppressed); type a hook's bag with an explicit interface; **one vitest at a time**, long
runs as a direct background command, batches of ten; **restore the file after every break before anything else runs**; every spec
rewrites `.specify/feature.json`, keep the branch's value on a rebase.

## Phase 1 — Record the "before" (T-A)

- [x] **T001 [US1]** (frontend) On the unsplit tree (after rebasing the branch onto `origin/develop`) record into the scratchpad, for the 14
  covering files (`LayoutGrid{LabelPairing,ResolvedTextRace,WallCredential}`, `CameraTile`, `CellPage`, `CellPage{FabSkew,LabelAge,
  LabelSet,LiveUpdates,OmittedInputs,ResolvedText,WallFab}`, `features/wall/WallPage`): (a) vitest JSON → sorted `file :: full name ::
  status` (`names471.cjs`); (b) sorted assertion lines; (c) line counts of `LayoutGrid.tsx` (682), `LayoutGrid` (202) and `Tile` (243),
  code and comment lines apart; (d) hook calls in the file, nested-generic-aware (expect 23); (e) `eslint-disable` (expect exactly
  one, `react-hooks/refs`); (f) the `kiosk-web` suite and the consumers' suites (`shared`, `management-web`, `management-cameras`):
  counts and durations. Depends on nothing.

## Phase 2 — Probe first, then pin (US1)

### User story 1 (P1) — the wall behaves exactly as before, on the same budget (FR-001, FR-003, FR-010, SC-005, SC-006)

- [x] **T002 [US1]** (test-adversary) Write `probe480.cjs` and `run480.cjs` in the scratchpad (pattern of `probe479.cjs`/`run479.cjs`): the
  named breaks of plan §R2 (A–K), each asserting its pattern matches **exactly once**, applying it to the unsplit `LayoutGrid.tsx`,
  running the family from `apps/kiosk-web`, recording which test failed and its message or "none", and restoring from git (`git diff`
  empty after each). Run in batches of ten, one vitest at a time. Record the matrix; for each "none" decide: new test, **double break**
  when another term masks it, or **equivalent** with the reason. Depends on T001.
- [x] **T003 [US1]** (test-writer) Create `LayoutGridIdentity.test.tsx` (plan §R3: the draw measurement fires once per distinct painted
  set and again for a changed set or a changed `highlighted`; a token renew does not re-fire it; the layout faults are logged once per
  page and never while loading; an equal re-render hands `CameraTile` the same `overlays` reference) **first**, and tests in further
  **new files** for every concern the matrix leaves unnoticed (as T002 names). Run from `apps/kiosk-web`: **green on the unsplit
  file** (quote the output). Depends on T002.
- [x] **T004 [US1]** (test-adversary) For each new test, re-apply its break and **read and quote the failure message** (right reason, not
  just red); restore (`git diff` empty). Write the double-break and equivalence tables. Commit `test(480): pin the LayoutGrid behaviours
  the family missed`. Depends on T003.

## Phase 3 — One extraction per commit (US1, US2)

### User story 2 (P2) — the components read as a list of what they compose (FR-002, FR-005, FR-006, FR-007, FR-010)

Each task: move only what plan §R4 names, **at the position of the code it replaces**; hook and effect order as plan §R5; dependency
arrays copied verbatim; comments move with their code once; after the move apply the matching T002 break to the **new unit** and see
the same test fail there; run the 14 covering files + the new gap files (including `LayoutGridIdentity`) from `apps/kiosk-web`,
`eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` (before committing); `git diff --stat` shows only the grid and its new
unit(s); `git add` those only; commit `refactor(480): <unit>` with a body line **"§IV legs touched: …; unchanged."** Fixed order, one
branch, one index. Each depends on the previous.

- [x] **T005 [US2]** `layoutGrid/gridPieces.tsx` (concerns F, K): `EmptyCell`, `FullScreen`, `WallHeader`, `gridPlacementStyle`. Legs:
  composite + render (static presentational pieces); unchanged.
- [x] **T006 [US2]** `layoutGrid/Tile.tsx` (concern K): the whole `Tile`, `TileProps`, `useStableByKey`, `paintedElementsKey`,
  `isTextElement` moved **as they are** to their own file; `LayoutGrid.tsx` imports `Tile`. Legs: none (a module move; same component,
  same key at the call site); unchanged.
- [x] **T007 [US2]** `layoutGrid/useLatestAccessToken.ts` (concern A): the ref, **the one suppression (`react-hooks/refs`)** with its
  comments and the stable `getToken`; the grid calls it with `auth.user?.access_token`. Legs: the identity every tile effect keys on
  (overlay draw, decode sampler); unchanged.
- [x] **T008 [US2]** `layoutGrid/useLayoutFaultReporting.ts` (concern D): the once-per-page latch ref and the fault effect, called right
  after `useOverlayHubHandlers`. Legs: none (log only; no leg's data path); unchanged.
- [x] **T009 (decision gate T-D1)** (orchestrator) **Measure, then ask.** `LayoutGrid` and `Tile` lengths, `LayoutGrid.tsx` lines, hook
  calls left, the units' sizes and the **file total** against T001; extrapolate to the four remaining hooks using plan §R4. Report to
  the maintainer **before** T010 and ask whether to continue. Depends on T008.
- [x] **T010 [US2]** `layoutGrid/useLayoutData.ts` (concern C): `useGetLayoutQuery` (with its `skip`), `published`, `tiles`, `items`
  (unmemoised), `useWallCredential`, `wallFab`; returns them. Legs: composite + render (the page's data); unchanged.
- [x] **T011 [US2]** `layoutGrid/useTileOverlay.ts` (concern G): `overlayIdentifier`, the overlay query, `publishedOverlay`,
  `overlayUnavailable`, `elements`, `textElements`, `hasPlaceholder`, the label-verdict effect. Legs: event → overlay state; unchanged.
- [x] **T012 [US2]** `layoutGrid/useTileLiveElements.ts` (concern H): `snapshotSkipped`, the snapshot query, `textsByTemplate`,
  `liveElements`, `hasTemplateMiss`, `useTemplateMissRefetch`. Legs: event → overlay state (the resolved-text path); unchanged.
- [x] **T013 [US2]** `layoutGrid/useHeldOverlay.ts` (concerns I, J): `paintedElementsKey`, `useStableByKey`, `reportHeld`,
  `useLabelDelay`, `renderElements`, the draw-measurement effect. Legs: label hold and overlay draw; unchanged. **Stop and ask** past
  ~8 inputs.

## Phase 4 — Gates and PR

- [x] **T014** (frontend) **T-A after:** repeat T001's records. (a) names+status `diff` empty for the 14 covering files (new gap files
  listed separately); (b) assertion lines `diff` empty; `git diff --stat origin/develop...HEAD` on the family is empty (FR-001); (c)
  `LayoutGrid.tsx`, `LayoutGrid`, `Tile`, every unit (code and comment lines apart) and the **file total** before/after (SC-002); (d)
  exactly one `eslint-disable`, in `useLatestAccessToken`, rule and justification unchanged (SC-003); (e) distinctive comment phrases
  once, no stale "above"/"below" pointers; (f) the `kiosk-web` suite and the consumers' suites, counts and durations before/after
  (SC-004); (g) the `LayoutGridIdentity` figures equal before and after (SC-006); (h) the per-unit counterfactuals recorded. Depends on
  T013.
- [x] **T015** (frontend) `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` for `kiosk-web`, `shared`, `management-web`,
  `management-cameras`; `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`). Depends on T014.
- [x] **T016** (frontend) `specs/480-layout-grid-split/verification.md` (probe matrix, gap tests with quoted messages, double-break and
  equivalence tables, the identity/count figures, sizes, **the per-commit §IV leg statements**, the latest-token follow-up, what was
  not extracted and why); tick T001–T017. Depends on T015.
- [ ] **T017** (orchestrator) Phase 6 `/code-review medium`, findings fixed or accepted in writing; Phase 7 PR to `develop` (`--base
  develop`, `Closes #3195`, Phase 4a characterisation stated, **a §IV section naming each leg touched and its evidence**, sizes as
  measured, gaps and slips visible); park; merge when CI concludes success on the PR tip; close-out comment on #3195. **This closes the
  survey.** Depends on T016.

## Dependencies

T001 → T002 → T003 → T004 → T005 → … → T009 (**gate**) → T010 → … → T013 → T014 → T015 → T016 → T017. Strictly linear: one branch, one
index, one concern per commit. No parallel tasks.

## Implementation strategy

MVP is T005–T008 plus the gate: two presentational/module moves and the two hooks that touch the fewest legs (the token getter and the
log-only fault reporter). They are expected to shrink the components little (478: −6 %, 479: 0 %); the real reduction is T010–T013,
which touch the data, overlay-state and render legs and so come only after the gate, with the identity/count test already green. If
T009 shows the decomposition is not earning its keep, stop there and ask.
