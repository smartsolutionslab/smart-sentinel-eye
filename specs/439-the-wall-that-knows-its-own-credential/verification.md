# Verification 439 - the wall that knows its own credential

## Observed (phase 4b, local)

- vitest: apps/shared 1142 passed, apps/kiosk-web 308 passed (the 21 red tests were unmodified when first observed green; the review of #2781 then changed or added tests, listed in its commits).
- `tsc --noEmit` in both apps, `pnpm typecheck:e2e`, eslint `--max-warnings 0`, `pnpm format:check`: clean.
- `playwright test --list`: the spec is `e2e/kiosk-credential-refused.spec.ts`, listed only under the `kiosk` project.
- Architecture.Tests (Release): MotionLanguage|WallStatusChip 26/26; full suite 3068/3068.
- e2e not run locally (stack shut down); the blocking CI `e2e` job is the observation.

## Pending: composite + render leg (constitution IV, <= 50 ms)

The wall state changes only on a refusal, so the steady-state render path is unchanged. Procedure (spec 297 V3),
to run after CI, not attempted here:

1. Scratch baseline from the last nine `develop` push runs' `render-leg-attempt-*.json` (mean / 3 sigma).
2. Download `playwright-report-*-of-4` before any re-run.
3. Three PR records through `node scripts/render-leg-check.mjs`; all three must be `within tolerance`.
4. Record p50/p95/max/T, run ids and SHAs here. `regressed` -> ADR-0123 triage; a second `regressed` or `unmeasured` blocks.

Status: NOT MEASURED. T020 (live screenshots) also pending.
