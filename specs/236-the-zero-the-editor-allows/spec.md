# Spec 236 — The zero the editor allows

**Issue**: #2361 (feature-level; no per-task issues) · **Branch**: `fix/2361-clamp01-zero-size-mismatch`
**Status**: Phase 1 — ready for review · **Lane**: autonomous (ADR-0144)
**ADRs**: ADR-0036 (smallest change; fix the bug you are fixing), ADR-0139 (new behaviour starts
red), ADR-0079 (Zod is the browser-side form contract), ADR-0144 (lane). Domain model: spec 004
FR-005 (`NormalizedSize` in `(0, 1]`). Neighbours: spec 149 (keyboard floor), spec 151 (typed entry,
§The zero-size question), spec 147 (the characterisation this spec moves).
**Latency budget**: N/A — authoring-time editor in `management-web`; not on the event→overlay path
(constitution §IV).
**No new ADR.** Every rule here is already decided (spec 004 FR-005, ADR-0079); this spec makes one
function agree with them.

---

## 1. Premise, re-verified 2026-09-24 at `develop` (fad9e216..c5fa1406)

| layer | bound on width / height | 0 |
|---|---|---|
| `src/OverlayDesigner/Domain/Overlay/NormalizedSize.cs:39-42` | `value is > 0m and <= 1m` | refused |
| `apps/shared/src/api/overlays.schema.ts:10-11` | `z.number().gt(0).max(1)` | refused |
| `apps/shared/src/ui/composites/OverlayGeometryFields.tsx:57-61` (typed entry, spec 151) | `normalized > 0 && normalized <= 1`, refused with a message | refused |
| `apps/shared/src/ui/composites/OverlayEditor.tsx:52-60` `clamp01` | `[0, 1]`; `NaN → 0` | **emitted** |

The disagreement holds. It is **narrower than the issue describes**, because two of its three
predicted routes have since been closed by other specs:

- **Keyboard (spec 149)** — `resizeSize` floors at `MIN_NORMALIZED_SIZE = 0.005` (`OverlayEditor.tsx:70,
  131-137`) before `clamp01` sees the value. Closed.
- **Typed entry (#2346 / spec 151)** — refuses `0` in the validator and commits through
  `handleGeometryCommit` (`:352-366`), which deliberately bypasses `clamp01`. Closed.
- **Drag / resize (the remaining route)** — `clamp01` is the *only* bound on the two size values in
  two places:
  - `emitNormalized` (`:259-272`), reached by `onDragStop` / `onResizeStop` → `emitGeometry`, and by
    every keyboard press;
  - `geometryFromPixels` (`:299-307`), the spec 151 live readout during a gesture.

  `onDragStop` passes the render-floored `pixelWidth` / `pixelHeight` (`Math.max(…, 24)` /
  `Math.max(…, 16)`), so a **move** cannot emit 0. `onResizeStop` passes `ref.offsetWidth` /
  `ref.offsetHeight` raw — a **resize** reporting `≤ 0` emits size `0`, and
  `OverlayEditorCharacterisation.test.tsx:156` pins exactly that (`expect(next.normalizedHeight).toBe(0)`).
  Whether a real pointer can drive `react-rnd` to `≤ 0` is **not established** (assumption A1); that
  the editor would emit it if it did is.

The issue's **second finding** (per-value bounds permit `x + width > 1`) is out of scope. Spec 151
already decided it deliberately (§The off-edge question, FR-012: accepted, with an advisory). This
spec does not touch cross-value bounds.

---

## 2. What kind of change this is

**Behaviour-changing, and it closes a gap between layers that already enforce the rule — it adds no
enforcement at any trust boundary.** The domain and the schema refuse a zero size today and continue
to, unchanged. What changes is one observable output: the value the editor hands to `onChange` (and
shows in the live readout) when a resize reports a non-positive or non-numeric size. Today that is
`0` — a value the editor's own save will refuse at the end of the session. After, it is a valid size.

So "red" (§5) means: **component-level assertions on the editor's emitted geometry fail against
current code**. No server, schema or e2e assertion changes colour; there is nothing to make red at
those layers, because they already hold.

---

## 3. User story

### US1 (P1) — A resize never hands the form a size it will refuse

An operator resizing a label in the overlay editor gets, at every point the editor reports geometry
(the live readout while resizing, and `onChange` on release), a width and height the save will
accept — so a label cannot be authored into a state that only fails at submit.

**Independent test**: render `OverlayEditor` with the `react-rnd` stub the characterisation suite
already uses; fire `onResizeStop` (and `onResize`) with a non-positive size; the emitted label parses
under `overlayLabelSchema`, and nothing domain-valid the operator did not touch is rewritten.

---

## 4. Requirements

- **FR-001** — The **size** values (`normalizedWidth`, `normalizedHeight`) the editor emits through
  `onChange` are always in `(0, 1]`: a candidate `≤ 0` or `NaN` becomes `MIN_NORMALIZED_SIZE`
  (`0.005`); a candidate `> 1` becomes `1`.
- **FR-002** — A size candidate already in `(0, 1]` is emitted **unchanged** — including values below
  `0.005`. The size clamp changes a value **only when the domain would refuse it**.
  *Why this matters:* `emitNormalized` also carries the untouched axes of every keyboard press. A
  label typed at width `0.3%` (valid under spec 151 FR-009 and the domain) must keep `0.003` when the
  operator nudges its `x`; a monotone `Math.max(v, 0.005)` would silently rewrite it — the exact
  failure spec 151 §3c exists to prevent. Likewise spec 149 finding 2a deliberately emits a positive
  size below the floor at a near-edge origin; the size clamp must not undo that and push the label
  off-canvas.
- **FR-003** — The live readout (`geometryFromPixels`) applies the same size clamp as FR-001/FR-002,
  so the readout never shows a size the release would not emit (spec 151 FR-013's invariant).
- **FR-004** — **Position** values (`normalizedX`, `normalizedY`) keep `clamp01`'s `[0, 1]` behaviour
  byte-for-byte, including `NaN → 0`. Position may legitimately be 0.
- **FR-005** — The keyboard path is unchanged: `nudgePosition` and `resizeSize` keep their bodies,
  outputs and announcements exactly (spec 149 FR-007/FR-008, findings 2a/2b).
- **FR-006** — Typed entry is unchanged (`handleGeometryCommit` still bypasses every clamp; spec 151
  FR-006/FR-009).
- **FR-007** — No change to `NormalizedSize.cs`, `overlays.schema.ts`, the Rnd render floors, or any
  cross-value (`x + width`) bound.

---

## 5. Acceptance scenarios

```gherkin
Feature: The editor's size output agrees with the schema and the domain

  # Happy path — the defect, closed (RED today)
  Scenario: A resize reporting a non-positive height emits the floor, not zero
    Given the editor renders the default label on the 800x450 canvas
    When onResizeStop reports offsetWidth 1600 and offsetHeight -100 at position (100, 50)
    Then onChange receives normalizedWidth 1 and normalizedHeight 0.005
    And the emitted label parses under overlayLabelSchema

  Scenario: A resize reporting exactly zero width emits the floor
    When onResizeStop reports offsetWidth 0
    Then onChange receives normalizedWidth 0.005

  Scenario: A non-numeric size never becomes zero
    When onResizeStop reports offsetHeight NaN
    Then onChange receives normalizedHeight 0.005

  Scenario: The live readout agrees with the release
    When onResize (in progress) reports offsetHeight -100
    Then the Height field reads "0.5" while the gesture is in flight

  # Conflict — what must NOT change (GREEN before and after; guards against the wrong fix)
  Scenario: A small but valid size survives a resize untouched
    When onResizeStop reports offsetWidth 1 on the 800px canvas
    Then onChange receives normalizedWidth 0.00125, not 0.005

  Scenario: An untouched small size survives a keyboard nudge
    Given a label with normalizedWidth 0.003
    When the operator presses ArrowRight on the focused label
    Then onChange receives normalizedWidth 0.003

  Scenario: Position still clamps to zero
    When onDragStop reports position (-50, 5000)
    Then onChange receives normalizedX 0 and normalizedY 1

  # Bad request — the layers that already refuse keep refusing (unchanged; existing tests)
  Scenario: A typed zero size is still refused with a message, never floored
    When the operator commits "0" in the Width field
    Then the field shows "Width must be greater than 0% and at most 100%." and nothing is emitted

  # Auth — N/A: a presentational component in apps/shared; no request, no scope, no token.
```

---

## 6. End-to-end test procedure

No browser e2e: jsdom and Playwright alike cannot reliably drive `react-rnd` to a `≤ 0` offset (A1),
and the spec 147/151 suites already establish the `react-rnd` stub as the harness for exactly this
mapping. The procedure is:

1. `pnpm --filter @smart-sentinel-eye/shared test -- OverlayEditor OverlayGeometryFields` on
   unchanged code — the §5 RED scenarios fail on the emitted value (`0` / readout `"0"`), the GREEN
   guards pass.
2. Apply the change; the same command is fully green with the new tests unmodified.
3. Counterfactual (T005): substitute a monotone `Math.max(v, MIN_NORMALIZED_SIZE)` for the size clamp
   — the two FR-002 guards must go red. This proves they discriminate the right fix from the tempting
   one.
4. Full shared suite, `pnpm -r lint`, `pnpm -r typecheck` green.

---

## 7. Assumptions

- **A1** — Whether a real mouse resize can make `react-rnd` report `≤ 0` is unknown; the fix does not
  depend on it. The clamp is the guarantee, not the pixel floors.
- **A2** — The substitute for an invalid size is `0.005`, the constant spec 149 already defines as the
  editor's minimum size (`MIN_NORMALIZED_SIZE`), rather than a new epsilon. See plan §2.
- **A3** — The substitution is deliberately non-monotone at 0 (a reported `0.00125` stays; a reported
  `0` becomes `0.005`). Every monotone floor either rewrites domain-valid values (FR-002) or chooses an
  arbitrary sub-pixel epsilon; this one changes nothing the domain accepts.
