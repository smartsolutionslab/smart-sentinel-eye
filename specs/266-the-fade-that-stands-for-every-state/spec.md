# Spec 266 — The fade that stands for every state

**Issue:** [#2336](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2336)
— *Every interactive state is hover:opacity-90*. Label `enhancement`. Phase 02 of the
frontend redesign programme. On Project #13, status **Todo** — verified 2026-09-26 via
`gh issue view 2336 --json projectItems`. No `item-add` needed.

**Gate.** The issue was held on #2329 (design language) and #2332 (tokens). Both are
closed: #2329 by PR #2338 (ADR-0146/0147/0148), #2332 by PR #2604 (spec 257, merged
2026-09-26 00:53 UTC). This spec is written against what those two shipped, on
`origin/develop` at `7aab60de`, not against the question as #2336 first framed it.

**Spec number.** Every remote branch (`git ls-tree` of each `specs/`), every local
worktree's `specs/` directory (tracked or not) and both open PRs were listed on
2026-09-26. The highest claimed number is **265** (`fix/2486-legacy-bundle-remainder`,
untracked in `D:\Github\sse-2486b`); 264 is `fix/2575`, 263 is `feat/2608`, 262 is
`feat/2607`. **266 is free.** Re-check immediately before opening the PR — two parked
PRs can still land a 266 between now and then.

**ADRs referenced:**

- **ADR-0146** (one discipline, two surfaces), discipline item 5: *"Real interaction
  states. Rest, hover, pressed, focus-visible, disabled and loading, designed per
  variant. A uniform opacity fade is not a state."* This spec implements that sentence.
  Discipline item 4 and §"The ground and the accent": the triad is not affordance colour;
  the cyan accent's *"job is interactive affordance and selection, nothing else."*
- **ADR-0148** (two-layer tokens in OKLCH), decision 2: *"State variants are derived,
  not chosen"* via `color-mix(in oklch, …)`. The five tokens this spec adds are derived
  roles under that rule.
- **ADR-0151** (a disable that keeps the operator's place): `unavailable` /
  `aria-disabled` semantics, which the new disabled and busy treatments must not break.
- **ADR-0077** (Radix headless + own design system), **ADR-0078** (Tailwind + CSS custom
  properties): the mechanism, unchanged.
- **ADR-0139 / ADR-0144**: phase-4a colour — **red** (§6).
- **ADR-0036** (smallest change), **ADR-0037** (phases and gates), **ADR-0109** (`[P]`).

**No new ADR is needed for US1 or US3.** §4 names the one question — the *loading*
visual — that ADR-0146 does not answer, and which US2 therefore carries as an explicit
open decision rather than a guess.

**Latency budget (§IV): N/A.** No file the kiosk imports changes behaviour. See §5.

---

## 1. The issue's premise, re-checked on this tree (`7aab60de`, 2026-09-26)

| Claim in #2336 | On this tree |
|---|---|
| `primary` and `danger` hover as `hover:opacity-90` | **True.** `Button.tsx:74,80`. It is the only `hover:opacity` in `apps/**/src`. |
| No pressed state on any variant | **True.** No `active:` utility anywhere in `apps/**/src`. |
| Focus ring has no colour | **True for `Button`** (`ring-2 ring-offset-2`, no colour). `Input.tsx`, `DataTable.tsx` and `GridDesigner.tsx` *do* state one — `ring-accent-active`, the triad's green, which ADR-0146 forbids as affordance colour. |
| Disabled is `opacity-50` on already-muted text | **True.** `Button` (`disabled:` and `aria-disabled:`), `Input` (`disabled:`), `ChainRecoveryNotice` (`aria-disabled:`). |
| Touch panels latch hover after a tap | **Already false on this tree, by the framework.** Tailwind **4.3.3** wraps every `hover:` utility in `@media (hover: hover)` (`tailwindcss/dist/lib.js`: `i.static("hover", … B("@media","(hover: hover)", …))`), and compiling `hover:bg-accent-hover` through management-web's real config confirms it. No hand-written `:hover` exists in any `apps/**/*.css`. Requirement 3 is therefore a **pin** (§6), not work. |
| Async actions give no feedback | **Partly false.** Eight call sites swap their label (`'Saving…'`, `'Registering…'`, `'Running…'`, `'Creating…'`) while a mutation is in flight. What is missing is a *state* — `aria-busy`, and a treatment that is not "disabled". |

Findings the issue does not mention, each material to scope:

1. **Spec 257 handed three things to this issue by name** (257 spec §3 "Deferred"):
   Button's states; the triad used as affordance (`primary` is `bg-accent-active`;
   `Input`'s and `DataTable`'s focus rings are `ring-accent-active`); and
   `forced-colors`, because Tailwind's `ring-*` is a `box-shadow` and forced-colors mode
   drops box-shadows — the focus ring vanishes. This spec closes all three.
2. **The shipped `--color-accent-disabled` cannot carry a legible label.** Measured
   (plan §2.3): every shipped foreground role on it is ≤ 2.6:1 (`fg-disabled` 1.74:1 dark,
   1.80:1 light). A disabled button built from it is exactly the illegible disabled the
   issue complains about. This spec's disabled treatment is therefore neutral and
   variant-independent; `--color-accent-disabled` stays for non-text uses (#2335's
   switch/checkbox tracks).
3. **A secondary/ghost hover of `bg-bg-elevated` inverts on a raised surface.** A button
   inside a Dialog (`bg-bg-raised`, lighter) hovers *darker*. The neutral states need a
   tint that works on any ground (plan §2.2).
4. **The shipped fault pressed-step would fail text contrast.** Mirroring the accent's
   `mix(84%, black)` onto `--color-accent-fault` gives 3.99:1 for the dark label — below
   4.5. The fault derivation is its own (plan §2.1).
5. **32 raw `<button>` elements** in both apps never touch the primitive — including the
   kiosk's touch targets and the console's sign-in buttons, several painted
   `bg-accent-active`. They are out of this slice (§3) and inventoried for a follow-up
   (plan §7).
6. **Six call sites natively `disabled={isLoading}` a mutation button, and `ConfirmDialog` does the same with `disabled={pending}` on both its buttons** — the ADR-0151
   focus-loss shape. Not this spec's defect to fix (it changes focus behaviour, and
   ADR-0151 requires a guard and an Enter-key Playwright test per site); recorded in
   plan §7.

## 2. User stories

### US1 (P1): Every Button variant has a designed state matrix

An operator at the console sees, for each of `primary`, `secondary`, `ghost` and
`danger`: a rest state; a hover that only a hover-capable pointer can trigger; a pressed
state that is visibly distinct from hover rather than a deeper fade; a focus ring in a
stated colour that survives forced-colors mode; and a disabled state that reads as
unavailable without dropping below 3:1. `primary` stops being the triad's green and
becomes the cyan accent. `danger`'s pressed state moves in the opposite direction from
its hover, so committing a destructive action is a different gesture from pointing at it.

**Why P1:** it is the issue's whole finding and ADR-0146 item 5 verbatim. It is
independently shippable: `Button` is one file, and every console call site inherits it.

**Independent test:** sign in to management-web, open Cameras, and exercise *Register
camera* (primary), *Cancel* in its dialog (secondary), and on a camera's detail page
*Retire camera* (danger) — hover, press-and-hold, Tab to focus, and a disabled
instance — reading computed styles (spec §7).

```gherkin
Feature: Button interaction states

  Background:
    Given the console is rendered in a real browser with the dark theme

  Scenario Outline: rest, hover and pressed are three different fills (happy path)
    Given a <variant> Button at rest
    When a mouse pointer hovers it
    Then its background colour differs from rest
    When the mouse button is held down on it
    Then its background colour differs from both rest and hover
    And its label colour is unchanged from rest
    And no state applies an opacity below 1 to the button
    Examples:
      | variant   |
      | primary   |
      | secondary |
      | ghost     |
      | danger    |

  Scenario: primary is the affordance accent, not the triad
    Given a primary Button at rest
    Then its background resolves to --color-accent
    And not to --color-accent-active

  Scenario: danger's pressed moves opposite to its hover
    Given a danger Button
    Then its hover fill is lighter than its rest fill
    And its pressed fill is darker than its rest fill
    And its label keeps at least 4.5:1 against rest, hover and pressed

  Scenario: a touch tap does not latch a hover fill (pin: already true)
    Given a device whose primary pointer cannot hover
    When a primary Button is tapped and released
    Then its background resolves to its rest fill

  Scenario: focus-visible draws a stated, offset outline
    When a Button receives focus from the keyboard
    Then its outline is 2px solid in --color-focus-ring
    And it is offset 2px from the button's edge
    And no box-shadow ring is used to draw it

  Scenario: focus survives forced-colors mode
    Given forced-colors is active
    When a Button receives focus from the keyboard
    Then an outline is still drawn

  Scenario Outline: disabled reads as unavailable, not as a fade (bad request: the action cannot run)
    Given a <variant> Button that is <how>
    Then it has no fill and a --color-border-subtle border
    And its label is --color-fg-disabled
    And its label keeps at least 3:1 against every surface token of the theme
    And hovering or pressing it does not change its fill
    Examples:
      | variant | how                     |
      | primary | natively disabled       |
      | danger  | natively disabled       |
      | primary | unavailable (ADR-0151)  |
      | ghost   | unavailable (ADR-0151)  |

  Scenario: an unavailable Button still keeps the operator's place (conflict: ADR-0151 pin)
    Given an unavailable Button holding focus
    Then it remains focused
    And its onClick still fires when clicked
```

**Auth:** N/A — no endpoint, scope or token is involved. The e2e reaches the Button
through the ordinary seeded console sign-in and asserts nothing about authorisation.

### US2 (P2): An async action shows that it is in flight

A console action that round-trips shows a *busy* state that is not the *disabled*
state: the Button announces `aria-busy="true"`, shows a progress cursor, keeps its
variant's rest fill (it is working, not refused), and suppresses hover and pressed
feedback so a second press is not invited. The call site's existing label swap
(`'Saving…'`) stays where it is — the copy belongs to the feature.

**Decision D1 (§4) is resolved: option (a), the static busy state below.** It ships in this PR;
#2334 may add an indicator later without changing the `busy` prop.

**Independent test:** in the Rename camera dialog, submit and hold the network (route
interception), then read `aria-busy`, the cursor and the fill while the request is
pending.

```gherkin
Feature: Button busy state

  Scenario: busy announces and keeps the rest fill (happy path)
    Given a primary Button with busy set
    Then it has aria-busy="true"
    And its cursor is progress
    And its background resolves to its rest fill, not the disabled treatment

  Scenario: busy does not change how the Button is disabled (conflict: ADR-0151)
    Given a Button with busy set and neither disabled nor unavailable
    Then it is neither natively disabled nor aria-disabled
    And given a Button with busy and disabled set
    Then it is natively disabled
    And given a Button with busy and unavailable set
    Then it is aria-disabled and still focusable

  Scenario: busy suppresses hover and pressed feedback
    Given a primary Button with busy set
    When a mouse pointer hovers and presses it
    Then its background stays at its rest fill

  Scenario: no busy prop leaves the button unannounced (bad request: nothing in flight)
    Given a Button without busy
    Then it has no aria-busy attribute
```

### US3 (P3): Every focus ring in the tree cites the focus token

`Input`, `DataTable`'s sort header and `GridDesigner`'s preset chips draw the same
focus outline as `Button`, in `--color-focus-ring`, and `Input`'s disabled state uses
the same legible treatment. After this story no `ring-*` or `outline-*` utility in
`apps/**/src` cites a triad colour, and no focus indicator is drawn with a box-shadow.

**Why P3:** it finishes the triad-as-affordance handover from spec 257 for focus rings
(the four sites are the *only* focus declarations in the tree), and it is small. It is
independent of US1 only in files; it reuses US1's focus recipe.

**Independent test:** Tab into the Register camera dialog's first field, and to a
Cameras table sort header, and to a layout editor preset chip; read `outline-color` and
`box-shadow` on each.

```gherkin
Feature: One focus indicator

  Scenario Outline: focus-visible uses the focus token as an outline (happy path)
    When <control> receives focus from the keyboard
    Then its outline colour resolves to --color-focus-ring
    And its box-shadow draws no ring
    Examples:
      | control                        |
      | an Input                       |
      | a DataTable sort header button |
      | a GridDesigner preset chip     |

  Scenario: a disabled Input is legible (bad request: the field cannot be edited)
    Given a disabled Input
    Then its text is --color-fg-disabled with no opacity applied

  Scenario: no triad colour draws a focus indicator (conflict: ADR-0146)
    Given every .ts and .tsx file under apps/*/src, excluding tests
    Then no ring-* or outline-* utility names accent-active, accent-fault or accent-warning
    And no focus-visible:ring-* utility remains
```

## 3. Scope

### In this PR

| # | What | Story |
|---|---|---|
| 1 | Five derived colour roles in `tokens.css` (plan §2), mapped in `tailwindTheme.ts` | US1 |
| 2 | `Button.tsx`: the state matrix per variant; `primary` → accent; outline focus; neutral disabled for `disabled` and `unavailable` | US1 |
| 3 | `Button.tsx`: `busy` prop; adopted at `ConfirmDialog`'s confirm button and the eight label-swap call sites (plan §6) as a pure addition beside their existing `disabled`/`unavailable` | US2 |
| 4 | `Input.tsx`, `DataTable.tsx`, `GridDesigner.tsx`: focus outline; `Input` disabled; `ChainRecoveryNotice.tsx`: `aria-disabled:opacity-50` → the disabled label colour | US3 (+US1 disabled rule) |
| 5 | One C# architecture test, additions to both `tokens.build.test.ts`, `Button.test.tsx` cases, one Playwright spec | all |

### Deferred, and to where

| What | Owner | Reason |
|---|---|---|
| New primitives' state matrices (Select, Tabs, Switch, …) | **#2335** | They adopt this spec's recipe as they are built; `--color-accent-disabled` is kept for their tracks. |
| Motion on press (scale, spring), an animated busy indicator, reduced-motion | **#2334** | ADR-0146 allows console motion "on state change … transform and opacity only"; the *language* is #2334's. This spec's only motion is the existing `transition-colors` at `--duration-fast`. |
| 32 raw `<button>` elements → `<Button>`; triad-as-selection chips and triad-green raw buttons (`App.tsx`, `ShellLayout.tsx`, `LayoutsPage.tsx`, `OverlaysPage.tsx`, `SystemVariablesPage.tsx`, `GridDesigner.tsx` chip fill, kiosk `App.tsx`/`PickerPage.tsx`/`CellPage.tsx`/`ReconnectingScreen.tsx`) | **Follow-up issue — not yet filed** | Two apps, ~20 files, and the kiosk's touch targets; not independently reviewable alongside the primitive. Inventory in plan §7 so it is not re-measured. |
| Six `disabled={isLoading}` buttons and `ConfirmDialog`'s `disabled={pending}` (ADR-0151 focus-loss shape) | **Follow-up issue — not yet filed** | Changes focus behaviour; ADR-0151 demands a guard and an Enter-key Playwright test per site. |
| Light-theme triad text contrast | Spec 257 §4 item 1 (open) | Unchanged here; the danger label is dark on red in every theme (plan §2.1), so this spec does not depend on it. |

## 4. Decisions: what is settled, and the one that is not

**Settled by ADR, applied mechanically:** that a state matrix exists (ADR-0146 item 5);
that `primary` uses the accent and focus uses a non-triad colour (ADR-0146 item 4 and
§accent; spec 257's handover); that state colours are `color-mix` derivations of a role
(ADR-0148 decision 2); that `unavailable` keeps focus (ADR-0151).

**Chosen here as implementation detail inside those ADRs, each written down with its
measurement in plan §2:** the five new derived roles and their percentages; the neutral
disabled recipe and its ≥ 3:1 target (WCAG 1.4.3 exempts inactive components, so the
threshold is this spec's choice — 3:1 keeps it legible while reading as inactive); an
outline rather than a ring for focus; `danger` keeping the fault red at rest.

> Confirmed 2026-09-26 (relayed by the coordinator): `danger` keeps the fault red at rest. ADR-0146 says the triad is "not
> available as brand colour". A destructive button is not brand, and the existing
> `Button.tsx` comment argues deliberately for the fault red; spec 257 inventoried the
> triad violations and did **not** list `danger`. This spec follows that precedent.
> If the reviewer reads ADR-0146 more strictly, the alternative (outline-danger at rest,
> red fill only on hover/pressed) changes one class string in T012 and no token.

**Decision D1 — resolved 2026-09-26: option (a), relayed by the coordinator.** The question as it was put:
**What does *loading* look like?** ADR-0146 lists *loading* as a state and describes
console motion only as "on state change and surface entry, 120–200 ms". A continuous
spinner is neither, and the motion language is #2334's (open). Options:

- **(a) Static busy — the default this spec plans against.** `aria-busy`, progress
  cursor, rest fill held, hover/pressed suppressed, call-site label swap. No animation.
  Ships now; #2334 may later add an indicator without changing the prop.
- **(b) Hold US2 until #2334** defines a busy motion, and ship US1 + US3 alone.
- **(c) Add a spinner now** — which decides a piece of #2334's motion language inside
  #2336. Not recommended.

## 5. Latency budget (§IV)

**N/A — no leg is touched.** `apps/kiosk-web` imports `CameraViewer`, `ErrorBoundary`
and API/observability modules from `apps/shared` — **not** `Button`, `Input`,
`DataTable`, `ConfirmDialog` or `ChainRecoveryNotice` (grep of every
`@smart-sentinel-eye/shared` import in `apps/kiosk-web/src`, 2026-09-26). The only
shared file the wall loads that changes is `tokens.css`, which gains five custom
properties nothing on the wall cites. No compositing layer, animation, filter or
shadow is added anywhere. Phase 5 still cites the spec-225 render-leg figure from the
PR's CI run, since `tokens.css` is on the wall's import path.

## 6. Phase-4a colour: **red**

This is visible behaviour change on every console Button. **Declared green in advance
as pins** (their green result is not 4a evidence):

- a `hover:` utility compiles inside `@media (hover: hover)` (Tailwind 4.3.3, §1), and
  the touch-tap scenario (US1) — both hold today;
- an unavailable Button stays focused and its `onClick` fires (existing `Button.test.tsx`
  case 2, ADR-0151);
- the triad and dark ground keep their rendered values (`DesignTokenLayerTests`, unchanged).

**Three existing assertions encode the old dimming and must be rewritten in 4a, by the
test-writer, observed red** — not edited by the engineer to pass: `Button.test.tsx` cases
1 and 4 (`aria-disabled:opacity-50`, `disabled:opacity-50`), and one line each in
`LayoutEditorDialogSaveGate.test.tsx:227` and `OverlayEditorDialogSaveGate.test.tsx:247`
(`toHaveClass('aria-disabled:opacity-50', …)`). The ADR-0151 half of each assertion
(`aria-disabled:cursor-progress`, focus retention) is kept verbatim.

Every other scenario above must be observed red on unmodified `develop`.

## 7. Independent end-to-end test procedure

1. `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~InteractionState|FullyQualifiedName~DesignTokenLayer|FullyQualifiedName~SharedUiTokenUsage"`: green.
2. `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/ui/primitives` and
   `pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/styles/tokens.build.test.ts src/features`: green; same build test for kiosk-web.
3. Boot the stack; run `e2e/interaction-states.spec.ts` (Playwright, `chromium` project):
   for each variant, `getComputedStyle(button).backgroundColor` at rest, after
   `hover()`, and between `mouse.down()` and `mouse.up()` — three distinct values;
   Tab-focus → `outlineColor` equals a probe element painted `--color-focus-ring`,
   `outlineStyle` `solid`, `outlineWidth` `2px`; a `hasTouch`+`isMobile` context: tap,
   release, fill equals rest; `emulateMedia({ forcedColors: 'active' })`: outline style
   still `solid`.
4. Screenshot Cameras list, Register dialog and Camera detail before and after; the
   expected differences are exactly plan §8.

## 8. Success criteria

- **SC-001** No `opacity-*` utility sits behind an interaction-state variant anywhere in
  `apps/shared/src/ui` (architecture test).
- **SC-002** Rest, hover and pressed are three distinct computed fills for all four
  variants; danger's hover is lighter and its pressed darker than rest (e2e).
- **SC-003** Every focus indicator in `apps/**/src` is an outline in `--color-focus-ring`;
  no triad colour and no `focus-visible:ring-*` remains (architecture test + e2e).
- **SC-004** Every measured pair in plan §2.3 meets its threshold in all three themes.
- **SC-005** No file imported by `apps/kiosk-web` changes except `tokens.css`'s additions;
  render-leg CI check green, figure cited in the verification note.
