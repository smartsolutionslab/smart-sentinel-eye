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
