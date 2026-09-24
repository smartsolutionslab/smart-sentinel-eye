# Spec 242 — The fab error that survives Cancel

**Issue**: #2561 (feature-level; no per-task issues) · **Related**: #2433, spec 231
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`
**ADRs**: ADR-0037 (phases), ADR-0144 (the lane), ADR-0139 (new behaviour observed red),
ADR-0036 (smallest change; mirror the existing pattern, no new abstraction), ADR-0114 (the fab
select exists only for multi-fab operators), ADR-0079 (React Hook Form — why `fabId` sits outside
the form), ADR-0109 (disjoint files)
**New ADR needed**: **No** (§9).

## 1. The premise, re-checked against `a54b11d0`

The issue is accurate.

- `apps/management-web/src/features/systemVariables/SystemVariableDialog.tsx:39-41` — the close
  effect is `if (!open) resetMutationState();` and nothing else. `fabId` and `fabError` (lines
  34-35) are never cleared on close.
- `RegisterCameraDialog.tsx:33-46` and `RuleDialog.tsx:40-46` — the same effect also calls
  `setFabId('')` and `setFabError(null)`.
- The dialog **stays mounted while closed**: `SystemVariablesPage.tsx:220` renders
  `<SystemVariableDialog open={dialogOpen} … />` unconditionally, so `useState` values survive a
  close/reopen. The bug is real, not masked by an unmount.
- The Dialog's own `onOpenChange` (`SystemVariableDialog.tsx:100-103`) sees only Radix-initiated
  closes (Esc, overlay click). **Cancel** (`:163`) calls the parent's `onOpenChange(false)` directly,
  so no handler on the Dialog can catch it — which is exactly why `RegisterCameraDialog` watches
  `open` in an effect instead (its comment at `:35-40` records this).

### Structural check — is a direct mirror right?

Yes. Same state shape (`fabId: string`, `fabError: string | null`), same effect dependency list
(`[open, resetMutationState]`; the setters are stable), same placement before `useForm`. Nothing in
`SystemVariableDialog` (its `watch`/`unregister` effect for Boolean labels is independent) makes
the mirror wrong.

The one lint difference between the two precedents: `RegisterCameraDialog.tsx:42` carries
`// eslint-disable-next-line react-hooks/set-state-in-effect`; `RuleDialog.tsx` does not and its
lint is clean. Follow whichever `pnpm --filter management-web lint` (`--max-warnings 0`) requires —
do not add a suppression the linter does not ask for.

## 2. User story

### US1 (P1) — Reopening the dialog starts clean

A multi-fab operator who triggered "Choose which fab this variable belongs to." and then cancelled
sees no such message when they open "New variable" again, and the fab select is back at
"Choose a fab…".

## 3. Acceptance scenarios

```gherkin
Feature: New-variable dialog forgets the fab choice and its error on close

  Background:
    Given an operator assigned to fabs "munich" and "dresden"

  Scenario: Missing-fab error does not survive Cancel (happy path of the fix)
    Given the New variable dialog is open
    And the operator entered name "lineStatus" and pressed Define without choosing a fab
    And the message "Choose which fab this variable belongs to." is shown
    When the dialog is closed
    And the dialog is opened again
    Then no "Choose which fab" message is shown

  Scenario: A chosen fab does not survive Cancel
    Given the New variable dialog is open
    And the operator chose fab "dresden"
    When the dialog is closed and opened again
    Then the Fab select shows "Choose a fab…"

  Scenario: Bad request — reopened dialog still refuses a missing fab
    Given the dialog was closed and reopened after a missing-fab error
    When the operator enters a valid name and presses Define without choosing a fab
    Then "Choose which fab this variable belongs to." is shown again
    And no define request is sent

  Scenario: Single-fab operator unaffected (auth / scope)
    Given an operator assigned only to fab "munich"
    Then no Fab select is rendered, before or after a close/reopen (ADR-0114)
```

There is no conflict (409) scenario: the fix is client-side state only and sends nothing. The
existing 409/`VARIABLE_STALE` handling (spec 231 US1) is untouched.

## 4. Functional requirements

- **FR-001** When `open` becomes `false`, `SystemVariableDialog` sets `fabId` to `''` and
  `fabError` to `null`, in the same effect that already calls `resetMutationState()`.
- **FR-002** The reset applies on **every** close path — Cancel, Esc/overlay, and submit success —
  hence an effect on `open`, not the Dialog's `onOpenChange`.
- **FR-003** No other behaviour changes. In particular the form-field reset (`reset(DEFAULT_INPUT)`
  on Radix closes only) is **not** touched here — see §10.

## 5. Phase 4a colour — RED

Behaviour-changing. The close/reopen test must be observed **failing** on `a54b11d0` (the message
still present after reopen) and the failure quoted verbatim in the PR (ADR-0139).

## 6. Independent end-to-end test procedure

1. Component (authoritative): in `SystemVariableDialog.test.tsx`'s multi-fab section, render with
   `open={true}`, type a name, press Define → message shown; `rerender` with `open={false}`, then
   `open={true}` → `queryByText(/choose which fab/i)` is null and the Fab select's value is `''`.
2. Live (phase 5): boot the stack, sign in to management-web as a user in two fab groups, open
   System variables → New variable, press Define with a name and no fab, press Cancel, reopen: no
   message, select at "Choose a fab…". Repeat with Esc instead of Cancel.

## 7. Locked tech choices

React + TypeScript (ADR-0074), React Hook Form + Zod (ADR-0079), Vitest + Testing Library for the
component test. No new dependency.

## 8. Latency budget

**N/A** — management-web operator console dialog; not on the event → overlay path (constitution §IV).

## 9. ADR check

No new decision. The fix copies an established in-repo pattern (RegisterCameraDialog/RuleDialog);
ADR-0114 already governs when the fab select appears.

## 10. Out of scope (candidate follow-up, not filed by this spec)

**Form fields survive Cancel in all three dialogs.** `reset(DEFAULT_INPUT)` / `reset()` lives in
the Dialog's `onOpenChange` in `SystemVariableDialog.tsx:100-103`, `RegisterCameraDialog.tsx:81-86`
(and presumably `RuleDialog`), which — per `RegisterCameraDialog`'s own comment — never sees Cancel.
So a typed name is likely still there after Cancel + reopen. Not verified by test here; it spans
three files and is a different defect from #2561's, so it belongs in its own issue if confirmed.
