# Plan 427 — The link and the pressed chip

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2668

## 1. Constitution and ADR check

| Authority | How this plan complies |
|---|---|
| ADR-0077 | Two primitives in `apps/shared/src/ui/primitives`. Native `<button>`: no Radix package offers a focus, keyboard or portal behaviour this needs (spec §3); `Button`/`Input` are precedent. **No ADR is written.** |
| ADR-0078 | Existing tokens only: `accent`, `accent-subtle`, `fg-muted`, `bg-hover`, `bg-pressed`, `focus-ring`, `fg-disabled`. No new token. |
| ADR-0146 / ADR-0148 | No `transition-*`, `duration-*`, `ease-*`, `animate-*` in either primitive; `MotionLanguageTests` fact 2's empty allowlist stays empty. |
| ADR-0151 | `LinkButton` passes `aria-disabled` through and never sets `disabled` itself; `cursor-progress` stays a call-site class (as `Button`'s `unavailable` doc prescribes). No `unavailable` prop: its one consumer (`ChainRecoveryNotice`) passes `aria-disabled` directly today, and ADR-0036 forbids a prop with no second user. |
| ADR-0036 | No `asChild`, `variant`, `size`. Every prop has a consumer in this PR. |
| ADR-0139 / ADR-0144 | Phase 4a **red**; pins declared green in advance (§5b). No gate weakened, no allowlist grown in an existing guard. |
| ADR-0087 | Seven commits, each typechecks alone (§6). |
| Constitution §IV | N/A — console only; no kiosk import (spec §4). |

Bounded contexts, `Shared.Contracts`, AppHost, migrations: untouched. Frontend (`apps/shared`,
`apps/management-web`) plus one new architecture-test class.

## 2. Layers touched

| File | Change | Story |
|---|---|---|
| `apps/shared/src/ui/primitives/LinkButton.tsx` (new) | §3a | US1 |
| `apps/shared/src/ui/primitives/LinkButton.test.tsx` (new) | §5a R1-R3, pins P1-P4 | US1 |
| `apps/shared/src/ui/primitives/ToggleChip.tsx` (new) | §3b | US1 |
| `apps/shared/src/ui/primitives/ToggleChip.test.tsx` (new) | §5a R4-R8, pins P5-P6 | US1 |
| `apps/shared/package.json` | `exports`: `"./ui/primitives/LinkButton": "./src/ui/primitives/LinkButton.tsx"`, `"./ui/primitives/ToggleChip": "./src/ui/primitives/ToggleChip.tsx"`, next to the other primitives | US1 |
| `apps/shared/src/ui/composites/RetryBanner.tsx` | L5 → `<LinkButton onClick={onRetry}>Retry</LinkButton>` (relative import `../primitives/LinkButton.js`, as `Button.tsx` is imported within shared) | US2 |
| `apps/shared/src/ui/composites/RetryBanner.test.tsx` | R9 added; existing cases unmodified | US2 |
| `apps/shared/src/ui/composites/ChainRecoveryNotice.tsx` | L6, L7 → `LinkButton` with `className="aria-disabled:cursor-progress"`; `ref`, `aria-disabled`, `onClick` unchanged; `controlClassName` deleted | US2 |
| `apps/management-web/src/features/{layouts/LayoutsPage,overlays/OverlaysPage,rules/RulesPage,systemVariables/SystemVariablesPage}.tsx` | L1-L4 → `LinkButton` (deep specifier); the preceding "Reload, never retry" comments stay | US2 |
| `tests/Architecture.Tests/TextActionAndChipPrimitiveTests.cs` (new) | §5a G1, G3, G4 (US2); G2 + its allowlist (US3) | US2/US3 |
| `apps/management-web/src/features/{layouts/LayoutsPage,overlays/OverlaysPage,systemVariables/SystemVariablesPage}.tsx` | C1-C3: `<ToggleChip key={option} pressed={option === filter} onClick={() => setFilter(option)}>{option}</ToggleChip>` | US3 |
| `apps/management-web/src/features/rules/RulesPage.tsx` | C4 (A1): `<ToggleChip key={filter.label} pressed={stateFilter === filter.value} onClick=…>`; the `role="group" aria-label="Filter by state"` wrapper stays | US3 |
| `apps/management-web/src/features/{layouts,overlays,rules,systemVariables}/*Page.test.tsx` | R10 per page | US3 |

Untouched (SC-5): `Button.tsx`, `GridDesigner.tsx`, `ShellLayout.tsx`, `apps/kiosk-web/**`, every `e2e/*`.

## 3. Primitive design (design-system level; no domain model)

### 3a. `LinkButton`

```ts
export type LinkButtonProps = ComponentPropsWithRef<'button'>;
export function LinkButton({ className, type, ...rest }: LinkButtonProps)
```

Renders `<button type={type ?? 'button'} className={clsx(base, className)} {...rest} />` with

`base = 'underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring aria-disabled:text-fg-disabled'`

Invariants:
- **No colour, size, weight, background or border utility at rest.** Colour and type come from the
  surrounding alert by inheritance (Tailwind preflight sets `color: inherit; font: inherit` on
  `button`). This is the owner's D2 sidestep: the primitive never chooses a colour.
- Focus ring identical to `Button`'s three utilities (parity).
- Unavailable treatment (`aria-disabled:` only — see R-d) is the neutral `text-fg-disabled` already used by
  `ChainRecoveryNotice` and `Button` (spec 268; ADR-0146 item 5: no opacity fade).
- Call-site `className` is appended last (`clsx(base, className)`).
- No motion utility.

Doc comment: why it inherits colour (D2 / #2668 owner decision), and that `cursor-progress` stays at the
call site (ADR-0151). Keep it short; `Button.tsx`'s long `unavailable` doc is not repeated — point at it.

### 3b. `ToggleChip`

```ts
export interface ToggleChipProps extends Omit<ComponentPropsWithRef<'button'>, 'aria-pressed'> {
  pressed: boolean;
}
export function ToggleChip({ pressed, className, type, ...rest }: ToggleChipProps)
```

Renders `<button type={type ?? 'button'} aria-pressed={pressed} className={clsx(base, pressed ? pressedFill : restFill, className)} {...rest} />`

- `base = 'rounded-md border px-3 py-1 text-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring'` (no `disabled:` treatment — no chip site disables; same reasoning as R-d)
- `pressedFill = 'border-accent bg-accent-subtle text-accent'` (today's selected chip, spec 287 US2)
- `restFill = 'border-border-subtle text-fg-muted hover:bg-bg-hover active:bg-bg-pressed'` (today's
  unselected chip, plus the hover/pressed feedback the issue asks for; the border was `border-fg-muted/30` until the shared-UI alpha guard `No_call_site_alpha_on_a_semantic_colour` rejected it, and `border-border-subtle` is the nearest non-alpha token: gray-800 on dark against ~30% of fg-muted over gray-950, gray-200 on light)

Invariants:
- `pressed` is required, destructured (never reaches the DOM), and is the **only** source of
  `aria-pressed` (`'aria-pressed'` is omitted from the props type).
- Exactly one of `pressedFill` / `restFill` per render; the pressed state carries **no** `hover:bg-*` /
  `active:bg-*`, because a hover background would override `bg-accent-subtle` while hovered and the
  selected chip would visibly lose its selection under the pointer.
- Accessible name is the children only.
- No motion utility.
- Geometry equals today's chips (`rounded-md border px-3 py-1 text-sm`), so C1-C3 do not move.

### 3c. Why not Radix Toggle

`@radix-ui/react-toggle` would add a dependency to provide `aria-pressed` (one attribute), `data-state`
(unused — the style is chosen in JS, §3b) and uncontrolled state (excluded by the owner's required
`pressed`). It contributes no focus management, keyboard handling or portal. ADR-0077's "zero
accessibility debt" rationale is satisfied by the native element, which *is* the ARIA toggle-button
pattern.

## 4. Messaging

N/A — no domain or integration event, no endpoint, no realtime message.

## 5. Tests

Local, no Docker: vitest, `tsc --noEmit`, eslint, prettier, `Architecture.Tests`. **Run `tsc --noEmit`
with every vitest run** (a missing named import binds to `undefined` silently in vitest). **Run
`prettier --check`** as well as eslint. `apps/shared` has no jest-dom: read classes via
`element.classList` / `getAttribute`, as `Button.test.tsx` does. Use `toHaveClass` only in
`apps/management-web` tests.

### 5a. Red (observed failing before the source change; failure quoted verbatim in the PR)

The red commit for US1 adds **stubs** of both primitives with their final exported **types** and a body
of `return <button {...rest} />` (ToggleChip's stub destructures `pressed` and discards it with `void
pressed;`) so `typecheck` passes and every test reaches its assertion instead of failing on import —
the spec 425 / spec 268 "declaration only" precedent.

| # | Where | Asserts | Why red at the stub |
|---|---|---|---|
| R1 | `LinkButton.test.tsx` | default `type="button"`; `type="submit"` honoured | stub writes no `type` |
| R2 | `LinkButton.test.tsx` | `underline` + the three focus-visible ring utilities | stub has no class |
| R3 | `LinkButton.test.tsx` | `aria-disabled` → attribute `"true"`, class `aria-disabled:text-fg-disabled`, not `disabled` | no class |
| R4 | `ToggleChip.test.tsx` | `pressed` → `aria-pressed="true"`; `pressed={false}` → `"false"`; `getByRole('button', { name: 'Archived', pressed: true })` resolves | stub sets no `aria-pressed` |
| R5 | `ToggleChip.test.tsx` | pressed → `border-accent`, `bg-accent-subtle`, `text-accent`; unpressed → none of them, and `border-border-subtle`, `text-fg-muted` | no class |
| R6 | `ToggleChip.test.tsx` | unpressed → `hover:bg-bg-hover`, `active:bg-bg-pressed`; pressed → no class starting `hover:bg-` or `active:bg-` | first half red; second half green at the stub (state it) |
| R7 | `ToggleChip.test.tsx` | three focus-visible ring utilities; `rounded-md border px-3 py-1 text-sm` | no class |
| R8 | `ToggleChip.test.tsx` | default `type="button"` | stub writes no `type` |
| G1 | `TextActionAndChipPrimitiveTests.cs` `An_underlined_text_action_is_a_LinkButton` | regex `(?<![\w-])underline(?![\w-])` (matches `hover:underline`, not `no-underline`/`underline-offset-*`) over comment-stripped non-test `apps/*/src/**/*.tsx`; allowed only in `apps/shared/src/ui/primitives/LinkButton.tsx` | red naming exactly 6 `file:line`s: `LayoutsPage.tsx:158`, `OverlaysPage.tsx:146`, `RulesPage.tsx:182`, `SystemVariablesPage.tsx:147`, `RetryBanner.tsx:15`, `ChainRecoveryNotice.tsx:302` |
| R9 | `RetryBanner.test.tsx` | Retry carries `focus-visible:outline-focus-ring` and still `underline` | raw button has only `underline` |
| G2 | `TextActionAndChipPrimitiveTests.cs` `A_selected_chip_is_a_ToggleChip` | bare `bg-accent-subtle` (`(?<![\w:\[\]-])bg-accent-subtle(?![\w/-])` — excludes `data-[highlighted]:` prefixed uses in `CommandPalette`/`DropdownMenu`/`Select`) only in `ToggleChip.tsx`, or on an allowlisted `(path, match, reason)` line: `ShellLayout.tsx` / `font-medium text-accent` (NavLink `aria-current`, not a toggle); `GridDesigner.tsx` / `relative rounded-md border border-accent` (native radio, spec 228 FR-001) | red naming `LayoutsPage.tsx:138`, `OverlaysPage.tsx:126`, `SystemVariablesPage.tsx:129` |
| R10 | each of the four `*Page.test.tsx` | on first render `getByRole('button', { name: 'All', pressed: true })` resolves and every other state filter is `pressed: false`; after `user.click(Archived)` (Rules: `Active`) the pressed state moves | no `aria-pressed` anywhere |

G3 `Each_allowlist_entry_still_applies` (file exists, still contains its match) and G4
`The_scan_is_not_empty` (≥ 1 `.tsx` scanned under each of `apps/shared/src` and `apps/management-web/src`)
are **green in advance**; prove each by a planted counterfactual (rename an allowlist match; point the
root at an empty directory), quoted in the PR. Mirror `KioskButtonPrimitiveTests.cs`'s comment stripping
(line numbers preserved) and use `RepositorySource.RelativePath` (forward slashes — a backslash literal
is green on Windows and red on Linux CI).

### 5b. Characterisation / pins (green before and after, unmodified)

- P1-P4 (`LinkButton.test.tsx`, green at the stub, **declared**): no unprefixed `text-*`/`bg-*`/`border-*`
  class; a passed `ref` is the `HTMLButtonElement`; call-site `className` (`aria-disabled:cursor-progress`)
  kept; no `transition*`/`duration-`/`ease-`/`animate-` class.
- P5-P6 (`ToggleChip.test.tsx`, declared green): no `pressed` DOM attribute; no motion class.
- `RulesPage.test.tsx:483` (`Retry` `toHaveClass('underline')`), `RetryBanner.test.tsx`, `FaultNotice.test.tsx`.
- `LayoutEditorDialogChainRecovery`, `…ChainAnnouncement`, `…SaveGate`, `OverlayDraftFormChainRecovery`,
  `…ChainAnnouncement`, `…SaveGate`, `OverlayCreatePage.test.tsx`, `OverlayDraftForm.test.tsx` — the focus,
  `aria-disabled` and remount contract of `ChainRecoveryNotice`.
- The four pages' existing filter tests (`RulesPage.test.tsx:322-333`, `SystemVariablesPage.test.tsx:656`,
  Layouts/Overlays filter cases) and their `getAllByRole('button')` row scans.
- `MotionLanguageTests` (all facts), `KioskButtonPrimitiveTests`, both apps' `tokens.build.test.ts`.
- CI only: `e2e/interaction-states.spec.ts` "a selected filter chip is drawn in the accent…" and the
  focus-ring cases; `e2e/motion.spec.ts`.

## 6. Commit sequence (each typechecks alone)

| # | Commit | Content | Suite state |
|---|---|---|---|
| C1 | `docs(2668): specify, plan and task LinkButton and ToggleChip` | this folder | green |
| C2 | `test(2668): LinkButton and ToggleChip primitives (red)` | both test files; both stubs; `package.json` `exports` | R1-R8 red; pins green |
| C3 | `feat(2668): LinkButton and ToggleChip primitives` | both primitives per §3 | green |
| C4 | `test(2668): underlined text actions are LinkButton (red)` | `TextActionAndChipPrimitiveTests.cs` G1, G3, G4; R9 | G1, R9 red |
| C5 | `fix(2668): text actions in fault alerts use LinkButton` | L1-L7 | green |
| C6 | `test(2668): state filters are ToggleChip and announce pressed (red)` | G2 + its allowlist; R10 ×4 | G2, R10 red |
| C7 | `fix(2668): state filters use ToggleChip` | C1-C4 | green |

C2/C3, C4/C5, C6/C7 are phase-4a pairs (a bisect landing on C2/C4/C6 re-runs one commit over).
C1-C3 is a mergeable prefix (primitives only); C4-C5 and C6-C7 are independent of each other and each
depends only on C3. Each commit is a stand-alone `typecheck` (stubs carry the final types).

## 7. Risks

- **R-a: the pressed accessible name.** Testing Library's `pressed` filter reads `aria-pressed`; a chip
  whose name included "(selected)" would break `name: /^archived$/i` in `interaction-states.spec.ts`. The
  design keeps the name to the label (§3b).
- **R-b: `hover:bg-bg-hover` on the unpressed chip changes C1-C3's hover look.** Intended (the issue asks
  for hover/pressed states). No pre-existing test pins its absence.
- **R-c: Rules filters (A1) lose the filled-primary look.** The only non-identity visual change. Droppable
  per spec §6.
- **R-d: no `disabled:` treatment on `LinkButton`, deliberately.** No site disables a text action
  natively (ADR-0151 sends them to `aria-disabled`), so a `disabled:` class would have no consumer
  (ADR-0036). Do not add it reflexively.
- **R-e: remotes rendered stand-alone** (not under the shell) would lack the shared classes — already true
  for `Button`; unchanged by this spec.

## 8. Follow-ups (text for the orchestrator, not `agent:ready`)

> **The Layouts / Overlays / System variables state filters have no group label.** `RulesPage.tsx:159`
> wraps its filters in `role="group" aria-label="Filter by state"`; the other three do not, so a screen
> reader hears a run of toggle buttons with no context. One attribute pair per page. Related: #2668.
