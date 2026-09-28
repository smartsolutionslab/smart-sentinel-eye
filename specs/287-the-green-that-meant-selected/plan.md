# Implementation Plan: The green that meant "selected"

**Spec:** `spec.md` (287) · **Issue:** #2623 · **Lane:** autonomous (ADR-0144)
**Branch:** `enhancement/2623-raw-button-to-button-primitive`

## 1. Constitution and ADR check

| Gate | Result |
|---|---|
| ADR-0146 items 4–5 | Applied: the triad leaves every console affordance and selection in scope; status uses untouched (spec FR-004). |
| ADR-0077 / ADR-0078 / ADR-0148 | Uses the existing `Button` and existing tokens (`accent`, `accent-subtle`). Nothing added. |
| ADR-0151 | No site in scope disables; no `disabled`/`unavailable` introduced. |
| §IV latency | N/A — management-web only (spec §6, SC-003). |
| §Testing / ADR-0139 | Split colour (§5): characterisation for handlers and names, red for the colour and Button's state matrix. |
| ADR-0087 | Three commits, each builds and type-checks on its own (§6). |
| Bounded contexts / backend | None. Frontend-only; no contracts, no messaging, no persistence. |
| New ADR | None for this slice. D1(b) (Button's `transition-colors` vs ADR-0146) may need one — follow-up, not the lane's to write. |

## 2. Layers touched

`apps/management-web` only. The shared primitive is **consumed, not changed**.

| File | Change | Story |
|---|---|---|
| `src/App.tsx` | 3 raw `<button>` → `<Button>`: session-expired *Sign in* `primary`; sign-in-failed *Try again* `secondary`; first-visit *Sign in* `primary`. Import `Button`. Drop the `className`s. | US1 |
| `src/app/ShellLayout.tsx` | `CrashPanel` *Try again* → `<Button>` (`primary`). `NavItem` active string: `bg-accent-active/10 … text-accent-active` → `bg-accent-subtle … text-accent`. | US1 + US2 (one file, one owner) |
| `src/features/layouts/LayoutsPage.tsx` | selected chip: `border-accent-active bg-accent-active/10 text-accent-active` → `border-accent bg-accent-subtle text-accent` | US2 |
| `src/features/overlays/OverlaysPage.tsx` | same | US2 |
| `src/features/systemVariables/SystemVariablesPage.tsx` | same | US2 |
| `src/features/layouts/GridDesigner.tsx` | preset `<label>` active string, same swap; the focus recipe and `cursor-pointer` suffix unchanged | US2 |

Nothing else in these files moves: the *Reload* links in the three pages stay raw (D2), the filter chips stay raw `<button>`s (D2), `WallForm`/`WallsPage` untouched.

**Class hygiene.** `Button` merges with `clsx`; a call-site class that sets a property the variant already sets (`px-*`, `py-*`, `bg-*`, `text-*`) races on stylesheet order. So the four US1 sites pass **no** `className`. If a layout wrapper needs spacing, it goes on the parent.

## 3. Entities / invariants

None in the domain sense. The UI invariant this slice establishes, per surface = console: *a triad token on an interactive or selected element means status, never affordance.* Recorded, not yet enforced app-wide (spec §8).

## 4. Contrast

`text-accent` on `bg-accent-subtle` is a new pair on these surfaces. It must hold ≥ 4.5:1 in `dark`, `light` and `high-contrast` (the three blocks in `tokens.css`, each of which re-derives `--color-accent-subtle`). Add **fact 7** to `tests/Architecture.Tests/InteractionStateTests.cs` reusing fact 6's resolver (`color-mix(in oklch …)` support already exists): resolve `--color-accent` against `--color-accent-subtle` per theme and assert ≥ 4.5.

Colour: **PIN**, expected green on develop (the tokens exist today). If it is red in any theme, **stop and block** — choosing a different selection token is a design decision, not the lane's. No new test class, so no shard-filter entry is needed.

## 5. Tests — the two colours, per ADR-0139 and the #2488 split

### 5a. Characterisation (captured green on develop **before** any source change, re-run **unmodified** after)

| Covers | Test |
|---|---|
| session-expired *Sign in* click → `signinRedirect` | `apps/management-web/src/App.test.tsx` (`:200-201`) |
| nav links, `aria-current` | `apps/management-web/src/app/ShellLayout.test.tsx` (`:63`, `:115`) |
| filter chips change the list | `LayoutsPage.test.tsx`, `OverlaysPage.test.tsx`, `SystemVariablesPage.test.tsx` |
| preset selection, keyboard, retained option | `GridDesignerKeyboard.test.tsx`, `GridDesignerRetainedOption.test.tsx`, `GridDesignerSpans.test.tsx` |
| Healthy badge keeps green (spec conflict scenario) | `StreamHealthBadge.test.tsx:21` |
| real sign-in in a browser | `e2e/management-identity.spec.ts`, `e2e/layouts.spec.ts` |
| crash panel retries | **no test exists** → write `apps/management-web/src/app/ShellLayoutCrashPanel.test.tsx` first: a child route whose element throws once; assert the panel, click *Try again*, assert the child re-rendered. Observed **green on develop**. (A refactor with no covering test is a rewrite.) |

An assertion in any of these that must be edited to pass is evidence behaviour moved: **block, don't adjust**.

### 5b. Red (new behaviour — must be observed failing on develop, failure quoted in the PR)

Extend `e2e/interaction-states.spec.ts` with a new `test.describe('Console affordance and selection use the accent (spec 287)')`, reusing its `probeToken` helper and `signInAsOperator`:

1. **Signed-out *Sign in***: fresh context, no session → button `backgroundColor` equals the `--color-accent` probe and ≠ `--color-accent-active` probe; `hover()` changes it; keyboard focus gives `outlineColor` = `--color-focus-ring` probe. Red: today it is the triad green, no hover, no outline recipe.
2. **Active nav link**: signed in, on `/rules` → `color` equals the `--color-accent` probe. Red.
3. **Selected filter chip**: `/layouts`, click *Archived* → its `borderColor` equals the `--color-accent` probe; an unselected chip's does not. Red.
4. **Selected grid preset**: open *New layout*; the checked preset `<label>`'s `borderColor` equals the `--color-accent` probe. Red.

Plus, in `ShellLayoutCrashPanel.test.tsx`, one new case (jsdom has no computed colours, so this checks the Button contract, which is the subject here): *Try again* carries `hover:bg-accent-hover` (only `Button`'s primary variant emits it) and no class matching `/accent-active/`. Red.

The sign-in-failed *Try again* (App.tsx:60) needs `auth.error` set, which e2e cannot provoke cheaply. A **new** file `App.affordance.test.tsx` with its own controllable `react-oidc-context` mock (the pattern at `App.test.tsx:33`, but returning a mutable state) covers all three `App.tsx` sites: signed-out and session-expired *Sign in* carry `hover:bg-accent-hover`; sign-in-failed *Try again* carries `border-border-strong` (secondary's rest border); none carries a class matching `/accent-active/`. Red. New file so `App.test.tsx` stays unmodified (5a).

Waits by condition, never timeout (ADR-0150). Colours compared token-to-probe, never hard-coded `rgb()`.

## 6. Commit sequence (each builds on its own, ADR-0087)

1. `test(management-web): characterise the crash panel's retry` — `ShellLayoutCrashPanel.test.tsx` characterisation case only; green.
2. `test(console): pin the accent as the console's affordance and selection colour` — §5b cases, §4 fact 7; typecheck green, §5b red, fact 7 green (pin).
3. `fix(management-web): draw sign-in and crash actions with the shared Button` — US1 (`App.tsx`, `ShellLayout.tsx` CrashPanel).
4. `fix(management-web): draw selection in the accent, not the status triad` — US2 (`ShellLayout.tsx` NavItem, three pages, `GridDesigner.tsx`).

Commits 3 and 4 both touch `ShellLayout.tsx` — different functions, sequential, no conflict.

## 7. One PR, not several

One PR. After carving out D1/D2 the slice is six source files in one app and ≈ 30 changed lines; splitting by site would produce PRs too small to review meaningfully and would serialise four e2e runs on the same `interaction-states.spec.ts`. The split that matters has already been made: **the kiosk is separate**, because it touches the wall's render tree (a §IV citation), carries open design questions, and targets a different device class.

## 8. Verification (Phase 5)

Run spec §3's end-to-end procedure. Screenshot, dark theme: signed-out console, a selected chip, the nav, a preset. Set `data-theme='light'` and `'high-contrast'` once each. Confirm SC-002 by the grep and SC-003 by `git diff --name-only`.

## 9. Risks

- **R1 — the kiosk half left open.** Mitigation: file D1 and D2 as issues without `agent:ready` before the PR merges, and reference them in the PR body; recommend the PR uses `Refs #2623` rather than `Closes`, *or* close #2623 only once both follow-ups exist (orchestrator's call).
- **R2 — `e2e/interaction-states.spec.ts` is shared with spec 268's tests.** New `describe` block appended; no existing test edited.
- **R3 — the signed-out e2e context.** `signInAsOperator` must not run for scenario 1; use a fresh `browser.newContext()` like the file's touch scenario does.

## 10. Follow-ups to file (text for the orchestrator)

- **D1** — *The kiosk's buttons need a size and a motion decision before they can move to `<Button>`*. Sites: kiosk rows 8–16, `WallForm` 24–25. Questions: a `size` API (touch target ≥ current 48 px?); `Button`'s `transition-colors` vs ADR-0146's kiosk and console motion clauses; whether picker cards are Buttons; §IV render-leg citation for `LayoutGrid.tsx`. Needs a human design call; possibly an ADR-0146 amendment. Not `agent:ready`.
- **D2** — *`<Button>` has no link or toggle variant*. Sites: rows 17–23 and the three filter-chip groups (states, `aria-pressed`, possibly one shared chip composite). Includes whether red underlined text inside a fault alert is triad-as-affordance. Not `agent:ready`.
