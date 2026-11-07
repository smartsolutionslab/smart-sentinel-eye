# Implementation Plan: The samplers report failures through two reporters, not raw counters

**Branch**: `483-sampler-failure-reporters` | **Date**: 2026-11-07 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/483-sampler-failure-reporters/spec.md`

## Summary

`useSamplerReporting` stops handing its failure counters out. It returns `reportDecodeFailure(error)` and
`reportLagFailure(error)`; each sampler calls its own and sees no counter and no transition string. **First probe, then pin, then
move**: break each concern of the failure reporting once in today's code (decode transition, lag transition, counters kept apart,
counter not reset on a camera swap, counter once per tile, the reportable-count rule, the shared throttle, reporter identity across
re-renders, reporter identity with the camera), see whether the existing `CameraViewer*` tests notice, and write characterisation
tests in **new files** for what they do not. Then add the reporters **beside** the old surface, switch the two samplers one per
commit, and remove the old surface last, so every commit builds and passes on its own. Behaviour-preserving, so
**characterisation** (ADR-0139/0144 phase 4a). Same method as specs 476–482.

**Sizing, stated before building.** About neutral: two small `useCallback`s and a narrower interface come in; two ref members, the
counter-passing call shape and two dependency-array entries go out. Reported as measured (SC-004); not expected to fall much, the
gain is that the counter ↔ transition pairing is held in one place.

## Technical Context

- **Stack**: TypeScript, React 19.3, Vitest + Testing Library (jsdom). `apps/shared` only; `kiosk-web` and `management-cameras`
  consume `CameraViewer` and are re-run at verify.
- **Baseline (2026-11-07, `develop` @ `e9b973a3`)**: `useSamplerReporting` returns five members; the two samplers destructure
  `reportSamplerFailure` plus their own counter ref; covering tests `CameraViewerReporting`, `CameraViewerSamplerWindow`,
  `CameraViewerAlignment`, `CameraViewerTimers`, `CameraViewerReportCadence` and `kioskLatency.test` (the sink).
- **Touches**: `useSamplerReporting.ts`, `useDecodeSampler.ts`, `useLagSampler.ts`; new test files for gaps. No existing test file,
  no config, no `CameraViewer.tsx` (it passes the whole `reporting` object through).
- **Latency budget (§IV)**: **on the path.** `useDecodeSampler` is the SFU → kiosk decode fragment; `useLagSampler` the
  presentation buffer. Both effects list the reporting values in dependency arrays. **Every commit states the legs it touches and that
  they are unchanged** (FR-009).

## Constitution Check

| Principle                                             | Verdict                                                                                                                                                                                                                           |
| ----------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| §Testing — refactors stay green                       | Characterisation: existing suites unmodified, plus gap tests written first and seen green on today's code.                                                                                                                        |
| §IV latency budget is sacred                          | The samplers keep their triggers: the interval is rebuilt on `status`, `stats`, `cameraIdentifier`, `getToken` (and the reporter, whose identity follows the last two) and never otherwise. No new state, effect or subscription. |
| ADR-0036 — smallest change, no speculative generality | Two reporters for the two callers that exist; no generic factory. The throttle and the missing-field reporter are not touched.                                                                                                    |
| ADR-0180 — the `sampler_failed` sink                  | Same call, same arguments, same counts.                                                                                                                                                                                           |

## Decisions

**D1. Build the two reporters on the existing private callback.** `reportSamplerFailure` stays inside the hook (no longer returned),
and `reportDecodeFailure = useCallback((error) => reportSamplerFailure(decodeSampleFailuresRef, 'decode-sampler-failed', error),
[reportSamplerFailure])`, likewise for lag. The refs are stable for the mount, so each reporter's identity changes exactly when
`reportSamplerFailure`'s does (`[cameraIdentifier, getToken]`): FR-005 by construction. Alternatives: (a) two independent
`useCallback`s each inlining the counting and logging: duplicates the six-line body (the counter, the log call, the sink call) for the
sake of one fewer hop; (b) a module-level helper taking `cameraIdentifier`, `getToken`, the throttle and the counter: moves the
dependencies into every call. Chosen: the thin wrappers, smallest diff.

**D2. The returned shape.** `SamplerReporting` becomes `{ reportMissingStatsField, reportDecodeFailure, reportLagFailure,
reportThrottleRef }`. The throttle stays: both samplers read it for their own latency reports.

**D3. Dependency arrays.** Each sampler's array swaps `reportSamplerFailure, <counter>Ref` for its one reporter. The set of values
that can change is the same (the counter ref never changed; the reporter changes with the old `reportSamplerFailure`), so the
effect rebuilds on the same occasions. This is the claim the identity tests pin; the plan does not rest on it.

**D4. Order: pin, add, switch, remove.** The reporters are added **before** any sampler moves, with the old members still returned
(an additive commit, unused). Each switch is then a pure substitution in one sampler. The old members go in the last commit, with
the grep (SC-002). Every commit builds and passes (FR-007).

## Phases and commits

| Step    | Commit                                                                                                                                                                 | Legs touched                   |
| ------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------ |
| T-probe | Break each concern in `useSamplerReporting` (named, exactly-once substitutions, `probe483.cjs`); run the `CameraViewer*` tests; record the matrix in `verification.md` | none                           |
| T-gap   | Gap tests in **new** files for what nothing noticed, green on today's code, proved able to fail (message read)                                                         | none                           |
| 1       | `useSamplerReporting` also returns `reportDecodeFailure` / `reportLagFailure` (additive, unused)                                                                       | none (unused)                  |
| 2       | `useDecodeSampler` calls `reportDecodeFailure`; its dependency array follows; `CameraViewerTimers`, `…Reporting`, `…SamplerWindow` re-run unchanged                    | SFU → kiosk decode: unchanged  |
| 3       | `useLagSampler` calls `reportLagFailure`; same                                                                                                                         | presentation buffer: unchanged |
| 4       | Remove `reportSamplerFailure`, `decodeSampleFailuresRef`, `lagSampleFailuresRef` from the returned shape and the interface; grep                                       | none                           |
| 5       | Verification note, tick tasks, QA dispositions                                                                                                                         | none                           |

After each switch, the matching break is applied to the **shared** reporters and the sampler's tests must fail there (counterfactual,
SC-003). New untracked files are staged before counterfactuals.

**Measure-and-ask?** Not needed in the usual form. If a sampler cannot switch without an edited assertion, that is a **block**,
reported, not adjusted.

## Risks and how they are covered

- **Effect triggers move** (a reporter identity that changes per render or too rarely): pinned by a new identity test through the
  real `CameraViewer` (interval not cleared on a re-render; cleared on a `getToken` or camera change); probed by dropping
  `useCallback` and by dropping a dependency.
- **A counter is shared or reset** (one counter for both samplers; reset on camera swap; created per effect): probed; gap tests if
  unnoticed.
- **A transition name is crossed** (the decode reporter logging `lag-sampler-failed`): probed in both directions.
- **The throttle is lost** (sink report bypasses it): probed.
- **Fake timers and the sampler loop** make the new tests brittle: they reuse the harness of the existing `CameraViewerReporting` /
  `CameraViewerSamplerWindow` files rather than inventing one.

## Project Structure

```text
apps/shared/src/ui/composites/cameraViewer/useSamplerReporting.ts   (reporters; old members removed last)
apps/shared/src/ui/composites/cameraViewer/useDecodeSampler.ts
apps/shared/src/ui/composites/cameraViewer/useLagSampler.ts
  new: gap tests beside the existing CameraViewer*.test.tsx, only where the probe finds an unnoticed break
```
