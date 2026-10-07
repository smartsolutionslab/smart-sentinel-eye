# Plan 308: The label that forgot the video

**Spec:** [spec.md](./spec.md) · **Issue:** #2709 · **Base:** `362ffbeb`

## 1. Where this lives

Frontend design system only — `apps/shared` (tokens, shared Tailwind theme, one composite) plus the
C# architecture guards that read the token file. No bounded context, no domain model, no messaging,
no `Shared.Contracts`, nothing under `src/`, `AppHost`, CI or `deploy/`. Boundary rules
(NetArchTest) are untouched. Latency: **N/A** — a static class swap, same element count, no
runtime work; the ≤ 50 ms composite + render leg is not affected (constitution §IV).

| File | Change | Owner |
|---|---|---|
| `apps/shared/src/ui/tokens/tokens.css` | +2 roles in `:root` (+1 comment) | frontend-engineer |
| `apps/shared/src/ui/tokens/tailwindTheme.ts` | +2 `extend.textColor.accent` keys | frontend-engineer |
| `apps/shared/src/ui/composites/CameraViewer.tsx` | 2 class strings in `ViewerOverlay` | frontend-engineer |
| `tests/Architecture.Tests/StatusTintTests.cs` | +2 facts (existing class) | test-writer |
| `apps/shared/src/ui/tokens/tailwindTheme.test.ts` | +1 `it`, 1 declared expected-object extension | test-writer |
| `apps/shared/src/ui/composites/CameraViewerOverlayTone.test.tsx` | new file | test-writer |
| `docs/adr/0146-one-discipline-two-surfaces.md` | dated inline note (§5) | orchestrator (see spec §5 lane rule) |

## 2. Tokens

**Naming.** `--color-accent-<role>-on-video`: ADR-0148's `--<category>-<role>-<variant>`, a sibling
of spec 299's `-text` and `-subtle` so the triad family stays together and `StatusTintTests` can
build the name the way it builds `-text`. The variant names the **ground**, which is the reason the
role exists — the same preposition as `--color-fg-on-fault` / `-on-label` / `-on-accent` (ink *on*
that ground). Rejected: `-video-text` (reads as "text of the video"); `--color-fg-<role>-on-video`
(moves the triad out of its `accent` family, breaking the `TriadRoles` loop shape).

Placed directly after the three `-text` roles in the second `:root` block:

```css
  /* On-video triad text (#2709; ADR-0146 item 4, note of 2026-10-07): the
     ground is --color-bg-video, pinned black in every theme, so the signal
     clears 4.5:1 on it everywhere and no theme may move these — unlike the
     -text roles above, which adapt to a theme's own surfaces. Not declared
     for `active`: no on-video call site paints green text (ADR-0036). */
  --color-accent-warning-on-video: var(--color-accent-warning);
  --color-accent-fault-on-video: var(--color-accent-fault);
```

Not declared in `[data-theme='light']` or `[data-theme='high-contrast']`. Cites the signal **role**
(as the dark `-text` roles do), never a primitive.

Resolved, every theme (= signal): fault `#ff5252`, warning `#ffab40`. On `--color-bg-video`:
6.58 / 11.15 : 1; with the light scrim composited 6.38 / 10.81 : 1 (spec §1).

## 3. Tailwind

```ts
textColor: {
  accent: {
    active: 'var(--color-accent-active-text)',
    warning: 'var(--color-accent-warning-text)',
    fault: 'var(--color-accent-fault-text)',
    'warning-on-video': 'var(--color-accent-warning-on-video)',
    'fault-on-video': 'var(--color-accent-fault-on-video)',
  },
},
```

Extend the existing comment above `textColor` by one sentence: the `-on-video` keys exist so text on
the theme-invariant video ground keeps the signal (#2709). Not added to `extend.colors` — no
`bg-`/`border-` use (ADR-0036). `DesignTokenLayerTests.Every_token_the_theme_cites_is_declared_and_semantic`
covers the new `var()`s for free.

## 4. `CameraViewer.tsx`

`ViewerOverlay` tone, lines 534-539:

```tsx
  const tone =
    failedRead || status === 'error' || status === 'offline'
      ? 'text-accent-fault-on-video'
      : status === 'reconnecting'
        ? 'text-accent-warning-on-video'
        : 'text-fg-muted';
```

Nothing else in the file changes. No comment at the call site — the class name says it; the *why*
lives once in `tokens.css` (CLAUDE.md: no drive-by comments).

## 5. ADR-0146 inline note (T003)

Appended to the amendment's **Decision** section, after the "What may now differ per theme"
paragraph, in the blockquote-note form ADR-0148 line 119 uses:

> **2026-10-07 (issue #2709, spec 308).** "Wherever the triad colours text" means on a theme
> surface — the tint and the three grounds this section names. Text on `--color-bg-video`, which
> is black in every theme, uses `--color-accent-<role>-on-video` instead: declared once in `:root`,
> equal to the signal, never redeclared by a theme, because the signal already clears 4.5:1 there
> in every theme. Decided on the issue by a human before the spec; recorded here so this sentence
> is not contradicted by the token file.

Header status line gains `; note 2026-10-07`. Original text kept. No constitution change.

## 6. Guards

### 6.1 `StatusTintTests` (existing class — no shard entry; Architecture.Tests is not sharded)

Reuse the class's own `ParseDeclarations`, `RootMap`, `ThemeMap`, `IsRootSelector`,
`TryContrastRatio`. A local `OnVideoRoles = ["warning", "fault"]` (not `TriadRoles` — active is
deliberately absent; spec §3.2).

1. **`Each_on_video_text_role_is_its_signal_in_every_theme`** — for each of the two roles: declared
   in `:root` with value exactly `var(--color-accent-<role>)`; no declaration outside `:root`.
   Failure names the selector, citing #2709/ADR-0146.
2. **`A_triad_label_is_legible_on_video`** — for `dark`, `light`, `high-contrast` maps and both
   roles, `TryContrastRatio("--color-accent-<role>-on-video", "--color-bg-video", map) >= 4.5`.
   Counterfactual for phase 6 (memory: *prove a guard by counterfactual*): pointing light's value at
   `--color-accent-<role>-text` must fail it (2.91 / 3.14).

Fact 1 alone would pass if someone redeclared the role in `light` to the same expression; fact 1's
"not outside `:root`" clause catches that. Fact 2 alone would pass for any light value ≥ 4.5; fact 1
pins it to the signal. Together they state FR-002/FR-003.

### 6.2 `tailwindTheme.test.ts`

- New `it('maps each on-video role under extend.textColor.accent to its signal-tracking var')` —
  `textColor.accent['fault-on-video']` / `['warning-on-video']` equal the two `var()`s.
- Declared edit: spec 299's `toEqual` gains the same two entries. Stays `toEqual`.

### 6.3 `CameraViewerOverlayTone.test.tsx` (new)

Reuse the harness of `CameraViewer.test.tsx` (its WHEP/stream-health doubles and `setHealth`), not a
new one. The overlay is `aria-hidden`, so query by text with `{ hidden: true }` or within the
`aria-hidden` container:

- reconnecting → label has `text-accent-warning-on-video`, not `text-accent-warning`;
- stream health `Offline` → `text-accent-fault-on-video`;
- session `error` → `text-accent-fault-on-video`;
- connecting → `text-fg-muted`, no `-on-video` class.

jsdom does not compute Tailwind colours; class assertions are what this layer can prove. The
rendered colour is phase 5's job (spec §7).

## 7. Sequencing and commits (ADR-0030; each builds alone)

1. `test(2709): red-first guards for the on-video triad text role (spec 308)` — §6, plus the
   declared `toEqual` extension. Builds; the named tests fail.
2. `fix(2709): keep on-video status text at the signal colour in every theme` — §2-§4.
3. `docs(2709): note the on-video text role on ADR-0146's amendment` — §5 (if not blocked).
4. `docs(2709): spec 308 artefacts` / `verification.md` — as the lane orders them.

## 8. Engineers

- **Phase 4a — `test-writer`:** §6 only; returns verbatim red output on `362ffbeb`.
- **Phase 4b — `frontend-engineer`:** `tokens.css`, `tailwindTheme.ts`, `CameraViewer.tsx`. May not
  edit tests. Runs `prettier --check` as well as eslint (memory: *eslint clean ≠ prettier clean*).
- **No backend or infra engineer.**
- **Phase 6:** `frontend-reviewer`.

## 9. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Tailwind splits `text-accent-fault-on-video` differently than expected. | Dashed keys already compile (`fault-hover`); §7 step 2 compiled-CSS diff. |
| R2 | A reviewer judges the ADR note a new decision. | Spec §5 lane rule: block T003 only; code ships. |
| R3 | The new Vitest file duplicates harness code. | Import/copy the minimum from `CameraViewer.test.tsx`; if its doubles are file-private, copy rather than refactor (spec 297 precedent). |
