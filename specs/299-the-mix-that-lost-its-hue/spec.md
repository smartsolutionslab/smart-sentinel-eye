# Spec 299 — The mix that lost its hue

**Issue:** [#2695](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2695)
— *Triad text contrast in the light theme, and ADR-0148 decision 2's OKLCH premise*.
Label `tech-debt`, **not** `agent:ready`. Project #13, status **Todo** (verified 2026-10-04,
`gh issue view 2695 --json projectItems`).
**Branch:** `fix/2695-triad-contrast-and-oklch-drift` (cut from `origin/develop` @ `e892965b`)
**Created:** 2026-10-04
**Lane:** supervised (ADR-0037) — stop at every gate.

**Spec number.** On 2026-10-04 every remote branch, local branch, worktree and open PR was
listed. `origin/develop` tops out at **298**; no remote branch carries a `specs/299-*` or later
directory; there are no open PRs; the only other worktree is `develop`. **299** is free.
Re-check before opening the PR (memory: *spec number: origin/develop isn't enough*).

**ADRs this spec is bound by:**

- **ADR-0146** item 4 — *as amended 2026-10-04 by this issue*: the triad's roles, names, hues
  and signal values survive every theme; a per-theme **text** role at the same hue is now
  permitted, moving only lightness and chroma, only as far as 4.5:1 needs.
- **ADR-0148** decision 2 — *as amended 2026-10-04 by this issue*: every `color-mix()` in the
  token file mixes `in oklab`. Decisions 1 and 3 (theme remaps roles; primitives are OKLCH
  literals) are unchanged and govern the new primitives and roles.
- **ADR-0078** — tokens are CSS custom properties consumed by the shared Tailwind theme.
- **ADR-0139 / ADR-0144** — red first; phase-4a colour declared in §6.
- **ADR-0036** — smallest change; basis for every "not here" in §3.
- **ADR-0037**, **ADR-0109** (`[P]`), **ADR-0053** (sentence-style test names).

**The two human decisions were taken before this spec** (issue #2695 marked them as such) and
are written as in-place amendments of ADR-0146 and ADR-0148, in the repo's existing amendment
form (ADR-0047's `## Amendment (date)` section, ADR-0080's inline "Amended" note with the
original text kept). **No new ADR is needed**: every other choice below — the role names, the
Tailwind mechanism, the stops — is an application of those two amendments and ADR-0148
decision 1, recorded in plan.md.

---

## 1. The issue's premise, re-checked on this tree (`e892965b`)

Computations below use the CSS Color 4 OKLab↔sRGB matrices (the same constants as
`tests/Architecture.Tests/OklchColorResolver.cs`) and CSS Color 4's interpolation rules
(OKLCH: hue linear by percentage along the shorter arc, a *written* hue kept even at chroma 0;
OKLab: linear on L, a, b). **The model reproduces all three browser observations the issue and
spec 297 recorded exactly** — `rgb(20,24,37)`, `rgb(48,14,0)` and `rgb(44,26,27)` — which is
what makes the rest of this table trustworthy without a browser.

| Issue says | Tree says | Consequence |
|---|---|---|
| Triad as label text on its light tint is 1.95 / 1.62 / 2.71 : 1. | **Confirmed**, and no better on the plain grounds: light `bg-base` 2.08 / 1.75 / 2.97, `bg-elevated`/`-raised` (white) 2.24 / 1.88 / 3.19. Dark and high-contrast are ≥ 5.07 : 1 everywhere. | US2 fixes text on the tint **and** on the three grounds — the issue's "any light surface". |
| Spec 297's `A_triad_label_is_legible_on_its_tint` excludes `light` by a named, shrink-only entry with an honesty fact. | **The exclusion is not what keeps light out.** The fact's theme loop (`StatusTintTests.cs:179-183`) lists only `dark` and `high-contrast`; `light` is never iterated, so removing the `ContrastExclusions` entry alone would leave light **untested** while the honesty fact went vacuous. | US2 adds `light` to the loop and deletes the exclusion list *and* its honesty fact — an empty shrink-only list is an assertion that cannot fail (memory: *an assertion must not check its own input*). |
| `--color-accent-fault-subtle` is `color-mix(in oklch, …)`. | **Stale** — spec 297 already re-pointed it at `var(--red-900)` (`tokens.css:149`), and the active/warning tints are stops too. No status tint is a mix any more. | Not touched. The tints **stay stops** even under OKLab: red 10 % into `--gray-950` in OKLab lands at hue 7.7°, 17° short of the triad (§3.2). |
| `color-mix(in oklab, …)` renders the right hue for fault: `rgb(44,26,27)`. | That value is fault **16 %** into `--gray-950` (the `--color-accent-subtle` percentage), not 10 %; 10 % is `rgb(32,21,23)`. Both are reddish but **neither is at the triad's hue** — OKLab does not make a mix into a hued ground hue-invariant. | ADR-0148's amendment states the corrected premise precisely (achromatic operand → hue exact; hued ground → hue moves by the ground's chroma, not the percentage). |
| `--color-accent-subtle` drifts ~37° toward blue. | 40.7° dark, 44° light. Under OKLab: 13.8° / 14.8° at chroma ≤ 0.019 — ΔE_OK ≤ 0.005, invisible. | Fixed in the sense ADR-0148 now claims; not hue-exact, and the spec does not pretend it is. |
| `--color-accent-hover`/`-pressed` drift "a few degrees". | **18° (hover) and 24–36° (pressed)** in every theme — mixing toward `--white`/`--black`, whose written hue 0 is kept. Under OKLab: **0°**. | Fully fixed (US1). |
| (not named) | **High-contrast is the worst case.** Its `--color-bg-base` is `--black`, so `--color-accent-disabled` renders at **300°** (purple, `rgb(57,51,69)`) and `--color-accent-subtle` at **336°** (magenta). Under OKLab both are exactly 210°. | US1's hue fact covers every theme, not the dark one. |
| (not named) | `--color-accent-fault-hover`/`-pressed` drift 3° / 2° (toward hue 0). | Fixed to 0° by US1. |
| `DesignTokenLayerTests.ColorMixValue` / `InteractionStateTests.ResolveOklch` accept `in oklch` only; `ResolveOklch` takes the higher-chroma operand's hue. | **Confirmed**, and there is a **third copy**: `StatusTintTests.TryResolveSrgb` (`:423-453`) with the same regex and approximation. A fourth pin is textual: `DesignTokenLayerTests.LabelSurface` (`:90`) pins `--color-bg-label`'s expression verbatim, `in oklch` included. | All four move (§3.1). |
| — | **The old approximation is exactly OKLab** for a mix toward black/white (same L, same C, base hue). So every *guarded* contrast figure is unchanged by the switch: fg-on-fault 6.10 / 7.04 / 4.93 : 1. Only hued-ground mixes move, and the one guarded such pair is light `--color-accent` on `--color-accent-subtle`: **4.549 : 1** exact vs 4.565 approximated — still passing, thin margin. | Recorded as a risk (§8), not a change. |
| — | Eleven `color-mix(… transparent)` declarations (`--color-bg-label`, `--color-scrim` ×3, `--color-bg-hover`/`-pressed`, `--color-accent-fault-border`, `--shadow-*` ×4 in two themes) render **identically** in either space: premultiplied, `transparent` contributes nothing, and its powerless hue is missing in OKLCH. | Switched anyway, so the guard has one rule (ADR-0148 amendment). A declared no-render-change; §6. |
| Fixing contrast "needs per-theme triad text roles". | **45 call sites** in ~25 files colour text with `text-accent-{active,warning,fault}`. Tailwind 4.3.3's `text-*` utility reads the `--text-color-*` namespace before `--color-*`; **spiked 2026-10-04** with `@tailwindcss/node` 4.3.3: `extend.textColor.accent.fault` makes `.text-accent-fault` compile to the text role while `bg-`/`border-accent-fault` keep the signal and every other key still falls through. | No call site changes (plan §3). |

## 2. User stories

The two stories are independent — neither reads the other's tokens or tests — and each is
observable alone. Same files, so they run in sequence (US1 first: it changes the resolver US2's
contrast facts call).

### US1 (P1): A derived colour keeps its hue

Every `color-mix()` in `tokens.css` mixes `in oklab`. Hover, pressed and every other mix toward
black or white keep their base role's hue exactly, in every theme; a mix into a hued ground moves
only by that ground's real chroma. The architecture guards evaluate what the browser renders.

**Why P1:** it is a visible defect in the default (dark) theme today — every hover and pressed
accent on the console is bluer than the accent — and the worst instances are live in
high-contrast. US2's defect is latent (no page sets `light`).

```gherkin
Scenario: a mix toward black or white keeps its base hue (happy path)
  Given tokens.css as amended
  When every opaque color-mix declaration is resolved in every theme (dark, light, high-contrast)
  And its second operand resolves to an achromatic primitive (--black, --white)
  Then its rendered hue equals its first operand's hue within 0.5 degrees

Scenario: the old space is refused (conflict — the decision that was amended)
  Given a --color-* declaration written as color-mix(in oklch, var(--a) N%, var(--b))
  Then DesignTokenLayerTests' semantic-colour fact fails naming that declaration
  And the hue fact fails naming the rendered hue it drifted to

Scenario: a mix the resolver cannot evaluate is reported, not guessed (bad request)
  Given a --color-* declaration that is a color-mix in any other space, or a nested mix
  Then the resolver fails naming the declaration, rather than approximating it

Scenario: the resolver reproduces the browser (characterisation of the model)
  When the resolver evaluates color-mix(in oklch, red-500 10%, gray-950)
  Then it yields rgb(20,24,37), the value Chromium 1243 rendered (spec 297 §1)
  And color-mix(in oklch, green-500 30%, black) yields rgb(48,14,0)
  And color-mix(in oklab, red-500 16%, gray-950) yields rgb(44,26,27)

Scenario: guarded contrast is unchanged (characterisation)
  Then --color-fg-on-fault on --color-accent-fault / -hover / -pressed is 6.10 / 7.04 / 4.93 : 1
  And --color-accent on --color-accent-subtle is >= 4.5 : 1 in every theme
```

*Auth:* N/A — design tokens; no endpoint, no scope, no principal.

### US2 (P2): A status label is legible in every theme

Each triad hue gets a text role, `--color-accent-<role>-text`. In dark and high-contrast it is
the signal itself; in light it is a darker stop at the triad's own hue. Every
`text-accent-<role>` utility resolves to it; fills, borders and the wall's glow keep the signal.

```gherkin
Scenario: a status label clears 4.5:1 on its chip and on every ground (happy path)
  Given each theme: dark, light, high-contrast
  Then --color-accent-<role>-text on --color-accent-<role>-subtle is >= 4.5 : 1 for every role
  And on --color-bg-base, --color-bg-elevated and --color-bg-raised it is >= 4.5 : 1

Scenario: the text role keeps the triad's hue (conflict — what the amendment does not permit)
  Given a theme that maps --color-accent-fault-text to a stop at a different hue
  Then StatusTintTests fails naming the theme, the role and both hues

Scenario: the signal itself still never moves (conflict)
  Given a theme block redeclaring --color-accent-active, -warning or -fault
  Then No_theme_redeclares_a_triad_role fails (unchanged, green pin)

Scenario: a text role that is not a plain var() chain to a literal is refused (bad request)
  Given --color-accent-warning-text declared as a color-mix or a raw literal
  Then the hue fact fails naming the declaration

Scenario: text utilities pick the text role; fills keep the signal
  When the management-web and kiosk-web CSS is built
  Then .text-accent-fault compiles to color: var(--color-accent-fault-text)
  And .bg-accent-fault and .border-accent-fault still compile to var(--color-accent-fault)

Scenario: dark and high-contrast render exactly as before (characterisation)
  Then every element's computed text colour in dark and high-contrast is unchanged
```

*Auth:* N/A.

## 3. Scope

### 3.1 In

- **`apps/shared/src/ui/tokens/tokens.css`** — every `color-mix(in oklch,` → `in oklab` (20
  declarations, plan §2.1); three new primitives `--green-700`, `--amber-700`, `--red-700`; three
  new semantic roles `--color-accent-<role>-text` in `:root`, redeclared in `light`; comments that
  still describe the OKLCH drift as current updated to the amended rule.
- **`apps/shared/src/ui/tokens/tailwindTheme.ts`** — `extend.textColor.accent.{active,warning,fault}`.
- **`tests/Architecture.Tests/`** — `DesignTokenLayerTests` (`ColorMixValue`, `LabelSurface`,
  fact 5's message), `InteractionStateTests` (`ResolveOklch` evaluates the declared space exactly;
  new hue fact), `StatusTintTests` (third resolver copy; text-role hue and contrast facts; light
  in the loop; exclusion list and honesty fact deleted), `OklchColorResolver` (the shared mix
  evaluation both resolvers call).
- **Two ADR amendments** — written in phase 1 (this phase), part of this PR.

### 3.2 Out, and why

- **Status tints back to mixes.** OKLab does not hold a hue exactly against a hued ground
  (§1); spec 297's stops stay. ADR-0148's amendment says so.
- **Making `light` reachable** (a theme switcher). Not asked; `light` stays latent. This spec
  makes it correct, not shipped.
- **Re-tuning hued-ground mixes to be hue-exact** (e.g. mixing `--color-accent-subtle` toward
  `--black` instead of the ground). Residual drift is ΔE_OK ≤ 0.005; changing the formula would
  change the colour's meaning for an invisible gain.
- **Migrating 45 `text-accent-*` call sites** to a new utility. The Tailwind mapping makes it
  unnecessary (plan §3), and a rename would churn ~25 components and 8 test files for nothing a
  reviewer could see.
- **Past specs' prose** (`specs/257`, `268`, `293`, `297`, `298` mention `in oklch`). They record
  what was decided then; rewriting them falsifies the history ADR-0080's amendment note warns
  about.
- **e2e `interaction-states.spec.ts`'s oklab/oklch parsing** — already accepts both wire formats.

## 4. Functional requirements

- **FR-001** Every `color-mix()` in `tokens.css` — in a `--color-*` role or embedded in a `--shadow-*` value — mixes `in oklab`. A `--color-*` role's mix keeps fact 5's shape, `color-mix(in oklab, var(--x) N%, var(--y)|transparent)`, no nesting.
- **FR-002** For every opaque mix whose second operand resolves to an achromatic literal, in every theme, the rendered hue equals the first operand's within 0.5°.
- **FR-003** The architecture resolvers evaluate `in oklab` exactly (interpolate L, a, b) and `in oklch` per CSS Color 4 (shorter-arc hue, written hue kept), and fail on anything else. No higher-chroma-hue approximation remains in `tests/`.
- **FR-004** `--color-accent-<role>-text` exists for active, warning, fault; declared in `:root` as `var(--color-accent-<role>)`; redeclared in `light` only, as `var(--<hue>-700)`.
- **FR-005** In every theme each text role resolves through `var()` only to an OKLCH literal whose hue equals its triad's resolved hue within 0.01°.
- **FR-006** In every theme each text role is ≥ 4.5 : 1 on its own `-subtle` tint and on `--color-bg-base`, `-elevated`, `-raised`.
- **FR-007** `--color-accent-active`, `-warning`, `-fault` remain declared only in `:root` with their ADR-0146 values (existing pins unchanged).
- **FR-008** `text-accent-<role>` compiles to `var(--color-accent-<role>-text)`; `bg-`, `border-` and every other `accent-*` utility compile exactly as before.
- **FR-009** No contrast exclusion list remains in `StatusTintTests`.

## 5. Latency budget

**N/A.** No change to any leg of constitution §IV. `color-mix()` is resolved at computed-style
time at the same cost in either space, and the wall (dark-pinned) repaints nothing new: its only
triad uses are the highlight outline/glow (signal, unchanged) and status text, whose dark value is
unchanged (US2 characterisation).

## 6. Phase-4a colour

**Red — both stories are behaviour-changing** (rendered colours move in US1; a new role and new
rendered text colour in `light` in US2). Ambiguity would resolve to red regardless.

Red facts, each observed failing on `e892965b` before any production edit, verbatim output
quoted in the PR (ADR-0139):

| Story | Fact | Why it is red on develop |
|---|---|---|
| US1 | `DesignTokenLayerTests.Semantic_colours_cite_the_scale_never_a_literal` (regex → oklab only) | 16 `--color-*` declarations are `in oklch` (the other 4 are `--shadow-*`, which fact 5 does not scan). |
| US1 | `DesignTokenLayerTests.Every_colour_mix_in_the_token_file_mixes_in_oklab` (new — every declaration value, every `color-mix(` occurrence in it, shadows included) | 20 declarations; 4 are `--shadow-*`, which fact 5 never sees. |
| US1 | `DesignTokenLayerTests.Content_roles_keep_their_rendered_values` (`LabelSurface` → oklab) | `--color-bg-label` is `in oklch`. |
| US1 | `InteractionStateTests.A_mix_toward_black_or_white_keeps_its_base_hue` (new) | hover 228°, pressed 234–246°, hc disabled 300°, hc subtle 336°, fault 21.7°/22.7°. |
| US2 | `StatusTintTests.Each_triad_text_role_keeps_its_triad_hue` (new) | `--color-accent-<role>-text` not declared. |
| US2 | `StatusTintTests.A_triad_label_is_legible_on_its_tint` (text role; light added) | not declared; light 1.62–2.71 : 1. |
| US2 | `StatusTintTests.A_triad_label_is_legible_on_every_ground` (new) | not declared. |
| US2 | `tailwindTheme.test.ts` (new, apps/shared) | no `textColor` key. |

Green on develop, and must stay green **unmodified** (characterisation): the resolver-model facts
(the three browser-observed values — new, but they pin the *model*, not the product, so they are
green from their first run; a red one means the model is wrong, not that the tokens are),
`Fault_label_and_disabled_label_contrast_hold`, `Accent_text_on_accent_subtle_meets_contrast_in_every_theme`,
`The_triad_keeps_its_rendered_values`, `No_theme_redeclares_a_triad_role`,
`The_neutral_label_is_legible_on_its_fill`, `Each_triad_tint_is_a_literal_at_its_triad_hue`, and
every vitest/e2e asserting `text-accent-*` class names.

**Two declared assertion edits** — named here so a reviewer does not read them as weakening:
`LabelSurface`'s expected text (render-identical, §1), and the deletion of `ContrastExclusions`
plus `Each_contrast_exclusion_still_fails` (FR-009; replaced by a strictly wider fact).

## 7. Independent end-to-end test procedure (phase 5)

1. `dotnet test tests/Architecture.Tests` — all facts above green; quote the counts.
2. Build both apps; **diff the compiled CSS against `e892965b`'s build**. The only differences
   permitted: `in oklch` → `in oklab` inside token values, the three new primitives/roles, and the
   three `.text-accent-{active,warning,fault}` rules now citing `-text`. Anything else is a
   regression.
3. Boot the stack, open management-web in Chromium (Playwright one-off or devtools):
   - **US1:** canvas read-back (the `readBackColour` technique in `e2e/kiosk-live-updates.spec.ts`)
     of `--color-accent-hover`, `-pressed` in dark and of `--color-accent-disabled`/`-subtle` with
     `data-theme="high-contrast"` set on `<html>`; record the rendered hue — expected ≈ 210°.
   - **US2:** set `data-theme="light"`, read the computed `color` of a Badge (cameras list) and a
     `FaultNotice`, compute contrast against its computed background; expected ≥ 4.5 : 1. Then
     `dark`: computed colours equal the `e892965b` values.
4. Write every figure into the PR verification note as observed (memory: *self-review catches
   contradictions, never omissions*).

## 8. Risks and assumptions

- **Thin margin:** light `--color-accent` on `--color-accent-subtle` is 4.549 : 1 under exact
  OKLab after 8-bit rounding in the resolver. If the C# resolver's rounding lands it below 4.5, the
  fix is a percentage change on `--color-accent-subtle` in `light` (12 % → 11 %), declared — not a
  threshold change.
- **Stop values are computed, not eyeballed** (plan §2.2): `--green-700 oklch(48% 0.132 148.34)`,
  `--amber-700 oklch(48% 0.1 68.43)`, `--red-700 oklch(48% 0.185 24.66)` — in sRGB gamut, worst
  case 5.35 / 5.76 / 6.13 : 1. Amber at that lightness reads as dark ochre (ADR-0146 amendment).
- **Assumed:** Chromium is the only browser that matters (kiosk + console fleet). Not re-verified
  for Firefox/Safari OKLab interpolation; both implement CSS Color 4.
