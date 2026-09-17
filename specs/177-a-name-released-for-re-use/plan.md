# Spec 177 — Plan

**Issue:** #2216 · **Branch:** `fix/2216-a-name-released-for-re-use`
**Base:** `32a26c3a` (`origin/develop`) · **Phase:** 2 (Plan) · **Date:** 2026-09-17
**Spec:** `specs/177-a-name-released-for-re-use/spec.md`

## The decision — option 1, restated against what the call sites showed

`spec.md` carries the reader table and the four reasons. The plan-level points:

### What was checked before choosing

Every consumer of a rule row, not only the ones the issue named:
`ListRulesQueryHandler`, `GetRuleQueryHandler`, `DryRunRuleQueryHandler`, the
three command handlers, `InMemoryRuleCache`, and the frontend RTK endpoints in
`apps/shared/src/api/rules.api.ts` with their call sites in both apps.

**One live reader of archived rules exists: `ListRulesQueryHandler`.** It takes
`rules.Rules` unfiltered and applies the caller's own `state` filter, and
`ListRulesErrors.cs:18` lists `Archived` among the legal values — so
`GET /rules?state=Archived` is the "show archived" path the issue warned might
exist. **It is a different handler and this slice does not touch it.** The
archived rule stays fully visible where the console already looks for it.

**No by-name reader needs an archived rule.** The frontend's `getRule` endpoint
has zero call sites in either app; `RulesPage` renders from the list query and
already suppresses Publish/Archive for `state: 'Archived'`. So the issue's
stated condition for preferring option 2 — a live caller that needs archived
visibility through *this* path — is not met.

### Why option 2 was rejected rather than merely not chosen

Option 2 keeps archived rows in the by-name result and teaches the ambiguity
check to ignore them. It produces the same 200 for the scenario in the issue,
so on the symptom the two options are indistinguishable. They differ on where
the invariant lives.

`ux_rules_fab_name_active` (`RuleConfiguration.cs:118-120`) is a unique index on
`(fab, name)` filtered `state <> 'Archived'`. Under option 1, excluding Archived
makes the query's result set exactly the set that index governs: at most one row
per fab, so `matches.Count > 1` and "more than one fab" become the same fact,
and the error message is true by construction. Under option 2 the query returns
rows outside the index's scope and the handler carries, in application code, a
correctness burden the schema is already carrying — and carries it in two
handlers that would then need to stay in step with each other and with
`GetByNameAsync` forever.

Option 2 also leaves the read handler as a third dialect: `GetByNameAsync` says
archived names are gone, the index says archived names are gone, and the read
would say archived names are here but discounted. Three components, two stories.

### What option 1 costs

`GET /rules/{name}` and `POST /rules/{name}/dry-run` can no longer reach an
archived rule at all — including one whose name was never re-used, which today
answers 200. Deliberate, and pinned by its own scenario (US1, *bad request*) so
it is recorded as intended rather than discovered later as a regression. The row
remains readable through `GET /rules?state=Archived`, and `RuleDto` still carries
`archivedAt` (`RuleDto.cs:44`). Nothing becomes unreachable; one route stops
answering for a row another route still serves.

**If phase 4 finds an existing test asserting 200 for an archived rule read by
name, that is a finding to report before editing anything** — it would mean a
caller this investigation missed. The premise check found no such test.

## Bounded context and layers

**Automation only.** One context, three layers touched, no cross-context
reference and no `Shared.Contracts` change.

| Layer | File | Change |
|---|---|---|
| Application / Queries / Handlers | `GetRuleQueryHandler.cs` | One `Where` predicate excluding Archived; the refusal branch keyed on distinct fabs |
| Application / Queries / Handlers | `DryRunRuleQueryHandler.cs` | The same two edits (US3) |
| Application / Queries / Handlers | `RuleFabCandidates.cs` | `Describe` → a distinct, ordinal-ordered `IReadOnlyList<string>` |
| Application / Queries | `GetRuleErrors.cs` | `FabAmbiguous` carries the candidate list, not a pre-joined string |
| Application / Queries | `DryRunRuleErrors.cs` | The same (US3) |

**Domain: unchanged.** `Rule`, `RuleState`, `ArchivedAt` and the transitions are
correct as they stand — the defect is a read that forgot a state, not a state
that is wrong.

**Infrastructure: unchanged.** `RuleRepository.GetByNameAsync` is the handler
this slice makes the others agree with. `RuleConfiguration` and every migration
are untouched; the fix depends on `ux_rules_fab_name_active` continuing to
exist exactly as it is.

**Api: unchanged.** `RulesEndpoints` keeps its routes, its `RequireScope`, its
ADR-0114 fab resolution and its ETag. The status code moves only because the
handler's `Result` changes, which is the existing `error.ToProblem()` path.

## Entities, value objects, invariants

No new entity, no new value object. The invariants this slice relies on and
must not disturb:

- **`(fab, name)` is unique across non-Archived rules** — FR-002, spec 013
  FR-004, enforced by `ux_rules_fab_name_active`. *This slice's correctness
  rests on it.* Load-bearing: if that filter were ever dropped, the refusal
  branch below would start hiding a real collision.
- **`Draft → Active → Archived`, `Draft → Archived`; Archived is terminal** —
  FR-003, `Rule.cs:96-130`. Untouched, and the reason the test scenario needs
  only three calls.
- **Only Active rules are evaluated** — FR-004. Reinforces that dry-running an
  archived rule is meaningless and that US3's narrowing loses nothing.
- **Comparisons stay on the value object.** `RuleState`, `RuleName` and
  `FabIdentifier` are value-converted (`RuleConfiguration`), so
  `x.State.Value` does not translate and throws at query-build time. The new
  predicate must read `rule.State != RuleState.Archived`, mirroring
  `RuleRepository.cs:33` — the same trap both handlers' existing comments
  already warn about for `RuleName` and `FabIdentifier`. Constitution §II.
- **Fab scope is applied to every by-name read** — ADR-0114, spec 013 FR-007.
  The new predicate joins the existing `Where` chain; it must not replace,
  reorder or short-circuit the fab scope.

## The refusal branch — how the message is made structurally honest (US2)

Today:

```csharp
if (matches.Count > 1)
{
    return Failure(GetRuleFailures.FabAmbiguous(name, RuleFabCandidates.Describe(matches)));
}
return Success(RuleMapper.Map(matches[0]));
```

`Describe` joins one entry **per row**, so a same-fab collision renders the one
fab repeated inside a sentence claiming there are several.

Planned:

```csharp
IReadOnlyList<string> fabsHolding = RuleFabCandidates.Fabs(matches);   // distinct, ordinal-ordered
if (fabsHolding.Count > 1)
{
    return Failure(GetRuleFailures.FabAmbiguous(name, fabsHolding));
}
return Success(RuleMapper.Map(matches[0]));
```

Three consequences, each deliberate:

1. **The condition becomes the thing the message asserts.** `RULE_FAB_AMBIGUOUS`
   is now unreachable with fewer than two distinct fabs named. Not "unlikely" —
   unreachable, from the branch condition itself.
2. **It is one condition, not two.** Simpler than today, not an addition.
3. **A same-fab multi-match falls through to `matches[0]`.** Stated plainly
   because it is the one place this plan trades something. With Archived
   excluded, `ux_rules_fab_name_active` makes more than one non-Archived row per
   `(fab, name)` impossible, so the branch is unreachable by construction. Were
   it somehow reached, every row is in a fab the caller already holds and is
   entitled to read — so the outcome is a row the caller is allowed to see,
   not a leak. Refusing instead would mean printing a message about a state the
   database forbids. **No speculative branch is added for it** (ADR-0036).

**Error shape follows the existing sibling, not a new idea.**
`GetVariableError.VariableFabAmbiguous(string Name, IReadOnlyList<string> Candidates)`
(`src/SystemVariables/Application/Queries/GetVariableErrors.cs:20-25`) already
carries the list and joins it in the message template. `GetRuleError.FabAmbiguous`
and `DryRunRuleError.FabAmbiguous` move to that shape. Code, status and wording
stay: `RULE_FAB_AMBIGUOUS`, 400, *"'{Name}' exists in more than one of your fabs
({…}). Name the one you mean with ?fabId=."* — the contract does not move, only
what fills the parenthesis and the fact that a test can now inspect it.

**Why carrying the list matters to the test, not just to taste.** The existing
cross-fab assertions are `Message.ShouldContain("munich")` and
`ShouldContain("dresden")`, and **both pass unchanged against today's broken
`(munich, munich)` rendering** — a substring check cannot see a duplicate. An
assertion on the rendered sentence would be an assertion that cannot fail for
the defect it is meant to guard. Asserting the distinct candidate collection can.

`RuleFabCandidates` stays `internal static` in `Handlers/` and stays shared by
both handlers — that is why it exists (its own doc comment says so), and why
US2 lands once rather than twice.

## Messaging — domain to integration event

**Nothing.** No domain event is raised, none is consumed, no integration event
is published or altered, no Wolverine registration changes, no outbox
interaction. Both handlers are `IQueryHandler<,>` implementations: reads with no
side effects. `POST /rules/{name}/dry-run` is a POST only because it carries a
body; `RulesEndpoints.cs:96-101` records that it persists nothing and publishes
nothing.

Consequently ADR-0142 / ADR-0143 do not apply: no `Idempotency-Key`, no retry
policy question, no new non-idempotent call.

## Boundary rules

- **No cross-context project reference.** Everything is inside `src/Automation`
  and its two test projects. `SystemVariables`' identical defect is *named* in
  `spec.md` and *not touched* — fixing it here would be exactly the cross-context
  drive-by NetArchTest and ADR-0036 both exist to prevent.
- **Application does not reach into Infrastructure.** Both handlers keep
  depending on `IRuleQuerySource`. The new predicate is expressed on the
  `IQueryable<Rule>` the source exposes — the same shape `ListRulesQueryHandler`
  already uses for its `state` filter, so the EF translation path is proven.
- **Domain stays pure.** No I/O, no framework reference, no change.
- **ADR-0093 layout preserved.** Handlers in `Queries/Handlers/`, errors in the
  paired `Queries/*Errors.cs`. No file moves, no new file in `src/`.
- **ADR-0084 metrics.** Both handlers gain one `Where` and swap one condition;
  neither approaches 300 LOC or complexity 10 — the refusal path loses a branch
  net of the change.
- **ADR-0109 disjointness.** The two production handler files are separate, but
  they share `RuleFabCandidates.cs` and both test edits land in one file
  (`RuleQueryHandlerTests.cs`). **US1 and US3 are therefore not `[P]` against
  each other.** Marked accordingly in `tasks.md`; the honest answer is that this
  slice is small and mostly serial.

## Phase 4a colour — RED, and what "red" must look like here

**Behaviour-changing. The new tests must be observed failing against `32a26c3a`
before any production edit, and the failure quoted verbatim in the PR.**

This is not a formality for this issue in particular. #2216's own diagnosis is
that *the current behaviour passes every existing test* — the defect's whole
character is that it is invisible to the suite. A green-on-arrival test here
would prove only that the suite still cannot see it.

**The red must be specific.** Expected: the archived-reuse assertions fail
because a `GetRuleError.FabAmbiguous` (respectively `DryRunRuleError.FabAmbiguous`)
is returned where a success was expected — not a null reference, not a builder
throw, not a missing helper. **A red for any other reason is a broken
arrangement, not evidence**, and must be fixed until the failure is the one
predicted. Spec 106's own arrange-step failure is what led here; the same trap
applies to this slice's arrange step.

**Every pre-existing test in both edited test files must be green in the same
red run.** That separates "the new assertion sees the defect" from "the new
fixture broke the file".

**After the fix, every pre-existing assertion must pass unmodified.** One is
expected to be at risk and none was found: no current test reads an archived
rule by name (case-insensitive grep for `Archiv` returns nothing in either
file). If phase 4 must edit an existing assertion to reach green, **stop and
report** — that is evidence the behaviour moved further than this plan intends.

### The counterfactual for US2

US2's assertion is on the candidate collection, so its red is structural rather
than scenario-driven: written against today's `Describe`, an assertion that the
candidates are distinct fails on the `(munich, munich)` rendering the
archived-reuse fixture produces **before** US1's predicate lands. Sequencing
matters: US2's test is written and observed red while the archived rows are
still visible, which is the only window in which a single-fab ambiguity is
reachable at all. After US1 it becomes a guard against reintroduction rather
than a reproduction — and that is the point of keeping it a separate story.

## Risks

| Risk | Mitigation |
|---|---|
| The `Where` translates at runtime but not in the in-memory fake, or vice versa | The same predicate already exists in `RuleRepository.cs:33` against the real `DbContext`, and `ListRulesQueryHandler` filters on `rule.State == parsedState` through `IRuleQuerySource`. Both paths are proven; and this slice carries a real Aspire integration test, so EF translation is exercised for real. |
| An existing caller relied on reading an archived rule by name | Enumerated: none. Pinned by a new acceptance scenario asserting the 404 so the narrowing is recorded, not assumed. |
| The error-shape change ripples to an unexpected consumer | `FabAmbiguous` is constructed only via `GetRuleFailures` / `DryRunRuleFailures` (ADR-0047), from the two handlers, and consumed only via `ApiError.ToProblem()`. `grep RULE_FAB_AMBIGUOUS` across `src`, `tests` and `apps` returns two error files, two handlers, three test assertions and one comment in `rules.api.ts:77`. The wire contract — code, status, sentence — does not move. |
| The two handlers drift apart again | They share `RuleFabCandidates`, and US3 lands the identical predicate. If US3 is dropped at review, `GetRuleQueryHandler` and `DryRunRuleQueryHandler` are knowingly left in disagreement and that must be said in the PR, not left implicit. |
| Aspire contention | The stack at **pid 3312 is running and must not be stopped**. The integration test runs against it via `AspireFixture`; `ResetAutomationAsync` is the existing per-test isolation both target files already use. One stack, one run — no second boot. |

## No new ADR

FR-002 already decided that archived names are released for re-use. The
repository and the partial unique index already implement that decision. This
slice makes two read handlers agree with it. **No architectural choice is being
taken, and none is implied.** The one thing that came close — `SystemVariables`
sharing the defect — is a bug report, not a decision, and is deliberately left
to its own issue.
