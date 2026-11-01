# Implementation Plan: The Overlays and Layouts FocusRefetch/RetryRecovery tests use the shared test helpers

**Branch**: `474-overlays-layouts-test-helpers` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/474-overlays-layouts-test-helpers/spec.md`

## Summary

In four `management-web` test files, replace the local `jsonResponse` (and `problemResponse` where present) with imports
from `@smart-sentinel-eye/shared/test/responses`, and the hand-built listener-plus-slice store with
`createApiStore([slice])`. One commit per file. Behaviour-preserving, so **characterisation**, not red-first
(ADR-0139/0144 phase 4a): the four files are their own covering tests. The mechanical scripts from spec 468 apply as-is.

## Technical Context

- **Stack**: TypeScript, React 19, Vitest + Testing Library, `apps/management-web` (jsdom).
- **Baseline (2026-11-01, `develop` at 1f19ea0a):** the four files 179 + 105 + 203 + 128 lines; whole `management-web`
  suite 90 files / 684 tests / ~96 s.
- **Touches**: those four test files only (plus `specs/474-…`, `.specify/feature.json`). No production file, no
  `shared`, no config.
- **Latency budget**: none.

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green | The design: characterisation, assertions unmodified (SC-001/002). |
| ADR-0036 — smallest change | Substitutes identical code for identical code; adds nothing. |
| Lane may not weaken a gate / delete a test | Test count and names are success criteria, checked by diff. |
| No new ADR | Applies the convention #3196 set. |

No violation.

## Research (decisions that shaped the plan)

### R1 — The migration is a pure substitution (checked, not assumed)

- **Helpers.** The four local `jsonResponse` bodies hash identically (md5 `c8c8576c`) and equal the shared module's
  code; the two `problemResponse` copies equal the shared one (`jsonResponse({ title: 'PROBLEM', status }, status)`).
  `migrate.cjs helpers` (spec 468) matches each by exact regex and **aborts** on any difference.
- **Stores.** All four are `createListenerMiddleware()` prepended plus `<slice>.middleware`, reducer
  `[<slice>.reducerPath]: <slice>.reducer`: `createApiStore([slice])` builds that. `migrate.cjs store` matches the shape
  by regex (including the two-line comment in the RetryRecovery stores) and aborts otherwise.
- **Dry run on scratch copies** (2026-11-01): all four files migrate cleanly (`helpers` then `store`), each ending
  with `import { jsonResponse[, problemResponse] } from '@smart-sentinel-eye/shared/test/responses'` and
  `import { createApiStore } from '@smart-sentinel-eye/shared/store'`, `createStore()` reduced to
  `return createApiStore([<slice>]);`. No file in the repository was touched by the dry run.

### R2 — Slices: Overlays files use `overlaysApi`, Layouts files use `layoutsApi`

Read from each file's reducer line, not guessed: `OverlaysPageFocusRefetch`/`RetryRecovery` → `[overlaysApi]`;
`LayoutsPageFocusRefetch`/`RetryRecovery` → `[layoutsApi]`. Each file has exactly one slice.

### R3 — A risk to run, not assume: static `createApiStore` import vs `vi.stubEnv`

These files call `vi.stubEnv('VITE_API_GATEWAY_URL', …)` and import the API slices **dynamically after it**
(gateway.ts resolves the origin at module load). `migrate.cjs` adds `createApiStore` as a **static** import, which
sits above the stub. If `createApiStore.ts` (or anything it imports) read the origin at load, the fetches would use
a relative URL and the tests would fail. In #3196 PR 2 the same static import worked in management-web files with the
same pattern, and in `kiosk-web`'s `CameraTile` I used a dynamic import for a related reason. The rule: **run each
file after migrating it**; if one fails for this reason, switch that file to a dynamic import beside its slice
imports (as `CameraTile` does) and say so in the PR. Not expected, not assumed.

### R4 — Module-global `setupListeners` (as in #3196)

`FocusRefetch` files call `installFocusListeners`, which touches RTK Query's module-global focus listener. A store
change could interact with another file's. Each `FocusRefetch` file is run **alone and in the full suite**; Vitest
isolates files by default, so this is a check, not an expected problem.

### R5 — Not in this plan

`CameraTile.test.tsx` (helpers differ on purpose). Any production file. A shared `resetHarness()` for the page-test
harnesses (a separate follow-up). Any change to what a test asserts.

## Project Structure

```text
apps/management-web/src/features/
  overlays/OverlaysPageFocusRefetch.test.tsx    (local helpers + store removed)
  overlays/OverlaysPageRetryRecovery.test.tsx   (local helper + store removed)
  layouts/LayoutsPageFocusRefetch.test.tsx      (local helpers + store removed)
  layouts/LayoutsPageRetryRecovery.test.tsx     (local helper + store removed)
specs/474-overlays-layouts-test-helpers/
```

## Delivery: one PR, one commit per file

Four commits, in the order above, each green on its own (one file at a time; the helpers and the store of one file
change in the same commit, since the store migration removes the imports the helper migration leaves). Then task
ticks and the verification note. `prettier --write` the migrated file before committing (the hook rejects
unformatted files); `git add` exactly that file; run Vitest from `apps/management-web`.

## Verification (what "unchanged" means, concretely)

Recorded on the unmigrated tree, repeated after the last commit:

- **T-A names:** sorted `file :: full name :: status` for the four files — identical (SC-001).
- **T-A assertions:** sorted `expect(`, `.toHaveBeen…`, `.toBe…`, `.toEqual`, `.toThrow` lines — `diff` empty (SC-002).
- **Lines removed/added** per file from `git diff --stat`, to show the change is the helper and store blocks and the
  imports and nothing else; each file's diff read.
- **Each `FocusRefetch` file alone**, then the **whole `management-web` suite** (684 → 684) with duration
  before/after (SC-004).
- **SC-003:** `grep -rn "function jsonResponse\|function problemResponse" apps --include=*.test.*` finds only
  `CameraTile.test.tsx`.
- **Counterfactual** (the shared pieces are really in the path): in the shared `responses.ts` break `problemResponse`
  (e.g. drop `status` from the body) and run `OverlaysPageFocusRefetch`; it must fail where it uses it; restore
  (`git diff` empty). Without it, a green run could mean the file no longer calls the helper at all.
- `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` for `management-web`;
  `dotnet test tests/Architecture.Tests -c Release` (they scan `.tsx`).

## Risks

| Risk | Mitigation |
|---|---|
| Static `createApiStore` import changes env-stub timing (R3) | Run each file after migrating; fall back to a dynamic import for that file only, stated in the PR. |
| `setupListeners` global state (R4) | Each `FocusRefetch` file alone and in the suite. |
| A local helper turns out not to be identical | `migrate.cjs` aborts on any mismatch; hashes already equal. |
| Unused imports fail `eslint --max-warnings 0` | The store migration removes `configureStore`/`createListenerMiddleware`; lint gates. |
