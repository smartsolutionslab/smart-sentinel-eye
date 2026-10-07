# Spec 308 — The label that forgot the video

**Issue:** [#2709](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2709)
— *CameraViewer's on-video status text fails contrast in light theme (spec 299 follow-up)*.
Label `agent:ready`; Project #13, status **In Progress** (verified 2026-10-07,
`gh issue view 2709 --json projectItems`).
**Branch:** `fix/2709-camera-viewer-video-text-contrast` (cut from `origin/develop` @ `362ffbeb`)
**Created:** 2026-10-07
**Lane:** autonomous (ADR-0144).

**Spec number.** On 2026-10-07 `origin/develop` tops out at **306**; open PR branch
`fix/2563-throttle-kiosk-telemetry` already carries `specs/307-telemetry-that-fits-its-bucket`.
No remote branch carries `specs/308-*`; the only worktree is this one. **308** is free.
Re-check after every parked-PR merge before opening the PR (memory: *spec number:
origin/develop isn't enough*).

**The design decision was taken by a human before this spec**, recorded on the issue
([comment 6028912437](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2709#issuecomment-6028912437),
2026-10-07):

> Decision: a new semantic token that stays signal-color in every theme — matches the per-role
> colour pattern spec 299 already established.

This spec decides nothing beyond that: it names the token, places it, and wires the one call site.

**ADRs this spec is bound by:**

- **ADR-0146** item 4, *as amended 2026-10-04* (spec 299) — the three signal values are declared
  once in `:root`, never redeclared by a theme; a per-theme **text** role
  (`--color-accent-<role>-text`) may move lightness/chroma "only as far as that theme's label-text
  contexts need … — the role's own tint and the three grounds". See §5 for the one sentence of
  that amendment this spec narrows, and why that is recording the human decision, not a new one.
- **ADR-0148** decision 1 (a theme remaps semantic roles; primitives fixed; components cite the
  semantic layer only) and its Naming rule (`--<category>-<role>-<variant>`).
- **ADR-0078** — tokens are CSS custom properties consumed through the shared `tailwindTheme.ts`.
- **ADR-0165** §3 — precedent for a theme-invariant role because "an overlay sits on video, not on
  a theme surface" (`--color-overlay-ink-dark/-light`).
- **ADR-0036** (smallest change — two roles, not three; §3.2), **ADR-0139/ADR-0144** (red first;
  colour in §6), **ADR-0037**, **ADR-0109** (`[P]`), **ADR-0053** (sentence-style test names).

---

## 1. The issue's premise, re-checked on this tree (`362ffbeb`)

Computed with the CSS Color 4 OKLab→sRGB matrices (same constants as
`tests/Architecture.Tests/OklchColorResolver.cs`), WCAG 1.4.3 relative luminance.

| Issue says | Tree says | Consequence |
|---|---|---|
| `ViewerOverlay` draws `text-accent-fault` / `text-accent-warning` over `bg-scrim` on `bg-bg-video`. | **Confirmed** — `CameraViewer.tsx:534-539` (tone), `:549` (`bg-scrim`), `:415` (`bg-bg-video`). | The one call site to change. |
| `--color-bg-video` is `--black` in every theme. | **Confirmed** — declared once (`tokens.css` `:root`), pinned by `DesignTokenLayerTests.Content_roles_are_pinned_across_themes`. | The ground is theme-invariant, so the text on it can be too. |
| fault 6.58 → 2.91 : 1, warning 11.15 → 3.14 : 1 against `--color-bg-video`. | **Reproduced exactly.** With the light scrim composited (`--gray-950` 40 % over black) the ground lightens slightly: signal 6.38 / 10.81, light `-text` 2.82 / 3.04. Dark/high-contrast scrims are black-over-black, i.e. black. | The signal clears 4.5 : 1 with ≥ 1.88 margin in every theme, scrim included. |
| (not named) | `--color-scrim` *is* redeclared per theme (light: `gray-950` 40 %, hc: black 80 %). | Not touched: it only darkens a black ground; the figures above already include it. |
| (not named) | **`text-fg-muted` on the same overlay** (neutral label and the hint line) is `--gray-600` in light: **4.15 : 1** on bare video, **4.02 : 1** with the scrim — also below 4.5. Dark `--gray-500`: 6.00 / 5.82. | **Out of scope** (§3.2): a neutral on-video role is a different decision from the one the human took. Filed as a follow-up. |
| Used by `CameraDetailPage` and `OverlayEditorDialog`. | `CameraViewer` is imported by `management-web` `CameraDetailPage.tsx`, `OverlayDraftForm.tsx`, and `kiosk-web` `LayoutGrid.tsx`. | Kiosk renders identically (dark pinned; the new role equals the signal, which equals the dark `-text`). |
| Light theme is unreachable at runtime. | **Confirmed** — both `index.html` pin `data-theme="dark"`; no switcher. | Latent; phase 5 sets the attribute by hand. |

## 2. User stories

### US1 (P1): A camera's status reads over its video in every theme

As an operator viewing a camera in the console, when the stream is in fault (error/offline/failed
read) or reconnecting, I can read the status label over the video at ≥ 4.5 : 1 in every theme,
because on-video triad text keeps its signal colour — the ground it sits on never changes.

**Acceptance (Gherkin):**

```gherkin
Scenario: Fault label on video is the signal in light theme (happy)
  Given data-theme="light" on <html>
  And a CameraViewer whose stream status is "offline"
  When the viewer overlay renders its status label
  Then the label carries class "text-accent-fault-on-video"
  And its computed colour equals the computed --color-accent-fault
  And its contrast against the video container ground is >= 4.5:1

Scenario: Reconnecting label on video is the signal in light theme (happy)
  Given data-theme="light" on <html>
  And a CameraViewer whose WHEP session is "reconnecting"
  Then the label carries class "text-accent-warning-on-video"
  And its contrast against the video container ground is >= 4.5:1

Scenario: Dark and high-contrast render exactly as before (conflict — no regression)
  Given data-theme="dark" (or "high-contrast")
  When the same two states render
  Then the label's computed colour equals its value on 362ffbeb

Scenario: The on-surface text role is untouched (conflict — boundary)
  Given data-theme="light"
  When a Badge or FaultNotice renders a fault label on a light surface
  Then it still carries text-accent-fault and resolves to --color-accent-fault-text (--red-700)

Scenario: A theme cannot move the on-video role (bad request — guard)
  Given a [data-theme] block that redeclares --color-accent-fault-on-video
  Then the architecture guard fails the build

Scenario: Neutral states keep their tone (bad request — not a triad state)
  Given a CameraViewer whose status is "connecting" or "live"
  Then the label carries text-fg-muted and no -on-video class
```

**Auth:** N/A — a presentation token; no endpoint, no scope, no data reached.

**Independent test:** §7.

## 3. Scope

### 3.1 In

- Two semantic colour roles in `apps/shared/src/ui/tokens/tokens.css` `:root`:
  `--color-accent-fault-on-video`, `--color-accent-warning-on-video`, each citing its signal role;
  declared in no theme block.
- Two `extend.textColor.accent` keys in `tailwindTheme.ts` → `text-accent-fault-on-video`,
  `text-accent-warning-on-video`.
- `ViewerOverlay`'s two triad tone classes switch to them (`CameraViewer.tsx:536,538`).
- Guards: two architecture facts, one Vitest file, one extended Vitest assertion (§6).
- One dated inline note on ADR-0146's 2026-10-04 amendment (§5, T003).

### 3.2 Out, and why

- **`--color-accent-active-on-video`.** No on-video call site paints green text (`ViewerOverlay`'s
  tones are fault, warning, muted). ADR-0036: add it with its first consumer.
- **The neutral on-video tone (`text-fg-muted`, 4.15 / 4.02 : 1 in light).** A real defect of the
  same class, but the human decision names a *signal-colour* token; a neutral role is a different
  colour choice (which gray, or `--white` at an alpha) and needs its own call. Follow-up issue (T030).
- **Kiosk `LayoutGrid.tsx:214` / `WallPage.tsx:166`** (`text-accent-active` on
  `bg-accent-active/20`): a tinted chip, not bare video; kiosk is dark-pinned. Not touched.
- **A theme switcher.** Light stays unreachable at runtime; this is a latent fix.
- **`--color-scrim`.** Already per-theme; only darkens the ground (§1).

## 4. Functional requirements

- **FR-001** `tokens.css` `:root` declares `--color-accent-fault-on-video: var(--color-accent-fault)`
  and `--color-accent-warning-on-video: var(--color-accent-warning)` — citing the signal **role**,
  not a primitive (`Nothing_outside_the_token_file_cites_a_primitive` stays satisfied; the value
  follows the signal if ADR-0146 ever re-pins it).
- **FR-002** No `[data-theme=…]` block declares either name.
- **FR-003** Each resolves, in every theme, to the same colour as its signal role, and clears
  4.5 : 1 on `--color-bg-video` in every theme.
- **FR-004** `tailwindTheme.extend.textColor.accent` gains `'fault-on-video'` and
  `'warning-on-video'`; the three spec-299 keys are unchanged; `extend.colors` is unchanged.
- **FR-005** `ViewerOverlay` uses `text-accent-fault-on-video` where it used `text-accent-fault`
  and `text-accent-warning-on-video` where it used `text-accent-warning`; the tone *selection*
  (`failedRead || error || offline` → fault; `reconnecting` → warning; else muted) is unchanged.
- **FR-006** No other call site changes; `text-accent-<role>` keeps meaning "on a theme surface".
- **FR-007** `tokens.css` comment above the new roles states why they are theme-invariant (the
  ground is `--color-bg-video`, pinned) and cites #2709 / ADR-0146 — one comment, not per line.

## 5. The one sentence this narrows — no new ADR

ADR-0146's 2026-10-04 amendment says the text role is "used **wherever** the triad colours text."
Text on video would no longer use it. Everything else in the amendment already accommodates the
new roles:

- *What survives unchanged, in every theme* (item 3): "The signal values themselves … declared once,
  in `:root`, and never redeclared by a theme." — the new roles **are** the signal, pinned.
- The text role's latitude is scoped to "that theme's label-text contexts … — the role's own tint
  and the three grounds (`--color-bg-base`, `-elevated`, `-raised`)". `--color-bg-video` is not one
  of them; the amendment never considered a theme-invariant ground.
- "Where the signal already clears 4.5:1 … the text role **is** the signal." On video it clears in
  every theme (§1), so the amendment's own rule yields the signal there.
- *Still not permitted*: a different hue, a theme dropping/renaming a triad role, triad as
  affordance/brand. None applies.
- ADR-0148 decision 1 ("a theme redefines the semantic layer only") does not require every role
  to be redefined; spec 293 §5 and ADR-0165 §3 already rely on that (`--color-bg-video`,
  `--color-fg-on-fault`, `--color-overlay-ink-*`).

So the human decision does not contradict a "not permitted" clause; it narrows one over-broad
word. **No new ADR.** The record is kept honest by a dated inline note on that amendment (text in
plan §5), in the form ADR-0148 already uses for its 2026-10-04 cross-reference. It records a
decision a human took on #2709; it decides nothing.

**Lane rule.** ADR-0144 forbids the lane to *write an ADR*. If the orchestrator or the phase-6
reviewer reads the inline note as that, **block T003 only** (comment + `agent:blocked` on a
follow-up, not on #2709) and ship the rest: no code depends on the note.

## 6. Phase-4a colour

**Red.** The change is behaviour-changing: new tokens, a new Tailwind mapping, a different class
emitted by `CameraViewer`, and a different rendered colour in `light`. That light is unreachable
today does not make it a refactor — §Testing's characterisation path is for changes whose
*observable* behaviour does not move, and the emitted class and the light-theme colour both move.
Ambiguity would resolve to red regardless (CLAUDE.md, phase 4a).

Red facts, observed failing on `362ffbeb` before any production edit, output quoted verbatim in
the PR (ADR-0139):

| Fact | File | Why red on develop |
|---|---|---|
| `Each_on_video_text_role_is_its_signal_in_every_theme` (new) | `tests/Architecture.Tests/StatusTintTests.cs` | Neither `-on-video` role is declared. |
| `A_triad_label_is_legible_on_video` (new) | same | Not declared → unresolved. |
| `maps each on-video role under extend.textColor.accent to its signal var` (new `it`) | `apps/shared/src/ui/tokens/tailwindTheme.test.ts` | No such keys. |
| ViewerOverlay tone facts (new file) | `apps/shared/src/ui/composites/CameraViewerOverlayTone.test.tsx` | Overlay emits `text-accent-fault` / `-warning`. |

**One declared assertion edit** (not a weakening): spec 299's
`maps each triad role under extend.textColor.accent to its -text var` asserts `toEqual` on the
whole `textColor.accent` object, so adding keys turns it red. The edit adds the two new entries to
its expected object; the three existing entries stay byte-identical. It must **not** become
`toMatchObject` (that would stop it catching an unexpected key).

**Characterisation — green on `362ffbeb`, passing unmodified after:** every other
`StatusTintTests`, `DesignTokenLayerTests` and `InteractionStateTests` fact (notably
`No_theme_redeclares_a_triad_role`, `The_triad_keeps_its_rendered_values`,
`Content_roles_are_pinned_across_themes`, `Every_token_the_theme_cites_is_declared_and_semantic`,
`A_triad_label_is_legible_on_every_ground`); every existing `CameraViewer*.test.tsx`; the
`tailwindTheme.test.ts` `colors.accent` fact; `Badge.test.tsx`, `FaultNotice.test.tsx`.

## 7. Independent end-to-end test procedure (phase 5)

1. `dotnet test tests/Architecture.Tests` and `npm test` in `apps/shared` — quote counts.
2. Build `management-web` and `kiosk-web`; diff compiled CSS against `362ffbeb`'s build. Permitted
   differences only: the two new custom properties in `:root`, and two new rules
   `.text-accent-fault-on-video { color: var(--color-accent-fault-on-video) }` /
   `.text-accent-warning-on-video { … }`. The existing `.text-accent-fault` / `-warning` rules stay
   (other call sites use them).
3. Boot the stack; open `management-web` `CameraDetailPage` for a camera in fault (provoke an
   outage by patching its MediaMTX path — memory: *provoking a stream outage*) and one reconnecting.
   For each of `dark`, `light`, `high-contrast` set on `<html>`: read the label's computed `color`
   and the composited ground (canvas read-back of the overlay region, the `readBackColour`
   technique) and compute contrast. Expected: ≥ 4.5 : 1 in all three; dark/hc colours equal the
   `362ffbeb` values; light equals the signal (`rgb(255,82,82)` / `rgb(255,171,64)` ± rounding).
4. Same check once in `OverlayDraftForm`'s preview (the second console consumer).
5. Write every figure into `verification.md` as observed.

## 8. Risks and assumptions

- **A1** Tailwind 4.3.3 resolves `text-accent-fault-on-video` to the `--text-color-accent-fault-on-video`
  key rather than splitting it. Dashed keys under `extend.colors.accent` (`fault-hover`) already
  work; §7 step 2 proves it in the real build.
- **A2** The guard measures against `--color-bg-video` (opaque), not the scrim composite; the
  composite only lowers the signal's figures to 6.38 / 10.81 (§1). Recorded, not guarded.
- **R1** Naming collision with `--color-fg-on-*` ("ink on a fill"): `-on-video` here means "text on
  the video ground", the same preposition with the same meaning (`fg-on-fault` = ink on a fault
  fill; `accent-fault-on-video` = fault ink on video). Consistent, not ambiguous.
