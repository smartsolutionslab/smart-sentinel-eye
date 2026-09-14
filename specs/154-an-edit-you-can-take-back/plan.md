# Plan 154 — An edit you can take back

**Spec:** `specs/154-an-edit-you-can-take-back/spec.md`
**Issue:** #2347 **Branch:** `feat/2347-undo-in-the-overlay-editor`
**Phase 4a colour:** **red** (new behaviour).
**Engineer:** `frontend-engineer`. No backend agent is needed — no file under
`src/` is touched.

---

## 1. Bounded context and layers

**No bounded context is involved.** This is frontend-only, entirely inside
one React component tree, and it crosses no service boundary.

- No file under `src/` changes. No `Shared.Contracts` message, no domain
  event, no endpoint, no migration. Undo is session-local state that never
  reaches the wire.
- The boundary rules NetArchTest enforces are therefore not engaged, and the
  constitution §III cross-context prohibition has nothing to bite on.
- The layer that *is* involved is the frontend's own boundary, and it has a
  rule of its own: **`apps/shared` composites do not assume a Redux store**
  (spec 150 FR-018, pinned by `OverlayEditorKeyboard.test.tsx:693`). This
  plan honours it — see §4.

### Files

| File | Status | Owner |
|---|---|---|
| `apps/shared/src/ui/composites/useOverlayEditHistory.ts` | **new** | the hook, §2 |
| `apps/shared/src/ui/composites/useOverlayEditHistory.test.ts` | **new** | unit tests for the hook in isolation |
| `apps/shared/src/ui/composites/OverlayEditor.tsx` | modified | wiring, §3 |
| `apps/shared/src/ui/composites/OverlayEditorUndo.test.tsx` | **new** | the editor-level behaviour suite |
| `e2e/overlays.spec.ts` | modified (appended) | one end-to-end test, §7 |

**Not modified, by design:** `OverlayEditorDialog.tsx` (spec FR-016),
`OverlayGeometryFields.tsx`, `BackdropControls.tsx`, `overlayLabelStyle.ts`,
`normalizedPercent.ts`, and every one of the seven guard suites.

**`apps/management-web/src/features/layouts/**` is not touched** — PR #2373 is
open there.

### On file size

`OverlayEditor.tsx` is 602 lines and this adds to it. **No lint rule binds** —
there is no `max-lines` in any eslint config under `apps/`, and ADR-0084's
300-LOC cap is a SonarAnalyzer rule that applies to C# only. The judgement is
readability, and it is why the history is a separate module rather than
another 120 lines of stacks and refs in the middle of a file that already
holds a keyboard map, a capture session, an announcer and five emission
sites. Expected net addition to `OverlayEditor.tsx`: roughly 60 lines —
handler wrapping, two controls, one live region.

---

## 2. The model — entities, value objects, invariants

There are no domain entities here; the equivalent artefacts are the hook's
state shape and its invariants. They are written in the same spirit.

### `useOverlayEditHistory`

```
useOverlayEditHistory(value: OverlayLabel, onChange: (next: OverlayLabel) => void)
  -> {
       commit: (next: OverlayLabel, boundary: Boundary) => void,
       endRun: (key?: RunKey) => void,
       undo: () => boolean,
       redo: () => boolean,
       canUndo: boolean,
       canRedo: boolean,
     }
```

`Boundary` is either `'atomic'` or `{ run: RunKey }`.

`RunKey` is a string, and its spelling is the whole coalescing design:

| Site | Key |
|---|---|
| text input | `'text'` |
| font-size slider | `'fontSize'` |
| arrow nudge | `` `arrow:${'move'\|'resize'}:${'x'\|'y'\|'width'\|'height'}` `` |

### State

- `past: OverlayLabel[]` — the snapshot taken *before* each step. `past` is
  empty exactly when the current value is the floor.
- `future: OverlayLabel[]` — snapshots taken from `past`'s tip on undo.
- `openRun: RunKey | null` — the run currently absorbing emissions.
- `lastEmittedRef: OverlayLabel | null` — the object the hook last handed to
  `onChange`, used only for identity comparison.
- `idleTimerRef` — one timer, for the two idle-bounded run kinds.

### Invariants

1. **I1 — the floor is the absence of a predecessor.** `canUndo === past.length > 0`.
   Nothing checks "is this the opened state"; there is simply nothing below
   it. This is what makes spec Decision 5 true by construction rather than by
   a guard someone could forget.
2. **I2 — an undo is not a step.** `undo()` moves a snapshot from `past` to
   `future` and emits it; it never pushes onto `past` and never clears
   `future`. Same, mirrored, for `redo()`.
3. **I3 — a new step clears the future.** Any `commit` that opens a step
   empties `future`. A `commit` absorbed into an open run does not — it is
   the same step, and the future was already cleared when the run opened.
4. **I4 — a run absorbs, it does not stack.** While `openRun === key`, a
   `commit` with that key emits the new value and pushes **nothing**. The
   `past` entry recorded when the run opened is the pre-run value, so one
   undo reverts the whole run.
5. **I5 — a foreign commit closes an open run.** Any `commit` whose boundary
   is `'atomic'`, or carries a different `RunKey`, first closes the open run,
   then does its own work. This is what keeps "type, then drag, then undo"
   from merging two unrelated things.
6. **I6 — a value the hook did not emit is a new session.** On render, if
   `value !== lastEmittedRef.current`, then `past = []`, `future = []`,
   `openRun = null`, `lastEmittedRef = value`. This is FR-011, and §5
   explains why it must be identity and nothing else.
7. **I7 — only the six label fields move.** The hook's type parameter is
   `OverlayLabel`. Backdrop state is not reachable from it.
8. **I8 — the timer is cleaned up on unmount.** Mirrors the existing
   `announceTimerRef` cleanup at `OverlayEditor.tsx:374-381`.

### The idle constant

`TEXT_IDLE_MS = 500`, defined in `useOverlayEditHistory.ts`.

Chosen over `DEBOUNCE_MS = 250` (`useDebouncedValue.ts:11`) deliberately.
`DEBOUNCE_MS` answers "has the operator stopped typing *enough to spend a
request*" — it is tuned to be cheap to be wrong about. This answers "was that
one thought or two", where being wrong costs the operator a lost edit or an
undo that does too little. `ANNOUNCE_DELAY_MS = 500`
(`OverlayEditor.tsx:67`) is the constant this file already uses for exactly
that judgement — "a burst of input has ended" — so the editor stays
internally consistent.

It is **not** imported from `OverlayEditor.tsx`: that would be a circular
import, the same one `OverlayGeometryFields.tsx:34-37` documents having hit
with the axis labels. Restated with a comment pointing at its twin, as that
file does.

---

## 3. Wiring in `OverlayEditor.tsx`

Six call sites, each a one-line change at the point of emission.

| # | Existing | Becomes |
|---|---|---|
| 1 | `handleDragStop` → `emitGeometry(...)` | `emitGeometry` routes through `commit(next, 'atomic')` |
| 2 | `handleResizeStop` → `emitGeometry(...)` | same |
| 3 | `handleGeometryCommit` → `onChange({...value, [field]: normalized})` | `commit(next, 'atomic')` |
| 4 | text `onChange` (`:546`) | `commit({...value, text}, { run: 'text' })`; `onBlur` → `endRun('text')` |
| 5 | range `onChange` (`:556`) | `commit({...value, fontSizePx}, { run: 'fontSize' })`; `onBlur`/`onPointerUp` → `endRun('fontSize')` |
| 6 | `handleLabelKeyDown` → `emitNormalized(...)` (`:428`) | `commit(next, { run: arrowRunKey(axis, resizing) })` |

Plus three new pieces:

- **`onKeyUp` on the `<Rnd>` label** — if the released key is an arrow,
  `endRun()`. Also `endRun()` from the existing `onBlur` at `:509`, which
  already exists for the focus ring.
- **`onKeyDown` on the editor's root `<div>`** — the `Ctrl/Cmd+Z` /
  `Ctrl/Cmd+Shift+Z` / `Ctrl+Y` map. Root, not the label, so the bindings work
  from any focus position inside the editor (FR-007). `preventDefault` on a
  handled combination; everything else falls through untouched, including
  `Ctrl+C`, `Ctrl+V` and the browser's own shortcuts.
- **Two controls and one live region**, §6.

### The one shared emit path

`emitNormalized` (`:243-256`) is already described in its own comment as *"the
single place that builds the `onChange` payload"* for the drag and keyboard
paths. It keeps that role and gains a boundary argument; `handleGeometryCommit`
and the two input handlers call `commit` directly, as they call `onChange`
directly today. Six sites, one hook, no new indirection.

### Ordering hazard, called out

`handleLabelKeyDown` reads `value` from props and computes the next value from
it. Under key repeat, React batches, and the component re-renders between
presses with the emitted value — this already works today and is pinned by the
160-press test. `commit` must not change that: it emits synchronously through
`onChange` exactly as `emitNormalized` does now, and does its bookkeeping
around the emission, never instead of it. **If a task proposes making the
emission async, deferred, or batched, it is wrong** and
`OverlayEditorKeyboard.test.tsx` will say so.

---

## 4. Messaging — domain event to integration event

**None.** No domain event, no integration event, no `Shared.Contracts`
message, no Wolverine handler, no outbox row. Undo produces no observable
effect outside the component until the operator clicks Save, at which point
the existing create/edit mutation fires with the label it already would have
fired with.

The one thing worth stating, because it is the kind of thing that gets added
by reflex: **undo does not emit an audit event.** An editing-session keystroke
is not an auditable act; the auditable act is the PATCH, and it is already
audited.

## 4b. Boundary rules

- **No cross-context references.** None are possible — nothing under `src/`
  changes.
- **No Redux.** `useOverlayEditHistory` imports from `react` and from
  `overlays.api`'s `OverlayLabel` *type* only. No `react-redux`, no
  `@reduxjs/toolkit`, no store, no selector. `OverlayEditorKeyboard.test.tsx:693`
  is the enforcement.
- **No new dependency.** Not `use-undo`, not `immer`, not a Redux undo
  middleware. Two arrays and a ref.
- **`apps/shared` stays app-agnostic.** The hook and the editor must contain
  nothing that assumes `management-web`; the file is in the package both apps
  consume.

---

## 5. The re-seed detector — the part most likely to be built wrong

This is FR-011 / invariant I6, and it is the single implementation detail with
a track record in this repo (#2341, #2364, #2368 were all "the wrong field
changed identity at the wrong time").

**The rule (corrected post-phase-6 — see below): echo-tolerant identity.**
Compare the incoming `value` prop against the object the hook last emitted —
by reference **or** by structural equality of `OverlayLabel`'s six flat
fields — never against anything else.

```
isEcho(value, lastEmitted)   ->  our own echo; do nothing
!isEcho(value, lastEmitted)  ->  external re-seed; clear both stacks,
                                  close any run, adopt value as floor
```

**Corrected, phase 6 review, spec 154 delivery.** The paragraph this replaced
asserted reference identity alone was sufficient because "RHF's `Controller`
hands `field.value` straight from form state without cloning on render." That
is false, measured against the installed `react-hook-form@7.86.0`:
`useController` (which `Controller` calls internally) sources `field.value`
through `useWatch`, and `useWatch` re-derives its return value via
`generateWatchOutput` on **every** form-state notification — deep-equal to
what was emitted, but a new object, never the same reference. A reference-only
detector reads every one of the `Controller`'s own echoes as an external
re-seed and clears both stacks the render after every single edit — undo has
never worked inside `OverlayEditorDialog`, the only place it ships, and no
test caught it because every test suite rendered `OverlayEditor` either bare
(a literal built once) or behind a hand-rolled `useState` passthrough that
hands the hook's own object straight back — neither exercises the production
wiring. `OverlayEditorReseedRegression.test.tsx` (`apps/management-web`,
added at delivery) renders the real `Controller`/`useWatch` tree and is what
found this. This file is worth re-reading skeptically wherever else it
asserts a third-party library's mechanism rather than measuring it.

**Four implementations that look equivalent and are wrong:**

1. **A `useEffect` keyed on a prop other than `value`.** `OverlayEditor`
   receives `resolvedPreview`, `isResolving` and `resolveFailed`, all derived
   from `useResolveOverlayTextQuery`'s **`currentData`**
   (`OverlayEditorDialog.tsx:166`, used at `:178`). `currentData` is a fresh
   object every time the query settles. Any dependency array containing it
   clears the history the moment a `{{placeholder}}` resolves.
2. **A deep-equality check against anything other than `lastEmitted`** — in
   particular, against the previous render's `value` (which is really just
   "clearing on re-render" wearing a structural-comparison disguise, item 4
   below) or general deep equality with no reference short-circuit at all.
   The comparison target matters as much as the comparison method: compared
   against `lastEmitted`, an undo that lands back on a value the operator
   visited before is recognized as an echo (`lastEmitted` was set to exactly
   that value by the undo itself) and correctly kept; compared against
   anything else, the same revisit can misread as a re-seed.
3. **Reference identity alone, with no structural fallback.** This is the
   error this section itself made — sound against a parent that never clones,
   unsound against one (RHF's `Controller`, and by extension any parent
   whose own render pipeline clones) that does. `OverlayEditorUndo.test.tsx`'s
   *"a new object with content identical to the last emission preserves the
   history"* test pins the fix directly: a `rerender` with a fresh object
   carrying the exact fields of the hook's last emission must **not** read as
   a re-seed.
4. **Clearing on re-render.** The dialog re-renders on every `isFetching`
   flip, every `currentChain` arrival, and every keystroke through `useWatch`.

**Why echo-tolerant identity is sound here.** `OverlayLabel` is six flat
fields (Decision 3) — a field-by-field compare, not a general deep-equal
utility (there is nothing else in the domain to make general). One accepted
miss, stated once more here because it is easy to lose in a list of what the
check must catch: an external `reset()` to a label that happens to be
structurally identical to what this hook last emitted goes undetected as a
re-seed and the history is kept instead of cleared. Benign — the visible
content is the same either way.

**The fallback**, unchanged in shape from the original text but now the
documented outcome of a real failure rather than a hypothetical one: the
explicit `editSessionKey` prop from spec Decision 4 — optional, defaulted,
and requiring one line in `OverlayEditorDialog.tsx`, which would cost
FR-016. It was **not** taken for this defect — echo-tolerant identity fixes
it without touching `OverlayEditorDialog.tsx` at all, and the fallback would
still leave any *other* cloning parent broken, pushing the burden onto every
future call site instead of fixing it once, here.

---

## 6. The controls and the announcement

**Placement.** Immediately after the canvas `<div>` and the two `sr-only`
regions, **before** the `<div style={{display:'grid'}}>` that holds the text
and font-size inputs. `OverlayEditorKeyboard.test.tsx:103` requires the label
to precede the text input in DOM order; inserting between them preserves that,
and it puts the controls where the operator's eye already is.

**Markup.** A `<div>` with two `<button type="button">` — `type="button"`
is mandatory, the editor renders inside `OverlayEditorDialog`'s `<form>` and
a bare `<button>` would submit it. Labelled `Undo` and `Redo`, `disabled` from
`canUndo` / `canRedo`, each carrying `aria-keyshortcuts` and a `data-testid`.

**Styling.** The file's existing inline-style idiom, matching the controls
already in it. **Not** Tailwind and **not** design tokens — #2342 is gated on
#2332. Adopt the shared `Button` primitive only if it can be used without
pulling a token or Tailwind class into this file; otherwise inline, and say so
in the PR.

**The live region (US2).** A fourth `aria-live="polite"` `sr-only` region with
its own `data-testid`, following the convention `OverlayEditor.tsx:534-541`
documents for exactly this reason — three regions already share the tree and
`querySelector('[aria-live]')` takes whichever is first in the DOM. Three
strings: `Undone`, `Redone`, `Nothing to undo`. No debounce and no
zero-width-space toggle: undo is a discrete act, not a burst, so the
`ANNOUNCE_DELAY_MS` machinery at `:361-372` is not reused — but repeated
identical messages have the same React no-op problem that machinery exists to
solve, so the same toggle trick applies if a test shows it does.

---

## 7. Testing strategy

**Colour: red.** Every test in T004-T010 must be observed failing, and the
verbatim output goes in the PR body (ADR-0139).

**Three levels:**

1. **Hook unit tests** (`useOverlayEditHistory.test.ts`) — the invariants I1-I8
   in isolation, with `renderHook`. Fast, and they pin the coalescing algebra
   without a DOM.
2. **Editor behaviour tests** (`OverlayEditorUndo.test.tsx`) — the spec's US1
   and US2 scenarios through Testing Library, rendered bare with no
   `<Provider>` like the five existing suites. This is where the five emission
   sites are exercised for real. Fake timers for the idle rules;
   `fireEvent.keyDown` repeated without `keyUp` for the key-repeat run, which
   is how `OverlayEditorKeyboard.test.tsx:409` already models it.
3. **One e2e** appended to `e2e/overlays.spec.ts` — the feature observed in a
   browser against the real stack. One test, not six: drag, `Ctrl+Z`, assert
   the geometry field returns to its pre-drag value; then `Ctrl+Z` to the
   floor and assert the Undo button is disabled. The file already has an
   `operator edits a saved draft in place` test (`:122`) to model it on,
   including `FIRST_WRITE_TEST_TIMEOUT_MS`.

**The guard run is a task of its own** (T001), before any source change, and
repeated at the end. Seven suites, expected green both times, **unmodified**.

**Not tested, deliberately:** that `Ctrl+Z` "feels" right; that native input
undo is defeated (it is unreliable per-browser and asserting on it would pin
browser behaviour we do not control).

---

## 8. Alignment check

| Authority | Bearing | Status |
|---|---|---|
| Constitution §II (value objects) | C# domain models only | N/A — no C# |
| Constitution §III (context isolation) | no cross-context refs | N/A — nothing under `src/` |
| Constitution §IV (latency budget) | kiosk `event → overlay` path | **N/A, stated explicitly.** `OverlayEditor` is imported by `management-web` only. The composite+render leg is at p50 54.2 ms against 50 ms (ADR-0123); this adds nothing to it. |
| Constitution §VII (observability) | §IV's dashboard rule binds implemented legs | N/A — no leg touched |
| Constitution §Testing | red for new behaviour | **Red.** §7. |
| ADR-0074 | two React apps | followed — shared composite, one consumer |
| ADR-0075 | RTK Query for server state | followed by *not* using it; this is ephemeral component state |
| ADR-0077 | Radix + design system | followed conditionally, §6 |
| ADR-0079 | RHF + Zod | untouched — the `Controller` keeps owning `value` |
| ADR-0084 | 300 LOC / file | **does not bind** — SonarAnalyzer, C# only; no `max-lines` exists in TypeScript |
| ADR-0105 | `Ensure.That` guards | N/A — C# |
| ADR-0109 | `[P]` markers for disjoint files | applied in `tasks.md` |
| ADR-0113 | two-layer optimistic concurrency | untouched — undo never reads or writes the If-Match version |
| ADR-0123 | the render leg is the operator's wait | cited as the reason N/A is stated rather than assumed |
| ADR-0139 / ADR-0140 | rules that fail the build | the guard suites are the mechanism here |
| ADR-0142 / ADR-0143 | idempotency and retry | N/A — undo issues no request |
| ADR-0144 | autonomous lane | followed; no ADR authored, no gate weakened |
| ADR-0146 | one discipline, two surfaces | `management-web` is the arm's-length surface where this affordance belongs |
| spec 150 FR-018 | `apps/shared` composites are store-free | **honoured** — §4b. Candidate ADR, filed as an observation, not written here. |

**No new ADR is required to build this.** One is worth filing; spec.md says
which and why the lane does not write it.
