# Verification 474 — The Overlays and Layouts FocusRefetch/RetryRecovery tests use the shared test helpers

Phase 5 (ADR-0037). Observed on branch `474-overlays-layouts-test-helpers` on 2026-11-01, base `develop` at 1f19ea0a.
Test-only change, phase 4a colour **characterisation**: no red test; the evidence is that the same tests pass
unmodified after the substitution.

## Before (T001, unmigrated tree)

| Record | Value |
|---|---|
| The four files | 179 + 105 + 203 + 128 = 615 lines; 8 tests (3 + 1 + 3 + 1), 8 passed |
| Sorted assertion lines | 16 |
| `grep "function jsonResponse\|function problemResponse"` over `apps/**/*.test.*` | the four files + `kiosk-web/.../CameraTile.test.tsx` |
| Whole `management-web` suite | 90 files, 684 tests, 104.0 s |

## After (T006)

| Check | Result |
|---|---|
| Test names with status, four files | 8; `diff` against before **empty** |
| Sorted assertion lines | 16; `diff` **empty** |
| Each file alone | `OverlaysPageFocusRefetch` 3, `OverlaysPageRetryRecovery` 1, `LayoutsPageFocusRefetch` 3, `LayoutsPageRetryRecovery` 1, all passed |
| Whole suite | 90 files, 684/684, **97.9 s** (the unchanged tree measured 104.0 s minutes earlier; run-to-run noise on this machine) |
| Lines | 575 total (−40): 3 insertions and 12-14 deletions per file |
| Grep (SC-003) | only `kiosk-web/.../CameraTile.test.tsx` remains |
| `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` | clean |
| `dotnet test tests/Architecture.Tests -c Release` | 3254/3254 |
| `git diff --name-only origin/develop...HEAD` outside the four files and `specs/474-…` / `feature.json` | none (FR-004) |

Each commit's diff was read before committing. In every file the change is exactly: the `configureStore` /
`createListenerMiddleware` import removed; `jsonResponse` (and `problemResponse` where present) imported from
`@smart-sentinel-eye/shared/test/responses`; `createApiStore` imported from `@smart-sentinel-eye/shared/store`; the
local helper definitions removed; `createStore()` reduced to `return createApiStore([<slice>]);`. In the two
RetryRecovery files the comment explaining why the hand-built store needs a listener ("`useRevocationFallback`
observes settlements … throws without this middleware") went with that store, because `createApiStore` already
guarantees it.

**Plan §R3 (static `createApiStore` import above `vi.stubEnv`) did not bite.** All four files pass alone with the
static import, so no file needed the dynamic-import fallback.

## Counterfactual: the shared helper is really in the path (T007)

A first break **proved nothing**: dropping `status` from `problemResponse`'s JSON body left all 6 focus-refetch
tests green, because the tests react to the HTTP status, not that body field. Restored (`git diff` empty), and a
break that changes what they depend on was used instead.

`problemResponse` answers `200` instead of the refusal status (restored afterwards):

```text
 × Three consecutive 403 focus refreshes drop the stale rows and show the failure banner   (Overlays and Layouts)
 × 403, 403, 200, 403, 403 via focus is not refused                                        (Overlays and Layouts)
TestingLibraryElementError: Unable to find an accessible element with the role "alert"
Test Files  2 failed (2)   Tests  4 failed | 2 passed (6)
```

The four failures are exactly the tests that refuse via `problemResponse`, and they fail because the refusal never
happens at status 200 (no alert appears). The two tests that do not use it still pass.

## Not observed

- CI on Linux: the first run is the PR's.
- The `FocusRefetch` files in a suite with a different file order: Vitest isolates files by default and the full
  suite passed, which is the evidence for plan §R4.
