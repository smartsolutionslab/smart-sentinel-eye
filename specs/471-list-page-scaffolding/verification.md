# Verification 471 — The list-page scaffolding management-web pages repeat lives in one place

Phase 5 (ADR-0037). Observed on branch `471-list-page-scaffolding` on 2026-11-01, base `develop` at 8c551733.
Phase 4a: **red first** for the four new pieces, **characterisation** for the four pages.

## Red first (the pieces are new behaviour)

`apps/management-web` tests written before the modules existed (`b3f1ccfb`):

```text
FAIL  src/features/PageHeading.test.tsx     Error: Failed to resolve import "./PageHeading.js" from "src/features/PageHeading.test.tsx". Does the file exist?
FAIL  src/features/StateFilter.test.tsx     Error: Failed to resolve import "./StateFilter.js" from "src/features/StateFilter.test.tsx". Does the file exist?
FAIL  src/features/useConfirmTarget.test.tsx Error: Failed to resolve import "./useConfirmTarget.js" …
FAIL  src/features/useHeadingFocus.test.tsx Error: Failed to resolve import "./useHeadingFocus.js" …
Test Files  4 failed (4)   Tests  no tests
```

Green after the modules (`434f5a03`): 17 tests. One lint-only change to a test between the two: `HTMLHeadingElement`
→ `ComponentRef<'h1'>` (ESLint `no-undef`; no assertion touched).

## Counterfactuals: each piece can fail

| Break (one at a time, restored; `git diff` empty after) | Test that failed |
|---|---|
| drop `tabIndex={-1}` from `PageHeading` | `Is focusable by script but not by Tab, and carries the page-heading classes` |
| `focusHeading` becomes a no-op | `Focuses the element the heading ref is attached to` |
| invert `pressed` in `StateFilter` | `Presses only the option equal to the value`, `Treats an undefined value as an option of its own` |
| `useConfirmTarget` clears before running | `Keeps the target when the action throws, because it clears only after the action ran` |
| page level: `focusHeading` no-op | `Focuses the page heading when a retried read succeeds`, on **all four** pages' retry-recovery suites |
| chip guard: drop `role="group"` from `StateFilter` | `A_ToggleChip_sits_inside_a_labelled_group` at `StateFilter.tsx:22` |

**Two flaws in my own counterfactuals, caught and fixed before relying on them:** (1) the first run was started
from the wrong directory, so Vitest had no jsdom and *every* test failed with `document is not defined`; the
breaks looked caught and proved nothing. Re-run from `apps/management-web`. (2) The "clear before run" break
**passed all tests**: the first `useConfirmTarget` test checked only values it had itself passed in, and
`confirm` hands `run` the target it captured, so order was unobservable. The observable difference is when
`run` throws, so that is now the test (`8a66ffb9`), seen failing under the break.

## Characterisation: the four pages

Recorded on the unchanged tree, repeated after the last page commit:

| Check | Result |
|---|---|
| The 11 page suites (`*Page`, `*PageFocusRefetch`, `*PageRetryRecovery` for Overlays, Layouts, Rules, SystemVariables) | 177 before, 177 after, all passing |
| Sorted `file :: full name :: status` | `diff` empty |
| Sorted assertion lines (298) | `diff` empty |
| `git diff origin/develop...HEAD` on those 11 test files | empty — none edited (FR-001) |
| Old patterns in the four pages (`headingRef.current`, `useRef<ComponentRef`, `tabIndex={-1}`, `set(Archive\|Discard)For`, `STATE_FILTERS.map`, `<ToggleChip`) | none left (SC-003); `headingRef` still appears twice per page, as the hook's return and `PageHeading`'s `ref` |
| `SystemVariablesPage` default filter | still `useState<VariableState \| 'All'>('Defined')` |
| Whole management-web suite | 77 files / 666 tests → 81 files / 684 tests (666 + 18 new), all passing; 90.9 s → 92.9 s |
| `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` | clean |
| `dotnet test tests/Architecture.Tests -c Release` | 3254/3254 (after the guard update below) |

## The size, stated as measured (plan §Summary estimated ~90-110 lines; that was wrong)

| | Before | After |
|---|---|---|
| `OverlaysPage` | 404 | 399 |
| `LayoutsPage` | 390 | 385 |
| `RulesPage` | 272 | 259 |
| `SystemVariablesPage` | 257 | 248 |
| **Four pages** | **1323** | **1291** (−32, −2.4 %) |
| New shared code (4 modules) | | +77 |
| New tests (4 files) | | +205 |

Prettier re-wraps `archiveFor.target.revisionNumber`-style reads, which consumed most of the saving. Tried both
shapes on `OverlaysPage` (destructured `{target, ask, cancel, confirm}`: 401; single object: 399) and kept the
single object. The repository gets ~250 lines larger. The maintainer was asked after this measurement and chose
to adopt in all four anyway, for consistency rather than size.

## A guard had to change (read this one)

`TextActionAndChipPrimitiveTests.A_ToggleChip_sits_inside_a_labelled_group` pinned the four pages as files that
**must contain `<ToggleChip>`** (so the guard cannot pass vacuously). The chips now live once, in
`StateFilter.tsx`, so that test failed (1 of 3254). I changed the pinned list to `StateFilter.tsx` and two
comments that named RulesPage; the rule itself (every chip sits in a labelled group) is untouched and, per the
counterfactual above, still fails when `StateFilter` loses its `role="group"`. It is an edit to a gate, made
because the thing it guards moved, not to reach green. Reviewer: confirm that reading.

## Not observed

- CI on Linux (the first run is the PR's).
- The pages in a browser; the evidence is the existing page suites, which render them under jsdom.

## QA (phase 6, `/code-review medium`): 6 findings, none a defect

| Finding | Disposition |
|---|---|
| `confirm` runs the action then clears; if the action throws, the dialog stays open | **Known, pinned.** It is the order every page had inline; `useConfirmTarget.test.tsx` asserts it and was seen failing under the reverse order. Not changed. |
| `ask: setTarget` exposes the raw setter, so a function-typed `T` would run as an updater | **Not changed.** Every `T` is a plain object; guarding it is generality for a caller that does not exist (ADR-0036). |
| `cancel`/`confirm` are new closures each render | **Not changed.** The inline lambdas they replace also changed every render; `ArchiveConfirmation` has no effect keyed on them. |
| Chip key is `label`, selection is `===` on `value`: duplicate labels or object values would misbehave | **Documented** in a comment on `StateFilter` (labels unique, values primitives or `undefined`). All four callers satisfy it. |
| `.specify/feature.json` is in the diff | **Not changed:** every spec PR commits it (see 469's verification). |
| Only 4 of the pages with this heading markup migrated | **Planned scope, but the count was wrong.** The wiring is in **9** production files in **three** apps; my first count searched only management-web and management-cameras and missed `management-event-ingestion/.../HeldEventTypesPage.tsx`. Spec corrected. The other five (`AuditPage`, `WallsPage`, `CamerasPage`, `CameraDetailPage`, `HeldEventTypesPage`) are the follow-up; the last three live in apps that cannot import from management-web, so that follow-up needs these pieces moved to `apps/shared` first. |
