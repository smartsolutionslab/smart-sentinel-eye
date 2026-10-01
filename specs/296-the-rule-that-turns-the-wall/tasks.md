# Tasks 296: The rule that turns the wall

Spec: [`spec.md`](./spec.md) · Plan: [`plan.md`](./plan.md) · Issue #2618 · Parent: spec 263
(its US2 task IDs T101–T130 are reused where the task survives, so the lineage is readable)

## Phase-3 declarations

Supervised lane (spec header): every phase gate stops for the user; nothing here is
dispatched by `/next-issue`.

| Declaration | Value |
|---|---|
| Engineers | **Two phase-4 rounds.** **PR-A:** `test-writer` → `backend-engineer` (Automation + LayoutComposition + contract/audit). **PR-B:** `test-writer` → `frontend-engineer` (apps/shared + management-web rule editor + e2e). **No `infra-engineer`**: no gateway route, AppHost resource, Keycloak or CI change (spec §1.4). The one migration is ordinary LayoutComposition EF work. |
| Reviewers (phase 6) | PR-A: `backend-reviewer` + `security-reviewer` (a message-borne request moving state in a fab; FR-010's cross-fab drop; the unscoped `FindFabAsync` must stay off request paths). PR-B: `frontend-reviewer`. |
| New ADR? | **No** — conditional on FR-011 as written. Retrying a rule switch after a concurrency conflict (spec 263 plan §3's sketch) contradicts ADR-0113's "automatic retry is forbidden in both the backend and the SPAs" and **would need an ADR-0113 amendment; if any phase concludes the retry is required, block the issue with that reason rather than build it** (ADR-0144). AEL predicates (ADR-0099), the new `RuleAction` variant (ADR-0157 §2), the dedup store (spec 007 FR-018 precedent) and the handler (the `SystemVariableValueRequestedV1Handler` precedent) are all applications of existing decisions. |
| Phase 4a colour | **Behaviour-changing → RED for every task.** No refactor, no characterisation. Pre-existing tests that must pass **unmodified**: `EventReachesItsEffectsTests`, `AcceptToDecideLatencyTests`, `WallEndpointsTests`, `SwitchWallSceneCommandHandler` tests, `CellPage`/`WallPage` tests. Editing any of them to pass is a block, not an adjustment. |
| Board | Feature-level issue #2618 is on Project #13 (status In Progress). No per-task issues (CLAUDE.md Phase-3 row). Follow-ups to file at the gate if the human wants them tracked: R-retry (ADR-0113 amendment question), R-ttl (dedup-table sweeper, shared with SystemVariables). |

Format: `[ID] [P?] [Story] description`. `[P]` = disjoint files (ADR-0109).

**Blocking order.** T001 blocks everything. After it, PR-A's Automation tasks
(T110–T112 → T120 → T121) and LayoutComposition tasks (T113–T114 → T122) are `[P]` with
each other. PR-B (T116+) starts only after PR-A is merged to `develop`.

## Phase 3 (architect — done here)

- [x] **T100** Spec, plan, tasks at `specs/296-the-rule-that-turns-the-wall/`.

---

## PR-A — backend (US1 of this spec)

### Phase 0: foundational

- [ ] **T001 [US1]** In one commit:
  - `src/Shared.Contracts/LayoutComposition/WallSceneSwitchRequestedV1.cs` exactly per
    plan §1 (field order preserved).
  - `tests/Shared.Contracts.Tests/LayoutComposition/WallSceneSwitchRequestedV1Tests.cs`.
  - `V1ResourceMapTests.AllCases` gains `WallSceneSwitchRequestedCase()` (resource kind
    `Wall`, identifier = `Wall`).
  - **Red evidence:** with only the contract and the two test additions in place, run
    `Architecture.Tests` + `AuditObservability.Application.Tests`; capture
    `Every_integration_event_has_an_audit_handler` and the mapping test failing verbatim.
  - Then add `IntegrationEventAuditHandler.Handle(WallSceneSwitchRequestedV1)` and the
    `V1ResourceMap.Conventions` entry; the same runs go green.

  Depends: nothing. Blocks: everything.

### Phase 4a: red (test-writer)

- [ ] **T110 [P] [US1]** Automation Domain:
  `tests/Automation.Domain.Tests/Rule/RuleActionSwitchWallSceneTests.cs` (`From` for both
  targets; each US2-7 bad shape throws `ArgumentException`; variant equality),
  `SceneTargetTests.cs`, `WallIdentifierTests.cs`, `LayoutIdentifierTests.cs` (empty GUID
  refused).
- [ ] **T111 [P] [US1]** Automation Application (extend existing test classes):
  - `RuleEvaluator` emits `RuleActionEffect.SwitchWallScene` carrying
    `CompiledRule.Identifier` and the target;
  - `FabEventIngestedV1Handler` publishes `WallSceneSwitchRequestedV1` with metadata equal
    field-for-field to the highlight branch's (`Fab`, `Actor` null, `RootIngestedAt` =
    `ingestedAt`), one per matched rule, two rules → two requests with distinct `Rule`;
  - `DryRunRuleQueryHandler` → `{ Matched: true, EvaluatedValue: null }` (US2-15);
  - `RuleMapper` → `RuleActionDto` kind `SwitchWallScene`, other variants' fields null.
- [ ] **T112 [P] [US1]** Automation Infrastructure: extend the `RuleActionColumnConverter`
  tests — both packed forms round-trip; malformed wall GUID, malformed layout GUID, unknown
  target, `Layout` without a layout each throw.
- [ ] **T113 [P] [US1]** LayoutComposition Application:
  `tests/LayoutComposition.Application.Tests/EventHandlers/WallSceneSwitchRequestedV1HandlerTests.cs`
  (hand-written fakes for `IWallRepository`, `ILayoutPublicationLookup`,
  `IWallSwitchRequestDedupStore`, a capturing `IEventBus`/logger — mirror
  `OverlayHighlightRequestedV1HandlerTests` and the `SwitchWallSceneCommandHandler` tests):
  - applies `Layout` and `Next` with `SceneSwitchCause.Rule(rule, causingEvent)`, no expected
    version (FR-006);
  - FR-010 (a)–(f): each drops, publishes nothing, and emits **its own** log event naming the
    wall (and both fabs for (b), the layout for (d)/(e)); (b) and (c) are distinguishable;
  - no-ops (already showing; `Next` with nothing else Published) publish nothing, log info;
  - FR-012: second request with the same `(rule, causingEvent)` → dedup hit, no second
    switch; two different rules on one event → both reserve; drops (a)–(c) never call
    `TryReserveAsync` (nor does (f));
  - FR-011: a repository whose `SaveAsync` throws `DbUpdateConcurrencyException` → the
    exception propagates out of `Handle` (not caught, not logged-and-swallowed).
- [ ] **T114 [P] [US1]** LayoutComposition Infrastructure: a test that the Wolverine failure
  rule for `WallSceneSwitchRequestedV1` maps `DbUpdateConcurrencyException` to dead-letter
  with no retry, and that no other message type's chain gained a rule. **Prove it by
  counterfactual** (delete the rule → the test fails) and quote that in the PR. If
  Wolverine 6.40's API makes this impractical without booting a broker, write down why in
  the test-writer's report and fall back to spec A3's recorded gap — do not substitute an
  assertion that reads the source text.
- [ ] **T115 [P] [US1]** Integration (Aspire fixture, ADR-0103):
  - `tests/Integration.Tests/Automation/RuleSwitchesAWallIntegrationTests.cs` — US2-1 (both
    targets round-trip), US2-2 (wall switched, `WallSceneChangedV1` audit row with
    `Cause "Rule"`/rule/causing event/null actor, `WallSceneSwitchRequestedV1` audit row,
    hub frame on the fab group), US2-3, US2-4, US2-5, US2-6 invariant (fire a manual switch
    and a matching event concurrently; assert audit `WallSceneChangedV1` count for W ==
    `sceneVersion` delta and `Showing` == last `CurrentLayout`; tolerate either ordering),
    US2-7 (each bad shape → 400 `RULE_INVALID_INPUT`), US2-8 (fab-G rule, fab-F wall →
    unchanged; sync on a control effect as `EventReachesItsEffectsTests` does, never a bare
    sleep), US2-11 (403), US2-12, US2-13.
  - `tests/Integration.Tests/LayoutComposition/WallSwitchRequestDedupStoreIntegrationTests.cs`
    — mirror `VariableValueRequestDedupStoreIntegrationTests` against the real table.
  - Reuse `WallEndpointsTests`' layout/wall arrangement and `PlantFloor` or the manual
    events endpoint for ingestion. **Add both classes to a `shard-N.filter`.**
- [ ] **T118 [US1]** Run T110–T115 and capture the output **verbatim** for the PR body.
  Every new test must be red on behaviour; a compile failure counts only for types that do
  not exist yet. Stop and report any test that arrives green.

Depends: T001 → T110..T115 → T118.

### Phase 4b: implement (backend-engineer; may not edit T110–T115)

- [ ] **T120 [P] [US1]** Automation Domain + Infrastructure: `WallIdentifier`,
  `LayoutIdentifier`, `SceneTarget`, `RuleAction.SwitchWallScene` (plan §2.1); the converter's
  `SwitchWallScene` tag (plan §2.2). No migration (`action_packed` is `text`).
- [ ] **T121 [US1]** Automation Application + Api (plan §2.3–§2.4): `RuleActionEffect`,
  `RuleEvaluator`, `FabEventIngestedV1Handler` fan-out, `RuleActionDto`/`RuleMapper`,
  `CreateRuleRequest`, `RulesEndpoints.BuildAction`, `Automation.Api.http`. **Do not touch**
  `CreateRuleCommandHandler` or `DryRunRuleQueryHandler` logic. Depends on T120.
- [ ] **T122 [P] [US1]** LayoutComposition (plan §3): `IWallSwitchRequestDedupStore` +
  `WallSwitchRequestDedupStore`; `wall_switch_request_receipts` table + migration
  (MigrationRunner, ADR-0067); `IWallRepository.FindFabAsync` + implementation (doc says why
  it is unscoped); `WallSceneSwitchRequestedV1Handler` in plan §3.1's order with its
  `[LoggerMessage]` entries; the scoped Wolverine failure rule in
  `LayoutCompositionInfrastructureModule` with its reason at the call site; DI. No `Wall`
  change. Record in the PR whether the reservation is enlisted in the handler transaction
  (plan §3.1) — one line, observed, not assumed.
- [ ] **T123 [US1]** Security pass (security-reviewer at phase 6, engineer self-check here):
  the fab is part of the write lookup; `FindFabAsync` is used only for the log line; no
  log line leaks anything beyond identifiers and fab names already in server logs; the
  rule API's scope is unchanged.
- [ ] **T125 [US1]** Run T110–T115 green, plus unmodified: `EventReachesItsEffectsTests`,
  `AcceptToDecideLatencyTests`, `WallEndpointsTests`, `PrimitiveBoundaryTests`,
  `HandlerDeconstructionTests`, `BoundaryTests`, `V1ResourceMapTests`. Release build with
  analyzers clean; coverage gates (ADR-0065).

Depends: T118 → (T120 → T121) ∥ T122 → T123 → T125.

### Phase 5: verify (PR-A)

- [ ] **T130 [US1]** Walk spec US1's independent test live, steps 1–6: the rule round-trips;
  a matching event switches W and the kiosk follows; the audit pair reads correctly; **PD-1
  observed** (manual back to A, next event returns to B); a non-matching payload changes
  nothing; one Aspire trace spans ingest → Automation → LayoutComposition → hub. Append to
  `specs/296-the-rule-that-turns-the-wall/verification.md`. If the machine's one Aspire stack
  is unavailable, record the gap explicitly (spec 263 `verification.md` precedent).

---

## PR-B — management-web rule editor (US2 of this spec), after PR-A merges

### Phase 4a: red (test-writer)

- [ ] **T116 [P] [US2]** Frontend unit tests:
  - `apps/shared` rules schema tests: `SwitchWallScene` requires a wall; `Layout` requires a
    target layout; `Next` forbids one; the other two action types still validate exactly as
    before (existing tests unmodified).
  - `apps/management-web/src/features/rules/RuleDialog.test.tsx`: the third option renders a
    wall select and a target select populated from the chosen wall's scenes by layout name;
    submit sends the FR-003 body; switching action type away and back submits no stale
    fields (US2-16); only walls in the rule's fab are offered (plan §4.2 A4); changing the
    wall clears the target.
  - `RulesPage.test.tsx`: the action column for both targets, by wall and layout **name**,
    with walls/layouts lists mocked; the identifier fallback when the lookup lacks the
    wall; the existing `Set …` / `Highlight overlay for …` assertions unmodified.
- [ ] **T117 [P] [US2]** `e2e/rule-switches-a-wall.spec.ts` (name must not start `wall-`,
  plan §4.3): two Published layouts, a wall, a kiosk on it; author the rule **through the
  dialog**; publish; `POST /event-ingestion/events/manual` a matching event; the kiosk renders
  the target grid. Helper `createSwitchWallRule` in `e2e/support/management-rules.ts`
  (ADR-0162).
- [ ] **T119 [US2]** Run T116–T117 and capture verbatim; all new tests red on behaviour.

### Phase 4b: implement (frontend-engineer; may not edit T116–T117)

- [ ] **T124 [US2]** `apps/shared/src/api/rules.api.ts` + `rules.schema.ts` (plan §4.1);
  `RuleDialog.tsx` one `actionType`-keyed field mapping replacing the `setsVariable`
  boolean's three branches, with wall/target selects via `useListWallsQuery` +
  `useListLayoutsQuery`; `RulesPage.tsx` exhaustive `describeAction` with name lookups
  (plan §4.2). Leave `StateBadge`/`RULE_STATE_TONE`/`FaultNotice`/`RetryBanner` as they are.
  Radix + RHF + Zod only.
- [ ] **T126 [US2]** Vitest green for `apps/shared` and `apps/management-web`; lint,
  typecheck, prettier clean; `Architecture.Tests` green (`ConsoleTriadAlphaTests` scans all of
  `apps/management-web/src` for alpha-modified triad classes); e2e green in CI.

Depends: PR-A merged → T116..T117 → T119 → T124 → T126.

### Phase 5: verify (PR-B)

- [ ] **T131 [US2]** Walk spec US2's independent test through the UI; append to
  `verification.md`.
