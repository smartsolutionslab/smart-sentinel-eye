# Spec 489 — Bump `jsdom` 30.1.3 → 31.0.0

**Feature Branch**: `489-jsdom-31`
**Created**: 2026-11-10
**Status**: Draft
**Issue:** #3240 (feature-level).
**ADRs:** ADR-0036 (smallest change), ADR-0139 / ADR-0144 (phase 4a), ADR-0037 (phases).
**No new ADR.** A test-environment version bump inside an already-decided stack.
**Latency budget (constitution §IV): NOT on the path.** `jsdom` is a dev dependency that runs tests; it ships in no bundle. No source
file changes, so no leg between event arrival and overlay render is touched. The PR says so.
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering suites are captured passing on the lockfile-true
unmodified tree first, and must pass **unmodified** after. An assertion that has to be edited is evidence that jsdom's behaviour moved:
block, do not adjust.

## Problem

`jsdom` is pinned at `30.1.3` in all five frontend apps; `31.0.0` (2026-11-05) is `latest`. Unlike the `@hookform/resolvers` bump (#3239),
this one can plausibly move test results, because 31.0.0 replaces parts of jsdom that tests observe indirectly.

**Premise, checked against the release notes and the tree (2026-11-10).** Three breaking changes:

1. **A new integrated CSS engine** for parsing, selector matching and computed styles. "CSS serialization and computed values can differ
   from previous releases." The exposure is **test-side and indirect**: production code makes no `getComputedStyle` call
   (`textLegibility.ts` only names it in a comment). jest-dom's `toBeVisible` / `toHaveStyle` and user-event's `pointer-events` check
   read computed style: 13 test files use `toBeVisible` / `toHaveStyle`, 78 use `userEvent`.
2. **The canvas backend** changes from `canvas` to `@napi-rs/canvas`. Neither package is a dependency here, so canvas-touching tests
   (`FrameCapture.test.tsx`, `OverlayEditorBackdrop.test.tsx`, `OverlayDraftForm.test.tsx`) run against jsdom's stubs today.
3. **HTML/XML parsing pauses at blocking scripts**, so a `JSDOM` constructor can return a partially parsed document. Vitest constructs
   the document, and no source calls `document.write`.

Engines are unchanged (`^22.22.2 || ^24.15.0 || >=26`).

**Which apps run jsdom.** `management-web` (102 test files), `management-cameras` (24), `kiosk-web` (39) and `management-event-ingestion` (4)
set `environment: 'jsdom'`. `apps/shared` defaults to `node` but **62 of its 92 test files opt in** per file with
`// @vitest-environment jsdom`. All five apps are affected.

The expected diff is five manifests and the lockfile, with no source change. That is the expectation, not a guarantee: the proof is the
suites.

## Scope

**In:** the five `apps/*/package.json` pins and `pnpm-lock.yaml`.

**Out (explicitly):** any source or test file; adding `@napi-rs/canvas` or `canvas`; `@microsoft/signalr`; `@hookform/resolvers`
(#3239); every other dependency, including patch and minor drift Dependabot owns.

## User Scenarios & Testing

### User Story 1 (P1) — the test environment moves and the tests do not

```gherkin
Scenario: the suites run on the new jsdom
  Given the five apps on jsdom 31.0.0 and no test or source file edited
  When each app's full suite runs
  Then every app passes with the same file and test counts as before the bump

Scenario: a jsdom behaviour change surfaces
  Given a test that fails only on jsdom 31
  When it is investigated
  Then it is classified as a jsdom behaviour change and reported, and no assertion is adjusted to hide it
```

### User Story 2 (P1) — nothing slipped away quietly

```gherkin
Scenario: no test is silently skipped
  Given the baseline's skipped-test count per app
  When the suites run on jsdom 31
  Then the skipped count is unchanged

Scenario: no new environment noise
  Given the baseline's virtual-console errors and warnings (for example CSS parse errors)
  When the suites run on jsdom 31
  Then any new message is read and recorded, not ignored
```

## Edge Cases

- **A flaky test that predates the bump** fails during the after-run. Re-run that file alone once, saving the failure first; a failure
  that reproduces on jsdom 31 and not on 30 is the bump's. #2641 showed the singleton-store-plus-fake-timers shape is timing-sensitive,
  so the baseline is taken **twice** to know what normal looks like.
- **Computed-style assertions that pass for the wrong reason.** A matcher that was vacuous on jsdom 30 stays vacuous on 31, and the
  characterisation cannot see it. Out of scope; the probe in FR-006 samples the visibility matchers to show they are live.
- **Canvas stubs.** If a canvas test changes outcome because the backend changed, that is a jsdom behaviour change under User Story 1,
  not licence to install `@napi-rs/canvas`.
- **A second jsdom in the lockfile**, for example one pulled by another package at a different version: checked in the diff.
- **Node version.** The local Node must satisfy the engines range; recorded in the note.

## Requirements

- **FR-001** Only the five app manifests and `pnpm-lock.yaml` change. If a source or test file has to change, stop and report: that is
  evidence the premise was wrong.
- **FR-002** All five apps pin `jsdom` at the same exact version.
- **FR-003** Baseline: each of the five apps' full vitest suites passes on the lockfile-true unmodified tree, **twice**, with files,
  tests, skipped count and the count of stderr/virtual-console lines recorded per app. The installed `jsdom` version is read from
  `node_modules`, not the manifest.
- **FR-004** After the bump, the same suites pass with **identical file, test and skipped counts**, and no test file is in the diff.
- **FR-005** New stderr/virtual-console messages versus baseline are listed and read in `verification.md`; none is assumed harmless.
- **FR-006** The visibility and style matchers are shown live: a small sample of the 13 `toBeVisible` / `toHaveStyle` files is probed
  once each, by exactly-once substitution of an input that must make the assertion fail (for example hiding the element), expecting at
  least one failure, on the bumped tree. A sample that cannot fail is recorded as a finding, not fixed here.
- **FR-007** `eslint --max-warnings 0`, `tsc --noEmit` and `prettier --check` on the changed files are clean in all five apps.
- **FR-008** The lockfile diff changes `jsdom` and its own dependencies only, and no second copy of `jsdom` appears.
- **FR-009** The PR states that no §IV leg is touched, that jsdom ships in no bundle, and that `@microsoft/signalr` was left alone.

## Success Criteria

- **SC-001** File, test and skipped counts are identical per app before and after; zero test files in the diff.
- **SC-002** The diff against `origin/develop` is the five manifests and `pnpm-lock.yaml`.
- **SC-003** Every new stderr/virtual-console message is accounted for in `verification.md`.
- **SC-004** At least the sampled visibility/style probes fail on the bumped tree, or the exception is recorded.

## Assumptions

- `pnpm-lock.yaml` is the lockfile of record; the package manager is pnpm.
- The local machine's Node satisfies the engines range.
- Vitest's `jsdom` environment resolves the app's own `jsdom`, so the app-level pin is what runs.

## Dependencies

Independent of #3239 / spec 488 in content. Both rewrite `pnpm-lock.yaml` and touch the manifests, so whichever merges second rebases
and re-resolves with `pnpm install --lockfile-only` rather than hand-merging.
