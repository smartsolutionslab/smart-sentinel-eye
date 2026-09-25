# Spec 256 — The form that survives Cancel

**Issue**: #2579 (feature-level; no per-task issues) · **Related**: #2561, spec 242 (same defect
class, for `fabId`/`fabError`)
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`
**ADRs**: ADR-0037 (phases), ADR-0144 (the lane), ADR-0139 (new behaviour observed red),
ADR-0036 (smallest change; mirror the existing pattern, no new abstraction), ADR-0079 (React Hook
Form — whose `shouldUnregister: false` default is what keeps the values), ADR-0074 (two React apps),
ADR-0109 (disjoint files)
**New ADR needed**: **No** (§9).

## 1. The premise, re-checked against `0d349ce8`

The issue is accurate for all three dialogs. `RuleDialog`'s gap is wider than the other two's.

All three parents mount the dialog unconditionally and toggle only `open`:
`CamerasPage.tsx:192`, `RulesPage.tsx:213`, `SystemVariablesPage.tsx:220`, each
`<XDialog open={dialogOpen} onOpenChange={setDialogOpen} />`. So `useForm` lives in a component that
is **never unmounted** by a close. Radix does unmount the portal content, which removes the inputs.
Under React Hook Form's default `shouldUnregister: false` their values stay in `_formValues`, and
re-registration on reopen writes them back into the fresh inputs.

| Dialog | Form reset today | Close paths that reset | Close paths that do **not** |
|---|---|---|---|
| `systemVariables/SystemVariableDialog.tsx` | `reset(DEFAULT_INPUT)` in the Dialog's `onOpenChange` wrapper (`:111-114`) and on submit success (`:90`) | Esc, overlay click, submit success | **Cancel** (`:174`, `onClick={() => onOpenChange(false)}`, which calls the parent directly) |
| `cameras/RegisterCameraDialog.tsx` | `reset()` in the Dialog's `onOpenChange` wrapper (`:81-86`) and on submit success (`:70`) | Esc, overlay click, submit success | **Cancel** (`:124`, same shape) |
| `rules/RuleDialog.tsx` | `reset(DEFAULT_INPUT)` on submit success **only** (`:100`); the Dialog gets `onOpenChange={onOpenChange}` (`:109`), with no wrapper | submit success | **Cancel** (`:213`), **Esc**, **overlay click** |

Each dialog already has a close effect that watches `open`: `SystemVariableDialog.tsx:40-52`,
`RegisterCameraDialog.tsx:33-46` and `RuleDialog.tsx:40-46`. It clears the mutation state, `fabId`
and `fabError` (spec 242) but not the form. The comments in two of those effects already explain why
the Dialog's `onOpenChange` is the wrong place for a close reset: it never sees Cancel. The form
reset sits in exactly that wrong place.

### Why the fix is the close effect, not the Cancel button

The issue suggests routing Cancel through the wrapper's reset. That would fix Cancel only, and it
**would not turn the confirming test green**. The test mirrors #2561 and drives `open` through
`rerender`, so it never calls the Cancel `onClick` or the Dialog's `onOpenChange`. It models what
the parent does in every close path, which is `setDialogOpen(false)` → re-render with
`open={false}`. The only code that sees that is an effect on `open`. The same argument settled
spec 242, and the effects' own comments record it. The repo already uses this mechanism for form
values: `RenameCameraDialog.tsx:66-72` and `EditCameraAddressDialog.tsx:58-64` call `reset(...)`
from an effect on `open`.

## 2. User stories

### US1 (P1) — Reopening "New variable" starts from the defaults

An operator who typed a name, changed Type, and then cancelled sees an empty Name and Type
"String" when they open "New variable" again.

### US2 (P1) — Reopening "Register a camera" starts empty

An operator who typed a name and an RTSP URL, then cancelled, sees both fields empty on reopen.

### US3 (P1) — Reopening "New rule" starts from the defaults, however it was closed

An operator who typed a rule name and predicate, then closed the dialog by Cancel, Esc or overlay
click, sees empty Name and Predicate and Trigger source "plc" on reopen.

The three stories are independent. Each one is a single file pair (component + its test file)
and can ship alone. They are one spec because they share one defect, one mechanism and one
confirming-test shape.

## 3. Acceptance scenarios

```gherkin
Feature: Create dialogs forget typed form values on close

  Scenario Outline: Typed values do not survive a close (happy path of the fix)
    Given the <dialog> dialog is open
    And the operator typed "<value>" into <field>
    When the dialog is closed without submitting
    And the dialog is opened again
    Then <field> shows its default "<default>"

    Examples:
      | dialog            | field         | value                 | default |
      | New variable      | Name          | lineStatus            |         |
      | New variable      | Type          | Number                | String  |
      | Register a camera | Name          | Line-1-North          |         |
      | Register a camera | RTSP URL      | rtsp://10.0.5.12/h264 |         |
      | New rule          | Name          | high-oee              |         |
      | New rule          | Predicate     | $.payload.x > 1       |         |
      | New rule          | Trigger source| mqtt                  | plc     |

  Scenario: New rule is cleared by Esc as well as by Cancel (the wider RuleDialog gap)
    Given the New rule dialog is open with Name "high-oee"
    When the operator presses Esc
    And the dialog is opened again
    Then Name is empty

  Scenario: Bad request — a reopened dialog still validates
    Given the New variable dialog was closed and reopened after typing an invalid name
    When the operator presses Define with the Name empty
    Then a Name validation error is shown
    And no define request is sent

  Scenario: Submit success still leaves a clean form (regression guard)
    Given the operator submitted a valid variable and the dialog closed
    When the dialog is opened again
    Then Name is empty

  Scenario: Fab scoping unaffected (auth / scope)
    Given an operator assigned to fabs "munich" and "dresden"
    When the dialog is closed and reopened
    Then the Fab select still appears and shows "Choose a fab…" (ADR-0114, spec 242)
```

There is no conflict (409) scenario. The fix is client-side state only and sends nothing. The
existing 409 and backend-error handling is untouched.

## 4. Functional requirements

- **FR-001** When `open` becomes `false`, each of the three dialogs resets its React Hook Form
  state to its defaults: `reset(DEFAULT_INPUT)` for `SystemVariableDialog` and `RuleDialog`, and
  `reset()` for `RegisterCameraDialog`, whose `defaultValues` are inline. The reset goes in the
  existing close effect that already clears the mutation state and `fabId`/`fabError`.
- **FR-002** The reset applies on **every** close path: Cancel, Esc, overlay click and submit
  success. That is why it belongs in an effect on `open`.
- **FR-003** The now-redundant `if (!next) reset(...)` in the Dialog's `onOpenChange` wrapper is
  removed from `SystemVariableDialog` and `RegisterCameraDialog`, which then pass
  `onOpenChange={onOpenChange}` as `RuleDialog` already does. Leaving the wrapper in place would keep
  the mechanism that caused this defect sitting beside the one that fixes it. `RuleDialog` gets
  **no** wrapper, because the effect covers Esc and overlay click.
- **FR-004** No other behaviour changes. The submit-success `reset(...)` calls stay as they are,
  and so do the Cancel buttons' `onClick`. The mutation, `fabId` and `fabError` resets from spec 242
  are unchanged.

## 5. Phase 4a colour — RED

Behaviour-changing. Each dialog's close/reopen test must be observed **failing** on `0d349ce8`,
with the field still populated after reopen. The failure is quoted verbatim in the PR (ADR-0139).
A form test that arrives green is a phase-4 failure. It would mean the harness unmounted the
component, which proves nothing.

## 6. Independent end-to-end test procedure

1. Component (authoritative), per dialog: render with `open={true}` inside
   `<Provider store={store}>`, type into the target fields, then `rerender` the **same** tree with
   `open={false}` followed by `open={true}`. Assert that the fields show their defaults. Unmounting
   and remounting is forbidden, because it passes on the unfixed code.
2. Live (phase 5): boot the stack and sign in to management-web. For each of Cameras → Register
   camera, System variables → New variable and Rules → New rule, type values, press **Cancel** and
   reopen: the fields are empty or at their defaults. Repeat with **Esc**. Esc matters for New rule,
   which today keeps its values on Esc too.

## 7. Locked tech choices

React + TypeScript (ADR-0074), React Hook Form + Zod (ADR-0079), Vitest + Testing Library for the
component tests. No new dependency.

## 8. Latency budget

**N/A.** These are management-web operator-console dialogs and are not on the event → overlay path
(constitution §IV).

## 9. ADR check

No new decision. The fix puts the form reset in the close effect spec 242 already extended. The
repo already uses this mechanism for form values in `RenameCameraDialog` and
`EditCameraAddressDialog`.

## 10. Out of scope

- `RenameCameraDialog`, `EditCameraAddressDialog`: already reset the form on open (`:66-72`,
  `:58-64`), so they are not affected.
- `LayoutEditorDialog`, `OverlayEditorDialog`: they reset from a `defaultValues` effect and have
  their own create/edit wiring. They are not named by #2579 and were not checked here. If they turn
  out to be affected, that is a separate issue.
