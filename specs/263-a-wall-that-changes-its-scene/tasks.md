# Tasks 263: A wall that changes its scene

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2608 · **Phase**: 3
(Tasks), **past the gate for US1**.

**Colour**: **red** (behaviour-changing). The only exception is T020, the characterisation
for the `CellPage` → `LayoutGrid` extraction. It is **observed green before the change**
and must pass unmodified after it.

**Gate before any 4a work — cleared.** PD-1 through PD-6 were confirmed directly by the
product owner on 2026-09-26 (see spec.md's Decisions section). **US2 (T101-T130) is
parked as its own follow-up issue, filed at this Phase 3 gate, and is not built in this
delivery** — not merely gated on a PD anymore. Only the US1 tasks above are in scope for
this PR. If a PD is later overridden, re-plan the sections tagged with that PD in plan.md
before touching the tasks.

**Engineers**:

- `test-writer` for 4a.
- `backend-engineer` for Domain, Application, Infrastructure, Api and Automation.
- `infra-engineer` for the gateway route.
- `frontend-engineer` for apps/shared, management-web and kiosk-web.

**Reviewers**: backend-reviewer, frontend-reviewer, infra-reviewer, and security-reviewer
for T052 (scopes and fab lookup).

**Tracking**: feature-level issue #2608 on Project #13. No per-task issues (CLAUDE.md
Phase-3 gate). Two follow-up issues are filed at the gate:

- "Archive/retire a wall" (spec §3)
- "Scheduled trigger source for scene rotation; reopens PD-1" (spec §3, R2)

Format: `[ID] [P?] [Story] description`.

**Foundational / blocking**: T001 (contracts) and T002 (gateway route) block every
later task. After them, work fans out by context under ADR-0109, because the file sets
are disjoint: LayoutComposition ∥ AuditObservability ∥ apps/shared, and Automation too
in US2. The kiosk depends on apps/shared, and management-web does too.

## Phase 0: foundational (US1)

- [ ] **T001 [US1]** `src/Shared.Contracts/LayoutComposition/WallConfiguredV1.cs`,
  `WallSceneChangedV1.cs`. The shapes are exactly plan §1, with field order preserved.
  Add contract tests `tests/Shared.Contracts.Tests/LayoutComposition/WallConfiguredV1Tests.cs`
  and `WallSceneChangedV1Tests.cs`, which pin the shape and the "Rule/CausingEvent set iff
  Cause == Rule" rule.
- [ ] **T002 [P] [US1]** Add a gateway route `/walls/{**catch-all}` → layout-composition,
  mirroring `/layouts`, and extend the ApiGateway integration test for the route.

Depends: T001 → everything. T002 → T052, T060+.

## Phase 4a: red (test-writer), US1

- [ ] **T010 [P] [US1]** `tests/LayoutComposition.Domain.Tests/Wall/WallTests.cs` +
  `SceneTargetTests.cs` + VO tests (`WallName`, `SceneVersion`, `WallIdentifier`). These
  cover FR-001..004 and PD-5/PD-6: create showing the first scene; ValidateScenes'
  too-few, too-many and duplicate cases; Next wraps; Next skips unpublishable scenes;
  Next with nothing publishable is a no-op; Layout(x) equal to Showing is a no-op;
  Layout(x) not in the set throws; SceneVersion +1 only on change; EditScenes that
  drops Showing moves to the first scene and raises Reconfigured.
- [ ] **T011 [P] [US1]** `tests/LayoutComposition.Application.Tests/Walls/`:
  CreateWall, EditWallScenes, SwitchWallScene, GetWall and ListWalls handler tests. Every
  error code in plan §3; the fab is part of the lookup, so other fab → NOT_FOUND;
  `WALL_SCENE_OTHER_FAB` vs `NOT_FOUND`; `WALL_STALE`; domain → integration event
  mapping incl. `Metadata.Actor`; a no-op publishes nothing.
- [ ] **T012 [P] [US1]** `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs`:
  extend it so `WallConfiguredV1` and `WallSceneChangedV1` are mapped and audited.
- [ ] **T013 [P] [US1]** `tests/Integration.Tests/LayoutComposition/WallEndpointsTests.cs`
  (Aspire fixture): US1-1, 2, 3 (HTTP + event + audit row), 4, 5, 6, 7, 8, 9, 10, 11,
  12 (428), 13 (kiosk token 403 on switch, 200 on GET), 14 (404), and 15.
- [ ] **T014 [P] [US1]** Frontend unit tests:
  - `apps/shared/src/api/walls.api.test.ts`: If-Match is sent; the schema parses.
  - `apps/shared/src/realtime/layoutHub.test.ts`: extend with the `WallSceneChanged`
    subscription.
  - `apps/kiosk-web/src/features/wall/WallPage.test.tsx`: renders the showing layout;
    discards frames with sceneVersion ≤ the current one (US1-16); re-reads on reconnect
    (US1-17).
  - `apps/management-web/src/features/walls/*.test.tsx`: form validation for 2..8 scenes
    and no duplicates; the stale toast on 409 `WALL_STALE`.
- [ ] **T015 [P] [US1]** `e2e/wall-changes-its-scene.spec.ts` for US1-3 end to end:
  create 2 layouts, create the wall, open the kiosk, click Next, and see the kiosk
  render the second grid. Also US1-17: drop the hub, switch, and see the kiosk
  reconcile.
- [ ] **T016 [US1]** Run T010–T015 and capture the output **verbatim**. Every new test
  must be red **on behaviour**. A compile failure alone counts for missing types only.
  Stop and report any test that arrives green.
- [ ] **T020 [US1]** **Characterisation (green)**: run `CellPage.test.tsx` and
  `e2e/kiosk-shows-a-wall.spec.ts` as they are, before T063, and capture the output
  verbatim. If `CellPage` lacks a test for multi-tile rendering plus the highlight
  routing it performs, write it now and observe it green.

Depends: T001 → T010..T015 → T016. T020 has no dependencies within 4a and must precede
T063.

## Phase 4b: implement, US1 (engineers may not edit the T010–T020 tests)

### LayoutComposition (backend-engineer), sequential within the context

- [ ] **T030 [US1]** Domain `src/LayoutComposition/Domain/Wall/`: every file in plan §2,
  including `Events/`.
- [ ] **T031 [US1]** Application: the commands, queries, DTO, errors and the two
  domain-event handlers in plan §3, plus `[LoggerMessage]` entries in
  `Application/Log.cs`.
- [ ] **T032 [US1]** Infrastructure:
  - the `WallConfiguration` EF mapping, the `wall_scenes` owned table, the `(fab,
    lower(name))` unique index, and the migration via MigrationRunner;
  - `WallRepository` and `LayoutPublicationLookup`;
  - the broadcaster's `WallSceneChangedAsync`, `WallSceneChangedHubMessage`, and
    `ILayoutLifecycleClient.WallSceneChanged`;
  - DI in `Add…Infrastructure`.
  Confirm that the `IdempotencyKeyTable` already exists.
- [ ] **T033 [US1]** Api: the `WallEndpoints*.cs` files and `Requests/` per plan §4.4,
  with `RequireScope`, `ConcurrencyHeaders` (If-Match) and
  `IdempotencyHeaders.TryRead` + `IdempotentRequest.ExecuteCreateAsync`. Also update
  `LayoutComposition.Api.http`.

### AuditObservability [P] with T030–T033

- [ ] **T040 [P] [US1]** `IntegrationEventAuditHandler`: two `Handle` lines, and V1
  resource-map entries for `WallConfiguredV1` and `WallSceneChangedV1`.

### Frontend [P] with the backend, depends on T001's shapes only

- [ ] **T060 [P] [US1]** `apps/shared`: `walls.api.ts`, `walls.schema.ts`, an index
  export, and `layoutHub.ts` `onWallSceneChanged`.
- [ ] **T061 [US1]** management-web `features/walls/`: the list, create/edit form, detail
  page with Next/Show and the live Showing badge, the nav entry, and routes. Depends on
  T060.
- [ ] **T063 [US1]** kiosk-web **refactor**: extract `features/cell/LayoutGrid.tsx` from
  `CellPage.tsx`. T020 must pass **unmodified**. Commit this separately from T064.
- [ ] **T064 [US1]** kiosk-web: `features/wall/WallPage.tsx`, the `/walls/:wallIdentifier`
  route in `app/router.tsx`, and the picker's Walls section. Depends on T060 and T063.

### US1 wrap-up

- [ ] **T052 [US1]** Security pass on T033: scope per route; the fab is part of every
  lookup; the kiosk bundle is unchanged (PD-4); the idempotency scope includes the
  caller.
- [ ] **T053 [US1]** Run T010–T015 and T020. All green, with Release-build analyzers
  clean and coverage gates met (ADR-0065).

Depends: T030 → T031 → T032 → T033. T040 ∥ T030–T033. T060 → T061 ∥ (T063 → T064).
Everything above → T052 → T053.

## Phase 5: verify, US1

- [ ] **T070 [US1]** Run the spec §7 (FR-V1) settle check: ≥ 20 switches, a 4-tile
  worst case with disjoint cameras (and 9-tile if the ADR-0156 cap has shipped), p50,
  p95 and max for settle time and blank time, hardware noted, **run twice**. Then walk
  through US1's independent test. Write `verification.md`.

---

## US2 (separate PR, after US1 merges; **blocked on PD-1**)

### Phase 0

- [ ] **T101 [US2]** Add `src/Shared.Contracts/LayoutComposition/WallSceneSwitchRequestedV1.cs`
  and its contract test, including the "TargetLayout set iff Target == Layout" rule.

### Phase 4a: red

- [ ] **T110 [P] [US2]** `tests/Automation.Domain.Tests/Rule/RuleActionSwitchWallSceneTests.cs`
  plus tests for the Automation-local `WallIdentifier`, `LayoutIdentifier` and
  `SceneTarget` VOs.
- [ ] **T111 [P] [US2]** Automation.Application tests:
  - the evaluator emits `SwitchWallScene` carrying the `CompiledRule.Identifier`;
  - the `FabEventIngestedV1Handler` fan-out metadata mirrors the highlight branch,
    including RootIngestedAt;
  - dry-run reports that the action fires with no value;
  - `CreateRuleCommandHandler` validation (US2-7).
- [ ] **T112 [P] [US2]** Automation.Infrastructure: `RuleActionColumnConverter`
  round-trips both packed forms.
- [ ] **T113 [P] [US2]** LayoutComposition.Application:
  `WallSceneSwitchRequestedV1HandlerTests`. Cases: applies with Cause.Rule; the FR-010
  drops (other fab, unknown wall, unpublishable target) each log and publish nothing; a
  no-op publishes nothing.
- [ ] **T114 [P] [US2]** Extend the audit map test for `WallSceneSwitchRequestedV1`.
- [ ] **T115 [P] [US2]** `tests/Integration.Tests/Automation/SwitchWallSceneRuleTests.cs`:
  US2-1..11, including US2-4/5 (PD-1 last-writer-wins, both orders), US2-6 (concurrent
  switches, no lost update), and US2-10 (duplicate delivery advances once).
- [ ] **T116 [P] [US2]** Extend `e2e/rules.spec.ts` with US2-2: create the rule, ingest
  an event, and see the kiosk switch.
- [ ] **T117 [US2]** Run and capture the output verbatim. Everything new must be red.

### Phase 4b: implement

- [ ] **T120 [P] [US2]** Automation Domain + Infrastructure: `RuleAction.SwitchWallScene`,
  the local VOs, and `RuleActionColumnConverter`. Confirm that `action_packed` has no
  length limit that would reject the new forms.
- [ ] **T121 [US2]** Automation Application + Api: `RuleActionEffect`, `RuleEvaluator`,
  the `FabEventIngestedV1Handler` fan-out, `CreateRuleRequest` / `RulesEndpoints`
  parsing, `RuleDto`, `RuleMapper`, and the dry-run. Depends on T120.
- [ ] **T122 [P] [US2]** LayoutComposition: `WallSceneSwitchRequestedV1Handler` with the
  FR-010 drop logs. Confirm the Wolverine retry on `DbUpdateConcurrencyException` (plan
  §3), adding a narrowly scoped policy with a reason if needed.
- [ ] **T123 [P] [US2]** Audit: one `Handle` line and a resource-map entry.
- [ ] **T124 [P] [US2]** Frontend: the `rules.schema.ts` / `rules.api.ts` variant, and the
  management-web rule editor's "Switch wall scene" option (wall select + target select).
- [ ] **T125 [US2]** Run everything green, with analyzers and coverage clean.

Depends: T101 → T110..T116 → T117 → (T120 → T121) ∥ T122 ∥ T123 ∥ T124 → T125.

### Phase 5

- [ ] **T130 [US2]** Walk through US2's independent test, including the PD-1 observation
  (step 4). Show one Aspire trace spanning ingest → Automation → LayoutComposition →
  hub. Append to `verification.md`.
