# Spec 427 — The link and the pressed chip: `LinkButton` and `ToggleChip`

**Issue:** [#2668](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2668) (feature-level;
no per-task issues, per CLAUDE.md Phase 3). Carved out of #2623 / spec 287 (plan §10, item D2).
**Branch:** `feat/2668-link-button-toggle-chip` (cut from `origin/develop` `235d7ed4`, which already
carries spec 425 / #2667: `Button`'s `size` lookup and the removal of its `transition-colors`).
**Lane:** autonomous (ADR-0144). **Not security-sensitive** (no endpoint, token, scope or input path).

**Decision implemented (owner, recorded on #2668, 2026-10-21).** *Separate primitives, not `Button`
variants.* `LinkButton` — text-only, colour **inherited** from the surrounding alert (which sidesteps
spec 287 open question D2: no colour is chosen for it at all). `ToggleChip` — required
`pressed: boolean`, sets `aria-pressed`, accent-subtle style. Then migrate the underline sites and the
chip sites, including the shared `RetryBanner.tsx` / `ChainRecoveryNotice.tsx`. **No ADR** (ADR-0077
already places primitives in `apps/shared/src/ui/primitives`). **`Button` is untouched** — #2667 just
changed its sizing and motion; this spec does not couple to it.

**ADRs this spec is bound by:** ADR-0077 (design-system primitives), ADR-0078 (Tailwind tokens via CSS
custom properties), ADR-0146 (console: transform and opacity only — no paint transitions), ADR-0148
(prohibition by absence), ADR-0151 (`aria-disabled` keeps focus; cursor treatment stays at the call
site), ADR-0036 (no speculative generality), ADR-0139 / ADR-0144 (phase-4a colours, the lane),
ADR-0087 (every commit builds on its own). **Predecessors:** spec 268 (Button states), spec 287 (the
inventory, D2), spec 292 (the motion guard), spec 425 (`Button` size, M2, `KioskButtonPrimitiveTests`).

---

## 1. The premise, re-verified on `235d7ed4` (2026-10-21)

| # | Issue / brief says | Tree says |
|---|---|---|
| P1 | "7 underlined text actions" vs. "one review found 6" | **Both are right.** 7 *elements* in 6 *files*: `ChainRecoveryNotice.tsx` has two buttons (Retry, Reload) sharing one class string at `:302`. `grep -rn underline apps --include=*.tsx` (tests excluded) returns exactly 6 lines. Spec 287 §1 rows 17-23 list the same 7. |
| P2 | 4 chip sites incl. `GridDesigner` | **3 raw chip groups + 1 `Button` chip group; `GridDesigner` is not a button.** `GridDesigner.tsx:200-232` is a native radio group (`<label>` over `<input type="radio">`, spec 228 US1 FR-001: roving tab stop and arrow keys from the platform). A `ToggleChip` there would regress that. `RulesPage.tsx:159-170` is a filter group the issue missed: `<Button variant={selected ? 'primary' : 'ghost'}>` inside `role="group" aria-label="Filter by state"`. |
| P3 | `ShellLayout.tsx:178` | A `NavLink` active state (`aria-current`), not a chip. Out. |
| P4 | Nothing uses `aria-pressed` | **Holds.** No hit in `apps/` or `e2e/`. |
| P5 | Radix Toggle available? | **Not installed.** `apps/shared/package.json` has `react-slot`, `-dialog`, `-alert-dialog`, `-dropdown-menu`, `-popover`, `-select`, `-tabs`, `-tooltip`. `Button` (native + `Slot`) and `Input` (native, no Radix) are precedent for a native primitive where Radix adds no focus/portal/keyboard behaviour. |
| P6 | Barrel export | `apps/shared/src/ui/primitives/index.ts` exports **nothing** (`export {};`). Every consumer imports a deep specifier listed in `apps/shared/package.json` `exports` (`./ui/primitives/Button` ×26). New primitives get `exports` entries, not barrel lines. |
| P7 | Federated remotes' Tailwind | `management-cameras` / `management-event-ingestion` scan only their own `./src` (`tailwind.config.ts:5-8`: "the shell's build already generates the shared primitives' classes"); the shell (`management-web`) and the kiosk scan `../shared/src/**`. Both remotes render `RetryBanner`. Every class the new primitives use already exists in the shell's sheet today (`underline`, the three `focus-visible:` ring utilities and `aria-disabled:text-fg-disabled` from `Button`/`ChainRecoveryNotice`; `hover:bg-bg-hover` / `active:bg-bg-pressed` from `Button`; the chip colour classes from the three pages). |
| P8 | Motion | `MotionLanguageTests` fact 2's `PaintTransitionAllowlist` is `[]` since spec 425; any `transition-colors` in a new primitive fails the build. Neither primitive needs motion. |

### 1.1 The real site list

| # | Site | Today | Decision |
|---|---|---|---|
| L1 | `apps/management-web/src/features/layouts/LayoutsPage.tsx:158` *Reload* | `<button className="underline">` in `FaultNotice` | **`LinkButton`** |
| L2 | `apps/management-web/src/features/overlays/OverlaysPage.tsx:146` *Reload* | same | **`LinkButton`** |
| L3 | `apps/management-web/src/features/rules/RulesPage.tsx:182` *Reload* | same | **`LinkButton`** |
| L4 | `apps/management-web/src/features/systemVariables/SystemVariablesPage.tsx:147` *Reload* | same | **`LinkButton`** |
| L5 | `apps/shared/src/ui/composites/RetryBanner.tsx:15` *Retry* (shell + both remotes) | same | **`LinkButton`** |
| L6 | `apps/shared/src/ui/composites/ChainRecoveryNotice.tsx:358` *Retry* (`ref`, `aria-disabled`) | `controlClassName` (`:302`): `underline aria-disabled:text-fg-disabled aria-disabled:cursor-progress` | **`LinkButton`**, `className="aria-disabled:cursor-progress"` |
| L7 | `apps/shared/src/ui/composites/ChainRecoveryNotice.tsx:379` *Reload* (`aria-disabled`) | same | **`LinkButton`**, same; `controlClassName` deleted |
| C1 | `LayoutsPage.tsx:131-144` state filters (All/Draft/Published/Archived) | raw `<button>`, selected `border-accent bg-accent-subtle text-accent` | **`ToggleChip`** |
| C2 | `OverlaysPage.tsx:119-132` state filters | same | **`ToggleChip`** |
| C3 | `SystemVariablesPage.tsx:122-135` state filters (All/Defined/Archived) | same | **`ToggleChip`** |
| C4 | `RulesPage.tsx:159-170` state filters | `Button` primary (selected) / ghost | **`ToggleChip`** — *assumption A1, droppable (§6)* |
| — | `GridDesigner.tsx:200-232` grid preset | native radio under a styled `<label>` | **Stays.** Radio semantics are correct and spec 228 built them deliberately. |
| — | `ShellLayout.tsx:178` `NavItem` | `NavLink` + `aria-current` | **Stays.** A link, not a toggle. |
| — | kiosk | no underline or chip site (`grep` empty) | N/A |

**Assumption A1 (marked, not asked — the lane cannot ask).** The owner's "4 chip sites" were counted
with `GridDesigner`, which is not a toggle. `RulesPage`'s filter group is the same control as C1-C3
(single-select state filter), currently drawn as a filled `primary` button and announcing no selection
at all. Migrating it makes the four filter groups one control. It is a separate task pair (US3's last)
so review can drop it without touching anything else; its visible change (filled primary → accent-subtle)
is the only visual change in this spec that is not a re-spelling of an existing look.

## 2. User stories

### US1 (P1): `LinkButton` and `ToggleChip` exist as design-system primitives

**As** a console contributor, **I want** a text action that reads as a link and a selection chip that
announces its state, **so that** I stop hand-spelling `className="underline"` and the chip triad, and
so a screen reader hears which filter is on.

```gherkin
Feature: LinkButton

  Scenario: A text action inherits its colour (happy)
    Given <FaultNotice><LinkButton onClick={retry}>Retry</LinkButton></FaultNotice>
    Then it is a native <button type="button"> named "Retry"
    And  it carries `underline`
    And  it carries no unprefixed text-*, bg-* or border-* utility   # colour and size come from the alert
    And  it carries focus-visible:outline-2, focus-visible:outline-offset-2, focus-visible:outline-focus-ring

  Scenario: Unavailable while focused (ADR-0151, conflict)
    Given <LinkButton aria-disabled={true} className="aria-disabled:cursor-progress">Retry</LinkButton>
    Then it carries aria-disabled="true" and aria-disabled:text-fg-disabled
    And  the call site's aria-disabled:cursor-progress is kept
    And  it is not natively disabled

  Scenario: Ref reaches the native element (ChainRecoveryNotice restores focus through it)
    Given a ref passed to LinkButton
    Then ref.current is the HTMLButtonElement

  Scenario: No state animates (ADR-0146)
    Then no class starts with transition, duration-, ease- or animate-

Feature: ToggleChip

  Scenario: The pressed chip announces it (happy)
    Given <ToggleChip pressed>Archived</ToggleChip>
    Then it is a button named exactly "Archived" with aria-pressed="true"
    And  it carries border-accent, bg-accent-subtle and text-accent

  Scenario: The unpressed chip announces it and offers hover/press feedback
    Given <ToggleChip pressed={false}>All</ToggleChip>
    Then aria-pressed="false"
    And  it carries border-border-subtle, text-fg-muted, hover:bg-bg-hover, active:bg-bg-pressed
    And  none of border-accent, bg-accent-subtle, text-accent

  Scenario: A pressed chip has no hover fill to race its accent fill (conflict)
    Given <ToggleChip pressed>Archived</ToggleChip>
    Then no class starts with hover:bg- or active:bg-

  Scenario: pressed is required and owned (bad request)
    Then omitting `pressed` is a TypeScript error
    And  `aria-pressed` is not an accepted prop                          # tsc --noEmit
    And  the rendered <button> has no `pressed` attribute

  Scenario: Same focus ring as Button; no motion
    Then it carries the three focus-visible ring utilities
    And  no transition/duration/ease/animate utility

  # Auth: N/A — no endpoint, token or scope.
```

**Independent test:** `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/ui/primitives/LinkButton.test.tsx src/ui/primitives/ToggleChip.test.tsx`
plus `pnpm --filter @smart-sentinel-eye/shared typecheck`. No Docker.

### US2 (P2): The seven underlined text actions are `LinkButton`

**As** an operator reading a fault alert, **I want** *Retry* and *Reload* to look and focus the same
everywhere, **so that** the action reads as one affordance across every page and both remotes.

```gherkin
  Scenario: No hand-spelled underline remains (happy)
    Given apps/*/src/**/*.tsx, tests excluded, comments stripped
    Then the `underline` utility (any variant prefix) appears only in apps/shared/src/ui/primitives/LinkButton.tsx

  Scenario: RetryBanner's Retry draws the design-system focus ring (shell and both remotes)
    Given <RetryBanner message="…" onRetry={fn} />
    Then the Retry button carries focus-visible:outline-focus-ring and still `underline`
    And  clicking it calls onRetry once

  Scenario: ChainRecoveryNotice keeps its focus contract (characterisation)
    Given the existing *ChainRecovery / *ChainAnnouncement suites
    Then they pass unmodified

  Scenario: The guard's allowlist cannot rot (honesty), and the scan cannot pass empty
```

**Independent test:** `dotnet test tests/Architecture.Tests --filter FullyQualifiedName~TextActionAndChipPrimitive`,
`RetryBanner.test.tsx`, and the four pages' + the six chain-recovery suites in `apps/management-web`. No Docker.

### US3 (P3): The state filters are `ToggleChip`, and say which one is on

**As** an operator using a screen reader, **I want** the state filters to announce *pressed*, **so that**
I know which filter the list is showing without seeing the accent.

```gherkin
  Scenario: The current filter is pressed (happy)
    Given /layouts (likewise /overlays, /system-variables, /rules)
    Then the button named "All" has aria-pressed="true"
    And  every other filter button has aria-pressed="false"

  Scenario: Choosing a filter moves the pressed state (happy)
    When the operator clicks "Archived"
    Then "Archived" is pressed and "All" is not
    And  the list query is called with that state      # existing tests, unmodified

  Scenario: Selected chip keeps the accent (characterisation, CI)
    Given e2e/interaction-states.spec.ts "a selected filter chip is drawn in the accent…"
    Then it passes unmodified

  Scenario: No hand-spelled selected chip remains
    Then bare `bg-accent-subtle` appears in apps/*/src/**/*.tsx only in ToggleChip.tsx, plus the
         allowlisted ShellLayout.tsx NavItem and GridDesigner.tsx preset label (each with a reason)
```

## 3. Locked choices (applied, not decided)

- **Native `<button>`, not `@radix-ui/react-toggle`.** The owner fixed the API as controlled-only
  (`pressed` required); Radix Toggle's added value is uncontrolled state plus `data-state`, neither
  needed. A native `<button aria-pressed>` is the WAI-ARIA toggle-button pattern with no keyboard or
  focus behaviour to supply. No dependency is added (ADR-0036). `Input` and `Button` are precedent.
- **No `asChild`, no `variant`, no `size` on either primitive** — no consumer needs one (ADR-0036).
- **React 19 ref-as-prop**: props extend `ComponentPropsWithRef<'button'>` (spec 156's `Button`
  convention), not `forwardRef`. `ToggleChip` omits `'aria-pressed'` from its props.
- **`clsx`, one class slot per state** — the pressed/unpressed strings are mutually exclusive, so no two
  colour utilities race in the compiled sheet (the defect `Button.tsx`'s spec-268 comment records).
- **Deep import specifiers** `@smart-sentinel-eye/shared/ui/primitives/LinkButton` and `…/ToggleChip`,
  added to `apps/shared/package.json` `exports`. The empty barrel is not touched.
- **Accessible name excludes state.** The chip's name stays its label; state lives only in
  `aria-pressed`, so `getByRole('button', { name: /^archived$/i })` keeps matching.

## 4. Latency budget (constitution §IV)

**N/A — no leg.** Every site is console-only (`apps/management-*`, and shared composites rendered by the
console): `RetryBanner` is imported by the console shell and the two console remotes; `ChainRecoveryNotice`
by the layout editor and overlay draft form. Nothing in `apps/kiosk-web` imports either new primitive or
any migrated file; `LayoutGrid.tsx` and the wall render tree are untouched.

## 5. Phase-4a colour: **red** (behaviour-changing), with characterisation pins

New primitives, new `aria-pressed` semantics, a new focus-ring colour on seven text actions, new hover
feedback on unpressed chips, and (A1) a changed look on the Rules filters. Red evidence and pins are
listed per test in plan §5.

## 6. Scope

**In:** two primitives + their tests + `exports` entries; L1-L7; C1-C3; C4 (A1, droppable); one new
architecture guard; page-level `aria-pressed` tests.

**Out:** `Button` (any change); `GridDesigner` preset (radio); `ShellLayout` nav; a `role="group"` label
on C1-C3 (RulesPage has one; the others do not — a follow-up, not this spec); a radio-group rewrite of
the filters (the owner chose toggle semantics); any e2e addition (attributes are proved in jsdom; the
colour characterisation already exists in `interaction-states.spec.ts`).

**If A1 is dropped:** delete T042's `RulesPage` test cases and T043's `RulesPage.tsx` change; nothing
else depends on them.

## 7. End-to-end procedure (Phase 5)

1. `pnpm --filter @smart-sentinel-eye/shared test`, `typecheck`, `lint`; same for
   `@smart-sentinel-eye/management-web`, `management-cameras`, `management-event-ingestion`; `pnpm format:check`.
2. `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~TextActionAndChipPrimitive|FullyQualifiedName~MotionLanguage|FullyQualifiedName~KioskButtonPrimitive"`.
3. `grep -rn "underline" apps --include=*.tsx | grep -v "\.test\."` → exactly `LinkButton.tsx`.
4. CI: `e2e/interaction-states.spec.ts` (selected chip accent; focus-ring cases) green unmodified.
5. With a browser (CI or a human): `/layouts`, provoke a 409 or load failure, Tab to *Reload*/*Retry* —
   cyan focus ring, red underlined text; click *Archived* — accessibility tree shows `pressed`.

## 8. Success criteria

- SC-1: The `underline` utility exists in one source file, `LinkButton.tsx`.
- SC-2: All four state-filter groups expose exactly one `aria-pressed="true"` button.
- SC-3: `MotionLanguageTests` and `KioskButtonPrimitiveTests` green, unmodified.
- SC-4: Every pre-existing test touching these sites passes unmodified (plan §5b).
- SC-5: `git diff --name-only` shows no change to `Button.tsx`, `GridDesigner.tsx`, `ShellLayout.tsx` or `apps/kiosk-web`.
