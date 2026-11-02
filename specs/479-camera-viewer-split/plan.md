# Implementation Plan: `CameraViewer` is broken up by responsibility

**Branch**: `479-camera-viewer-split` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/479-camera-viewer-split/spec.md`

## Summary

Move the concerns of the 352-line `CameraViewer` component (the refs and callbacks that report, the two samplers, the
playout-target effect) and the module-level helpers and sub-components into pure modules, small hooks and components **next to
the file**, one extraction per commit, each green on its own. **First probe, then pin, then move**: break each concern once in
the unsplit file, see whether the 3,616-line family notices, and write characterisation tests, in **new files**, for what it
does not. **This is the kiosk hot path**, so a new test pins the **timer counts** of a live tile (SC-006) before any sampler
moves. Behaviour-preserving, so **characterisation** (ADR-0139/0144 phase 4a). Reuses the method of specs 476–478.

**Sizing, stated before building.** A decomposition: the file total will rise (476: +42 %, 477: +26 %, 478: +16 %). The value is a
component that reads as a list of what it composes. Moving the **module-level** code first barely shrinks the component (478:
−6 %); here the component is mostly effects, so the real reduction comes from the three sampler/playout hooks, which are
therefore **not** left to the end by default but scheduled right after the pure moves, with a **measure-and-ask** step (T-D1)
between the two groups.

## Technical Context

- **Stack**: TypeScript, React 19.3, Vitest + Testing Library (fake timers), `apps/shared` (jsdom). Callers: `kiosk-web`
  (`CameraTile`, `LayoutGrid`), `management-cameras` (`CameraDetailViewer`), `management-web` (`tileView/*`).
- **Baseline (2026-11-02, `develop` after #3218):** `CameraViewer.tsx` 661 lines (component 352, 13 hook calls, 0
  `eslint-disable`); covering family 10 files / 3,616 lines.
- **Touches**: new files in `apps/shared/src/ui/composites/cameraViewer/`, the viewer, and new test files. No existing test
  file, no caller, no config.
- **Latency budget (§IV)**: the component hosts the decode-fragment sampler, the presentation-buffer sampler and the
  playout-target application. **Every extraction commit states the legs it touches and that they are unchanged** (FR-010); the
  PR repeats it per commit.

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green | Characterisation: the family unmodified, plus gap tests written first and seen green on the unsplit file. |
| §IV latency budget is sacred | No new timer, subscription, state or render-time work; intervals, cadences, dependency arrays and ref scopes copied verbatim; timer counts pinned by a test before and after. |
| ADR-0036 — smallest change, no speculative generality | Each unit has one caller (the point of a decomposition, stated); no shared sampler abstraction; `countReportableFailure` is **not** unified with `CellPage`'s copy (spec 140: "extract it at the third site"). |
| ADR-0122 / 0128 / 0180 | Unchanged: the same `reportKioskLatency` calls, the same `onLagMeasured` contract, the same `sampler_failed` counting. |
| ADR-0169 — shared composites stay Redux-free | Unchanged. |
| Lane may not weaken a gate | No test edited; no suppression added or needed. |
| No new ADR | Decomposition inside existing conventions. |

No violation.

## Research (decisions that shaped the plan)

### R1 — What pins what (read from the family's names, then probed)

| Concern | Visibly pinned by | Probe expectation |
|---|---|---|
| B missing-field once per tile | `…ReportCadence`, `…SamplerWindow` | probe sharing between samplers, the `null` guard, the set insert, the camera-swap non-reset |
| C failure counters, throttle | `…ReportCadence` (323) | probe: separate counters, the decade curve, the throttle created once, the `sampler_failed` sink call |
| D decode sampler | `…SamplerWindow` (447), `CameraViewer.test` | probe: interval length, the `previous` reset on a throw, the live guard, clearInterval |
| E lag sampler | `…Alignment` (859), `…SamplerWindow` | probe: interval length, the `sampleLag` guard, the callback ref, the `lag`/`buffered` null guards, `presentation_buffer` |
| F playout target | `…Alignment`, `…Media` | probe: the undefined/null guard, the try/catch mapping, report-once, which outcomes report |
| G status text | `…Announcement` (306), `…Refusal` (528), `…OverlayTone` (218) | probe: `failedRead`, labels, the idle-announces-nothing rule, tones |
| H overlay elements | `…MixedElementsCharacterisation`, `…OverlayTone` | probe: keys, percent geometry, `aria-hidden`, `pointerEvents` |

### R2 — Method: break, observe, then write what is missing

For each row, apply **one deliberate break** to the unmigrated viewer (scratch, not committed), run the whole family, record
which test failed and why, or "none". Planned probes (a script of named breaks, each asserted to match exactly once, restored
from git after; **one vitest at a time**, batches of ten, direct background runs only):

- **B** the shared ref split in two (a second `Set`); the `field === null` guard dropped; the `.add(field)` dropped; the
  `cameraIdentifier` dropped from the log detail; the callback's `[cameraIdentifier]` dependency dropped.
- **C** one counter for both samplers; the decade rule (`decade === count`) loosened to `count % 10 === 0`; the throttle created
  on every render (`if (… === undefined)` removed); `reportKioskLatency('sampler_failed', …)` dropped; the `logResilienceEvent`
  dropped; `reasonFrom`'s non-Error fallback; `getToken` dropped from the callback's dependencies.
- **D** `DECODE_SAMPLE_INTERVAL_MS` changed; the `status !== 'live'` guard dropped; `previous = null` in the `.catch` dropped;
  `previous = current` dropped; the `elapsed !== null` guard dropped; the missing-field report in the `null` branch dropped;
  `clearInterval` dropped from the cleanup; `cameraIdentifier`/`getToken`/`reportSamplerFailure` dropped from the dependencies.
- **E** `LAG_SAMPLE_INTERVAL_MS` changed; `sampleLag` guard dropped (sample with no callback); `onLagMeasuredRef` assignment
  dropped (read the prop directly); the `lag !== null && buffered !== null` guard loosened; the `presentation_buffer` report
  dropped; `previous = null` in the `.catch` dropped; `sampleLag` dropped from the dependencies.
- **F** the `undefined`/`null` target guard dropped; the `try/catch` dropped; a throw mapped to something other than
  `'refused'`; `reportedNoPlayoutRef` guard dropped; `'refused'` no longer reported; `'not-connected'` reported.
- **G** `failedRead` predicate loosened; each of the six labels; the `hintFor` fallback text; `announcementFor`'s idle rule; each
  tone class; `aria-hidden` on the overlay; the status `<p>` made conditional; the `<video>` `aria-label` fallback.
- **H** the overlay `key` changed from ordinal; the percent geometry (`* 100`); `aria-hidden` on the shape; `pointerEvents`
  dropped.

A probe that bites is recorded in `verification.md` and needs no new test. One nobody notices gets a new test **before** the
extraction touches that concern; a break masked by another term is checked with a **double break**, or recorded as equivalent
with the reason.

### R3 — The timer-count test (SC-006) and the other new tests go in **new files**

`CameraViewerTimers.test.tsx` (name proposed): with fake timers and a spy on `window.setInterval`/`clearInterval`, assert
for a live tile **how many intervals exist and with which delay**: with `onLagMeasured` two (5000 ms and 2000 ms), without it
one (5000 ms); a re-render with a **new inline `onLagMeasured`** creates **no** interval (the issue-1889 failure); a status
change away from `live` clears both; a `cameraIdentifier` change recreates them (current behaviour, pinned as is). Written
first, green on the unsplit file, then re-run unchanged after every sampler move. Further gap tests by concern as the probes
decide (e.g. `CameraViewerReporting.test.tsx`). Each is proved able to fail by a break, with the failure message read.

### R4 — The units (names are proposals; each ≤ 50 code lines where the logic allows)

New folder `apps/shared/src/ui/composites/cameraViewer/` (mirroring `overlayEditor/`, `overlayDraftForm/`):

| Unit | Kind | Holds | Concern |
|---|---|---|---|
| `viewerStatus.ts` | pure | `statusInfoFor`, `announcementFor`, `hintFor`, `labelFor` with their comments | G |
| `ViewerOverlay.tsx` | component | `ViewerOverlay` | G |
| `overlayTypes.ts` + `overlayElements.tsx` | types, components | `CameraViewerOverlay*` types (neutral home, re-exported by the viewer so no cycle); `OverlayLabel`, `OverlayShape` (re-exported by the viewer) | H |
| `samplerFailure.ts` | pure | `countReportableFailure`, `reasonFrom` | I |
| `useSamplerReporting(cameraIdentifier, getToken)` | hook | the shared missing-field ref and `reportMissingStatsField`, the two failure-counter refs, the lazily created throttle ref, `reportSamplerFailure`; returns them | B, C |
| `useDecodeSampler({ status, stats, cameraIdentifier, getToken, reporting })` | hook | the decode interval effect and its constant | D |
| `useLagSampler({ status, stats, cameraIdentifier, getToken, onLagMeasured, reporting })` | hook | `onLagMeasuredRef` and its every-render assignment effect, `sampleLag`, the lag interval effect and its constant | E |
| `usePlayoutTarget({ status, playoutTargetMilliseconds, setPlayoutTarget, cameraIdentifier })` | hook | `reportedNoPlayoutRef` and the apply/report effect | F |

`CameraViewer` keeps the composition (`useWhepSession`, the four hook calls, `statusInfoFor`), the JSX and its exports and props.
`reporting` is the object `useSamplerReporting` returns; the sampler hooks **destructure its members and put the members, not the
object, in their dependency arrays**, so each array is the same as today.

### R5 — What must not change (checked per extraction)

- **Hook and effect order is preserved**: `useWhepSession`; reporting (refs/callbacks, no effects); decode effect; the lag
  callback-ref assignment effect, then the lag effect; the playout effect. Moving each hook to the position of the code it
  replaces keeps this.
- **One shared missing-field ref, two separate failure counters, one throttle per mounted tile** — created once by
  `useSamplerReporting` and handed to both samplers; none is created per sampler and none resets on `cameraIdentifier`.
- **The lag callback ref stays assigned by an effect with no dependency array** (every render, after commit).
- **Dependency arrays are copied verbatim**, including `status`, `stats`, `cameraIdentifier`, `getToken`, the two reporting
  callbacks, and `sampleLag`.
- **The status region stays always mounted, and sibling order** (video → overlays → status → `ViewerOverlay`) **stays**.
- **The exports stay importable from `CameraViewer.tsx`** (`OverlayLabel`, `OverlayShape`, the types, `CameraViewer`,
  `CameraViewerProps`, `CameraViewerStatus`).
- **Each comment moves with its code once; any "above"/"below" pointer is rewritten** to name the new unit (grep after each
  move). The `#2084`/`#2189`/`#2314`/issue-1889 evidence comments are kept whole.
- **No `eslint-disable` appears.**
- **No work is added per render:** a hook adds no `useMemo`, no new `useState`, no object allocation that the viewer did not
  already make per render beyond the returned `reporting` bag (one small object per render, replacing nothing observable; if the
  timer test or a render-count probe shows any extra render, the unit is not extracted).

### R6 — Not in this plan

`useWhepSession`; unifying `countReportableFailure` with `CellPage`'s; changing cadences; `OverlayDraftForm` and any caller; any
behaviour change.

## Project Structure

```text
apps/shared/src/ui/composites/
  CameraViewer.tsx                              (shrinks: composition + JSX; re-exports)
  cameraViewer/                                 (new folder beside the viewer)
    viewerStatus.ts
    ViewerOverlay.tsx
    overlayTypes.ts
    overlayElements.tsx
    samplerFailure.ts
    useSamplerReporting.ts
    useDecodeSampler.ts
    useLagSampler.ts
    usePlayoutTarget.ts
  CameraViewerTimers.test.tsx                   (new: SC-006)
  CameraViewer<Concern>.test.tsx                (new: other gap tests, as the probes decide)
specs/479-camera-viewer-split/
```

## Delivery: one PR, commits that each stay green

1. The timer-count test and the other gap tests (new files), **green on the unsplit file**, with their breaks proved.
2. Extractions: the pure and presentational moves first — `samplerFailure`, `viewerStatus`, `overlayTypes` + `overlayElements`,
   `ViewerOverlay` → **T-D1 measure-and-ask** (component length, file total, hook calls left) → then
   `useSamplerReporting`, `useDecodeSampler`, `useLagSampler`, `usePlayoutTarget`.
3. Task ticks and the verification note (with the per-commit leg statement).

Every commit: the ten covering files and the new gap files run (from `apps/shared`), `eslint --max-warnings 0`, `tsc --noEmit`,
`prettier --check`; the callers' tests (`kiosk-web`, `management-cameras`, `management-web` `tileView`) re-run at the gate and at
the end.

## Verification (what "unchanged" means, concretely)

Recorded on the unmigrated tree, repeated after:

- **Family:** sorted `file :: full name :: status` and sorted assertion lines of the 10 files — `diff` empty; `git diff
  origin/develop...HEAD` on them empty (FR-001).
- **Timer counts:** the `CameraViewerTimers` assertions, identical before and after (SC-006).
- **Probe matrix:** every R2 break, before (family) and after (family + gap files), in `verification.md`; double breaks for any
  jointly defended behaviour; equivalents with reasons.
- **Per extraction:** the key break applied to the **new unit** (not the old location) fails the matching test there; and the
  commit's leg statement (which §IV legs, unchanged).
- **Lengths (SC-002):** the component, every unit (code and comment lines separately) and the **file total**.
- **Greps:** no `eslint-disable`; distinctive comment phrases once; no stale "above"/"below" pointers.
- `shared`, `kiosk-web`, `management-cameras`, `management-web` suites; `eslint --max-warnings 0`, `tsc --noEmit`,
  `prettier --check` for all four; `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`).

## Lessons carried from specs 470–478 (each cost time once)

Anchor the working directory in every command (print `pwd`); write long notes and scripts with the file tool, not a heredoc or a
`node -e` with `/tmp` paths (Windows node does not see msys `/tmp`); the node scripts live in the scratchpad and run with the
cwd at the folder they edit; `grep -E` with `\|` matches a literal pipe; count hook calls with a regex that allows **nested**
generics (`useRef<Set<string>>(` hid one in 479's first count); grep for **other uses** before removing an import; format every
generated file each step; add only the step's files to a commit; a counterfactual must fail for the right reason, so read the
message; a **new untracked unit cannot be restored by `git checkout`**, so stage it before its counterfactuals; **branches are cut
from `origin/develop`**; run **one vitest at a time** and launch long runs as a direct background command; a break applied to the
file must be **restored before anything else runs**; every spec rewrites `.specify/feature.json`, so each rebase conflicts there —
keep the branch's own value; a **foreign edit** in the working tree is never staged (`git add` named files only).

## Risks

| Risk | Mitigation |
|---|---|
| A moved sampler is re-created per render (a dependency array differs) and a wall never aligns (issue 1889 again) | R5 verbatim arrays; the new timer-count test (R3) written first; `…Alignment`/`…SamplerWindow`. |
| Each sampler gets "its own" missing-field ref or counter and the once-per-tile / separate-counters scopes change | R4: `useSamplerReporting` creates them once and hands them to both; probes B, C. |
| The throttle is created per render or per sampler | R5; probe C. |
| The lag callback ref is read from props instead of a ref | R5; probe E. |
| The `reporting` bag adds a render cost across 250 tiles | R5's last bullet; a render-count probe; do not extract if it shows. |
| A unit needs more than ~8 inputs (`useLagSampler` is the widest, 6) | Stop and ask rather than push on. |
| The decomposition is longer and no easier to read | Stated up front; T-D1 stop point. |
