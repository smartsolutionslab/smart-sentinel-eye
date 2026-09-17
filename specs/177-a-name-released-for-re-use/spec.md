# Spec 177 — A name released for re-use

**Issue:** #2216 · **Branch:** `fix/2216-a-name-released-for-re-use`
**Base:** `32a26c3a` (`origin/develop`) · **Phase:** 1 (Specify) · **Date:** 2026-09-17
**Surfaced by:** spec 106 (#749), whose *arrange* step failed on this. #749 changes
no production code and stays that way — this is the separate correctness issue
ADR-0144 requires when a test issue turns out to be a bug.
**ADRs:** ADR-0037 (phases and gates), ADR-0047 (`Result<T, Error>` + `ApiError`,
and the `*Failures` factory that builds the base type), ADR-0052 (xUnit +
Shouldly), ADR-0053 (sentence-style test names), ADR-0054 (hand-written
builders), ADR-0084 (300 LOC/file, complexity ≤ 10), ADR-0093 (`Queries/` +
`Handlers/` + paired `*Errors.cs` layout), ADR-0103 (integration tests against
the Aspire fixture, no Testcontainers), ADR-0105 (`Ensure.That`), ADR-0109
(disjoint files for `[P]`), ADR-0113 (two-layer optimistic concurrency — the
`If-Match` version this defect makes unreachable), ADR-0114 (the fab-resolution
decision table), ADR-0139 / ADR-0144 (two testing obligations; the architect
declares the colour).
**Constitution:** §II (value objects — the comparisons stay on the value object),
§Testing (two obligations; this slice is **red**).
**Specs:** 007 FR-002 (the rule being enforced), 007 FR-003 (`Draft → Archived`
is a legal transition, so the scenario is reachable in three calls), 013 FR-004
(uniqueness is per fab), 013 FR-007 (a rule in a fab you do not hold reads as
not-found).
**§IV latency:** **N/A.** Nothing here sits on the `event → overlay` path. The
rule *evaluation* path reads `InMemoryRuleCache`, which is populated from
`IRuleRepository` and is not touched. This slice changes two by-name **read**
handlers behind `GET /rules/{name}` and `POST /rules/{name}/dry-run`, both
operator-console calls off the SLO path.
**New ADR needed:** **No.** FR-002 already decided that archived names are
released for re-use, and `RuleRepository.GetByNameAsync` plus the partial unique
index already implement that decision. This slice makes the two read handlers
agree with a rule that was already made. No new architectural choice is taken.

## The defect in one line

`RuleRepository.GetByNameAsync` excludes Archived rows; `GetRuleQueryHandler`
and `DryRunRuleQueryHandler` do not. So re-using an archived name — which
FR-002 explicitly permits — leaves the new rule readable by nobody and
manageable by nobody.

## Premise check — every claim in #2216, verified by reading the code

### Confirmed

- **`RuleRepository.GetByNameAsync` excludes Archived.**
  `src/Automation/Infrastructure/Persistence/RuleRepository.cs:33` —
  `.Where(rule => rule.State != RuleState.Archived)`, with the comment *"FR-002:
  archived names are released for re-use; ignore Archived rows."* The interface
  doc (`src/Automation/Domain/Rule/IRuleRepository.cs:6-9`) says the same.
- **`GetRuleQueryHandler` does not exclude Archived.**
  `src/Automation/Application/Queries/Handlers/GetRuleQueryHandler.cs:52-54` —
  the only predicates are `scopedFabs.Contains(candidate.Fab)` and
  `candidate.Name == parsed`. State is never mentioned in the file.
- **Two matches therefore become `RULE_FAB_AMBIGUOUS`.** `:59-62`, on
  `matches.Count > 1`, via `RuleFabCandidates.Describe(matches)`.
- **The message cannot describe what happened.**
  `GetRuleErrors.cs:22-27` renders *"'{Name}' exists in more than one of your
  fabs ({Fabs}). Name the one you mean with ?fabId=."* and
  `RuleFabCandidates.Describe` (`Handlers/RuleFabCandidates.cs:15-16`) joins
  `match.Fab.Value` **per match, without `Distinct()`** — so a single-fab
  archived collision renders the caller's one fab, repeated, inside a sentence
  that asserts there are several. The issue quotes the singular form; the code
  emits the fab once per matching row. Either way the claim in the sentence is
  false, and neither spelling is a message about a real ambiguity.
- **Publish and archive are blocked by it, exactly as filed.** Both handlers
  resolve the aggregate through `rules.GetByNameAsync(fab, name, …)` —
  `PublishRuleCommandHandler.cs:23-24`, `ArchiveRuleCommandHandler.cs:24-25` —
  so *server-side* they find the live rule correctly. But both then compare
  `rule.Version != expectedVersion` (`:33-36` and `:34-37`) against the
  `If-Match` the caller supplied, and the only place a caller can learn that
  version is the `ETag` set in `RulesEndpoints.GetOne`
  (`src/Automation/Api/RulesEndpoints.cs:168-170`) — from the very GET that now
  returns 400. The rule is unmanageable through the API, precisely as #2216
  says, and the mechanism is the ETag, not the command handlers.
- **No existing test covers it.** A case-insensitive grep for `Archiv` across
  `tests/Automation.Application.Tests/Queries/RuleQueryHandlerTests.cs` and
  `tests/Integration.Tests/Automation/RuleFabResolutionIntegrationTests.cs`
  returns **no match at all** (exit 1) in either file. Both ambiguity tests —
  `Get_refuses_a_name_that_resolves_in_two_of_the_callers_fabs` (`:410`) and
  `DryRun_refuses_a_name_that_resolves_in_two_of_the_callers_fabs` (`:446`) —
  seed **munich + dresden** and assert both fab names appear. The integration
  case `A_name_held_in_two_of_the_callers_fabs_is_refused_as_ambiguous` (`:98`)
  does the same over HTTP. Genuine cross-fab only, as filed.
- **The scenario is reachable in three calls.** FR-003 permits
  `Draft → Archived` (cancel), so a freshly created rule can be archived
  immediately: `POST /rules` → `POST /rules/{name}/archive` (If-Match 0) →
  `POST /rules` again with the same name → `GET /rules/{name}`. No publish step,
  no load, no timing.

### Found while checking, not in the issue

- **`DryRunRuleQueryHandler` carries the identical defect.**
  `src/Automation/Application/Queries/Handlers/DryRunRuleQueryHandler.cs:59-73`
  is the same two predicates, the same `matches.Count > 1`, and the same shared
  `RuleFabCandidates.Describe`. Its own comment even names `GetRuleQueryHandler`
  as the reason for its shape. So the same re-used archived name also breaks
  `POST /rules/{name}/dry-run`, with the same false message. It is the same
  defect at the same seam in the same context, one file away, sharing one
  helper — fixing one and leaving the other is not a smaller change, it is half
  a fix. In scope, as **US3 (P3)**, so it can be dropped without touching US1.
- **The database already guarantees the invariant the fix relies on.**
  `RuleConfiguration.cs:118-120` declares
  `ux_rules_fab_name_active` — `HasIndex(rule => new { rule.Fab, rule.Name })
  .IsUnique().HasFilter("state <> 'Archived'")`, present in every migration
  since `20260528192601_InitialAutomation`. **At most one non-Archived rule per
  `(fab, name)`.** So once Archived is excluded, two matches can only ever mean
  two fabs — which is exactly what the error message claims. Option 1 does not
  merely fix the symptom; it makes the message structurally true.
- **`SystemVariables` has the same disagreement.**
  `VariableRepository.GetByNameAsync:30` excludes `VariableState.Archived`;
  `GetVariableQueryHandler.cs:17-19` does not. `IRuleRepository`'s own doc says
  Automation *"mirrors spec 005's SystemVariables pattern"*, and it mirrors the
  defect too. **Out of scope here** — different bounded context, different spec,
  and it deserves its own red. Reported for a follow-up issue, not fixed
  silently and not ignored silently.

### One correction to the issue's wording

#2216 quotes the message as `(munich)` — one fab named once. The code as written
joins one entry **per matching row**, so the archived-reuse case renders
`(munich, munich)`. The issue's point stands in full and is if anything
understated. Recorded because the exact string matters to the assertions in US2.

## The decision — option 1, and what the investigation found

The issue offered two readings and warned: *"check what reads archived rules
today before assuming nothing needs them."* Every reader of rule rows was
enumerated before choosing. What was checked and what was found:

| Reader | Reads Archived? | Effect of option 1 |
|---|---|---|
| `ListRulesQueryHandler` (`GET /rules`, `?state=…`) | **Yes — this is the one.** Queries `rules.Rules` unfiltered and applies the caller's own `state` filter, and `ListRulesErrors.cs:18` names `Archived` as a legal filter value. `GET /rules?state=Archived` is the "show archived" path, and a plain `GET /rules` lists them too. | **None.** Different handler, different query, untouched by this slice. The archived-visibility path survives intact. |
| `GetRuleQueryHandler` (`GET /rules/{name}`) | Yes, by omission — the defect. | Fixed. |
| `DryRunRuleQueryHandler` (`POST /rules/{name}/dry-run`) | Yes, by omission — the same defect. Dry-running an archived rule is meaningless anyway: FR-004 says only Active rules are evaluated. | Fixed (US3). |
| `PublishRuleCommandHandler`, `ArchiveRuleCommandHandler`, `CreateRuleCommandHandler` | **No** — all three go through `GetByNameAsync`, which already excludes Archived. | None; they already hold option 1's semantics. This slice makes the read side agree with the write side, not the other way round. |
| `InMemoryRuleCache` / the evaluation path | **No.** `ArchiveRuleCommandHandler:43` evicts on archive; only Active rules are cached. | None. §IV path untouched. |
| Frontend `getRule` (`apps/shared/src/api/rules.api.ts:107-110`) | The RTK endpoint exists. **It has zero callers** — grepping `useGetRuleQuery` and `getRule` across `apps/management-web/src` and `apps/kiosk-web/src` returns nothing. `RulesPage` renders rules from `useListRulesQuery` and hides Publish/Archive for `state: 'Archived'` (`RulesPage.test.tsx:208`). | **None.** No UI reads an archived rule by name. |

**Nothing needs an archived rule from a by-name read.** The one live reader of
archived rules is the *list*, and it is a different handler that this slice does
not touch. So the issue's own condition for preferring option 2 — a live caller
that needs archived visibility through this path — is not met.

**Option 1 it is: exclude Archived from both by-name read handlers.** Four
reasons, in order of weight:

1. **It is what FR-002 already decided.** "The pair `(fabId, name)` is unique
   across non-archived rules; archived names are released for re-use." A
   released name that still resolves to its former holder has not been released.
2. **It restores the invariant the error message depends on.** With
   `ux_rules_fab_name_active` filtered on `state <> 'Archived'`, excluding
   Archived makes "more than one match" and "more than one fab" the *same
   condition*. Option 2 would leave the query returning rows the database's own
   uniqueness constraint does not govern, and the ambiguity check would be
   carrying a correctness burden the schema is already carrying.
3. **It makes three components agree instead of two.** `GetByNameAsync`, the
   partial index, and the read handlers would all state one rule. Option 2
   makes the read handler a third dialect.
4. **It is the smaller change.** One `Where` clause per handler, mirroring a
   line that already exists in `RuleRepository`.

**What option 1 costs, stated plainly:** `GET /rules/{name}` can no longer reach
an archived rule at all — including one whose name has *not* been re-used, which
today returns 200. That is a deliberate narrowing, not an oversight, and it is
pinned by its own scenario in US1 so it can never happen by accident. The
archived rule remains fully readable through `GET /rules?state=Archived`, which
is where the console already gets it from, and the DTO still carries
`archivedAt` (`RuleDto.cs:44`). No caller loses access to any data; one route
stops answering for a row another route still serves.

## User story US1 (P1) — a re-used archived name is manageable again

As an operator who archived a rule and created a new one with the same name —
which FR-002 says I may — I can read that rule by name, see its `ETag`, and
publish or archive it, because the by-name read resolves to the live rule and
ignores the archived one.

Independently shippable: one handler, one `Where` clause, one unit test and one
integration test. No migration, no contract, no frontend change.

### Acceptance scenarios

**Happy — the live rule wins.**

```gherkin
Given a rule named "r-abc" in fab "munich" that is Archived
  And a second rule named "r-abc" created afterwards in fab "munich", Draft
When an operator holding only "munich" reads GET /rules/r-abc
Then the response is 200
  And the body names state "Draft"
  And the body's ruleIdentifier is the second rule's, not the archived one's
  And the ETag matches the version in the body
```

**Happy — publish and archive work on the re-used name (the consequence).**

```gherkin
Given the 200 above and the ETag it returned
When the operator POSTs /rules/r-abc/publish with that ETag as If-Match
Then the response is 200
  And a subsequent GET /rules/r-abc reports state "Active"
```

**Conflict — genuine cross-fab ambiguity still refuses.**

```gherkin
Given a rule named "shared" in fab "munich" and another named "shared" in "dresden"
  And both are Draft
When an operator holding both fabs reads GET /rules/shared without naming a fab
Then the response is 400 RULE_FAB_AMBIGUOUS
  And the message names both "munich" and "dresden"
```

**Conflict — an archived rule does not save a cross-fab collision.**

```gherkin
Given a rule named "shared" in "munich", Archived
  And a rule named "shared" in "munich", Draft
  And a rule named "shared" in "dresden", Draft
When an operator holding both fabs reads GET /rules/shared without naming a fab
Then the response is 400 RULE_FAB_AMBIGUOUS
  And the message names "munich" and "dresden" exactly once each
```

**Bad request — an archived rule whose name was never re-used is now not-found.**

```gherkin
Given a single rule named "r-gone" in fab "munich" that is Archived
  And no other rule named "r-gone"
When an operator holding "munich" reads GET /rules/r-gone
Then the response is 404 RULE_NOT_FOUND
  And the message does not disclose that the name was ever used
```

This is the deliberate narrowing named under *What option 1 costs*. It is an
acceptance scenario rather than a footnote so that the change of status code is
recorded as intended behaviour, observed, and cannot later be mistaken for a
regression.

**Bad request — a name that is not a legal `RuleName`.**

```gherkin
Given any set of rules
When an operator reads GET /rules/NOT-A-Legal-Name
Then the response is 404 RULE_NOT_FOUND
```

Unchanged (`GetRuleQueryHandler.cs:27-36`); asserted so the parse guard is not
disturbed by the new predicate.

**Auth — a fab the caller does not hold reads as not-found.**

```gherkin
Given a rule named "secret-rule" in fab "munich", Draft
When an operator holding only "dresden" reads GET /rules/secret-rule
Then the response is 404 RULE_NOT_FOUND, byte-identical to a name never used
```

Spec 013 FR-007, already covered at `RuleQueryHandlerTests:332-335`; re-asserted
because the new predicate sits in the same `Where` chain as the fab scope
(ADR-0114) and must not reorder or weaken it. No `Idempotency-Key` applies —
these are reads (ADR-0142 covers creates and rotations).

### Independent end-to-end test procedure

**The counterfactual is the evidence.** #2216 is explicit that the current
behaviour passes every existing test, so a fix without an observed red proves
nothing.

1. **Red, before any production edit.** On `32a26c3a` with only the new tests
   added, run `RuleQueryHandlerTests` and the new integration case. The new
   archived-reuse assertions must **fail**, and the failure text must be quoted
   verbatim in the PR — the expected shape is a `GetRuleError.FabAmbiguous`
   where a `RuleDto` was expected. Every pre-existing test in both files must be
   **green** in that same run: the red must be the new behaviour, not a broken
   arrangement.
2. **Green, after.** Apply the `Where` clause. The new tests pass; all
   pre-existing tests in `RuleQueryHandlerTests`,
   `RuleFabResolutionIntegrationTests`, `RuleLifecycleIntegrationTests` and
   `RuleReadIntegrationTests` still pass **unmodified**. An existing assertion
   that has to be edited is evidence the behaviour moved further than intended —
   block and report, do not adjust. The one exception is the archived-not-reused
   case, if any test asserts 200 for it today; the premise check found none, and
   if phase 4 finds one, that is a finding to report before editing.
3. **Live stack, phase 5.** Against the running Aspire stack (**pid 3312 — do
   not stop it**), as a munich operator: create `r-<guid>`, archive it with
   `If-Match: 0`, create it again, `GET` it — expect 200 and an `ETag` — then
   publish with that ETag and expect 200. Quote the four status codes and the
   `ETag`. This is the issue's own three-call reproduction, run forwards.

## User story US2 (P2) — an ambiguity that cannot name one fab as several

As anyone reading a `RULE_FAB_AMBIGUOUS` response, the fabs it names are the
distinct fabs that actually hold the name, and there is always more than one —
so the sentence *"exists in more than one of your fabs"* is true whenever it is
printed.

Required by the issue regardless of which option lands. Separate from US1
because US1 removes today's *reachable* cause and US2 removes the *capability*:
after US1 no input can produce a single-fab ambiguity, and after US2 no future
input could either.

### Acceptance scenarios

**Happy — two fabs, named once each.**

```gherkin
Given matches for "shared" in fabs "munich" and "dresden"
When the by-name read refuses as ambiguous
Then the candidate fabs are exactly ["dresden", "munich"] — distinct, ordinal-ordered
  And the message names each exactly once
```

**Conflict — one distinct fab is not an ambiguity.**

```gherkin
Given every match for a name lies in a single fab
When the by-name read resolves
Then RULE_FAB_AMBIGUOUS is not returned
```

**Bad request / auth — N/A.** No new input and no new boundary; this is the
shape of an error already produced behind the existing `RequireScope` and the
ADR-0114 fab resolution.

### Independent end-to-end test procedure

A unit test asserting the candidate list, not the rendered sentence: the
existing cross-fab tests assert `Message.ShouldContain("munich")` and
`ShouldContain("dresden")`, and **both of those already pass against today's
broken `(munich, munich)` rendering** — a `Contains` cannot see a duplicate.
The new assertion must be on the distinct, ordered candidate collection the
error carries, so that the subject can change without the assertion text
changing.

## User story US3 (P3) — dry-run resolves the same name the same way

As an operator trying a re-used rule name against a sample event, `POST
/rules/{name}/dry-run` resolves the live rule, for the same reason and by the
same rule as `GET`.

Last because it is the same defect with a smaller blast radius: dry-run is a
trial, not a management operation, so it cannot make a rule unmanageable. Drop
it and US1 still ships.

### Acceptance scenarios

**Happy.**

```gherkin
Given a rule named "r-abc" in "munich" that is Archived
  And a live rule named "r-abc" in "munich" with predicate "$.payload.cycleTime <= 30"
When an operator holding "munich" dry-runs "r-abc" with cycleTime 20
Then the response is 200 and the predicate matched
```

**Conflict — genuine cross-fab ambiguity still refuses**, with the US2 message.
**Bad request / auth** — unchanged: `DryRunRuleFailures.RuleNotFound` for an
illegal name, 404 for a fab not held (spec 013 FR-006, `RulesEndpoints.cs:188-190`).

### Independent end-to-end test procedure

Same counterfactual as US1: `DryRun_...` archived-reuse test red first, green
after, existing dry-run tests untouched.

## Locked choices

xUnit + **Shouldly** + hand-written fakes (ADR-0052); the existing `RuleBuilder`
(ADR-0054) with the aggregate's own `Archive(clock)` for the archived fixture —
**no new builder method unless the test-writer finds one genuinely necessary**;
sentence-style test names (ADR-0053); `InMemoryRuleQuerySource` /
`InMemoryRuleRepository` as they stand; integration against the **Aspire
fixture** (ADR-0103, no Testcontainers), reusing `RuleLifecycleIntegrationTests`'
`CreateAsync` / `ReadAsync` / `UniqueName` helpers rather than adding a file.

`Result<T, Error>` with the `*Failures` factory building the base type (ADR-0047);
the comparison stays on the value object, never `.Value` — reaching into a
value-converted property fails EF translation, as both handlers' own comments
record.

**No new ADR. No migration. No contract change. No frontend change.** The
`ux_rules_fab_name_active` index already exists and is not altered.

## Explicitly not in scope

- **Spec 106 / #749.** Changes no production code and must not start.
- **`SystemVariables`' identical disagreement** between `VariableRepository`
  and `GetVariableQueryHandler`. Real, found here, different context — a
  follow-up issue, not this branch.
- **Any change to `ListRulesQueryHandler`.** It is the archived-visibility path
  and the reason option 1 is safe; touching it would remove that safety.
- **Any change to `ux_rules_fab_name_active`.** The fix relies on it.
- **Reworking `RULE_FAB_AMBIGUOUS`'s code, status or shape.** US2 changes what
  the message is built from, not the contract.
