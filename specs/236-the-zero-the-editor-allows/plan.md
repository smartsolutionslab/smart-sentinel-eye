# Plan 236 — The zero the editor allows

**Spec**: [spec.md](./spec.md) · **Issue**: #2361 · **Engineer**: `frontend-engineer`

## 1. Scope and placement

One file of production code: `apps/shared/src/ui/composites/OverlayEditor.tsx`. No bounded context,
no backend, no `Shared.Contracts`, no AppHost, no migration. The domain rule (`NormalizedSize`,
OverlayDesigner) and its browser mirror (`overlays.schema.ts`) are the authorities and are not
edited; this change makes the editor agree with them.

## 2. Decision — split the clamp, additively

**Chosen: keep `clamp01` as the position clamp (`[0, 1]`, untouched) and add a `clampSize` for the
two size values.** Not a single floored `clamp01`.

Why not a single floored `clamp01`:

1. `clamp01` also clamps **position**, where `0` is correct (`NormalizedPosition` is `[0, 1]`).
   Flooring it would move every label dragged to the top-left edge off the edge — a new defect.
2. `nudgePosition` calls `clamp01` on a position; changing it would change spec 149's pinned
   keyboard output.

Why the split is additive rather than a rename: `clamp01`'s name is accurate for what it still does
(clamp to `[0, 1]`), and `resizeSize` calls it on a size *before* its own floor — renaming it to
`clampPosition` would make that call read wrong, and switching `resizeSize` to `clampSize` is
output-identical churn (every stepped value `< 0.005` ends at `0.005` either way). ADR-0036: leave
`nudgePosition`/`resizeSize` byte-for-byte.

This mirrors the domain's own split (`NormalizedPosition` vs `NormalizedSize` — the doc comment on
`NormalizedSize.cs` says the zero refusal is "the whole reason this is a separate type").

### The function

```ts
function clampSize(value: number): number {
  if (!(value > 0)) return MIN_NORMALIZED_SIZE; // 0, negatives, NaN
  if (value > MAX_NORMALIZED) return MAX_NORMALIZED;
  return value;
}
```

`!(value > 0)` catches `NaN` in the same comparison — the shape `OverlayGeometryFields.tsx:59`
already uses for the same bound. Placed after the `MIN_NORMALIZED_SIZE` declaration (it references
it; `const` in TDZ is fine at call time, but reading order should match).

### The floor value — reuse `MIN_NORMALIZED_SIZE` (`0.005`)

- It already exists in this file as "the server's size floor" for the keyboard path (spec 149
  FR-008). A second constant would give the editor two minimum sizes.
- It is 4 px × 2.25 px on the default canvas — visible and re-grabbable, unlike a sub-pixel epsilon.
- It is on the `1e-4` quantum grid, so a floored value round-trips through the Width/Height fields
  (`toPercentText` → `"0.5"`).

**It is a substitute, not a floor** (spec FR-002): values in `(0, 0.005)` pass through. Reason:
`emitNormalized` re-clamps all four values on every keyboard press, including the untouched axes, so
a monotone `Math.max(v, 0.005)` would rewrite a typed `0.3%` width on an `x` nudge, and would undo
spec 149 finding 2a's deliberate below-floor size at a near-edge origin — pushing the label past the
canvas edge.

## 3. Call-site changes (four lines, plus comments)

| site | line (today) | before | after |
|---|---|---|---|
| `emitNormalized` | `:266-267` | `clamp01(nextWidth)`, `clamp01(nextHeight)` | `clampSize(…)` |
| `geometryFromPixels` | `:304-305` | `clamp01(widthPx / canvasWidthPx)`, `clamp01(heightPx / canvasHeightPx)` | `clampSize(…)` |

`x`/`y` at `:264-265` and `:302-303` stay `clamp01`.

Comments that become false and must be corrected (not new commentary):
- `:254-258` — "`clamp01` is the one clamp both the drag and keyboard paths call … a no-op on them":
  now two clamps; `clampSize` is likewise a no-op on keyboard output (always `> 0`).
- `:358-359` — "It would also let a typed `0` reach `clamp01`'s own zero": `clampSize` has no zero;
  the bypass is still justified by the first reason (no silent rewrite of untouched fields). Trim the
  second sentence.
- JSDoc on `OverlayEditor` (`:~235`) — "All four normalized values are clamped to [0, 1]": position
  to `[0, 1]`, size to `(0, 1]`.

## 4. Tests (phase 4a — RED)

| # | file | test | colour on current code |
|---|---|---|---|
| a | `OverlayEditorCharacterisation.test.tsx:156` | resize-stop assertion moves `toBe(0)` → `toBe(0.005)`; rename the test from "clamped [0,1]" to name the size bound; comment at `:154-155` updated | **RED** (`received 0`) |
| b | same file, new test after it | `onResizeStop` offsetWidth `0` → width `0.005`; emitted label `overlayLabelSchema.safeParse(...).success === true` | **RED** |
| c | same file | `onResizeStop` offsetHeight `NaN` → height `0.005` | **RED** |
| d | same file | `onResizeStop` offsetWidth `1` → width `1 / 800` (`toBe`, exact) | GREEN guard |
| e | `OverlayEditorKeyboard.test.tsx` | label width `0.003`; `ArrowRight` → emitted width `0.003` | GREEN guard |
| f | `OverlayGeometryFields.test.tsx`, "wires the live readout" section, via `ControlledOverlayEditor` | `onResize` with offsetHeight `-100` → Height field value `"0.5"` | **RED** (`"0"`) |

Moving assertion (a) is **not** a characterisation edit to reach green: it is the behaviour this issue
exists to change, and spec 151 §The zero-size question named `:156` as "the assertion to move when it
is fixed". The PR quotes its red output.

Guards d and e arrive green by design; T005's counterfactual (a monotone floor) must turn them red,
or they are not guarding anything.

## 5. Boundaries, invariants, messaging

- Invariant enforced: every size the editor emits satisfies `NormalizedSize.From` / `overlayLabelSchema`.
- No domain or integration events; no messaging; no cross-context references. N/A.
- Constitution §II (value objects) does not apply: `apps/shared` TypeScript, outside the C# domain.

## 6. Risks

- **Undo history (spec 154)**: `commit` receives the clamped payload, so a floored resize records
  `0.005` as the step — correct, and no test pins `0` there (checked: only `:156` pins a zero size).
- **Parity (`OverlayLabelParity.test.tsx`)**: renders stored values, not emitted ones; unaffected.
