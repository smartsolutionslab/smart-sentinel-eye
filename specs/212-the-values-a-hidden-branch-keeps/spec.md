# Spec 212 — The values a hidden branch keeps

**Issue:** #2430 — *Toggling a conditional form section (variable type, rule action) leaves ghost field values that silently block submission with no visible error*
**Branch:** `2430-ghost-field-values-block-submission` (worktree `D:/Github/sse-2430`)
**Base:** `origin/develop` @ `1cd84443`
**Phase-4a colour:** **RED** (behaviour-changing). A click on **Define** / **Create draft** that today produces no request must produce one. A test that arrives green is a phase-4 failure, not a shortcut (ADR-0139, constitution §Testing).
**ADRs:** **ADR-0079** (React Hook Form + Zod — the decision whose default this defect lives inside), ADR-0074 (two apps; this is `management-web` only), ADR-0077 (Radix primitives — the `Dialog` whose unmount behaviour matters here), ADR-0036 (smallest possible change — it decides the fix mechanism, see §Mechanism), ADR-0108 (Playwright e2e), ADR-0109 (`[P]` markers), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane, phase-4a colours), ADR-0037 (phases).
**Constitution:** §Testing (red for new behaviour).

**No new ADR is required.** Nothing here decides architecture. ADR-0079 already chose React Hook Form and Zod; this spec fixes two forms that use them in a way the library's defaults quietly defeat. The fix stays inside the two components and one new shared composite of the kind ADR-0079 §Decision already names (`FormField` lives in `apps/shared/ui/composites/`).

**Latency budget: N/A.** `management-web` is the operator console. Nothing here is on the `event arrival → overlay rendered` path (constitution §IV); no leg is touched, none is cited.

---

## Problem

Every line number, version, default and predicate below was **re-read in the working tree at HEAD `1cd84443`** or extracted from the installed package, not copied from the issue. Where the issue's figures were confirmed, that is said; where the framing narrows, that is said too.

### The operator's experience

**System variables.** Open *New variable*, type a name, select **Type = Boolean**, change back to **Type = String**, click **Define**. Nothing happens. No request, no error text, no focus move. Clicking again does nothing. Only Cancel-and-restart recovers — and the next time the operator checks what Boolean looks like, it happens again.

**Rules.** Open *New rule*, fill it out, click the **Action** select to see both options, land back on **Set a system variable**, fill it in, click **Create draft**. The same silent nothing, permanently.

### Why — three facts compose

**1. React Hook Form does not drop a field's value when the field unmounts.**

Confirmed against the installed package, not the docs. `apps/management-web/package.json:21` pins `react-hook-form` at `7.86.0`; `node_modules/.pnpm/react-hook-form@7.86.0_react@19.2.8/node_modules/react-hook-form/package.json` reports `"version": "7.86.0"`, so the shipped source below *is* what runs.

`dist/index.esm.mjs:2061-2064`:

```js
const defaultOptions = {
    mode: VALIDATION_MODE.onSubmit,
    reValidateMode: VALIDATION_MODE.onChange,
    shouldFocusError: true,
};
```

`shouldUnregister` is **absent** from that object, so `_options.shouldUnregister` is `undefined`. The ref callback `register` returns (`dist/index.esm.mjs:3141-3143`) is the only thing that queues a field for removal:

```js
(_options.shouldUnregister || options.shouldUnregister) &&
    !(isNameInFieldArray(_names.array, name) && _state.action) &&
    _names.unMount.add(name);
```

Undefined is falsy, neither dialog passes the option, so nothing is ever added to `_names.unMount` and `_removeUnmounted` (`:2536-2545`, run from `useForm`'s effect at `:3672`) has nothing to remove. **The value survives its input's unmount.** The issue's claim holds, and the file it cited (`logic/createFormControl.ts`) is the pre-build source of the same code.

**2. Merely revealing the section writes the ghost value. The operator never types.**

`apps/management-web/src/features/systemVariables/SystemVariableDialog.tsx:124-133`:

```tsx
{selectedType === 'Boolean' && (
  <>
    <FormField label="Truthy label" ...>
      <Input id="variable-truthy" defaultValue="Yes" {...register('truthyLabel')} />
```

`defaultValue="Yes"` puts `Yes` in the DOM; RHF reads the live ref on register and records it. Selecting Boolean and changing straight back leaves `truthyLabel: 'Yes'`, `falsyLabel: 'No'` in form state with no keystroke involved.

`RuleDialog.tsx:163-177` has no `defaultValue`, and gets there by a different route — the empty controls themselves:

- `overlayIdentifier` — an empty `<Input>` registers as `''`.
- `durationMs` — `{...register('durationMs', { valueAsNumber: true })}` on an empty number input registers as **`NaN`** (`:173`).

**3. The schemas reject the ghost, and path the error to a field that is no longer on the page.**

`apps/shared/src/api/systemVariables.schema.ts:33-37` — the `superRefine`'s non-Boolean branch:

```ts
} else if (value.truthyLabel !== undefined || value.falsyLabel !== undefined) {
  ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['truthyLabel'],
    message: 'BooleanLabels can only be set on Boolean variables.' });
}
```

`errors.truthyLabel` is read only at `SystemVariableDialog.tsx:126`, inside `{selectedType === 'Boolean' && ...}`. With Type back on String that `FormField` does not exist, so the message has nowhere to render.

`apps/shared/src/api/rules.schema.ts:33-34` — and here the issue's line numbers are right for a reason worth recording, because it changes what a fix must satisfy. The rejection is **not** the `superRefine`; it is the field schemas themselves:

```ts
overlayIdentifier: z.string().uuid('Choose an overlay').optional(),
durationMs: z.number().int().min(500).max(60_000).optional(),
```

`''` fails `.uuid()`; `NaN` fails `z.number()`. Both are field-level issues, pathed to `overlayIdentifier` and `durationMs`, both read only inside the `HighlightOverlay` half of the ternary at `RuleDialog.tsx:146-177`. The `superRefine` never runs — in Zod, object-level refinement is skipped when the shape itself fails. **A fix that only relaxes the `superRefine` would not work**, and a fix that makes the schema a discriminated union would be relaxing the field schemas by removing them.

**The compound effect.** `handleSubmit` never calls the submit callback, so no mutation is dispatched. `_focusError` (`:3148-3150`) iterates `_names.mount` — the mounted set — so `shouldFocusError: true` cannot focus a field that is not in the document. Nothing in the DOM changes. The failure is total and invisible.

### Confirmed: the server would have accepted the request the browser refuses to send

`src/Automation/Api/RulesEndpoints.cs:427-443`, `BuildAction`, switches on `body.ActionType` and reads only that branch's fields:

```csharp
SetVariableValue =>
    RuleAction.SetVariableValue.From(
        body.VariableName ?? throw ...,
        body.ValueExpression ?? throw ...),
```

`OverlayIdentifier` and `DurationMs` are not read at all on the `SetVariableValue` arm. So this is a browser-side refusal with no server-side counterpart — there is no safety net below it, and equally no server contract that the fix has to negotiate with.

### Scope check: is this defect anywhere else?

Seven `useForm` call sites exist in `apps/` (`grep -rn "useForm<\|useForm(" apps --include=*.tsx --include=*.ts`, excluding tests). All seven were read.

| Component | Conditional block | Gated on | Verdict |
|---|---|---|---|
| `systemVariables/SystemVariableDialog.tsx` | truthy/falsy labels | `watch('type')` | **Defect — US1** |
| `rules/RuleDialog.tsx` | action branch ternary | `watch('actionType')` | **Defect — US2** |
| `cameras/RegisterCameraDialog.tsx` | fab select (`:95`) | `mustChooseFab` | Not a defect |
| `cameras/EditCameraAddressDialog.tsx` | none | — | Not a defect |
| `cameras/RenameCameraDialog.tsx` | none | — | Not a defect |
| `layouts/LayoutEditorDialog.tsx` | name field (`:385`) | `!isEdit` | Not a defect |
| `overlays/OverlayEditorDialog.tsx` | name field (`:304`) | `!isEdit` | Not a defect |

The four near misses are near misses for three distinct reasons, so none of them is one toggle away from the same failure:

- **`mustChooseFab`** hides a plain `<select>` bound to `useState`, not a registered field. It is not in form state at all, and its error (`fabError`) is component state rendered by its own `FormField`.
- **`!isEdit` in `LayoutEditorDialog`** hides a registered `name`, but the resolver is mode-aware — `createGridDesignerResolver(isEdit ? 'edit' : 'create')` (`:201`) — so the hidden field is not validated in the mode that hides it.
- **`!isEdit` in `OverlayEditorDialog`** hides a registered `name` under a schema that requires it, but `defaultValues` supplies `editTarget.name` in edit mode (`:151-154` re-seeds on target change), so the requirement is satisfied by a value the operator cannot see rather than violated.

In all four, the discriminant is a **prop**, not a watched form field. The operator cannot toggle it there and back inside one dialog session, which is the manoeuvre that creates a ghost. **No third instance is in scope.** No follow-up issue is proposed; there is nothing latent to file.

---

## Mechanism — what the fix is, and what it is not

Four mechanisms were considered. This is decided here rather than in `plan.md` because it changes what the acceptance scenarios can assert.

**(a) `shouldUnregister: true` on `useForm` — rejected.** Form-wide. It changes `_formValues` seeding (`:2103`), sets `_state.watch` (`:3343`), and alters dirty/reset semantics for every field in the form. ADR-0036: a bug fix changes the bug, nothing else.

**(b) `register(name, { shouldUnregister: true })` per field — rejected, and the reason is specific.** It is supported (`dist/types/validator.d.ts:34` puts it on `RegisterOptions`; `:3141` honours it) and it would be the smallest possible diff — four tokens. But it fires on **every** unmount of that input, including dialog close: `apps/shared/src/ui/primitives/Dialog.tsx` uses Radix `Portal` + `Content` with no `forceMount`, so closing unmounts every field. `SystemVariableDialog` calls `reset(DEFAULT_INPUT)` on close (`:79`) so it would not notice — but **`RuleDialog` does not reset on close** (`:39-45` resets only the mutation state and the fab), so today it retains everything on reopen. Per-field unregistration would leave it retaining name, trigger and predicate while silently blanking the action branch: a half-cleared form, which is worse than either whole answer, and a second behaviour change riding along with the fix.

**(c) A discriminated-union / stripping schema — rejected.** It would work (unknown keys are stripped), but it lands in `apps/shared`, widens the blast radius to every consumer of `DefineVariableInput` / `CreateRuleInput`, turns those types into unions that RHF field paths handle badly, and deletes the `superRefine` that deliberately asserts *"BooleanLabels can only be set on Boolean variables"*. Silent stripping is a weaker contract than the one in place.

**(d) The error-summary region alone — insufficient as a fix.** It would make the failure visible, but the message an operator would read is *"BooleanLabels can only be set on Boolean variables"* pointing at a field they cannot see and did not fill. Visible but not actionable. It is the safety net (US3), not the fix.

**Chosen: (e) drop the inactive branch's values when the discriminant changes** — an effect keyed on the watched discriminant calling RHF's own `unregister([...])` with the inactive branch's field names. It fires exactly when the operator changes the thing that makes a branch inactive, and at no other time; dialog-close behaviour is untouched in both dialogs; the schemas keep their assertions; `apps/shared` is not touched by US1 or US2.

Two implementation notes for phase 4, both verified but neither load-bearing on the acceptance criteria:

- Re-selecting Boolean re-registers `truthyLabel`/`falsyLabel`, and RHF reads the live `defaultValue="Yes"`/`"No"` from the DOM on register (`updateValidAndValue`, `:3133`) — the same route the ghost took in. Scenario **S3** asserts the round trip restores them; if it does not, that is a finding for phase 4, not an assumption this spec has made.
- If `unregister` proves to fight re-registration, `setValue(name, undefined)` reaches the same end (`.optional()` accepts `undefined`). Recorded as a fallback, not a preference.

---

## User stories

### US1 (P1) — A variable type an operator can change their mind about

An operator defining a system variable looks at what **Boolean** offers, decides on **String**, and clicks **Define**. The variable is created.

**Independently shippable.** Touches `SystemVariableDialog.tsx` and its two test files. Nothing in US2 or US3 is required for it to be true.

### US2 (P1) — A rule action an operator can change their mind about

An operator authoring a rule opens the **Action** select, sees both options, lands back on **Set a system variable**, and clicks **Create draft**. The draft is created.

**Independently shippable.** Touches `RuleDialog.tsx` and its two test files. Disjoint from US1 (ADR-0109 `[P]`).

### US3 (P2) — A refusal an operator can read

When either dialog refuses to submit, the operator is told so — in an always-mounted region that no conditional branch can hide, carrying the messages of any errors that have no visible field of their own.

**Independently shippable, and separable.** US1 and US2 remove the only known way to produce such an error, so this is defence in depth against the *class* rather than a fix for the reported instances. If review judges it out of scope for a bug fix, it drops to a follow-up issue without touching US1 or US2 — say so in the PR rather than shipping it unremarked.

---

## Acceptance scenarios (Gherkin)

### US1 — system variables

**S1 · happy (the reported defect)**
```gherkin
Given the New variable dialog is open
  And the operator has typed the name "lineStatus"
When they select Type "Boolean"
  And they select Type "String"
  And they click Define
Then defineVariable is called exactly once
  And the payload carries name "lineStatus" and type "String"
  And the payload carries neither truthyLabel nor falsyLabel
```

**S2 · happy (no regression on the untoggled path)**
```gherkin
Given the New variable dialog is open
  And the operator has typed the name "lineStatus"
When they click Define without touching Type
Then defineVariable is called with { name: "lineStatus", type: "String", initialValue: "" }
```
*(`initialValue: ""` is today's observed payload — `SystemVariableDialog.test.tsx:51-55`. The fix must not change it.)*

**S3 · happy (the round trip restores what Boolean needs)**
```gherkin
Given the New variable dialog is open
  And the operator has typed the name "doorOpen"
When they select Type "Boolean", then "String", then "Boolean" again
  And they click Define
Then defineVariable is called once
  And the payload carries type "Boolean", truthyLabel "Yes" and falsyLabel "No"
```

**S4 · conflict (a real validation failure still speaks)**
```gherkin
Given the New variable dialog is open
  And the operator has typed the name "1bad"
When they select Type "Boolean", then "String"
  And they click Define
Then defineVariable is not called
  And the name field shows "must start with a letter"
```

**S5 · bad request (a genuinely wrong Boolean is still refused)**
```gherkin
Given the New variable dialog is open
  And the operator has typed the name "doorOpen"
When they select Type "Boolean"
  And they clear the Truthy label field
  And they click Define
Then defineVariable is not called
  And a validation message is visible in the dialog
```

### US2 — rules

**S6 · happy (the reported defect)**
```gherkin
Given the New rule dialog is open
  And the operator has filled name, trigger source, trigger kind and predicate
When they select Action "Highlight an overlay"
  And they select Action "Set a system variable"
  And they fill Variable name and Value expression
  And they click Create draft
Then createRule is called exactly once
  And the payload carries actionType "SetVariableValue"
  And the payload carries neither overlayIdentifier nor durationMs
```

**S7 · happy (the reverse toggle carries nothing back either)**
```gherkin
Given the New rule dialog is open
  And the operator has filled name, trigger source, trigger kind and predicate
  And they have filled Variable name and Value expression
When they select Action "Highlight an overlay"
  And they fill Overlay with a uuid and Duration with 5000
  And they click Create draft
Then createRule is called exactly once
  And the payload carries actionType "HighlightOverlay"
  And the payload carries neither variableName nor valueExpression
```

**S8 · conflict (a real validation failure still speaks)**
```gherkin
Given the New rule dialog is open
  And the operator has filled every field except Variable name
When they select Action "Highlight an overlay"
  And they select Action "Set a system variable"
  And they click Create draft
Then createRule is not called
  And the Variable name field shows "Variable name is required for SetVariableValue"
```

**S9 · auth / fab scoping (unchanged by this fix)**
```gherkin
Given the operator is assigned to two fabs
  And the New rule dialog is open with every field filled
When they select Action "Highlight an overlay", then "Set a system variable"
  And they click Create draft without choosing a fab
Then createRule is not called
  And the Fab field shows "Choose which fab this rule belongs to."
```

### US3 — the refusal is readable

**S10 · the region speaks when no field can**
```gherkin
Given a form whose errors include a field name that is not currently rendered
When the summary is rendered with the list of field names that are
Then a role="alert" region carries that error's message
```

**S11 · the region stays quiet when every field can speak for itself**
```gherkin
Given a form whose every error names a currently-rendered field
When the summary is rendered
Then it contributes no role="alert" region
```

**S12 · the region is reachable from a hidden branch**
```gherkin
Given the New variable dialog is open
  And an error is pathed to truthyLabel while Type is String
When the operator clicks Define
Then the dialog carries a visible role="alert" region naming the problem
```
*(S12 is not reachable through the UI once US1 ships — it is written against an injected error, and that is the point: it guards the class, not the instance.)*

---

## Independent end-to-end test procedure

Performed against the real Aspire stack, by hand, without reading the unit tests. This is the phase-5 note's procedure.

1. Boot the stack (`dotnet run --project src/AppHost`) and sign in to `management-web` as an operator.
2. **System variables → New variable.** Type `lineStatus`. Set **Type** to `Boolean` — confirm *Truthy label* shows `Yes` and *Falsy label* shows `No`. Set **Type** back to `String` — confirm both disappear.
3. Click **Define**. **Expected after the fix:** the dialog closes and `lineStatus` appears in the list as a String variable. **Today:** nothing at all happens.
4. Reopen **New variable**, type `doorOpen`, toggle `Boolean → String → Boolean`, click **Define**. Confirm it is created as Boolean and the list shows the `Yes`/`No` labels.
5. **Rules → New rule.** Fill name `toggle-then-back`, trigger source `plc`, trigger kind `PlcCycleStart`, predicate `$.payload.cycleTime <= 30`. Open **Action**, choose *Highlight an overlay*, then choose *Set a system variable* again. Fill *Variable name* `lineStatus` and *Value expression* `"ok"`.
6. Click **Create draft**. **Expected after the fix:** the draft appears in the rules list. **Today:** nothing at all happens.
7. Repeat step 5 in the other direction (fill the variable branch, then switch to *Highlight an overlay*, fill overlay + duration) and confirm the draft is created as a HighlightOverlay rule.
8. Capture the network tab for steps 3, 6 and 7 — the request bodies are the evidence that the ghost fields are gone, not merely tolerated.

**What would falsify the fix:** a `POST` in step 3 that still carries `truthyLabel`, or any step where the button produces neither a request nor visible text.

---

## Locked tech choices

Nothing new is introduced. The stack in play is exactly what ADR-0079 chose and ADR-0077 renders it with:

| Concern | Choice | ADR |
|---|---|---|
| Forms | React Hook Form `7.86.0` + Zod via `zodResolver` | 0079 |
| Field rendering | `FormField` composite in `apps/shared/src/ui/composites/` | 0079, 0077 |
| Dialog | Radix `Dialog` (unmounts on close; no `forceMount`) | 0077 |
| App | `management-web` only | 0074 |
| Mutations | RTK Query (`useDefineVariableMutation`, `useCreateRuleMutation`) | 0075 |
| Unit tests | Vitest + Testing Library + `userEvent`, per-file `vi.mock` of the API module | — (existing file convention) |
| E2E | Playwright, `e2e/system-variables.spec.ts` and `e2e/rules.spec.ts` | 0108 |

**No new dependency. No new ADR. No schema change in `apps/shared` for US1 or US2** — the only `apps/shared` file this spec adds is the US3 composite.

---

## Out of scope

- **`durationMs`'s message when the operator leaves it empty on the HighlightOverlay branch.** `NaN` fails `z.number()` before the `superRefine` runs, so the visible text is Zod's `"Invalid input: expected number, received NaN"` rather than the intended *"Duration is required for HighlightOverlay"*. The field **is** rendered in that case, so the failure is visible and nothing is silently blocked — a separate, lesser defect. File it if US2's tests surface it; do not fix it here.
- **`RuleDialog`'s lack of a close-time `reset`.** Noted above as the reason mechanism (b) was rejected. Whether reopening should keep or clear the form is a product question, not this bug.
- **Any change to `apps/shared/src/api/*.schema.ts`.** Explicitly ruled out by §Mechanism (c).
- **The server side.** `RulesEndpoints.BuildAction` is correct as written and is not touched.

---

## Success criteria

- **SC-001** — S1 and S6 pass; both were observed failing first, and the failure output is quoted in the PR (ADR-0139).
- **SC-002** — S2, S4, S8 and S9 pass, and the pre-existing assertions in `SystemVariableDialog.test.tsx` and `RuleDialog.test.tsx` pass **unmodified**. An assertion that has to be edited is evidence the behaviour moved further than intended.
- **SC-003** — S3 and S7 pass: the round trip is reversible in both directions and in both dialogs.
- **SC-004** — The end-to-end procedure above completes as written, on the real stack, with the step-3/6/7 request bodies captured.
- **SC-005** (US3) — S10, S11 and S12 pass, and the summary region is rendered outside every conditional branch in both dialogs.

## Assumptions

- **A1** — The operator population reaching this is any operator who inspects the Boolean or HighlightOverlay options before deciding. No telemetry was consulted; the issue reports it as reproducible and this spec verified the mechanism, not the frequency.
- **A2** — S3's restoration of `Yes`/`No` after a round trip is asserted as the *requirement*. The mechanism that should deliver it was read in the RHF source, but not executed. If phase 4 finds it does not, the spec's requirement stands and the implementation adapts.

No `[NEEDS CLARIFICATION]` remains.
