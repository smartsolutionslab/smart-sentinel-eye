# Tasks 481 — One latest-access-token getter, shared

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3222 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation.** The existing token tests stay unmodified (`git
diff` on them empty): `LayoutGridIdentity` and the `LayoutGrid*` family (kiosk), `LayoutEditorDialogToken`, `OverlayDraftFormGetToken`
and `OverlayDraftForm.test` (management-web), `CameraDetailPage*.test`, `CameraDetailViewer.test`, `CameraViewer*.test` (management-
cameras). Anything the probes (T002) show they do not notice gets a test in a **new** file, written first and **green on today's
code** (T003), with its failure message read (T004).
**Latency (§IV): on the kiosk path.** The getter's identity keeps the tile overlay-draw measurement, the decode sampler and the hub
connect effects from rebuilding (issues 1888, 1889). Every commit message and the PR name the legs touched (overlay draw, decode
sampler, composite + render, hub connect, or none) and state them unchanged (FR-008).

One PR. Run Vitest from the app that owns the test; scripts, from the repo root: `npm run -w <app> test | lint | typecheck`,
`npm run format:check`. Scratchpad files are not committed.
**Commit hygiene (lessons from specs 470–480):** anchor the working directory in every command; `git add` only the files of the step
(the two foreign C# edits are never staged); `prettier --write` every generated file; **a counterfactual must fail for the right
reason: read the message, not the count**; stage a new untracked file before its counterfactuals; scripts are files in the scratchpad
(never `node -e` with `/tmp` paths); grep for other uses before removing an import; **one vitest at a time**, batches of ten;
**restore the file after every break before anything else runs**; every spec rewrites `.specify/feature.json`, keep the branch's value
on a rebase.

## Phase 1 — Record the "before" (T-A)

- [x] **T001 [US1]** (frontend) On the current tree record into the scratchpad: (a) the names and status of every test in the token
  tests above (`file :: full name :: status`); (b) line counts of the touched files (`LayoutGrid.tsx`, `LayoutEditorDialog.tsx`,
  `OverlayDraftForm.tsx`, `CameraDetailPage.tsx`, the three `useLatestAccessToken.ts`) and their total; (c) the four token-ref
  `eslint-disable` lines (`react-hooks/refs`), by search of the four apps; (d) `accessTokenRef.current ?? null` occurrences in non-test
  code (expect 4); (e) suite counts and durations for `shared`, `kiosk-web`, `management-web`, `management-cameras`. Depends on nothing.

## Phase 2 — Probe first, then pin (US1)

### User story 1 (P1) — every caller behaves exactly as before, on the same budget (FR-001, FR-004, FR-008, SC-004)

- [x] **T002 [US1]** (test-adversary) Write `probe481.cjs` and `run481.cjs` in the scratchpad (pattern of `probe480.cjs`/`run480.cjs`):
  named, exactly-once breaks of each concern in **each of the four copies**: (A) identity: dependency array `[]` → `[accessToken]` (or
  the token value), (B) identity: drop `useCallback` so a new function each render, (C) newest-at-call: assign the ref once (only in
  `useRef(initial)`, no per-render assignment), (D) `?? null` → `?? ''` and → returns `undefined`, (E) first-render value: initial ref
  `undefined`, (F) `Promise.resolve` → rejected / not a promise (sync return). Each asserts its pattern matches once, applies it, runs
  the site's token tests, records which test failed and its message or "none", and restores from git (`git diff` empty). Batches of
  ten. For each "none": new test, **double break** when another term masks it, or **equivalent** with the reason. Depends on T001.
- [x] **T003 [US1]** (test-writer) Write the **site-level** gap tests for every "none" in new files beside each site's existing token test
  (e.g. `LayoutGridToken.test.tsx`, `LayoutEditorDialogTokenGaps.test.tsx`, `OverlayDraftFormGetTokenGaps.test.tsx`,
  `CameraDetailPageToken.test.tsx`; names decided by the matrix). Run from the owning app: **green on today's code** (quote the
  output). Depends on T002.
- [x] **T004 [US1]** (test-adversary) For each new test, re-apply its break and **read and quote the failure message**; restore (`git diff`
  empty). Write the double-break and equivalence tables. Commit `test(481): pin the access-token getter behaviours the suites
  missed`. Depends on T003.

**Gate:** tests green, new tests observed red under their breaks. Stop and ask.

## Phase 3 — Add the shared hook (US2)

- [x] **T005 [US2]** (frontend) Add `apps/shared/src/hooks/useLatestAccessToken.ts` (plan D1–D3: token-value parameter, one consolidated
  comment, the one `react-hooks/refs` suppression with its justification) and `useLatestAccessToken.test.tsx` (identity across
  re-renders and a renew; yields the new token at call time; `undefined` → `null`; first-render value), plus the barrel export in
  `hooks/index.ts`. Stage the new files, then counterfactual each concern in the **shared** hook (the new unit test fails; message read).
  `prettier --write`; shared lint/typecheck. Commit `feat(shared): useLatestAccessToken` with `§IV legs touched: none (unused)`.
  Depends on T004.

## Phase 4 — Switch the four sites, one per commit (US2) (FR-003, FR-006, FR-008)

- [x] **T006 [US2]** (frontend) Kiosk `LayoutGrid.tsx` imports the hook from `@smart-sentinel-eye/shared/hooks`; delete
  `layoutGrid/useLatestAccessToken.ts`. Check D2 (barrel resolves in kiosk-web; `tsc`, `eslint`, vitest clean; subpath export if not).
  Re-run, unchanged: `LayoutGridIdentity`, the `LayoutGrid*` and `CellPage*` families, `WallPage.test`. Apply the matching break to the
  shared hook: the same kiosk test fails. Commit with `§IV legs touched: overlay draw, decode sampler, composite + render; unchanged`.
  Depends on T005.
- [x] **T007 [US2]** (frontend) `LayoutEditorDialog.tsx` switches (`useLatestAccessToken(auth?.user?.access_token)` with its own `useAuth()`
  and `auth?.` kept); delete `layoutEditor/useLatestAccessToken.ts`. `LayoutEditorDialogToken` and the dialog family unchanged; matching
  break on the shared hook fails the same test. Commit `§IV legs touched: token handed to the overlay editor/WHEP session; unchanged`.
  Depends on T006.
- [x] **T008 [US2]** (frontend) `OverlayDraftForm.tsx` switches the same way; delete `overlayDraftForm/useLatestAccessToken.ts`.
  `OverlayDraftFormGetToken` and `OverlayDraftForm*` unchanged; matching break fails the same test. Commit as T007. Depends on T007.
- [ ] **T009 [US2]** (frontend) `CameraDetailPage.tsx` replaces its inline ref and `useCallback` with the hook (`auth.user?.access_token`, no
  `auth?.`, as today); drop now-unused imports (`useCallback`, `useRef` only if no other use: grep first); move any reason local to
  the page into a one-line pointer. `CameraDetailPage*`, `CameraDetailViewer`, `CameraViewer*` unchanged; matching break fails the page
  identity test. Commit `§IV legs touched: decode sampler; unchanged`. Depends on T008.

**Gate (T-D1, brief):** report the measured deltas (lines, suppressions 4 → 1, `?? null` occurrences 4 → 1). A site that could not
switch without an edited assertion is a **block**, reported, not adjusted. Stop and ask.

## Phase 5 — Verify (US1, US2)

- [ ] **T010** (frontend) Write `verification.md`: matrix of probes and dispositions, per-commit §IV statements, size and suppression deltas,
  existing-test `git diff` empty, suite counts and durations before/after for the four apps, `eslint --max-warnings 0`, `tsc --noEmit`,
  `prettier --check` for the four apps, `Architecture.Tests` (they scan `.tsx`). Tick T001–T010. Depends on T009.
- [ ] **T011** (frontend) Final grep: `accessTokenRef.current ?? null` finds one non-test hit; token-ref `eslint-disable` count is one;
  no copy remains. Depends on T010.

## Phase 6 — QA and PR

- [ ] **T012** `/code-review medium`; dispositions recorded in `verification.md`. **T013** open the PR to `develop` with the phase 4a
  evidence, the per-commit §IV statements and the measured figures; park it (do not wait on CI); merge on green with the run's head
  SHA equal to the PR tip; close-out comment on #3222.

## Dependencies

T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010 → T011 → T012 → T013. No parallel tasks (one branch, one index).
