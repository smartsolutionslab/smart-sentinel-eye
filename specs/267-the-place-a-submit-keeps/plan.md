# Plan 267: The place a submit keeps

**Spec**: [spec.md](spec.md) · **Tasks**: [tasks.md](tasks.md) · **Issue**: #2624

## Constitution / ADR check

| Check | Result |
|---|---|
| ADR-0151 §1: `aria-disabled` for a control that disables while it holds focus | Applied at all eight sites |
| ADR-0151 §2: the guard ships in the same change, and is on the form's `onSubmit` for submit buttons | §3.1–3.5 (form), §3.6–3.7 (click handler), §3.8 (Radix Close) |
| ADR-0151 §3: use the `Button` primitive's `unavailable` prop, not raw `aria-disabled` | All eight sites are already `Button` call sites |
| ADR-0151 Implementation Notes: a real-browser Enter test in a text field, and a counterfactual on the guard | §6, tasks T011–T012 |
| ADR-0036: smallest change, mirror the pattern, no new abstraction | No hook, no wrapper, no new prop. The guard is repeated inline, as the two reference dialogs repeat it |
| §II primitives / NetArchTest / backend | Not touched. Frontend only |
| §IV latency | N/A (console, not the event→overlay path) |
| Karpathy: "no drive-by comments" | One short *why* comment per guard (implicit submission is non-obvious). No issue or task references in code |

## Bounded context and layers

This is frontend only, and no bounded context is touched. It covers two workspaces:
`apps/shared/src/ui/primitives` (ConfirmDialog) and `apps/management-web/src/features/{cameras,rules,systemVariables}`
(the rest). There are no entities, value objects, events, contracts, AppHost resources or
migrations. The boundary rule is unchanged. management-web imports from `@smart-sentinel-eye/shared`
already, and no new import direction appears.

## 1. The established pattern (what every diff below mirrors)

**Reference: spec 160, the Save button in `OverlayEditorDialog.tsx` and `LayoutEditorDialog.tsx`.**
These are the only two existing `Button`-primitive `aria-disabled` sites, and they are
character-identical in the parts that matter:

```tsx
// OverlayEditorDialog.tsx:279-285 (LayoutEditorDialog.tsx:317-323 identical)
function handleFormSubmit(event: FormEvent) {
  if (saveBlocked) {
    event.preventDefault();
    return;
  }
  void onSubmit(event);
}
// :303
<form onSubmit={handleFormSubmit} className="flex flex-col gap-4">
// :357
<Button ref={saveRef} type="submit" unavailable={saveBlocked} className="aria-disabled:cursor-progress">
```

Here is how the three properties are achieved:

- **Focus retention.** `unavailable` makes `Button` emit `aria-disabled={unavailable}` and never
  the native `disabled` attribute (`apps/shared/src/ui/primitives/Button.tsx:85`), so the browser
  never runs its disable-blur.
- **Looks disabled.** `Button` adds `aria-disabled:opacity-50` whenever `unavailable !== undefined`
  (`Button.tsx:86`). The call site adds `aria-disabled:cursor-progress`, because the doc comment
  (`Button.tsx:42-45`) keeps the cursor at the call site: `cursor-progress` means *busy*. The base
  class `disabled:pointer-events-none` does **not** apply, so the control stays clickable. That is
  why the guard is mandatory.
- **Action refused.** The guard sits on the **form's** `onSubmit`, ahead of `handleSubmit`, so it
  covers both a click and implicit submission.

The raw-`<button>` reference sites use the other ADR-0151 guard shape:
`ChainRecoveryNotice.tsx:264-268`, `if (reReading) return;` at the top of `activate()`, with
`aria-disabled={reReading}` and `className` `aria-disabled:opacity-50 aria-disabled:cursor-progress`
(`:302`). That is the same visual treatment that `unavailable` plus the call-site class produce.
Sites 4 and 7 are non-form buttons and take this handler-guard shape, but through `Button`'s
`unavailable` like the Save sites.

`OverlayEditor.tsx:683-698` (Undo/Redo) uses raw `aria-disabled` with no dimming class and no
explicit guard (`undo()`/`redo()` already no-op). ADR-0151 records that difference as deliberate,
so it is not a pattern this spec needs to reconcile.

## 2. Mapping the reference onto these sites

- The in-flight signal is the mutation's `isLoading` (sites 1–6) or `pending` (7–8). There is **no
  `…Blocked` alias**. The reference needed `saveBlocked` because four conditions feed it. Here the
  condition is one variable, and naming it would be speculative.
- `handleFormSubmit` is copied inline into each dialog. Five near-identical 7-line functions is
  what the reference already does across two files. A `useSubmitGuard` hook would be new
  abstraction that ADR-0151 did not ask for (Karpathy: no speculative generality).
- `type FormEvent` is imported from `'react'`, as the reference does.

## 3. Exact diff shape per site

### 3.1 `apps/management-web/src/features/cameras/EditCameraAddressDialog.tsx`

```diff
-import { useEffect } from 'react';
+import { useEffect, type FormEvent } from 'react';
@@ after `const onSubmit = handleSubmit(...)` (ends :75)
+  // ADR-0151: `unavailable` keeps Save focusable and clickable, and no longer
+  // suppresses implicit submission (Enter in the field), so this is what refuses
+  // a second submit while the first is in flight.
+  function handleFormSubmit(event: FormEvent) {
+    if (isLoading) {
+      event.preventDefault();
+      return;
+    }
+    void onSubmit(event);
+  }
@@ :84
-      <form onSubmit={onSubmit} className="flex flex-col gap-4">
+      <form onSubmit={handleFormSubmit} className="flex flex-col gap-4">
@@ :95
-          <Button type="submit" disabled={isLoading}>
+          <Button type="submit" unavailable={isLoading} className="aria-disabled:cursor-progress">
```

### 3.2 `apps/management-web/src/features/cameras/RenameCameraDialog.tsx`

This is identical to §3.1: the import on `:16`, the guard after `onSubmit` (ends `:88`), the form
on `:97` and the button on `:108`.

### 3.3 `apps/management-web/src/features/cameras/RegisterCameraDialog.tsx`

This is identical to §3.1. The import is on `:10` (`useEffect, useEffectEvent, useState` gains
`type FormEvent`), the guard goes after `onSubmit` (ends `:99`), the form is on `:110` and the
button is on `:147`. The guard runs **before** `onSubmit`, so an in-flight Enter also skips the
missing-fab check at `:84-90`. That is correct, because nothing should be re-validated while a
submit is in flight.

### 3.4 `apps/management-web/src/features/rules/RuleDialog.tsx`

This is identical to §3.1. The import is on `:1`, the guard goes after `onSubmit` (ends `:115`),
the form is on `:124` (keeping `data-testid="rule-form"`) and the button is on `:229`.

### 3.5 `apps/management-web/src/features/systemVariables/SystemVariableDialog.tsx`

This is identical to §3.1. The import is on `:11`, the guard goes after `onSubmit` (ends `:102`),
the form is on `:123` and the button is on `:182`.

### 3.6 `apps/management-web/src/features/rules/DryRunPanel.tsx` (no form, handler guard)

```diff
   const onRun = async () => {
+    // ADR-0151: Run stays clickable while in flight (`unavailable`), so this refuses.
+    if (isLoading) return;
     const parsed = dryRunSampleSchema.safeParse(sample);
@@ :61
-      <Button type="button" onClick={onRun} disabled={isLoading}>
+      <Button type="button" onClick={onRun} unavailable={isLoading} className="aria-disabled:cursor-progress">
```

This panel sits outside any `<form>` (`RulesPage.tsx:186`) and its only field is a `<textarea>`, so
there is no implicit-submission route.

### 3.7 `apps/shared/src/ui/primitives/ConfirmDialog.tsx`: confirm (handler guard)

```diff
-            <Button variant="danger" disabled={pending} onClick={onConfirm}>
+            <Button
+              variant="danger"
+              unavailable={pending}
+              className="aria-disabled:cursor-progress"
+              onClick={() => {
+                if (pending) return;
+                onConfirm();
+              }}
+            >
```

Then extend the existing JSX comment above the button with one sentence: "`unavailable`
(ADR-0151) keeps it focusable and clickable while pending, so the click handler is what refuses."

### 3.8 `apps/shared/src/ui/primitives/ConfirmDialog.tsx`: Cancel (Radix Close guard)

```diff
             <RadixAlertDialog.Cancel asChild>
-              <Button variant="secondary" disabled={pending}>
+              <Button
+                variant="secondary"
+                unavailable={pending}
+                className="aria-disabled:cursor-progress"
+                // ADR-0151: stays focusable while pending. Radix closes on click unless the
+                // event is default-prevented, so this is what refuses the cancel.
+                onClick={(event) => {
+                  if (pending) event.preventDefault();
+                }}
+              >
```

**Why this works** (to be re-verified by T000 against the installed version):
`AlertDialog.Cancel` renders `DialogClose`, whose `onClick` is
`composeEventHandlers(props.onClick, () => context.onOpenChange(false))`. It runs the second
handler only if `!event.defaultPrevented`. With `asChild`, Radix `Slot`'s `mergeProps` calls the
**child's** handler first (`childPropValue(...args); slotPropValue(...args)`, react-slot 1.3.3), so
the child's `preventDefault()` is already set when the composed close checks it.

**Why Cancel stays wrapped in `RadixAlertDialog.Cancel`:** the alert dialog focuses its Cancel on
open. The "Puts the keyboard on cancel" test pins this, and unwrapping Cancel would lose it.

The ConfirmDialog doc comment on `pending` ("Blocks a second submit.") stays true. Do not edit it.

## 4. Tests: jsdom (attribute and guard; cannot see focus)

jsdom does not implement the disable-blur ("focus fixup"). A vitest `activeElement` assertion
passes on `develop` too, so jsdom tests here assert only the **attribute** and the **guard**. Do
not assert class names: `Button.test.tsx` owns those, and spec 266 (#2336) is rewriting them.

Each mutation hook is mocked today with a static `{ isLoading: false }`. Make the mocked state
mutable in the same way `RegisterCameraDialog.test.tsx` already makes `assignedGroups` mutable (a
module-level `{ current: false }` object read by the mock factory, and reset in `beforeEach`).

| Test file | New cases | On `develop` |
|---|---|---|
| `cameras/EditCameraAddressDialog.test.tsx` | (a) `isLoading` true: Save has `aria-disabled="true"` and **no** `disabled` attribute. (b) `isLoading` true: `fireEvent.submit(form)` calls the mutation 0 times. (c) pairing: `isLoading` false, same submit, called once | (a) red, (b) red (no guard on `develop`; native `disabled` does not stop a dispatched `submit`), (c) green pin |
| `cameras/RenameCameraDialog.test.tsx` | same (a)(b)(c) | same |
| `cameras/RegisterCameraDialog.test.tsx` | same (a)(b)(c) | same |
| `rules/RuleDialog.test.tsx` | same (a)(b)(c); form via `getByTestId('rule-form')` | same |
| `systemVariables/SystemVariableDialog.test.tsx` | same (a)(b)(c) | same |
| `rules/DryRunPanel.test.tsx` (**new**) | (a) `isLoading` true: Run `aria-disabled="true"`, no `disabled`. (b) `isLoading` true: `fireEvent.click(Run)` calls the dry-run trigger 0 times. (c) pairing: false, valid sample, once | (a) red, (b) green pin (React drops clicks on natively disabled buttons), (c) green |
| `apps/shared/src/ui/primitives/ConfirmDialog.test.tsx` | (a) `pending`: both buttons `aria-disabled="true"`, no `disabled`. (b) `pending`: `fireEvent.click(Cancel)` calls `onOpenChange` 0 times. (c) pairing: not pending, Cancel calls `onOpenChange(false)` once. **Existing** "Refuses a second confirmation while in flight" stays **unmodified**, because it is the confirm guard's pin | (a) red, (b) green pin, (c) green |

The `fireEvent.submit` in (b) is a **form-level** guard test, not an implicit-submission proof.
ADR-0151 is explicit that `user-event`'s Enter is a synthetic click. Implicit submission is proven
only in §6, and so is focus, because jsdom cannot fail on it (spec 160,
`e2e/overlays.spec.ts:313-327`).

## 5. (reserved; section numbers kept stable for tasks.md references)

## 6. Tests: Playwright (`e2e/in-flight-focus.spec.ts`, **new**)

It is a new file, so it is disjoint from `e2e/interaction-states.spec.ts` (spec 266) and from the
existing area specs. It runs in the `chromium` project, with automatic sharding and no CI
registration. It follows the `e2e/overlays.spec.ts:334-499` recipe, with one deliberate
tightening: **the hold is released by the test**, not by a 1 s `setTimeout`. That makes the
in-flight window as long as the assertions need and keeps waits condition-based (ADR-0150).

The file-local helper looks like this (it is not shared, because one caller does not earn a
support module):

```ts
/** Holds every matching request until release(); counts them at the route. */
async function holdWrites(page: Page, matches: (url: URL) => boolean, method: string) {
  let count = 0;
  let release!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route(matches, async (route) => {
    if (route.request().method() !== method) return route.fallback();
    count += 1;
    await gate;
    await route.continue();
  });
  return { count: () => count, release };
}
```

Always call `release()` in a `finally`, so that a red run does not leave the page wedged.

Route predicates (discriminate by method; list GETs share the URL):

| Site | Method | Path predicate (gateway `pathname`) |
|---|---|---|
| Register | POST | ends `/camera-catalog/cameras` |
| Rename, Correct address | PATCH | matches `/camera-catalog/cameras/[^/]+$` |
| Retire (ConfirmDialog) | POST | ends `/retire` under `/camera-catalog/cameras/` |
| Create rule | POST | ends `/automation/rules` |
| Dry run | POST | ends `/dry-run` under `/automation/rules/` |
| Define variable | POST | ends `/system-variables/system-variables` |

Each form-site test (sites 1, 2, 3, 5, 6) runs these steps:

1. Sign in (`signInAsOperator`). Seed through the UI as needed: a camera named `E2E Focus …`, which
   the retire teardown's `DISPOSABLE` regex sweeps, and a kebab-case rule name for the rule tests.
2. Open the dialog, fill valid input, and call `holdWrites(...)`.
3. `await submit.focus(); await expect(submit).toBeFocused(); await page.keyboard.press('Enter');`
4. Wait until `count() === 1` (`expect.poll`). This proves the request is in flight.
5. **The red assertion**: `await expect(submit).toBeFocused();` and
   `await expect(submit).toHaveAttribute('aria-disabled', 'true');`. Use an accessible-name regex
   covering both labels, for example `/^(save|saving…)$/i`, because the label swaps for exactly
   this window (`overlays.spec.ts:364-372`).
6. **Implicit submission**: `await <text input>.press('Enter');`. This focuses the input and
   submits the form implicitly.
7. `release()`. Wait for a settle point **after the mutation's own response**: the dialog closing
   *and* the list or heading showing the result. Only then assert `expect(count()).toBe(1)`.
   Asserting right after `press()` could pass while a regressed second request is still being
   issued (ADR-0151 Implementation Notes, spec 160 phase-6 finding 2).

Site 4 (DryRun) repeats steps 3–5 on Run. Its step 6 is a second `keyboard.press('Enter')` with
focus still on Run, and its settle point is `dry-run-result` becoming visible.

Site 7 (Retire, confirm) repeats steps 3–5 on "Retire camera". Its step 6 is a second Enter on the
button, and its settle point is the dialog closing.

Site 8 (Retire, Cancel):

- `await expect(cancel).toBeFocused()`, which is Radix's initial focus.
- `await retire.dispatchEvent('click')`. This activates without moving focus, the WebKit pointer
  path. Programmatic activation is used for the same reason `overlays.spec.ts:420-425` uses
  programmatic focus: the mechanism does not care how it got there.
- Wait until `count() === 1`, then assert `cancel` `toBeFocused()` and `aria-disabled="true"`.
- `await page.keyboard.press('Enter')` (on Cancel), then
  `await expect(page.getByRole('alertdialog')).toBeVisible()`, with the request still held.
- `release()`. The dialog then closes on success.

**Expected on `develop`**: every step-5 focus assertion is **red**, because the native `disabled`
blurs the button to `<body>`. Every `count() === 1` assertion is **green**, because native
`disabled` suppresses implicit submission and refuses clicks. That makes the count assertions
**characterisation of the regression risk**, not red evidence. Their discrimination is proven by
counterfactual after the fix: remove one form guard and watch `Expected: 1, Received: 2` (tasks
T013). Cancel's "still visible" assertion is red on `develop` only by way of its focus assertion,
which fails first. Its own discrimination is proven by the same counterfactual (remove the Cancel
guard, and the dialog closes).

## 7. Collision with in-flight work (textual, not logical)

The issue's "no dependency on #2336" is right about **behaviour** but not about **lines**:

- **#2336 / spec 266** (`feat/2336-interaction-states`, unmerged) adds `busy={isLoading}` "next to
  the existing prop" at **the same eight lines** plus the two reference Save buttons, and
  `busy={pending}` on ConfirmDialog's confirm. Its `busy` supplies `cursor-progress` and
  `aria-busy`. It also replaces `aria-disabled:opacity-50` with `aria-disabled:text-fg-disabled` in
  `Button.tsx` and in the two SaveGate tests' class assertions, and it adds `aria-busy` cases to
  `ConfirmDialog.test.tsx`.
- **#2335 / spec 266** (`feat/2335-radix-primitives`, unmerged) edits `RuleDialog.tsx` (Controller
  for the action select) and ConfirmDialog behaviour around DropdownMenu.

**Rebase rule** (for whichever merges second): keep **both** props on each button, `busy={x}` and
`unavailable={x}`. If `busy` has landed, drop this spec's call-site
`className="aria-disabled:cursor-progress"` at these eight sites, because `busy` already applies
`cursor-progress` for exactly the same window. Keeping it would be harmless but redundant. This is
a mechanical merge, not a design choice. Spec 266's own plan §3 says `busy` "never disables; pass
`disabled` or `unavailable` for that", and §6 names these eight sites as ADR-0151's follow-up.
Neither the jsdom tests (§4, no class assertions) nor the e2e (attributes and focus only) depend on
which lands first.

## 8. Commit sequence (each builds on its own, ADR-0087)

1. `test(ui): pin focus through an in-flight submit at the eight ADR-0151 sites`: all §4 and §6
   tests. Red as declared.
2. `fix(ui): keep focus on in-flight submits in the camera, rule and variable dialogs`: §3.1–3.6.
3. `fix(shared): keep focus on ConfirmDialog's buttons while pending`: §3.7–3.8.

Commits 2 and 3 touch disjoint files. Tests from commit 1 go green across them. Per ADR-0087, each
commit must build, and CI runs the tip: commit 1 alone is expected-red on tests, not on build or
typecheck.

## 9. Verification (Phase 5)

Spec §6 on a booted stack, with the e2e run quoted. There is no latency figure to cite (§IV N/A).

## 10. Risks

| Risk | Mitigation |
|---|---|
| A form converted without its guard ships a double submit (the ADR-0151 trap) | Guard and button in the same commit per file. Implicit-submission e2e per form site. Counterfactual T013 |
| The Radix Cancel guard depends on handler order in the installed version | T000 re-reads the installed `react-alert-dialog` / `react-dialog` / `react-slot` / `primitive` sources before 4a |
| An e2e asserts too early and passes on a regression | Count asserted only after a post-response settle point (§6 step 7). The counterfactual is quoted in the PR |
| Merge conflict with #2336 / #2335 | §7 rebase rule. Neither this spec's tests nor spec 266's depend on the other's classes |
