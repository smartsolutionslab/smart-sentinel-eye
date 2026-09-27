# Spec 276 — The conflicts no caller receives

**Issue**: #2200 · **Branch**: `fix-2200-archived-resource-error` · **Phase**: 1 (Specify)
**Date**: 2026-09-27 · **Base**: `1a51bd18` (`origin/develop`, fetched 2026-09-27)
**Contexts**: SystemVariables and Automation — `Application` (deletions), `Domain` (doc comments on
the repository contracts), `Infrastructure` (inline comments only); `tests/Architecture.Tests`
(one mechanism string). No Api, `Shared.Contracts`, AppHost, migration or frontend change.
**Engineer**: `backend-engineer` · **Reviewer**: `backend-reviewer`
**Lane**: supervised — #2200 carries no `agent:ready`; the decision below was made by the product
owner in the issue thread (2026-09-27)
**Phase 4a colour**: **characterisation, observed green** — whole spec (§6). No real caller observes a
change: the 404 this spec keeps is the 404 every caller already gets.
**ADRs**: ADR-0037 (phases), ADR-0036 (smallest change; a refactor changes shape, not behaviour),
ADR-0139 (§Testing — two obligations), ADR-0047 / ADR-0089 (`Result<T, Error>`, `ApiError` with an
HTTP status — the error types being deleted), ADR-0041 (repository contracts live in Domain),
ADR-0048 / ADR-0141 (`Option<T>` from repository lookups), ADR-0113 (the `If-Match` 409 that stays),
ADR-0053 (test naming), ADR-0103 (integration via the Aspire fixture)
**Constitution**: §IV — no leg's budget changes (§7). §Testing — behaviour-preserving, so the
covering tests are captured green first and pass unmodified.
**New ADR needed**: **No.** The decision (404, not 409) applies two existing requirements — spec 005
FR-005 and spec 007 FR-002, *archived names are released for re-use* — to the mutation path. It does
not introduce a new mechanism.

---

## 1. The premise, re-checked against `1a51bd18`

| # | Issue claim | Status at `1a51bd18` |
|---|---|---|
| 1 | `SetVariableValueError.VariableArchived`, `SetVariableValueErrors.cs:15-19` | **Holds exactly.** Plus its factory `SetVariableValueFailures.VariableArchived` at `:50-51`. |
| 2 | `PublishRuleError.RuleAlreadyArchived`, `PublishRuleCommand.cs:20-24` | **Holds exactly.** Plus its factory `PublishRuleFailures.RuleAlreadyArchived` at `:49-50`. |
| 3 | `VariableRepository.cs:30` filters Archived | **Holds exactly** (`src/SystemVariables/Infrastructure/Persistence/VariableRepository.cs:30`). |
| 4 | `RuleRepository.cs:33` filters Archived | **Holds exactly** (`src/Automation/Infrastructure/Persistence/RuleRepository.cs:33`). |
| 5 | "the handler's `catch`" | **Half right.** `PublishRuleCommandHandler.cs:39-46` is a `try/catch (InvalidOperationException)` around `rule.Publish(clock)`. `SetVariableValueCommandHandler.cs:43-46` is **not a catch** — it is an explicit `if (variable.State == VariableState.Archived)` check before `SetValue`. Both are unreachable for the same reason. |
| 6 | `Rule.Publish` throw is real and unit-tested at `RuleStateMachineTests.cs:84-91` | **Holds exactly** (`tests/Automation.Domain.Tests/Rule/RuleStateMachineTests.cs:84-91`, `Publish_on_an_Archived_rule_throws`). The throw is `Rule.cs:106-109` and is the **only** `InvalidOperationException` `Publish` raises — `Active` returns early (idempotent), `Draft` proceeds. |
| 7 | `ConcurrencyConflictDeclarationTests.cs:310-313` | **Lines drifted to `:299-302`.** And it is **not a row to remove** — see §1.2. |

### 1.1 Both domain invariants stay

- `Rule.Publish` throws on Archived (`Rule.cs:106-109`), pinned by `RuleStateMachineTests.cs:84-91`.
- `Variable.SetValue` throws on any state other than Defined (`Variable.cs:118-122`).

These are the aggregates' own invariants and hold for any caller, including one that obtains an
aggregate by `GetByIdentifierAsync` (which does **not** filter Archived). Only the Application-layer
translation of them — which no lookup-by-name can reach — is deleted. Should a future caller ever hand
an Archived aggregate to either handler, the domain throws and the request fails loudly (500) rather
than being quietly translated into a 409 nobody specified. That is the correct failure for a broken
precondition, and it is the same failure every other handler in these two contexts already has.

### 1.2 The architecture-test entry is a mechanism string, not a row

`ConcurrencyConflictDeclarationTests.cs:299-302` is the `CanAnswerConflict` entry for
`Automation POST /rules/{name}/publish`, mechanism
`"refusal (PublishRuleFailures.RuleStale, RuleAlreadyArchived); lost update (rule.Publish(clock) then SaveAsync)"`.

The **route** must stay in `CanAnswerConflict`: it still answers 409 through `RuleStale`
(ADR-0113 Layer 1, `PublishRuleCommandHandler.cs:35-38`, observed by
`RuleLifecycleIntegrationTests.A_mutation_carrying_a_superseded_version_is_refused_with_409`) and
through the lost-update race. Removing the entry would fail
`Every_mutating_route_sits_in_exactly_one_of_the_two_pinned_sets` and
`Every_endpoint_that_can_answer_a_conflict_declares_it`. Only the words `, RuleAlreadyArchived` leave
the mechanism string. `Mechanism` is read only to compose a failure message (`:944`) — its content is
asserted nowhere, which is how it outlived the branch it names.

### 1.3 The contract is already written down — once, and one-sided

`IVariableRepository` (`IVariableRepository.cs:11-13`) and `IRuleRepository` (`IRuleRepository.cs:6-9`)
already say `GetByNameAsync` ignores Archived rows *so a name is free for re-use by a fresh
Define/Create*. Both implementations carry a one-line `FR-005` / `FR-002` comment. What neither says is
the consequence this issue tripped over: **every command that resolves by name — set value, publish,
archive — therefore answers not-found for an archived name.** That half is what gets written down.

### 1.4 Nothing outside the two handlers names the deleted types

`grep` over `src`, `tests`, `apps` (`.cs`, `.ts`, `.tsx`, `.json`) for `VariableArchived(`,
`RuleAlreadyArchived`, `VARIABLE_ARCHIVED`, `RULE_ALREADY_ARCHIVED`: only the two error files, the two
handlers, and the architecture-test string. No frontend, no OpenAPI snapshot, no test asserts either
code. The Automation bridge (`SystemVariableValueRequestedV1Handler.cs:111-117`) matches only
`VariableNotFound` and logs every other error by `Code`, so it needs no edit. `specs/007-automation/tasks.md:116`
(T045) names `RuleAlreadyArchived`; spec artefacts are history and are not edited.

---

## 2. Decision (product owner, 2026-09-27 — recorded, not re-made)

**404 is correct.** An archived name is released for re-use by design (spec 005 FR-005, spec 007
FR-002), so after archiving the name genuinely resolves to nothing; "does not exist" is the true answer
for a name-addressed route. Consequently:

- delete `SetVariableValueError.VariableArchived` and `PublishRuleError.RuleAlreadyArchived`, their
  factories, and the handler code that returns them;
- make the repositories' archived-row exclusion documented, intentional behaviour;
- remove the stale `RuleAlreadyArchived` mention from the architecture-test mechanism string.

The alternative (409 via an archived-inclusive lookup) is rejected and out of scope. Spec 007 US3
scenario 2 ("re-activate an archived rule → rejected") is still satisfied: the request is rejected,
with 404.

---

## 3. User story

### US1 — A caller acting on an archived name is told the truth, and the code says only what it does (P1)

An operator (or the Automation bridge) that sets a value on an archived variable, or publishes an
archived rule, receives **404** with `VARIABLE_NOT_FOUND` / `RULE_NOT_FOUND` — as today. A maintainer
reading either handler or error type sees exactly the refusals that can occur; a maintainer reading
either repository contract learns that name-addressed commands treat an archived name as gone, and why.

**Independent test**: boot the stack via the Aspire fixture; define-then-archive a variable and
create-then-archive a rule; `PUT /system-variables/{name}/value` and `POST /rules/{name}/publish` with
the post-archive `If-Match` version both answer 404 with the not-found code — **before** the change
(characterisation, green) and **after** it (same tests, unmodified, green).

**Acceptance scenarios** (all describe behaviour that holds *today* and must still hold after):

```gherkin
Scenario: setting a value on an archived variable is refused as not found
  Given variable "v1" is defined in munich and then archived
  When an operator with sse.variables.write sends PUT /system-variables/v1/value
    with If-Match set to the variable's post-archive version
  Then the response is 404
  And the error code is VARIABLE_NOT_FOUND

Scenario: publishing an archived rule is refused as not found
  Given rule "r1" is created in munich and then archived
  When an admin sends POST /rules/r1/publish with If-Match set to the rule's post-archive version
  Then the response is 404
  And the error code is RULE_NOT_FOUND

Scenario (conflict): a stale publish is still refused with 409   # unchanged, already covered
  Given rule "r1" was read at version N and has since moved to N+1
  When an admin sends POST /rules/r1/publish with If-Match N
  Then the response is 409 RULE_STALE
  # RuleLifecycleIntegrationTests.A_mutation_carrying_a_superseded_version_is_refused_with_409

Scenario (conflict): a stale value write is still refused with 409   # unchanged, already covered
  Given variable "v1" was read at version N and has since moved to N+1
  When an operator sends PUT /system-variables/v1/value with If-Match N
  Then the response is 409 VARIABLE_STALE
  # SystemVariableLifecycleIntegrationTests.A_second_writer_holding_the_old_version_is_refused_with_409

Scenario (bad request): unchanged — 400 type mismatch / 428 missing If-Match are untouched
  # A_value_that_does_not_match_the_declared_type_is_refused_with_400, A_mutation_without_If_Match_is_refused_with_428

Scenario (auth): unchanged — no scope, policy or fab-resolution path is touched
```

Why the post-archive version matters: with it, the stale check would pass if the archived row were
ever returned, so the **only** thing standing between the caller and the deleted 409 is the lookup.
That is what lets §5's counterfactual prove each test reaches the branch it claims to characterise.

---

## 4. Functional requirements

- **FR-001** `SetVariableValueError.VariableArchived` and `SetVariableValueFailures.VariableArchived`
  no longer exist.
- **FR-002** `SetVariableValueCommandHandler` no longer tests `variable.State == VariableState.Archived`.
- **FR-003** `PublishRuleError.RuleAlreadyArchived` and `PublishRuleFailures.RuleAlreadyArchived` no
  longer exist.
- **FR-004** `PublishRuleCommandHandler` calls `rule.Publish(clock)` without a surrounding
  `try/catch`.
- **FR-005** `Rule.Publish`'s Archived throw, `Variable.SetValue`'s non-Defined throw, and their unit
  tests are **unchanged**.
- **FR-006** `IVariableRepository.GetByNameAsync` and `IRuleRepository.GetByNameAsync` each carry a
  `<summary>` stating that Archived rows are excluded deliberately — the name is released for re-use —
  and that a command resolving by name therefore answers not-found for an archived name. The
  implementations' inline comments point at that contract. No issue or task number in code
  (CLAUDE.md, ADR-0036).
- **FR-007** The query in both `GetByNameAsync` implementations, both in-memory fakes, and
  `ExistsIncludingArchivedAsync` are **unchanged**.
- **FR-008** The `CanAnswerConflict` entry for `Automation POST /rules/{name}/publish` stays; its
  mechanism string no longer names `RuleAlreadyArchived`.
- **FR-009** Every HTTP response on both routes is identical before and after for every input a caller
  can construct.

## 5. Characterisation set (phase 4a)

Written first, run against **unmodified** production code, **observed green**, output quoted verbatim.
Then the production edit; then the same tests, **unmodified**, observed green again.

| ID | File | Test (sentence-style, ADR-0053) |
|---|---|---|
| C1 | `tests/Integration.Tests/SystemVariables/SystemVariableLifecycleIntegrationTests.cs` | `Setting_a_value_on_an_archived_variable_is_refused_as_not_found` |
| C2 | `tests/Integration.Tests/Automation/RuleLifecycleIntegrationTests.cs` | `Publishing_an_archived_rule_is_refused_as_not_found` |

Both classes are already in `ci-shards/shard-1.filter`; adding methods needs no filter entry.

**Why integration, not handler unit tests.** The behaviour being preserved is decided by the *real*
repository's SQL filter; a handler test would exercise the in-memory fake's copy of it. The
handler-level tests are the deliverables of **#458** and **#695**, which this spec unblocks and does
not deliver (§8).

**Counterfactual 1 — each test can fail, and fails on the branch it names.** Remove the
`State != Archived` predicate from `VariableRepository.GetByNameAsync` → C1 red with **409
`VARIABLE_ARCHIVED`**. Remove it from `RuleRepository.GetByNameAsync` → C2 red with **409
`RULE_ALREADY_ARCHIVED`**. Run against unmodified handlers. Restore with `git checkout -- <file>`,
confirm `git diff` empty, rebuild `--no-incremental` (a restored file keeps its old timestamp).

**Counterfactual 2 — the branches are dead to the unit suites.** With the deletions applied (§4
FR-001..FR-004) and nothing else, `SystemVariables.*.Tests` and `Automation.*.Tests` stay green
**unmodified**. (The brief's baseline figures, ~195 and ~573, are to be re-measured and quoted, not
assumed.)

Quote all red outputs and both green runs in the PR body.

## 6. Colour

**Characterisation, observed green** — whole spec.

- **Deletions (FR-001..FR-004)** — behaviour-preserving: they remove code no input reaches.
- **Doc comments (FR-006)** — behaviour-*documenting*: no executable change. Characterised by hashing
  the comment-stripped source of the four touched files before and after (identical hash), with a
  counterfactual that the hash does change when a code token changes. No test asserts prose.
- **Architecture-test string (FR-008)** — changes a message, not an assertion: the route set is
  unchanged and `Architecture.Tests` passes unmodified in every other respect.

An assertion that must be edited to pass after the change is evidence behaviour moved: **block, do not
adjust** (CLAUDE.md, ADR-0144).

## 7. Latency (§IV)

`SetVariableValueCommandHandler` sits on the **event → overlay state (≤ 200 ms)** leg (the Automation
bridge's `SystemVariableValueRequestedV1` → `SetVariableValueCommand`). Effect: one in-memory enum
comparison removed; no I/O added or removed. Not measurable, not claimed. `PublishRuleCommandHandler`
is off the event path (operator-initiated). No dashboard obligation arises.

## 8. Out of scope / follow-ups

- **#458** (`SetVariableValueCommandHandlerTests` — archived case) and **#695**
  (`PublishRuleCommandHandlerTests` — archived case) — **unblocked** by this spec. Their deliverable
  becomes *archived → `VariableNotFound` / `RuleNotFound`* (404), not the deleted 409 types. Comment on
  both after merge; do not deliver them here.
- `POST /system-variables/{name}/archive` and `POST /rules/{name}/archive` on an already-archived name
  also answer 404 today by the same lookup; FR-006's contract text covers them. No code change.
- Spec 007 `tasks.md` T045 still names `RuleAlreadyArchived` — historical artefact, not edited.

## 9. ADR

None needed (see header). If the product owner later wants archived-name commands to answer 409 or
410, that is a contract change requiring its own spec and — because it would introduce an
archived-inclusive lookup on the command path — an ADR.
