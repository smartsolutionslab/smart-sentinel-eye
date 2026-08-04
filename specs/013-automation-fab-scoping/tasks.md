# Tasks: Automation rules belong to a fab

**Input**: Design documents from `/specs/013-automation-fab-scoping/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/rules-api.md](./contracts/rules-api.md)

**Tests**: Included. ADR-0052 mandates TDD for the domain, and `Automation.Domain`
sits at **90.8%** against a 90% gate — the tightest margin in the solution
(plan.md Constitution Check), so a value object or aggregate field landing
without tests breaches CI.

**Organization**: Grouped by user story. Phases 1–3 alone close #1252 and are
worth shipping on their own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: US1–US4 from spec.md
- Exact file paths in every task

---

## Phase 1: Setup

**Purpose**: Record the decision and remove the duplication before either is
built on. No project initialisation — this is an existing context.

- [x] T001 Write `docs/adr/0114-fab-inferred-for-single-fab-operators.md` recording FR-013: inferring a rule's fab from a single-fab operator, why it was chosen over requiring `fabId` on every request, and that it contradicts the "no implicit current fab" position. A **new** ADR, not an amendment — research.md R5 found no ADR asserts that position; it exists only as an XML comment.
- [x] T002 Correct the contradicted comment in `src/ServiceDefaults/Authorization/IFabAuthorizationGuard.cs` (the `<para>` beginning "Multi-fab users") to point at ADR-0114 instead of asserting there is no implicit current fab.
- [x] T003 [P] Promote fab enumeration to `src/ServiceDefaults/Authorization/FabClaims.cs`, moving the body of the private `ExtractFabSet` from `src/AuditObservability/Api/AuditEndpoints.cs:148`. Keep it **off** `IFabAuthorizationGuard` — that interface answers one question and every test double would otherwise grow a method it does not use (research.md R2).
- [x] T004 [P] Cover `FabClaims` in `tests/ServiceDefaults.Tests/Authorization/FabClaimsTests.cs`: space- and tab-separated group claims, repeated single-value claims, entries without the `/fabs/` prefix ignored, and no groups at all yielding an empty set.
- [x] T005 Replace `ExtractFabSet` in `src/AuditObservability/Api/AuditEndpoints.cs` with the shared helper and delete the private copy. Run `tests/Integration.Tests/AuditObservability/` to confirm audit fab filtering is unchanged.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Put a fab on the aggregate. Nothing in any user story can proceed
without this, and the migration must be applied before later phases run
against the real shape.

**⚠️ Blocks every user story below.**

- [x] T006 [P] Add `src/Automation/Domain/Rule/FabIdentifier.cs` as a `StringValueObject` with `From(...)` + `Ensure.That(...)`, mirroring `src/Identity/Domain/RegisteredClient/FabIdentifier.cs` so the same fab string validates identically on both sides. Automation's own copy — constitution §III forbids referencing Identity's (research.md R1).
- [x] T007 [P] Add `tests/Automation.Domain.Tests/Rule/FabIdentifierTests.cs` covering the grammar, rejection of null/whitespace, and equality. Lands **with** T006, not after — see the coverage note above.
- [x] T008 Add `Fab` to `src/Automation/Domain/Rule/Rule.cs`: private setter, required by `Create`, never mutated afterwards. Do not add a `MoveToFab` — relocation is out of scope (spec Assumptions).
- [x] T009 Add `WithFab` to `tests/Automation.Domain.Tests/Rule/RuleBuilder.cs`, defaulting to `munich` so existing call sites stay readable.
- [x] T010 Extend `tests/Automation.Domain.Tests/Rule/RuleStateMachineTests.cs` to assert `Fab` survives `Publish` and `Archive` unchanged.
- [x] T011 Map the column in `src/Automation/Infrastructure/Persistence/Configurations/RuleConfiguration.cs`: `fab` NOT NULL; unique index `(fab, name)` replacing `(name)`; lookup index gains `fab` as its leading column.
- [x] T012 Generate the EF migration under `src/Automation/Infrastructure/Persistence/Migrations/`. Order per data-model.md: add nullable → backfill `'munich'` → set NOT NULL → drop and recreate both indexes **in the same migration**, so uniqueness is never absent in a released state. `'munich'` is a literal, not configuration (research.md R4).
- [x] T013 Change `GetByNameAsync` to take a `FabIdentifier` in `src/Automation/Domain/Rule/IRuleRepository.cs` and `src/Automation/Infrastructure/Persistence/RuleRepository.cs`. Without it a now-per-fab-unique name can return another fab's rule, and the `If-Match` check would then compare the wrong aggregate (research.md R6).
- [x] T014 Update `tests/Automation.Application.Tests/Fakes/InMemoryRuleRepository.cs` to match, filtering by fab and name together.

**Checkpoint**: Solution builds, migration applies to a populated database, and every pre-existing rule reports `munich`.

---

## Phase 3: User Story 1 — A rule only acts on its own plant (P1)

**Goal**: An event from one fab stops firing another fab's rules (#1252).

**Independent test**: Two active rules in two fabs on the same `(source, kind)`
with different targets. Send an event from the first fab; only its rule acts.

**This phase alone closes the unattended defect, with no authorization work
done. The branch is worth shipping at this checkpoint.**

- [x] T015 [US1] Add `Fab` to `src/Automation/Application/Evaluation/CompiledRule.cs` so the cache can key on it.
- [x] T016 [US1] Change `LookupActive` to take a fab in `src/Automation/Application/Evaluation/IRuleCache.cs`.
- [x] T017 [US1] Key `_byTrigger` on `(fab, source, kind)` in `src/Automation/Infrastructure/Cache/InMemoryRuleCache.cs`. **Widen the key — do not filter the returned bucket.** Filtering makes lookup cost grow with other fabs' rule counts, which fails SC-007 on the axis this system scales along and sits inside the 200 ms event→overlay-state budget (research.md R3).
- [x] T018 [US1] Thread the fab through `Evaluate` in `src/Automation/Application/Evaluation/RuleEvaluator.cs`.
- [x] T019 [US1] Pass `message.Fab` into evaluation in `src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs`, and return without evaluating when the event carries no fab (FR-012).
- [x] T020 [US1] Populate `Fab` when building compiled rules in `src/Automation/Infrastructure/Cache/RuleCacheSeederHostedService.cs`.
- [x] T021 [P] [US1] Add cross-fab cases to `tests/Automation.Application.Tests/Evaluation/RuleEvaluatorTests.cs`: a rule in another fab is not returned; a matching rule in the event's own fab is; an event with no fab returns nothing.
- [x] T022 [P] [US1] Add `tests/Automation.Application.Tests/EventHandlers/FabEventIngestedV1HandlerTests.cs` cases asserting the **downstream effect** — that no `SystemVariableValueRequestedV1` is published for another fab's rule. Assert the published messages, not just that evaluation returned empty; a handler that evaluated correctly but published anyway would pass the weaker check.
- [x] T023 [US1] Add `tests/Integration.Tests/Automation/CrossFabEvaluationIntegrationTests.cs`: seed one active rule per fab on the same trigger, ingest an event from one fab, and assert the other fab's target variable is untouched **and** the resulting change is attributed to the ingesting fab (FR-003).

**Checkpoint**: #1252 is closed and demonstrable via quickstart.md §1.

---

## Phase 4: User Story 2 — An operator only works with their own plant's rules (P2)

**Goal**: Rules belonging to other fabs are neither listed nor reachable.

**Independent test**: As a single-fab operator, list rules and request another
fab's rule by name; only own-fab rules appear and the direct request is refused.

- [x] T024 [US2] Apply `IFabAuthorizationGuard.EnsureAccessAsync` to every rule endpoint in `src/Automation/Api/RulesEndpoints.cs` — create, publish, archive, list, get, **and dry-run**. Call it right after model binding and **before** `ConcurrencyHeaders` is read (research.md R6).
- [x] T025 [US2] Thread the fab into `src/Automation/Application/Queries/ListRulesQuery.cs` and `Handlers/ListRulesQueryHandler.cs`, narrowing results to the caller's fabs (FR-005).
- [x] T026 [US2] Thread the fab into `src/Automation/Application/Queries/GetRuleQuery.cs` and `Handlers/GetRuleQueryHandler.cs`, returning the **not-found** shape for another fab's rule (FR-007) — not 403, which would confirm the rule exists and let an operator enumerate names one guess at a time.
- [x] T027 [US2] Thread the fab into `src/Automation/Application/Queries/DryRunRuleQuery.cs` and `Handlers/DryRunRuleQueryHandler.cs`. Leave dry-run's **absence of `If-Match` untouched** — spec 012 T048 pinned that with a test and this feature does not change it.
- [x] T028 [US2] Thread the fab into `src/Automation/Application/Commands/PublishRuleCommand.cs`, `ArchiveRuleCommand.cs` and their handlers, comparing fab before version.
- [x] T029 [US2] Add `Fab` to `src/Automation/Application/DTOs/RuleDto.cs` and `src/Automation/Application/Queries/Handlers/RuleMapper.cs`.
- [x] T030 [P] [US2] Add handler tests under `tests/Automation.Application.Tests/Queries/` and `Commands/` for the refusal paths, asserting a foreign-fab rule is indistinguishable from an absent one.
- [x] T031 [US2] Extend `tests/Integration.Tests/Automation/RuleLifecycleIntegrationTests.cs` (and `RuleReadIntegrationTests.cs`) with an operator who cannot reach another fab's rule via get, publish, archive or dry-run, asserting **byte-identical** responses for "not yours" and "does not exist".

**Checkpoint**: quickstart.md §2 passes.

---

## Phase 5: User Story 3 — Authoring picks up the operator's plant (P2)

**Goal**: Single-fab operators author without naming a fab; multi-fab operators
must name one.

**Independent test**: Author as a single-fab operator with no `fabId` and it
lands in their fab; repeat as a multi-fab operator and the attempt is refused.

- [x] T032 [US3] Add fab resolution to `src/Automation/Api/RulesEndpoints.cs` using `FabClaims` (T003): infer when the caller has exactly one fab, refuse `400 RULE_FAB_REQUIRED` when they have several and supplied none, `403` when they name one they lack, and `403` when they have none at all.
- [x] T033 [US3] Thread the resolved fab into `src/Automation/Application/Commands/CreateRuleCommand.cs` and `Handlers/CreateRuleCommandHandler.cs`.
- [x] T034 [US3] Add `RuleFabRequired` and, if reachable per the contract, `RuleFabAmbiguous` to `src/Automation/Application/Commands/CreateRuleErrors.cs` and the query error unions, following the `ApiError(Code, Message, HttpStatusCode)` shape.
- [x] T035 [P] [US3] Add endpoint-level tests covering all four rows of the resolution table in contracts/rules-api.md, including the no-fabs caller.
- [x] T036 [US3] Add `tests/Integration.Tests/Automation/RuleFabResolutionIntegrationTests.cs` with a multi-fab operator: refused without `fabId`, accepted with one of theirs, refused with one they lack.

**Checkpoint**: quickstart.md §3 passes.

---

## Phase 6: User Story 4 — The same rule name in different plants (P3)

**Goal**: `high-oee` can exist once per fab.

**Independent test**: Author the same name in two fabs (both accepted) and
twice in one fab (second refused).

- [x] T037 [US4] Scope the duplicate-name check in `src/Automation/Application/Commands/Handlers/CreateRuleCommandHandler.cs` to the rule's fab, and reword `RULE_NAME_TAKEN` so it says the name is taken *in that fab*.
- [x] T038 [P] [US4] Add cases to `tests/Automation.Application.Tests/Commands/CreateRuleCommandHandlerTests.cs` asserting the same name is accepted in a second fab and refused in the same fab.
- [x] T039 [US4] Add a case to `tests/Integration.Tests/Automation/RuleLifecycleIntegrationTests.cs` authoring the same name in two fabs, proving the unique index from T012 is `(fab, name)` and not `(name)`.

**Checkpoint**: quickstart.md §4 passes.

---

## Phase 7: Polish & Cross-Cutting

- [x] T040 [P] Add `e2e/rules.spec.ts` — Automation currently has **no e2e spec at all**. Cover authoring as a single-fab operator (fab inferred) and confirm another fab's rule is absent from the list.
- [x] T041 [P] Update `src/ScenarioSimulator/` if it authors rules, so the simulator keeps working against the new required field.
- [x] T042 [P] Declare the newly reachable statuses on the rule endpoints in `src/Automation/Api/RulesEndpoints.cs` — 400 and 403 where they became possible — so the generated OpenAPI does not claim they cannot happen. Same omission spec 012 T056 fixed for LayoutComposition.
- [x] T043 Run `scripts/coverage-check.ps1 -Configuration Release` and confirm `Automation.Domain` still clears 90%. It was at 90.8% before this feature; T007 and T010 are what keep it there.
- [x] T044 Walk quickstart.md end to end against a live stack, including the migration check, and record the observations on the PR. "Done" is those observations, not a green compile.
- [x] T045 Close #1252 with the cross-fab test named; comment on #843 that its authorization half is delivered and on #1155 that Automation is no longer one of the missing contexts.

---

## Dependencies

```text
Phase 1 (Setup)         ── T003 blocks T032
      │
Phase 2 (Foundational)  ── blocks EVERYTHING below
      │
      ├─ Phase 3 (US1, P1)  ← independent; closes #1252 on its own
      │
      ├─ Phase 4 (US2, P2)  ← needs T013 (repository takes fab)
      │        │
      │        └─ Phase 5 (US3, P2)  ← needs T024's resolution point
      │
      └─ Phase 6 (US4, P3)  ← needs T012's unique index
                │
Phase 7 (Polish) ← needs all of the above
```

US1 is genuinely independent of US2–US4: it touches the evaluation path only
and never consults a caller. US3 depends on US2 because both edit the same
resolution point in `RulesEndpoints.cs`.

## Parallel Opportunities

- **Phase 1**: T003 and T004 together; T005 after T003.
- **Phase 2**: T006+T007 together, then T008; T011 after T008.
- **Phase 3**: T021 and T022 together once T015–T020 land.
- **Phase 4**: T030 alongside T024–T029.
- **Phase 7**: T040, T041 and T042 are mutually independent.

Phases 4 and 6 could run in parallel with Phase 3 by separate people —
different files, no shared state — but Phase 3 should land first regardless,
because it is the fix that matters and holding it behind authorization work
delays the only defect currently causing harm.

## Implementation Strategy

**MVP = Phases 1–3.** That is `FabIdentifier`, `Rule.Fab`, the migration, and
the cache/evaluator scoping. It closes #1252 — an event from one fab no longer
fires another fab's rules — with no authorization change at all, and it is
independently shippable and demonstrable.

Phases 4–6 close the access gap (#843, #1155), which needs a person to act and
is bounded today by there being one live fab.

**Do not reorder** so that authorization lands first. It is the more visible
half and the one the issues describe, but it is not the one silently
corrupting data.

## Delivery status (2026-08-04)

Delivered in #1299; the remainder of the checkboxes were ticked afterwards, so
this section records what was **not** done rather than leaving three lines
unexplained.

**T036 — done, after the realm gained a second fab.** It was blocked on there
being no principal that could reach the multi-fab branch: the realm defined
`/fabs/munich` and nothing else. It now seeds `/fabs/dresden`,
`op-dresden@dresden.test` and `op-multi@smart-sentinel-eye.test`, and
`RuleFabResolutionIntegrationTests` drives the resolution table over real HTTP
— including the `RULE_FAB_AMBIGUOUS` read, which until then could only be
reached from a unit test.

**T040 — done.** `e2e/rules.spec.ts` runs. It was blocked twice: first by #1298
(`GET /rules` returned 500, fixed in #1302), then by #1303 (the UI rendered no
fab and sent none, fixed in #1308). Un-skipping also required correcting three
assertions that had never been executed — two wrong field ids, the wrong submit
button, and rows treated as listitems when `DataTable` renders a table.

**T044 — done (2026-08-04).** Walked against a live stack; observations are
recorded at the end of quickstart.md. 22 of 23 steps matched. The migration
ran against a database predating spec 013, so the backfill did real work and
its warning fired naming four rules. Two defects came out of it: #1312 (a
missing required query parameter returns 500, not 400) and two inaccuracies in
the document itself, both corrected.

**T031 and T039 landed elsewhere.** Both name
`RuleLifecycleIntegrationTests.cs`; the cases went into
`CrossFabEvaluationIntegrationTests.cs` instead, where the rest of the fab
scoping lives. The behaviour is tested; only the file differs.
