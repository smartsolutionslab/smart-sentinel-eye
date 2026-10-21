# Spec 425 — The button a finger can find: a touch size for `<Button>`, and hover that does not move

**Issue:** [#2667](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2667) (feature-level;
no per-task issues, per CLAUDE.md Phase 3). Carved out of #2623 / spec 287 (plan §10, item D1).
**Branch:** `fix/2667-button-touch-size` (cut from `origin/develop` `cc9f7779`).
**Lane:** autonomous (ADR-0144), `agent:ready`. **Not security-sensitive** (no endpoint, token, scope or
input path is touched).

**Decision implemented (owner, recorded on #2667, 2026-10-21: "option A + M1", fallback M2).**
(1) `Button` gains `size?: 'md' | 'touch'`, a variant lookup (padding and type per size, no call-site
overrides), and the kiosk's raw buttons migrate to it; the picker cards probably stay raw.
(2) Motion: M1 — set `--duration-state: 0ms` in the kiosk's stylesheet, *after first verifying* that this
counts as no animation under the motion guards (spec 292) and on the rendered page; **if M1 fails, M2**
— delete `transition-colors` (and its `MotionLanguageTests` allowlist entry), hover instant everywhere.
**§1 V3 records that M1 fails the guard, so this spec implements M2**, exactly as the owner's fallback
and spec 292 §4 name it. No ADR is written and no ADR or constitution section is amended.

**ADRs this spec is bound by:** ADR-0146 (one discipline, two surfaces — the wall's prohibitions and
the console's "transform and opacity only"), ADR-0148 (prohibitions expressed as absence, not as a
token with a different value), ADR-0077 / ADR-0078 (the design-system Button, Tailwind tokens),
ADR-0123 + constitution §IV (composite + render leg), ADR-0151 (`unavailable` vs `disabled`,
untouched), ADR-0139 / ADR-0144 (phase-4a colours), ADR-0087 (every commit builds on its own),
ADR-0036 (no speculative generality). **Predecessors:** spec 268 (#2336, built `Button`'s states),
spec 287 (#2623, the inventory and D1), spec 292 (#2334, the motion language and its guard).

---

## 1. The premise, re-verified on `cc9f7779` (2026-10-21)

| # | Issue / brief says | Tree says |
|---|---|---|
| P1 | `Button` is one size, ~38 px | **Holds.** `apps/shared/src/ui/primitives/Button.tsx:95-98`: `px-4 py-2 text-sm` in `base`, plus `border` → 0.5 rem × 2 + 1.25 rem line height + 2 px = **38 px**. No `size` prop. |
| P2 | `clsx`, not `tailwind-merge` | **Holds.** `clsx(base, …, className)`. `tailwind-merge` is not imported anywhere in `Button.tsx`. No consumer passes a size override today: the only `<Button className>` values in `apps/` are `ml-auto` and `aria-disabled:cursor-progress` (×2). |
| P3 | `transition-colors` in the base | **Holds, now with roles** (spec 292): `transition-colors duration-state ease-state` (`Button.tsx:96`). It is the **only** `transition-*` call site in `apps/*/src`, and the only consumer of the `state` role utilities. |
| P4 | 9 raw kiosk sites | **Holds at 9, line numbers drifted.** See §1.1. `WallForm.tsx` Up/Down is a console file, not a kiosk site. |
| P5 | `--duration-state` defined where | `apps/shared/src/ui/tokens/tokens.css:289` (`var(--duration-fast)`, 120 ms), `:root`, shared by both apps. No per-app or per-surface override exists; `tokens.css:309-310` binds Tailwind's default-transition bridge to the same steps. The kiosk imports tokens via `apps/kiosk-web/src/styles/index.css:1`. |
| P6 | Kiosk compiles shared classes | **Holds.** `apps/kiosk-web/tailwind.config.ts` `content` includes `../shared/src/**/*.{ts,tsx}`, so every `Button` utility already compiles into the kiosk's CSS. |

### 1.1 The real site list

| # | Site | Today | Decision |
|---|---|---|---|
| S1 | `apps/kiosk-web/src/App.tsx:44` *Sign in again* | `bg-accent-subtle px-6 py-3 text-accent` (48 px, no border) | **Migrate** — `size="touch" variant="secondary"` |
| S2 | `apps/kiosk-web/src/App.tsx:87` *Sign in* | same | **Migrate** — `size="touch" variant="secondary"` |
| S3 | `apps/kiosk-web/src/features/auth/ReconnectingScreen.tsx:33` *Try now* | `bg-accent-subtle px-4 py-2 text-accent` (40 px) | **Migrate** — `size="touch" variant="secondary"` |
| S4 | `apps/kiosk-web/src/features/cell/LayoutGrid.tsx:213` *Back to picker* (fallback, no tiles) | same as S3 | **Migrate** — `size="touch" variant="secondary"` |
| S5 | `apps/kiosk-web/src/features/cell/LayoutGrid.tsx:658` *Back* (`WallHeader`, `CellPage` only) | `bg-bg-elevated/60 px-3 py-1 text-sm` (28 px, translucent over video) | **Migrate** — `size="touch" variant="ghost"` |
| S6 | `apps/kiosk-web/src/features/picker/PickerPage.tsx:72` *Retry* | same as S3 | **Migrate** — `size="touch" variant="secondary"` |
| S7 | `apps/kiosk-web/src/features/wall/WallPage.tsx:164` *Retry* | same as S3 | **Migrate** — `size="touch" variant="secondary"` |
| S8 | `PickerPage.tsx:94` layout card | full-width, left-aligned card with heading + meta, `p-6` | **Stays raw** — a card, not a button shape; already ≥ 48 px |
| S9 | `PickerPage.tsx:112` wall card | same | **Stays raw** — same reason |
| — | `apps/management-web/src/features/walls/WallForm.tsx:143,152` Up/Down | console, `px-2 py-0.5 text-xs` inline row controls | **Out of scope** — needs a *smaller* size the decided API (`'md' \| 'touch'`) does not have; adding one is speculative (ADR-0036). Recorded in §6. |

**Variant choice (an interpretation within ADR-0146, marked as such).** None of the seven sites has a
`Button` variant equal to today's `bg-accent-subtle text-accent` tint. `primary` (solid accent fill) is
rejected for the kiosk: S1-S4, S6, S7 are fallback screens that can stand unattended for hours, and
ADR-0146 disqualifies "static bright regions larger than a status chip" on the wall. `secondary`
(bordered, unfilled, `text-fg-primary`) keeps the screen dark at rest. S5 sits in a header over live
video, where ADR-0146 also disqualifies translucency; `ghost` (no fill at rest) removes today's
`bg-bg-elevated/60` rather than reproducing it. Hover feedback on the kiosk is moot on touch hardware:
Tailwind 4.3.3 wraps `hover:` in `@media (hover: hover)` (pinned by
`apps/kiosk-web/src/styles/tokens.build.test.ts:218`).

**Kiosk import path.** Sites import `Button` from the deep specifier
`@smart-sentinel-eye/shared/ui/primitives/Button` (`apps/shared/package.json:38`), **never** the
`@smart-sentinel-eye/shared/ui` barrel, which would drag every primitive (Dialog, motion consumers)
into the kiosk reach set of `MotionLanguageTests` fact 1.

### 1.2 Verification of M1 — it fails, by counterfactual (2026-10-21, on `cc9f7779`)

| # | Check | Result |
|---|---|---|
| V1 | Baseline: `dotnet test tests/Architecture.Tests --filter FullyQualifiedName~MotionLanguage` | **9/9 passed.** |
| V2 | Does a 0 ms transition count as "no animation" in CSS? | In the CSS Transitions spec, a combined duration of 0 starts no transition, so the *rendered* page would show none. This is the half of M1 that holds — and it is the half no guard reads. |
| V3 | **M1 planted:** `ReconnectingScreen.tsx` imports `Button` (deep path) and uses it; `apps/kiosk-web/src/styles/index.css` gains `:root { --duration-state: 0ms; }` | **Red, fact 1** (`The_wall_has_no_motion_but_its_signal`): `Button.tsx: Tailwind motion utility 'transition-colors'`, `'duration-state'`, `'ease-state'`. The guard scans the kiosk's reach set for motion utilities in source; a token override does not remove them. Making M1 pass would need a new fact-1 exemption for `Button.tsx` — a new suppression, which the lane may not add (CLAUDE.md, ADR-0144), and which fact 6 calls growing a shrink-only list. |
| V4 | ADR-0148, independently of the guard | "ADR-0146's wall subtractions are **not tokens with different values** — they are tokens the wall has no reason to cite." M1 is precisely a token with a different value. M1 fails on the ADR text as well as on the guard. |
| V5 | **M2 planted:** the same kiosk import, plus `transition-colors duration-state ease-state` deleted from `Button.tsx` | Fact 1 **green**. Fact 6 (`Each_allowlist_entry_still_applies`) **red**: `Button.tsx no longer contains 'transition-colors' — its allowlist entry no longer applies and must be removed`. Removing that entry is a shrink, which is what fact 6 demands. |

All plants were reverted (`git checkout -- apps`); the working tree was clean afterwards.

**Verdict: M2.** Spec 292 §4 already recorded M2 as the fallback under a literal reading of ADR-0146's
console clause ("transform and opacity only"); the owner named it as the fallback. M2 makes `Button`
compliant with ADR-0146 on **both** surfaces, which closes the issue's "sharper problem" rather than
working around it on one. **No ADR is needed**: nothing is decided here that ADR-0146 and spec 292 did
not already settle. (Had neither M1 nor M2 held, the honest answer would have been an ADR-0146
amendment, which the lane may not write.)

## 2. User stories

### US1 (P1): `Button` has a touch size, and its states change without animating

**As** a contributor building a kiosk control, **I want** `<Button size="touch">` to give a ≥ 48 px
target from the type and spacing scales, **so that** I never pass a size through `className` and depend
on stylesheet order; **and as** an operator on either surface, **I want** hover and pressed fills to
change instantly, **so that** no control on the wall animates a paint property (ADR-0146).

Default `size` is `md`, and `md` renders exactly the classes `Button` renders today minus the three
motion utilities — so no console call site changes and no console geometry changes.

```gherkin
Feature: Button sizes and state changes

  Scenario: The touch size uses the touch padding and type (happy)
    Given <Button size="touch">Sign in</Button>
    Then the button carries px-6, py-3 and text-base
    And  it carries none of px-4, py-2, text-sm
    # 0.75rem x 2 + 1.5rem line height + 2px border = 50px at a 16px root

  Scenario: Omitting size is md, and md is today's geometry (characterisation)
    Given <Button>Save</Button>
    Then the button carries px-4, py-2 and text-sm
    And  the same for <Button size="md">Save</Button>

  Scenario: size is consumed, never written to the DOM (bad request)
    Given <Button size="touch">Sign in</Button>
    Then the rendered <button> has no size attribute

  Scenario: A size cannot be overridden by a call-site className (conflict)
    Given ButtonProps
    Then size accepts only 'md' or 'touch'
    And  passing any other value is a TypeScript error   # tsc --noEmit

  Scenario: No state change animates (conflict with ADR-0146)
    Given any Button of any variant and size
    Then its class list contains no transition-*, duration-* or ease-* utility
    And  on the console, the Register camera button's computed transition-duration is 0s

  Scenario: The paint-transition allowlist is empty (honesty)
    Given tests/Architecture.Tests/MotionLanguageTests.cs
    Then PaintTransitionAllowlist has no entry
    And  every MotionLanguageTests fact passes

  # Auth: N/A — no endpoint, token or scope.
```

**Independent test:** `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/ui/primitives/Button.test.tsx`,
`pnpm --filter @smart-sentinel-eye/shared typecheck`, and
`dotnet test tests/Architecture.Tests --filter FullyQualifiedName~MotionLanguage` — all without Docker.
The console computed-style assertion (`e2e/motion.spec.ts`) is CI-only.

### US2 (P2): The kiosk's buttons are the design-system Button at touch size

**As** someone standing at a kiosk display, **I want** every action button to be a ≥ 48 px target that
looks like the product's other buttons and draws the product's focus ring, **so that** I can hit it
with a finger and see where keyboard focus is; **and as** the wall's owner, **I want** no new bright
fill or translucency over video as a result.

```gherkin
Feature: Kiosk buttons use the shared Button

  Scenario: Every kiosk action button is the shared Button at touch size (happy)
    Given apps/kiosk-web/src, non-test files
    Then no raw <button> element appears
    Except the two picker cards in PickerPage.tsx, each on a reasoned allowlist

  Scenario: The first-boot Sign in button is a touch target (happy, real browser)
    Given a kiosk with no session
    Then the "Sign in" button's bounding box is at least 48px tall
    And  hovering it starts no animation (element.getAnimations() is empty)

  Scenario: The wall still has no motion but its signal (conflict)
    Given the kiosk reach set now includes Button.tsx
    Then MotionLanguageTests fact 1 passes with no new exemption

  Scenario: A stale allowlist entry fails (honesty)
    Given a kiosk raw-button allowlist entry
    When its file no longer contains the match
    Then the guard fails and says to remove the entry

  Scenario: Behaviour is unchanged (characterisation)
    Given the existing kiosk tests that click Sign in, Try now, Retry, Back to picker and Back
    Then each still finds its control by role and name, and passes unmodified

  # Bad request / auth: N/A — the buttons call the same handlers as before.
```

**Independent test:** the kiosk raw-button guard (`dotnet test tests/Architecture.Tests --filter
FullyQualifiedName~KioskButton`), `pnpm --filter @smart-sentinel-eye/kiosk-web test`, `typecheck`, `lint`;
the bounding-box scenario is CI-only (Playwright needs the full stack).

## 3. Locked choices (applied, not decided)

- React + TypeScript + Vite, the two-app split (ADR-0074); Radix Slot + custom design system (ADR-0077);
  Tailwind with tokens as CSS custom properties (ADR-0078, ADR-0148).
- Sizes are a lookup in `Button.tsx` keyed by a closed union, exactly like `restFill` / `interactive`
  keyed by `ButtonVariant`. No `tailwind-merge` (it stays unused; a lookup has no conflicts to merge).
- Values come from the existing scales only (ADR-0146 item 1): `px-6`/`py-3` (`--space-6`/`--space-3`),
  `text-base` (`--text-base` with its own leading). No token is added.
- Motion: none on `Button` (M2). The `--duration-state` / `--ease-state` tokens and utilities stay: fact 7
  requires them and binds Tailwind's default-transition bridge to them (spec 292 plan §2).
- Source-scan guards live in `tests/Architecture.Tests` and use `RepositorySource.RelativePath`
  (forward slashes on every OS), as `MotionLanguageTests` does.

## 4. Latency budget (constitution §IV)

- **Leg: overlay composite + render (≤ 50 ms).** Recorded figure: ADR-0123, p50 **54.2 ms** on a real
  wall (p95 79.2, max 164.6; #1891) — already over budget. CI baseline used by spec 297: mean p50
  57.66 ms over nine `develop` runs. `render-leg-gate` (`ci.yml`) is report-only and writes its figure to
  the job summary, not the log; the most recent green `develop` run is `39493830176` (`7909dd20`).
- **Impact: none added, a small removal.** M2 removes a 120 ms paint transition from every `Button`; no
  `Button` is rendered on the measured wall today, and after US2 the wall page (`WallPage` →
  `LayoutGrid` with `headerTitle`) still renders none: S5 renders only on `CellPage` (`onBack` is
  `undefined` when `headerTitle` is set), and S4 only in the no-tiles fallback. S5 also drops a
  translucent `bg-bg-elevated/60` fill over video. Nothing new animates, blurs or casts a shadow.
- **Cited for review because `LayoutGrid.tsx` is on the wall render tree** (CLAUDE.md). Phase 5 reads
  the PR run's `render-leg-gate` summary and states it beside `39493830176`'s.

## 5. Phase-4a colour: **red** (behaviour-changing), with characterisation pins

Behaviour changes: a new prop, a new size, console and kiosk hover become instant, seven kiosk buttons
change look and size. The red tests (plan §5) are shown red on the branch before any source change;
pins are declared green in advance and are not 4a evidence.

## 6. Scope

**In:** `Button.tsx` size lookup and motion removal; `MotionLanguageTests` allowlist shrink; the seven
kiosk sites; a kiosk raw-button guard; test updates listed in plan §5.

**Out, and where:**

| What | Where |
|---|---|
| `WallForm.tsx` Up/Down (console, needs a size smaller than `md`) | Follow-up issue text in plan §9; not `agent:ready` (a third size is a design call). |
| Picker cards as a `Card`/`ButtonCard` primitive | Not needed; they stay raw with a reason. |
| A compositor-only rewrite of the highlight pulse | Already deferred by spec 292 §6. |
| Removing the now consumer-less `duration-state`/`ease-state` utilities | No — fact 7 keeps the tokens as the bridge's source of truth. |

## 7. End-to-end procedure (Phase 5)

1. Local, no Docker: the four commands in US1/US2's independent tests, plus `pnpm -r typecheck`,
   `pnpm lint`, `pnpm format:check` (eslint clean is not prettier clean).
2. CI: `e2e/motion.spec.ts` (console Button `transition-duration: 0s`), `e2e/interaction-states.spec.ts`
   (unmodified, still green — its settle helper waits for `getAnimations()` to be empty, which is now
   immediately true), the new kiosk sign-in size check, and the `render-leg-gate` summary.
3. If a stack is available: open the kiosk signed out, measure the Sign in button (≥ 48 px), hover it and
   run `document.getAnimations().length` → `0`; open a `CellPage`, check the Back button over video has no
   fill at rest.

## 8. Success criteria

- SC-1: `<Button size="touch">` renders `px-6 py-3 text-base`; default renders `px-4 py-2 text-sm`.
- SC-2: no `transition-*`/`duration-*`/`ease-*` in `Button.tsx`; `PaintTransitionAllowlist` empty; all
  `MotionLanguageTests` facts green with no new exemption.
- SC-3: zero raw `<button>` in `apps/kiosk-web/src` outside the two allowlisted picker cards.
- SC-4: no console call site file changes; `Button.test.tsx`'s existing cases pass unmodified.
- SC-5: `render-leg-gate` reports on the PR, and the figure is stated in the PR body.
