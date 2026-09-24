# Spec 231 — The stale sentence and the sticky fab error

**Issue**: #2433 · **Branch**: `fix/2433-conflict-message-and-fab-error-clear` · **Phase**: 1 (Specify)
**Date**: 2026-09-24 · **Base**: `88ba65fd` (`origin/develop`, fetched 2026-09-24)
**Context**: frontend only — `apps/management-web`. No bounded context, no C#, no `apps/shared` change.
**Engineer**: `frontend-engineer` · **Reviewer**: `frontend-reviewer`
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`
**Phase 4a colour**: **RED for both items** (§6). Both change what the operator sees.
**ADRs**: ADR-0037 (phases), ADR-0144 (the lane), ADR-0139 (new behaviour observed red),
ADR-0036 (smallest change; no new abstraction), ADR-0113 (two-layer optimistic concurrency —
why a stale write says "reload", never "try again"), ADR-0119 (the `_STALE` code suffix
`isStaleConflict` keys on), ADR-0089 (`ApiError` → ProblemDetails: code in `title`, message in
`detail`), ADR-0114 (the fab select exists only for multi-fab operators), ADR-0109 (disjoint files)
**Constitution**: §IV — **N/A**, no leg touched (management console, not the wall);
§Testing (new behaviour starts red); §II does not bind (no domain model).
**New ADR needed**: **No** (§9).

---

## 1. The premise, re-checked against `88ba65fd`

Every file and line the issue names was re-read. **Both defects hold.** Two line numbers drifted;
one test gap the issue did not mention was found.

| # | Issue claim | Status at `88ba65fd` |
|---|---|---|
| 1a | `LayoutsPage.tsx:129` passes a plain fallback to `problemDetail`, gates *Reload* on `isConflict` | **Holds exactly.** `:129` `problemDetail(mutationError, 'Could not apply that change.')`; `:130` `isConflict(mutationError) && (…Reload…)`. Import at `:11` lacks `CONFLICT_FALLBACK`, `isStaleConflict`. |
| 1b | `SystemVariablesPage.tsx:116` — same | **Holds; line is `:115`** (`:116` is the `isConflict` gate). Import at `:8` lacks the two names. |
| 1c | Reference pattern at `OverlaysPage.tsx:129`, `RulesPage.tsx:156` | **Holds exactly.** Both: `problemDetail(mutationError, isStaleConflict(mutationError) ? CONFLICT_FALLBACK : 'Could not apply that change.')`. `LayoutEditorDialog.tsx:250-253`, `OverlayEditorDialog.tsx:218-226`, `EditCameraAddressDialog.tsx:145-147`, `RenameCameraDialog.tsx:190-191` use the same helper. The four pages are the **only** `isConflict(` call sites in `apps/` — no fifth page is missed. |
| 2a | `RegisterCameraDialog.tsx:63` — `fabError` not cleared on select | **Holds exactly.** Set at `:63`; select `onChange` at `:101` is `(event) => setFabId(event.target.value)`. |
| 2b | `RuleDialog.tsx:72` — same | **Holds; set at `:93`**, select `onChange` at `:124`. |
| 2c | `SystemVariableDialog.tsx:63` — same | **Holds; set at `:72`**, select `onChange` at `:118`. |

**Shared helper for `fabError`?** None. `grep fabError|FabSelect|useFabChoice apps/` matches
exactly the three dialogs; each owns `useState<string | null>` and its own `<select>`. The fix is
three near-identical one-line edits. Extracting a shared `FabSelect`/hook is **out of scope**
(ADR-0036: no speculative generality; a refactor is not mixed into a bug fix).

**Reachability of item 1 — recorded, not a reason to drop it.** Every stale refusal the two
contexts emit (`LAYOUT_REVISION_STALE` ×5, `VARIABLE_STALE` ×2) goes through
`ApiErrorResults.ToProblem` (`src/ServiceDefaults/ApiErrorResults.cs:19`), which always sets
`detail: error.Message`. So with today's backend the operator sees the server's own sentence on
all four pages, and the fallback is reached only when a 409 `_STALE` arrives **without** a
`detail` (an intermediary rewriting the body, a future producer). The issue's "for the identical
server response" is therefore a statement about that defensive path. It is still worth fixing:
the other two pages already defend it, `problemDetail.ts` exists to make it uniform, and the
wrong fallback on this path is the one ADR-0113 warns against — a generic "could not apply"
invites a resubmit rather than a reload.

**Test gap the issue did not name.** `SystemVariableDialog.test.tsx:33-35` has a mutable
`assignedGroups` whose comment says "the multi-fab case overrides it" — **no test does**. There is
no multi-fab test in that file at all (the other two dialogs have "Refuses to submit … with no
fab chosen"). The red test for 2c therefore also supplies the missing multi-fab setup.

---

## 2. Scope decision — one PR

Both items ship in **one PR**, one red pass:

- **Same colour.** Unlike #2306 (spec 228, red + characterisation mixed), both items here are
  behaviour-changing. One test-writer pass, one verbatim red output, one engineer pass.
- **Same surface, disjoint files.** Five production files + five test files, no file shared
  between items, all in `apps/management-web`. Nothing to sequence.
- **Same issue, same size.** Each edit is one or two lines; splitting would double the phase-5/6/7
  overhead for no reviewability gain. Each item stays a separate commit (ADR-0030) so either can be
  reverted alone.

---

## 3. User stories

### US1 (P1) — A stale write reads the same on every list page

As an operator whose publish/archive/branch/revert (layouts) or set-value/archive (system
variables) was refused because someone else moved the version, I am told to reload and reapply —
on the Layouts and System Variables pages exactly as on Overlays and Rules — even when the server
sent no `detail`.

### US2 (P1) — A fixed fab choice stops being reported as missing

As a multi-fab operator who submitted a Register-camera / New-rule / New-variable dialog without
choosing a fab, once I pick a fab the "Choose which fab this … belongs to." message disappears
immediately, not on my next submit.

---

## 4. Acceptance scenarios

### US1

```gherkin
Scenario: stale refusal without detail on Layouts shows the conflict sentence   # happy / conflict
  Given the Layouts page lists a layout
  And a layout mutation failed with status 409 and body { title: "LAYOUT_REVISION_STALE" } and no detail
  Then the alert reads CONFLICT_FALLBACK ("Someone else changed this … reapply your change.")
  And the alert does not read "Could not apply that change."
  And a "Reload" button is offered

Scenario: same on System Variables                                             # happy / conflict
  Given the System Variables page lists a variable
  And a variable mutation failed with 409 { title: "VARIABLE_STALE" } and no detail
  Then the alert reads CONFLICT_FALLBACK and offers "Reload"

Scenario: the server's own detail still wins                                   # regression guard
  Given a 409 { title: "…_STALE", detail: "<server sentence>" }
  Then the alert reads "<server sentence>"

Scenario: a non-stale 409 keeps the generic fallback                           # conflict, not stale
  Given a 409 { title: "LAYOUT_NAME_TAKEN" } with no detail
  Then the alert reads "Could not apply that change."
  And does not read "someone else"

Scenario: a 400 without detail keeps the generic fallback                      # bad request
  Given a 400 { title: "…_INVALID…" } with no detail
  Then the alert reads "Could not apply that change." and offers no "Reload"
```

Auth: **N/A** — 401/403 handling is unchanged and happens before these banners (the pages are
behind the existing route guard; this spec changes wording only).

### US2 (each of the three dialogs)

```gherkin
Scenario: choosing a fab clears the missing-fab message                        # happy
  Given a multi-fab operator (groups /fabs/munich, /fabs/dresden)
  And they submitted the dialog with valid fields and no fab
  And "Choose which fab this <thing> belongs to." is shown
  When they select "dresden" in the Fab select
  Then the message is no longer shown
  And no request has been sent

Scenario: the message returns if they submit with no fab again                 # bad request
  Given the message was cleared by a selection
  When they re-select "Choose a fab…" and submit
  Then the message is shown again and no request is sent
```

Single-fab operators: **unchanged** — no select is rendered (ADR-0114), existing tests cover it.

---

## 5. Functional requirements

- **FR-001** `LayoutsPage` and `SystemVariablesPage` pass
  `isStaleConflict(mutationError) ? CONFLICT_FALLBACK : 'Could not apply that change.'` as the
  `problemDetail` fallback — character-identical to `OverlaysPage.tsx:129` / `RulesPage.tsx:156`.
- **FR-002** The *Reload* gate stays on `isConflict` on both pages (unchanged, matches the
  reference pages).
- **FR-003** In each of the three dialogs the Fab `<select>`'s `onChange` sets the value **and**
  clears `fabError` (`setFabError(null)`), unconditionally — the submit guard re-raises it if the
  empty option is chosen again. *(Explicit guess: unconditional clearing is the issue's stated fix;
  "clear only on a non-empty choice" was considered and rejected as extra branching for a message
  the next submit restores anyway.)*
- **FR-004** No new module, hook, component or export. `apps/shared` is not modified.

## 6. Phase 4a colour — RED, both items

Both items change rendered output. Tests are written first and must be **observed failing** on
`88ba65fd` with the failure quoted verbatim in the PR (ADR-0139):

- US1: asserting `CONFLICT_FALLBACK` fails because the alert reads "Could not apply that change."
- US2: asserting the message is gone after `selectOptions` fails because it is still in the DOM.

A test that arrives green is a phase-4 failure, not a shortcut.

## 7. Independent end-to-end test procedure

1. `pnpm --filter management-web test -- LayoutsPage SystemVariablesPage RegisterCameraDialog RuleDialog SystemVariableDialog`
   — the new tests red before the fix, green after; all pre-existing tests in those files green
   and **unmodified** except the harness change in plan §3.
2. Phase 5 (running app, item 2 only — item 1's path is not producible by today's backend, §1):
   boot the stack, sign in to management-web as a user in two fab groups, open Register camera,
   submit with no fab → message shown; pick a fab → message gone before any submit. Repeat for
   New rule and New variable. Item 1 is verified by the component tests; record that explicitly
   in the verification note rather than claiming a live observation.

## 8. Latency budget

**N/A.** Management console list pages and create dialogs; no leg of the event→overlay path
(constitution §IV) is touched.

## 9. ADR check

No new decision. The conflict wording follows ADR-0113/0119 via the existing helper; the fab
select follows ADR-0114. Nothing here is uncovered.

## 10. Out of scope

- Extracting a shared fab-select component or `useFabChoice` hook (a refactor; separate issue if
  wanted).
- Changing `problemDetail`, `isConflict`, `isStaleConflict` or `CONFLICT_FALLBACK`.
- Other dialogs' error clearing (none has a `fabError`; §1).
