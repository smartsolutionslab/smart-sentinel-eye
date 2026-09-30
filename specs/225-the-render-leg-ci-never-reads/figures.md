# Figures — Spec 225, the composite-and-render leg (§IV, ≤ 50 ms)

**T015–T017 (spec §10.5, plan §9.3).** Collected 2026-09-30 against `origin/develop`
at `419f4f20`, on branch `ci/2337-render-leg-gate-completion`. Method:
`gh run download <id> -p 'playwright-report-*' -D <dir>` for every run in the
window, then `readRenderLegRecords` from `e2e/support/render-leg.ts`, invoked
under plain `node` (Node 24, type-stripped `.ts` import) — the same approach
spec 294's `verification.md` T014 used. No scratch script is committed; this
file and the rule that produced it are what the tree carries.

**Window (FR-022, fixed before this file was written — spec §10.5):** every
`develop` **push** run of `ci.yml` in which the span test ran on the nine-tile
fixture with `05061543` in its history, from run **36351086768** (the first
nine-tile run) up to the collection date, **nothing dropped**. `gh run list
--branch develop --event push --limit 100` was read back to
`origin/develop`'s current tip and filtered to `databaseId >= 36351086768`,
giving **22** `develop` push runs. Every one of them is accounted for below —
21 with a render-leg record, 1 with none.

**Artifact expiry.** Retention is 14 days. The oldest eligible run
(36351086768, created 2026-09-27T21:16:09Z) expires around 2026-10-11; every
run in the window was downloaded today, 2026-09-30, well inside that deadline,
so no row here lost its raw samples to expiry. The three earlier one-tile runs
(§ below) were also still available and were downloaded for the same reason.

---

## `develop` baseline (nine-tile fixture, CI)

All 21 runs below are `complete: true`, attempt 0 (no retries fired), with
`count: 108` (9 tiles × 12 draws — the `ITERATIONS = 10` loop plus the
existing 2-draws-of-slack the wall's own per-camera assertion tolerates).
`p95` is computed at n = 108, which supports one (`kiosk-shows-a-label-over-video.spec.ts:756-766`'s
own index rule). Figures are **truncated** to 0.01 ms, never rounded up (plan
§5), including the raw samples.

| Run id | SHA | Samples | p50 | p95 | max | Observed T (before → after) | Raw |
|---|---|---|---|---|---|---|---|
| [36351086768](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36351086768) | `44651124b6981bf9a657a4f864fe595a14d95dd4` | 108 | 54.75 ms | 75.20 ms | 88.50 ms | 31.76 → 32.25 ms/frame | [76.00, 77.79, 77.80, 77.90, 73.79, 68.40, 68.50, 65.00, 57.30, 31.59, 31.80, 32.00, 32.30, 32.50, 32.59, 32.79, 33.00, 33.19, 61.50, 62.09, 62.59, 62.90, 63.29, 65.50, 66.00, 66.30, 66.59, 35.50, 36.00, 36.40, 36.80, 37.20, 37.40, 37.80, 38.19, 40.69, 68.69, 65.90, 73.69, 74.09, 74.40, 74.69, 74.90, 75.20, 88.50, 58.69, 59.40, 59.79, 60.00, 63.00, 63.40, 65.90, 69.00, 71.09, 50.79, 51.19, 51.50, 51.80, 52.19, 54.19, 55.50, 56.09, 56.59, 36.50, 37.40, 38.20, 39.00, 39.30, 39.60, 45.10, 45.40, 45.59, 24.20, 24.70, 27.00, 28.10, 28.19, 28.59, 32.19, 32.69, 33.09, 58.59, 59.19, 59.50, 59.90, 60.09, 60.59, 65.80, 68.50, 68.90, 49.50, 50.20, 50.79, 51.19, 51.59, 51.89, 52.19, 52.59, 54.59, 52.79, 53.29, 53.50, 54.90, 55.30, 55.69, 57.50, 58.09, 58.80] |
| [36405585641](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36405585641) | `3b046175ec587ff5d052ad0acbef5e529eb21eeb` | 108 | 82.34 ms | 139.40 ms | 179.00 ms | 32.28 → 29.89 ms/frame | [108.19, 99.70, 99.60, 99.70, 92.70, 92.50, 87.80, 88.40, 88.40, 26.20, 28.70, 29.80, 30.20, 30.39, 36.50, 41.19, 41.59, 42.09, 35.69, 36.40, 44.70, 45.20, 55.80, 56.20, 58.89, 62.59, 70.09, 110.69, 111.09, 111.50, 109.29, 109.69, 110.00, 126.40, 126.80, 127.30, 111.09, 88.39, 90.00, 91.59, 92.90, 94.59, 98.30, 98.69, 99.39, 179.00, 161.00, 109.89, 110.09, 99.79, 100.50, 101.90, 103.69, 104.80, 77.80, 86.09, 46.20, 40.39, 43.79, 44.50, 48.59, 49.30, 45.40, 50.80, 59.90, 60.59, 43.40, 45.50, 47.20, 55.90, 56.79, 60.29, 90.19, 90.70, 51.00, 53.80, 54.30, 54.80, 55.20, 55.50, 58.50, 91.59, 84.29, 37.59, 38.00, 38.30, 38.60, 24.50, 25.39, 26.29, 140.10, 124.50, 124.89, 81.50, 82.09, 82.59, 80.00, 81.69, 83.90, 66.29, 166.10, 166.59, 139.40, 118.19, 118.69, 118.90, 121.90, 122.19] |
| [36421356179](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36421356179) | `c709495bb6135d9ee4a507c7bb5d7ce4342eb40f` | 108 | 49.49 ms | 71.40 ms | 81.70 ms | 32.28 → 32.28 ms/frame | [63.19, 63.50, 63.50, 63.40, 52.20, 52.19, 51.80, 51.69, 51.70, 71.40, 72.59, 66.70, 63.30, 63.50, 60.00, 55.80, 51.59, 46.80, 16.90, 16.10, 17.20, 17.80, 18.09, 18.59, 20.09, 20.39, 20.50, 42.09, 43.90, 45.40, 46.69, 48.00, 48.29, 48.50, 53.40, 53.80, 31.29, 33.59, 42.40, 43.30, 47.00, 47.30, 47.40, 47.59, 47.80, 41.89, 41.39, 41.79, 43.79, 44.00, 44.29, 49.29, 49.69, 50.00, 47.90, 49.90, 52.69, 53.19, 53.30, 53.59, 55.40, 55.70, 55.90, 27.50, 28.10, 29.89, 31.19, 32.59, 32.79, 32.90, 32.90, 35.09, 53.10, 53.69, 53.90, 54.19, 54.59, 54.90, 55.09, 55.29, 55.69, 55.69, 56.09, 56.30, 56.50, 56.80, 57.00, 43.30, 43.80, 41.19, 72.00, 74.20, 79.00, 81.70, 43.60, 48.29, 49.00, 44.09, 44.50, 61.09, 63.40, 64.30, 65.70, 50.40, 51.19, 46.30, 39.50, 39.80] |
| [36434265198](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36434265198) | `0b949ef5be15cceeb2643b26b1f463f3bf119b1a` | 108 | 56.39 ms | 67.29 ms | 85.09 ms | 29.04 → 32.25 ms/frame | [85.09, 80.20, 75.90, 70.79, 65.39, 60.10, 56.89, 52.09, 48.09, 32.79, 33.70, 34.00, 34.20, 34.50, 34.79, 35.09, 40.20, 40.60, 37.09, 37.50, 37.90, 38.20, 38.50, 39.29, 39.70, 40.20, 40.50, 45.69, 46.50, 47.09, 47.50, 47.70, 48.10, 48.40, 50.70, 51.10, 54.29, 56.09, 56.69, 58.09, 59.50, 60.40, 60.90, 61.20, 61.80, 59.60, 60.30, 60.80, 61.20, 61.50, 61.79, 65.00, 65.29, 67.20, 26.09, 28.70, 29.00, 29.40, 29.70, 30.00, 30.20, 30.39, 30.69, 53.89, 54.40, 54.79, 55.10, 58.39, 59.29, 60.00, 60.29, 60.59, 55.00, 58.50, 60.79, 61.70, 62.50, 66.50, 66.89, 67.29, 67.69, 59.39, 60.09, 60.59, 60.79, 61.09, 62.39, 62.79, 63.19, 63.59, 46.79, 47.29, 47.50, 47.69, 48.00, 48.10, 48.30, 48.70, 49.00, 59.10, 59.59, 59.89, 60.10, 60.40, 60.70, 60.90, 61.10, 63.79] |
| [36444024298](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36444024298) | `edf41b97530dc9daf7f97fab18550a7e9a61b723` | 108 | 57.25 ms | 90.00 ms | 109.19 ms | 32.28 → 32.25 ms/frame | [105.00, 99.29, 90.00, 79.70, 80.00, 78.59, 78.59, 70.09, 70.50, 60.90, 59.79, 63.89, 64.39, 70.59, 71.09, 74.90, 75.19, 87.50, 50.50, 52.89, 53.19, 53.90, 54.40, 56.00, 56.19, 56.50, 59.40, 29.40, 28.90, 29.40, 29.69, 30.09, 30.59, 31.59, 32.40, 34.40, 34.09, 34.59, 35.00, 35.40, 37.30, 37.59, 45.50, 46.00, 46.29, 85.30, 85.90, 86.50, 86.80, 86.59, 86.90, 87.29, 100.90, 102.30, 79.20, 66.19, 66.50, 58.00, 58.19, 53.59, 59.89, 49.39, 49.79, 89.09, 67.59, 60.80, 61.10, 52.59, 42.00, 42.19, 47.00, 45.50, 85.40, 67.59, 68.00, 56.40, 58.00, 46.50, 41.60, 41.90, 38.59, 109.19, 84.29, 85.40, 85.69, 84.79, 71.09, 74.29, 74.59, 68.59, 33.90, 51.29, 52.69, 53.00, 49.69, 47.20, 48.59, 50.09, 50.59, 30.89, 63.10, 59.30, 60.09, 56.00, 50.50, 46.60, 45.29, 54.09] |
| [36456616486](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36456616486) | `8e1d173bd18dd4c0b251fd6589e24173930fec64` | 108 | 51.94 ms | 82.40 ms | 90.09 ms | 32.25 → 28.57 ms/frame | [76.20, 77.40, 73.90, 73.79, 67.29, 63.09, 60.59, 56.40, 56.50, 23.00, 21.70, 22.09, 22.40, 24.80, 25.00, 30.50, 32.39, 32.79, 40.09, 40.69, 41.09, 41.50, 41.70, 42.40, 44.00, 44.50, 45.30, 35.29, 35.80, 41.40, 41.80, 43.10, 43.50, 44.10, 45.40, 49.30, 67.50, 67.20, 67.60, 68.10, 68.40, 68.59, 76.00, 76.39, 76.69, 58.69, 59.29, 59.59, 59.90, 60.19, 60.50, 64.90, 68.00, 68.30, 42.09, 45.50, 46.09, 46.40, 46.90, 47.40, 47.70, 48.09, 48.50, 39.40, 40.59, 41.00, 43.29, 45.19, 45.59, 46.00, 46.80, 47.09, 57.80, 58.30, 58.70, 59.09, 61.00, 61.50, 62.10, 62.60, 64.00, 31.80, 33.60, 38.20, 40.50, 43.00, 43.29, 46.90, 49.69, 50.09, 66.09, 62.39, 62.40, 58.20, 53.79, 60.09, 60.69, 63.00, 56.40, 82.90, 78.00, 73.89, 89.39, 90.09, 81.80, 82.40, 85.50, 87.19] |
| [36458326373](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36458326373) | `aa08ce169ea999aba5c44f77196d6f0419735697` | 108 | 46.84 ms | 124.00 ms | 147.59 ms | 31.76 → 32.26 ms/frame | [147.59, 141.00, 134.29, 121.50, 120.30, 120.40, 110.70, 110.50, 112.90, 27.59, 28.09, 25.69, 25.90, 32.09, 33.19, 33.50, 33.80, 34.09, 27.00, 27.60, 28.40, 29.20, 30.30, 32.90, 33.40, 34.10, 34.70, 58.59, 59.10, 59.60, 59.90, 60.90, 61.09, 71.50, 71.90, 76.69, 32.00, 32.69, 33.09, 33.30, 33.59, 34.00, 34.30, 34.70, 35.10, 120.50, 121.60, 123.19, 124.40, 125.79, 124.00, 61.30, 61.90, 68.90, 36.30, 37.59, 60.00, 60.70, 55.30, 51.40, 46.59, 46.79, 46.40, 44.90, 40.69, 41.00, 41.20, 55.19, 56.59, 41.29, 41.79, 44.59, 87.30, 87.80, 88.09, 84.69, 77.30, 77.30, 78.50, 78.90, 79.20, 55.00, 58.50, 54.40, 51.69, 54.90, 35.30, 35.60, 38.59, 39.70, 57.40, 57.90, 43.40, 44.09, 21.40, 22.00, 23.00, 23.29, 23.79, 55.40, 46.59, 46.90, 45.40, 38.10, 38.39, 58.50, 60.90, 57.00] |
| [36482189965](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36482189965) | `2cbd044b4534ec4344a3960044234d9cda6c8e5b` | 108 | 50.00 ms | 98.00 ms | 133.09 ms | 32.25 → 32.28 ms/frame | [44.59, 46.20, 48.09, 49.20, 50.50, 50.69, 49.80, 49.70, 49.70, 24.89, 25.50, 25.70, 26.09, 26.40, 26.70, 27.70, 28.00, 28.29, 58.20, 58.79, 59.09, 59.50, 59.70, 60.00, 63.59, 63.90, 69.79, 54.00, 56.50, 56.59, 56.89, 61.59, 61.79, 62.10, 66.39, 66.69, 27.50, 29.29, 30.29, 30.70, 31.50, 34.10, 34.60, 35.69, 38.09, 82.00, 65.20, 53.10, 56.90, 42.50, 47.09, 47.29, 39.39, 46.79, 94.09, 89.90, 47.20, 47.29, 27.00, 27.90, 29.29, 39.59, 44.00, 53.20, 39.40, 39.80, 42.40, 43.80, 44.50, 41.09, 42.00, 66.50, 77.79, 68.59, 61.90, 62.29, 62.79, 61.30, 60.00, 53.40, 48.29, 92.00, 67.20, 67.50, 67.79, 41.90, 48.29, 50.20, 67.60, 68.00, 95.50, 81.29, 43.60, 46.20, 46.59, 47.00, 47.29, 43.79, 44.20, 98.00, 133.09, 97.50, 97.79, 98.10, 98.39, 102.09, 97.60, 98.79] |
| [36491446212](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36491446212) | `ba8c5041147a0c510be63b2537420a8d20c0e4d4` | 108 | 70.90 ms | 112.69 ms | 131.00 ms | 32.29 → 29.04 ms/frame | [110.90, 104.69, 97.59, 91.50, 85.00, 79.40, 71.59, 68.20, 63.10, 21.50, 22.30, 22.80, 23.00, 23.40, 23.70, 24.00, 24.29, 24.59, 36.90, 38.69, 38.90, 39.20, 39.70, 39.79, 40.19, 43.59, 43.79, 73.50, 74.59, 74.90, 85.69, 86.29, 86.69, 87.00, 87.30, 97.09, 77.79, 77.80, 77.30, 77.40, 80.00, 80.39, 80.59, 81.00, 72.59, 95.09, 87.50, 82.29, 78.09, 80.29, 80.59, 74.70, 79.60, 79.09, 72.60, 60.19, 44.00, 43.40, 44.40, 44.79, 53.00, 53.50, 53.90, 32.79, 64.50, 63.40, 108.19, 108.90, 109.29, 91.19, 91.59, 97.19, 58.09, 48.59, 34.09, 34.29, 50.20, 50.90, 51.20, 56.10, 46.19, 105.30, 127.39, 127.90, 131.00, 104.39, 111.89, 112.69, 113.09, 104.60, 87.09, 70.20, 47.50, 52.39, 52.79, 53.09, 60.19, 60.89, 61.29, 120.09, 102.30, 78.69, 44.80, 45.30, 46.79, 48.69, 22.69, 24.00] |
| [36498579914](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36498579914) | `3cb08cb2c53163b9acc3ccab018ade85f2164f7b` | 108 | 49.80 ms | 110.40 ms | 116.59 ms | 32.29 → 28.57 ms/frame | [87.20, 87.50, 87.59, 83.30, 83.19, 79.40, 81.20, 81.30, 81.70, 26.20, 28.50, 28.59, 28.80, 29.90, 30.50, 30.70, 32.40, 32.70, 30.80, 38.20, 39.00, 41.70, 42.10, 42.39, 52.00, 52.59, 53.79, 40.20, 40.80, 41.10, 41.60, 41.90, 42.19, 57.09, 57.50, 65.29, 40.39, 41.09, 41.40, 42.59, 44.40, 44.59, 45.00, 49.70, 49.90, 76.19, 71.10, 64.20, 65.50, 33.00, 33.40, 33.69, 36.00, 35.69, 69.00, 63.90, 62.90, 37.59, 38.29, 38.59, 40.30, 42.90, 46.00, 50.00, 49.70, 46.90, 40.09, 38.59, 67.00, 64.39, 65.00, 63.90, 54.70, 38.50, 43.90, 32.19, 24.69, 28.80, 29.30, 30.50, 28.40, 69.70, 64.79, 116.59, 111.80, 101.69, 91.90, 90.00, 91.10, 94.50, 40.09, 58.09, 61.29, 47.59, 64.09, 64.70, 65.00, 58.80, 57.59, 101.30, 97.79, 93.20, 110.00, 110.40, 110.60, 111.00, 111.20, 101.50] |
| [36538160799](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36538160799) | `082778553da67a70c2f2bef28883d4773689e68f` | 108 | 50.60 ms | 147.39 ms | 158.19 ms | 31.77 → 30.39 ms/frame | [29.90, 29.79, 29.80, 28.20, 28.20, 28.00, 28.00, 28.20, 32.09, 30.19, 30.69, 31.00, 31.19, 31.40, 31.29, 31.69, 31.90, 32.19, 45.40, 47.40, 49.09, 49.50, 49.90, 50.00, 50.40, 60.80, 61.20, 81.09, 79.40, 79.59, 84.09, 88.09, 91.00, 65.59, 66.29, 59.50, 23.00, 23.79, 24.29, 24.50, 24.80, 25.19, 27.90, 67.30, 60.50, 158.19, 147.39, 147.79, 150.50, 151.50, 152.59, 129.09, 112.19, 108.89, 68.70, 53.40, 46.69, 60.79, 61.29, 62.19, 63.30, 52.79, 44.60, 70.19, 47.30, 38.09, 41.90, 42.40, 47.90, 42.19, 42.50, 37.19, 73.90, 75.59, 72.10, 60.19, 60.59, 60.79, 50.40, 50.80, 32.80, 80.10, 81.80, 95.50, 82.50, 83.30, 83.80, 87.09, 87.50, 87.79, 25.10, 19.50, 60.30, 91.90, 92.50, 88.29, 88.69, 89.00, 86.90, 39.59, 34.59, 32.00, 47.90, 49.59, 43.80, 45.09, 37.70, 73.09] |
| [36550895121](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36550895121) | `c4c85d2f88c6ccc3e8aa2c31603e223e93170ea6` | 108 | 65.35 ms | 109.50 ms | 173.00 ms | 32.25 → 31.77 ms/frame | [69.00, 71.10, 71.40, 71.69, 71.80, 71.90, 71.79, 72.00, 72.09, 33.09, 35.69, 38.50, 39.00, 39.30, 41.40, 48.00, 48.40, 60.90, 42.90, 43.80, 44.10, 45.29, 45.50, 45.90, 46.19, 46.40, 48.40, 61.90, 63.69, 64.30, 64.70, 88.30, 89.40, 91.70, 92.89, 98.09, 23.70, 24.20, 28.90, 36.60, 44.09, 48.69, 51.69, 57.79, 58.40, 80.30, 81.00, 81.29, 81.59, 57.50, 21.00, 21.30, 21.80, 21.89, 34.50, 60.69, 66.00, 77.59, 78.30, 79.00, 70.79, 80.29, 80.69, 109.50, 89.00, 74.80, 46.70, 47.59, 50.00, 26.30, 26.80, 36.50, 100.59, 95.09, 95.40, 79.40, 62.09, 63.90, 55.50, 56.40, 56.70, 70.59, 78.09, 67.00, 69.09, 82.00, 80.80, 30.30, 30.90, 31.30, 110.30, 90.59, 95.09, 104.90, 73.00, 172.00, 172.69, 173.00, 169.00, 93.69, 74.90, 68.40, 56.29, 57.09, 76.50, 77.09, 77.39, 73.00] |
| [36564234273](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36564234273) | `af79b5ac5b9d6af3e376c71ee3bd9b0e50a451f3` | 108 | 57.75 ms | 94.00 ms | 124.90 ms | 31.77 → 32.28 ms/frame | [83.79, 77.50, 77.50, 72.89, 71.59, 68.60, 65.70, 62.70, 62.59, 13.89, 17.89, 18.29, 18.50, 18.79, 19.09, 19.20, 19.50, 23.40, 54.50, 55.90, 57.59, 57.90, 58.10, 58.40, 58.60, 64.70, 67.20, 45.90, 46.50, 46.69, 47.09, 47.39, 47.69, 48.00, 49.00, 49.40, 59.09, 59.70, 60.90, 61.70, 62.10, 62.30, 65.60, 66.20, 66.70, 39.10, 40.50, 41.79, 42.10, 42.40, 42.60, 44.29, 44.70, 45.00, 56.79, 59.59, 60.69, 62.00, 62.79, 63.19, 63.40, 59.00, 59.29, 50.29, 51.09, 51.50, 54.19, 55.50, 58.50, 58.70, 59.00, 59.40, 45.40, 46.09, 46.39, 46.70, 47.00, 47.20, 47.40, 47.60, 49.29, 67.20, 69.20, 69.50, 69.79, 70.10, 70.40, 94.00, 92.59, 91.39, 33.29, 22.70, 25.29, 20.20, 20.50, 26.90, 27.50, 48.29, 51.69, 101.00, 100.20, 99.19, 86.70, 71.50, 72.09, 72.20, 124.90, 124.40] |
| [36578982802](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36578982802) | `7ef12d7e0bac007d4aa95cd179d10f3805d9f032` | 108 | 55.79 ms | 91.00 ms | 141.50 ms | 32.25 → 32.29 ms/frame | [95.50, 97.00, 91.89, 89.60, 89.90, 90.80, 90.90, 91.00, 90.90, 25.89, 27.50, 27.79, 28.09, 28.50, 28.59, 28.90, 29.20, 35.00, 41.79, 43.19, 44.09, 45.50, 47.90, 48.30, 48.80, 49.40, 52.39, 46.70, 47.30, 47.70, 50.10, 50.50, 52.80, 53.00, 53.39, 60.19, 54.19, 54.80, 55.20, 55.70, 55.89, 56.29, 56.69, 82.09, 82.59, 141.30, 141.50, 48.00, 48.79, 49.39, 31.10, 33.89, 35.59, 38.00, 56.40, 72.89, 68.59, 68.09, 67.59, 65.30, 65.70, 64.69, 62.40, 52.69, 48.70, 43.79, 42.50, 65.30, 55.20, 44.60, 45.10, 45.40, 79.70, 78.19, 80.00, 60.29, 49.59, 48.50, 72.40, 73.00, 73.50, 51.09, 42.90, 72.09, 78.00, 70.30, 69.80, 79.40, 79.90, 85.30, 42.40, 75.50, 81.00, 81.59, 73.00, 73.40, 73.90, 75.50, 75.90, 72.20, 63.39, 62.50, 54.29, 55.19, 50.09, 50.59, 64.59, 65.09] |
| [36624326723](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36624326723) | `f484b51b4e6a7fa0244cb001ffd886497c001b07` | 108 | 50.54 ms | 66.90 ms | 69.09 ms | 31.76 → 32.29 ms/frame | [69.09, 65.50, 66.79, 63.00, 64.90, 60.90, 62.50, 62.69, 62.69, 34.50, 34.89, 35.09, 35.29, 36.09, 36.29, 36.59, 36.79, 37.00, 62.30, 62.90, 63.90, 64.70, 65.00, 65.19, 68.00, 68.29, 68.50, 48.29, 49.00, 49.19, 46.90, 47.79, 48.00, 51.59, 51.90, 52.19, 52.59, 54.50, 55.59, 56.40, 57.50, 58.19, 61.09, 61.50, 61.69, 61.89, 63.19, 63.50, 63.69, 64.09, 64.19, 66.79, 66.90, 67.19, 39.40, 41.40, 42.09, 42.40, 44.50, 46.00, 46.50, 48.30, 48.50, 33.59, 35.50, 35.50, 35.69, 40.09, 40.40, 40.59, 41.09, 41.40, 54.90, 55.69, 57.70, 58.00, 58.29, 58.50, 60.09, 60.29, 60.59, 42.70, 43.39, 43.59, 43.79, 43.50, 43.79, 44.00, 44.40, 44.50, 49.30, 50.59, 50.90, 51.70, 50.29, 50.50, 50.69, 51.00, 51.09, 44.20, 47.69, 48.19, 48.59, 48.90, 49.19, 49.69, 50.09, 50.29] |
| [36661215565](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36661215565) | `d65418a1f0e5db1140d4e0500a1e32519eadcf1d` | 108 | 57.14 ms | 93.80 ms | 100.09 ms | 30.39 → 32.28 ms/frame | [36.50, 39.30, 40.00, 40.59, 41.30, 43.70, 43.59, 45.80, 45.40, 29.90, 32.19, 32.50, 33.00, 33.29, 33.69, 34.09, 34.59, 35.00, 56.50, 57.00, 57.40, 57.59, 57.80, 58.09, 58.30, 58.50, 58.90, 40.70, 41.30, 40.00, 40.29, 40.40, 40.69, 40.90, 41.30, 41.59, 89.30, 92.50, 93.30, 93.80, 94.20, 99.40, 99.59, 99.89, 100.09, 54.40, 55.20, 55.80, 56.09, 60.20, 60.90, 61.00, 61.19, 61.50, 44.80, 45.50, 48.20, 48.60, 49.00, 49.30, 49.39, 49.79, 49.69, 56.19, 56.90, 57.29, 57.79, 58.59, 58.90, 59.79, 60.19, 60.50, 58.90, 59.60, 59.89, 60.29, 60.89, 61.79, 62.00, 62.30, 62.59, 51.50, 48.39, 48.69, 51.19, 51.40, 53.00, 55.19, 55.59, 55.59, 56.69, 57.69, 59.50, 59.80, 63.09, 63.30, 63.59, 63.90, 64.09, 61.30, 62.10, 62.50, 62.80, 63.80, 64.20, 64.69, 66.59, 67.79] |
| [36666872927](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36666872927) | `dcd686942e88ce8c9391bd062e38cce50477ea32` | 108 | 66.85 ms | 134.20 ms | 166.90 ms | 32.29 → 26.75 ms/frame | [110.90, 110.90, 102.69, 96.50, 96.50, 88.80, 83.50, 79.00, 73.89, 33.00, 34.09, 35.20, 35.70, 35.89, 36.19, 36.30, 36.69, 36.90, 64.50, 66.00, 67.60, 68.19, 71.29, 75.39, 75.69, 76.09, 76.40, 48.80, 49.59, 49.80, 58.00, 62.00, 62.50, 62.79, 63.19, 63.50, 58.30, 64.89, 65.39, 68.19, 68.50, 68.90, 69.30, 120.20, 134.20, 30.30, 31.10, 31.39, 35.50, 39.90, 40.29, 43.90, 16.80, 18.30, 80.79, 77.69, 54.09, 56.00, 57.50, 103.29, 106.69, 107.09, 106.09, 62.59, 73.40, 67.30, 62.80, 53.90, 47.00, 47.79, 48.39, 49.00, 82.19, 74.00, 70.80, 69.50, 66.00, 60.40, 55.19, 90.80, 91.40, 166.90, 152.59, 155.40, 144.39, 145.90, 128.00, 126.59, 127.20, 127.50, 62.29, 55.29, 55.59, 52.30, 94.90, 84.70, 83.59, 62.79, 63.19, 73.50, 66.40, 56.30, 56.59, 89.79, 80.39, 76.39, 68.19, 68.50] |
| [36674174440](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36674174440) | `ca5d1c6c2b56a920f738684349b26424600017d2` | 108 | 59.55 ms | 74.70 ms | 85.39 ms | 29.41 → 32.25 ms/frame | [47.30, 47.70, 44.69, 41.40, 41.40, 38.20, 34.50, 28.80, 25.09, 27.00, 27.09, 28.20, 28.50, 28.69, 28.89, 29.50, 30.00, 32.29, 54.59, 55.09, 55.30, 55.80, 56.69, 57.09, 58.09, 58.50, 58.80, 48.19, 48.69, 49.09, 49.40, 49.69, 49.90, 50.09, 50.40, 55.80, 56.30, 57.80, 59.40, 59.80, 60.10, 60.20, 60.50, 60.59, 60.89, 70.59, 72.30, 73.80, 74.70, 74.90, 75.10, 75.40, 75.70, 85.39, 54.79, 55.69, 56.09, 56.50, 59.90, 61.29, 62.00, 62.40, 62.80, 59.80, 60.50, 60.70, 62.30, 62.90, 63.00, 63.19, 63.39, 63.59, 56.90, 57.59, 57.90, 58.30, 58.50, 59.00, 59.20, 59.40, 59.70, 60.70, 61.40, 61.70, 62.00, 62.20, 63.09, 63.80, 64.00, 65.40, 58.09, 58.70, 59.40, 60.70, 61.20, 61.60, 63.00, 63.30, 63.59, 61.30, 61.90, 62.09, 62.40, 62.29, 62.50, 62.79, 63.00, 63.29] |
| [36697596703](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36697596703) | `34d46a23b81f38c85a1a0f93ae0e87a6a4a3c7a9` | 108 | 51.90 ms | 77.20 ms | 93.20 ms | 32.25 → 32.25 ms/frame | [93.20, 90.29, 84.20, 75.30, 68.89, 69.40, 69.70, 69.59, 69.69, 75.50, 75.89, 76.19, 76.59, 76.90, 77.20, 71.59, 58.29, 48.00, 63.40, 65.30, 65.59, 65.80, 66.09, 66.40, 66.59, 66.70, 68.10, 21.79, 22.50, 22.80, 27.50, 29.40, 29.90, 30.30, 30.70, 31.00, 44.69, 45.19, 45.40, 45.90, 46.09, 50.59, 51.10, 51.40, 51.80, 39.00, 39.69, 41.09, 41.50, 41.69, 42.79, 42.90, 44.30, 44.50, 42.20, 45.40, 46.90, 50.60, 51.00, 53.40, 53.59, 53.89, 54.39, 27.00, 51.00, 47.00, 48.30, 49.50, 44.19, 41.20, 41.50, 52.50, 50.90, 80.30, 73.69, 77.09, 83.39, 69.79, 71.09, 68.09, 56.39, 49.59, 34.70, 38.50, 39.09, 40.79, 33.50, 28.39, 32.79, 25.00, 75.50, 70.80, 72.40, 73.00, 73.29, 63.00, 63.40, 63.69, 50.30, 52.30, 76.00, 71.09, 60.40, 60.79, 50.69, 51.00, 55.40, 52.00] |
| [36715830923](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36715830923) | `e56c2b8eff33e11df8e14ea79cb301e5d7efea6a` | 108 | 53.59 ms | 74.70 ms | 91.90 ms | 31.30 → 26.75 ms/frame | [91.90, 86.80, 82.09, 78.20, 74.59, 73.00, 73.70, 74.70, 75.69, 40.80, 41.30, 41.50, 41.90, 42.09, 42.30, 42.60, 44.60, 45.20, 50.20, 51.00, 51.50, 52.00, 52.40, 53.19, 54.09, 54.50, 55.59, 33.50, 34.69, 35.19, 35.59, 36.29, 38.50, 42.09, 43.40, 44.69, 53.19, 53.59, 54.09, 54.60, 55.60, 56.30, 56.79, 57.19, 58.29, 49.30, 49.69, 50.19, 50.50, 50.69, 51.09, 51.29, 51.40, 54.50, 21.09, 21.59, 22.00, 22.30, 22.59, 22.80, 22.90, 23.10, 29.00, 55.80, 56.29, 59.29, 61.09, 61.90, 66.30, 67.90, 70.80, 71.19, 54.09, 55.70, 56.60, 56.90, 57.59, 58.90, 59.30, 59.70, 59.90, 49.40, 53.30, 53.59, 54.00, 57.30, 65.59, 66.00, 66.20, 66.70, 62.59, 63.20, 67.80, 68.10, 68.40, 72.59, 73.00, 73.39, 73.69, 50.80, 35.90, 36.19, 38.80, 40.80, 41.19, 41.30, 44.00, 42.29] |
| [36737867431](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36737867431) | `419f4f204cf00b7b640a9a3e00b096ea73b7ac40` | 108 | 50.45 ms | 61.29 ms | 64.59 ms | 30.39 → 32.28 ms/frame | [56.00, 52.60, 49.29, 49.09, 46.00, 47.00, 45.20, 41.40, 38.59, 19.00, 20.19, 20.30, 20.40, 20.70, 20.90, 21.20, 22.80, 24.40, 44.80, 45.69, 46.79, 49.89, 50.19, 50.50, 56.30, 56.69, 57.09, 47.29, 49.09, 49.59, 49.89, 50.09, 50.40, 50.79, 59.80, 60.30, 55.60, 58.00, 58.39, 59.69, 60.00, 60.50, 60.90, 61.29, 62.09, 38.19, 38.90, 39.09, 39.40, 40.59, 41.00, 41.30, 41.50, 42.00, 51.29, 56.09, 56.40, 56.59, 56.80, 60.80, 61.00, 61.30, 64.59, 54.09, 56.09, 57.09, 57.40, 58.90, 59.70, 61.60, 60.59, 60.90, 51.79, 52.30, 53.59, 53.90, 54.50, 54.59, 54.90, 55.09, 55.40, 19.70, 21.00, 21.30, 21.59, 22.60, 23.20, 23.89, 24.89, 25.89, 55.79, 61.30, 60.70, 51.19, 46.09, 41.09, 41.30, 41.59, 39.19, 57.09, 54.70, 55.30, 54.00, 50.79, 39.40, 41.09, 36.50, 36.90] |

**Footnotes on the two rightmost rows:** run 36715830923 (`e56c2b8e`) is the
first run in this window to carry commit `b635a48a` ("convert the overlay
label's surface to design tokens") in its history; run 36737867431
(`419f4f20`) additionally carries `9163bf00` ("size a label's type against
its tile, not the viewport") — the render-path change spec 294 measured
separately (`specs/294-the-type-that-ignored-its-tile/verification.md` T014,
PASS, neutral). Per spec §10.5, these rows are **marked, not excluded**: a
render-path change inside the window is not grounds to drop a row, and if a
render change should ever start a new window that is a re-baseline with its
own visible diff.

### The 22nd run: no measurement attempted

Run **36491424136** (SHA `a3677e45d746f10e85cd8a0402aab27d00b0f596`,
2026-09-28T22:16:44Z) is a `develop` push in the window whose single matrix
shard job for `e2e (Playwright, full stack)` shows `${{ matrix.shard }}/4` —
the matrix variable never resolved — with conclusion `cancelled`, and
produced **zero** `playwright-report-*` artifacts (`gh run download` returned
"no valid artifacts found to download"). No shard ran, so no measurement was
attempted. Per FR-022/§10.6 (FR-023's rule for the eventual `render-leg-gate`
job), this is listed with its reason and does **not** count as a refusal —
a refusal is a run where the span test ran and produced no complete
measurement; here nothing ran at all.

### A discrepancy from spec §10.5's own count, found and kept rather than silently matched

Spec §10.5 lists **20** nine-tile runs and their p50s. This collection finds
**21** complete records in the same window — every run above **except**
36456616486 matches spec §10.5's 20 exactly (to the rounding difference
between truncation and `toFixed`). The 21st is:

| Run id | SHA | e2e-shards shard 4/4 status | Record |
|---|---|---|---|
| [36456616486](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/36456616486) | `8e1d173bd18dd4c0b251fd6589e24173930fec64` | job API conclusion: **cancelled** (shards 1/4 and 2/4: success; 3/4 and 4/4: cancelled) | `complete: true`, `count: 108`, p50 51.94 ms — same shape as every other row |

The workflow run's overall conclusion is also `cancelled` (a later push likely
cancelled it via the repo's concurrency group after shard 4 had already
finished its work, written its record and uploaded its artifact — the
artifact and the record are there, fully formed, not a partial write). This
is genuinely ambiguous: the job-status API says the shard was cancelled, but
a complete, self-consistent measurement exists for it, indistinguishable in
shape from every uncontested row.

**Per FR-022 — "every run in which the span test ran … nothing inside the
window is dropped … not truncated to exclude an outlier" — the literal rule
is that the span test *did* run here and produced a complete record, so this
row is INCLUDED in the 21-run figures above and in the primary statistics
below.** Dropping it because its GitHub Actions job label reads "cancelled",
when the artifact proves a full measurement was taken, is exactly the kind
of after-the-fact selection FR-022 exists to forbid — especially since (as
the arithmetic below shows) this run's inclusion or exclusion does not
change T018's verdict, so there is no incentive-shaped reason to have
picked either way and no reason not to say so plainly. Both the 20-run and
21-run statistics are shown below for a reader who wants to reproduce spec
§10.5's own number.

---

## Verdict

**Primary (FR-022, all 21 runs, nothing dropped):**

- n = 21
- mean p50 = **56.63 ms**
- sample standard deviation (n−1) σ = **8.55 ms**
- 3σ tolerance (FR-018) = **25.65 ms**
- Would-be threshold (mean + 3σ) = **82.28 ms** (truncated, not rounded up)

**For comparison — spec §10.5's own 20-run set (excludes 36456616486):**

- n = 20
- mean p50 = **56.86 ms** (truncated; spec §10.5's own text states this as
  "56.87 ms", rounded rather than truncated — same underlying value,
  56.8675 ms)
- sample standard deviation (n−1) σ = **8.70 ms**
- 3σ tolerance (FR-018) = **26.11 ms**
- Would-be threshold (mean + 3σ) = **82.97 ms** (truncated, not rounded up)

Both are re-derivable from the raw samples printed above (`Σ(p50)/n`, then
`√(Σ(p50−mean)²/(n−1))`, then `×3`).

**FR-019's test: is 3σ < 25 ms (1.5 × the 16.67 ms vsync quantum)?**

| Dataset | 3σ | FR-019 (< 25 ms) |
|---|---|---|
| 21-run (this file's primary, FR-022-complete) | 25.65 ms | **FAILS** |
| 20-run (spec §10.5's set) | 26.11 ms | **FAILS** |

**Both fail.** The ambiguity over 36456616486 does not change the outcome —
3σ is above the 25 ms limit either way, by roughly one to 1.1 ms. This is
not a close call decided by a judgment call about one row; it fails with
room to spare on the stricter (smaller-σ) set too.

**What this can and cannot detect, stated per NFR-004/§9.4's own convention.**
At 3σ = 25.65 ms (21-run set), a shift of about `3σ + 1.65σ = 4.65σ ≈ 39.8 ms`
in p50 would be caught about 95% of the time — noticeably more than one
17–19 ms vsync quantum at this window's observed cadence (`T` clusters at
~29.0–32.3 ms across every row above; the wider spread than a clean 60→30 Hz
step reflects the runner losing and regaining partial cadence, not a clean
frame-count ladder). A tolerance this wide is not "no signal" — F2 (spec §9.2)
established that render cost on this runner shows up as a cadence *step*, and
a genuine one-frame regression (~30 ms here) sits close to or inside the noise
band rather than clearly above it. That is the concrete failure mode FR-019
exists to catch before a threshold ships: a gate built from either of these
sets could not reliably distinguish "one dropped frame from a real render
regression" from "this window's own run-to-run noise".

**Decision (T018, mechanical per FR-019): the gate does not ship as a
threshold.** Per spec FR-014 / plan §9.4's report-only branch:

- **`baseline.json` is NOT committed.** A baseline that exists is a threshold
  that exists, and this data does not support one honestly.
- T019 and T019c are deferred with it.
- T021 (the `render-leg-gate` job), if built, must be report-only — it must
  not derive an uncommitted threshold from this file, which would be a
  threshold by the back door.
- **The finding is: on the real, complete FR-022 window (21 runs; 20 under
  spec §10.5's own prior count), 3σ = 25.65–26.11 ms, which exceeds FR-019's
  25 ms limit.** An ADR is needed on what CI may enforce on this leg
  (spec §10.5 names three options: accept report-only; restate the yardstick
  against this runner's *observed* ~29–32 ms cadence rather than a clean
  16.67 ms vsync (spec §10.5's own arithmetic: 1.5 × ~32 ms ≈ 48 ms), which
  would raise the limit from 25 ms to roughly 44–48 ms; or raise
  `ITERATIONS` to narrow σ, at a shard-time cost — NFR-003, #2376). **The
  lane may not write that ADR (ADR-0144).** #2337 stays open pending that
  decision.

This routes to plan §9.4's report-only branch, not its threshold branch.

---

## Preliminary rows, pre-T026 (four-tile fixture; outside the FR-022 window)

Spec §9.2 F3's five rows, re-downloaded and re-read for this file (the
artifacts were still within the 14-day retention window at collection time,
so this is first-hand data, not a transcription — `p50`/`sha`/`n` match
spec §9.2 F3 exactly, confirming that citation). Recorded before the
harness-stability discussion (§10.2) and before the fixture moved from four
tiles to nine (§10.3, ADR-0156). **Not part of the FR-022 window and not
used in the Verdict above** — different fixture size, taken on a harness
later found unstable at the time. Kept here only as historical context per
plan §9.3. Each row is the **first complete attempt** for its run (attempt
column), matching spec §9.2 F3's own selection — note three of the five
needed a retry to reach one, which is exactly the harness instability T026
(later discharged by evidence, §10.2) was about:

| Run id | SHA | Attempt | Samples | p50 | p95 | max | Observed T (before → after) | Raw |
|---|---|---|---|---|---|---|---|---|
| [35892337959](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35892337959) | `94efb23101ede2bde313d6c49c67de7da7411cae` | 0 | 48 | 50.79 ms | 64.60 ms | 66.70 ms | 31.76 → 32.28 ms/frame | [29.10, 29.70, 29.60, 29.79, 25.79, 24.50, 24.70, 24.90, 49.20, 50.70, 50.89, 51.19, 62.29, 63.00, 62.10, 61.80, 63.50, 64.60, 65.70, 66.70, 55.10, 57.70, 58.20, 58.40, 42.60, 43.40, 43.80, 43.90, 49.50, 50.70, 51.79, 53.00, 60.29, 60.89, 61.19, 61.39, 25.79, 26.39, 30.79, 31.09, 53.60, 54.20, 58.79, 59.20, 42.00, 42.50, 42.70, 42.90] |
| [35894668870](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35894668870) | `b8c14eb9a6f781f787fcf4a0f236d524cb43f951` | 2 | 48 | 50.15 ms | 77.90 ms | 81.40 ms | 32.25 → 32.25 ms/frame | [29.40, 31.30, 32.20, 32.69, 21.69, 22.59, 24.50, 24.90, 67.30, 67.89, 68.39, 68.69, 77.30, 77.90, 81.00, 81.40, 26.80, 27.50, 27.90, 28.29, 26.19, 27.59, 28.00, 28.50, 57.19, 57.70, 58.20, 59.30, 54.29, 54.90, 55.90, 56.59, 49.30, 49.80, 50.50, 50.90, 36.19, 32.40, 58.89, 57.00, 61.59, 56.40, 55.50, 54.90, 49.79, 38.20, 34.50, 37.20] |
| [35907278215](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35907278215) | `ec9b4c633811b07ff821232a98729be5086e74c0` | 2 | 48 | 56.45 ms | 76.50 ms | 79.50 ms | 32.25 → 32.29 ms/frame | [30.50, 31.09, 31.29, 31.30, 32.50, 30.70, 31.00, 31.50, 72.00, 75.50, 76.09, 76.40, 60.50, 62.19, 62.69, 63.09, 36.20, 37.70, 38.10, 38.80, 53.09, 53.29, 57.09, 57.69, 42.19, 42.80, 42.20, 42.50, 56.59, 57.39, 58.29, 58.69, 59.90, 61.40, 61.69, 62.09, 75.79, 76.50, 77.00, 79.50, 54.50, 51.59, 46.50, 43.70, 43.50, 76.20, 60.10, 56.30] |
| [35918329596](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35918329596) | `4599d26c0e271389cece8eb05acc4c147b2a359a` | 1 | 48 | 43.50 ms | 153.40 ms | 181.30 ms | 32.25 → 27.47 ms/frame | [42.09, 43.59, 40.70, 35.90, 21.70, 22.09, 22.50, 22.79, 63.00, 53.19, 48.79, 43.40, 72.70, 55.39, 40.80, 38.69, 181.30, 153.40, 157.40, 147.50, 40.69, 32.59, 32.90, 31.09, 57.39, 53.40, 84.00, 78.69, 47.00, 39.59, 33.40, 103.09, 53.59, 63.50, 59.90, 58.89, 41.89, 35.50, 31.19, 30.50, 34.80, 78.00, 78.00, 80.10, 47.69, 33.19, 28.89, 22.30] |
| [35918472066](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35918472066) | `3d93a58e5c84e64fb837e3ad9381b4244149b9ee` | 0 | 48 | 54.25 ms | 106.00 ms | 113.60 ms | 31.76 → 32.29 ms/frame | [64.20, 57.29, 50.29, 44.29, 31.89, 32.59, 32.79, 33.19, 34.59, 35.29, 35.79, 37.90, 53.90, 54.59, 56.79, 57.20, 96.80, 97.70, 105.80, 106.00, 72.70, 69.60, 65.20, 65.59, 51.39, 88.10, 78.10, 78.39, 44.89, 46.20, 113.00, 113.60, 45.29, 42.50, 42.79, 29.20, 85.59, 73.39, 64.19, 64.50, 70.50, 72.00, 51.40, 40.69, 51.79, 46.50, 40.90, 37.50] |

Mean p50 51.03 ms, sample σ 4.93 ms (matches spec §9.2 F3). This set's own
3σ (≈ 14.8 ms) is spec §9.4's superseded prediction — the real 21-run
figure above (25.65 ms) is materially larger, because five points
understate a variance whose true spread only becomes visible with more
runs, exactly as FR-018's "why five, not three" reasoning warned. Note the
p95/max spread within a couple of these attempts (e.g. 35918329596:
p50 43.50 ms but max 181.30 ms) — a single stalled iteration inflates the
tail far more than the median, consistent with the harness instability
§10.2 records for this window.

---

## One-tile rows, pre-US2 (comparison only, outside the FR-022 window)

The three runs from before US2 widened the fixture from one tile to four
(later nine). Re-downloaded and re-read for this file (artifacts were still
within the 14-day retention window):

| Run id | SHA | Samples | p50 | p95 | max | Observed T (before → after) | Raw |
|---|---|---|---|---|---|---|---|
| [35873924190](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35873924190) | `1b0f100167bf7c234442df242bf39d5afc4010c4` | 12 | 30.75 ms | n/a (n<20) | 111.09 ms | 16.39 → 18.82 ms/frame | [14.09, 19.60, 27.00, 24.69, 34.30, 27.19, 69.69, 23.00, 41.60, 111.09, 36.00, 59.39] |
| [35878409081](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35878409081) | `6e2cb1aa75df471940e2119b74089e7696801ca5` | 12 | 28.74 ms | n/a (n<20) | 67.70 ms | 16.39 → 16.39 ms/frame | [22.19, 26.89, 30.59, 22.50, 56.70, 31.70, 67.70, 26.39, 39.90, 14.10, 21.79, 49.29] |
| [35883494479](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35883494479) | `9d8cecbce649ff7674c31def534c697470397951` | 12 | 28.64 ms | n/a (n<20) | 109.50 ms | 16.39 → 16.94 ms/frame | [24.20, 30.70, 11.50, 19.79, 19.19, 26.09, 109.50, 33.40, 59.70, 93.10, 43.50, 26.59] |

`p95` is not printed for these (n = 12 per run, below the sample count
`percentiles()` supports one at — `kiosk-shows-a-label-over-video.spec.ts:756-766`'s
own rule, mirrored by `RenderLegRecord.p95`'s `null`-below-n rule). Mean p50
across the three ≈ 29.4 ms — this is the "one tile costs about 29 ms, nine
tiles cost about 51–57 ms" comparison plan §5 asks for, now anchored to the
nine-tile figure rather than the four-tile figure that later became the
fixture spec §9.2 recorded it against.

---

## Stability record: the harness-fix window (spec §10.2 F5, transcribed)

Not raw render-leg data — the span-test pass/fail record across `develop`
push runs either side of commit `05061543` (spec 232, gateway rate-budget
widening), which is why the FR-022 window starts where it does (the first
nine-tile run *after* the harness had already stabilised for seven days).

| Window | Fixture | Runs | Attempt 0 passed | Retried or red |
|---|---|---|---|---|
| 2026-09-23 21:52Z – 2026-09-26 12:38Z (`6d9b0041` … `e918d457`) | 4 tiles | 16 | 6 | 10 (8 flaky, 2 red on all 3 attempts) |
| 2026-09-26 14:18Z – 2026-09-30 15:35Z (`c276d717` … `419f4f20`) | 4 tiles, then 9 from `44651124` | 27 | 27 | 0 |

Every run in this file's primary 21-row table above falls in the second,
stable window.

---

## F7: `complete: true` attempts that failed mid-loop (spec §10.4, transcribed)

None of these runs are in the FR-022 window (all predate 36351086768).
Recorded here because they are the reason FR-021/T029 tightens
`isCompleteRenderLegMeasurement` to require the span loop to have finished,
not only the per-camera counts — a defect the 21-row table above did not
need to work around (every row's underlying test attempt genuinely passed,
per the F5 stability record above), but one a future run in this window
could still hit until T029 lands.

| Run | Attempt 0 failed with | Record |
|---|---|---|
| 35979443020 | "iteration 8: the value never painted" | `complete: true`, n = 40 |
| 35986570873 | "iteration 8: the value never painted" | `complete: true`, n = 40 |
| 35988620124 | "iteration 9: the value never painted" | `complete: true`, n = 44 |
| 35936415099 | "iteration 9: the value never painted" | `complete: true`, n = 44 |
| 36242562581 | "iteration 9: the value never painted" | `complete: true`, n = 44 |

---

## What this file does not establish

- **Not a committed baseline.** T018's verdict is report-only (FR-014). No
  `baseline.json` exists, and none should be added to this repository until
  an ADR settles which of spec §10.5's three options (or another) applies.
- **Not a re-measurement on representative hardware.** Every figure here is
  from `ubuntu-latest`, headless Chromium, software rasterisation
  (SwiftShader). ADR-0123's fab-hardware re-read stays open work, unrelated
  to this file (spec §Out-of-scope).
- **Does not discharge #1940** (§VII's dashboard obligation) or **#2614**
  (the real-kiosk-hardware measurement ADR-0156 requires before `MaxTiles = 9`
  ships to production, per spec §10.3).
