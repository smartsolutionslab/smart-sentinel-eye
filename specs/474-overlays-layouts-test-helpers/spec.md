# Spec 474 — The Overlays and Layouts FocusRefetch/RetryRecovery tests use the shared test helpers

**Feature Branch**: `474-overlays-layouts-test-helpers`
**Created**: 2026-11-01
**Status**: Draft
**Issue:** #3210 (feature-level; the follow-up #3196's plan §R4 and #3190's close-out both named).
**ADRs:** ADR-0052 (Vitest + hand-written fakes), ADR-0054 (hand-written builders), ADR-0139 / ADR-0144
(phase 4a).
**No new ADR.** Applies the module and the store helper that specs 468 and the rest of #3196 already established.
**Latency budget:** none — test-only; no production module changes.
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are the four files being
migrated; they must pass with assertions unmodified.

## Problem

Four `management-web` test files still define their own response helper and hand-build their store, after
#3196 moved every other copy to `@smart-sentinel-eye/shared/test/responses` and `createApiStore` (PRs #3201–#3203):

| File | Lines | Local helpers | Store |
|---|---|---|---|
| `features/overlays/OverlaysPageFocusRefetch.test.tsx` | 179 | `jsonResponse`, `problemResponse` | hand-built |
| `features/overlays/OverlaysPageRetryRecovery.test.tsx` | 105 | `jsonResponse` | hand-built |
| `features/layouts/LayoutsPageFocusRefetch.test.tsx` | 203 | `jsonResponse`, `problemResponse` | hand-built |
| `features/layouts/LayoutsPageRetryRecovery.test.tsx` | 128 | `jsonResponse` | hand-built |

They were left alone on purpose: they were the unmodified characterisation suite for the page refactor #3190
(merged as #3205), and editing a safety net while it is in use defeats it. That refactor is done, so the reason
is gone.

### Premise check (2026-11-01, `develop` after #3209)

1. **These four are the only remaining local definitions** of `jsonResponse`/`problemResponse` in the repo,
   apart from `kiosk-web/.../CameraTile.test.tsx`, whose helpers differ on purpose (fixed status, bodyless) and
   stay.
2. **All four local `jsonResponse` bodies hash identically** and are the code of the shared one; both
   `problemResponse` copies are the shared one too. So the migration is a pure substitution, as in #3196 PR 1–3.
3. **All four stores are exactly `createListenerMiddleware()` prepended plus one slice's middleware**, which is
   what `createApiStore([slice])` builds (it already prepends a listener). No store behaviour changes. The
   comment in the two RetryRecovery stores ("`useRevocationFallback` … throws without this middleware") describes
   a guarantee `createApiStore` already gives, so it goes with the hand-built store.
4. **The `FocusRefetch` files touch module-global state** (`setupListeners` is global), so each must be run alone
   *and* in the full suite, as in #3196.

## Scope

**In:** replace the local `jsonResponse` (and `problemResponse` where present) in the four files with imports from
the shared module, and each hand-built `configureStore` store with `createApiStore([slice])`.

**Out (explicitly):**
- `kiosk-web/.../CameraTile.test.tsx`: its local helpers differ on purpose.
- Any production module, `createApiStore` itself, the shared responses module.
- Changing what a test asserts, adding coverage, or fixing a test that turns out to be wrong.
- The `OverlaysPage` / `LayoutsPage` page tests (already split; they use the harnesses).

## User Scenarios & Testing

### User Story 1 (P1) — The four files use the shared helpers and store, and behave as before

As a test author I want no local copy of `jsonResponse`, `problemResponse` or the listener-plus-slice store in
these four files, and as the maintainer I want proof that nothing they assert changed.

**Independent test:** the four files' tests, before and after.

```gherkin
Scenario: no local copy remains
  Given the four files
  When they are migrated
  Then none declares function jsonResponse, function problemResponse, createListenerMiddleware or configureStore
  And each imports from @smart-sentinel-eye/shared/test/responses and @smart-sentinel-eye/shared/store as needed

Scenario: no test or assertion changes
  Given the sorted test names with status and the sorted assertion lines of the four files, recorded before
  When they are recorded again after the migration
  Then both lists are identical

Scenario: the focus-refetch files pass alone and in the suite
  When each FocusRefetch file is run on its own and then the whole management-web suite is run
  Then every file passes both ways
```

### Edge cases

- **Module-global `setupListeners`** (above): a store change could interact with another file's; run alone and in
  the suite.
- **Unused imports** after removing `configureStore`/`createListenerMiddleware` (`eslint --max-warnings 0`).
- **A file whose helper differs from the shared one keeps it with a comment (FR-008 of spec 468)**: none do
  (hashes equal), stated so a reviewer need not wonder.

## Requirements

- **FR-001** Each of the four files imports `jsonResponse` (and `problemResponse` where it had one) from
  `@smart-sentinel-eye/shared/test/responses` and builds its store with `createApiStore([slice])` from
  `@smart-sentinel-eye/shared/store`.
- **FR-002** No assertion line, test name or test count changes.
- **FR-003** The local helper definitions and the hand-built stores are removed, together with the
  now-unused imports.
- **FR-004** No file other than these four (and `specs/474-…`, `.specify/feature.json`) changes.
- **FR-005** `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` are clean for `management-web`, and
  `Architecture.Tests` pass (they scan `.tsx`).

## Success Criteria

- **SC-001** Test names with status: identical set before and after (the four files' count, recorded in T001).
- **SC-002** Sorted assertion lines: `diff` empty.
- **SC-003** A search of `apps/` for `function jsonResponse|function problemResponse` finds only
  `CameraTile.test.tsx`.
- **SC-004** Each `FocusRefetch` file passes alone and in the whole suite; the suite count (684) and duration
  are reported.

## Assumptions

- The shared module and `createApiStore` are unchanged since #3201 (checked at plan time).
- The substitution of an identical helper cannot change a test's outcome except through module-global state,
  which SC-004 covers.

## Dependencies

#3196 (merged), #3190 (merged as #3205), which are why the four files were left alone.
