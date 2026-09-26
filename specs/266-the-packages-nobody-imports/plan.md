# Plan 266: The packages nobody imports

**Spec**: [spec.md](spec.md) · **Issue**: #2335 · **Phase**: 2 (Plan)
**Written against the defaults of spec §0 Q2–Q5.** An answer other than the default changes
only the section named in that row. Q1 (command palette) changes nothing here.

## Constitution / ADR check

| Rule | How this plan meets it |
|---|---|
| ADR-0077 — primitives on Radix, visual code in the repo | Each primitive wraps the Radix package already installed. No new runtime dependency. |
| ADR-0148 — cite the semantic layer only | §2 lists every class each primitive uses; all are semantic (`bg-bg-*`, `fg-*`, `border-*`, `accent-*`, `focus-ring`, `shadow-popover`, `z-popover`, `rounded-*`, spacing). Enforced by the existing `SharedUiTokenUsageTests` and `DesignTokenLayerTests`, unchanged. |
| ADR-0146 — shadow only for what floats, no blur, triad is status | `shadow-popover` on floating content only; no `backdrop-blur`; no triad hue on any interactive affordance except the `danger` menu item, which reuses `Button`'s existing `danger` precedent (fault = destructive). |
| ADR-0151 — `disabled` vs `unavailable` | Menu items and tabs use `disabled`: a Radix menu closes on select, so an item never holds focus as it becomes unavailable. The Select trigger takes `disabled` only; no call site disables it while focused. |
| ADR-0150 — wait on a condition | Every async assertion is `findBy*` / `waitFor` / Playwright `expect(...).toBeVisible()`. No fixed sleeps; §5.3 deletes one. |
| ADR-0139 / 0144 — red first | Signature-only stubs make the red land on content (§6). |
| ADR-0036 — no speculative generality | No variant, size or slot prop without a consumer in this spec. No animation (#2334). No `hover:` treatment beyond what §2 names (#2336). |
| ADR-0087 — each commit builds | Stubs keep `tsc --noEmit` green in the red commit (§6). |
| §III bounded contexts | N/A — no backend, no `Shared.Contracts`, no messaging. |
| §IV latency | N/A — `kiosk-web` imports no primitive (spec §5). |

## Bounded context and layers

**None.** Frontend-only, in two workspaces:

- `apps/shared/src/ui/primitives/` — four new primitives (the design system, ADR-0077).
- `apps/management-web/src/features/{rules,layouts,cameras}/` — three consumers.
- `tests/Architecture.Tests/` — one source-scanning guard (C#, test code only).

No entity, value object, domain event, integration event, endpoint or migration. The
"invariants" of this work are the component contracts in §3 and the guard in §5.1.

## 1. File ownership (drives the `[P]` markers — ADR-0109)

| Area | Files | Story |
|---|---|---|
| Foundation (serial, blocks all) | `apps/shared/package.json` (devDep + 4 `exports` entries), `apps/shared/src/test/radixJsdom.ts` (new), `apps/shared/src/test/setup.ts`, `apps/management-web/src/test/setup.ts`, `pnpm-lock.yaml` | — |
| Guard | `tests/Architecture.Tests/SharedUiDependencyUsageTests.cs` | US5 |
| Select | `apps/shared/src/ui/primitives/Select.tsx`, `Select.test.tsx` | US1 |
| Select consumer | `apps/management-web/src/features/rules/RuleDialog.tsx`, `RuleDialog.test.tsx`, `e2e/rules.spec.ts` | US1 |
| DropdownMenu | `apps/shared/src/ui/primitives/DropdownMenu.tsx`, `DropdownMenu.test.tsx` | US2 |
| DropdownMenu consumer | `apps/management-web/src/features/layouts/LayoutsPage.tsx`, `LayoutsPage.test.tsx`, `e2e/kiosk-reconciliation.spec.ts`, `e2e/support/archive-e2e-layouts.teardown.ts` | US2 |
| Popover | `apps/shared/src/ui/primitives/Popover.tsx`, `Popover.test.tsx` | US3 |
| Popover consumer | `apps/management-web/src/features/cameras/StreamHealthBadge.tsx`, `StreamHealthBadge.test.tsx` | US3 |
| Tabs | `apps/shared/src/ui/primitives/Tabs.tsx`, `Tabs.test.tsx` | US4 |

The four `exports` entries go in the **foundation** task so no story task touches
`apps/shared/package.json` — it is the one file every story would otherwise contend on.

## 2. Token mapping (every class a primitive may use)

Shared by all floating content (Select content, DropdownMenu content, Popover content —
the same recipe `Tooltip.tsx` already uses, so the family matches):

```
z-popover rounded-md border border-border-subtle bg-bg-raised text-fg-primary shadow-popover
```

High-contrast sets `--shadow-popover: 0 0 #0000` and `--color-border-subtle` to `gray-500`,
so the border carries separation there with no per-theme code.

| Element | Rest | Highlighted / selected | Focus-visible | Disabled |
|---|---|---|---|---|
| Select trigger | `border border-border-strong bg-bg-elevated px-3 py-2 text-sm text-fg-primary rounded-md`; placeholder `data-[placeholder]:text-fg-muted` | open: `data-[state=open]:border-accent` | `focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring` | `disabled:text-fg-disabled disabled:border-border-subtle` |
| Select item | `px-3 py-2 text-sm rounded-sm outline-none` | `data-[highlighted]:bg-accent-subtle`; checked indicator `text-accent` | via highlighted (Radix moves real focus) | `data-[disabled]:text-fg-disabled` |
| Menu item | same as Select item; `danger` variant `text-accent-fault` | `data-[highlighted]:bg-accent-subtle` | via highlighted | `data-[disabled]:text-fg-disabled` |
| Menu separator | `my-1 h-px bg-border-subtle` | — | — | — |
| Popover trigger | caller-supplied (`asChild`) | — | caller's | — |
| Tabs list | `flex gap-1 border-b border-border-subtle` | — | — | — |
| Tab trigger | `px-3 py-2 text-sm text-fg-muted border-b-2 border-transparent` | `data-[state=active]:text-fg-primary data-[state=active]:border-accent` | `focus-visible:ring-2 focus-visible:ring-focus-ring` | `data-[disabled]:text-fg-disabled` |

Deliberately absent: `hover:` (Radix `data-highlighted` covers pointer and keyboard on
items; triggers get their hover in #2336's matrix, guarded by `@media (hover: hover)`),
`opacity-*` for disabled (#2336 item 4), any `transition-*` or `animate-*` (#2334), and
`backdrop-blur-*` (ADR-0146).

**No new token.** Every role above exists in `tokens.css` at `7aab60de`.

## 3. Component contracts

Props-object style, matching `Dialog`, `Tooltip` and `ConfirmDialog` — not re-exported
Radix compound parts. A caller never imports `@radix-ui/*` directly; that is what keeps
the classes in one place.

### 3.1 `Select.tsx` (US1)

```ts
export interface SelectOption {
  value: string;           // non-empty; "" is the placeholder's (spec §1 finding 3)
  label: string;
  disabled?: boolean;
}

export interface SelectProps {
  id: string;                         // FormField's htmlFor targets the trigger
  value: string | undefined;          // undefined or "" shows the placeholder
  onValueChange: (value: string) => void;
  options: readonly SelectOption[];
  placeholder?: string;
  disabled?: boolean;
  name?: string;                      // renders Radix's hidden native select for form posts
  onBlur?: () => void;                // RHF Controller's field.onBlur
  'aria-invalid'?: boolean;
  'aria-describedby'?: string;
  ref?: Ref<HTMLButtonElement>;       // RHF focuses the trigger on a validation error
}
```

- Renders `Root` → `Trigger` (`id`, `aria-*`, `ref`) with `Value` + an icon-free chevron
  glyph (`▾`, `aria-hidden`) → `Portal` → `Content position="popper" sideOffset={4}` →
  `Viewport` → one `Item` per option with `ItemText` and `ItemIndicator` (`✓`, `aria-hidden`).
- **Guard at the boundary** (spec US1 "bad request"): an option
  whose `value` is `""` throws, in every build (a programming error, not input to tolerate)
  `Error('Select option "<label>" has an empty value; use placeholder for "nothing chosen"')`.
  Frontend argument checking stays inline; `Ensure.That` is the .NET convention.
- `ref` is a prop (React 19), mirroring `Button`'s `ComponentPropsWithRef` choice (spec 156).

### 3.2 `DropdownMenu.tsx` (US2)

```ts
export type MenuEntry =
  | { kind: 'item'; label: string; onSelect: () => void; disabled?: boolean; variant?: 'default' | 'danger' }
  | { kind: 'separator' };

export interface DropdownMenuProps {
  trigger: ReactNode;              // rendered via Trigger asChild — must be a focusable element (a Button)
  entries: readonly MenuEntry[];
  align?: 'start' | 'end';         // default 'end' (row actions sit at the right edge)
}
```

- `Root modal={false}`. **This is the load-bearing choice.** A modal Radix menu that
  closes while a Radix dialog opens from its `onSelect` leaves `pointer-events: none` on
  `<body>` and races the two focus scopes — the dialog's close then returns focus to
  `<body>`. Non-modal menus avoid both. Spec US2 scenario 2 ("the page accepts pointer
  input", "focus is on that row's trigger") is the test that holds this.
- `onSelect` is invoked **after** the menu has closed: the item's Radix `onSelect`
  schedules the caller's callback so the dialog mounts once the menu's focus scope has
  released. The unit test proves the ordering by observable outcome (the dialog's Cancel
  has focus; on dismiss the trigger has focus), not by counting ticks (ADR-0150).
- `danger` reuses the fault token for a destructive entry, per `Button`'s `danger`.

### 3.3 `Popover.tsx` (US3)

```ts
export interface PopoverProps {
  trigger: ReactNode;              // Trigger asChild — must be focusable
  children: ReactNode;
  label: string;                   // aria-label for the dialog-role content
  side?: 'top' | 'right' | 'bottom' | 'left';   // default 'bottom'
}
```

- `Content` with `role="dialog"` (Radix default) and `aria-label={label}`, the §2 floating
  recipe plus `px-3 py-2 text-xs`, `Arrow` in `fill-bg-raised` (as `Tooltip`).
- No close button: Escape and outside-click dismiss; the content is read-only text.

### 3.4 `Tabs.tsx` (US4)

```ts
export interface TabDefinition {
  value: string;
  label: string;
  content: ReactNode;
  disabled?: boolean;
}

export interface TabsProps {
  tabs: readonly TabDefinition[];
  label: string;                   // aria-label on the tab list
  value?: string;                  // controlled
  defaultValue?: string;           // uncontrolled; defaults to the first enabled tab
  onValueChange?: (value: string) => void;
  activationMode?: 'manual' | 'automatic';   // default 'manual' (spec US4)
}
```

- Guard: a duplicate `value` throws naming it, in every build.
- Inactive panels are unmounted (Radix default, no `forceMount`) — a panel with live media
  must not keep its session open behind another tab.

## 4. Consumer changes

### 4.1 `RuleDialog.tsx` — Action (US1)

Replace the native `<select id="rule-action-type" {...register('actionType')}>` with a
React Hook Form `Controller` rendering `Select`:

```tsx
<Controller
  name="actionType"
  control={control}
  render={({ field }) => (
    <Select
      id="rule-action-type"
      value={field.value}
      onValueChange={field.onChange}
      onBlur={field.onBlur}
      ref={field.ref}
      options={ACTION_OPTIONS}
      aria-invalid={errors.actionType !== undefined}
    />
  )}
/>
```

`ACTION_OPTIONS` is a module constant with the two existing labels verbatim. `watch('actionType')`,
the `unregister` effect and `renderedFields` are **unchanged** — `field.onChange` updates the
same form value the native `onChange` did, so the effect fires on the same render
transition. `control` is added to the `useForm` destructure. FormField is unchanged; its
`<label htmlFor>` targets the trigger `<button>` (a labelable element).

The Fab `<select>` in the same file stays native (spec §3.1).

### 4.2 `LayoutsPage.tsx` — row actions (US2, Q4 default)

Inline, unchanged: **Publish** (when a draft exists) and **Edit (new draft)** (when live or
fully archived). Into one `DropdownMenu` per row, trigger
`<Button variant="ghost" aria-label={`More actions for ${chain.name}`}>More actions</Button>`:
**Discard draft**, **Revert**, **Archive** (`variant: 'danger'`), each with the
**same** condition and **same** handler body it has today, and `disabled` from the row's
existing `disabled` flag. The trigger is omitted when no entry applies. The long comments
beside Edit and Archive move with their entries.

### 4.3 `StreamHealthBadge.tsx` — detail (US3)

- `stream === undefined`: unchanged (inert `<span>`, "Unknown").
- Otherwise: `Popover` whose trigger is a `<button type="button">` carrying the existing
  `PILL` + tone classes (unchanged) plus `focus-visible:ring-2 focus-visible:ring-focus-ring`,
  and whose content is the existing `buildTooltip(...)` lines, one `<p>` each.
  `label={`Stream ${stream.state}`}`.
- The `Tooltip` import goes. The tone map is not touched (Badge is its own spec).

## 5. Tests

All frontend tests: Vitest + Testing Library + `user-event` 14.6.6,
`// @vitest-environment jsdom`, sentence-style names (ADR-0053). `apps/shared`'s
`setup.ts` already imports `@testing-library/jest-dom/vitest`, so `toHaveAttribute` /
`toHaveFocus` are available (the Button test's comment saying otherwise predates that).

### 5.1 `tests/Architecture.Tests/SharedUiDependencyUsageTests.cs` (US5)

- Fact `Every_radix_package_the_shared_workspace_declares_is_imported`: parse
  `apps/shared/package.json` `dependencies` (`System.Text.Json`), take every key starting
  `@radix-ui/`, and for each require one `.ts`/`.tsx` under `apps/shared/src` — excluding
  `*.test.*` and `src/test/` — whose comment-stripped text contains `from '<package>'`.
  Failure message names every unused package and says "import it from a primitive, or
  remove it from dependencies".
- Follow `SharedUiTokenUsageTests` for `RepositoryRoot()`, comment stripping and
  **slash normalising** of relative paths (repo memory: a backslash literal is green on
  Windows and red on Linux).
- **Counterfactual** (goes in the PR body, not the suite): in a scratch copy, add
  `"@radix-ui/react-switch": "1.0.0"` to `dependencies`, run the fact, quote the failure,
  revert.
- **Red on develop:** names `react-dropdown-menu`, `react-popover`, `react-select`,
  `react-tabs`. The stubs (§6) do **not** import Radix, so the guard stays red until each
  real implementation lands.

### 5.2 Primitive tests (US1–US4) — one file each, beside the component

| File | Cases (spec scenario → assertion) |
|---|---|
| `Select.test.tsx` | ArrowDown on trigger opens `listbox` with the option names; selected option has `aria-selected="true"`; choosing calls `onValueChange` with the value; Escape closes and focus is on the trigger (`toHaveFocus`); `disabled` trigger does not open on Enter; `""` option throws with the label in the message; placeholder shows when `value` is `undefined`; `aria-invalid`/`aria-describedby` reach the trigger; trigger is found by `getByLabelText` when wrapped in `FormField`. |
| `DropdownMenu.test.tsx` | Enter on trigger opens `menu` and focuses the first enabled `menuitem`; ArrowDown moves; Escape closes and returns focus; `onSelect` fires once; a `disabled` item has `aria-disabled` and does not fire; separator renders `role="separator"`; **an item that opens a `ConfirmDialog`**: its Cancel has focus, and after Escape the menu trigger has focus and `document.body.style.pointerEvents` is not `none`. |
| `Popover.test.tsx` | Enter on trigger opens a `dialog` named by `label` containing children; Escape closes and focus returns to trigger; outside pointer-down closes. |
| `Tabs.test.tsx` | tablist named by `label`; ArrowRight moves focus without activating (manual); Enter activates and shows the panel; `activationMode="automatic"` activates on arrow; disabled tab skipped; duplicate value throws. |

### 5.3 Consumer tests

- **`RuleDialog.test.tsx`** — new case, red: *"The action field opens a listbox of the two
  actions"* (ArrowDown on `getByRole('combobox', { name: /action/i })` → `findByRole('listbox')`
  with both option names). Existing `user.selectOptions(getByLabelText(/action/i), X)` at
  lines 98, 246, 247, 264 become a local helper `chooseAction(user, label)` (click trigger,
  click `option` by name). Their **assertions do not change** (spec §6).
- **`LayoutsPage.test.tsx`** — new cases, red: the row shows "Publish" and "Edit (new draft)"
  as buttons and **no** "Revert"/"Archive"/"Discard draft" button; "More actions" opens a
  menu with those three items; Archive → confirmation → Escape → focus on "More actions".
  Existing clicks at lines 338, 764, 780, 784, 792 open the row menu first via a helper;
  assertions unchanged.
- **`StreamHealthBadge.test.tsx`** — new cases, red: the Degraded badge is a `button`;
  Enter opens a dialog containing `Error: source unreachable`; a Healthy stream with an
  error shows no `Error:` line; Unknown is not a button. **Delete** the existing
  *"Surfaces the error string in the tooltip content"* case: it cannot fail (spec §1
  finding 4) and waits a fixed 250 ms (ADR-0150). Its replacement is the Enter case above.
  The tone-class cases stay unchanged (declared green pins).

### 5.4 e2e

- `e2e/rules.spec.ts:157-158`: replace `selectOption` with
  `page.getByRole('combobox', { name: /^action$/i }).click()` then
  `page.getByRole('option', { name: 'Highlight an overlay' }).click()` (and back).
- `e2e/kiosk-reconciliation.spec.ts:90-93` and
  `e2e/support/archive-e2e-layouts.teardown.ts:166-178`: open
  `row.getByRole('button', { name: /more actions/i })` and click
  `page.getByRole('menuitem', { name: /^archive$/i })` — the menu content is portalled, so
  it is **not** inside `row`. The teardown's "row has no Archive" wait becomes "row has no
  More actions trigger, or its menu has no Archive item"; keep its timeouts.
- No new e2e spec: the existing three exercise every consumer in a real browser, which
  is where jsdom's shimmed pointer capture cannot mislead.

## 6. Red that lands on content (the stubs)

Phase 4a commits, **with** the tests, one stub per primitive: the exact §3 interface and a
component body `return null;` — no Radix import. This means:

- `tsc --noEmit` passes in the red commit (ADR-0087: each commit builds on its own);
- the primitive tests fail on content ("Unable to find role 'combobox'"), not on
  "Failed to resolve import", which would prove nothing about behaviour;
- the US5 guard stays red (the stubs import no Radix package).

Consumer tests need no stub: they run against today's native select / button row /
tooltip and are red on content.

**Required 4a outcome** (quote verbatim): every case in §5.2 red (the stub renders
nothing, so there is no primitive case that can pass). Every §5.3 new case red; the
declared pins green; the guard red naming exactly four packages. Any case expected red that arrives green
is a stop-and-report, not an adjustment.

## 7. jsdom shim (`apps/shared/src/test/radixJsdom.ts`)

Radix calls APIs jsdom does not implement: `Element.prototype.hasPointerCapture`,
`setPointerCapture`, `releasePointerCapture` (Select trigger, `dist/index.mjs` line 211),
`Element.prototype.scrollIntoView` (Select content, lines 341 and 1041) and
`ResizeObserver` (`@radix-ui/react-use-size`, used by every popper). The module installs
no-op implementations **only when absent**, with a header saying why and that real-browser
behaviour is covered by e2e (§5.4). Imported from `apps/shared/src/test/setup.ts`; exported
as `"./test/radixJsdom"` in `apps/shared/package.json` and imported from
`apps/management-web/src/test/setup.ts`, so the shim exists once.

## 8. Commit sequence (each commit builds on its own, ADR-0087)

1. `docs(266): specify, plan and task the missing Radix primitives` (this phase).
2. `test(ui): enable Radix under jsdom and user-event in the shared workspace` (foundation — green; no behaviour).
3. `test(ui): pin every declared Radix package to an import, red-first` (US5 guard, red).
4. Per story, two commits: `test(ui): <primitive> and its first consumer, red-first` (tests + stub + consumer test edits, red), then `feat(ui): <primitive> ...` (implementation + consumer + e2e edits, green).
   US1 → US2 → US3 → US4. After US4 the guard is green.

## 9. Verification (Phase 5)

Spec §7, against a running stack, all three themes, with screenshots of each floating
surface. Additionally: keyboard-only walk of each consumer with a screen reader's
accessibility tree (Chromium DevTools) showing `combobox`/`listbox`, `menu`/`menuitem`,
`dialog` names.

## 10. Risks

| Risk | Mitigation |
|---|---|
| Menu → dialog focus/pointer-events race | `modal={false}` + deferred `onSelect` (§3.2), held by a unit test **and** the e2e teardown, which drives it in Chromium on every CI run. |
| Select inside `Dialog` (the rule dialog is a Radix Dialog) | Select content portals at `z-popover` (300) above the dialog's `z-overlay` (200). Radix's layered `DismissableLayer` handles Escape: first press closes the listbox, second the dialog. Covered by the US1 Escape case. |
| jsdom shims hide a real-browser failure | Every consumer is also exercised in Chromium by an existing e2e spec (§5.4). |
| The teardown change breaks every layout e2e run | It is the one e2e edit on the critical path; it lands in US2's feat commit with `layouts.spec.ts` run locally before push. |
| #2336 restyles these immediately | §2 builds only states the token file already expresses; #2336's matrix is additive (hover/pressed/loading), not a rewrite. |
| Q3 answered "defer" | Drop US4's tasks; replace with removing `@radix-ui/react-tabs` from `dependencies` (+ its `exports` entry is never added). Guard still closes. |
