# Spec 267: The place a submit keeps

**Issue**: #2624 (feature-level; no per-task issues) · **Filed from**: #2336's Phase 3 gate (spec 266,
`specs/266-the-fade-that-stands-for-every-state/plan.md` §7, T022)
**Lane**: supervised (ADR-0037). Phases 1–3 only in this artifact set.
**ADRs**: **ADR-0151** (the rule this applies), ADR-0077 (the `Button` primitive that carries
`unavailable`), ADR-0074 (two React apps; this is `management-web` + `apps/shared`), ADR-0139 / §Testing
(new behaviour observed red), ADR-0036 (smallest change; mirror the existing pattern), ADR-0109
(disjoint files), ADR-0150 (waits by condition, not by clock).
**New ADR needed**: **No.** ADR-0151 already decides the rule, the guard obligation and the
real-browser test obligation. §4 of it ("existing sites are not converted en masse") binds *sweeps*;
this spec converts eight named sites for a filed defect, which is the "any control a spec is already
touching" clause.
**Spec number**: 267. Two unmerged branches both hold 266 (`feat/2335-radix-primitives`,
`feat/2336-interaction-states`). Re-check against every remote branch and worktree before the PR.
**Latency budget (constitution §IV)**: **N/A.** Operator console only. Nothing on the
event→overlay path, nothing in `kiosk-web`.

## 1. The premise, re-checked against `b23b9307` (origin/develop)

The issue's inventory is **accurate: all eight sites exist and all eight carry the defect**, with
the exact prop the issue names. None has been fixed and none has drifted.

| # | Site | File:line | Today | Control kind |
|---|---|---|---|---|
| 1 | Correct the address — Save | `apps/management-web/src/features/cameras/EditCameraAddressDialog.tsx:95` | `<Button type="submit" disabled={isLoading}>` | form submit |
| 2 | Register a camera — Register | `apps/management-web/src/features/cameras/RegisterCameraDialog.tsx:147` | `<Button type="submit" disabled={isLoading}>` | form submit |
| 3 | Rename camera — Save | `apps/management-web/src/features/cameras/RenameCameraDialog.tsx:108` | `<Button type="submit" disabled={isLoading}>` | form submit |
| 4 | Dry run — Run | `apps/management-web/src/features/rules/DryRunPanel.tsx:61` | `<Button type="button" onClick={onRun} disabled={isLoading}>` | plain button, **no form** |
| 5 | New rule — Create draft | `apps/management-web/src/features/rules/RuleDialog.tsx:229` | `<Button type="submit" disabled={isLoading}>` | form submit |
| 6 | New variable — Define | `apps/management-web/src/features/systemVariables/SystemVariableDialog.tsx:182` | `<Button type="submit" disabled={isLoading}>` | form submit |
| 7 | ConfirmDialog — confirm | `apps/shared/src/ui/primitives/ConfirmDialog.tsx:76` | `<Button variant="danger" disabled={pending} onClick={onConfirm}>` | plain button, no form |
| 8 | ConfirmDialog — Cancel | `apps/shared/src/ui/primitives/ConfirmDialog.tsx:66` | `<Button variant="secondary" disabled={pending}>` inside `RadixAlertDialog.Cancel asChild` | Radix Close |

ConfirmDialog is shared. Its callers are `RetireCameraDialog` and `ArchiveConfirmation`, and
`ArchiveConfirmation` serves Layouts, Overlays, Rules and System variables. So sites 7–8 fix six
confirmation flows in one change.

**None of the five form dialogs (1, 2, 3, 5, 6) has an in-flight guard today.** Their only
protection against a double submit is the native `disabled` itself, which also suppresses implicit
submission (Enter in a text field). Swapping `disabled` for `aria-disabled` without adding the
guard would **introduce** a double-submit defect. ADR-0151 §2 names this trap, and spec 160 hit it.
The guard is therefore part of every form site's fix, not an extra.

### Same-shape sites outside the issue's list (reported, not in scope)

These controls also become natively `disabled` as a result of their own activation. They are not
in #2624 and are not converted here. ADR-0151 §4 forbids widening a fix into a sweep, and several
of these are owned by other in-flight work. Each one belongs in a follow-up issue.

| File:line | Control | Why it is the same shape |
|---|---|---|
| `apps/management-web/src/features/systemVariables/SystemVariablesPage.tsx:167` | Set value | `disabled={inProgress …}`, where `inProgress` is the set-value mutation it triggers |
| `apps/management-web/src/features/layouts/LayoutsPage.tsx:173,188,211,225,246` | Publish / Discard draft / branch / Revert / Archive | `disabled = publishing‖archiving‖branching‖reverting` (`:160`). The direct-action buttons disable on their own click |
| `apps/management-web/src/features/overlays/OverlaysPage.tsx:169,184,209,231,246,267` | the same row actions | the same `disabled` (`:156`) |
| `apps/management-web/src/features/cameras/CamerasPage.tsx:177,184` | Previous / Next | `‖ isFetching`, which the click starts |
| `apps/shared/src/ui/composites/BackdropControls.tsx:182` | Capture frame | `captureState === 'capturing'`, which the click enters. Spec 266 fences this file for #2342 |
| `apps/management-web/src/features/audit/AuditPage.tsx:200` | Next page | disables once the page it loads is the last (delayed, after the response) |

## 2. User stories

All eight are the same defect. They are split by **guard shape** rather than by file, because the
guard is where each one can go wrong (ADR-0151 lists three shapes, and this spec uses two of them
plus the Radix-Close variant in §5).

### US1 (P1): A keyboard operator submitting a dialog form keeps their place

Covers sites 1, 2, 3, 5 and 6. The operator tabs to the dialog's submit button and presses Enter.
While the request is in flight, focus stays on that button, which announces itself as unavailable.
A second Enter, or an Enter in a text field, sends nothing more.

### US2 (P1): A keyboard operator running a dry run keeps their place

Covers site 4. The operator focuses Run and presses Enter. While the dry run is in flight, focus
stays on Run and a second Enter sends nothing more.

### US3 (P1): A keyboard operator confirming a destructive action keeps their place

Covers sites 7 and 8. The operator confirms, whether retiring a camera or archiving a rule,
overlay, layout or variable. While the request is in flight, focus stays wherever it was, on
confirm or on Cancel. Neither button acts: a second confirm sends nothing, and Cancel does not
close the dialog out from under an in-flight request.

Every story is independently shippable. They share no file, apart from the one new e2e file,
which is split by `test.describe` block (plan §6).

## 3. Acceptance scenarios

Legend: *held*: the test holds the request in flight until it releases it.

### US1: form dialogs (each of sites 1, 2, 3, 5, 6)

```gherkin
Scenario: Focus survives an Enter-key submit (happy path)
  Given the dialog is open with valid input
  And its submit request will be held
  When the operator focuses the submit button and presses Enter
  Then while the request is held the submit button is still document.activeElement
  And it has aria-disabled="true" and no disabled attribute
  When the request is released and succeeds
  Then the dialog closes as it does today

Scenario: Implicit submission while in flight sends nothing (the ADR-0151 §2 trap)
  Given a submit is held in flight
  When the operator presses Enter in one of the dialog's text inputs
  Then no second request is sent — the request count is still 1 after the first request settles

Scenario: A click while in flight sends nothing
  Given a submit is held in flight
  When the submit button is clicked
  Then no second request is sent

Scenario: A refused submit leaves the operator on the button (conflict / bad request)
  Given the request is answered with the dialog's existing refusal (409/412/400)
  Then the refusal renders as it does today
  And the submit button is no longer aria-disabled, still focused, and a further Enter submits again

Scenario: Validation failure is unchanged (bad request, client-side)
  Given invalid input
  When the operator submits
  Then no request is sent and the field error shows, exactly as today
```

### US2: dry run (site 4)

```gherkin
Scenario: Focus survives an Enter-key run
  Given a valid sample and the dry-run request will be held
  When the operator focuses Run and presses Enter
  Then while held, Run is document.activeElement with aria-disabled="true" and no disabled attribute

Scenario: A second activation while in flight sends nothing
  Given a dry run is held in flight
  When the operator presses Enter on Run again
  Then the dry-run request count is 1 after the first settles

Scenario: Invalid JSON is unchanged
  Given the sample is not valid JSON
  When the operator runs it
  Then no request is sent and "not valid JSON" shows, exactly as today
```

### US3: ConfirmDialog (sites 7, 8), exercised through Retire camera

```gherkin
Scenario: Focus survives an Enter-key confirm
  Given the retire confirmation is open and the retire request will be held
  When the operator focuses "Retire camera" and presses Enter
  Then while held, "Retire camera" is document.activeElement with aria-disabled="true"
  And pressing Enter on it again sends no second retire request

Scenario: Cancel keeps focus, and refuses, while a confirm is in flight
  Given the retire confirmation is open and Cancel holds focus (Radix's initial focus)
  And the retire request will be held
  When "Retire camera" is activated without moving focus (a pointer activation in a browser that does not focus on click)
  Then while held, Cancel is still document.activeElement with aria-disabled="true"
  And pressing Enter on Cancel does not close the dialog

Scenario: Cancel still cancels when nothing is in flight
  Given nothing is in flight
  When the operator activates Cancel
  Then the dialog closes and the action is called zero times, as today
```

**Auth scenario: N/A, with the reason.** No endpoint, scope or policy changes. Every request these
controls send is unchanged in method, path, body and credentials. The existing per-endpoint
401/403 coverage stands.

## 4. Requirements

- **FR-001**: Each of the eight controls announces the in-flight window with `aria-disabled`,
  through the `Button` primitive's `unavailable` prop (ADR-0151 §3), and never with native
  `disabled`.
- **FR-002**: Each form site (1, 2, 3, 5, 6) refuses submission while in flight with a guard on the
  **form's** `onSubmit`, placed before `handleSubmit`/validation. This covers the button click and
  implicit submission (ADR-0151 §2).
- **FR-003**: Sites 4 and 7 refuse activation while in flight with a guard at the top of their click
  handler (ADR-0151's spec-156 shape).
- **FR-004**: Site 8 (Cancel) refuses to close the dialog while in flight, and **keeps Radix's
  initial focus on Cancel**. The control must stay inside `RadixAlertDialog.Cancel`, because that
  wrapper is what gives Cancel the initial focus (`ConfirmDialog.test.tsx` "Puts the keyboard on
  cancel").
- **FR-005**: Outside the in-flight window every control behaves exactly as today: same labels
  (including `Saving…` / `Registering…` / `Running…` / `Creating…`), same requests, same close and
  refusal behaviour.
- **FR-006**: Each of the eight has a real-browser Playwright proof that focus survives entering
  the in-flight state. For the five form sites there is also a real-browser proof of implicit
  submission, with Enter pressed **in a text field**, not on the button (ADR-0151 Implementation
  Notes). jsdom cannot observe either (spec 160, `e2e/overlays.spec.ts:313-327`).

## 5. Assumptions and judgement calls (marked, not buried)

- **[A1] Cancel (site 8) is in scope.** It is in the issue's list. Under ADR-0151 §1 its case is
  the weaker one: Cancel is not what the operator activated. But it **can** hold focus when it
  disables, because Radix puts initial focus on it. A pointer activation of confirm in a browser
  that does not focus buttons on click (WebKit/Safari) leaves focus on Cancel as `pending` flips,
  and it blurs to `<body>`. If the reviewer rules Cancel out, drop US3's second scenario and
  plan §3.8. Nothing else depends on it.
- **[A2] Cancel's guard uses `event.preventDefault()`, which ADR-0151 does not list verbatim.** It is
  still ADR-0151 §1's "guard in the handler that refuses the action". The mechanism is Radix's
  `composeEventHandlers` skipping the close when `defaultPrevented` is set. It was verified in
  `@radix-ui/react-dialog` 1.1.x `DialogClose` and `@radix-ui/react-slot` 1.3.3 `mergeProps`
  (child handler first, then the slot's). It must be re-verified against the worktree's installed
  `@radix-ui/react-alert-dialog` 1.1.23 (tasks T000).
- **[A3] Escape while in flight is not changed.** Radix closes an alert dialog on Escape whatever
  the state of `pending`. Neither today's native `disabled` nor this change touches that. It is out
  of scope, and is recorded so that nobody reads the Cancel guard as covering it.
- **[A4] Cursor treatment mirrors the reference sites**: `className="aria-disabled:cursor-progress"`
  at each call site, per `Button.tsx`'s doc comment. See plan §7 for the collision with spec 266's
  `busy` prop.

## 6. Independent end-to-end test procedure (Phase 5)

1. Boot the stack (`aspire run`), open management-web, and sign in as the seeded operator.
2. In DevTools, enable network throttling (Slow 3G) so the in-flight window is visible.
3. For each of the eight: reach the control **by Tab**, press **Enter**, and during `Saving…`
   (etc.) run `document.activeElement` in the console. It must be the button, not `<body>`. Then
   press **Tab** once. Focus must move to the next control in the dialog, not to the first link on
   the page.
4. For each form dialog: during `Saving…`, click into a text field and press Enter. The Network tab
   must show exactly one write request.
5. Retire a camera: with focus left on Cancel, click "Retire camera" in a WebKit browser, or use
   `dispatchEvent` in Chrome. During the request, Cancel stays focused and Enter on it does not
   close the dialog.
6. Automated equivalent: `pnpm test:e2e e2e/in-flight-focus.spec.ts` against the booted stack.

## 7. Locked tech choices (unchanged)

React + TypeScript + Vite (ADR-0074), RTK Query mutation `isLoading` as the in-flight signal
(ADR-0075), Radix (ADR-0077) through the `Button` primitive's `unavailable` prop, React Hook Form
`handleSubmit` (ADR-0079), Playwright against the live Aspire stack (ADR-0108), and vitest + Testing
Library in jsdom for the attribute and guard assertions.

## 8. Phase 4a colour

**RED: behaviour-changing.** Focus now survives a submit where on `develop` it falls to `<body>`.
This is a real interaction, testable and currently broken. The submission itself still happens
once either way. The expected-red and declared-green-pin sets are listed per test in `tasks.md`.
