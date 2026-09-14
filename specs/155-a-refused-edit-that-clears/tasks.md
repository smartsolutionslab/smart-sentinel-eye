# Tasks — 155: a refused layout edit that actually clears

**Issue:** #2371 (bug, `agent:ready`, on Project #13 — verified 2026-09-14 by
`content.url`, not by number).
**Branch:** `fix/2371-a-refused-edit-that-clears`, cut from `origin/develop` @ `366fabdc`.
**Phases 1–2: skipped.** One selection expression and one effect, an exact twin
of a fix merged today (#2364 / PR #2369). ADR-0037 permits the skip for a change
this size; the investigation below is what the phases would have produced, so it
is recorded here rather than inflated into a `spec.md`.
**Phase 4a colour: RED** (ADR-0139). Behaviour-changing — the operator sees a
banner they do not see after the fix.
**Latency budget (constitution §IV): N/A.** `apps/management-web`, an operator
console dialog. The ≤50 ms composite-and-render leg is the kiosk wall's
(ADR-0123, p50 54.2 ms); nothing here touches it.

## The defect, restated from the code

`LayoutEditorDialog.tsx:92` selects the mutation state by mode, and `:96-98`
resets whatever that selection named on close:

```ts
const { isLoading, error, reset: resetMutationState } = isEdit ? editState : createState;
useEffect(() => { if (!open) resetMutationState(); }, [open, resetMutationState]);
```

`isEdit` is `editTarget !== undefined`; `LayoutsPage.tsx:334-339` drives the edit
instance as `open={editTarget !== undefined}`. The two are the same boolean, so
on close `isEdit` is already `false` and `createState.reset` runs. `editState.reset`
is never called by this effect — ever. A refused edit's error survives into the
next open, on a different layout, and the `Reload` control offered beside it
refetches *that* layout's chain, which cannot clear a message about the first.

## Investigation findings (these change the work, so read them)

1. **The create instance is correct by accident, and the fix makes it correct on
   purpose.** `LayoutsPage.tsx:333` renders a second dialog with no `editTarget`,
   so `isEdit` is permanently `false` there and `createState.reset` happens to be
   the right target. The two instances are separate components, so neither can
   leak into the other. Resetting both states removes the accident rather than
   relying on it.
2. **`reset` is the right mechanism, and the next mutation is not a substitute.**
   Read from `@reduxjs/toolkit@2.12.0`
   (`dist/query/react/rtk-query-react.modern.mjs:544-565`): with no
   `fixedCacheKey` — none of these hooks passes one — `reset` dispatches nothing
   at all; it calls `setPromise(undefined)`, which makes the mutation cache key
   `skipToken` and the selector return the uninitialised state. That clears the
   error, and it is the only thing that clears it *before* a submit. A new
   trigger does replace the error, but only once the operator has pressed Save —
   after they have read the banner and decided. So reset-on-close is necessary,
   exactly as in the overlay twin.
3. **The reference's effect deps terminate — verified, not assumed.** The twin
   depends on `[open, createState, editState]`, and those objects change identity
   when the mutation state changes (`finalState` is `useMemo`'d over
   `[currentState, originalArgs, reset]`). The second pass is the last: `reset`
   is guarded by `if (promise)`, so once `promise` is `undefined` it performs no
   state change and the identity settles. Mirror the twin; do not invent a
   variant.
4. **#2368's sibling is already on this branch and does not conflict.**
   `LayoutEditorDialog.tsx:86-91` reads `currentData` with
   `refetchOnMountOrArgChange` (commit `8ae39fd5`, confirmed an ancestor of HEAD),
   and the alert block is already the single-alert ternary. This change touches
   `:92` and `:96-98` only.
5. **The overlay twin is clean and is the reference implementation.**
   `OverlayEditorDialog.tsx:122-127` resets both; `OverlaysPage.tsx:362-369` is
   the identical two-instance wiring with the defect gone. Nothing under
   `features/overlays/**` needs to be touched, and nothing here will be.
6. **This closes the family.** The other four dialogs carrying this effect —
   `RegisterCameraDialog`, `RenameCameraDialog`, `EditCameraAddressDialog`,
   `RuleDialog`, `SystemVariableDialog` — each hold exactly one mutation, so
   there is no mode flag to disagree with `open`. `LayoutEditorDialog` is the
   last instance of the shape in the repo.
7. **The existing mock cannot fail this test, and that is the first task.**
   `LayoutEditorDialog.test.tsx:13-21` feeds **one** module-level `editError` to
   **both** mutation hooks and gives both a bare `vi.fn()` reset. Under that mock
   the wrong reset clears the same variable as the right one, so the defect
   passes. The mocks must become behavioural
   (`vi.fn(() => (editError = undefined))`) over **separate** variables, as
   `OverlayEditorDialog.test.tsx:30-48` already does.

## Tasks

| ID | P? | Task |
|---|---|---|
| T001 | — | **test-writer.** In `apps/management-web/src/features/layouts/LayoutEditorDialog.test.tsx`: split the shared `editError` into `createError` and `editError`, give each mutation mock a behavioural `reset` that clears its own variable, and retarget the one existing create-mode test that sets `editError` (`'Keeps retry wording, and offers no reload, for a name collision'`, :342-357) to set `createError` — a variable rename, no assertion changes. Then add the discriminating test below. Run it, observe **red**, return the verbatim output. |
| T002 | — | **frontend-engineer.** `LayoutEditorDialog.tsx`: drop `reset: resetMutationState` from the `:92` destructure and reset **both** mutation states in the close effect, deps `[open, createState, editState]` — mirroring `OverlayEditorDialog.tsx:122-127`, including the shape of its comment. Nothing else in the file, nothing outside it. T001's red output is the brief. |

T001 blocks T002 strictly; there is no `[P]` work in a two-file change (ADR-0109
marks parallelism, it does not manufacture it).

### The discriminating test

`'Does not carry a refused layout edit banner over to a different draft after closing'`,
in the `LayoutEditorDialog — edit` describe. Shape, copied from the overlay twin:

- set `editError` to a `LAYOUT_REVISION_NOT_DRAFT` 409 and render the dialog on
  layout A with `open={true}`;
- `await screen.findByRole('alert')` — the refusal is on screen;
- rerender with `open={false}` and `editTarget={undefined}` — `LayoutsPage` always
  drives the two together;
- rerender with `open={true}` and a **different** `editTarget` (layout B, its own
  identifier, revision and tiles);
- assert B's form actually seeded — B's tile camera is the selected option — so a
  rerender that silently did not take cannot pass the test vacuously;
- assert `screen.queryByRole('alert')` is `null`.

Red today because the create reset fires and `editError` is untouched.

## Verification (phase 5)

The unit test is the evidence. A browser pass is cheap and worth one line in the
note: refuse an edit on one layout (a stale `If-Match` version), cancel, open a
draft of a second layout, see no banner.

## References

ADR-0037 (phases and the documented skip), ADR-0144 (autonomous lane),
ADR-0139 (new behaviour starts red), ADR-0075 (RTK Query), ADR-0113 (the
`If-Match` version the refused mutation carries), ADR-0123 (the §IV leg this
change is not on). Specs 152 and 153 are the twin's and the sibling's.
