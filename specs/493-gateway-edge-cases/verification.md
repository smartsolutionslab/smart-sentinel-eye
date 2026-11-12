# Verification: spec 493 gateway and shell dev proxy edge cases

Raw evidence recorded as observed during Phase 4. Phase 5 and 6 notes are appended later.

## T001 Baseline (clean branch, before any change)

- (a) `vitest run src/app/devProxy.test.ts` in `apps/management-web`: `Test Files 1 passed (1)`, `Tests 5 passed (5)`.
- (b) `dotnet test tests/Integration.Tests -c Release --filter <five gateway classes>`:
  `Passed!  - Failed: 0, Passed: 77, Skipped: 0, Total: 77`.
- (c) `dotnet test tests/Architecture.Tests -c Release`: `Passed! - Failed: 0, Passed: 3254, Skipped: 0, Total: 3254`.

## T003 Characterisation: devProxy.test.ts lookup helper, run against the unfixed devProxy.ts

`entryFor(table, url)` replaces key-spelling lookups; every assertion on the entry found is unchanged. Result against the unfixed
`devProxy.ts`: `Test Files 1 passed (1)`, `Tests 5 passed (5)` (same count as T001(a)).
