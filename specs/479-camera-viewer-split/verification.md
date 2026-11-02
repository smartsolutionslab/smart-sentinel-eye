# Verification 479 — `CameraViewer` is broken up by responsibility

Behaviour-preserving → **characterisation** (ADR-0139 / ADR-0144 phase 4a). **This component is on the kiosk latency path
(§IV)**, so the evidence below includes the per-commit leg statements and the timer counts (SC-006). Not observed on a running wall
(see "Not observed"); the evidence is the covering family and four new files under jsdom with fake timers.

## Size, as measured (it is a decomposition: the total rises)

|                                   | Before | After                               |
| --------------------------------- | ------ | ----------------------------------- |
| `CameraViewer.tsx`                | 661    | **170**                             |
| the component                     | 352    | **82** (−77 %)                      |
| hook calls in the component       | 13     | **5** (`useWhepSession` + the four) |
| **File total (viewer + 9 units)** | 661    | **788** (+127, +19 %)               |

Units (`cameraViewer/`, total / code lines): `useLagSampler` 132 / 77, `useSamplerReporting` 122 / 51, `useDecodeSampler` 84 / 58,
`viewerStatus` 75 / 33, `usePlayoutTarget` 71 / 33, `overlayElements` 41 / 38, `ViewerOverlay` 39 / 33, `overlayTypes` 30 / 17,
`samplerFailure` 24 / 10. Over the issue's 50-code-line bar: `useLagSampler` (77) and `useDecodeSampler` (58), each one
interval effect moved whole, and `useSamplerReporting` (51). ADR-0084's limit is not enforced, so no failing gate is being fixed.

**The measure-and-ask (T009) came out as the plan predicted.** After the four pure/presentational moves the component was still
352 lines (the file 466, total +2 %): they moved module-level code. The maintainer was asked and chose to continue with the four
hooks; those took the component to 82.

## Characterisation (FR-001)

- 10 covering files: **81 test names with status identical** (the only `diff` line is a missing trailing newline in the "before"
  file), **239 assertion lines identical**; `git diff origin/develop...HEAD` on them is empty. Only four test files were added:
  `CameraViewer{Timers,Reporting,Playout,Presentation}.test.tsx` (41 tests).
- `shared` **1,349 passed** (84 files); `kiosk-web` **329** (32 files); `management-cameras` **150** (23); `management-web` **727**
  (97); `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` clean for all four; `Architecture.Tests` **3254 / 3254**.
- `eslint-disable`: none in the viewer or `cameraViewer/` (SC-003), as before.

## §IV: the legs, per commit, and the timer counts (FR-010, SC-006)

`CameraViewerTimers.test.tsx` spies on `setInterval`/`clearInterval` under fake timers; it was written first, green on the unsplit
viewer, and **run unchanged after every sampler move**. The figures it pins, identical before and after:

| Situation                                               | Intervals                                                                                          |
| ------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| live tile **without** `onLagMeasured`                   | exactly 1 (5,000 ms, decode), 1 created in total                                                   |
| live tile **with** `onLagMeasured`                      | exactly 2 (2,000 ms lag + 5,000 ms decode), 2 created in total                                     |
| before live (connecting)                                | 0 created; `stats` never read                                                                      |
| 8 re-renders with a new inline `onLagMeasured` over 4 s | 0 created, 0 cleared; the callback still fires (issue 1889)                                        |
| leaving live                                            | both cleared (2 `clearInterval`); nothing reads `stats` for the next 10 s; recovery starts 2 fresh |
| unmount                                                 | 0 active; no further `stats` reads                                                                 |
| `cameraIdentifier` change                               | 2 cleared, 2 new (4 created in total)                                                              |
| `getToken` identity change                              | the same figures; the session is **not** rebuilt                                                   |
| `onLagMeasured` appearing / disappearing while live     | `[5000]` becomes `[2000, 5000]` and back                                                           |

Per extraction (each commit body repeats this):

| Commit                             | §IV legs touched                                                       | Result                                                                             |
| ---------------------------------- | ---------------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| `samplerFailure`                   | none (pure helpers)                                                    | unchanged                                                                          |
| `viewerStatus`                     | none (pure text, still computed once per render)                       | unchanged                                                                          |
| `overlayTypes` + `overlayElements` | none (static presentational components; re-exported, no caller change) | unchanged                                                                          |
| `ViewerOverlay`                    | none (rendered only while not live)                                    | unchanged                                                                          |
| `useSamplerReporting`              | decode fragment and presentation buffer, **reporting plumbing only**   | unchanged                                                                          |
| `useDecodeSampler`                 | SFU → kiosk decode (the `receive_to_decoded` fragment sampler)         | unchanged: 5 s, once per live status/camera/token                                  |
| `useLagSampler`                    | presentation buffer + the wall-alignment feed (`onLagMeasured`)        | unchanged: 2 s, only with the callback, callback read through the every-render ref |
| `usePlayoutTarget`                 | playout-target application                                             | unchanged: same guard, same throw-to-`refused` mapping, report-once                |

**One deliberate difference, stated:** the two sampler effects' dependency arrays gained the stable ref members that now arrive from
a hook (`decodeSampleFailuresRef` or `lagSampleFailuresRef`, and `reportThrottleRef`). `react-hooks/exhaustive-deps` requires them
once they are no longer locally declared refs, and adding them avoids a suppression. A ref's identity never changes, so no effect
re-runs more often; the timer test is the evidence.

## Probing before moving: 25 of 62 deliberate breaks went unnoticed by the family

Each break was one named, exactly-once substitution into the unsplit viewer (`probe479.cjs`), the covering family run, the file
restored from git; matrix: 37 noticed, **25 not** (40 %). The 25: the decode and lag intervals' cleanup, the `live` guard and the
`cameraIdentifier`/`sampleLag`/`getToken` dependency members; the lag callback read through the ref (and its every-render
assignment); the missing-field report in each sampler; the failure callbacks' dependencies and the non-`Error` reason; the playout
target's `live` guard, throw mapping and `refused` reporting; the offline exception to a failed read, the Provisioning and Idle
labels, the overlay showing while live; the overlay `key`, the label's pointer events and percent geometry, the shape's pointer
events.

**41 tests in four new files** pin 22 of them. Each was green on the unsplit viewer and proved able to fail with the message read.
Examples: D2 `expected [ {…}, {…} ] to have a length of 1 but got 2` (the live guard); D7 `a reconnecting tile samples nothing:
expected [ 5000 ] to deeply equal []`; E3 `a closure the parent has replaced must not be called: expected "vi.fn()" to not be called at
all`; E7 `the callback arrived: sampling starts: expected [ 5000 ] to deeply equal [ 2000, 5000 ]`; F7 `expected [ 400, 400 ] to deeply
equal [ 400 ]`; G6 `Unable to find an element with the text: Provisioning stream….`; H6 `expected '12.5%' to be '75%'`.

After each extraction the matching break was applied to the **new unit** and the same test failed there (reporting 6/6 + C7 by hand,
decode 7/9, lag 9/10, playout 8/8, status 11/11, elements 5/5, overlay 3/3, failure 2/2; the remainder are the equivalents and the
jointly defended below).

### Equivalent (no observable behaviour), recorded with the reason

| Break                                        | Why nothing can notice it                                                                                                          |
| -------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| D5 `elapsed !== null` guard                  | `reportKioskLatency`'s first guard `!Number.isFinite(elapsed)` already returns for `null`, so nothing is sent or logged either way |
| E4 `buffered !== null` half of the lag guard | `lagBetween`'s null conditions are a superset of `bufferDelayBetween`'s, so a non-null lag implies a non-null buffer               |

### Defended jointly

| Break                                                                                                 | Result                                                                                                                      |
| ----------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| D8 `cameraIdentifier` dropped from the decode effect's own dependencies, alone                        | green: `reportMissingStatsField` and `reportSamplerFailure` both depend on `cameraIdentifier`, so the effect re-runs anyway |
| D8 **and** B4 (the callback's `[cameraIdentifier]` dropped)                                           | still green                                                                                                                 |
| D8 **and** B4 **and** `reportSamplerFailure`'s `[cameraIdentifier, getToken]` reduced to `[getToken]` | the camera-change test fails: `two old intervals, two new ones: expected [ {…}, {…}, {…} ] to have a length of 4 but got 3` |

C7 (`getToken` dropped from `reportSamplerFailure`'s dependencies) is **not** visible through timers because the effects carry
`getToken` directly; it is pinned by "Sends the failure count with the getToken the tile has now", which fails with `the failure
report is sent with the current getToken: expected "vi.fn()" to be called at least once`.

## Slips and judgements, kept visible

- My first hook count was 12; the issue was right with 13 (my regex missed the nested generic in `useRef<Set<string>>(…)`).
- The explicit `SamplerReporting` interface (the reporting bag's type) is added to `useSamplerReporting.ts` inside the
  `useDecodeSampler` commit, not the `useSamplerReporting` one: it was written while typing the sampler hook's input, to replace a
  `Pick<ReturnType<typeof hook>, …>` that would have coupled the units by inference.
- Several scripted extractions needed a second run (an unused import, a comment wrapped differently than I had quoted it). None
  reached a commit.
- A **foreign edit** to `src/ScenarioSimulator/CameraCatalog/CameraCatalogClient.cs` has been in the working tree throughout. It is
  not from this work; it was never staged and no commit of this branch contains it.

## Not observed

CI on Linux (the first run is this PR's); a running wall. The §IV claim rests on the family plus `CameraViewerTimers` under fake
timers, not on a measured wall: #1714 (alignment under real load) stays open as before, and this change does not touch it.

## QA

Recorded in the PR (Phase 6, `/code-review medium`).

## QA (`/code-review medium`): 6 findings, none fixed, no behavioural regression

The reviewer compared the sampler effects, intervals, refs, throttle, per-tile failure-counter scoping and the playout-target error
handling with the original and found them unchanged; the public exports (`OverlayLabel`, `OverlayShape`, the overlay types) are
re-exported from `CameraViewer.tsx`. It did not read the four new test files or run the suite (both are recorded above).

Accepted, in writing:

- **The unrelated `CameraCatalogClient.cs` edit in the working tree.** Not from this work, never staged; the branch diff lists only
  `apps/shared`, `specs/479-…` and `.specify/feature.json`.
- **`SamplerReporting` exposes the raw counters and a string transition, so a sampler could pass the wrong counter.** True, and a
  `reportDecodeFailure`/`reportLagFailure` pair would be tidier; but it changes which callbacks the sampler effects depend on, in a
  commit whose point is that the dependency arrays do not change. Recorded as a follow-up, not done on the latency path.
- **The two ref members added to the sampler effects' dependency arrays make ref identity load-bearing.** Stated in the commit and
  above as the one deliberate difference; `CameraViewerTimers` fails if a ref were ever recreated per render (a new interval per
  render), which is exactly the guard the reviewer asks for.
- **`samplerFailure`'s "Duplicated rather than shared with CellPage … extract it at the third site" note.** The duplication is
  unchanged by this move and spec 140's rule (extract at the third site) still holds; moving the helper to its own file makes a
  later share easier, it does not make the note stale.
- **Time-relative wording in `useLagSampler`'s `.catch` comment ("now advances roughly every other tick").** Moved verbatim, as the
  specs require for evidence comments; rewording it is not part of a behaviour-preserving split.
- **`.specify/feature.json`** is rewritten by every spec branch and conflicts on each rebase; the lessons section keeps the
  branch's own value.
