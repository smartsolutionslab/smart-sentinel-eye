# Spec 471 — The list-page scaffolding management-web pages repeat lives in one place

**Feature Branch**: `471-list-page-scaffolding`
**Created**: 2026-11-01
**Status**: Draft
**Issue:** #3190 (feature-level; found by the frontend refactoring survey).
**ADRs:** ADR-0037 (phases), ADR-0075 (RTK Query), ADR-0077 (Radix + custom design system), ADR-0079
(forms), ADR-0139 / ADR-0144 (phase 4a).
**No new ADR.** Pure extraction of code that already exists, inside the conventions those ADRs set.
**Latency budget:** none — operator console list pages; not on the event-to-overlay path.
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are the existing page
suites; they must pass with every assertion **unmodified**. An assertion that has to be edited is evidence the
behaviour moved: block, do not adjust (CLAUDE.md).

## Problem

Four management-web list pages repeat the same page scaffolding feature by feature. Measured on `develop`
(2026-11-01):

| Page | Lines |
|---|---|
| `features/overlays/OverlaysPage.tsx` | 404 |
| `features/layouts/LayoutsPage.tsx` | 390 |
| `features/rules/RulesPage.tsx` | 272 |
| `features/systemVariables/SystemVariablesPage.tsx` | 257 |

`OverlaysPage` and `LayoutsPage` are near twins: with "overlay"/"layout" normalised, **298 diff lines remain out
of ~794** (the issue's "about 300 of roughly 800" holds). What recurs, with the shape each page actually has:

1. **Focus the heading after recovery.** `const headingRef = useRef<ComponentRef<'h1'>>(null)`, an
   `<h1 ref={headingRef} tabIndex={-1} className="text-2xl font-semibold focus:outline-none">`, and
   `<RetryBanner … onRecovered={() => headingRef.current?.focus()} />`. This pairing is in **8 production files**:
   six in management-web (`OverlaysPage`, `LayoutsPage`, `RulesPage`, `SystemVariablesPage`, `AuditPage`,
   `WallsPage`) and two in management-cameras (`CamerasPage`, `CameraDetailPage`). The issue's "24 occurrences"
   counted tests as well; in production it is 8.
2. **A state filter.** `STATE_FILTERS` is declared in all four pages and rendered as a group of `ToggleChip`s
   (a button group with `aria-label="Filter by state"`, not a tab list). **The shapes differ:**
   Overlays and Layouts hold `'All' | State` with default `'All'`; SystemVariables holds `'All' | VariableState`
   with default **`'Defined'`**; Rules holds `{ label, value: RuleState | undefined }[]` where `undefined` means
   "all".
3. **Confirm-target state for archive and discard.** `archiveFor` / `discardFor` plus their setters, feeding a
   confirm dialog. **The shapes differ:** Overlays and Layouts carry `…Identifier + revisionNumber + version +
   name` (discard also `liveRevision`); Rules and SystemVariables carry `name + version + fab` and have no discard.
4. **`refetchOnFocus: true`** on the list query (Overlays, Layouts, SystemVariables; also `AuditPage`).
5. **`RetryBanner` wiring**: `failed`, `retrying`, `onRecovered`, `message`, `onRetry`.

### What the issue did not say

- The filter and confirm-target shapes **differ between the pages**, so the extraction cannot be a literal
  copy-out; where a shared piece has to accommodate a difference, that is a design question for the plan.
- `RulesPage` has a retry-recovery test but **no focus-refetch test**: item 4 is uncovered there today. The
  issue says "a feature with no covering test gets one written first"; the plan must decide whether Rules'
  `refetchOnFocus` is touched at all.
- The covering suites are `OverlaysPage*`, `LayoutsPage*`, `RulesPage*`, `SystemVariablesPage*` (each
  `.test.tsx`, `…FocusRefetch`, `…RetryRecovery` where they exist).

**Amended at plan time (2026-11-01):** items 4 and 5 are left in the pages (plan §R5), `RulesPage` has no
`refetchOnFocus`, and the extraction is sized honestly: ~90–110 lines out of the pages, none out of the repository.

## Scope

**In:** removing the scaffolding repeated in the four pages above, by extracting it into shared piece(s) the
pages use. The final design (hook, component, or both; where they live; whether `AuditPage`, `WallsPage` and the
two management-cameras pages adopt the heading piece) is **the plan's decision**, not the spec's.

**Out (explicitly):**
- The `react-hooks/*` suppressions: judged individually in #2118, delivered in `694ad2a8`.
- Date formatting: browser-local is deliberate (#2431).
- A shared state badge: only `RulesPage` has one.
- Per-feature columns, dialogs, mutations and copy; they stay in their pages.
- Splitting `OverlaysPage.test.tsx` / `LayoutsPage.test.tsx`, and migrating the four `Overlays|Layouts`
  `FocusRefetch`/`RetryRecovery` tests to the shared helpers (#3196's note): follow-ups **after** this, because
  those four are this change's unmodified safety net.
- Any behaviour change, including "obvious" fixes found on the way (two issues, per CLAUDE.md).

## User Scenarios & Testing

### User Story 1 (P1) — The four pages behave exactly as before

As an operator I see the same pages: the same heading, filter chips, banners, confirm dialogs and focus
behaviour. As the maintainer I can prove it.

**Why P1:** this is the only thing that makes the refactor safe.

**Independent test:** the existing page suites pass with no assertion edited.

```gherkin
Scenario: the existing suites are the safety net
  Given the page suites for Overlays, Layouts, Rules and SystemVariables
  When they are run after the extraction
  Then every test passes
  And git diff shows no line of those test files changed (assertions or otherwise)

Scenario: the heading takes focus after a failed load recovers
  Given a list page whose read failed and then succeeded on retry
  When the page recovers
  Then the page heading has focus, as today

Scenario: the default filter is unchanged on each page
  When each page first renders
  Then Overlays and Layouts show "All" pressed, SystemVariables shows "Defined" pressed,
  And Rules shows its first option pressed, exactly as today
```

### User Story 2 (P2) — A new list page repeats none of this

As a developer adding a list page I want the heading focus, state filter, confirm target and banner wiring
supplied, so I write only what is specific to my feature.

**Independent test:** each of the four pages no longer contains its own copy; the shared piece has its own
unit tests.

```gherkin
Scenario: the scaffolding exists once
  Given the four pages
  When the shared piece(s) are in place
  Then no page declares its own headingRef/focus wiring, its own filter chip group, or its own
  archiveFor/discardFor state
  And the line count of each page is lower than before

Scenario: a difference between pages is expressed, not erased
  Given SystemVariables defaults to "Defined" and Rules treats undefined as "all"
  When the shared filter is used
  Then each page still gets its own default and its own option set
```

### Edge cases

- **`Rules` has no focus-refetch test** (above): if its list query is touched, a covering test comes first.
- **Hook ordering and `useRevocationFallback`.** Three pages call `useRevocationFallback` after their queries;
  moving state into a hook must not change the order hooks run in.
- **A mutation resets the confirm target on success only.** `setArchiveFor(null)` after the mutation fires
  today; the extraction must not move it earlier or later.
- **Focus on mount.** A page must not start taking focus it does not take now (the heading has `tabIndex={-1}`
  and is focused only on recovery).
- **Generated copy.** Subject strings (`revision N of NAME`, `draft revision …`, `rule NAME`) stay in each
  page; only the state that selects them moves.

## Requirements

- **FR-001** Every test in the four page suites (`*Page.test.tsx`, `*PageFocusRefetch.test.tsx`,
  `*PageRetryRecovery.test.tsx`) passes after the change; none of those files is modified.
- **FR-002** The repeated scaffolding in items 1–3 above (heading focus, state-filter chips, confirm-target state)
  is supplied by shared code and absent from the four pages. Items 4 and 5 are not extracted (plan §R5):
  `refetchOnFocus` is a one-line option and `RetryBanner` is already the shared piece; only its `onRecovered`
  callback changes.
- **FR-003** Each page keeps its own default filter, option set and confirm-target shape; the shared code does not
  force one.
- **FR-004** The shared code has its own tests, each seen failing first when its behaviour is new (phase 4a:
  characterisation for the pages, red-first for the new unit tests, ADR-0139).
- **FR-005** No change to API calls, query arguments, mutation arguments, rendered text, `aria-*` attributes,
  class names, test ids or hook call order.
- **FR-006** No change outside `apps/management-web/src/features/`, the chip architecture guard (`tests/Architecture.Tests/TextActionAndChipPrimitiveTests.cs`, see verification.md) plus
  `specs/471-…` and `.specify/feature.json`.
- **FR-007** `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` are clean for every touched app, and
  `Architecture.Tests` pass (they scan `.tsx`).

## Success Criteria

- **SC-001** The four page suites: same test count before and after, all passing, `git diff` on those files empty.
- **SC-002** Total lines in the four pages fall from **1323** (404 + 390 + 272 + 257) by a measured amount the
  plan states as a target and the PR reports.
- **SC-003** No page declares `STATE_FILTERS`' chip group, a `headingRef` focus callback or its own
  `archiveFor`/`discardFor` state (a `grep` returns nothing in those four files).
- **SC-004** The shared code's unit tests are green, and each was red before the code existed.
- **SC-005** The whole management-web suite passes, with its duration noted before and after.

## Assumptions

- The page suites are strong enough to be the characterisation (the issue says so). The plan checks that claim
  for the hook-order and reset-on-success edge cases, and writes a covering test first where it is not.
- Moving state into a hook does not change rendering as long as the hook's calls occur in the same order and
  the same component renders them.
- The 8 production files with the heading wiring are the full set (counted by `grep` for the focus callback;
  the plan re-greps with the general pattern, per the repo's lesson on one-spelling counts).

## Dependencies

#3196 and #3197 (merged). Blocks the splits of `OverlaysPage.test.tsx` and `LayoutsPage.test.tsx`.
