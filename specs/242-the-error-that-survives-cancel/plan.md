# Plan 242 — The fab error that survives Cancel

**Spec**: [spec.md](./spec.md) · **Issue**: #2561 · **Engineer**: `frontend-engineer`

## Context and layers

- **App**: `apps/management-web` (operator console). No bounded context, no backend, no
  `apps/shared`, no `Shared.Contracts`, no AppHost change.
- **Component**: `features/systemVariables/SystemVariableDialog.tsx` — local UI state only.

## Entities / invariants

No domain model. The UI invariant introduced:

> While the dialog is closed, `fabId === ''` and `fabError === null`.

It is established by the effect on `open`, so it holds for every close path (Cancel, Esc/overlay,
submit success) — the Dialog's `onOpenChange` cannot see Cancel (spec §1).

## Change

In the existing close effect (`SystemVariableDialog.tsx:37-41`), mirror `RegisterCameraDialog.tsx:33-46`:

```tsx
useEffect(() => {
  if (!open) {
    resetMutationState();
    setFabId('');
    setFabError(null);
  }
}, [open, resetMutationState]);
```

Dependencies unchanged (state setters are stable). Update the effect's comment to say it also
drops the fab choice and its error, and why it watches `open` rather than `onOpenChange` — briefly;
`RegisterCameraDialog` holds the long form. Add the `react-hooks/set-state-in-effect` suppression
only if `lint` reports it (`RuleDialog` needs none; `RegisterCameraDialog` has one).

## Messaging / boundaries

None. No request is sent by the fix; no cross-context reference; latency N/A.

## Test

Add one case (and optionally the chosen-fab case) to the existing
`describe('SystemVariableDialog — multi-fab …')` block in `SystemVariableDialog.test.tsx`. The
current `renderDialog()` hard-codes `open={true}`; the new test needs `rerender` with
`open={false}` then `open={true}` inside the same `<Provider store={store}>`. Prefer a local
helper over changing `renderDialog`'s signature, so no existing test is edited.

Expected red on `a54b11d0`: `expect(screen.queryByText(/choose which fab/i)).not.toBeInTheDocument()`
fails because the message is still rendered after reopen.

## Risks

- `rerender` to `open={false}` makes Radix unmount the portal content; on reopen the select is
  remounted, but `fabId`/`fabError` live in the (still-mounted) `SystemVariableDialog` — which is
  exactly the bug the test must catch. A test that unmounts the whole component would pass on the
  unfixed code and prove nothing.
