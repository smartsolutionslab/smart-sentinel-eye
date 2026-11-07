# Spec 483 — The samplers report failures through two reporters, not raw counters

**Feature Branch**: `483-sampler-failure-reporters`
**Created**: 2026-11-07
**Status**: Draft
**Issue:** #3224 (feature-level; follow-up recorded by the frontend refactoring survey, #3190–#3198, via #3194 / `CameraViewer`).
**ADRs:** ADR-0036 (smallest change, no speculative generality), ADR-0139 / ADR-0144 (phase 4a), ADR-0180 (the `sampler_failed`
sink), ADR-0037 (phases).
**Refs:** #2189 (a sampler that throws says so), #2314 (the window left open), spec 307 / #2563 (one throttle per mounted tile).
**No new ADR.** A narrowing of an internal hook's surface.
**Latency budget (constitution §IV): ON the kiosk path, so the claim is made per leg.** `useDecodeSampler` is the **SFU → kiosk
decode** fragment (`receive_to_decoded`); `useLagSampler` is the **presentation buffer** (`presentation_buffer`). Both run inside
`CameraViewer`, which every kiosk tile mounts. Both effects list the reporting values in their **dependency arrays**: if any of them
changed identity per render, the sampler's interval would be cleared before it could take a second sample (the failure of issues
1888 / 1889). The change moves _how a failure is reported_ and must keep the effects' triggers exactly: rebuilt when `status`,
`stats`, `cameraIdentifier`, `getToken` change, and never otherwise. No new state, effect, subscription or render-time work. Every
commit states the legs touched and that they are unchanged; `CameraViewerTimers` re-runs unchanged.
**Phase 4a colour:** behaviour-preserving → **characterisation**. Existing suites pass **unmodified**; an assertion that has to be
edited means behaviour moved: block (CLAUDE.md). A refactor that is also a bug fix is two issues.

## Problem

`useSamplerReporting` (`apps/shared/src/ui/composites/cameraViewer/useSamplerReporting.ts`) hands both samplers its internals:

| Returned                                           | Consumed by             | How                                                                    |
| -------------------------------------------------- | ----------------------- | ---------------------------------------------------------------------- |
| `reportSamplerFailure(counter, transition, error)` | both                    | the caller passes **its own counter and the matching transition name** |
| `decodeSampleFailuresRef`                          | `useDecodeSampler` only | passed as `counter` with `'decode-sampler-failed'`                     |
| `lagSampleFailuresRef`                             | `useLagSampler` only    | passed as `counter` with `'lag-sampler-failed'`                        |
| `reportMissingStatsField`, `reportThrottleRef`     | both                    | unchanged by this spec                                                 |

(Premise check on `develop` @ `e9b973a3`, 2026-11-07: the surface is as the issue says. Each sampler destructures the shared
counter it owns and passes it back with a hand-typed transition string, so the pairing "decode counter ↔ `'decode-sampler-failed'`" is
held in two places the type system does not connect.)

The issue asks to wrap them behind `reportDecodeFailure(error)` / `reportLagFailure(error)`. A sampler then no longer sees a counter
at all, and cannot hand the lag counter to the decode transition.

### What the issue does not settle

1. **Identity is the whole risk.** Today `reportSamplerFailure` is `useCallback(…, [cameraIdentifier, getToken])` and the two refs are
   stable for the mount; the effects list all three. Two new reporters must change identity **exactly when `reportSamplerFailure`
   does** (a camera swap or a new `getToken`) and not otherwise, or the effect triggers move. The plan decides how, and the probe
   must show a test notices a per-render reporter.
2. **Counter scope.** Two counters, one per sampler, never shared, never reset on a camera swap (documented in the file, #2189).
   After the change the counters are private to the hook; their scope must be exactly as before.
3. **Does the existing suite notice a break?** `CameraViewerReporting`, `CameraViewerSamplerWindow`, `CameraViewerAlignment` and
   `CameraViewerTimers` exist; which concerns they pin (the counters kept apart, not reset per camera, the right transition per
   sampler, the throttle shared, the identity) is for the probe to answer, not for this spec to assume.
4. **The `SamplerReporting` type is exported** and imported by both samplers; no other consumer exists (grep, 2026-11-07).

## Scope

**In:** `useSamplerReporting` returns `reportDecodeFailure(error)` and `reportLagFailure(error)` instead of `reportSamplerFailure`,
`decodeSampleFailuresRef`, `lagSampleFailuresRef`; `SamplerReporting` and `useDecodeSampler` / `useLagSampler` follow; the sampler
effects' dependency arrays name the new reporters.

**Out (explicitly):** `reportMissingStatsField` and `reportThrottleRef` (the throttle is read directly by both samplers for the
latency reports; wrapping it is a separate question); the counting rule (`countReportableFailure`, `reasonFrom`); the log and sink
payloads (`logResilienceEvent`, `reportKioskLatency('sampler_failed', …)`); `usePlayoutTarget`; any change to when a sampler runs
or what it measures; any new suppression.

## User Scenarios & Testing

### User Story 1 (P1) — A sampler that throws reports exactly as before, on the same budget

As the kiosk wall, when the decode or lag sampler throws I get the same console line and the same `sampler_failed` sink report, at
the same reportable counts, and the samplers keep running on the same triggers. As the maintainer I can prove it.

**Independent test:** the existing reporting, window and timer tests pass with no line edited.

```gherkin
Scenario: a decode failure reports under the decode transition with its own count
  Given a live tile whose decode sampler throws
  When the failure is reported
  Then the console line names 'decode-sampler-failed' with the decode counter's count
  And the sink receives 'sampler_failed' at the same reportable counts

Scenario: the two counters stay apart
  Given a decode failure and a lag failure on the same tile
  Then each is counted and reported on its own (the first of each is reported)

Scenario: the counters survive a camera swap
  Given a tile that has reported failures
  When its camera identifier changes
  Then neither counter resets

Scenario: the effects do not rebuild
  Given a live tile
  When it re-renders with the same status, stats, camera and getToken
  Then each sampler's interval is not cleared and recreated
```

### User Story 2 (P2) — A sampler cannot pair the wrong counter with a transition

```gherkin
Scenario: no raw counter in the samplers' reach
  When the change is done
  Then useDecodeSampler and useLagSampler read no failure counter
  And SamplerReporting exposes no counter ref
```

### Edge cases

- **Camera swap:** `reportDecodeFailure` / `reportLagFailure` change identity with `cameraIdentifier` and `getToken` (as
  `reportSamplerFailure` does) and the counters do not reset.
- **Reconnect (status flap):** the effects rebuild (status is a trigger) and the counters, throttle and reported-field set persist.
- **A throw while reporting:** unchanged; `countReportableFailure` and the log call keep their order.
- **First failure after mount:** counted as 1 and reported, as today.

## Requirements

- **FR-001** Every existing test passes after each commit; none is modified.
- **FR-002** `useSamplerReporting` exposes `reportDecodeFailure(error)` and `reportLagFailure(error)`; it no longer exposes
  `reportSamplerFailure`, `decodeSampleFailuresRef` or `lagSampleFailuresRef`.
- **FR-003** Behaviour is unchanged: each reporter counts on its own counter, logs under its own transition name with the same
  fields, and reports `sampler_failed` to the sink through the shared per-tile throttle at the same reportable counts.
- **FR-004** The counters are created once per mounted tile and never reset on a camera swap; the throttle and the reported-field
  set are unchanged.
- **FR-005** The reporters' identity changes exactly when `cameraIdentifier` or `getToken` does, and never otherwise.
- **FR-006** `useDecodeSampler` and `useLagSampler` call their reporter; the effects' triggers (status, stats, camera, getToken, and
  the reporters) cause the same rebuilds as before.
- **FR-007** Each commit builds and passes the affected suites on its own (bisect); one concern per commit.
- **FR-008** `eslint src --max-warnings 0`, `tsc --noEmit`, `prettier --check` clean for `shared`, `kiosk-web`, `management-web`,
  `management-cameras`; `Architecture.Tests` pass (they scan `.tsx`) or CI is named as the evidence if they cannot run locally.
- **FR-009** **Latency (§IV).** Each commit states the legs touched (SFU → kiosk decode; presentation buffer) and that they are
  unchanged; `CameraViewerTimers`, `CameraViewerReporting`, `CameraViewerSamplerWindow` re-run unchanged.
- **FR-010** No change outside `cameraViewer/useSamplerReporting.ts`, the two samplers, their tests' new files, `specs/483-…` and
  `.specify/feature.json`.

## Success Criteria

- **SC-001** The covering suites: same test names before and after, all passing, `git diff` on them empty.
- **SC-002** `grep` finds no `SampleFailuresRef` and no `reportSamplerFailure` outside `useSamplerReporting.ts`'s own private body.
- **SC-003** For each concern (decode transition; lag transition; counters kept apart; counter not reset on camera swap; counter
  once per tile, not per effect; reportable-count rule; shared throttle; reporter identity stable across re-renders; reporter
  identity changes with the camera), a deliberate break is noticed by a named test; where nothing notices, a new test in a **new
  file** is written first, green on today's code, proved able to fail (message read), and kept; equivalents and jointly defended
  behaviours are recorded with evidence.
- **SC-004** Line counts reported before/after for the touched files, with the total.

## Assumptions

- Wrapping inside `useSamplerReporting` with two `useCallback`s keyed `[cameraIdentifier, getToken]` reproduces
  `reportSamplerFailure`'s identity; the plan decides and the probes confirm.
- The throttle stays exposed: both samplers read it for their own latency reports, which this spec does not touch.

## Dependencies

Independent of #3225 / #3226. Builds on #3194 (the `CameraViewer` split that created `useSamplerReporting`), #2189 and spec 307.
