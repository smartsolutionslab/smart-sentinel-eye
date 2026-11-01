# Implementation Plan: The list-page scaffolding management-web pages repeat lives in one place

**Branch**: `471-list-page-scaffolding` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/471-list-page-scaffolding/spec.md`

## Summary

Extract three small pieces that `OverlaysPage`, `LayoutsPage`, `RulesPage` and `SystemVariablesPage` each rebuild —
the page heading that takes focus after a load recovers, the state-filter chip group, and the archive/discard
confirm-target state — into `apps/management-web/src/features/`, beside `ArchiveConfirmation` and `chainView`.
Pages keep their own columns, rows, dialogs, mutations, copy and query arguments. Behaviour-preserving, so
**characterisation** for the four pages (existing suites, unmodified) and **red-first** for the three new pieces.

**Honest sizing, up front.** These pieces remove an estimated **~90–110 lines** from the four pages (1323 today,
~7–8 %) and add ~110 lines of shared code plus tests, so the repository does not get smaller. What the change buys is
that the scaffolding exists once and each page reads as what is specific to it. The bulk of the
`OverlaysPage`↔`LayoutsPage` similarity (298 normalised diff lines) is **row rendering and action handlers**, which
the issue puts out of scope ("per-feature columns, dialogs and mutations stay") and which this change leaves alone.

## Technical Context

- **Stack**: TypeScript, React 19 (`ref` as a prop; `ToggleChip` already takes `ComponentPropsWithRef`), Vitest +
  Testing Library, `apps/management-web` (jsdom).
- **Baseline (2026-11-01, `develop` at 8c551733):** the four pages 1323 lines; management-web suite 666 tests.
- **Touches**: `apps/management-web/src/features/` only (new files beside `ArchiveConfirmation.tsx`, the four
  pages). No `apps/shared`, no test file of the four pages, no config.
- **Latency budget**: none.

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green; new behaviour starts red | Pages: characterisation. New pieces: red first (module not found), quoted in the PR. |
| ADR-0036 — smallest change, no speculative generality | Each piece has ≥ 4 callers today (confirm-target has 6 uses); nothing is built for a fifth that does not exist. |
| ADR-0077 — custom design system | Pieces compose existing primitives (`ToggleChip`); no new primitive, no new class names. |
| No new ADR | Extraction inside existing conventions; follows the precedent in `ArchiveConfirmation`'s own comment (one component where the behaviour is identical). |

## Research (decisions that shaped the plan)

### R1 — Where: `management-web/src/features/`, not `apps/shared`

`ArchiveConfirmation` and `chainView` already live there as helpers shared across the feature folders, and
`ArchiveConfirmation`'s own comment records the rule: share where the **behaviour** is identical. The two
management-cameras pages share only the heading wiring, and importing from management-web into another app is not
possible; moving these to `apps/shared` can happen when a second app needs them. Not speculative-generality now.

### R2 — The heading: a hook plus a component

```tsx
// useHeadingFocus.ts
export function useHeadingFocus() {
  const headingRef = useRef<ComponentRef<'h1'>>(null);
  const focusHeading = useCallback(() => headingRef.current?.focus(), []);
  return { headingRef, focusHeading };
}
// PageHeading.tsx
export function PageHeading({ ref, children }: { ref: Ref<HTMLHeadingElement>; children: ReactNode }) {
  return <h1 ref={ref} tabIndex={-1} className="text-2xl font-semibold focus:outline-none">{children}</h1>;
}
```

The pages' `onRecovered={() => headingRef.current?.focus()}` becomes `onRecovered={focusHeading}`. **Checked:**
`RetryBanner` stores `onRecovered` in a ref refreshed every render (`onRecoveredRef`), so a stable callback and an
inline arrow behave identically; no effect depends on its identity. The `<h1>` attributes are byte-identical to
today's (`ref`, `tabIndex={-1}`, the same `className`).

### R3 — The filter chips: one generic component, options as `{label, value}`

```tsx
<StateFilter options={STATE_FILTERS} value={filter} onChange={setFilter} className="mb-4 flex gap-2" />
```

Renders `<div className={className} role="group" aria-label="Filter by state">` and one `ToggleChip` per option
(`key={label}`, `pressed={value === option.value}`, `onClick={() => onChange(option.value)}`). `className` defaults
to `'flex gap-2'` and is used **verbatim** (not appended), because `RulesPage` has no `mb-4` and the class string
must not change. Rules already declares `{label, value}`; Overlays, Layouts and SystemVariables pass their string
unions through `labelled([...])`, a one-line helper (`values.map((v) => ({ label: v, value: v }))`). Each page keeps
its **own default** ('All', 'All', first option / `undefined`, 'Defined') and its own query wiring — the component
only renders and reports a click. Server-side vs client-side filtering (Rules, SystemVariables vs Overlays, Layouts)
is not touched.

### R4 — The confirm target: `useConfirmTarget<T>()`

```ts
export function useConfirmTarget<T>() {
  const [target, setTarget] = useState<T | null>(null);
  return {
    target,
    ask: setTarget,
    cancel: () => setTarget(null),
    // Same order as today: run, then clear. A null target is a no-op.
    confirm: (run: (target: T) => void) => () => {
      if (target === null) return;
      run(target);
      setTarget(null);
    },
  };
}
```

Replaces six declarations (`archiveFor` ×4, `discardFor` ×2) and six `onConfirm` bodies. Each page keeps the **shape
of its own target** as the type argument, so the field-name differences (`overlayIdentifier` vs `layoutIdentifier`
vs `name + fab`) stay in the page and no mutation argument is renamed. `ArchiveConfirmation` is unchanged.
`pending={archiving}`, the subject strings and the consequence copy stay in the page.

### R5 — What is deliberately not extracted

- **`refetchOnFocus: true`** is a one-line option literal in three pages; `RulesPage` does not have it at all and has
  no focus-refetch test. A wrapper hook would hide which query is refetching for no gain. Left in place. This
  amends spec FR-002 (item 4 was already conditional there).
- **The `RetryBanner` wiring** (`failed`, `retrying`, `onRetry`) is `RetryBanner` already being the shared piece; what
  remains is three expressions drawn from the page's own query, plus a page-specific message. Wrapping them would
  take the query state out of sight. Only `onRecovered` changes (R2). This amends FR-002 item 5.
- **`AuditPage`, `WallsPage`, `CamerasPage`, `CameraDetailPage`** keep their own heading wiring (SC-003 names only the
  four). Adopting `useHeadingFocus`/`PageHeading` there is mechanical and is a follow-up.
- **The row/menu/handler duplication** between `OverlaysPage` and `LayoutsPage` (out of scope per the issue).
- **The DOM order** of filter, banner and error notice differs between pages (Layouts: filter, banner, error; Rules:
  filter, error, banner). It is kept page by page: the pieces are bricks, not a page frame, precisely so the DOM
  does not change.

### R6 — Hook order

Each replacement sits at the position of the `useState`/`useRef` it replaces (heading first, then the page's other
state, filter before the query that reads it), so `useRevocationFallback` and every query still run after the same
state, in the same order, as today.

## Project Structure

```text
apps/management-web/src/features/
  ArchiveConfirmation.tsx          (unchanged)
  chainView.ts                     (unchanged)
  PageHeading.tsx                  (new)
  PageHeading.test.tsx             (new)
  useHeadingFocus.ts               (new)
  useHeadingFocus.test.tsx         (new)
  StateFilter.tsx                  (new; exports StateFilter and labelled)
  StateFilter.test.tsx             (new)
  useConfirmTarget.ts              (new)
  useConfirmTarget.test.tsx        (new)
  overlays/OverlaysPage.tsx        (shrinks)
  layouts/LayoutsPage.tsx          (shrinks)
  rules/RulesPage.tsx              (shrinks)
  systemVariables/SystemVariablesPage.tsx (shrinks)
specs/471-list-page-scaffolding/
```

## Delivery: one PR, commits that each stay green

1. The four new test files only, **red** (module not found) — failure quoted in the PR (`test(471): … (red)`).
2. The four modules; tests green, unmodified.
3. `OverlaysPage` adopts the pieces. 4. `LayoutsPage`. 5. `RulesPage`. 6. `SystemVariablesPage`. Each run with its
   page's three suites before the commit.
7. Task ticks, verification note.

Commit 1 leaves the new suite failing until commit 2 (accepted pattern, CLAUDE.md "builds is not passes").

## Verification (what "unchanged" means, concretely)

Recorded on the unchanged tree and repeated after:

- **Page suites (the characterisation):** per page, sorted `full name :: status` and sorted assertion lines of
  `*Page.test.tsx`, `*PageFocusRefetch.test.tsx`, `*PageRetryRecovery.test.tsx` — `diff` empty; and
  `git diff origin/develop...HEAD -- '**/*Page*.test.tsx'` empty (FR-001).
- **Whole management-web suite:** 666 → 666 + the new tests; duration before/after.
- **Counterfactuals for the new pieces** (the tests must be able to fail): break each piece once — drop `tabIndex`
  from `PageHeading`; make `focusHeading` a no-op; invert `pressed` in `StateFilter`; clear the target before running
  in `useConfirmTarget` — and quote that the matching test fails; restore.
- **A page-level counterfactual** (the pieces are really in the path): break `focusHeading`; the three pages' retry-
  recovery suites must fail.
- **Greps (SC-003):** no `headingRef`/`archiveFor`/`discardFor`/`STATE_FILTERS.map` left in the four pages, searched
  with the general pattern (`headingRef`, `useRef<ComponentRef<'h1'>>`, `For, set`) not one spelling.
- **Line count** of the four pages before/after, reported with the added shared lines, not just the pages' drop.
- `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` for management-web;
  `dotnet test tests/Architecture.Tests -c Release` (they scan `.tsx`).

## Risks

| Risk | Mitigation |
|---|---|
| A page suite relies on exact DOM (class order, attribute order) | `className` is used verbatim; `<h1>` attributes copied exactly; the suites are the check. |
| `confirm` captures a stale `target` | The returned handler is created each render from the current `target`, exactly like today's inline `onConfirm`; a test asserts it uses the latest. |
| `PageHeading`'s `ref` prop differs by React version | React 19 is in use (`ToggleChip` takes `ComponentPropsWithRef`); `tsc` gates it. |
| The saving is smaller than hoped (R, sizing) | Stated in the Summary; the PR reports measured lines for pages and for new code. |
| `Rules` has no focus-refetch test | `refetchOnFocus` is not touched (R5); nothing to cover. |
