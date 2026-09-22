# Plan — Spec 212, the values a hidden branch keeps

**Spec:** `specs/212-the-values-a-hidden-branch-keeps/spec.md`
**Issue:** #2430
**Branch:** `2430-ghost-field-values-block-submission` (worktree `D:/Github/sse-2430`)
**Base:** `origin/develop` @ `1cd84443`
**Phase-4a colour:** **RED** (behaviour-changing).
**ADRs:** ADR-0079, ADR-0077, ADR-0074, ADR-0075, ADR-0036, ADR-0108, ADR-0109, ADR-0139, ADR-0144, ADR-0037.

---

## Context and layers

**There is no bounded context here, and that is the whole shape of this plan.** The defect lives entirely in the browser, in two React components of `apps/management-web`. No `src/` project is touched; no domain model, aggregate, value object, repository, command, query, handler, migration or endpoint is involved.

Concretely, this means the usual boundary machinery has nothing to say about this spec:

- **No cross-context project reference is possible** — nothing in `src/` is opened. NetArchTest's rules are not engaged.
- **No `Shared.Contracts` message** is added, changed or consumed. No domain event, no integration event, no `V<N>` contract.
- **No wire contract changes.** `CreateRuleRequest` and the define-variable body are read by `src/Automation/Api/RulesEndpoints.cs:427` and its system-variable counterpart exactly as they are today. This spec makes the browser *send* a request it currently refuses to send; the shape of that request is already what those endpoints expect.
- **No constitution §II concern.** Primitive-typed state is a domain-model rule; TypeScript form input types are not domain models, and `PrimitiveBoundaryTests` does not scan `apps/`.

Layers in play, in the repo's own vocabulary:

| Layer | Files |
|---|---|
| `management-web` feature | `src/features/systemVariables/SystemVariableDialog.tsx`, `src/features/rules/RuleDialog.tsx` |
| `apps/shared` UI composites | `src/ui/composites/FormErrorSummary.tsx` (**new**, US3 only) |
| `apps/shared` API schemas | **untouched** — see spec §Mechanism (c) |
| E2E | `e2e/system-variables.spec.ts`, `e2e/rules.spec.ts` |

## Entities, value objects and invariants

No entities and no value objects. The invariant this spec restores is a **form-state** invariant, and it is worth stating in one line because every task below exists to serve it:

> **A conditional branch that is not rendered contributes nothing to the submitted payload.**

Today both dialogs violate it in one direction (the hidden branch contributes a ghost that blocks submission) and `RuleDialog` also violates it in the other (a hidden `SetVariableValue` branch contributes `variableName`/`valueExpression` that are sent and then ignored server-side). The second violation is harmless — it blocks nothing — but it is the same invariant, the fix for it is the same three tokens, and leaving it asymmetric is what a later reader files as an oversight. **Both directions are in scope; the asymmetry is the thing being removed.** If review disagrees, dropping the reverse direction removes S7 and nothing else.

## Messaging

None. No domain event, no integration event, no RabbitMQ, no Wolverine. The only "message" is the RTK Query mutation dispatch (`useDefineVariableMutation`, `useCreateRuleMutation`) that today does not happen — ADR-0075.

## Boundary rules

- `apps/management-web` may import from `apps/shared` (US3's composite) and must not import from `apps/kiosk-web` (ADR-0074). US1 and US2 add no import at all.
- The new composite belongs in `apps/shared/src/ui/composites/`, alongside `FormField`, because ADR-0079 §Decision places form composites there and both apps may want it. It is presentational: it takes props and renders; it does not call `useFormContext`, does not import from `react-hook-form` beyond the `FieldErrors` **type**, and holds no state.

## The change, precisely

### US1 — `SystemVariableDialog.tsx`

Pull `unregister` out of the existing `useForm(...)` destructure (`:41-50`) and add one effect after the `selectedType` watch (`:56`):

- keyed on `[selectedType, unregister]`
- when `selectedType !== 'Boolean'`, `unregister(['truthyLabel', 'falsyLabel'])`

Nothing else in the file changes. `DEFAULT_INPUT`, the `reset` on close, the fab handling, the submit callback and every `FormField` stay exactly as they are.

**Ordering, and why the effect is correct rather than merely convenient.** React commits the render in which the Boolean `FormField`s unmount *before* running effects, so by the time the effect fires the inputs are gone and RHF cannot re-register them from a live ref. An `onChange` handler on the `<select>` would run *before* that render and would be racing the unmount. Use the effect.

**It must not fire on close.** The dependency is the watched value, which does not change when the dialog closes. Confirmed by construction, and S2/S3 are the tests that would catch a regression here.

### US2 — `RuleDialog.tsx`

The same shape, symmetric, after the `actionType` watch (`:65`):

- keyed on `[actionType, unregister]`
- when `actionType === 'SetVariableValue'`, `unregister(['overlayIdentifier', 'durationMs'])`
- otherwise, `unregister(['variableName', 'valueExpression'])`

**No shared hook.** Two call sites, three lines each, different field lists and a different discriminant. A `useBranchFields(...)` abstraction over two call sites is speculative generality (ADR-0036) and would make each dialog's rule harder to read than the rule itself. Recorded here so phase 6 does not raise it as an omission.

### US3 — `FormErrorSummary`

New file `apps/shared/src/ui/composites/FormErrorSummary.tsx`. Presentational, and deliberately dumb:

```
props: { errors: FieldErrors; renderedFields: readonly string[]; }
```

It collects the top-level error entries whose key is **not** in `renderedFields`, and:

- renders `null` when that collection is empty — so a form whose every error has a visible field is unchanged, and no new text competes with the inline messages;
- otherwise renders one `role="alert"` region carrying those messages.

Wired into both dialogs as a sibling of the existing backend-error `<p role="alert">`, **outside every conditional branch**, with `renderedFields` computed from the same discriminant the branches already use. That co-location is the point: the list of visible fields and the condition that makes them visible must be one expression apart, or they drift.

**Why `renderedFields` is a prop rather than inferred.** The component could sniff the DOM for a matching `FormField`, and that would be a second, implicit source of truth for something the dialog already knows explicitly. The prop keeps the knowledge where the `&&` is.

## Slicing — one spec, three stories, two of them parallel

**US1 and US2 are genuinely independent and own disjoint files** (ADR-0109 `[P]`): `features/systemVariables/**` against `features/rules/**`, plus one e2e spec each. Either can merge without the other and each fixes a complete operator-visible failure on its own. They share a *mechanism*, not a file.

**US3 touches both dialogs**, so it is sequenced after both — not because it depends on them logically, but because three agents editing two files is a merge conflict, not parallelism.

**There is no foundational task.** Nothing here blocks another spec, and nothing in `Shared.Kernel`, `Shared.Contracts`, `AppHost` or `deploy/` is opened, so any concurrently-running backend spec is fully disjoint from this one.

**Why one spec and not two.** The two dialogs are one defect with one cause and one fix shape; splitting them would produce two specs whose §Problem sections are the same three paragraphs, and would double the chance that only one of them gets the round-trip test. The stories carry the independence; the spec carries the reasoning.

## Testing strategy

### The trap these tests must not fall into

**An assertion that cannot fail.** Asserting `expect(defineMock).toHaveBeenCalled()` after a toggle is right; asserting that the dialog *contains* the string `'Define'` is not — that text is in the DOM whether the click worked or not. Every new assertion must be able to change when the subject changes.

**A test that passes before the fix.** S1 and S6 are the red evidence. If either is green against `1cd84443`, the premise has moved and phase 4 stops and reports rather than proceeding.

**Asserting absence by `toHaveBeenCalledWith` alone.** `toHaveBeenCalledWith(expect.objectContaining({...}))` cannot prove `truthyLabel` is *gone*. Assert the actual call argument — `expect(defineMock.mock.calls[0][0]).not.toHaveProperty('truthyLabel')` — or assert the whole object with `toEqual`. This is the assertion that distinguishes the chosen mechanism from one that merely made the schema tolerant.

### Phase 4a — RED (`test-writer`), all before any production edit

Reuse each file's existing machinery unmodified: the `vi.mock` of the API module, the `defineMock`/`createMock` spies, `assignedGroups`, `renderDialog()`, and in `RuleDialog.test.tsx` the `fill()` helper (`:38-45`) that exists because per-character typing put the suite near the 5 s timeout. Do not restructure the existing tests, and do not touch their assertions — SC-002 requires them to pass unmodified.

Sentence-style names with underscores are the C# convention (ADR-0053); these two files use plain sentence-style strings, so **match the neighbouring files**, not the C# rule.

### Phase 4b — GREEN (`frontend-engineer`)

The engineer receives the verbatim red output as its brief and **may not edit the tests to pass**. The only production edits are the ones in §The change, precisely.

### Coverage, analyzers, gates

- The 90/80/90 coverage gates (ADR-0065) are .NET-side and are not engaged.
- `pnpm lint`, `pnpm typecheck` and `pnpm typecheck:e2e` must be clean. Note that `typecheck:e2e` can fail on a clean `develop` for unrelated reasons (missing `@types/node` in the workspace root) — if it fails, stash and re-run before attributing it to this branch.
- No analyzer suppression, no deleted test, no lowered threshold. ADR-0144 makes weakening a gate a blocked outcome, not a judgement call.

### Security review trigger

**None.** No auth decision, no scope check, no trust boundary, no secret, no idempotency key. The fab-scoping assertion S9 exists to prove this change did **not** disturb the existing authorization path (ADR-0114's inference), not because it introduces one. `/security-review` is not required; say so in the PR rather than leaving it unexplained.

## Why this is the smallest possible change

Three production files, one of them new and optional. No dependency added. No schema touched. No shared type widened. No abstraction introduced over two call sites. The three rejected mechanisms in spec §Mechanism are each *smaller in diff* or *more general*, and each was rejected for a named consequence rather than a preference — (a) changes form-wide semantics, (b) half-clears `RuleDialog` on reopen, (c) deletes a deliberate schema assertion and widens `apps/shared`.

## Review focus for phase 6

1. **Does the effect fire only on the discriminant?** A dependency array that also carries `errors`, `formState` or an inline array literal will re-run every render and can loop.
2. **Is the ghost gone from the payload, or merely tolerated?** The `not.toHaveProperty` assertions are the ones that answer this. A green suite without them proves less than it looks like.
3. **Did any pre-existing assertion move?** SC-002. An edited assertion is evidence the behaviour went further than the spec said.
4. **Is `FormErrorSummary`'s `renderedFields` derived from the same expression as the `&&`?** Two independent spellings of "which fields are visible" is the next instance of this defect.
5. **Is the summary outside every conditional?** A safety net inside the branch it guards is not one.
