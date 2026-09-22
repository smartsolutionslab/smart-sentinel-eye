# Tasks — Spec 212, the values a hidden branch keeps

**Spec:** `specs/212-the-values-a-hidden-branch-keeps/spec.md`
**Plan:** `specs/212-the-values-a-hidden-branch-keeps/plan.md`
**Issue:** #2430
**Branch:** `2430-ghost-field-values-block-submission` (worktree `D:/Github/sse-2430`)
**Base:** `origin/develop` @ `1cd84443`
**Phase-4a colour:** **RED** (behaviour-changing). A test that arrives green is a phase-4 failure (ADR-0139).

Format: `[ID] [P?] [Story]` — `[P]` means the task owns files disjoint from every other `[P]` task at the same step (ADR-0109).

---

## Ordering

```
T001 [P] (red, US1) ──► T002 [P] (fix, US1) ──► T003 [P] (e2e, US1) ─┐
                                                                     ├─► T007 (verify) ─► T008 (review) ─► T009 (PR)
T004 [P] (red, US2) ──► T005 [P] (fix, US2) ──► T006 [P] (e2e, US2) ─┤
                                                                     │
                            T010 (US3) ──► T011 (US3) ───────────────┘
```

**T001 must be complete and its failure output captured before T002 begins; T004 before T005.** That is the phase-4a gate, not a preference (ADR-0139, ADR-0144).

**T010 is US3's red test and T011 its implementation; both are sequenced after T002 and T005** because T011 edits the same two dialog files. See §Parallelism.

## Parallelism (ADR-0109)

**The US1 and US2 lanes are fully disjoint and can run concurrently end to end.**

| Lane | Files owned |
|---|---|
| US1 (T001, T002, T003) | `apps/management-web/src/features/systemVariables/SystemVariableDialog.tsx`, `…/SystemVariableDialog.test.tsx`, `e2e/system-variables.spec.ts` |
| US2 (T004, T005, T006) | `apps/management-web/src/features/rules/RuleDialog.tsx`, `…/RuleDialog.test.tsx`, `e2e/rules.spec.ts` |
| US3 (T010, T011) | `apps/shared/src/ui/composites/FormErrorSummary.tsx` (new) + `…/FormErrorSummary.test.tsx` (new), **plus one line in each dialog** |

US3's last step reaches into both lanes' components, so it is sequenced rather than marked `[P]`. Two agents on one file is a merge conflict, not parallelism.

**There is no foundational task.** Nothing in `Shared.Kernel`, `Shared.Contracts`, `AppHost`, `src/`, `deploy/` or `apps/kiosk-web` is opened, so this spec is disjoint from every concurrently-running backend spec and blocks none of them.

---

## US1 (P1) — A variable type an operator can change their mind about

### Phase 4a — RED (`test-writer`)

**[T001] [P] [US1] Add the toggle cases to `SystemVariableDialog.test.tsx`, observe them red, report the output verbatim.**

File: `apps/management-web/src/features/systemVariables/SystemVariableDialog.test.tsx` (extend; do not restructure the existing 4 tests, do not edit their assertions — SC-002).

Reuse unchanged: the `vi.mock` of `react-oidc-context` (`:11-13`) and of `@smart-sentinel-eye/shared/api/systemVariables.api` (`:17-23`), the `defineMock` spy, `assignedGroups`, and `renderDialog()` (`:27-33`).

Cases, sentence-style strings to match the file:

1. **`Submits a String variable after the operator has looked at Boolean and changed back`** — spec **S1**. Type `lineStatus`, `selectOptions(type, 'Boolean')`, `selectOptions(type, 'String')`, click **Define**.
   Assert `defineMock` called **once**, and assert on the *actual argument*, not a subset:
   `expect(defineMock.mock.calls[0][0]).not.toHaveProperty('truthyLabel')` and the same for `falsyLabel`, plus `expect.objectContaining({ name: 'lineStatus', type: 'String' })`.
   **This is the red case.** If it is green before T002, stop and report — the premise has changed.
2. **`Restores the Boolean labels when the operator toggles there, away, and back`** — spec **S3**. Type `doorOpen`, `Boolean → String → Boolean`, **Define**. Assert one call carrying `type: 'Boolean'`, `truthyLabel: 'Yes'`, `falsyLabel: 'No'`.
   Per spec **A2**, the mechanism for this was read in the RHF source but not executed. If it comes out differently, report the observed behaviour rather than weakening the assertion.
3. **`Still refuses a name that breaks the grammar after a type toggle`** — spec **S4**. Type `1bad`, `Boolean → String`, **Define**. `defineMock` not called; `findByText(/must start with a letter/i)` present.
4. **`Still refuses a Boolean variable whose truthy label has been cleared`** — spec **S5**. Type `doorOpen`, select `Boolean`, `clear()` the *Truthy label* input, **Define**. `defineMock` not called; a validation message is findable.

Run: `pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/features/systemVariables/SystemVariableDialog.test.tsx`. The package's own `test` script is a bare `vitest run` (`apps/management-web/package.json:12`) and the root `test` script fans out across every app plus `test:guards` (`package.json:14`), so neither accepts a file filter — check the filter name against the package's `name` field before running. **Capture the full failure output verbatim**; it is the PR's red evidence (ADR-0139).

**Done when:** case 1 is observed failing with no mutation call, and the raw output is in hand.

### Phase 4b — GREEN (`frontend-engineer`)

**[T002] [P] [US1] Drop the Boolean labels when Type leaves Boolean.**

File: `apps/management-web/src/features/systemVariables/SystemVariableDialog.tsx` — this file only.

- Add `unregister` to the existing `useForm` destructure (`:41-50`).
- After the `selectedType` watch (`:56`), add one `useEffect` keyed on `[selectedType, unregister]` that calls `unregister(['truthyLabel', 'falsyLabel'])` when `selectedType !== 'Boolean'`.

Constraints:

- **An effect, not the `<select>`'s `onChange`.** The handler runs before the render that unmounts the fields; the effect runs after it. Plan §The change, precisely.
- **Do not** add `shouldUnregister` to `useForm` or to any `register` call — both were considered and rejected with reasons (spec §Mechanism a, b).
- **Do not** touch `apps/shared/src/api/systemVariables.schema.ts`.
- **Do not** edit any test. The red output from T001 is the brief.
- No drive-by comment unless the *why* is non-obvious; if one is warranted, it is the ordering constraint above, not a restatement of the code.

**Done when:** all four T001 cases and all four pre-existing cases pass, the pre-existing assertions unmodified.

**[T003] [P] [US1] Extend `e2e/system-variables.spec.ts` with the toggle-and-back define.**

Model it on the existing `operator defines a Boolean system variable and it appears in the list` (`:47`) — same fixtures, same sign-in, same list assertion. Add a test that selects Type `Boolean`, selects `String`, clicks **Define**, and asserts the variable appears in the list as a String variable.

Keep it to one case. The unit tests carry the matrix; the e2e proves it works through the gateway against the real stack (ADR-0108).

---

## US2 (P1) — A rule action an operator can change their mind about

### Phase 4a — RED (`test-writer`)

**[T004] [P] [US2] Add the toggle cases to `RuleDialog.test.tsx`, observe them red, report the output verbatim.**

File: `apps/management-web/src/features/rules/RuleDialog.test.tsx` (extend; do not restructure the existing 11 tests, do not edit their assertions).

Reuse unchanged: the `vi.mock`s, `createMock`, `assignedGroups`, `renderDialog()`, and — this one matters — the **`fill()` helper (`:38-45`)**. Its doc comment records that per-character typing across five fields put this suite within reach of the 5 s default timeout and flaked in CI. Use `fill`, not `user.type`.

Cases:

1. **`Submits a SetVariableValue rule after the operator has looked at the overlay action and changed back`** — spec **S6**. Fill name/source/kind/predicate, `selectOptions(action, 'HighlightOverlay')`, `selectOptions(action, 'SetVariableValue')`, fill variable name + value expression, **Create draft**.
   Assert `createMock` called **once**; assert on the actual argument: `not.toHaveProperty('overlayIdentifier')`, `not.toHaveProperty('durationMs')`, and `actionType: 'SetVariableValue'`.
   **This is the red case.** Green before T005 ⇒ stop and report.
2. **`Submits a HighlightOverlay rule without the variable fields it no longer uses`** — spec **S7**. Fill the variable branch first, then switch to `HighlightOverlay`, fill a uuid overlay and `5000`. Assert one call, `actionType: 'HighlightOverlay'`, `not.toHaveProperty('variableName')`, `not.toHaveProperty('valueExpression')`.
3. **`Still requires the variable fields after the action has been toggled there and back`** — spec **S8**. `createMock` not called; the *Variable name* message is findable.
4. **`Still asks a multi-fab operator to choose a fab after an action toggle`** — spec **S9**. Set `assignedGroups.current` to two fabs (mirror the existing `:151` and `:164` cases). `createMock` not called; `Choose which fab this rule belongs to.` is visible.

If case 2 surfaces Zod's `"Invalid input: expected number, received NaN"` for an empty duration, that is the out-of-scope defect the spec names — **report it, do not fix it here.**

Run: `pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/features/rules/RuleDialog.test.tsx`. **Capture the full failure output verbatim** (ADR-0139).

**Done when:** case 1 is observed failing with no mutation call, and the raw output is in hand.

### Phase 4b — GREEN (`frontend-engineer`)

**[T005] [P] [US2] Drop the inactive action branch's values when the action changes.**

File: `apps/management-web/src/features/rules/RuleDialog.tsx` — this file only.

- Add `unregister` to the existing `useForm` destructure (`:47-56`).
- After the `actionType` watch (`:65`), add one `useEffect` keyed on `[actionType, unregister]`:
  - `actionType === 'SetVariableValue'` ⇒ `unregister(['overlayIdentifier', 'durationMs'])`
  - otherwise ⇒ `unregister(['variableName', 'valueExpression'])`

Same constraints as T002: effect not handler; no `shouldUnregister` anywhere; no schema edit; no test edit; **no shared hook extracted across the two dialogs** (plan §US2 records why, so phase 6 does not raise it as an omission).

**Done when:** all four T004 cases and all 11 pre-existing cases pass, the pre-existing assertions unmodified.

**[T006] [P] [US2] Extend `e2e/rules.spec.ts` with the toggle-and-back draft.**

Model it on `operator authors a rule and it lands in their own fab without naming it` (`:19`). One case: open the action select, choose *Highlight an overlay*, choose *Set a system variable* again, fill the variable branch, **Create draft**, assert the draft appears.

---

## US3 (P2) — A refusal an operator can read

**Sequenced after T002 and T005** — T011 edits both dialog files.

**[T010] [US3] Write `FormErrorSummary.test.tsx` against the component contract, observe it red.**

New file `apps/shared/src/ui/composites/FormErrorSummary.test.tsx`. Cases from spec S10, S11, S12:

1. An error whose key is **not** in `renderedFields` ⇒ its message appears inside a `role="alert"` region.
2. Every error's key **is** in `renderedFields` ⇒ the component contributes no `role="alert"`.
3. No errors at all ⇒ renders nothing.

These are red because the file does not exist. That is a legitimate red for new behaviour; say so in the PR rather than letting it read as a trivially-passing suite.

`apps/shared` has its own `vitest run` (`apps/shared/package.json:51`) and a dozen component tests already sit beside their composites (`ErrorBoundary.test.tsx`, `OverlayGeometryFields.test.tsx`, …) — mirror one of those rather than inventing a harness. Run: `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/ui/composites/FormErrorSummary.test.tsx`.

**[T011] [US3] Add `FormErrorSummary` and wire it into both dialogs.**

- New `apps/shared/src/ui/composites/FormErrorSummary.tsx`: props `{ errors: FieldErrors; renderedFields: readonly string[] }`. Presentational — no state, no `useFormContext`, and the only `react-hook-form` import is the `FieldErrors` **type**. Returns `null` when no error falls outside `renderedFields`.
- Wire into `SystemVariableDialog.tsx` and `RuleDialog.tsx` as a sibling of the existing backend-error `<p role="alert">`, **outside every conditional branch**, with `renderedFields` computed from the same `selectedType` / `actionType` expression the branches already use (plan §US3 — two spellings of "which fields are visible" is the next instance of this defect).
- Add spec **S12** to `SystemVariableDialog.test.tsx`: inject an error pathed to `truthyLabel` while Type is `String` and assert a visible `role="alert"`. This case is unreachable through the UI once T002 ships, which is the point — it guards the class, not the instance.

**If review judges US3 out of scope for a bug fix**, drop T010 and T011 to a follow-up issue. US1 and US2 are complete without them; say which you did in the PR body rather than shipping it unremarked.

---

## Phases 5-7

**[T007] Phase 5 — verify (`/verify`).** Execute spec §Independent end-to-end test procedure against the real Aspire stack, by hand, without reading the unit tests. Capture the request bodies for steps 3, 6 and 7 — they are the evidence the ghost fields are gone rather than tolerated. Write `specs/212-the-values-a-hidden-branch-keeps/verification.md`.

**No latency figure is cited: this is not on the event→overlay path** (constitution §IV, spec §Latency budget). Say that in the note rather than omitting the subject.

Before trusting a manual observation, check the AppHost's start time against the commit — a persistent stack keeps serving the binaries it booted with.

**[T008] Phase 6 — review (`/code-review`).** Work the five items in plan §Review focus for phase 6. **`/security-review` is not required** — no auth decision, no scope check, no trust boundary, no secret, no idempotency key; S9 exists to prove the existing authorization path was not disturbed. State that in the PR rather than leaving the omission unexplained.

**[T009] Phase 7 — PR.** `gh pr create --base develop`. The body must carry:

- the **verbatim red output** from T001 and T004 (ADR-0139) — this is the only form of the evidence a later reader can check;
- `Closes #2430`, with the issue's state checked after the merge (a mention alone rarely closes it);
- `Phase 6: /security-review skipped — <the one line above>`;
- whether US3 shipped or moved to a follow-up;
- Conventional Commits per ADR-0030, **no `Co-Authored-By` footer** (ADR-0086), rebase-merge only (ADR-0087), and each commit building on its own.

**Board gate (phase 3): already satisfied — verified, not assumed.** The **feature-level** issue #2430 is on Project #13 with status `Todo`. No per-task issues are created; that stopped after spec 028, and `/speckit-taskstoissues` is deliberately not run here.

Checked with (the number filter returns zero — match on `content.url`; and `item-list` defaults to 30 items, so the limit is not optional):

```sh
gh project item-list 13 --owner smartsolutionslab --limit 2000 --format json \
  -q '.items[] | select(.content.url != null) | select(.content.url | endswith("/2430"))'
```
