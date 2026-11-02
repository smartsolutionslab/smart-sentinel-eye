# Spec 479 — `CameraViewer` is broken up by responsibility

**Feature Branch**: `479-camera-viewer-split`
**Created**: 2026-11-02
**Status**: Draft
**Issue:** #3194 (feature-level; found by the frontend refactoring survey; sibling of #3190–#3193, #3198).
**ADRs:** ADR-0037 (phases), ADR-0084 (frontend 50-LOC-per-function row: recorded there as **not yet enforced**),
ADR-0122 (browser measurements enter through a service), ADR-0128 (playout alignment without PTP), ADR-0169 (composites stay
Redux-free), ADR-0180 (a sampler failure is a measurement), ADR-0139 / ADR-0144 (phase 4a).
**No new ADR.** A decomposition inside existing conventions; follows specs 476–478, whose probe-first method it reuses.
**Latency budget (constitution §IV): this component is ON the path, so the claim is made per leg, not waved through.**
`CameraViewer` hosts three observers/controllers of §IV legs: the **SFU → kiosk decode** leg (the `receive_to_decoded`
sampler, a *fragment* of the leg per ADR-0122), the **presentation buffer** leg (the lag sampler, which reports
`presentation_buffer` and feeds the wall's alignment controller, ADR-0128), and the playout-target write that *applies* that
controller's decision. The split is a move of code between files and hooks. It must add **no work per frame, per render or
per tick**, change no timer cadence (5 s decode, 2 s lag), change no effect dependency array, and add no state or re-render
(a ref stays a ref). Every extraction commit states which legs it touches and that they are unchanged (FR-010).
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are the existing
`CameraViewer*.test.tsx` family (9 files, ~3,300 lines beside `CameraViewer.test`); they must pass with every assertion
**unmodified**. An assertion that has to be edited is evidence the behaviour moved: block, do not adjust (CLAUDE.md).

## Problem

`apps/shared/src/ui/composites/CameraViewer.tsx` is **661 lines**; one component, `CameraViewer`, is **352** (lines 137–488),
against ADR-0084's 50 LOC per function. Measured on `develop` (2026-11-02), not only grepped:

- **13 hook calls** in the file, as the issue says (my first grep counted 12: the regex did not allow the nested generic in
  `useRef<Set<string>>(…)`): 1 `useWhepSession`, 6 `useRef`, 2 `useCallback`, 4 `useEffect`.
- **The component is 352 lines, not the issue's "about 415"**; the file is 661 as stated. The module also already holds the
  pure helpers (`statusInfoFor`, `announcementFor`, `hintFor`, `labelFor`, `countReportableFailure`, `reasonFrom`) and three
  sub-components (`ViewerOverlay`, `OverlayLabel`, `OverlayShape`) at module scope. So the *component* is mostly effects
  and the refs that feed them, and a large share of the file is comments recording real failures.
- **No `eslint-disable`** in the file.
- **The covering family is 10 files, 3,616 lines** (`CameraViewer.test` 336, `…Alignment` 859, `…Announcement` 306,
  `…Media` 476, `…MixedElementsCharacterisation` 99, `…NoStoreCharacterisation` 24, `…OverlayTone` 218, `…Refusal` 528,
  `…ReportCadence` 323, `…SamplerWindow` 447). The issue named nine; the tenth, `CameraViewer.test.tsx`, also covers it.
- **Exports that other apps import and must not move:** `CameraViewer`, `CameraViewerProps`, `CameraViewerStatus`,
  `CameraViewerOverlay`, `CameraViewerOverlayLabel`/`Shape` types, and **`OverlayLabel` and `OverlayShape`** (components,
  imported by `management-web`'s `TileFrame`). Callers: kiosk `CameraTile` and `LayoutGrid`, `management-cameras`'
  `CameraDetailViewer`, `management-web`'s `tileView/*`.
- Concerns interleaved in the component, each with comments recording a real failure:

  | # | Concern | Where (lines) |
  |---|---|---|
  | A | The WHEP session (`useWhepSession`) | 151–160 (already a hook; stays) |
  | B | "Report a missing stats field once per mounted tile": the shared ref and `reportMissingStatsField` | 162–199 |
  | C | "A sampler that throws says so": the two failure counters, the per-tile report throttle, `reportSamplerFailure` | 201–249 |
  | D | The decode sampler effect (`receive_to_decoded`) | 251–299 |
  | E | The lag sampler effect (`presentation_buffer`, wall alignment) with its callback ref and `sampleLag` | 301–385 |
  | F | The playout-target effect with `reportedNoPlayoutRef` | 387–438 |
  | G | Status derivation (`statusInfoFor`) and the announcement/overlay text | 440–446, 490–587 |
  | H | The overlay elements (`OverlayLabel`, `OverlayShape`) | 589–626 |
  | I | The sampler failure helpers (`countReportableFailure`, `reasonFrom`) | 628–651 |

### What the issue does not settle (premise check, 2026-11-02)

1. **A decomposition, not a deduplication: the line total will not fall.** Expect the file total to **rise**, as in 476
   (+42 %), 477 (+26 %), 478 (+16 %). The value is a component that reads as a list of what it composes; ADR-0084's limit is not
   enforced, so no failing gate is being fixed. A measure-and-ask step follows the first extractions, and the earlier specs
   showed the first moves of **module-level** code barely shrink the component (478: −6 %), so the order matters.
2. **This is the hot path.** A 250-tile wall mounts this component 250 times; each instance owns two intervals while live.
   Anything that makes a unit re-created, re-subscribed or re-rendered more often than today is a regression the family may not
   notice (it asserts behaviour, not render counts). The plan must therefore **probe the tick/effect behaviour** and may add a
   characterisation test that pins timer counts (an interval is created once per `status`/`cameraIdentifier`/`getToken` change,
   not per render) before moving the samplers.
3. **Two scope facts are deliberate and load-bearing, and their comments are evidence:** the missing-field ref is **one per
   mounted tile, shared by both samplers**, and **not** reset on a camera swap; the two sampler-failure counters are
   **separate**, and also not reset on a camera swap. A hook that owns "its own" ref per sampler, or one that resets on
   `cameraIdentifier`, silently changes both (the first was #2084/#2189's firehose, the second its inverse).
4. **The throttle is created once, lazily, in render** (`reportThrottleRef.current === undefined`), shared by both legs. It
   must stay created once per mounted tile and shared; it is not a candidate for `useMemo`/`useState` initialisation without
   the same observable behaviour.
5. **The lag sampler's callback goes through a ref updated in an effect with no dependency array** (`useEffect(() => {
   onLagMeasuredRef.current = onLagMeasured; })`): the comment records the failure that this prevents (a wall that never
   aligns, issue 1889). It must run every render, after commit, exactly as now.
6. **The exports stay at the same path.** `OverlayLabel`/`OverlayShape` and the types are re-exported from `CameraViewer.tsx` if
   they move, so no caller changes.

## Scope

**In:** `CameraViewer.tsx`: its concerns moved into modules **next to the file** in
`apps/shared/src/ui/composites/` (a folder `cameraViewer/` mirroring `overlayEditor/`, `overlayDraftForm/`,
`layoutEditor/`), one extraction per commit, each green on its own (bisect).

**Out (explicitly):**
- Any behaviour change, including "obvious" fixes found on the way (two issues, per CLAUDE.md).
- `useWhepSession`, `WhepClient`, `kioskLatency`, `wallAlignment`, `resilienceLog`, `overlayLabelStyle` (already separate); every
  caller; the props interface.
- New abstractions beyond what the split needs; unifying `countReportableFailure` with `CellPage`'s copy (spec 140: "extract
  it at the third site"); changing either sampler cadence.

## User Scenarios & Testing

### User Story 1 (P1) — The viewer behaves exactly as before, on the same budget

As an operator and as the kiosk wall, I see the same video, status text and overlays, the same samples are taken at the same
cadence and reported at the same counts, and the wall aligns the same way. As the maintainer I can prove it.

**Independent test:** the ten covering test files pass with no line edited.

```gherkin
Scenario: the existing tests are the safety net
  Given the CameraViewer*.test.tsx family
  When they are run after each extraction
  Then every test passes
  And git diff shows no line of those test files changed

Scenario: the samplers keep their cadence and their windows
  Given a live tile
  When time advances
  Then one decode sample is taken every 5 s and one lag sample every 2 s (only when onLagMeasured is given)
  And a thrown tick closes its window, as before

Scenario: a missing stats field is reported once per mounted tile, by either sampler
  Given a receiver that omits a stat both samplers read
  When both samplers run repeatedly, across reconnects and a camera swap
  Then exactly one line is logged for that field

Scenario: a sampler failure is reported at the decade counts, separately per sampler
  Given a decode sampler and a lag sampler that keep throwing
  When they fail 1, 10, 100 times
  Then each reports at those counts independently, and the counters survive a camera swap
```

### User Story 2 (P2) — The component is short enough to read

```gherkin
Scenario: the component is short
  When the split is done
  Then CameraViewer is well under its former 352 lines
  And each extracted unit is under 50 code lines where the logic allows (the issue's own bar)
```

### Edge cases

- **One ref, two readers.** The missing-field ref is shared; units that read it receive it (or the callback that closes over
  it), they do not each create one.
- **Callback identity.** `reportMissingStatsField` depends on `[cameraIdentifier]`; `reportSamplerFailure` on
  `[cameraIdentifier, getToken]`; both feed the sampler effects' dependency arrays, so a changed identity re-creates the
  interval. Copied verbatim.
- **`stats` identity.** The effects depend on `stats` from `useWhepSession`; the hooks take it as an input, unchanged.
- **Status region always mounted.** The `<p role="status">` is never conditional (#2346); the JSX order of video → overlays →
  status → `ViewerOverlay` is unchanged.
- **Comments are evidence, not decoration.** Each records a real failure and moves with its code, once.
- **A first extraction that does not help is a stop sign** (#3190, #3198, #3193, #3192, #3191): measure, then ask.

## Requirements

- **FR-001** Every test in the ten covering files passes after each commit; none of those files is modified.
- **FR-002** `CameraViewer` composes extracted modules for the concerns B–I (A stays); its own body is the composition and the
  JSX that remains.
- **FR-003** No change to what is logged, reported or sampled: same log events and fields, same `reportKioskLatency` calls and
  arguments, same intervals (5 s / 2 s), same dependency arrays, same ref scopes (per mounted tile; shared vs separate as
  today).
- **FR-004** The exports and props of `CameraViewer.tsx` are unchanged (`OverlayLabel` and `OverlayShape` included); no caller
  changes.
- **FR-005** No `eslint-disable` is added (the file has none).
- **FR-006** Comments move with their code; none is dropped, none duplicated; any "above"/"below" pointer is rewritten to name
  the new unit.
- **FR-007** Each commit builds and passes the family on its own; one concern per commit.
- **FR-008** No change outside `apps/shared/src/ui/composites/` (new files and the viewer), `specs/479-…` and
  `.specify/feature.json`.
- **FR-009** `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` are clean for `shared`, `kiosk-web`,
  `management-web` and `management-cameras` (the callers), and `Architecture.Tests` pass (they scan `.tsx`).
- **FR-010** **Latency (§IV).** Each extraction commit states which legs it touches (decode fragment, presentation buffer,
  playout application, none) and that they are unchanged; no new timer, subscription, state or render-time computation is
  introduced; the number of intervals created for a live tile is pinned by a test (see plan) and equal before and after.

## Success Criteria

- **SC-001** The ten covering files: same test count and names before and after, all passing, `git diff` on them empty.
- **SC-002** `CameraViewer`'s own length and each new unit's length, reported before/after **with the file total**, code lines
  and comment lines separately.
- **SC-003** A search of the viewer and its new units for `eslint-disable` finds none.
- **SC-004** The `shared` and `kiosk-web` suites (and the three other callers' tests) pass, with counts and durations before
  and after.
- **SC-005** For every concern (B–I), a deliberate break is noticed by a named test; where the family notices nothing, a new
  characterisation test is written first, green on the unsplit file, and proved able to fail (the message read). Where a
  behaviour is defended jointly or is equivalent, that is recorded with evidence.
- **SC-006** The timer counts of a live tile (decode interval, lag interval with and without `onLagMeasured`) are the same
  before and after, shown by a test, not asserted by argument.

## Assumptions

- The family is strong on the sampler windows, the report cadence and the alignment (they are the subject of the three
  largest files); the probes decide where it is not, and the likeliest gaps are the shared-ref scope, the throttle sharing and
  the no-dependency-array callback ref.
- Extracting custom hooks preserves behaviour as long as they are called unconditionally, in the same order, with the same
  dependency arrays.

## Dependencies

Independent of #3219 (spec 478) and the earlier specs; follows their method. #3195 (`LayoutGrid`/`Tile`, also kiosk) is a
separate issue and not started.
