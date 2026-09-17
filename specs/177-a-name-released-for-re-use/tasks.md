# Spec 177 — Tasks

**Issue:** #2216 · **Branch:** `fix/2216-a-name-released-for-re-use`
**Base:** `32a26c3a` (`origin/develop`) · **Phase:** 3 (Tasks) · **Date:** 2026-09-17
**Spec:** `specs/177-a-name-released-for-re-use/spec.md` ·
**Plan:** `specs/177-a-name-released-for-re-use/plan.md`

## Declarations

**Phase 4a colour: RED.** Behaviour-changing bug fix. Every new assertion is
observed **failing against `32a26c3a`** before any `src/` edit, and the failure
text is quoted verbatim in the PR body. A test that arrives green is a phase-4
failure, not a shortcut — and for this issue specifically, since #2216's whole
diagnosis is that today's behaviour passes every existing test.

**Agents: the 4a/4b split, as this repo defaults to for behaviour-changing
work.**

- **Phase 4a — `test-writer`.** Writes T001–T004 only, runs them, returns the
  **verbatim** output. Does not touch `src/`.
- **Phase 4b — `backend-engineer`.** Receives that output as its brief.
  Implements T005–T009. **May not edit the tests to reach green.**

The split is warranted here on its own merits, not only by default: the
production change is four lines across two handlers, so a single agent holding
both halves could make any test pass by widening the fix or narrowing the
assertion, and nothing downstream would show it. The red output quoted in the PR
is the only evidence a later reader can check.

**Option chosen: 1** — exclude Archived from both by-name read handlers.
Grounded in the call-site investigation recorded in `spec.md` (reader table) and
`plan.md`: the one live archived-rule reader is `ListRulesQueryHandler`
(`GET /rules?state=Archived`), a different handler this slice does not touch,
and the frontend's `getRule` endpoint has zero call sites in either app. The
issue's stated condition for preferring option 2 is not met. Option 1 also makes
the ambiguity message structurally true, because `ux_rules_fab_name_active` —
unique on `(fab, name)` filtered `state <> 'Archived'` — then guarantees two
matches means two fabs.

**New ADR: no.** FR-002 already decided this; the repository and the index
already implement it. Nothing architectural is being chosen.

**Files phase 4 may touch — exhaustive.**

Production:

- `src/Automation/Application/Queries/Handlers/GetRuleQueryHandler.cs`
- `src/Automation/Application/Queries/Handlers/DryRunRuleQueryHandler.cs`
- `src/Automation/Application/Queries/Handlers/RuleFabCandidates.cs`
- `src/Automation/Application/Queries/GetRuleErrors.cs`
- `src/Automation/Application/Queries/DryRunRuleErrors.cs`

Tests:

- `tests/Automation.Application.Tests/Queries/RuleQueryHandlerTests.cs`
- `tests/Integration.Tests/Automation/RuleLifecycleIntegrationTests.cs`

Artefacts: `specs/177-a-name-released-for-re-use/*`.

**Nothing else.** In particular **not** `RuleRepository.cs`, **not**
`RuleConfiguration.cs` or any migration (the fix depends on
`ux_rules_fab_name_active` staying exactly as it is), **not**
`ListRulesQueryHandler.cs` (it is the archived-visibility path that makes option
1 safe), **not** `RuleFabResolutionIntegrationTests.cs`, **not** any `src/Domain`
file, **not** `apps/`, **not** `src/SystemVariables/` — whose identical defect is
reported for a follow-up issue, not fixed here. Touching a file outside this list
is a stop-and-report, not a judgement call.

**Foundational / blocking:** T005 (the shared `RuleFabCandidates` signature)
blocks T006–T008. There is no `Shared.Kernel`, `Shared.Contracts`, AppHost or
Aspire-resource work in this slice, so nothing fans out widely — this is a small
serial slice and `[P]` appears only where files are genuinely disjoint.

---

## User story US1 (P1) — a re-used archived name is manageable again

### T001 [US1] — the unit red: GET resolves past an archived namesake

`tests/Automation.Application.Tests/Queries/RuleQueryHandlerTests.cs`

Add to the `---- GetRuleQuery ----` section, using `Seed(...)`, `RuleBuilder`
(ADR-0054) and `Munich`:

1. **`Get_resolves_the_live_rule_when_an_archived_one_holds_the_same_name`** —
   seed two rules named `"re-used"` in `munich`; archive the first via the
   aggregate's own `Archive(clock)` (FR-003 permits `Draft → Archived`); expect
   **success**, `State` `"Draft"`, and `RuleIdentifier` equal to the **second**
   rule's. Asserting the identifier, not only the state, is what makes the test
   unable to pass by picking the wrong row.
2. **`Get_reports_an_archived_rule_whose_name_was_never_re_used_as_not_found`** —
   seed one rule in `munich`, archive it, read it; expect
   `GetRuleError.RuleNotFound` and `HttpStatusCode.NotFound`. This pins the
   deliberate narrowing (`spec.md`, *What option 1 costs*) as intended
   behaviour, in the same commit that introduces it.
3. **`Get_still_refuses_a_cross_fab_collision_when_an_archived_namesake_exists`** —
   `munich` archived + `munich` live + `dresden` live, caller holds `Both`;
   expect `FabAmbiguous`, and assert on the **candidate collection**: exactly
   two entries, `["dresden", "munich"]`. A genuine ambiguity must survive the
   new predicate.

Sentence-style names (ADR-0053), Shouldly (ADR-0052). Prefer
`RuleAggregate.Archive(clock)` over a new builder method; add
`RuleBuilder.Archived()` only if the aggregate route genuinely does not work,
and say so in the report.

**Expected red:** (1) and (3) fail with a `FabAmbiguous` where a success /
two-entry candidate list was expected; (2) fails with a success where a
`RuleNotFound` was expected. **Any other failure is a broken arrangement —
fix the arrangement until the red is the predicted one.** Every pre-existing
test in this file must be green in the same run.

**Note for the writer:** assertion 3 depends on T003's error shape. Write T001
and T003 together and run them as one red.

### T002 [US1] — the integration red: three calls over real HTTP

`tests/Integration.Tests/Automation/RuleLifecycleIntegrationTests.cs`

`A_name_freed_by_archiving_is_readable_and_publishable_again`, reusing the
file's existing `CreateAsync` / `ReadAsync` / `UniqueName` / `DiagnoseAsync`
helpers and its `ResetAutomationAsync` isolation. Aspire fixture (ADR-0103) —
**the stack at pid 3312 is running; do not stop it and do not boot a second**.

As a single-fab `munich` operator, the issue's own reproduction:

1. `POST /rules` with a fresh name → 201, version 0.
2. `POST /rules/{name}/archive` with `If-Match: "0"` → 200.
3. `POST /rules` with the **same** name → 201 (FR-002; this already works and
   is the arrange step, so assert it explicitly — a silent failure here would
   masquerade as the defect).
4. `GET /rules/{name}` → **200**, `state` `"Draft"`, and an `ETag` matching the
   body's `version`.
5. `POST /rules/{name}/publish` with that `ETag` → 200; a re-read reports
   `"Active"`.

Steps 4 and 5 are the assertions; 1–3 are arrange and must be diagnosed loudly
if they fail.

**Expected red at step 4:** `400` with `title` `RULE_FAB_AMBIGUOUS` instead of
`200` — the defect exactly as filed, over HTTP. Step 5 then fails for want of an
ETag. Quote both.

### T005 [US1] — `RuleFabCandidates` returns distinct, ordered fabs

`src/Automation/Application/Queries/Handlers/RuleFabCandidates.cs`

`Describe(IEnumerable<Rule>) → string` becomes
`Fabs(IEnumerable<Rule>) → IReadOnlyList<string>`: distinct fab values,
`StringComparer.Ordinal` order. Keep it `internal static` in `Handlers/`, keep
the doc comment's point that naming them leaks nothing, and update it to say why
distinctness is now load-bearing.

**Blocks T006, T007, T008** — shared by both handlers and both error types.

### T006 [US1] — `GetRuleError.FabAmbiguous` carries the candidates

`src/Automation/Application/Queries/GetRuleErrors.cs`

`FabAmbiguous(string Name, string Fabs)` →
`FabAmbiguous(string Name, IReadOnlyList<string> Fabs)`, joining in the message
template. Mirror `GetVariableError.VariableFabAmbiguous`
(`src/SystemVariables/Application/Queries/GetVariableErrors.cs:20-25`) — the
existing sibling, not a new idea. Update `GetRuleFailures.FabAmbiguous` to match
(ADR-0047: build the base type).

**Code, status and wording do not move:** `RULE_FAB_AMBIGUOUS`, 400, *"'{Name}'
exists in more than one of your fabs ({…}). Name the one you mean with
?fabId=."* Only what fills the parenthesis changes, and the fact that a test can
inspect it.

Depends on T005.

### T007 [US1] — `GetRuleQueryHandler` excludes Archived and refuses on distinct fabs

`src/Automation/Application/Queries/Handlers/GetRuleQueryHandler.cs`

Two edits:

1. Add `.Where(candidate => candidate.State != RuleState.Archived)` to the
   existing chain — **after** the fab scope, never replacing or reordering it
   (ADR-0114, spec 013 FR-007). Compare the **value object**, not `.Value`:
   `RuleState` is value-converted and a member access on it fails EF
   translation, the same trap the file's own comments already record for
   `RuleName` and `FabIdentifier` (constitution §II).
2. Replace `if (matches.Count > 1)` with the distinct-fab condition from
   `plan.md`:

   ```csharp
   IReadOnlyList<string> fabsHolding = RuleFabCandidates.Fabs(matches);
   if (fabsHolding.Count > 1)
   ```

   One condition, not two — the branch becomes unreachable with fewer than two
   fabs named, which is what makes the message true by construction.

Add a `why` comment on the new predicate citing FR-002 and pointing at
`RuleRepository.GetByNameAsync`, which is the line this one now agrees with.
**No comment restating what the code does** (ADR-0036).

Depends on T005, T006. Turns T001 (1, 2) and T002 green.

### T009 [US1] — verify the whole file set is green and untouched elsewhere

Run `Automation.Application.Tests` in full and the Automation integration suite.
Confirm:

- Every new assertion green.
- Every pre-existing assertion in both edited test files green **unmodified**.
  **If one had to be edited, stop and report** — that is evidence the behaviour
  moved further than planned (`plan.md`, *Phase 4a colour*).
- `git status` shows changes only within the seven files listed under
  *Declarations*.
- Format and analyzers clean; Release build green (ADR-0084 metrics, the
  collection-expression rule at `warning`).

---

## User story US2 (P2) — an ambiguity that cannot name one fab as several

### T003 [US2] — the red on the candidate collection

`tests/Automation.Application.Tests/Queries/RuleQueryHandlerTests.cs`

`The_ambiguity_refusal_names_each_holding_fab_exactly_once` — seed an archived
`"shared"` in `munich`, a live `"shared"` in `munich`, and a live `"shared"` in
`dresden`; caller holds `Both`; assert the `FabAmbiguous`'s candidate collection
is exactly `["dresden", "munich"]`.

**Assert the collection, not the sentence.** The two existing cross-fab tests
use `Message.ShouldContain("munich")` / `ShouldContain("dresden")`, and **both
pass unchanged against today's broken `(munich, munich)` rendering** — a
substring check cannot see a duplicate, so an assertion on the rendered message
would be one that cannot fail for the defect it guards.

**Expected red:** against `32a26c3a` the candidates are a joined string
`"dresden, munich, munich"` (three rows, no `Distinct`), so the test does not
compile or does not match — either is acceptable red provided the writer reports
which, and provided the *reason* is the missing distinctness, not a typo.

Written and run with T001 as one red. **Sequencing matters:** this red is only
reachable while archived rows are still visible to the query — i.e. before T007.
After T007 it becomes a guard against reintroduction, which is why US2 is a
separate story.

### T008 [US2] — (folded into T005 + T006) the message is built from distinct fabs

No separate production edit: T005 makes the collection distinct and T006 makes
the error carry it. Listed so the story has an explicit closure and so a reviewer
can see US2 is not an unimplemented heading. Verification is T003 green plus the
two existing cross-fab tests still green unmodified.

---

## User story US3 (P3) — dry-run resolves the same name the same way

### T004 [P] [US3] — the dry-run red

`tests/Automation.Application.Tests/Queries/RuleQueryHandlerTests.cs`

`DryRun_resolves_the_live_rule_when_an_archived_one_holds_the_same_name` —
archived `"re-used"` in `munich` plus a live `"re-used"` with predicate
`$.payload.cycleTime <= 30`; dry-run with the file's existing `Sample`
(`cycleTime` 20); expect success and a matched predicate.

`[P]` **against T002 only** — different file, no shared fixture. **Not** `[P]`
against T001 or T003: same file (ADR-0109).

**Expected red:** `DryRunRuleError.FabAmbiguous` where a `DryRunResultDto` was
expected.

### T010 [US3] — `DryRunRuleErrors` + `DryRunRuleQueryHandler`

`src/Automation/Application/Queries/DryRunRuleErrors.cs` and
`src/Automation/Application/Queries/Handlers/DryRunRuleQueryHandler.cs`

The identical pair of edits as T006 and T007, on the dry-run side. Keep the
existing comment that points at `GetRuleQueryHandler` as the reason for the
shape — it is now doubly true — and extend it to cover the archived predicate.

Depends on T005. Turns T004 green.

**If US3 is dropped at review**, `GetRuleQueryHandler` and
`DryRunRuleQueryHandler` are knowingly left in disagreement with each other and
with `GetByNameAsync`, and **the PR body must say so explicitly**. A dropped
story that goes unmentioned is how this defect reached production in the first
place.

---

## Dependencies

```
T001 ─┐
T003 ─┼─ (one red run, phase 4a)
T004 ─┤        ┌─ T006 ─ T007 ─┐
T002 ─┘        │               ├─ T009
        T005 ──┤               │
               └─ T010 ────────┘
```

- **T001, T002, T003, T004 all precede every `src/` edit.** Non-negotiable: red
  first (ADR-0139, constitution §Testing).
- **T005 blocks T006, T007, T010** — the shared helper's signature.
- **T006 blocks T007**; **T010** is self-contained once T005 lands.
- **T009 last.**
- `[P]` markers: **T004 `[P]` with T002 only.** Everything else shares a file.
  This slice is small and mostly serial, and saying so is more useful than
  decorating it with markers that do not hold (ADR-0109).

## Deliberately not covered

- **`SystemVariables`' identical disagreement** between
  `VariableRepository.GetByNameAsync:30` (excludes Archived) and
  `GetVariableQueryHandler.cs:17-19` (does not). Real, found during this
  investigation, **out of scope** — different bounded context, needs its own red.
  **Action at phase 7: file a follow-up issue** citing this spec. Do not fix it
  on this branch.
- **Spec 106 / #749.** Changes no production code; must not start.
- **The archived-rule listing path.** Untouched by design.
- **The wire contract of `RULE_FAB_AMBIGUOUS`.** Code, status and sentence stay.
- **`ux_rules_fab_name_active`.** Not altered; the fix depends on it.

## Gate (phase 3)

- [ ] Tasks atomic, each naming its files and its expected red.
- [ ] `[P]` markers claim only genuinely disjoint files.
- [ ] Phase 4a colour declared: **RED**, with the predicted failure per task.
- [ ] Engineer declared: **`test-writer` (4a) → `backend-engineer` (4b)**, with
      reasoning.
- [ ] Option declared: **1**, grounded in the call-site investigation.
- [ ] No new ADR; nothing architectural taken.
- [ ] Touchable files enumerated exhaustively, with the exclusions named.
- [ ] **#2216 on Project #13** — it is already there (status Todo), carrying
      `agent:ready`. Verified via `content.url`, not by number filter.
