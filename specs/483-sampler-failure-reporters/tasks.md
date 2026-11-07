# Tasks 483 — The samplers report failures through two reporters, not raw counters

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3224 (feature-level; on Project #13).
Engineer: **frontend**. Phase 4a colour: **behaviour-preserving → characterisation.** The existing tests stay unmodified (`git diff`
on them empty): `CameraViewerReporting`, `CameraViewerSamplerWindow`, `CameraViewerAlignment`, `CameraViewerTimers`,
`CameraViewerReportCadence` and the other `CameraViewer*` files in `apps/shared/src/ui/composites/`, and `observability/kioskLatency.test`.
Anything the probes (T002) show they do not notice gets a test in a **new** file, written first and **green on today's code** (T003),
with its failure message read (T004).
**Latency (§IV): on the kiosk path.** `useDecodeSampler` is the SFU → kiosk decode fragment (`receive_to_decoded`); `useLagSampler` is
the presentation buffer (`presentation_buffer`). Both effects list the reporting values in dependency arrays; a reporter that changed
identity per render would clear the sampler's interval before a second sample (issues 1888 / 1889). Every commit message and the PR
name the legs touched (SFU → kiosk decode, presentation buffer, or none) and state them unchanged (FR-009).

One PR. Run Vitest from `apps/shared`; scripts, from the repo root: `npm run format:check`; eslint and tsc directly in each app
(`eslint src --max-warnings 0`, `tsc --noEmit`; workspace names are scoped). Scratchpad files are not committed.
**Commit hygiene (lessons from specs 470–482):** anchor the working directory in every command; `git add` only the files of the step
(the two foreign C# edits are never staged); `prettier --write` every generated file; **a counterfactual must fail for the right
reason: read the message, not the count**; stage a new untracked file before its counterfactuals; scripts are files in the scratchpad
written with the Write tool (a bash heredoc turns `\n` into a real newline); **after a script run, check the results file is fresh,
not left over from an earlier run** (a syntax error in a probe script kept a stale file once); **one vitest at a time**, batches of
ten; **restore the file after every break before anything else runs**; every spec rewrites `.specify/feature.json`, keep the branch's
value on a rebase; `apps/shared` tests default to node (`// @vitest-environment jsdom` for DOM ones); a heavy job may be reaped for
low memory: report it, do not restart it unasked.

## Phase 1 — Record the "before" (T-A)

- [x] **T001 [US1]** (frontend) On the current tree record into the scratchpad: (a) the names and status of every test in the covering
      files above (`file :: full name :: status`); (b) line counts of `useSamplerReporting.ts`, `useDecodeSampler.ts`,
      `useLagSampler.ts` and their total; (c) occurrences of `reportSamplerFailure`, `decodeSampleFailuresRef` and
      `lagSampleFailuresRef` in non-test code (the greps for SC-002); (d) suite counts and durations for `shared`, plus `kiosk-web` and
      `management-cameras` (consumers of `CameraViewer`). Depends on nothing.

## Phase 2 — Probe first, then pin (US1)

### User story 1 (P1) — a sampler that throws reports exactly as before, on the same budget (FR-001, FR-003, FR-005, SC-003)

- [x] **T002 [US1]** (test-adversary) Write `probe483.cjs` and `run483.cjs` in the scratchpad (pattern of `probe482.cjs`): named,
      exactly-once breaks of each concern in `useSamplerReporting.ts`: (A) decode counter logs the lag transition; (B) lag counter logs the
      decode transition; (C) one counter for both samplers (`lagSampleFailuresRef` aliased to the decode ref); (D) counter reset on a camera
      swap (a `useEffect` or a key on `cameraIdentifier`); (E) counter created per call, not per mounted tile (`useRef` → a plain object each
      render); (F) the reportable-count rule bypassed (`count === null` check removed); (G) the sink report not given the shared
      throttle (`reportThrottleRef.current` → `undefined`); (H) `reportSamplerFailure`'s `useCallback` dropped (a new function per
      render); (I) its dependency array `[cameraIdentifier, getToken]` → `[]` (identity does not follow the camera); (J) `reportMissingStatsField`
      and `reportThrottleRef` left alone (not probed: out of scope). Each asserts its pattern matches once, applies it, runs the
      `CameraViewer*` tests, records which test failed and its message or "none", and restores from git (`git diff` empty). Batches of ten.
      For each "none": new test, **double break** when another term masks it, or **equivalent** with the reason. Depends on T001.
- [x] **T003 [US1]** (test-writer) Write the gap tests for every "none", in new files beside the existing ones (names decided by the matrix,
      e.g. `CameraViewerSamplerFailureReporting.test.tsx`): through the real `CameraViewer` with the existing fake-timer harness, assert the
      transition name and count in the console line per sampler, the counters apart, not reset on a camera swap, the throttle shared, and
      the effect's interval **not cleared on a plain re-render** and cleared on a camera or `getToken` change. Run from `apps/shared`:
      **green on today's code** (quote the output). Depends on T002.
- [x] **T004 [US1]** (test-adversary) For each new test, re-apply its break and **read and quote the failure message**; restore (`git diff`
      empty). Write the double-break and equivalence tables. Commit `test(483): pin the sampler failure-reporting behaviours the suites
missed`. Depends on T003.

**Gate:** tests green, new tests observed red under their breaks. Stop and ask.

## Phase 3 — Add, switch, remove (US2) (FR-002, FR-006, FR-007, FR-009)

- [x] **T005 [US2]** (frontend) `useSamplerReporting.ts`: add `reportDecodeFailure` and `reportLagFailure` (plan D1, `useCallback` over the
      private `reportSamplerFailure`, `[reportSamplerFailure]`), returned **in addition to** the old members (additive; nothing uses them
      yet); extend `SamplerReporting`. `prettier --write`; `tsc`, `eslint`. Commit `refactor(483): useSamplerReporting also offers a reporter
per sampler` with `§IV legs touched: none (unused)`. Depends on T004.
- [x] **T006 [US2]** (frontend) `useDecodeSampler.ts` destructures `reportDecodeFailure` instead of `reportSamplerFailure` and
      `decodeSampleFailuresRef`, calls `reportDecodeFailure(error)`, and swaps the two dependency-array entries for the one. Re-run, unchanged:
      `CameraViewerTimers`, `CameraViewerReporting`, `CameraViewerSamplerWindow`, `CameraViewerAlignment`. Apply the matching breaks
      (A, C, D, E, F, G, H, I) to the **shared** hook: the decode-sampler tests fail. Commit with `§IV legs touched: SFU → kiosk decode;
unchanged`. Depends on T005.
- [x] **T007 [US2]** (frontend) `useLagSampler.ts` switches the same way (`reportLagFailure`). Same re-runs and counterfactual. Commit with
      `§IV legs touched: presentation buffer; unchanged`. Depends on T006.
- [x] **T008 [US2]** (frontend) Remove `reportSamplerFailure`, `decodeSampleFailuresRef` and `lagSampleFailuresRef` from the returned shape and
      `SamplerReporting` (the callback and refs stay private inside the hook). Grep: SC-002 holds. All covering suites unchanged; the
      `kiosk-web` and `management-cameras` suites re-run. Commit `§IV legs touched: none`. Depends on T007.

**Gate (T-D1, brief):** report the measured deltas (lines, exposed members 5 → 4, counter refs outside the hook 2 → 0). A sampler that
could not switch without an edited assertion is a **block**, reported, not adjusted. Stop and ask.

## Phase 4 — Verify (US1, US2)

- [x] **T009** (frontend) Write `verification.md`: matrix of probes and dispositions, per-commit §IV statements, size deltas, existing-test `git
diff` empty, suite counts and durations before/after for `shared`, `kiosk-web`, `management-cameras`, `eslint src --max-warnings 0`,
      `tsc --noEmit`, `prettier --check`, `Architecture.Tests` (they scan `.tsx`; if the run is reaped for memory, say so and name the CI
      `backend` job as the evidence). Tick T001–T009. Depends on T008.
- [x] **T010** (frontend) Final grep: no `SampleFailuresRef` and no `reportSamplerFailure` outside `useSamplerReporting.ts`'s private body;
      both samplers call their own reporter. Depends on T009.

## Phase 5 — QA and PR

- [ ] **T011** `/code-review medium`; dispositions recorded in `verification.md`. **T012** open the PR to `develop` (`--base develop`) with
      the phase 4a evidence, the per-commit §IV statements and the measured figures; park it (do not wait on CI); merge on green with the
      run's head SHA equal to the PR tip; close-out comment on #3224 and check the issue closed (`Closes #3224`).

## Dependencies

T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010 → T011 → T012. No parallel tasks (one branch, one index).
