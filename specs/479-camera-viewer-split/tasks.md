# Tasks 479 — `CameraViewer` is broken up by responsibility

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3194 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation.** The ten covering files stay unmodified
(`git diff` on them empty). Anything the probes (T002) show the family does not notice, and the timer counts (SC-006), get tests
in **new** files, written first and **green on the unsplit file** (T003), with their failure messages read (T004).
**Latency (§IV): this component is on the kiosk path.** Every extraction commit message and the PR name the legs it touches (decode
fragment, presentation buffer, playout application, or none) and state them unchanged (FR-010).

One PR. The viewer and its family live in `apps/shared` (run Vitest from `apps/shared`); the callers' tests run from
`apps/kiosk-web`, `apps/management-cameras`, `apps/management-web`. Scripts, from the repo root: `npm run -w <app> test | lint |
typecheck`, `npm run format:check`. Scratchpad files are not committed.
**Commit hygiene (lessons from specs 470–478):** anchor the working directory in every command and print `pwd`; `git add` only
the files of the step (a foreign edit in the tree is never staged); `prettier --write` every generated file before committing;
**a counterfactual must fail for the right reason: read the failure message, not the count**; stage a **new untracked unit**
before its counterfactuals (`git checkout` cannot restore it); node scripts are files in the scratchpad (not `node -e` with
`/tmp` paths); count hook calls with a regex that allows **nested** generics; grep for other uses before removing an import;
rewrite every "above"/"below" pointer when the code it points at moves; **one vitest at a time**, long runs as a direct
background command, batches of ten; **restore the file after every break before anything else runs**; every spec rewrites
`.specify/feature.json`, keep the branch's value on a rebase.

## Phase 1 — Record the "before" (T-A)

- [ ] **T001 [US1]** (frontend) On the unsplit tree record into the scratchpad, for the 10 covering files (`CameraViewer.test`,
  `…Alignment`, `…Announcement`, `…Media`, `…MixedElementsCharacterisation`, `…NoStoreCharacterisation`, `…OverlayTone`,
  `…Refusal`, `…ReportCadence`, `…SamplerWindow`): (a) vitest JSON → sorted `file :: full name :: status` (`names471.cjs`); (b)
  sorted assertion lines; (c) line counts of `CameraViewer.tsx` (661) and the component (352), code and comment lines apart;
  (d) hook calls in the file, nested-generic-aware (expect 13); (e) `eslint-disable` (expect none); (f) the `shared` suite and
  the callers' suites (`kiosk-web`, `management-cameras`, `management-web` `tileView`): counts and durations. Depends on nothing.

## Phase 2 — Probe first, then pin (US1)

### User story 1 (P1) — the viewer behaves exactly as before, on the same budget (FR-001, FR-003, FR-010, SC-005, SC-006)

- [ ] **T002 [US1]** (test-adversary) Write `probe479.cjs` and `run479.cjs` in the scratchpad (pattern of `probe478.cjs` /
  `run478.cjs`): the named breaks of plan §R2 (B–H), each asserting its pattern matches **exactly once**, applying it to the
  unsplit `CameraViewer.tsx`, running the family from `apps/shared`, recording which test failed and its message or "none", and
  restoring from git (`git diff` empty after each). Run in batches of ten, one vitest at a time. Record the matrix; for each
  "none" decide: new test, **double break** when another term masks it, or **equivalent** with the reason. Depends on T001.
- [ ] **T003 [US1]** (test-writer) Create `CameraViewerTimers.test.tsx` (plan §R3: interval count and delays for a live tile with
  and without `onLagMeasured`; a re-render with a new inline callback creates none; leaving `live` clears both; a
  `cameraIdentifier` change recreates them) **first**, and tests in further **new files** for every concern the matrix leaves
  unnoticed (reporting scopes, throttle, the lag callback ref, playout outcomes — as T002 names). Run from `apps/shared`:
  **green on the unsplit file** (quote the output). Depends on T002.
- [ ] **T004 [US1]** (test-adversary) For each new test, re-apply its break and **read and quote the failure message** (right
  reason, not just red); restore (`git diff` empty). Write the double-break and equivalence tables. Commit
  `test(479): pin the CameraViewer behaviours the family missed`. Depends on T003.

## Phase 3 — One extraction per commit (US1, US2)

### User story 2 (P2) — the component reads as a list of what it composes (FR-002, FR-005, FR-006, FR-007, FR-010)

Each task: move only what plan §R4 names, **at the position of the code it replaces**; hook and effect order as plan §R5;
dependency arrays copied verbatim; comments move with their code once; after the move apply the matching T002 break to the
**new unit** and see the same test fail there; run the 10 covering files + the new gap files (including `CameraViewerTimers`) from
`apps/shared`, `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` (before committing); `git diff --stat` shows only
the viewer and its new unit(s); `git add` those only; commit `refactor(479): <unit>` with a body line **"§IV legs touched: …;
unchanged."** Fixed order, one branch, one index. Each depends on the previous.

- [ ] **T005 [US2]** `cameraViewer/samplerFailure.ts` (concern I): `countReportableFailure`, `reasonFrom`. Legs: none.
- [ ] **T006 [US2]** `cameraViewer/viewerStatus.ts` (concern G): `statusInfoFor`, `announcementFor`, `hintFor`, `labelFor`. Legs: none.
- [ ] **T007 [US2]** `cameraViewer/overlayTypes.ts` + `cameraViewer/overlayElements.tsx` (concern H): the overlay types move to a
  neutral file (no cycle) and `OverlayLabel`/`OverlayShape` follow; `CameraViewer.tsx` re-exports all of them. Legs: none.
- [ ] **T008 [US2]** `cameraViewer/ViewerOverlay.tsx` (concern G): the `ViewerOverlay` component. Legs: none.
- [ ] **T009 (decision gate T-D1)** (orchestrator) **Measure, then ask.** Component lines, `CameraViewer.tsx` lines, hook calls
  left, the units' sizes and the **file total** against T001; extrapolate to the four hooks using plan §R4. Report to the
  maintainer **before** T010 and ask whether to continue. Depends on T008.
- [ ] **T010 [US2]** `cameraViewer/useSamplerReporting.ts` (concerns B, C): the shared missing-field ref and
  `reportMissingStatsField`, the two counter refs, the lazily created throttle, `reportSamplerFailure`; returns them as one
  `reporting` bag. Legs: **decode fragment and presentation buffer (their reporting plumbing)**; unchanged.
- [ ] **T011 [US2]** `cameraViewer/useDecodeSampler.ts` (concern D): the decode interval effect and `DECODE_SAMPLE_INTERVAL_MS`;
  members of `reporting` (not the bag) in the dependency array. Legs: **SFU → kiosk decode fragment**; unchanged.
- [ ] **T012 [US2]** `cameraViewer/useLagSampler.ts` (concern E): `onLagMeasuredRef` and its every-render assignment effect,
  `sampleLag`, the lag interval effect and `LAG_SAMPLE_INTERVAL_MS`. Legs: **presentation buffer + wall alignment feed**; unchanged.
  **Stop and ask** past ~8 inputs.
- [ ] **T013 [US2]** `cameraViewer/usePlayoutTarget.ts` (concern F): `reportedNoPlayoutRef` and the apply/report effect. Legs:
  **playout-target application**; unchanged.

## Phase 4 — Gates and PR

- [ ] **T014** (frontend) **T-A after:** repeat T001's records. (a) names+status `diff` empty for the 10 covering files (new gap files
  listed separately); (b) assertion lines `diff` empty; `git diff --stat origin/develop...HEAD` on the family is empty (FR-001);
  (c) `CameraViewer.tsx`, the component, every unit (code and comment lines apart) and the **file total** before/after (SC-002);
  (d) no `eslint-disable` (SC-003); (e) distinctive comment phrases once, no stale "above"/"below" pointers; (f) the `shared`
  suite and the callers' suites, counts and durations before/after (SC-004); (g) the `CameraViewerTimers` figures equal before and
  after (SC-006); (h) the per-unit counterfactuals recorded. Depends on T013.
- [ ] **T015** (frontend) `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` for `shared`, `kiosk-web`,
  `management-cameras`, `management-web`; `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`). Depends on T014.
- [ ] **T016** (frontend) `specs/479-camera-viewer-split/verification.md` (probe matrix, gap tests with quoted messages, double-break
  and equivalence tables, timer counts, sizes, **the per-commit §IV leg statements**, what was not extracted and why); tick
  T001–T017. Depends on T015.
- [ ] **T017** (orchestrator) Phase 6 `/code-review medium`, findings fixed or accepted in writing; Phase 7 PR to `develop`
  (`--base develop`, `Closes #3194`, Phase 4a characterisation stated, **a §IV section naming each leg touched and its
  evidence**, sizes as measured, gaps and slips visible); park; merge when CI concludes success on the PR tip; close-out comment
  on #3194. Depends on T016.

## Dependencies

T001 → T002 → T003 → T004 → T005 → … → T009 (**gate**) → T010 → … → T013 → T014 → T015 → T016 → T017. Strictly linear: one branch,
one index, one concern per commit. No parallel tasks.

## Implementation strategy

MVP is T005–T008 plus the gate: four pure/presentational units that touch no latency leg. They are expected to shrink the
component little (478: −6 %); the real reduction is T010–T013, which touch the legs and so come only after the gate and with the
timer-count test already green. If T009 shows the decomposition is not earning its keep, stop there and ask.
