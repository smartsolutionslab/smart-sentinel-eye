# Spec 256 — The click the message moves away

**Issue:** [#2366](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2366)
— *a single click that both blurs an invalid field and hits Submit is silently
swallowed*. Labels `bug`, `agent:ready`. Found during spec 151's phase-5 browser
verification (#2346). **Board:** on 2026-09-25 a GraphQL `projectItems` query
for #2366 returned **no Project #13 item** — the phase-3 gate needs
`gh project item-add 13 --owner smartsolutionslab --url <issue-url>` (or a
re-check with a `read:project` token if that result was a scope artefact).

**Spec number.** Checked 2026-09-25 against `origin/develop` (highest 252), every
remote branch (254 claimed three times, 255 once) and every sibling worktree
(`sse-2526` holds an uncommitted 251; the main checkout holds an uncommitted
253). This spec is **256**. Re-check before the PR is opened.

**ADRs referenced:**

- **ADR-0079** (React Hook Form + Zod) — the form library whose validation
  timing §2.2 below relies on.
- **ADR-0077 / ADR-0078** (Radix + Tailwind) — the `Dialog` primitive whose
  sizing §3.3 rejects changing.
- **ADR-0151** (a disable that keeps the operator's place) — the same family of
  finding: a Save control that looks live and silently does nothing.
- **ADR-0139** and constitution §Testing — new behaviour is observed red first.
- **ADR-0144** — autonomous lane; phase-4a colour is **red** (§7).
- **ADR-0036** (smallest change), **ADR-0037** (phases and gates), **ADR-0109**
  (`[P]` rule).

**No new ADR.** This is a component-level interaction bug. No locked tech
choice, contract, bounded context, constitution clause or architectural
boundary changes; no new dependency or tooling is introduced.

**Latency budget (§IV): N/A.** `apps/shared/src/ui/composites/OverlayGeometryFields.tsx`
renders only inside the operator console's overlay editor dialog. It is not on
the event → overlay-rendered path of any kiosk or wall.

---

## 1. The defect, re-checked on this tree

Premise verified against `origin/develop` on 2026-09-25, not taken from the issue.

A pointer click is a `mousedown` + `mouseup` pair; the browser dispatches
`click` only to the nearest common ancestor of the two hit-test targets. On
`mousedown`, the focused text field blurs **before** `mouseup`. If the blur
causes a render that changes the height of anything above the submit button,
the button moves, `mouseup` lands where the button no longer is, no `click` is
dispatched to it, and the form never receives `submit`. Nothing is disabled or
refused; the operator's click simply evaporates. The second click works because
the layout has already settled.

The issue measured 852 px → 871 px on the Save button and zero `submit` events
with a capture-phase listener. Native constraint validation is not involved
(spec 151 FR-003: `type="text"` + `inputMode="decimal"`).

## 2. Which dialogs are affected — checked, not assumed

The trigger is **a render caused by blur that changes the height of content
above the submit control**. Every frontend blur handler was enumerated
(`grep onBlur|focusout|'blur'` over `apps/**`, excluding tests): there are four,
all in `apps/shared/src/ui/composites/`.

### 2.1 Affected: `OverlayEditorDialog` (create and edit mode), via `OverlayGeometryFields`

`OverlayGeometryFields.tsx:221` commits a draft on blur (spec 151 FR-004). The
commit produces **three** layout-changing renders, all above the dialog's
`Save as draft` / `Save draft` button (`OverlayEditorDialog.tsx:357`):

| # | Trigger on blur | What renders | Direction |
|---|---|---|---|
| A | Draft refused (FR-007/008/009) | `role="alert"` span under the field (`:229-233`), conditionally mounted | grows |
| B | Draft accepted, rectangle now runs off-canvas (FR-012) | advisory text in the always-mounted `role="status"` span (`:245-247`), which is zero-height while empty | grows |
| C | Draft accepted, rectangle moves back inside, or changes which edge it clips | advisory empties, or switches between its one- and two-edge wording | shrinks / changes |

The issue names only A. B and C are the same race and are fixed by the same
change; B is covered by an acceptance scenario because it is the case where the
lost click also loses a *valid* edit's save.

Not layout-changing, therefore not affected: `OverlayEditor.tsx:630` (label
focus ring — `outline`, no box change), `:715` / `:730` (`endRun`, clears an
undo-run ref; nothing renders). The geometry and undo live regions are
`sr-only` (absolutely positioned).

### 2.2 Not affected: every React Hook Form dialog using `FormField`

`FormField` (`apps/shared/src/ui/composites/FormField.tsx`) conditionally
mounts its error span, which *would* shift layout — but in no dialog is that
span ever produced by a blur:

- `LayoutEditorDialog` (incl. `GridDesigner`'s per-tile errors),
  `OverlayEditorDialog`'s own `Name` field, `RegisterCameraDialog`,
  `RenameCameraDialog`, `EditCameraAddressDialog`, `RuleDialog`,
  `SystemVariableDialog` — **all** call `useForm` with no `mode` /
  `reValidateMode`, i.e. RHF's defaults `onSubmit` / `onChange`.
- RHF 7.86.0 (the pinned version), `skipValidation`
  (`dist/index.esm.mjs:2038-2052`): with those defaults a blur event is skipped
  both before first submit (`mode.isOnSubmit` → `return true`) and after it
  (`reValidateMode.isOnChange` → `return isBlurEvent`). Errors appear on submit
  and change on keystrokes; never on blur.
- The hand-rolled `fabError` in the three fab-picking dialogs is set inside
  `onSubmit` and cleared on the select's `onChange`. `DryRunPanel`'s
  `jsonError` is set by a button click.
- `apps/kiosk-web` has no blur handlers at all.

So **`FormField` does not change**. Reserving space in it would add permanent
blank space to seven dialogs to fix a race none of them can have (ADR-0036: no
speculative generality). The latent risk — a future `useForm({ mode: 'onBlur' |
'onTouched' | 'all' })` would reintroduce the race — is recorded in §8, not
guarded here.

## 3. The fix — which "Done looks like" option survives scrutiny

### 3.1 Chosen: reserve the messages' vertical space (issue option 1)

Each message slot in `OverlayGeometryFields` — one per field, plus the advisory
— occupies, from first render, the height of **the tallest message that slot
can ever show**, at whatever width it is actually rendered. Showing, clearing or
changing a message then changes no box height, so nothing below it moves,
between `mousedown` and `mouseup` or at any other time.

Width-independence is the requirement that rules out a fixed `min-height`: the
four fields share a 4-column row in a `max-w-md` dialog, so a size message
("Height must be greater than 0% and at most 100%.") wraps to several lines,
and the line count depends on the rendered width. Any constant is either too
small (race returns) or too large at wide widths. The plan (§2) reserves by
rendering every candidate message invisibly in the same grid cell, so the cell
is always as tall as its tallest candidate at the current width.

**Cost, stated:** the panel carries a permanent blank band where messages can
appear — roughly the height of the longest wrapped size message under the four
fields, plus two advisory lines. This is the trade the issue calls "the most
robust fix, since it removes the race rather than working around it", and it is
local to one panel.

### 3.2 Rejected: commit on `pointerdown` / `mousedown` instead of blur (option 2)

It does not remove the race. `pointerdown` on the Save button *is* the press:
whatever it renders still shifts the button before the same press's `mouseup`.
Blur already fires during `mousedown`; moving the commit a few microseconds
earlier within the same press changes nothing. Deferring the render until after
`pointerup` instead would also defer `onCommit`, so the submit would read the
pre-commit label — trading a lost click for a lost edit.

### 3.3 Rejected: messages below the buttons, or a dialog-level pinned footer (option 3)

- Below the buttons separates each message from its field (four fields, one
  row) and still leaves the advisory's position arbitrary.
- Pinning the action row in `Dialog` (`apps/shared/src/ui/primitives/Dialog.tsx`)
  does not work either: the dialog is content-sized and vertically centred
  (`top-1/2 -translate-y-1/2`), so content growth moves the footer by half the
  growth until the dialog reaches `max-h-[90vh]`. Making it fixed-height would
  restyle every dialog in both apps for a defect one panel has.

## 4. User story

### US1 (P1) — One click on Save saves, whatever the geometry panel shows next

An operator types a value into Left / Top / Width / Height and, without first
pressing Tab or Enter, clicks the dialog's Save button once. The click reaches
the form: the form submits exactly as a second click does today, and any
message the blur produces appears without moving the button.

**Why P1 / why this slice:** it is the whole defect; one component, observable
end to end in one PR.

**Submit semantics are unchanged — assumption, stated.** Spec 151 FR-010: a
refused draft is not emitted and "the last committed value remains the truth".
So a single click on Save with a refused draft saves the **last committed**
geometry — exactly what today's *second* click does. This spec makes click one
behave like click two; it does not decide whether Save should be refused while
a geometry field holds a refused draft. That is a separate product question (§8).

## 5. Acceptance scenarios

```gherkin
Feature: A click on Save is not moved out from under the pointer

  Background:
    Given an operator is signed in to the operator console
    And the overlay editor dialog is open

  # Happy path — trigger B: a valid commit that raises the advisory
  Scenario: One click saves a valid, off-edge geometry typed without leaving the field
    Given a new overlay named "E2E Race <timestamp>" with the default label
    And the operator has typed "90" into Left and not left the field
    When the operator clicks "Save as draft" once
    Then the overlay appears in the overlay list
    And the saved revision's normalizedX is exactly 0.9

  # Bad request — trigger A: a refused draft
  Scenario: One click on Save while a field holds a refused value still submits
    Given a new overlay named "E2E Race <timestamp>" with the default label
    And the operator has typed "0" into Width and not left the field
    When the operator clicks "Save as draft" once
    Then the overlay appears in the overlay list
    And the saved revision's normalizedWidth is 0.3, the last committed value

  # The mechanism, observed directly
  Scenario: A message appearing on blur does not move the Save button
    Given the operator has typed "0" into Width and not left the field
    And the Save button's on-screen position has been measured
    When the Width field loses focus
    Then the Width refusal message is shown
    And the Save button's on-screen position is unchanged

  Scenario: The advisory appearing or clearing does not move the Save button
    Given the Save button's on-screen position has been measured
    When the operator commits Left "90" and then Left "10"
    Then after each commit the Save button's on-screen position is unchanged

  # Conflict — N/A to this change, stated so it is not mistaken for omitted
  Scenario: A stale-version conflict on edit is handled exactly as before
    Given the edit-mode dialog for a draft another session has since changed
    When the operator saves
    Then ChainRecoveryNotice behaves as specified by specs 155/156, unchanged

  # Auth — N/A: no endpoint, scope or token handling changes
  Scenario: An operator without the overlay write scope is refused exactly as before
    Then the gateway's 403 handling is unchanged by this spec
```

Component-level (jsdom cannot lay out, so these pin the mechanism; the e2e
scenarios above pin the outcome):

- Before any interaction, each field's message slot already contains, hidden
  from the accessibility tree, every message that field can show
  (`Enter a number.` plus its FR-008 or FR-009 range message), and the
  advisory slot contains all three FR-012 advisory wordings.
- The hidden copies never reach the accessibility tree: with no refusal,
  `queryAllByRole('alert')` is empty and the `role="status"` advisory's text is
  `''`.
- Every existing `OverlayGeometryFields.test.tsx`, `OverlayEditor*.test.tsx`
  and `OverlayEditorDialog*.test.tsx` assertion passes **unmodified** —
  `role="alert"` text, `aria-describedby` target text, `role="status"` text, and
  the advisory's `data-testid` staying mounted.

## 6. Independent end-to-end test procedure

With the Aspire stack up (`dotnet run --project src/AppHost`), in Chromium:

1. Sign in to management-web as the operator; open **Overlays → New overlay**.
2. Type a name. Click into **Width**, type `0`. **Do not** press Tab or Enter.
3. Click **Save as draft** once. *Before the fix:* a Width message appears, the
   dialog stays open, nothing is saved. *After:* the dialog closes and the
   overlay is in the list, saved with width 30% (the message may flash before
   the dialog closes; that is not asserted).
4. Repeat with Left `90` (Width untouched): one click saves, the saved revision
   has `normalizedX` 0.9 (read back through the gateway, as
   `e2e/overlays.spec.ts:125-142` does).
5. Re-open, type Width `0`, and press Tab: the Save button does not move
   (DevTools, or `getBoundingClientRect().top` before and after).

Automated as `e2e/overlays.spec.ts` (plan §4).

## 7. Phase-4a colour: **RED** (behaviour-changing)

This is a bug fix: the new tests assert behaviour the tree does not have. The
e2e scenarios and the component-level reservation test **must be observed
failing before the fix** and the failure quoted verbatim in the PR (ADR-0139,
ADR-0144). A new test that arrives green is a phase-4 failure.

The existing geometry-panel tests are the regression net and must pass
unmodified; an assertion that has to be edited is evidence the behaviour moved
— block, don't adjust.

## 8. Out of scope

- **`FormField` and the seven RHF dialogs** — not affected (§2.2). Latent risk:
  a future `useForm` with `mode: 'onBlur' | 'onTouched' | 'all'` (or
  `reValidateMode: 'onBlur'`) above a submit button reintroduces this race.
  Not guarded now (ADR-0036); worth a follow-up issue if a blur-validated RHF
  form is ever proposed.
- **Whether Save should be refused while a geometry field holds a refused
  draft.** Spec 151 FR-010 decided the last committed value is the truth; this
  spec preserves that. Candidate follow-up issue (F001 in tasks.md).
- Message wording (spec 151 FR-007–FR-012) — unchanged.
- The `Dialog` primitive's sizing — unchanged (§3.3).
