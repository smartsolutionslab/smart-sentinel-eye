# Plan 297: The chip each page painted

**Spec**: [spec.md](spec.md) · **Issue**: #2635 · **Phase**: 2 (Plan) · **Lane**: autonomous

## Constitution / ADR check

| Rule | How this plan meets it |
|---|---|
| ADR-0148 — two layers; components cite semantic roles only | Six new **primitive** stops (`--<hue>-<step>` OKLCH literals, `:root` only); three semantic `-subtle` roles cite them by `var()`; `light` remaps the semantic roles only. `DesignTokenLayerTests` unchanged and green. §2. |
| ADR-0148 decision 2 — derived variants use `color-mix(in oklch)` | Not applied to status tints: measured not to render the hue (spec §1). Recorded as a finding for a human (spec §4.1); no guard widened. |
| ADR-0146 — wall prohibitions | Opaque fill (no translucency over video), no border/shadow/blur/motion in `Badge`; the chip's box bounded (FR-012). `MotionLanguageTests` fact 1 covers `Badge.tsx` via the kiosk reach closure. |
| ADR-0146 item 4 — triad is status | Badge tones are the triad **as status**; `Badge` lives in `composites/`, not `primitives/` (spec §4.3). |
| ADR-0077 / 0078 | `asChild` through `@radix-ui/react-slot` (already a dependency, `Button.tsx:1`). No new package. |
| §IV / ADR-0123 | Leg cited (spec §5); measured by V3 (§8) with its honest limit stated. |
| ADR-0139 / 0144 | Red, with a characterisation set observed green first (spec §6). |
| ADR-0150 | Every async assertion `findBy*`/`waitFor`/`expect(...).toBeVisible()`; no sleeps. |
| ADR-0036 | Two sizes and four tones, each with a consumer. No icon, dot or dismiss. |
| ADR-0087 | Every commit builds; red evidence is 4a's verbatim output, not a red commit (§7). |
| §III bounded contexts | N/A — frontend and architecture tests only; no `Shared.Contracts`, no messaging, no endpoint, no migration. |

## Bounded context and layers

**None.** Frontend-only across three workspaces plus source-scanning architecture tests:

- `apps/shared/src/ui/tokens/` — primitive stops, semantic roles, Tailwind mapping.
- `apps/shared/src/ui/composites/Badge.tsx` — the component (+ `package.json` `exports`).
- `apps/management-web/src/features/{cameras,rules}/` — US1, US3.
- `apps/kiosk-web/src/features/{revocation,cell}/` — US2 (**the wall**).
- `tests/Architecture.Tests/` — C#, test code only.
- `e2e/kiosk-live-updates.spec.ts` — one real-browser check.

The "invariants" are the token facts (§5.1) and the component contract (§3).

## 1. File ownership (drives `[P]` — ADR-0109)

| Area | Files | Story |
|---|---|---|
| Foundation (serial, blocks all) | `apps/shared/src/ui/tokens/tokens.css`, `apps/shared/src/ui/tokens/tailwindTheme.ts`, `apps/shared/src/ui/composites/Badge.tsx`, `Badge.test.tsx`, `apps/shared/package.json` (one `exports` entry), `tests/Architecture.Tests/StatusTintTests.cs` (new) | F |
| Stream health | `apps/management-web/src/features/cameras/StreamHealthBadge.tsx`, `StreamHealthBadge.test.tsx` | US1 |
| Wall chips | `apps/kiosk-web/src/features/revocation/LiveUpdatesBadge.tsx`, `apps/kiosk-web/src/features/cell/TileAlignmentBadge.tsx`, `TileAlignmentBadge.test.tsx` (new), `apps/kiosk-web/src/features/cell/LayoutGrid.tsx`, `apps/kiosk-web/src/features/cell/CellPage.test.tsx`, `e2e/kiosk-live-updates.spec.ts`, `tests/Architecture.Tests/WallStatusChipTests.cs` (new) | US2 |
| Console labels | `apps/management-web/src/features/rules/RulesPage.tsx`, `RulesPage.test.tsx`, `apps/management-web/src/features/cameras/CameraDetailPage.tsx`, `CameraDetailPage.test.tsx` | US3 |

US1, US2 and US3 own disjoint files and fan out after the foundation. `CameraDetailPage.tsx`
(US3) and `StreamHealthBadge.tsx` (US1) share a folder, not a file.

**Contention with parallel branches.** `tokens.css` and `tailwindTheme.ts` are contention
files (every token spec touches them); `LayoutGrid.tsx` is touched by any wall spec (spec 296
on `feat/2618-automation-switches-a-wall` — check its diff before phase 4). Whoever lands
second rebases a small hunk.

## 2. Tokens

### 2.1 Primitive stops (`:root`, primitive block, beside the triad)

Values chosen so the label clears 4.5:1 with room in the dark theme and the chip reads as a
distinct surface on both grounds; **the hue is the triad's own, to the hundredth**.

```css
/* Triad tint stops (spec 297). A status chip's fill is a tint AT THE TRIAD'S HUE.
   Not a color-mix: color-mix(in oklch, …) interpolates hue by percentage, so a
   10 % mix into a ground with a real hue renders the ground's hue (fault →
   rgb(20,24,37), slate), observed in Chromium 2026-09-30. */
--green-900: oklch(25% 0.04 148.34);
--red-900:   oklch(25% 0.04 24.66);
--amber-900: oklch(25% 0.04 68.43);
--green-50:  oklch(95% 0.03 148.34);
--red-50:    oklch(95% 0.03 24.66);
--amber-50:  oklch(95% 0.03 68.43);
```

The comment at `tokens.css:70` (*"Triad stops, one each, no ramp."*) becomes *"Triad stops:
the signal (500) and a tint pair (900 dark, 50 light) for status chips (spec 297)."*

Rendered in Chromium 1243 (probe, 2026-09-30): dark `rgb(19,39,23)` / `rgb(50,26,24)` /
`rgb(46,30,11)`; light `rgb(226,244,228)` / `rgb(255,231,229)` / `rgb(252,235,218)`. OKLab
hue within 1.3° of the triad in all six.

### 2.2 Semantic roles

`:root` (dark default; also serves `high-contrast`, whose ground is black — the dark stops
read on it and need no redeclaration):

```css
--color-accent-active-subtle:  var(--green-900);
--color-accent-warning-subtle: var(--amber-900);
--color-accent-fault-subtle:   var(--red-900);   /* was color-mix(in oklch, fault 10%, bg-base) — slate in practice */
```

`--color-accent-fault-subtle`'s existing comment block (`tokens.css:128-135`) is rewritten:
the RetryBanner reasoning stays; the "mixes toward --color-bg-base so it still reads" claim is
replaced by the hue finding. `--color-accent-fault-border` is **unchanged** (console-only,
mixes toward `transparent`, whose hue is powerless — it keeps red).

`[data-theme='light']`:

```css
--color-accent-active-subtle:  var(--green-50);
--color-accent-warning-subtle: var(--amber-50);
--color-accent-fault-subtle:   var(--red-50);
```

Resolved contrast, triad label on its tint (the FR-008 fact recomputes these; they are the
architect's figures, not evidence):

| Theme | active | warning | fault |
|---|---|---|---|
| dark | 7.06 | 8.53 | 5.08 |
| high-contrast (same stops) | 7.06 | 8.53 | 5.08 |
| light | 1.95 | 1.62 | 2.71 — **excluded, #2695** |

Neutral tone `--color-fg-muted` on `--color-bg-raised`: dark 4.64 (tight but ≥ 4.5).

### 2.3 `tailwindTheme.ts`

Under `colors.accent`, beside `'fault-subtle'`: `'active-subtle': 'var(--color-accent-active-subtle)'`,
`'warning-subtle': 'var(--color-accent-warning-subtle)'`. Nothing else.

## 3. `Badge` — component contract

`apps/shared/src/ui/composites/Badge.tsx`; `exports`: `"./ui/composites/Badge": "./src/ui/composites/Badge.tsx"`.

```ts
export type BadgeTone = 'active' | 'warning' | 'fault' | 'neutral';
export type BadgeSize = 'sm' | 'md';

export interface BadgeProps extends ComponentPropsWithRef<'span'> {
  tone: BadgeTone;          // required: a chip with no stated tone is a bug at the call site
  size?: BadgeSize;         // default 'sm' (console); 'md' is the wall's
  asChild?: boolean;        // merge onto the single child (Slot) — a button, or a positioned div
}
```

Mirrors `Button`: `ComponentPropsWithRef`, `asChild` → `Slot`, `clsx(BASE, SIZE[size], TONE[tone], className)`.
`className` is merged **after** the Badge's classes and is for **placement only** at every
call site in this spec (`fixed …`, `absolute …`, `focus-visible:outline-…`); plan §4 lists
each. `Badge` does not set `role` — status semantics are the caller's (the wall chips are
`role="status"`, the console pills are not live regions).

| Part | Classes |
|---|---|
| `BASE` | `inline-flex items-center whitespace-nowrap rounded-md font-medium` |
| `SIZE.sm` | `px-2 py-0.5 text-xs` |
| `SIZE.md` | `px-3 py-1 text-xs` |
| `TONE.active` | `bg-accent-active-subtle text-accent-active` |
| `TONE.warning` | `bg-accent-warning-subtle text-accent-warning` |
| `TONE.fault` | `bg-accent-fault-subtle text-accent-fault` |
| `TONE.neutral` | `bg-bg-raised text-fg-muted` |

Deliberately absent (FR-004): border, alpha modifier, shadow, blur, transition, animation,
opacity, ring. `md` = `text-xs px-3 py-1` because two of the three wall chips are exactly that
today; the third (*Overlay unavailable*, `text-sm px-4`) is brought to it — declared.
`font-medium` is new on four of seven sites (it matches `StateBadge` and *Retired*) — declared.

## 4. Call-site changes

| Site | Before | After |
|---|---|---|
| `StreamHealthBadge.tsx` | `TONES` map + `PILL` string; `<span>` for unknown; `<button>` trigger with `PILL`+tone | `const TONE: Record<StreamState, BadgeTone> = { Healthy: 'active', Degraded: 'warning', Offline: 'fault', Provisioning: 'neutral' }`; lookup `TONE[stream.state] ?? 'neutral'`. Unknown: `<Badge tone="neutral" aria-label="Stream state unknown">Unknown</Badge>`. Trigger: `<Badge tone={…} asChild className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring"><button type="button">{stream.state}</button></Badge>`, passed as the `Popover`'s `trigger` (Radix `Trigger asChild` → Slot → Slot → `<button>`; one element). `clsx` import may go. |
| `LiveUpdatesBadge.tsx` | `<div role="status" data-testid className="fixed bottom-3 right-3 z-20 rounded-md border border-accent-warning/40 bg-accent-warning/15 px-3 py-1 text-xs text-accent-warning">` | `<Badge tone="warning" size="md" asChild className="fixed bottom-3 right-3 z-20"><div role="status" data-testid="live-updates-degraded">Live updates degraded</div></Badge>` |
| `TileAlignmentBadge.tsx` | `<div role="status" data-testid data-camera className="absolute bottom-2 left-1/2 z-10 -translate-x-1/2 rounded-md bg-accent-warning/30 px-3 py-1 text-xs text-accent-warning">` | same shape as above, `className="absolute bottom-2 left-1/2 z-10 -translate-x-1/2"`, keeps `data-camera={camera}`. |
| `LayoutGrid.tsx:443-450` | inline `<div role="status" className="absolute left-1/2 top-2 z-10 -translate-x-1/2 rounded-md bg-accent-warning/30 px-4 py-1 text-sm text-accent-warning">Overlay unavailable</div>` | `<Badge tone="warning" size="md" asChild className="absolute left-1/2 top-2 z-10 -translate-x-1/2"><div role="status">Overlay unavailable</div></Badge>`. **Line 210's button is not touched** (spec §3.2). |
| `RulesPage.tsx:224-228` | `<span className={\`text-xs font-medium ${tone}\`}>` | `const RULE_STATE_TONE: Record<RuleState, BadgeTone> = { Active: 'active', Draft: 'warning', Archived: 'neutral' }` → `<Badge tone={RULE_STATE_TONE[state]}>{state}</Badge>`. `RuleState` is exactly these three (`apps/shared/src/api/rules.api.ts:7`); the exhaustive `Record` replaces the old "anything else is warning" default. |
| `CameraDetailPage.tsx:98` | `<span className="rounded-md bg-fg-muted/15 px-2 py-1 text-xs font-medium text-fg-muted">Retired</span>` | `<Badge tone="neutral">Retired</Badge>` (`py-1` → `py-0.5` — declared) |

Imports: `import { Badge, type BadgeTone } from '@smart-sentinel-eye/shared/ui/composites/Badge';`
in both apps. kiosk-web's Tailwind `content` already scans `../shared/src/**` (`tailwind.config.ts:8`),
so the Badge's classes compile into the wall's CSS with no config change.

## 5. Tests

### 5.1 `tests/Architecture.Tests/StatusTintTests.cs` (F, new)

Reuses `DesignTokenLayerTests`' declaration parser shape and `OklchColorResolver`; if the
parser is private, **copy the minimum** rather than refactor a guard (a lift is a separate,
characterised change — spec 268 did its `TypeScriptSource` lift as its own task).

- **`Each_triad_tint_is_a_literal_at_its_triad_hue`** — for each hue, in `:root` and in `light`:
  the `-subtle` role resolves **through `var()` only** to an `oklch(L C H)` literal, and `H`
  equals the triad role's resolved `H` (tolerance 0.01). A `color-mix` value fails with *"a
  mix renders the ground's hue in the browser (spec 297 §1); cite a tint stop"*. Red on develop:
  `-active-subtle`/`-warning-subtle` absent; `-fault-subtle` is a mix.
- **`No_theme_redeclares_a_triad_role`** — `--color-accent-{active,warning,fault}` appear only
  in `:root`. Declared green pin.
- **`A_triad_label_is_legible_on_its_tint`** — ≥ 4.5:1 per hue in `dark` and `high-contrast`
  (reuse `InteractionStateTests`' `ContrastRatio`/`ToSrgb` pattern). Themes excluded by a
  `(Theme, Reason)` list with one entry: `("light", "#2695 — a triad hue as text on a near-white tint is below 4.5:1; needs per-theme triad text roles")`.
  Red on develop (roles absent).
- **`Each_contrast_exclusion_still_fails`** — honesty: for each excluded theme at least one hue
  is still below 4.5:1; message says remove the exclusion. Green once the roles exist.
- **`The_neutral_label_is_legible_on_its_fill`** — `--color-fg-muted` on `--color-bg-raised`
  ≥ 4.5:1 in all three themes. Declared green pin.

Counterfactual (PR body, not the suite): set `--color-accent-warning-subtle:
color-mix(in oklch, var(--color-accent-warning) 10%, var(--color-bg-base))` in a scratch copy,
run, quote the failure, revert.

### 5.2 `tests/Architecture.Tests/WallStatusChipTests.cs` (US2, new)

Shape copied from `SharedUiTokenUsageTests` (`TypeScriptSource.StringLiteralContent`,
`RepositorySource`), allowlist shape from `MotionLanguageTests` (`(RelativePath, Match, Reason)`).

- **`No_translucent_triad_colour_on_the_wall`** — every non-test `.ts`/`.tsx` under
  `apps/kiosk-web/src`, regex `(?<![\w-])(?:bg|border|text|ring|outline|fill|stroke)-accent-(?:active|warning|fault)/\d+`,
  each `(file, match)` outside the allowlist fails. `apps/shared/src/ui` is already held by
  `SharedUiTokenUsageTests.No_call_site_alpha_on_a_semantic_colour`; the class remarks say so,
  so together they cover the kiosk's whole reach for UI code.
  Allowlist (4 entries, each reason `"#2694 — a full-screen action button, not over video; the triad as affordance"`):
  `WallPage.tsx`, `ReconnectingScreen.tsx`, `PickerPage.tsx`, `LayoutGrid.tsx` — each with match `bg-accent-active/20`.
  **Red on develop**, naming exactly: `LiveUpdatesBadge.tsx` (`border-accent-warning/40`,
  `bg-accent-warning/15`), `TileAlignmentBadge.tsx` (`bg-accent-warning/30`), `LayoutGrid.tsx`
  (`bg-accent-warning/30`).
- **`Each_wall_allowlist_entry_still_matches`** — honesty; each entry names an issue (`#\d+`).
- Paths slash-normalised (repo memory: backslash literals are green on Windows, red on Linux).

### 5.3 `apps/shared/src/ui/composites/Badge.test.tsx` (F)

`// @vitest-environment jsdom`, against a **signature-only stub** (§6): per tone, the rendered
element's `className` contains the tone's two classes; `size="md"` gives `px-3 py-1`, default
`px-2 py-0.5`; no class matches `/\/\d+$|^border|shadow|backdrop|transition|animate|opacity|ring/`
(FR-004); `asChild` over `<div role="status" data-testid="x">` renders **one** element — the
`div` — with the badge classes, its role and test id (FR-002); `asChild` over `<button>` keeps
it a `button`; `className` is appended.

### 5.4 Consumer tests

- **`StreamHealthBadge.test.tsx`** (US1) — rewrite the `it.each` tone-class case and the Unknown
  case to the new classes (`bg-accent-active-subtle`, …, `bg-bg-raised`), and add: no pill's
  class list contains `/` or `border`; an unrecognised state (cast) renders neutral. The three
  popover cases are **not edited** (characterisation).
- **`TileAlignmentBadge.test.tsx`** (US2, new) — *characterisation first*, run on the untouched
  tree: `role="status"`, `data-testid="tile-out-of-alignment"`, `data-camera`, the text. Then the
  red case: its classes are Badge warning `md`, no `/`.
- **`CellPage.test.tsx`** (US2) — one new case near `:2270`: the *Overlay unavailable* status
  inside the flagged tile carries `bg-accent-warning-subtle` and no `/NN` class. The
  `live-updates-degraded` cases (`:522-532`, `:1220`) and `unavailableBadgeIn` cases unedited.
  `LiveUpdatesBadge`'s own class case also lives here (it has no test file; add one only if the
  test-writer prefers — either is disjoint).
- **`RulesPage.test.tsx`** (US3) — characterisation first: the State cell's text equals the
  rule's state (green today). Red: the cell's pill has the tone class per state.
- **`CameraDetailPage.test.tsx`** (US3) — characterisation first: "Retired" present iff retired.
  Red: it is a neutral Badge (`bg-bg-raised`, no `/`). The file exists; no case names
  "Retired" today.

### 5.5 e2e — `e2e/kiosk-live-updates.spec.ts` (US2)

After the existing `toBeVisible()` on `live-updates-degraded`, add (local function, not
`e2e/support/*` — one caller, ADR-0162):

- **opaque**: `locator.evaluate` → `getComputedStyle(el).backgroundColor` painted into a 1×1
  canvas, `getImageData` alpha **=== 255**. Red today (`bg-accent-warning/15` → alpha ≈ 38).
- **chip-sized**: `boundingBox().height <= 24`.
- **tints are their hue** (FR-011): in the same page, for each of the three `-subtle` roles, a
  throwaway element styled `background: var(--color-accent-<hue>-subtle)` read back through the
  canvas, converted to OKLab, hue angle within 20° of the matching triad role read the same way.
  Red today for all three (slate). This is the only check that watches the browser, not a
  resolver.

The existing unroute + `toBeHidden` recovery assertion stays last and unedited.

## 6. Red that lands on content

The foundation's red commit is avoided (ADR-0087; §7); 4a runs on the working tree:

- `Badge.tsx` stub: the §3 interface exported; body `return <span {...rest}>{children}</span>;`
  (no classes, no Slot). `tsc --noEmit` green; `Badge.test.tsx` red on class content; `asChild`
  cases red on element shape.
- Token facts red on the declarations (§5.1). Kiosk guard red naming three files (§5.2).
- Consumer red cases red on today's classes.

**Required 4a outcome** (quote verbatim): every §5.1–§5.5 red case red; every characterisation
case green **before any source edit**; the declared pins green. Arriving green when expected
red → stop and report.

## 7. Commit shape (ADR-0030; rebase-merge, ADR-0087 — each builds and passes on its own)

1. `docs(297): specify, plan and task the status Badge` (this phase).
2. `feat(ui): tint the triad at its own hue and add a status Badge` — F: tokens, theme, Badge,
   `exports`, `StatusTintTests.cs`, `Badge.test.tsx`. (`RetryBanner` visibly changes here.)
3. `feat(cameras): render stream health through the status Badge` — US1.
4. `fix(kiosk): make the wall's status chips opaque Badges` — US2, incl. `WallStatusChipTests.cs`
   and the e2e edit. `fix`, because it removes an ADR-0146 violation.
5. `feat(rules,cameras): render rule state and retirement as Badges` — US3.
6. `docs(297): verification` — phase 5.

Characterisation tests land in the commit of the story they pin, and were observed green on
the untouched tree before it (4a output).

## 8. Verification (phase 5)

**V1 — premise (before code; T001).** Re-run the Chromium probe (spec §1) against the running
kiosk's own CSS: read `--color-accent-fault-subtle` through a canvas on `develop`'s tip.
Expected slate (`rgb(20,24,37)`±2). If it is red, **stop**: the premise of §2 is wrong and the
tint design reverts to a question.

**V2 — look at the wall and the console (T030).** Spec §7 steps 1–7. Screenshots into
`specs/297-*/verification.md`, each chip's canvas-read fill and box height beside it. Before
and after for the three wall chips (checkout `develop`, same procedure) so the translucency
removal is visible, not asserted.

**V3 — the composite + render leg (T031).** Same procedure as spec 294 plan §8 V3, because
spec 225's gate is still unwired (`grep render-leg .github/workflows/ci.yml` → only the summary
step, `ci.yml:713-715`, on `ab030e5a`). **Re-check that grep first; if the `render-leg-gate`
job has landed, use its verdict instead and say so.**

1. Build a **scratch** `baseline.json` (scratchpad; not committed — spec 225 T019's job) from
   the most recent nine `develop` push runs' `render-leg-attempt-*.json` up to this branch's
   base, in `scripts/render-leg-check.mjs`'s schema: `baselineP50Milliseconds` = mean,
   `toleranceMilliseconds` = 3 × sample σ. Record both and the run ids. (Spec 294's were
   57.66 ms / 21.97 ms; do not reuse them if newer runs exist.)
2. Download `playwright-report-*-of-4` from the PR's CI run **before** any re-run (memory: a
   re-run erases the failure from history). Get **three** complete records (the run plus two
   `gh run rerun`s, downloading each first).
3. `node scripts/render-leg-check.mjs <shards-dir> <scratch-baseline.json>` per record.
   **Pass:** all three `within tolerance`. Record p50, p95, max, frame interval `T` and the
   shift against the baseline mean, with run ids and full SHAs, in `verification.md`.
4. **On `regressed`:** ADR-0123 triage — compare `T` first. `T` stepped on the PR and not on
   same-day `develop` → the change is costing a frame → **block** (`agent:blocked`, figures
   quoted). `T` unmoved → two more records (memory: the first run after churn looks like a
   regression); a second `regressed` blocks. **On `unmeasured`:** not a pass; one re-run after
   download, a second `unmeasured` blocks.
5. **State whether any tile was badged during the span test** (search the downloaded
   `e2e-report.json`/trace for `tile-out-of-alignment` / *Overlay unavailable*). If none — the
   expected case — write that the figure covers the healthy path only and the badged path rests
   on spec §5's argument plus V2. Never report the figure as covering a badged tile.

Optional, labelled `local` if taken: the span test on one machine, `develop` vs branch, three
runs each. Supporting only.

## 9. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Chromium does not drift the mix hue after all (premise wrong). | V1 first; stop if not reproduced. |
| R2 | Double `Slot` (Popover `Trigger asChild` → `Badge asChild` → `<button>`) drops the ref or handlers. | Radix Slot composes; the characterisation popover cases (Enter opens, Escape returns focus) are the proof and must pass unedited. If they fail, **stop** — do not rewrite them. |
| R3 | `bg-accent-*-subtle` classes absent from the kiosk's compiled CSS. | kiosk `content` scans `../shared/src/**`; the e2e opacity check fails loudly if the class did not compile (transparent → alpha 0). |
| R4 | `RuleState` gains a member beyond Active/Draft/Archived. | Today it is exactly those three (`rules.api.ts:7`); `Record<RuleState, BadgeTone>` makes a new member a type error rather than a silent default. |
| R5 | The Badge placement in `composites/` is ruled an evasion of `InteractionStateTests` fact 5. | Spec §4.3: block; moving it needs a human to narrow the regex. |
| R6 | Dark neutral contrast 4.64:1 is tight; a later `--color-fg-muted` change drops it. | `The_neutral_label_is_legible_on_its_fill` pins it. |
| R7 | `RetryBanner` reviewers read its colour change as unintended. | Declared in spec §3.1/§6, shown in V2 step 6, named in the F commit body. |
| R8 | The leg regresses and CI cannot tell (no wired gate). | V3 by hand with the gate's own checker; its limit (healthy path only) stated. |
| R9 | A parallel wall branch edits `LayoutGrid.tsx`. | Checked 2026-09-30: spec 296's branch touches nothing under `apps/kiosk-web` or `apps/shared/src/ui`. Re-check at phase 4; rebase after whichever merges first. |
