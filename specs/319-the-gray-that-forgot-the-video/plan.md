# Plan 319: The gray that forgot the video

**Spec:** [spec.md](./spec.md) · **Issue:** #2734 · **Base:** `ba37ca5a` · **Precedent:** spec 308

## 1. Where this lives

Frontend design system only — `apps/shared` (tokens, shared Tailwind theme, one composite) plus the C#
architecture guards that read the token file. No bounded context, no domain model, no messaging, no
`Shared.Contracts`, nothing under `src/`, `AppHost`, CI or `deploy/`. NetArchTest boundaries untouched.
Latency: **N/A** (spec §8).

| File | Change | Owner |
|---|---|---|
| `apps/shared/src/ui/tokens/tokens.css` | +1 role in `:root` (+1 comment), +1 line in `[data-theme='light']` | frontend-engineer |
| `apps/shared/src/ui/tokens/tailwindTheme.ts` | +1 `extend.textColor.fg` key, +1 comment sentence | frontend-engineer |
| `apps/shared/src/ui/composites/CameraViewer.tsx` | 2 class strings in `ViewerOverlay` | frontend-engineer |
| `tests/Architecture.Tests/StatusTintTests.cs` | +2 facts, +1 remarks paragraph (existing class) | test-writer |
| `apps/shared/src/ui/tokens/tailwindTheme.test.ts` | +1 `it`, +1 header-comment sentence | test-writer |
| `apps/shared/src/ui/composites/CameraViewerOverlayTone.test.tsx` | 1 declared edit, +1 `it`, header comment | test-writer |

No ADR file. No shard filter (Architecture.Tests is not sharded; no Integration.Tests class).

## 2. Token

**Naming.** `--color-fg-muted-on-video` — ADR-0148's `--<category>-<role>-<variant>` with spec 308's
`-on-video` variant appended to the neutral role it replaces on video, exactly as 308 appended it to
`--color-accent-<role>`. Tailwind class `text-fg-muted-on-video` beside `text-fg-muted`. Rejected:
`--color-fg-on-video` (drops the role — a future `fg-primary-on-video` would then have no sibling
shape); `--color-accent-neutral-on-video` (the triad family is three named hues; a neutral there breaks
the `TriadRoles`/`OnVideoRoles` loop shape and ADR-0146's "three" wording).

**`:root`**, directly after spec 308's two `-on-video` roles (so all on-video text roles sit together):

```css
  /* On-video neutral text (#2734): the same black --color-bg-video ground as
     the two roles above, but a neutral has no pinned signal to fall back
     on. It follows each theme's own --color-fg-muted — dark and
     high-contrast already clear 4.5:1 on black — and only `light`, whose
     surface muted is chosen for light surfaces (4.14:1 on video),
     redeclares it, to dark's stop. */
  --color-fg-muted-on-video: var(--color-fg-muted);
```

**`[data-theme='light']`**, directly after its `--color-fg-muted: var(--gray-600);` line:

```css
  --color-fg-muted-on-video: var(--gray-500);
```

Not declared in `[data-theme='high-contrast']` (inherits `var(--color-fg-muted)` → `--gray-200`).

Resolved: dark `--gray-500` `rgb(129,138,152)`, light `--gray-500` `rgb(129,138,152)`, high-contrast
`--gray-200` `rgb(211,216,224)`. On `--color-bg-video`: **6.02 / 6.02 / 14.67 : 1**; with the scrim
composited: **5.85 / 5.85 / 14.67 : 1** (spec §1, §5).

## 3. Tailwind

```ts
textColor: {
  accent: { /* spec 299 + 308 keys, unchanged */ },
  fg: {
    'muted-on-video': 'var(--color-fg-muted-on-video)',
  },
},
```

Extend the comment above `textColor` by one sentence: `fg.muted-on-video` (#2734) is the neutral
counterpart of the `-on-video` accent keys; every other `text-fg-*` keeps resolving through
`colors.fg`. Not added to `extend.colors.fg` (ADR-0036; no `bg-`/`border-` use), matching 308.
`DesignTokenLayerTests.Every_token_the_theme_cites_is_declared_and_semantic` covers the new `var()`.

## 4. `CameraViewer.tsx`

`ViewerOverlay`, line 553 and line 566:

```tsx
        : 'text-fg-muted-on-video';
...
      {hint !== null && <span className="px-4 text-xs text-fg-muted-on-video">{hint}</span>}
```

Nothing else changes. No call-site comment (the class name says it; the *why* lives once in
`tokens.css`).

## 5. Guards

### 5.1 `StatusTintTests` (existing class, reuse its helpers)

Reuse `ParseDeclarations`, `RootMap`, `ThemeMap`, `IsRootSelector`, `IsLightTheme`, `TryContrastRatio`,
`MinimumTextContrast`. No new helper, no new parser. The class already holds a neutral fact
(`The_neutral_label_is_legible_on_its_fill`), so the new facts belong here, not in a new class. Add a
class-remarks paragraph *Spec 319 (issue #2734)* naming the two new red facts.

1. **`The_neutral_on_video_role_is_the_muted_role_except_in_light`** — `--color-fg-muted-on-video`:
   declared in `:root` with value exactly `var(--color-fg-muted)`; every declaration outside `:root` is
   in a selector for which `IsLightTheme` is true (at most one such). Failures name the selector and
   cite #2734: a `:root` value other than the neutral role ("dark and high-contrast must keep their own
   muted"), a declaration in any other block ("only light's surface muted fails on video").
   Deliberately does **not** assert light's literal value — fact 2 bounds it by outcome, and asserting
   the value would make the test check its own input.
2. **`The_neutral_label_is_legible_on_video`** — for `dark`, `light`, `high-contrast` maps,
   `TryContrastRatio("--color-fg-muted-on-video", "--color-bg-video", map) >= MinimumTextContrast`.
   Mirrors `A_triad_label_is_legible_on_video` line for line.

**Counterfactuals for phase 6** (memory: *prove a guard by counterfactual*), each run and quoted:
- delete the light-block line → fact 2 fails `[light] … 4.14:1` (light resolves its own `--gray-600`);
- add `--color-fg-muted-on-video: var(--gray-500);` to the high-contrast block → fact 1 fails naming it;
- change `:root` to `var(--gray-500)` → fact 1 fails (the high-contrast regression spec §5 rejects).

Together: fact 1 pins dark/high-contrast to "unchanged" and confines movement to light (FR-001/002);
fact 2 bounds light by outcome (FR-003).

### 5.2 `tailwindTheme.test.ts`

New `it('maps the neutral on-video role under extend.textColor.fg to its var')`. Same cast trick as
spec 308's on-video `it`, for the same reason (until the key exists, a literal index is TS7053 — a
compile error, not the red this test must show):

```ts
const textColors = tailwindTheme.extend.textColor as Record<string, Record<string, string> | undefined>;
expect(textColors.fg?.['muted-on-video']).toBe('var(--color-fg-muted-on-video)');
```

The existing `textColor.accent` `toEqual` is **not** edited (the new key is under `fg`). One sentence
added to the file's header comment naming spec 319.

### 5.3 `CameraViewerOverlayTone.test.tsx` (spec 308's file — extend, don't duplicate)

- **Declared edit** (spec §7): `Keeps text-fg-muted, with no -on-video class, while connecting` →
  `Carries text-fg-muted-on-video, not text-fg-muted, while connecting`; `toContain('text-fg-muted')`
  becomes `toContain('text-fg-muted-on-video')` + `not.toContain('text-fg-muted')`; the two triad
  `not.toContain` lines unchanged.
- **New `it`**: `Paints the hint line with text-fg-muted-on-video, not text-fg-muted` — stream health
  `Offline` with error `'Source powered down.'` (the harness already sets that up), find the hint by
  its text with `{ hidden: true }` semantics as the existing cases do, assert the class pair. The
  test-writer confirms the rendered hint text against `hintFor` before asserting; if `Offline`'s error
  does not reach the hint, use the failed-read case, whose hint is `'Could not reach the streaming
  service.'`.
- Header comment: replace "The neutral tone (`text-fg-muted`) is untouched … T030 follow-up" with one
  sentence citing spec 319/#2734.

jsdom does not compute Tailwind colours; class assertions are what this layer proves. The rendered
colour is phase 5's job (spec §8).

## 6. Sequencing and commits (ADR-0030; each builds alone)

1. `test(2734): red-first guards for the on-video neutral text role (spec 319)` — §5, including the
   declared edit. Builds; the five named tests fail.
2. `fix(2734): give on-video neutral text its own role, legible in every theme` — §2-§4.
3. `docs(2734): spec 319 artefacts` / `verification.md` — as the lane orders them.

## 7. Engineers

- **Phase 4a — `test-writer`:** §5 only; returns verbatim red output on `ba37ca5a`.
- **Phase 4b — `frontend-engineer`:** `tokens.css`, `tailwindTheme.ts`, `CameraViewer.tsx`. May not edit
  tests. Runs `prettier --check` as well as eslint (memory: *eslint clean ≠ prettier clean*).
- **No backend or infra engineer.**
- **Phase 6:** `frontend-reviewer`, running the three §5.1 counterfactuals.

## 8. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | `extend.textColor.fg` shadows the `colors.fg` utilities. | Fallback already proven by `text-accent` without a `textColor.accent.DEFAULT`; spec §8 step 2 compiled-CSS diff. |
| R2 | A reviewer reads the light redeclaration as deviating from spec 308. | Spec §5 states the deviation and the high-contrast regression it avoids; fact 1 encodes the replacement rule. |
| R3 | The hint case picks a state whose hint is null. | §5.3 fallback to the failed-read hint. |
