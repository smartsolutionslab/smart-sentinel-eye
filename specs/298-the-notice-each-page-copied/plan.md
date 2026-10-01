# Plan 298: The notice each page copied

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2693

## Constitution / ADR check

| Rule | How this plan meets it |
|---|---|
| ADR-0148 — components cite semantic roles | `FaultNotice` cites `accent-fault`, `accent-fault-subtle`, `accent-fault-border`; no call-site alpha anywhere after this change. |
| ADR-0077 / ADR-0078 — shared composites, token-backed Tailwind | `FaultNotice` lives beside `RetryBanner` and `Badge` in `apps/shared/src/ui/composites`; held by `SharedUiTokenUsageTests` automatically. |
| ADR-0036 — smallest change, no speculative props | One prop (`children`). No variant/className. The mutation-notice logic is left where it is (spec §3.2). |
| ADR-0139 / ADR-0144 — red first, colour declared | Spec §7. |
| ADR-0109 — `[P]` on disjoint files | §1 below. |
| §IV latency | N/A (spec §6). |
| No ADR written | Spec §5. |

## Bounded context and layers

Frontend only. No bounded context, no backend, no contract, no token value change.

- `apps/shared/src/ui/composites/` — `FaultNotice.tsx` (new), `RetryBanner.tsx` (edit).
- `apps/shared/package.json` — one `exports` entry.
- `apps/management-web/src/features/{layouts,overlays,systemVariables,rules,walls}/` — five page
  files and their existing test files (new cases appended only).
- `tests/Architecture.Tests/ConsoleTriadAlphaTests.cs` (new).

## 1. File ownership (drives `[P]` — ADR-0109)

| Task group | Files owned | Parallel with |
|---|---|---|
| F | `FaultNotice.tsx`, `FaultNotice.test.tsx`, `RetryBanner.tsx`, `RetryBanner.test.tsx`, `apps/shared/package.json` | — (blocks all) |
| US1-L | `LayoutsPage.tsx`, `LayoutsPage.test.tsx` | US1-O, US1-S, US1-W |
| US1-O | `OverlaysPage.tsx`, `OverlaysPage.test.tsx` | US1-L, US1-S, US1-W |
| US1-S | `SystemVariablesPage.tsx`, `SystemVariablesPage.test.tsx` | US1-L, US1-O, US1-W |
| US1-W | `WallDetailPage.tsx`, `WallDetailPage.test.tsx` | US1-L, US1-O, US1-S |
| US1-R + US2 | `RulesPage.tsx`, `RulesPage.test.tsx` — **one owner for both stories** (same files) | US1-L/O/S/W |
| US3 | `ConsoleTriadAlphaTests.cs` | test may be written any time; turns green only after all page tasks |

## 2. `FaultNotice` — component contract

```tsx
import type { ReactNode } from 'react';

export interface FaultNoticeProps {
  children: ReactNode;
}

export function FaultNotice({ children }: FaultNoticeProps) {
  return (
    <div
      role="alert"
      className="mb-4 rounded-md border border-accent-fault-border bg-accent-fault-subtle px-3 py-2 text-sm text-accent-fault"
    >
      {children}
    </div>
  );
}
```

`package.json` `exports`: `"./ui/composites/FaultNotice": "./src/ui/composites/FaultNotice.tsx"`,
alphabetically beside the existing composite entries (`:44`–`:52`).

`RetryBanner` becomes:

```tsx
<FaultNotice>
  {message}{' '}
  <button type="button" className="underline" onClick={onRetry}>Retry</button>
</FaultNotice>
```

Its file comment (*"there is no `variant` prop"*) stays true and stays. The `tokens.css:139`
comment names `RetryBanner's box (issue #2523)` — update it to say the box is `FaultNotice`'s,
drawn for `RetryBanner` and the console's refusal notices (comment-only; no value moves).

## 3. Call-site changes

Each US1 page: replace the `<div role="alert" className="… accent-fault/…">` wrapper with
`<FaultNotice>`; the children (the `problemDetail(...)` expression, `{' '}`, the conditional
Reload `<button>` and its *"Reload, never retry"* comment) move inside **unchanged**.
`WallDetailPage`: `<FaultNotice>{backendError}</FaultNotice>`.

`RulesPage` US2: replace `:169-175`'s `<div>…<Button variant="secondary">Retry</Button></div>`
with `<RetryBanner message="Could not load rules." onRetry={() => void refetch()} />` inside the
same `isError ? … : <DataTable …/>` ternary. Add the `RetryBanner` import. `Button` stays
imported (the header and filter group use it).

Imports use the package path, mirroring the existing
`import { RetryBanner } from '@smart-sentinel-eye/shared/ui/composites/RetryBanner';`.

## 4. Tests

### 4.1 `apps/shared/src/ui/composites/RetryBanner.test.tsx` (new, characterisation)

Written and run **before** any edit to `RetryBanner.tsx`:

- `Renders the message and a Retry button inside one alert`
- `Calls onRetry once when Retry is clicked`
- `Paints the fault box on the fault tokens` — `toHaveClass('border-accent-fault-border',
  'bg-accent-fault-subtle', 'text-accent-fault', 'mb-4', 'rounded-md', 'px-3', 'py-2', 'text-sm')`.

Green on `56f2cde8`; must pass unmodified after F.

### 4.2 `apps/shared/src/ui/composites/FaultNotice.test.tsx` (new, red)

Against a signature-only stub (`return null`):

- `Renders its children inside one alert`
- `Renders a control passed as a child inside the alert`
- `Paints the fault box on the fault tokens and nothing translucent` — class set as §4.1, and
  no class matches `/\/\d+$/`.

### 4.3 Page red cases (one per site, appended to the existing test file)

Shape, reusing each file's existing mutation-state fixtures (`refusal(...)`, `publishState`,
`archiveState`, `setValueState`, `switchState`, `listMock`):

```ts
it('Shows a refused change on the shared fault notice, not call-site alpha', () => {
  publishState = { isLoading: false, error: refusal(409, 'LAYOUT_NAME_TAKEN') };
  renderPage();
  const alert = screen.getByRole('alert');
  expect(alert).toHaveClass('bg-accent-fault-subtle', 'border-accent-fault-border', 'text-accent-fault');
  expect([...alert.classList].some((c) => /\/\d+$/.test(c))).toBe(false);
});
```

- Layouts, Overlays, SystemVariables, Rules (mutation): as above, each with its own refusal.
- WallDetail: `refusal(409, 'WALL_STALE')`; additionally `within(alert).queryByRole('button')`
  is null (pins "no control" — currently green, so it is a characterisation half inside a red case;
  the red half is the class assertion).
- Rules (US2): `listMock.mockReturnValue({ isError: true, … })`; alert has the three fault
  classes. Red today (the box has no fill and no fault text).

Observed red on `56f2cde8` — the class assertion fails (`bg-accent-fault/10` present,
`bg-accent-fault-subtle` absent). Quote verbatim.

Each page renders at most one alert in the state each case sets up; if a file's fixture leaves
another alert mounted, scope with `getAllByRole('alert').find(...)` by text rather than relaxing
the assertion.

### 4.4 `tests/Architecture.Tests/ConsoleTriadAlphaTests.cs` (new, red)

Copy `WallStatusChipTests`' scan (`RepositorySource.Root()`, `ScannedFiles` over non-test
`.ts`/`.tsx`, `TypeScriptSource.StringLiteralContent`, the same `TranslucentTriadColour` regex)
rooted at `apps/management-web/src`, **without** the allowlist and its honesty fact. One fact:
`No_translucent_triad_colour_in_the_console`. Message cites ADR-0148 and points at
`FaultNotice`/`RetryBanner`/`Badge`.

Red on `56f2cde8` naming exactly (spec §1): `LayoutsPage.tsx` ×2, `OverlaysPage.tsx` ×2,
`RulesPage.tsx` ×3, `SystemVariablesPage.tsx` ×2, `WallDetailPage.tsx` ×2. A different set →
stop (the premise moved). Counterfactual (memory: *prove a guard by counterfactual*): after
green, re-add `bg-accent-fault/10` to one page locally and observe the fact fail naming it;
record, then revert.

Architecture.Tests is not sharded; no `ci-shards/*.filter` entry is needed (those are
`Integration.Tests` only).

## 5. Commit shape (ADR-0030; rebase-merge, ADR-0087 — each builds and passes on its own)

1. `test(shared): characterise RetryBanner's box` — `RetryBanner.test.tsx` (green).
2. `feat(shared): draw the fault box once, in FaultNotice` — `FaultNotice.tsx` + test +
   `exports` + `RetryBanner` through it + `tokens.css` comment. Commit 1's test unmodified.
3. `feat(console): show refused changes on FaultNotice` — the five US1 sites + their red cases.
4. `feat(rules): a failed rules load is a RetryBanner` — US2 + its red case.
5. `test(architecture): no translucent triad colour in the console` — US3 (green only after 3–4,
   so it lands last; its red run on `56f2cde8` is quoted in the PR, not committed red).

## 6. Verification (phase 5)

Spec §8. Latency: N/A, stated in the verification note. The note quotes one notice's computed
`background-color` beside `RetryBanner`'s from the same session.

## 7. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | A page test file leaves a second alert mounted, so `getByRole('alert')` throws in the new case. | Existing cases in the same files already use `getByRole('alert')` in the same states; mirror their setup exactly. §4.3 fallback scoping. |
| R2 | `RulesPage` spacing shifts (`mb-4` inside `space-y-4`). | Spec §4.2 argument + §8 step 4 screenshot pair. A real shift is a stop: the fix would be a prop, which this spec rules out, so it goes back to the architect. |
| R3 | `RulesPage`'s Retry changes from a `secondary` Button to `RetryBanner`'s underline link. | Declared visible change (spec §1). It matches the six other list pages. `RulesPage.test.tsx:275-284` finds it by role/name and stays unmodified. |
| R4 | Spec number collision with `sse-2692` / `sse-2698`. | Re-check before PR (tasks T002). |
