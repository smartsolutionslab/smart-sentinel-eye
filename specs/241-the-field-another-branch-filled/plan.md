# Plan 241 — The field another branch filled

**Spec:** [spec.md](./spec.md) · **Issue:** #2526 · **Engineer:** `frontend-engineer`
**ADRs:** ADR-0079, ADR-0074, ADR-0036, ADR-0139, ADR-0144. **No new ADR.**
**Latency:** N/A (operator console, not on the §IV path).

## 1. Context and layers

Frontend only: `apps/management-web` → `features/rules/RuleDialog.tsx`. No bounded context, domain model, entity, value object, message or contract is touched; no `Shared.Contracts`, AppHost or Aspire change. `createRuleSchema` (`apps/shared/src/api/rules.schema.ts`) is **unchanged** — the fix is in what reaches it, not in what it accepts.

## 2. The change

Give each branch wrapper of the action ternary (`RuleDialog.tsx:171` and `:188`) a distinct, stable `key`:

```tsx
{setsVariable ? (
  <div key="set-variable" className="grid grid-cols-2 gap-3"> … </div>
) : (
  <div key="highlight-overlay" className="grid grid-cols-2 gap-3"> … </div>
)}
```

Distinct keys make React unmount one branch's inputs and mount fresh ones on every toggle, so react-hook-form's ref-attach reads an empty node instead of the previous branch's text.

**Keep #2430's `unregister()` effect (`:71-77`) exactly as it is** — spec §2 Option A. It remains what stops a hidden branch's value reaching the payload or Zod; with fresh nodes it now also means a round trip returns empty fields (accepted, spec §2). No change to `renderedFields`, `FormErrorSummary`, `onSubmit`, or `DEFAULT_INPUT`.

A short *why* comment on the ternary is warranted (non-obvious: the `key` is load-bearing, and a future edit that "tidies" identical wrappers would reintroduce the defect). It states the reason — reused DOM nodes carry text across branches — not the issue number (CLAUDE.md: no drive-by comments; references belong in the PR).

Rejected alternatives and why: spec §2 table (B reopens #2430; C re-decides spec 212's mechanism speculatively; D breaks the rendered-field contract).

## 3. Invariants

- **I1** — On any action toggle, both newly shown fields are empty in the DOM and absent/empty in form state.
- **I2** — A submitted payload contains only the active action's fields (spec 212's invariant, preserved).
- **I3** — A submitted value was typed into the field that carries its name.

## 4. Tests (phase 4a, RED) — `RuleDialog.test.tsx`

New cases, one per spec §3 scenario 1–5; each must fail on unchanged production code **on its assertion** (a DOM value or a payload), not on a lookup/compile error:

| Case | Red today because (probe-observed) |
|---|---|
| a. variable → overlay: Overlay and Duration empty | Overlay shows `oeeLine1` |
| b. overlay → variable, fill name only: Value expression empty, no request, value-expression required-error | request sent with `valueExpression: "5000"` |
| c. variable (expr `1000`) → overlay, fill Overlay only: Duration empty, no request, error on Duration | request sent with `durationMs: 1000` |
| d. round trip with Overlay replaced: Variable name and Value expression empty | Variable name shows the UUID |
| e. round trip untouched: fields empty; no request; both required-errors | request sent with the old values |

Existing tests — two edits, both to **setup/workaround only; no assertion changes**:

- `:244` *"Submits a HighlightOverlay rule without the variable fields it no longer uses"* — delete the two `user.clear(...)` calls and the workaround comment at `:246-250`. After the fix they are no-ops; removing them makes this the scenario-6 happy-path guard (it goes red on today's code because `paste` appends to the carried `oeeLine1`, producing an invalid UUID).
- `:284` *"Still asks a multi-fab operator to choose a fab after an action toggle"* — after the toggle back to *Set a system variable*, re-fill Variable name and Value expression. Its setup depended on the carryover (spec §2, last paragraph); its two `expect`s stay byte-identical. Observed: with keys and no setup change it times out on `findByText(/choose which fab/i)`.

All other 14 existing cases must pass unmodified (observed: they do, with keys applied).

## 5. Boundary rules

No cross-context references introduced. `management-web` keeps consuming the shared schema and API client as today. No new dependency.

## 6. Risks

- **R1** — An operator who toggles to peek at the other action loses what they typed (spec A1). Visible, recoverable, and on submit named by the required-errors; accepted.
- **R2** — `valueAsNumber` on a freshly mounted empty Duration yields `NaN` — the same state a first visit to that branch already produces today; not new.
