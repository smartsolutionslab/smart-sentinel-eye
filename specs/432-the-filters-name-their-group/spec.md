# Spec 432: The filters name their group

**Issue**: #3083 (feature-level, Project #13, autonomous lane, ADR-0144) · **Follow-up of**: spec 427 /
PR #3082 (issue #2668), plan §8 · **ADRs**: ADR-0077 (Radix headless + custom design system), ADR-0074
(two frontend apps), ADR-0139/ADR-0144 (phase 4a red), ADR-0037 (phased workflow) · **Plan**:
[plan.md](./plan.md) · **Tasks**: [tasks.md](./tasks.md)

## 1. Premise, re-verified at `1ebcd516` (origin/develop)

`grep -rln "ToggleChip" apps --include=*.tsx` (non-test, outside the primitive itself) gives exactly four
pages. One has the group, three do not:

| Page | Filter container | Group label |
|---|---|---|
| `apps/management-web/src/features/rules/RulesPage.tsx:166` | `<div className="flex gap-2" role="group" aria-label="Filter by state">` | yes |
| `apps/management-web/src/features/layouts/LayoutsPage.tsx:135` | `<div className="mb-4 flex gap-2">` | **no** |
| `apps/management-web/src/features/overlays/OverlaysPage.tsx:123` | `<div className="mb-4 flex gap-2">` | **no** |
| `apps/management-web/src/features/systemVariables/SystemVariablesPage.tsx:126` | `<div className="mb-4 flex gap-2">` | **no** |

No other `ToggleChip` filter group exists in `apps/` (kiosk-web and shared use none). No test anywhere
queries a filter group by role: `RulesPage.test.tsx` does not pin its own label either. There is no i18n
layer in management-web; labels are literal English strings, so the RulesPage literal is the convention.

## 2. Owner decision (2026-10-21, issue comment)

Group label only. Radio-group semantics for mutually exclusive filters are **deferred and out of
scope** (file separately if wanted). No ADR.

## 3. User stories

### US1 (P1): A screen-reader user hears which control the state chips belong to

On Layouts, Overlays and System variables, the state filter chips sit inside `role="group"` with the
accessible name `Filter by state`, identical to Rules.

```gherkin
Scenario: Layouts state filters are a named group
  Given the Layouts page has rendered
  Then there is a group named "Filter by state"
  And it contains the toggle buttons "All", "Draft", "Published", "Archived"

Scenario: Overlays state filters are a named group
  Given the Overlays page has rendered
  Then there is a group named "Filter by state"
  And it contains the toggle buttons "All", "Draft", "Published", "Archived"

Scenario: System variables state filters are a named group
  Given the System variables page has rendered
  Then there is a group named "Filter by state"
  And it contains the toggle buttons "All", "Defined", "Archived"

Scenario: Rules keeps its group (characterisation)
  Given the Rules page has rendered
  Then there is a group named "Filter by state" containing its state chips

Scenario: Filtering behaviour is unchanged
  Given any of the four pages
  When the user activates a chip
  Then it is the only chip with aria-pressed="true" and the list filters as before
```

Conflict / bad-request / auth scenarios: **N/A** — a client-side attribute on a static container; no
request is issued, no auth or data path changes.

### US2 (P2): A new ToggleChip filter cannot ship without the group

`Architecture.Tests` gains one source guard: every `<ToggleChip` line in non-test `apps/*/src` code has
`role="group"` and `aria-label=` within the three preceding lines. Small (one `[Fact]` reusing the existing
`ScannedSourceFiles`/`HitLines` helpers in `TextActionAndChipPrimitiveTests.cs`), so it is in scope; it
turns the convention from something a reviewer remembers into a build failure, which is the shape #3083
exists to repair.

## 4. Independent end-to-end test

`pnpm --filter management-web exec vitest run src/features/{layouts,overlays,systemVariables,rules}` —
the four page suites query `getByRole('group', { name: 'Filter by state' })` and the chips `within` it;
then `dotnet test tests/Architecture.Tests --filter FullyQualifiedName~TextActionAndChipPrimitive`.
Optional manual check: the browser accessibility tree on `/layouts` shows `group "Filter by state"`.

## 5. Locked choices and latency

React + TypeScript + Vite (ADR-0074), design-system primitive unchanged (ADR-0077), vitest + Testing
Library, `Architecture.Tests` source guard (pattern from spec 427). **§IV leg: N/A** — management console
only, nothing on the event-to-overlay path.

## 6. Out of scope

Radio semantics / `aria-pressed` → `aria-checked` (owner-deferred); un-pressing an already-pressed chip;
any change to `ToggleChip.tsx`; restyling the containers (the `mb-4` vs `flex gap-2` difference stays).
