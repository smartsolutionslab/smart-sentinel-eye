# Spec 241 — The field another branch filled

**Issue:** #2526 — *RuleDialog: the action ternary's two branches share DOM nodes, so a value typed into one carries into the other*
**Branch:** `fix/2526-ruledialog-branch-key` (worktree `D:/Github/sse-2526`)
**Base:** `origin/develop`
**Phase-4a colour:** **RED** (behaviour-changing). A value the operator typed into one action's field must stop appearing in — and being submitted from — the other action's field. A test that arrives green is a phase-4 failure (ADR-0139, constitution §Testing).
**ADRs:** **ADR-0079** (React Hook Form + Zod — the uncontrolled-input model this defect lives inside), ADR-0074 (`management-web` only), ADR-0036 (smallest change), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane), ADR-0037 (phases). Prior art: **spec 212** (#2430), whose `unregister()` effect this spec interacts with and keeps.

**No new ADR is required.** Nothing here decides architecture: it corrects how one component uses React's reconciliation under the form library ADR-0079 already chose.

**Latency budget: N/A.** `management-web` is the operator console; nothing here is on the §IV event-to-overlay path.

---

## 1. Problem — observed, not taken from the issue

Every behaviour below was **observed** by a throwaway Vitest probe run against `apps/management-web/src/features/rules/RuleDialog.tsx` at `origin/develop` (react-hook-form 7.86.0), then deleted. Figures are the probe's output.

`RuleDialog.tsx:170-201` renders the two action branches as the same element tree (`div > FormField > Input` ×2) at the same position, with no `key`. React reconciles them as one subtree, so the two `<input>` DOM nodes survive a toggle and only their props (`id`, `name`, `placeholder`, `type`, `ref`) change. When the new branch's `register` ref attaches, react-hook-form finds no stored value for that field (none has a default, and #2430's effect unregisters the inactive pair) and **reads the live DOM value** — the text still sitting in the reused node.

| # | Operator does | Observed today |
|---|---|---|
| P1 | types `oeeLine1` in *Variable name*, switches to *Highlight an overlay* | *Overlay* field **displays** `oeeLine1`. Submit is blocked by the UUID check ("Choose an overlay"). |
| P4 | on *Highlight an overlay* fills Overlay `123e…4000` and Duration `5000`, switches to *Set a system variable*, types only *Variable name* | **Submitted:** `valueExpression: "5000"` — a value the operator never typed as an expression. |
| P5 | on *Set a system variable* types Value expression `1000`, switches to *Highlight an overlay*, types only *Overlay* | **Submitted:** `durationMs: 1000` — never typed as a duration. |
| P3 | fills both variable fields, switches to overlay, replaces Overlay with a UUID, switches back | **Submitted:** `variableName: "123e4567-…"` — the overlay UUID, as a variable name. |
| P2 | fills both variable fields, switches to overlay and straight back, touches nothing | Values reappear; submits `variableName: "oeeLine1", valueExpression: "42"`. |

**Two corrections to the issue.** (a) The carried value is *visible* in the new field, not "visually empty" — the defect is a wrong prefill, not an invisible one. (b) The issue's headline example (P1) is caught by Zod's UUID rule and never reaches the server. The **reachable data-integrity failures are P3, P4 and P5**: the pair `Overlay↔Variable name` and `Duration↔Value expression` each accept the other's typical content, so a carried value passes validation and is persisted into a draft rule.

## 2. The interaction with #2430's `unregister()` effect — the one judgement in this spec

`RuleDialog.tsx:71-77` (spec 212) unregisters the inactive branch's fields after every toggle, so a hidden branch's value can neither ride along in the payload nor fail Zod on a field that is not rendered.

**Verified by probe with `key`s temporarily added to both branch wrappers:**

- P1, P3, P4, P5 — the swapped-to fields mount **empty**; nothing carries; nothing unintended is submitted.
- P2 (round trip, untouched) — the fields come back **empty** and submit is refused with the visible "Variable name is required…" errors. The typed values are genuinely gone: the effect deleted them from form state, and the fresh DOM nodes hold nothing to re-read.

So the issue's warning is correct: `key` + the existing effect turns P2 from "values come back" into "values are gone".

**Why P2's "values come back" today is not behaviour worth preserving.** It is not a feature that coexists with the defect — it *is* the defect, seen from the other side. The same mechanism that restores `oeeLine1` in P2 writes the overlay UUID into *Variable name* in P3; the round trip is only faithful when the operator touched nothing on the other side. No code decides to remember a branch's values; the DOM node simply was not replaced.

**Options considered.**

| Option | Carryover (P1/P3/P4/P5) | Round trip (P2) | Cost |
|---|---|---|---|
| A. `key` per branch, keep #2430's effect unchanged | closed | fields empty; required-errors shown on submit | 2 attributes |
| B. `key` per branch, **remove** the effect | closed | values restored from RHF state (the ref re-attach writes stored values into the fresh node) | reopens #2430: hidden values ride in the payload and invalid hidden values block submit with no rendered field |
| C. `key` + keep hidden values in state but validate/submit only the active branch (custom resolver + payload strip) | closed | values restored, per branch | re-decides spec 212's mechanism; a resolver wrapper and a strip for a two-short-field convenience nobody asked for (ADR-0036: no speculative generality) |
| D. Always render both branches, hide the inactive one | closed | restored | fights the effect (hidden inputs re-register every render), breaks `FormErrorSummary`'s rendered-field contract from spec 212 |

**Decision: Option A.** Changing the action discards the fields of the action left behind. What the operator sees is exactly what is submitted, in both directions and on the round trip. The loss in P2 is **visible, not silent** — empty fields and, on submit, the named required-errors — and costs retyping at most two short fields. That is strictly better than today, where the round trip is faithful only by accident and cross-contaminates the moment the other branch is touched. If "remember each action's fields" is later wanted, it is Option C as its own issue, not a side effect of DOM reuse.

**Unavoidable consequence for one existing test** (observed by running the suite with `key`s added: 15 pass, 1 fails). `RuleDialog.test.tsx:284` *"Still asks a multi-fab operator to choose a fab after an action toggle"* fills the variable fields, toggles there and back, and expects the fab prompt. Its setup silently relied on P2's carryover; with Option A, Zod stops submit on the now-empty variable fields before the fab check runs, so the test times out on `findByText(/choose which fab/i)`. Its **assertions are correct and stay unmodified**; its **setup** must re-fill the variable fields after the toggle back. This is the behaviour move this spec intends, recorded here so the phase-6 reviewer reads the setup edit as authorised rather than as a test bent to pass.

## 3. User story

### US1 (P1) — A field shows and sends only what was typed into it

An operator creating a rule who switches the **Action** sees the newly shown fields empty, and a submitted draft carries only values typed into fields of the chosen action.

**Acceptance scenarios**

```gherkin
Scenario: A variable name does not become an overlay
  Given the New rule dialog with "oeeLine1" typed into Variable name
  When the operator switches Action to "Highlight an overlay"
  Then the Overlay field is empty
  And the Duration (ms) field is empty

Scenario: A duration does not become a value expression (was P4)
  Given the dialog on "Highlight an overlay" with Overlay and Duration "5000" filled
  When the operator switches Action to "Set a system variable" and fills only Variable name
  Then the Value expression field is empty
  And Create draft sends no request
  And "Value expression is required for SetVariableValue" is shown

Scenario: A value expression does not become a duration (was P5)
  Given the dialog on "Set a system variable" with Value expression "1000" filled
  When the operator switches Action to "Highlight an overlay" and fills only Overlay with a valid UUID
  Then the Duration (ms) field is empty
  And Create draft sends no request
  And an error is shown on Duration (ms)

Scenario: An overlay UUID does not become a variable name (was P3)
  Given both variable fields filled
  When the operator switches to "Highlight an overlay", types a UUID into Overlay, and switches back
  Then Variable name and Value expression are both empty

Scenario: Round trip discards the branch left behind, visibly (was P2 — accepted change)
  Given both variable fields filled
  When the operator switches to "Highlight an overlay" and straight back
  Then Variable name and Value expression are empty
  And Create draft sends no request and shows both required-errors

Scenario: Happy path is unchanged
  Given a fully and freshly filled HighlightOverlay rule (no stale text to clear)
  When the operator clicks Create draft
  Then exactly one request is sent with overlayIdentifier and durationMs as typed, and no variable fields
```

**Conflict / auth:** N/A — a client-side form fix; no request shape, endpoint, scope or concurrency token changes. Bad-request is covered by the scenarios above, which are the client-side refusals.

## 4. Scope

- **In:** `apps/management-web/src/features/rules/RuleDialog.tsx` (two `key` attributes) and `RuleDialog.test.tsx`.
- **Out:** `SystemVariableDialog.tsx` — its Boolean-only section has no sibling branch at the same position (confirmed by the issue; not re-opened). Remembering each action's values across toggles (Option C) — a separate feature if wanted. `e2e/rules.spec.ts:141` — toggles before typing anything, so it is unaffected; no e2e change.

## 5. Independent test procedure

1. `pnpm --filter @smart-sentinel-eye/management-web test -- RuleDialog` — all scenarios above green.
2. Manual (optional, phase 5): open *Rules → New rule*, type `oeeLine1` in Variable name, switch Action to *Highlight an overlay*: Overlay is empty. Switch back: Variable name is empty.

## 6. Assumptions

- **A1** — The discarded-on-toggle behaviour (Option A) is acceptable UX. It is the chosen default in the absence of a request to remember per-action values; flagged here so a human reader can overturn it.
