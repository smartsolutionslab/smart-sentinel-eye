# Plan 256 — The form that survives Cancel

**Spec**: [spec.md](./spec.md) · **Issue**: #2579 · **Engineer**: `frontend-engineer`

## Context and layers

- **App**: `apps/management-web` (operator console). No bounded context, no backend, no
  `apps/shared`, no `Shared.Contracts` and no AppHost change.
- **Components**: `features/systemVariables/SystemVariableDialog.tsx`,
  `features/cameras/RegisterCameraDialog.tsx` and `features/rules/RuleDialog.tsx`. All the state
  involved is local UI state.

## Entities / invariants

There is no domain model. The UI invariant, per dialog:

> While the dialog is closed, the React Hook Form values equal the dialog's defaults.

The effect on `open` establishes it, so it holds on every close path. Spec §1 explains why neither
the Dialog's `onOpenChange` nor the Cancel `onClick` can establish it.

## Change, per dialog

### SystemVariableDialog (US1)

1. In the close effect (`:40-52`), add `reset(DEFAULT_INPUT);` inside `if (!open)`. `reset` comes
   from `useForm` (`:54-64`), which is declared **after** the effect. Move the effect below the
   `useForm` call, or move `useForm` up, whichever reads better. Nothing else depends on the order.
   Add `reset` to the dependency list: `[open, reset, resetMutationState]`.
2. Replace the Dialog's `onOpenChange` wrapper (`:111-114`) with `onOpenChange={onOpenChange}`.
3. Extend the effect's comment by one clause so it names the form values too. The existing "the
   obvious rewrite is wrong here" paragraph already gives the reason.

### RegisterCameraDialog (US2)

Same three steps: the effect is at `:33-46`, `useForm` at `:48-56` and the wrapper at `:81-86`.
Use `reset()` without arguments, which is what the component uses elsewhere, because its
`defaultValues` are inline.

### RuleDialog (US3)

1. In the close effect (`:40-46`), add `reset(DEFAULT_INPUT);` and move it below `useForm`
   (`:48-58`). Dependencies: `[open, reset, resetMutationState]`.
2. **No wrapper is added.** `onOpenChange={onOpenChange}` at `:109` stays. The effect covers Esc
   and overlay click, because Radix calls the parent, the parent flips `open`, and the effect runs.
3. This effect has no comment today. Add one short line saying it covers every close path, and
   point to the long form in `RegisterCameraDialog` rather than copying it.

Lint: follow what `pnpm --filter management-web lint` (`--max-warnings 0`) actually reports.
Only `RegisterCameraDialog` carries a `react-hooks/set-state-in-effect` suppression today. Do not
add a suppression the linter does not ask for (the same rule as spec 242).

## Messaging / boundaries

None. The fix sends no request and adds no cross-context reference. Latency impact: N/A.

## Tests (phase 4a, test-writer)

Each dialog needs a `toggleOpen(rerender, open)` helper that re-renders the **same** tree in the
**same** `<Provider store={store}>` with a different `open`. That mirrors
`SystemVariableDialog.test.tsx:215-221`.

| Dialog | Test file | Existing helper | Helper to add | Fields to type → expected after reopen |
|---|---|---|---|---|
| SystemVariableDialog | `systemVariables/SystemVariableDialog.test.tsx` | `renderDialog()` at `:56` returns the render result. `toggleOpen` exists, but **only inside** the multi-fab `describe` at `:215` | Add a module-level copy for the single-fab `describe('SystemVariableDialog')`. The existing nested one stays untouched, so no existing test is edited | `getByLabelText(/name/i)` typed `lineStatus` → `''`; `selectOptions(getByLabelText(/type/i), 'Number')` → value `'String'` |
| RegisterCameraDialog | `cameras/RegisterCameraDialog.test.tsx` | `renderDialog()` at `:27` returns the render result. No `toggleOpen` | Module-level `toggleOpen` | `getByLabelText(/name/i)` typed `Line-1-North` → `''`; `getByLabelText(/rtsp/i)` typed `rtsp://10.0.5.12/h264` → `''` (the existing `fillValidCamera` at `:35` does both) |
| RuleDialog | `rules/RuleDialog.test.tsx` | `renderDialog()` at `:52` returns the render result, and `fill()` at `:43` pastes. No `toggleOpen` | Module-level `toggleOpen` | `getByLabelText(/^name$/i)` (anchored: `/name/i` also matches "Variable name") filled `high-oee` → `''`; `getByLabelText(/predicate/i)` → `''`; `getByLabelText(/trigger source/i)` cleared and filled `mqtt` → `'plc'` |

Assert with `toHaveValue(...)` on the element queried **after** the reopen, because the inputs are
new DOM nodes.

Expected red on `0d349ce8`, all three: `expect(nameInput).toHaveValue('')` fails with the typed
value still present.

Optional regression case, if cheap: submit success followed by reopen still gives a clean Name.
It passes today, so it is characterisation and not part of the red evidence. Label it that way.

## Risks

- **A test that arrives green** means the component was unmounted (for example, `render` was called
  again instead of `rerender`). Block, don't adjust.
- **Effect order**: the effect must be declared after `useForm` for `reset` to be in scope. Moving
  it is a textual reorder only. The effect already runs after render, so its timing does not change.
- **Initial mount with `open={false}`** (the real pages): the effect now also calls `reset` once
  at mount. That is a no-op against values that are already the defaults.
