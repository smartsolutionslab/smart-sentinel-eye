# Verification — spec 262 (issue #2607, ADR-0156)

Phase 5 (ADR-0037). Run 2026-09-26, worktree `D:\Github\sse-2607`, branch
`feat/2607-3x3-walls-spanning-tiles`, tip at the commit immediately preceding
this file.

## What was observed

### Backend coverage gate (ADR-0065)

**Command, with a deviation flagged up front:** `scripts/coverage-check.ps1`
requires PowerShell 7 (`#Requires -Version 7.0`), which is **not installed on
this machine** (confirmed: `Get-Command pwsh`, PATH inspection, `cmd /c where
pwsh`, filesystem search all came back empty). Windows PowerShell 5.1 (the
only PowerShell present) refuses to run the script directly. A scratch copy
of the script with only the `#Requires` line stripped was run instead under
5.1, identical logic and flags otherwise, then deleted (`git status --short
scripts/` confirmed clean afterward). **This is not a literal run of the
documented command** — flag for whoever next needs this script: either
install PowerShell 7 on this machine, or note the substitute is necessary
here.

**Output:** 28/28 non-integration backend test projects green (2,353 tests
total, 0 failed). All 20 ADR-0065-gated assemblies pass their coverage floor;
the two most relevant to this spec:

```
SmartSentinelEye.LayoutComposition.Application        89.3%   (gate >= 80%)  PASS
SmartSentinelEye.LayoutComposition.Domain             96.3%   (gate >= 90%)  PASS
```

`All gates pass.` (exit code 0)

### Frontend workspace tests

**Command:** `pnpm -r --filter "./apps/**" test` (repo-wide, not scoped to
this feature's files).

**Output:** `kiosk-web` 201/201 green, `management-web` 391/391 green.
`apps/shared` had 3-5 failures (varied between two runs) in
`OverlayEditorUndo.test.tsx`, `OverlayEditorKeyboard.test.tsx`,
`OverlayEditorBackdrop.test.tsx`, `FrameCapture.test.tsx` — all
`Error: Test timed out in 5000ms`, all pre-existing specs (147/149/154) with
no relationship to this branch's changed files, different failure set each
run. Read as machine-load flake (real-timer tests under load), not a
regression — not investigated further, out of this phase's scope.

### e2e — offline parse check

**Command:** `pnpm exec playwright test --list` (no live stack).

**Output:** `Total: 69 tests in 31 files`, exit code 0. New
`spanning-wall.spec.ts` and both cases in the modified
`kiosk-shows-a-label-over-video.spec.ts` parse and register correctly.

### Live, through the app

**Not performed.** `mcp__aspire__list_apphosts` and `docker ps` both confirm
a second Aspire stack (`D:\Github\sse-2526`, PID 12004, containers up
17-20 hours) is the only one running on this machine, in active use by
another workstream, off-limits per explicit product-owner instruction. No
Aspire stack of this spec's own was booted; no attempt was made to run
`tests/Integration.Tests` or any e2e spec live, and none of that is reported
as green — only as compiling/parsing (see backend and e2e engineer reports,
already in this PR's body).

This means **T038 (screenshot the hero wall live, watch a highlight light)
was not observed** — not because the behaviour is in doubt (it follows
directly from the same code paths the 190 domain tests, 88 application
tests, and 391+201 frontend tests already exercise), but because nobody
watched the actual running system do it, which is what this phase exists to
establish honestly rather than paper over.

## Latency budget (§IV) — the NFR gate

Per ADR-0156 §1 and spec.md §4, three measurements were planned:

| Measurement | Where | Result |
|---|---|---|
| **M1** — composite + render (`overlay_draw`), 9-tile vs 4-tile, cadence first | dev machine | **NOT MEASURED.** Same Aspire-stack contention as above — no dev-machine wall was ever live to measure against. |
| **M2** — decode, in part (`receive_to_decoded`, `decoderImplementation`, `powerEfficientDecoder`, `framesDropped`) | dev machine | **NOT MEASURED**, same reason. |
| **M3** — the same, on real kiosk hardware | not available in any environment this team has | **NOT PERFORMED — precondition for shipping `MaxTiles = 9` to production** (ADR-0156 §1). Follow-up issue: **[#2614](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2614)**, added to Project #13, carrying the measurement procedure, the budgets (composite+render ≤ 50 ms, SFU→kiosk decode ≤ 120 ms, cadence ≥ 40 Hz per ADR-0123), and the rule: *if either budget fails, ship the cap the measurement supports, not 9.* |

**This is a wider gap than spec.md §4 anticipated.** §4 expected M1/M2 to be
obtainable on this dev machine, with only M3 deferred. In practice, none of
the three were measured here — the same environment constraint (another
Aspire stack already occupying this machine, confirmed still active at
verification time) blocked the dev-machine figures too, not only the
real-hardware one. Issue #2614 has been scoped to cover getting the
dev-machine M1/M2 figures recorded here, in addition to the real-hardware M3
it was always going to need.

**Per ADR-0138 (honesty rules):** constitution §IV must **not** be edited to
say this leg is "measured" — no figure was read here. It stays "recorded, not
yet observed" until #2614 produces one. Merging to `develop` is not shipping
to production (no production deployment exists yet — ADR-0118), which is why
this merge and ADR-0156's production gate are compatible; the gate travels
with #2614, not with this PR.

## What was not covered

- Live click-path observation (T038) — not performed, Aspire-stack
  contention (documented above).
- M1/M2 dev-machine latency figures (T039) — not measured, same reason.
- M3 real-kiosk-hardware measurement (T040) — not performed, tracked by
  [#2614](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2614)
  per ADR-0156 §1's production precondition.
- Live integration-test execution against a real Aspire stack
  (`tests/Integration.Tests/LayoutComposition/TileSpanIntegrationTests.cs`,
  16 cases) and live e2e execution (`e2e/spanning-wall.spec.ts`,
  `e2e/kiosk-shows-a-label-over-video.spec.ts`) — both compile/parse clean but
  were not run against a live stack, same reason. CI's isolated runner is the
  gate that will actually execute these (same resolution as issue #2333's
  e2e suite).
- The e2e shard's wall-clock cost at 9 tiles (D1's stated risk) — cannot be
  measured until an actual PR CI run happens; will be recorded once observed.
- `apps/shared`'s flaky `OverlayEditor*`/`FrameCapture` timeout failures —
  confirmed pre-existing and unrelated to this branch's files, not
  investigated further (out of this phase's scope).

## Issue #2614 — M1/M2 dev-machine figures (2026-10-05)

Follow-up to the gap recorded above. Run from worktree `D:\Github\sse-2614`,
branch `docs/2614-m1-m2-tile-measurement`, against this agent's own
freshly-booted Aspire stack (confirmed no contending stack first — `docker ps`
and `tasklist` both read clean before boot).

**M3 (real kiosk hardware) is explicitly NOT performed here.** There is no
physical kiosk device in this environment — a Windows dev machine, Chromium
under Playwright, and the Aspire stack. It remains the blocker before
`MaxTiles = 9` ships to production (ADR-0156 §1), tracked by this same issue.
Per ADR-0138, constitution §IV keeps "overlay composite + render" and
"SFU → kiosk decode" exactly as already recorded — **this section does not
change any cell of that table.** The figures below are dev-machine evidence
about the code, not a production sign-off, and not a discharge of #2614's own
precondition.

### Method

- **9-tile (3×3) wall:** the existing CI fixture and harness
  (`e2e/support/seed-live-video-wall.setup.ts` + `kiosk-shows-a-label-over-video.spec.ts`'s
  "the span from a value being submitted to it being visible" test, spec 225
  US1/US2 — unmodified, run as-is against a live stack).
- **4-tile (2×2) wall:** a throwaway comparison harness (a `*.setup.ts` seed
  + a `kiosk-*.spec.ts` measurement, both named `*-m2614-*`, named with the
  `E2E ` / `E2E Layout ` prefixes the existing teardown sweeps already
  retire/archive by name) — **not committed**, written only to drive this
  measurement and deleted immediately after the run (`git status --short` is
  clean). It reused the product's own automatic instruments
  (`measureOverlayDraw`/`reportKioskLatency` in `kioskLatency.ts`, and the
  5 s decode-stats tick in `CameraViewer.tsx`) by listening on the kiosk
  page's console for `[latency]` lines, rather than re-implementing the
  9-tile test's click-to-paint calibration (which this comparison does not
  need). It additionally wrapped `window.RTCPeerConnection` before
  navigation (classic Playwright init-script technique, no production code
  touched) so it could read `decoderImplementation` /
  `powerEfficientDecoder` / `framesDropped` off `getStats()`'s inbound-rtp
  report per tile — fields the app's own instrument deliberately does not
  read (it only needs `framesDecoded`/`totalProcessingDelay`/`totalDecodeTime`
  for `receive_to_decoded`).
- Both walls: same browser build, same window size, same fixture video
  source (`rtsp://fixture-video:8554/loop`), one wall at a time. Cadence
  (`measureFrameInterval`, a 1 s `requestAnimationFrame` counting loop) read
  **before** the timed loop and again **after** it, never during (ADR-0123).
- Run **twice each**, per the standing lesson that the first run after
  machine churn reads like a regression.

### M1 — composite + render (`overlay_draw`), cadence first (ADR-0123)

| Wall | Attempt | Cadence before | Cadence after | `overlay_draw` samples | p50 | p95 | max |
|---|---|---|---|---|---|---|---|
| 9-tile (3×3) | 1 (clean, standalone) | 37.04 ms/frame (≈27.0 Hz) | 38.27 ms/frame (≈26.1 Hz) | 108 | 70.4 ms | not printed (see note) | 166 ms |
| 9-tile (3×3) | 2 (clean, standalone, retry) | 42.36 ms/frame (≈23.6 Hz) | 33.33 ms/frame (≈30.0 Hz) | 108 | 57.4 ms | not printed | 103.4 ms |
| 4-tile (2×2) | 1 (clean, standalone) | 32.26 ms/frame (≈31.0 Hz) | 38.27 ms/frame (≈26.1 Hz) | 48 | 50.5 ms | 78.1 ms | 79.3 ms |
| 4-tile (2×2) | 2 (clean, standalone) | 30.81 ms/frame (≈32.5 Hz) | 33.33 ms/frame (≈30.0 Hz) | 48 | 62.9 ms | 145.8 ms | 148.5 ms |

The 9-tile `p50`/`max` figures are exactly what the unmodified test's own
`reportLegs` function prints per leg (`[legs] overlay_draw: N sample(s), p50
X ms, max Y ms`) — that function does not print a p95 for any leg, which is
why the 9-tile rows above read "not printed" rather than a number. The 4-tile
figures (p50/p95/max) came from this issue's own throwaway script, which
computed all three from the raw samples (`n=48` on the 4-tile wall — 4 tiles
× 10-12 redraws each; `n=108` on the 9-tile wall — 9 tiles × up to 12 redraws
each). Raw sample arrays for both clean 4-tile runs:

```
run 1: [88,74.5,63.1,51,23.5,33.7,35.3,39.4,48.1,53.2,54.4,55.6,64.1,65.2,66.8,64,70.5,71.3,64.4,63.1,55.2,56.3,57,57.9,145.8,143.2,148.5,146.6,57.3,45.3,45.5,48.7,77.2,79.4,81.1,78.6,26.3,21.5,17.1,9.5,62.8,63.9,65.5,67.2,65.5,54,54.6,82.1]
run 2: [61.6,61.8,46.2,47.2,38.7,39.3,40.2,40.9,50.2,49.6,24,25,59.4,47.9,45,46.9,65.5,52,48.9,51,50.5,38.7,32.6,35.4,47.3,46.7,49.8,49.8,49.9,51.3,52.5,52,12.2,15.9,15.9,18,28.8,30,30.8,31.2,49.8,69.3,70.4,70.1,83.8,87.2,88.6,92.1]
```

**Budget comparison (≤ 50 ms, section IV composite + render):** every run on
both wall sizes breaches the median. This is the same reading ADR-0123
predicted and already documented for this leg in general — the leg's floor is
set by display cadence (frame wait + one frame interval), not by tile count —
and it holds here too: the 9-tile wall's worse cadence (~24-27 Hz, below even
the 30 Hz "median exactly at budget" line) tracks its worse p50 against the
4-tile wall's somewhat better cadence (~31-32 Hz before the loop). **Neither
wall sustains the ≥ 40 Hz ADR-0123 says the tail needs, and the 9-tile wall
does not reliably sustain the ≥ 30 Hz the median needs either.** This is a
dev-machine reading, not a kiosk-hardware one, and it is reported as a
negative finding, not smoothed over: on this machine, more tiles costs
cadence, and cadence is already the controlling term at 4.

### M2 — decode, in part (`receive_to_decoded`, plus `getStats()` fields)

| Wall | Attempt | `receive_to_decoded` samples | p50 | p95 | max | decoder (all tiles) |
|---|---|---|---|---|---|---|
| 9-tile (3×3) | 1 | 9 | 166.6 ms | — | 197.1 ms | not read this run (see note) |
| 9-tile (3×3) | 2 | 8 | 226.0 ms | — | 266.7 ms | not read this run (see note) |
| 4-tile (2×2) | 1 | 37 | 39.7 ms | 49.0 ms | 49.7 ms | `decoderImplementation: null`, `powerEfficientDecoder: null`, `framesDecoded: ~1377-1378`, `framesDropped: 0` on all 4 tiles |
| 4-tile (2×2) | 2 | 40 | 42.0 ms | 67.9 ms | 71.7 ms | `decoderImplementation: null`, `powerEfficientDecoder: null`, `framesDecoded: ~1388-1413`, `framesDropped: 0` on all 4 tiles |

`receive_to_decoded` carries **no budget** (ADR-0122/constitution §IV) — it is
a fragment of the SFU→kiosk decode leg, not the leg itself (the browser
cannot see the sending end). Read descriptively: the 9-tile wall's fragment
ran markedly higher (166-266 ms) than the 4-tile wall's (40-72 ms) — a ~4-6×
difference against roughly 2× the tile count, consistent with the same
cadence/contention pressure M1 found, now showing up on the decode side too.

**Decoder implementation fields were only captured on the 4-tile wall's two
runs** (the throwaway harness that read `getStats()` directly via a wrapped
`RTCPeerConnection`; the 9-tile measurement reused the unmodified, committed
test, which deliberately does not read `inbound-rtp` stats a second time —
see that test's own comment, "deliberately not a second reader of the WebRTC
inbound-rtp statistics"). On the 4-tile wall, **`decoderImplementation` and
`powerEfficientDecoder` both read `null` on every tile, in both runs** — this
Chromium build/platform did not populate those two fields in this
environment, so **this measurement cannot say whether decode ran in hardware
or software** on this machine; it can only say `framesDropped: 0` on every
tile in both runs — no dropped frames observed at 4 tiles. **Decoder
implementation was not captured at all for the 9-tile wall** (no time was
spent building a parallel `getStats()` reader for the unmodified 9-tile test
within this issue's scope) — recorded as a gap, not papered over.

### A finding this measurement surfaced, not asked for but worth recording

**Running the 9-tile measurement back-to-back with the 4-tile one, in the same
Playwright worker, destabilised it.** Two separate attempts where the 4-tile
test ran immediately before the 9-tile test both failed the 9-tile test's own
existing assertion — a different tile each time "contributed only 4 [then 7]
overlay_draw sample(s), not the 10 the span loop drove — it drew once at
mount and then froze rather than redrawing with the rest of the wall." The
*same* 9-tile test, run standalone (no preceding 4-tile load in the same
worker), passed cleanly twice (the two clean runs quoted above). This matches
an already-known, already-documented flake for this exact test and symptom
(unrelated prior PRs, same day, with `develop`'s own head green) — so it is
not reported here as a new regression this issue introduced — but it
recurred in 2 of 3 attempts specifically when run alongside the second wall,
which is itself a plausible dev-machine capacity signal (cumulative
decode/render load across back-to-back kiosk sessions) worth a human's
attention rather than a conclusion this report draws on its own.

The one 4-tile run most affected by the same back-to-back condition also
degraded, differently: it refused its own 6th iteration ("the value never
painted on the tile within 51779 ms … submit answered 409") after 5 valid
samples, rather than silently freezing. That attempt's partial figures are
not included in the tables above — only the two **clean, standalone** 4-tile
runs are.

### Summary against the three ADR-0123 budgets

| Budget | 9-tile dev-machine reading | 4-tile dev-machine reading |
|---|---|---|
| Composite + render ≤ 50 ms | **Fails** both clean runs (p50 57.4-70.4 ms) | **Fails** both clean runs (p50 50.5-62.9 ms) |
| SFU→kiosk decode ≤ 120 ms | Fragment only (`receive_to_decoded`, no budget attached); descriptively high (166-266 ms) relative to the 4-tile wall | Fragment only; descriptively low (40-72 ms) |
| Cadence ≥ 40 Hz (tail) / ≥ 30 Hz (median) | **Fails tail** every reading (≈24-27 Hz); **fails median** on 3 of 4 before/after readings, one reading exactly at the 30 Hz line | **Fails tail** every reading (≈26-32 Hz); **passes median** on 3 of 4 readings, one at ≈26 Hz fails |

None of this upgrades any §IV cell — per ADR-0138, these are dev-machine
figures about the code under this change, not about the fleet, and the
honest reading is that the composite+render budget is **already** unmet on
this machine even at the *old* 4-tile cap, which the 9-tile cap does not
newly break so much as make worse. **M3 — the same measurement on the fab's
actual kiosk hardware/model — is what would tell a reader whether that
matters, and it has not been performed.** This issue stays open on that
precondition; nothing here closes it.
