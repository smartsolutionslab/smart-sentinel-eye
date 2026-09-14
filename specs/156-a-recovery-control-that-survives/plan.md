# Plan — Spec 156, a recovery control that survives its own activation

**Phase:** 2 (Plan) — ADR-0037
**Spec:** `specs/156-a-recovery-control-that-survives/spec.md`
**ADRs:** ADR-0077 (Radix + custom design system), ADR-0074 (two apps),
ADR-0078 (Tailwind tokens via CSS custom properties), ADR-0113 / ADR-0119
(concurrency and refusal vocabulary — consumed, not changed), ADR-0109
(disjoint-file `[P]` marking), ADR-0144 (lane).

**Bounded context:** none. This is `apps/`, not `src/`. There is no domain
model, no aggregate, no value object, no domain event, no integration event, no
persistence and no message. The "no cross-context project references" rule and
the `Shared.Contracts` boundary are **not engaged** — nothing in this plan
crosses a context because nothing in it is in one. Recorded explicitly so the
absence reads as checked rather than forgotten.

**Latency budget (§IV): N/A** — `apps/management-web` plus one `apps/shared` UI
composite. No leg. See spec §Latency budget.

---

## 1. The shape of the change

Both dialogs today render one ternary with two `role="alert"` arms and one
control in each arm. The change replaces that ternary with a block that has
**four** states instead of two, and adds a fifth element that is always there.

| State | Predicate (edit mode) | Renders |
|---|---|---|
| Idle | read succeeded, no submit refused | nothing |
| Read failed | `chainFailed` | `role="alert"` + live recovery control |
| Re-reading | `chainFetching` | **same control**, `aria-disabled`; no alert |
| Submit refused | `backendError !== null` | `role="alert"` + optional Reload |
| — always — | — | `role="status"`, `sr-only`, text or `''` |

The one structural difference from today is the third row. Today it renders
nothing at all, which is the defect: the control the operator is standing on
disappears at the instant they press it (spec §1).

Priority between rows 2 and 4 is **unchanged** — the failed read still wins over
a standing `backendError`, for the reason the existing comment at
`OverlayEditorDialog.tsx:270-275` gives, and that comment moves with the code
rather than being rewritten. Row 3 joins row 2's side of the ternary: a re-read
in flight is part of the chain-read story, not the submit story.

## 2. Extract or duplicate — **extract**, with a named escape hatch

The block is ~25 lines in `OverlayEditorDialog.tsx` and ~22 in
`LayoutEditorDialog.tsx`, and after this change each grows a ref, an effect and a
de-duplicating announcer. Two hand-maintained copies of a focus-management effect
is precisely the mechanism that put the same defect in both files, and the issue
names anti-drift as its reason for existing.

**Decision: one composite, `ChainRecoveryNotice`, in
`apps/shared/src/ui/composites/`.** It owns rows 2–4 and the always-mounted
region; the dialogs own the query, the mutation and Save.

This is not speculative generality: there are two call sites today and the spec's
census names 14 more of the same shape. The component is built for the two, with
no prop that only a hypothetical third would need.

Props — seven, each one already a local in both dialogs:

| Prop | Source today (overlay / layout) |
|---|---|
| `noun` | `'overlay'` / `'layout'` — the only copy difference |
| `readFailed` | `chainFailed` (`:169` / `:88`) |
| `reReading` | `isFetching` from the chain query (layout has it as `chainFetching` `:89`; **overlay does not destructure it yet**) |
| `onReRead` | `refetchChain` |
| `backendError` | `:215` / `:237` |
| `offerReload` | `offerReload` `:228` / `staleConflict` `:236` |
| `onReadRecovered` | new — `() => saveRef.current?.focus()` |

**The escape hatch (spec NFR-002):** every test is written against the
**dialogs**, never against `ChainRecoveryNotice`. If extraction turns out awkward
in phase 4 — a prop count that trips ADR-0084's four-parameter limit is the
likely candidate, though that limit is a SonarAnalyzer C# rule and does not bind
TSX — the engineer may keep two copies and no test changes. The decision is the
plan's, the tests do not encode it.

> ADR-0084's metric limits are C#-side. The TSX equivalents here are the
> 300-LOC file ceiling as a matter of house habit: `OverlayEditorDialog.tsx` is
> 309 lines today and `LayoutEditorDialog.tsx` 424. Extraction moves both
> **down**, which is a second reason to prefer it over adding to them.

## 3. Component decomposition

### 3a. `apps/shared/src/ui/composites/ChainRecoveryNotice.tsx` — new

Four things, in this order in the DOM:

1. **The always-mounted status region.** `<p role="status" className="sr-only"
   data-testid={...}>{message}</p>`. Never conditional (#2346). A `data-testid`
   for the reason `OverlayEditor.tsx:631-638` records: `getByRole('status')` is
   fine here because these dialogs hold no other status node today, but the
   testid survives one being added.
2. **The chain-read arm.** Rendered when `readFailed || reReading`. The `<p
   role="alert">` is rendered only when `readFailed` — so during a re-read the
   arm holds the control and no alert, which is what keeps the alert's
   insertion-announcement working for the failure path (spec §The locked
   answers).
3. **The recovery control.** One `<button type="button">`, `aria-disabled={reReading}`,
   handler refuses when `reReading`. `type="button"` is mandatory — both dialogs
   render this inside a `<form>` and a bare button submits it
   (`OverlayEditor.tsx:645-648` learned this).
4. **The submit-refused arm.** Unchanged markup, moved. Reload keeps its own
   `role="alert"`, gets the announcement, gets no focus move (FR-008), and the
   reason is a comment at the handler.

**Announcement de-duplication (FR-009).** Two techniques exist in the repo. Pick
the **`key`-token remount** (`OverlayEditor.tsx:501-508`, `announceUndo`), not the
zero-width-space toggle:

- The ZWSP toggle (`:387-398`) exists because `queueAnnouncement` is *debounced*
  and fires from a key-repeat burst; it dedupes a stream.
- These announcements are **discrete acts** — one per button press — which is
  exactly what `announceUndo` was written for, and its comment says so.
- A token remount also makes the test assertion honest: the spec's repeat
  scenario asserts the DOM text *changed*, and a `key` bump changes the node
  rather than smuggling an invisible character into the string a test then has to
  know about.

So: `const [announcement, setAnnouncement] = useState({ text: '', token: 0 })`,
region renders `<span key={announcement.token}>{announcement.text}</span>`.

**Styling:** Tailwind classes, matching the `text-sm text-accent-fault` the two
alerts already carry (ADR-0078 tokens). `sr-only` for the status region — it is
for a screen reader, and the visible in-flight signal is the control's own
`aria-disabled` styling. Not inline styles: unlike `OverlayGeometryFields.tsx`,
which spec 151 kept inline deliberately, this component's neighbours in both
dialogs are Tailwind-classed (`FormField`, `Input`, `Button`), so Tailwind is the
non-hybrid choice here.

### 3b. `apps/shared/src/ui/primitives/Button.tsx` — one type widening

`ChainRecoveryNotice` cannot focus Save; the dialog must. That needs a ref on
`<Button>`.

React is **19.2.8** in all three packages, so `ref` is an ordinary prop on a
function component and would already flow through `{...rest}` onto `<button>` at
runtime. It is **TypeScript** that refuses: `ButtonProps extends
ButtonHTMLAttributes<HTMLButtonElement>`, and `ref` lives in `RefAttributes`, not
`ButtonHTMLAttributes`.

Change `ButtonHTMLAttributes<HTMLButtonElement>` →
`ComponentPropsWithRef<'button'>`. A strict superset; no runtime change; no call
site is invalidated. The component body is untouched.

**Fallback if that widening ripples** (it should not, but it is a primitive used
by both apps): give Save a `useId()`-derived `id` and focus via
`document.getElementById`. Uglier, contained, and it keeps the blast radius
inside the two dialogs. Named here so phase 4 does not have to invent it.

### 3c. `OverlayEditorDialog.tsx` — three changes

- Destructure `isFetching: chainFetching` from `useGetOverlayQuery` (`:161-169`).
  **The layout dialog already does this; the overlay one does not.** This is a
  read-only addition to an existing destructure.
- Add `const saveRef = useRef<HTMLButtonElement>(null)` and put it on the Save
  `<Button>` (`:302`). **The `disabled` predicate on that line is not touched** —
  spec FR-010, and spec §Found and not folded item 1 says why.
- Replace `:270-296` (the comment block and both ternary arms) with
  `<ChainRecoveryNotice ... />`.

**Not touched:** the label-validation alert at `:267`, the resolve-preview query
and its `currentData` reasoning (`:161-179`), the `getToken` ref (`:104-116`),
the close effect (`:120-126`), the `onSubmit` handler and its version read
(`:181-205`), the error-code keying (`:206-228`). Those carry merged work from
#2341, #2364 and spec 153 and this spec has no business in any of them.

### 3d. `LayoutEditorDialog.tsx` — two changes

- Add `saveRef` and put it on Save (`:416`). **`disabled` untouched** — it
  already includes `chainFetching` and is correct.
- Replace `:354-380` with `<ChainRecoveryNotice ... />`.

`chainFetching` is already destructured at `:89`. **Nothing this spec does goes
near lines 85–110** — see §5.

**Not touched:** the camera-filter `aria-live` region at `:340` (a different
region, for a different job, already always-mounted and already correct), the
truncation notice `:310`, `GridDesigner`, the close effect.

## 4. Where the focus move lives, and the trap in it

Not in the click handler — at click time the re-read has not happened. Not in a
plain effect on `readFailed` going false — **that fires on every normal dialog
open**, because a dialog that opens and reads its chain successfully goes
`false → false` with `reReading` flickering in between, and a naive
`useEffect(() => { if (!readFailed) onReadRecovered() }, [readFailed])` would
steal focus to Save every single time the dialog opens. That is FR-007, and it is
a worse defect than the one being fixed.

The move is therefore **latched by an operator act**:

- the control's handler sets `recoveryRequestedRef.current = true`;
- an effect watching `reReading` fires on its **falling edge** only;
- on that edge, if the latch is set and `readFailed` is now false, clear the
  latch, announce success, call `onReadRecovered()`;
- if the latch is set and `readFailed` is now true, clear the latch, clear the
  announcement, move nothing (FR-006 — the control is still mounted and still
  focused, so there is nothing to restore).

The latch is a ref, not state: it must not cause a render, and it is read inside
the effect that already runs.

**Reload uses the same latch and the same effect**, with `onReadRecovered`
withheld — a second boolean on the latch (`{ requested, moveFocus }`) rather than
a second effect, so there is one code path and one place to be wrong.

## 5. Interaction with PR #2377 (issue #2371) — expected conflicts

PR #2377 is open against `LayoutEditorDialog.tsx` and this branch is cut from
`develop` without it. Its diff touches:

- `LayoutEditorDialog.tsx` **lines 89–108 only** — the `const { isLoading, error }`
  destructure and the close effect.
- `LayoutEditorDialog.test.tsx` — the mock block (`:18-32`), three `beforeEach`
  bodies (`:135`, `:295`, `:402`), one changed assertion (`:439`), and two
  appended tests at `:238` and `:336`.
- `specs/155-a-refused-edit-that-clears/tasks.md` — new file.

**Predicted conflicts: none, if two rules are followed.**

1. **No edit above line 236 of `LayoutEditorDialog.tsx`.** All this spec's hooks
   (`saveRef`, the latch) go *below* the existing `staleConflict` /
   `backendError` derivations at `:236-240`, not beside the close effect. The
   JSX replacement at `:354-380` is 250 lines clear of #2377's window.
2. **New tests go in new files**, never appended to `LayoutEditorDialog.test.tsx`
   or `OverlayEditorDialog.test.tsx`. #2377 appends to the tail of
   `describe('LayoutEditorDialog — edit')`, which is exactly where a naive
   addition here would land, and a conflict inside a `describe` block is the
   annoying kind. House convention already splits these — there are three
   `LayoutEditorDialog*.test.tsx` and five `OverlayEditorDialog*.test.tsx`
   files today.

If #2377 merges first this branch still rebases cleanly under those rules. If
this branch merges first, #2377's `LayoutEditorDialog.tsx` hunk is untouched by
it and rebases cleanly too — but see the memory note: rebase-merge renames SHAs
(ADR-0087), so whichever lands second must rebase before merging regardless.

**One semantic interaction, not a textual one.** #2377 makes the close effect
reset *both* mutation states. That clears `backendError` on close, which is the
only thing that ever removes the Reload arm. It does not change FR-008 — Reload
still survives its own activation, because `refetchChain()` is still not what
clears the mutation. Worth re-reading FR-008 after the rebase rather than
assuming.

## 6. Test harness — the part that needs building first

Both dialogs' test files mock the chain query at module level from a mutable
`chainQueryState` object. **Neither mock can express this spec's behaviour yet:**

- `OverlayEditorDialog.test.tsx:24-28` — `chainQueryState` has `data`,
  `isLoading`, `isError`. **No `isFetching`.** The mock returns `isFetching:
  undefined`, so `reReading` would be permanently falsy and every in-flight
  assertion would be vacuously wrong.
- `refetchChainMock` is a bare `vi.fn()` in both files. It records the call and
  changes nothing, so today no test can drive `failed → in flight → resolved`.

So the harness needs, in both files (or in the new sibling files):

- `isFetching` on `chainQueryState`;
- `refetchChainMock` given an implementation that advances `chainQueryState` and
  re-renders, so a test can step the state machine deliberately.

A test that cannot make `isFetching` true would pass against an implementation
that never renders the in-flight state at all — the failure mode the repo has
hit before (a guard narrowed to its own examples). **T002 exists to make the
harness capable before any behaviour is asserted through it**, and its own
acceptance is the counterfactual: with the harness in place and the fix absent,
the discriminating test must fail for the right reason.

## 7. Boundary rules

- `ChainRecoveryNotice` lives in `apps/shared/src/ui/composites/`, imports only
  from `apps/shared` and React. It takes no store, no RTK hook, no API client —
  the dialogs pass plain values and callbacks. Same shape as `FormField` and
  `OverlayGeometryFields`.
- `apps/kiosk-web` also consumes `apps/shared` and is unaffected: the new
  composite is additive, and `Button`'s prop type only widens.
- No `src/**` file is read or written.
