# Plan — Spec 146, one label renderer

**Spec:** `specs/146-one-label-renderer/spec.md`
**Issue:** #2339 · **Branch:** `fix/2339-one-label-renderer` · **Engineer:**
`frontend-engineer` · **Lane:** autonomous (ADR-0144)

---

## Bounded context and layers

**None, and that is the architectural statement.** This change lives wholly inside
`apps/shared/src/ui/composites/` plus one test in `apps/management-web`. No `src/` file is
touched, no bounded context gains or loses anything, no `Shared.Contracts` message changes,
no migration, no Aspire resource, no NetArchTest rule is engaged — those rules bind C#
project references and there is no C# in this diff.

**The boundary check for this PR is therefore the diff's file list.** Anything under `src/`,
`deploy/` or `tests/` is a defect. The permitted set is named in `tasks.md` and is six files.

No entities, no value objects, no invariants, no domain events, no integration events. The
only invariant worth naming is presentational, and it is the point of the feature:

> **A label's surface and type treatment has exactly one definition.** Two components paint
> the same label; the properties that describe *how it looks* are read from one place, and
> the properties that describe *where it is* and *what it responds to* stay with whichever
> surface owns them.

---

## The module

**`apps/shared/src/ui/composites/overlayLabelStyle.ts`** — new, and the only new source file.

```ts
import type { CSSProperties } from 'react';

export interface OverlayLabelAppearance {
  fontSizePx: number;
}

export function overlayLabelSurfaceStyle(label: OverlayLabelAppearance): CSSProperties;
```

Three decisions in that signature, each with a reason.

**`composites/`, not `primitives/`.** `apps/shared/src/ui/primitives/index.ts` documents
itself as *"Each primitive wraps a Radix headless component (ADR-0077)"*; this wraps nothing
and renders nothing. Both of its consumers are in `composites/`, the barrel at
`ui/primitives/index.ts` is a stub (`export {}`) that nothing re-exports through, and every
existing consumer deep-imports. Colocating keeps the import path short and the convention
honest.

**A function returning `CSSProperties`, not a component.** `OverlayEditor` hands its style to
`<Rnd style={...}>` (`OverlayEditor.tsx:82`), which takes an object and cannot host a wrapper
component. A shared *component* would be usable by exactly one of the two call sites — the
failure being fixed. This is stated in the spec and repeated here because it is the kind of
decision a reviewer will otherwise second-guess.

**Its input is `{ fontSizePx }`, not the label.** Seven of the eight returned properties are
constants; only `fontSize` reads the label. A minimal structural parameter type means both
`CameraViewerOverlay` (`CameraViewer.tsx:35-42`) and `OverlayLabel`
(`overlays.api.ts:9-16`) are assignable with **no import churn and no type fold**, which
keeps this diff to presentation. Taking the object rather than a bare `number` keeps the call
sites readable (`overlayLabelSurfaceStyle(overlay)`) and makes the function's domain explicit.

**Do not name the export `OverlayLabel`.** That identifier is already taken twice — the local
component at `CameraViewer.tsx:392` and the interface at `overlays.api.ts:9`. A third would
make every import ambiguous.

### What it returns — the wall's eight, verbatim

```
display: 'flex'
alignItems: 'center'
justifyContent: 'center'
background: 'rgba(255, 255, 255, 0.85)'
color: '#111827'
fontSize: `clamp(${Math.min(12, fontSizePx / 4)}px, ${fontSizePx / 16}vw, ${fontSizePx}px)`
fontWeight: 600
padding: '0 4px'
```

**The wall's values win, unchanged, including the `vw` term** — see the spec's decision
section. The editor adopts them; the wall does not move.

### What stays where it is

| Property | Stays in | Why |
|---|---|---|
| `position`, `left`, `top`, `width`, `height` | `CameraViewer.OverlayLabel` | Percentage placement; one caller |
| `pointerEvents: 'none'` | `CameraViewer.OverlayLabel` | The wall is not interactive |
| `cursor: 'move'`, `userSelect: 'none'` | `OverlayEditor` | The editor *is* interactive — the only difference that should exist |
| `Math.max(..., 24)` / `Math.max(..., 16)` px floors | `OverlayEditor:45-46` | Keeps the react-rnd drag target grabbable. Deliberately kept; see the spec |
| `border: '1px solid rgba(17, 24, 39, 0.4)'` | **deleted** | Divergence 3. The wall has no border and the editor must stop claiming one |

### The §IV constraint on the engineer

The function is called **once per label per tile**, up to **250 times per wall render**, on
the composite + render leg (≤ 50 ms). It must be **pure, allocation-only and DOM-free**.
Specifically forbidden, and the reviewer is asked to check each:

- no `getComputedStyle`, `getBoundingClientRect` or any other DOM read;
- no `ResizeObserver`, `MutationObserver` or effect;
- no module-level memo cache keyed on a label object (an unbounded `Map` keyed on values
  that change every publish is a leak, not an optimisation);
- no new DOM node and no new CSS feature — in particular **no `container-type`**, which is
  the follow-up issue's business and carries a layout cost across 250 tiles.

One non-inlined call plus one object literal per label, replacing the object literal that is
allocated inline today. `plan` asserts the leg is unchanged; nothing here needs measuring.

---

## Phase 4a — two colours, on two artefacts, in this order

**Declared: CHARACTERISATION (observed green) for the wall, then RED (observed failing) for
the parity guard.** ADR-0144 assumes one colour per issue; spec 144 is the precedent for an
issue that genuinely has two, and this is the same shape with the halves swapped.

Constitution §Testing gives two obligations. This change triggers both, on different files:

### The wall — behaviour-preserving, so CHARACTERISATION

`CameraViewer.OverlayLabel` must paint **exactly** what it paints today. Every property, the
`vw` term included. There is nothing to fail, so a red test there would be meaningless; the
proof is the opposite kind — covering tests captured **green before** the change and passing
**unmodified after**. An assertion that has to be edited is evidence the wall moved: block,
do not adjust.

**The path has almost no cover today.** `apps/management-web/.../CameraViewer.test.tsx:51-52`
asserts `left` and `top`; `fontSize`, `background`, `padding` and `border` are untested at
every level. *"A refactor with no covering test is a rewrite"* — so the characterisation is
written first, while the old shape still compiles, and that is T002.

### The editor — behaviour-changing, so RED

`OverlayEditor`'s preview **must start matching**. Its background, padding, border and font
formula all change. That is new observable behaviour by any reading, and ADR-0144 resolves
ambiguity to red regardless. The red is T003 and its failure output is quoted in the PR body
(ADR-0139).

### The trap in the red, and why the guard must not import the new module

A parity test written as *"both components equal `overlayLabelSurfaceStyle(label)`"* cannot
be observed red: on today's tree the module does not exist and the run fails to resolve an
import. A module-resolution error is not the defect, and quoting it in the PR would be
evidence of nothing.

**So the parity guard compares the two rendered components to each other and never imports
the new module.** It compiles and runs on `origin/develop`, and it fails there with a real
value mismatch — `expected 'rgba(255, 255, 255, 0.92)' to be 'rgba(255, 255, 255, 0.85)'` —
which is the quotable artefact. It is also the better long-term guard: it keeps failing if
someone re-inlines a value, which a test written against the module would not catch.

### The trap in the characterisation, and it will bite

**Do not assert the whole `style` attribute as one string.** React serialises inline styles
in object key-insertion order, and the fold changes that order (the shared properties arrive
via a spread). A byte-exact attribute assertion would go red on a pure reorder — a false
red on a characterisation test, and exactly the pressure that tempts an engineer to edit
the assertion rather than block.

**Assert per property** — `label.style.background`, `label.style.fontSize`, … — which is
order-independent and is already the repo's only precedent for a style assertion
(`CameraViewer.test.tsx:51-52`).

### The falsifiable statement of "done"

> The new parity test fails on `origin/develop` with an observed `background` mismatch of
> `0.92` against `0.85`, and passes on this branch; the wall's characterisation test is
> observed green on `origin/develop` and passes on this branch **with no assertion edited**;
> and `apps/management-web/src/features/cameras/CameraViewer.test.tsx` passes unmodified.

Not "done when it typechecks". Not "done when the editor looks right".

---

## Test harness — the facts the engineer needs before writing a line

Read before write. These were checked, not assumed.

- **`apps/shared/vitest.config.ts` sets `environment: 'node'` and has no `setupFiles`.** So
  there are **no `@testing-library/jest-dom` matchers** in `apps/shared` — no `toHaveStyle`,
  no `toBeInTheDocument`, no `toHaveTextContent`. Use `expect(el.style.background).toBe(...)`.
  `apps/kiosk-web` and `apps/management-web` do load jest-dom; `apps/shared` does not, and
  **this plan does not add it** (a setup file is a repo-wide change riding along in a fold).
- **A DOM is opted into per file** with `// @vitest-environment jsdom` on line 1 — the
  pattern at `apps/shared/src/ui/composites/CameraViewer.test.tsx:1`. Any file that renders
  needs it. `overlayLabelStyle.test.ts` returns a plain object and needs **no** pragma.
- **jsdom preserves `clamp()` verbatim.** Verified on this tree, jsdom 30.0.1:
  `el.style.fontSize = 'clamp(12px, 3vw, 48px)'` reads back identically. It does **not**
  resolve `vw` — which is fine, because the assertion is on the specified value, not a
  resolved pixel count.
- **jsdom normalises `padding: '0 4px'` to `'0px 4px'`.** Assert the normalised form; the
  authored string will not match.
- **Rendering `CameraViewer` in `apps/shared` already works** — mirror the mock setup in
  `apps/shared/src/ui/composites/CameraViewer.test.tsx` rather than inventing one. The label
  renders whenever the `overlay` prop is set, independent of stream status
  (`CameraViewer.tsx:355`), so no session needs to reach `live`.
- **`OverlayEditor`'s Rnd box carries the style; the `overlay-editor-preview` testid is on
  the inner `<span>`.** The parity test needs a handle on the outer box. Preferred:
  `screen.getByTestId('overlay-editor-preview').parentElement`. If react-rnd's nesting makes
  that brittle, add `data-testid="overlay-editor-label"` to the `<Rnd>` — a test seam, and
  the editor is the side that is changing anyway. **Establish which works before writing the
  red, and say which in the PR.**

---

## Sequence, and the one coupling that matters

```
T001 baseline ──┬─→ T002 [P] characterisation (wall, GREEN) ──┐
                └─→ T003 [P] parity guard (RED, quoted)     ──┤
                                                              ▼
                                        T004 overlayLabelStyle.ts
                                                              │
                                          ┌───────────────────┴───────────────────┐
                                          ▼                                       ▼
                          T005 [P] fold CameraViewer                T006 [P] fold OverlayEditor
                                          └───────────────────┬───────────────────┘
                                                              ▼
                                    T007 module unit tests (bounds 8 / 256)
                                                              ▼
                                    T008 both colours re-observed, unmodified
                                                              ▼
                                    T009 [P] US2 single-source grep · T010 [P] lint/typecheck/suites
```

**The coupling: T002 and T003 must both be run against `origin/develop`, before T004
exists.** T002's green and T003's red are the two pieces of evidence this PR is built on,
and neither can be reconstructed afterwards. If T004 lands first, both are lost and the
phase-4 gate is not satisfiable. This is the one ordering that cannot be rearranged for
convenience.

`[P]` per ADR-0109 marks disjoint files only. T002 and T003 are two new files; T005 and T006
are two different components; T009 and T010 read and run, respectively.

---

## Files, and nothing else

| File | Change |
|---|---|
| `apps/shared/src/ui/composites/overlayLabelStyle.ts` | **new** — the module |
| `apps/shared/src/ui/composites/overlayLabelStyle.test.ts` | **new** — unit, node env |
| `apps/shared/src/ui/composites/OverlayLabelParity.test.tsx` | **new** — the red guard, jsdom |
| `apps/shared/src/ui/composites/OverlayLabelCharacterisation.test.tsx` | **new** — the wall's green, jsdom |
| `apps/shared/src/ui/composites/CameraViewer.tsx` | `OverlayLabel`'s style object spreads the shared eight |
| `apps/shared/src/ui/composites/OverlayEditor.tsx` | Rnd's style object spreads the shared eight; `border` deleted; possibly one testid |

Six files. `apps/management-web/src/features/cameras/CameraViewer.test.tsx` is **read and
re-run, not edited** — it is part of the characterisation evidence.

---

## Risks

| # | Risk | Mitigation |
|---|---|---|
| 1 | A reviewer reads "the editor changed" as the wall having changed, since both import one function. | T002's characterisation is the answer, and the PR body carries it. The wall's eight values are the ones that survive; the diff shows the editor's four moving to meet them. |
| 2 | The engineer "improves" a value while moving it — e.g. rounds `0.85` to `0.85` but drops the `vw` term as ugly. | T002 goes red. Block, do not adjust (§Testing). The spec's decision section explains why the `vw` term is preserved despite being wrong. |
| 3 | The parity test is written against the new module and cannot be observed red. | Called out above and repeated in T003's task text. The guard compares components to each other. |
| 4 | The characterisation asserts the whole `style` attribute and goes red on React's key order. | Called out above and repeated in T002's task text. Per-property assertions only. |
| 5 | react-rnd's DOM nesting makes the editor's style node hard to address, and the engineer reaches for a snapshot instead. | The stated fallback is a `data-testid` on the `<Rnd>`. A snapshot would pass on today's divergence and is not a guard. |
| 6 | Scope creep into tokens, because the module is visibly the right place for them. | #2342 is gated on a token system that does not exist. US-2 asserts the module adds **no** custom property. |
| 7 | Scope creep into `cqw`, because the spec argues it is correct. | It is a behaviour change over published data and it belongs to its own issue, blocked on #2337's render-budget gate. The reviewer fails the PR on any `container-type`. |

---

## Phase hand-off

Phase 3's artefact is `tasks.md`. The gate is: tasks atomic, and **#2339 on Project #13**.
**Both halves hold.** The board was queried, not assumed: #2339 is item-listed on Project
#13 at status **In Progress**. Feature granularity; no per-task issues (the practice since
spec 028), so `/speckit-taskstoissues` is deliberately not run.

**Two things the orchestrator owes this spec that are not tasks:**

1. **File the follow-up issue** the spec recommends — tile-relative overlay type — blocked
   on this PR and on #2337.
2. **Correct the record on #2339.** Its font-size paragraph is arithmetically wrong and this
   programme has eleven more issues behind it. A comment carrying the resolved-size table is
   cheaper now than a later spec re-deriving it.
