# Plan 299: The mix that lost its hue

**Spec:** [spec.md](./spec.md) · **Issue:** #2695 · **ADRs:** 0146 (amended), 0148 (amended), 0078

## 1. Where this lives

No bounded context, no Domain/Application/Infrastructure/Api layer, no entity, no message.
This is the **design-token layer** (ADR-0078/0148) and its architecture guards:

| Area | Files | Owner (phase 4) |
|---|---|---|
| Token file | `apps/shared/src/ui/tokens/tokens.css` | frontend-engineer |
| Shared Tailwind theme | `apps/shared/src/ui/tokens/tailwindTheme.ts` | frontend-engineer |
| Theme test (new) | `apps/shared/src/ui/tokens/tailwindTheme.test.ts` | test-writer |
| Architecture guards | `tests/Architecture.Tests/{DesignTokenLayerTests,InteractionStateTests,StatusTintTests,OklchColorResolver}.cs` | test-writer |
| Decisions | `docs/adr/0146-*.md`, `docs/adr/0148-*.md` (amended in phase 1) | architect — done |

**Domain model / messaging / boundary rules:** N/A — no domain type, no domain or integration
event, no `Shared.Contracts` change, no cross-context reference. `Architecture.Tests` already
reads `apps/shared` by path (`RepositorySource.Root()`); nothing new is referenced.

**Constitution check.** §II (primitives on domain models) — N/A, no domain code. §IV — N/A
(spec §5). §Testing — red for new behaviour, characterisation for what must not move (spec §6).
ADR-0084 metrics — the new resolver method stays under 30 lines; the hue fact is one loop.
No new test **class**, so no shard-filter entry is needed (memory: *new test classes need a
shard-filter entry*) — every new fact lands in an existing class.

## 2. Token changes

### 2.1 US1 — every mix to OKLab (20 declarations)

Mechanical: `color-mix(in oklch,` → `color-mix(in oklab,` on every declaration, nothing else on
the line. Rendered effect of the opaque ones (the rest are toward `transparent`, render-identical):

| Declaration | Theme | Rendered now (`in oklch`) | Rendered after (`in oklab`) |
|---|---|---|---|
| `--color-accent-hover` (88 % → white) | dark | 228.0° `rgb(136,202,232)` | 210.0° `rgb(128,206,220)` |
| `--color-accent-pressed` (84 % → black) | dark | 234.0° `rgb(96,153,184)` | 210.0° `rgb(84,158,170)` |
| `--color-accent-disabled` (40 % → bg-base) | dark | 239.0° `rgb(53,77,93)` | 214.8° `rgb(48,79,87)` |
| `--color-accent-subtle` (16 % → bg-base) | dark | 250.7° `rgb(28,36,45)` | 223.8° `rgb(25,38,42)` |
| `--color-accent-fault-hover` (88 % → white) | all | 21.7° `rgb(255,107,110)` | 24.66° `rgb(255,108,103)` |
| `--color-accent-fault-pressed` (92 % → black) | all | 22.7° `rgb(228,72,78)` | 24.66° `rgb(228,72,72)` |
| `--color-accent-hover` (88 % → black) | light | 228.0° `rgb(28,90,114)` | 210.0° `rgb(10,93,105)` |
| `--color-accent-pressed` (76 % → black) | light | 246.0° `rgb(36,69,98)` | 210.0° `rgb(7,75,85)` |
| `--color-accent-disabled` (inherited formula) | light | 240.0° `rgb(165,189,206)` | 213.7° `rgb(160,191,199)` |
| `--color-accent-subtle` (12 % → bg-base) | light | 254.0° `rgb(222,229,238)` | 224.8° `rgb(219,230,235)` |
| `--color-accent-hover` / `-pressed` (inherited) | high-contrast | 228° / 234° | 210° / 210° |
| `--color-accent-disabled` (→ black ground) | high-contrast | **300.0°** `rgb(57,51,69)` | 210.0° `rgb(34,60,64)` |
| `--color-accent-subtle` (→ black ground) | high-contrast | **336.0°** `rgb(11,6,10)` | 210.0° `rgb(3,9,11)` |

Comments in `tokens.css` that describe the OKLCH drift as the current rule (`:12-20` — whose
example is itself `in oklch` —, `:70-74`, `:141-148`) are updated to cite ADR-0148's amendment:
the tints stay stops because even OKLab pulls a hued-ground mix off hue (spec §1), not because of
OKLCH. US2 also rewrites the `light` block's comment at `:291-293`, which says the contrast fact
excludes that theme — false once US2 lands.

### 2.2 US2 — text roles

**Naming.** `--color-accent-<role>-text`: ADR-0148's `--<category>-<role>-<variant>`, a sibling of
`-subtle`/`-hover`, so the triad's family stays together and `StatusTintTests`' `TriadRoles` loops
build its name the way they build `-subtle`. (`--color-fg-<role>` was considered and rejected: it
reads beside `--color-fg-on-fault`, which means the opposite — ink *on* a fault fill.)

```css
/* :root — primitives, beside the existing 500/900/50 stops */
--green-700: oklch(48% 0.132 148.34);
--amber-700: oklch(48% 0.1 68.43);
--red-700:   oklch(48% 0.185 24.66);

/* :root — semantic (dark; high-contrast inherits) */
--color-accent-active-text:  var(--color-accent-active);
--color-accent-warning-text: var(--color-accent-warning);
--color-accent-fault-text:   var(--color-accent-fault);

/* [data-theme='light'] */
--color-accent-active-text:  var(--green-700);
--color-accent-warning-text: var(--amber-700);
--color-accent-fault-text:   var(--red-700);
```

Stops chosen at L 48 % with chroma at 95 % of the sRGB gamut edge for each hue (computed
2026-10-04), hue to the triad's declared precision so FR-005's 0.01° holds:

| Role | Light stop | sRGB | on its tint (`-50`) | on `bg-base` (`gray-50`) | on white (`-elevated`/`-raised`) |
|---|---|---|---|---|---|
| active | `oklch(48% 0.132 148.34)` | `rgb(14,113,47)` | 5.35 | 5.72 | 6.14 |
| warning | `oklch(48% 0.1 68.43)` | `rgb(130,81,12)` | 5.76 | 6.25 | 6.71 |
| fault | `oklch(48% 0.185 24.66)` | `rgb(174,18,33)` | 6.13 | 6.73 | 7.22 |

All three in sRGB gamut. Dark/high-contrast: the role *is* the signal, so the existing ≥ 5.07 : 1
figures hold unchanged.

**Why the triad roles themselves are not redeclared in `light`.** ADR-0146's amendment keeps the
signal values pinned, and `No_theme_redeclares_a_triad_role` (green pin) enforces it. Fills,
borders, dots and the kiosk glow (`apps/kiosk-web/src/styles/index.css:14-26`) keep the signal.

## 3. The Tailwind mechanism (FR-008)

```ts
// tailwindTheme.ts, inside extend
textColor: {
  accent: {
    active: 'var(--color-accent-active-text)',
    warning: 'var(--color-accent-warning-text)',
    fault: 'var(--color-accent-fault-text)',
  },
},
```

Tailwind 4.3.3's `text-*` utility resolves `valueThemeKeys: ['--text-color', '--color']`, and the
JS-config compat layer maps `extend.textColor` into `--text-color-*`. **Spiked 2026-10-04** with
`@tailwindcss/node` 4.3.3 (the pinned version) on a config carrying only `colors.accent` plus
`textColor.accent.fault`:

```
.border-accent-fault       { border-color: var(--color-accent-fault); }
.bg-accent-fault           { background-color: var(--color-accent-fault); }
.text-accent               { color: var(--color-accent); }
.text-accent-fault         { color: var(--color-accent-fault-text); }
.text-accent-fault-subtle  { color: var(--color-accent-fault-subtle); }
```

So: the override wins for `text-` only, unlisted keys fall through to `colors`, and alpha
modifiers follow the override. `DesignTokenLayerTests.Every_token_the_theme_cites_is_declared_and_semantic`
already requires every `var(--…)` the theme cites to be declared and semantic — it covers the new
entries for free. The new `tailwindTheme.test.ts` pins the mapping; phase 5's compiled-CSS diff
(spec §7 step 2) proves Tailwind honours it in the real build.

## 4. Guard changes

### 4.1 One mix evaluator — `OklchColorResolver.Mix`

New `internal static` method on the existing shared class (new behaviour, not a lift — spec 297's
"copy rather than refactor a guard" constrained *moving* existing code; this replaces two wrong
copies with one correct implementation, and the copies' callers are already being edited by the
red tests):

```csharp
internal enum MixSpace { Oklab, Oklch }

// L in percent, as OklchTriple carries it; returns OKLCH coordinates of the mix.
internal static (double Lightness, double Chroma, double Hue) Mix(
    MixSpace space,
    (double Lightness, double Chroma, double Hue) first, double firstFraction,
    (double Lightness, double Chroma, double Hue) second);
```

- **Oklab:** convert each to `(L, C·cos h, C·sin h)`, interpolate componentwise, convert back
  (`atan2`, normalised to [0, 360)). An achromatic operand contributes `a = b = 0` whatever its
  written hue — the property FR-002 asserts.
- **Oklch:** L and C linear; hue linear along the **shorter arc**, the written hue kept even at
  C = 0 (CSS Color 4 treats a hue as missing only when powerless *after conversion*; an `oklch()`
  literal's hue is not converted). This branch exists so the hue fact reports a reintroduced
  `in oklch` in rendered degrees rather than only as a regex miss, and so the model can be pinned
  against the browser (below).
- **Model facts** (in `InteractionStateTests`, green from first run, provenance in the comment):
  `Mix(Oklch, red-500, 0.10, gray-950)` → sRGB `(20,24,37)`; `Mix(Oklch, green-500, 0.30, black)` →
  `(48,14,0)`; `Mix(Oklab, red-500, 0.16, gray-950)` → `(44,26,27)`. All three are browser
  observations (spec 297 §1 and issue #2695, Chromium 1243); a red one means the model is wrong.

### 4.2 `ResolveOklch` keeps its name

`InteractionStateTests.ResolveOklch` returns OKLCH coordinates — what every literal in the file is
written in and what `OklchColorResolver.OklchToSrgb` consumes — and that stays true. What was
wrong was the mix *inside* it. After the change it parses `in (oklab|oklch)`, calls `Mix` with the
parsed space, and its comment states it. A rename to `ResolveOklab` would misdescribe the return
value. (Deviation from the brief's suggestion, recorded for the gate.)

`StatusTintTests.TryResolveSrgb`'s mix branch (`:423-453`) does the same: parse the space, call
`Mix`. Its doc comment's "before F re-points it" history is dropped.

### 4.3 Facts

**DesignTokenLayerTests**
- `ColorMixValue`: `in oklch` → `in oklab`. Fact 5's failure text names the amended ADR.
- `LabelSurface` → `color-mix(in oklab, var(--white) 85%, transparent)`; comment says
  render-identical (spec §1).
- **New** `Every_colour_mix_in_the_token_file_mixes_in_oklab`: every declaration value in every
  block; every `color-mix(in <space>` occurrence; space must be `oklab`. Catches `--shadow-*`.

**InteractionStateTests**
- **New** `A_mix_toward_black_or_white_keeps_its_base_hue`: for each theme map (dark, light,
  high-contrast), for each `--color-*` whose value is an opaque `color-mix` (enumerated from the
  file, not a name list — memory: *the wrong red matches the filed number*), resolve the second
  operand; if its chroma is 0, assert `|hue(result) − hue(first operand)|` (circular) ≤ 0.5°.
  Report every offender with theme, name, both hues.
- Three model facts (§4.1).

**StatusTintTests**
- **New** `Each_triad_text_role_keeps_its_triad_hue`: fact 1's shape over `-text`, themes dark,
  light, high-contrast, via `ResolveLiteralThroughVarOnly`.
- `A_triad_label_is_legible_on_its_tint`: text name → `--color-accent-<role>-text`; loop adds
  `("light", lightMap)`; exclusion check removed.
- **New** `A_triad_label_is_legible_on_every_ground`: text role on `--color-bg-base`, `-elevated`,
  `-raised`, every theme.
- **Deleted** `ContrastExclusions`, `IssueReference`, `Each_contrast_exclusion_still_fails`; class
  remarks updated (spec 299 replaces the carve-out).

**apps/shared `tailwindTheme.test.ts`** (Vitest, new) — imports `tailwindTheme`, asserts
`extend.textColor.accent` maps each role to its `-text` var, and that `extend.colors.accent.<role>`
still maps to the signal var (the fills must not move with it).

## 5. Sequencing and commits (ADR-0030; each commit builds and is green on its own)

1. `docs(2695): amend ADR-0146 item 4 and ADR-0148 decision 2` + spec/plan/tasks — phase 1–3.
2. `fix(2695): mix every derived token in oklab` — US1 red tests + resolver + tokens.css switch,
   one commit (a commit that only adds red tests would break `git bisect` on `develop`; the red run
   is quoted in the PR body, not committed red).
3. `fix(2695): give the triad a per-theme text role` — US2 red tests + stops/roles + Tailwind
   mapping, one commit.

US1 before US2 because both edit `StatusTintTests.cs` and US2's contrast facts call the resolver
US1 corrects. Nothing is `[P]` across stories; within a story the test-writer's C# and Vitest work
is `[P]` (disjoint files).

## 6. Engineers

- **Phase 4a — `test-writer`**: all C# architecture-test edits (including `OklchColorResolver.Mix`,
  which is test infrastructure) and the Vitest file. Runs them on `e892965b` and returns verbatim
  red output.
- **Phase 4b — `frontend-engineer`**: `tokens.css`, `tailwindTheme.ts`. May not edit tests.
- **No backend-, infra- or second engineer.** Nothing under `src/`, `AppHost`, CI or `deploy/`.
- **Phase 6 reviewer:** `frontend-reviewer` (tokens, Tailwind) — the C# changes are test-only
  guards in the same idiom as specs 257/268/297, which that reviewer has covered before.

## 7. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Light `--color-accent` on `--color-accent-subtle` at 4.549 : 1 rounds below 4.5 in the C# resolver. | Declared fix: `light` `--color-accent-subtle` 12 % → 11 %; not a threshold change. Stop and report if it happens. |
| R2 | Tailwind's compat mapping of `extend.textColor` changes in a future bump. | `tailwindTheme.test.ts` pins the declaration; the compiled-CSS diff is phase-5 evidence; a Tailwind bump PR re-runs it. |
| R3 | Characterisation drift in dark: the text role adds a `var()` hop; computed colour must be identical. | Phase 5 compares computed `color` of Badge/FaultNotice in dark against `e892965b`. |
| R4 | The issue's light-theme numbers were computed on spec 297's branch; tree may differ. | Re-computed on `e892965b` (spec §1): identical. |
