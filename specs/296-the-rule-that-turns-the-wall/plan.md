# Plan 296: The rule that turns the wall

**Spec**: [spec.md](spec.md) · **Issue**: #2618 · **Parent**: spec 263 · **Phase**: 2 (Plan)

## Constitution / ADR check

| Rule | How this plan holds it |
|---|---|
| §II no primitives on domain models (ADR-0139/0140) | `RuleAction.SwitchWallScene(WallIdentifier, SceneTarget)`; three new Automation-local VOs. `Shared.Contracts` keeps primitives, as exempted. |
| §III no cross-context references | Automation declares its own `WallIdentifier`, `LayoutIdentifier`, `SceneTarget` (the `OverlayIdentifier` precedent). LayoutComposition already declares `RuleIdentifier`/`CausingEventIdentifier` (shipped in US1). The only coupling is `WallSceneSwitchRequestedV1`. |
| §IV latency budget | No leg spent; Automation's fan-out loop gains one in-memory capture (spec header). `AcceptToDecideLatencyTests` unmodified. |
| §VII observability | `[LoggerMessage]` for every drop and no-op (FR-010); the dead-letter store for FR-011; one trace ingest → Automation → LayoutComposition → hub via existing Wolverine/OTel propagation. |
| §VIII trust boundaries | The fab comes from `Metadata.Fab`, stamped by Automation from the event; it is part of the wall lookup. The rule API is unchanged in scope (`sse.rules.write`). |
| §IX no speculative generality | No retry, no hold, no TTL sweeper, no create-time wall validation. |
| ADR-0043/0113 | Manual path unchanged. Rule path: no expected version (PD-1), and **no automatic retry on conflict** (FR-011). |
| ADR-0088/0126 | Outbox for the new event; LayoutComposition keeps its durable inbox. |
| ADR-0092/0093 | Folder placement per §2–§4. |
| ADR-0099 | Predicates are AEL; nothing new. |

## Bounded contexts touched

| Context / surface | Layers | PR |
|---|---|---|
| **Shared.Contracts** | `LayoutComposition/WallSceneSwitchRequestedV1.cs` | A (foundational) |
| **AuditObservability** | `IntegrationEventAuditHandler` one `Handle`; `V1ResourceMap.Conventions` one `Add` | A (foundational, same commit as the contract) |
| **Automation** | Domain (variant + 3 VOs), Infrastructure (converter), Application (effect, evaluator, fan-out, DTO, mapper, `Log.cs`), Api (request + `BuildAction`) | A |
| **LayoutComposition** | Application (new event handler, dedup-store interface, `Log.cs`), Domain (one repository method), Infrastructure (dedup store, EF config/migration, repository method, Wolverine failure rule, DI) | A |
| **apps/shared** | `rules.schema.ts`, `rules.api.ts` | B |
| **management-web** | `features/rules/RuleDialog.tsx`, `RulesPage.tsx` | B |
| **e2e** | `rule-switches-a-wall.spec.ts`, `support/management-rules.ts` | B |
| Kiosk-web, ApiGateway, AppHost, Keycloak | **none** (spec §1.3, §1.4) | — |

**Foundational (blocks everything):** T001 — the contract and its audit wiring, one
commit. After it, Automation and LayoutComposition fan out in parallel (disjoint files,
ADR-0109). PR-B depends on PR-A being merged.

## 1. Contract (`src/Shared.Contracts/LayoutComposition/WallSceneSwitchRequestedV1.cs`)

Unchanged from spec 263 plan §1 — its field order is load-bearing for
`HandlerDeconstructionTests`:

```csharp
public sealed record WallSceneSwitchRequestedV1(
    Guid Wall,
    string Target,                   // "Next" | "Layout"
    Guid? TargetLayout,              // set iff Target == "Layout"
    Guid Rule,
    DateTimeOffset RequestedAt,
    Guid CausingEventIdentifier,
    EventMetadata Metadata) : IIntegrationEvent;   // Fab = rule's fab; Actor null; RootIngestedAt forwarded
```

Namespace `LayoutComposition` (the consumer owns the request, as with
`OverlayHighlightRequestedV1`). Audit: `Add<WallSceneSwitchRequestedV1>(map,
DomainResourceKind.Wall, requested => requested.Wall)`. **Why the audit lines ride in the
same commit:** `BoundaryTests.Every_integration_event_has_an_audit_handler` and
`V1ResourceMapTests.Every_integration_event_has_a_row_in_the_mapping_table` fail for any
contract without them, so a contract-only commit would leave `develop` red at that SHA
(ADR-0087: every commit stands alone).

## 2. Automation (PR-A)

### 2.1 Domain — `src/Automation/Domain/Rule/`

```
WallIdentifier.cs      readonly record struct, IStronglyTypedId<Guid>, From() + IsNotEmpty — shaped like OverlayIdentifier (no IComparable: nothing orders walls here)
LayoutIdentifier.cs    same shape
SceneTarget.cs         abstract record: Next | Layout(LayoutIdentifier Value); private ctor — mirrors LayoutComposition's twin, deliberately separate (§III)
RuleAction.cs          + sealed record SwitchWallScene(WallIdentifier Wall, SceneTarget Target) : RuleAction
                         static From(Guid wall, string target, Guid? layout) — throws ArgumentException for FR-003's bad shapes
```

`From`'s `ArgumentException`s are what `RulesEndpoints` already maps to
`400 RULE_INVALID_INPUT`, so US2-7 needs no new error type. The target literals `"Next"`
/ `"Layout"` are constants on `SceneTarget`, reused by the converter, the DTO and the
fan-out so there is one spelling.

### 2.2 Infrastructure — `RuleActionColumnConverter` (in `RuleConfiguration.cs`)

Add the tag `SwitchWallScene` with the two packed forms of FR-002 (`:D` GUID format,
`TryParseExact` on read, `ArgumentException` on malformed input — the existing
`ParseHighlightOverlay` style). `action_packed` is `text`: **no migration**.

### 2.3 Application

- `Evaluation/RuleActionEffect.cs`: `+ SwitchWallScene(Guid Wall, string Target, Guid? Layout, Guid Rule)`.
- `Evaluation/RuleEvaluator.cs`: `+ case RuleAction.SwitchWallScene` → effect with
  `rule.Identifier.Value`. `CompiledRule` needs no change (no value expression).
- `EventHandlers/FabEventIngestedV1Handler.cs`: `+ case RuleActionEffect.SwitchWallScene`
  → `WallSceneSwitchRequestedV1(..., requestedAt, eventIdentifier, new EventMetadata(
  Guid.CreateVersion7(), requestedAt, fab, null, ingestedAt))` — byte-for-byte the
  highlight branch's metadata. Update the class doc's list of downstream contracts.
- `DTOs/RuleDto.cs`: `RuleActionDto` gains `Guid? Wall, string? SceneTarget, Guid?
  TargetLayout` (appended), `SwitchWallSceneKind`, `ForSwitchWallScene(...)`; the two
  existing factories pass nulls for the new fields.
- `Queries/Handlers/RuleMapper.cs`: `+ case RuleAction.SwitchWallScene`.
- `Queries/Handlers/DryRunRuleQueryHandler.cs`: **no change** (spec §1.8). Only its
  comment "HighlightOverlay matches but has nothing to evaluate" widens to name both.
- `Commands/Handlers/CreateRuleCommandHandler.cs`: **no change**.

### 2.4 Api

- `Requests/CreateRuleRequest.cs`: append `Guid? WallIdentifier, string? SceneTarget, Guid?
  TargetLayoutIdentifier`; extend the doc's per-type required-field list.
- `RulesEndpoints.BuildAction`: `+ "SwitchWallScene" => RuleAction.SwitchWallScene.From(
  body.WallIdentifier ?? throw …, body.SceneTarget ?? throw …, body.TargetLayoutIdentifier)`;
  the unknown-type message lists three types.
- `Automation.Api.http`: one example request.

## 3. LayoutComposition (PR-A)

### 3.1 Application — `EventHandlers/WallSceneSwitchRequestedV1Handler.cs`

A Wolverine subscriber, structured as `SystemVariableValueRequestedV1Handler` (the one
existing handler that writes an aggregate from a rule effect) and ordered as
`SwitchWallSceneCommandHandler` (spec §1.5):

```
Ensure.That(message).IsNotNull();
var (wall, target, targetLayout, rule, _, causingEventIdentifier, metadata) = message;

1. Metadata.Fab absent/unparseable            → log (a), return                 [no key consumed]
2. walls.FindAsync(wall, [fab])               → None:
     walls.FindFabAsync(wall) → Some(other)   → log (b) naming both fabs, return  [no key consumed]
                              → None           → log (c), return                  [no key consumed]
3. parse target → SceneTarget (Next | Layout(LayoutIdentifier)); an unparseable
   Target/TargetLayout is a contract violation → log (f), return                 [no key consumed]
4. dedup.TryReserveAsync(rule, causingEvent)  → false: log dedup hit, return      (FR-012)
5. Layout(x), x ∉ wall.Scenes                  → log (d), return
6. Layout(x), x == wall.Showing                → log no-op (information), return
7. publishable = lookup.PublishedAmong(wall.Scenes, wall.Fab)
   Layout(x), x ∉ publishable                  → log (e), return
8. switched = wall.SwitchTo(target, publishable, new SceneSwitchCause.Rule(rule, causingEvent), clock)
9. await walls.SaveAsync(ct)                   ← DbUpdateConcurrencyException escapes (FR-011)
10. log switched / no-op (Next found nothing)
```

- **Why the reservation sits after the wall lookup and before the scene checks.** Steps
  1–2 depend on nothing that can change between deliveries in a way that should apply a
  switch twice, and consume no key so a mis-stamped request cannot poison a key (the
  SystemVariables "no fab → no key" reasoning). Steps 5–7 depend on wall state; once
  reserved, a request is decided exactly once whatever it decides.
- **The reservation and the commit are deliberately not required to be atomic.** If
  `ExecuteSqlRawAsync` is enlisted in Wolverine's handler transaction, a conflict (step 9)
  rolls the key back and a human replaying the dead letter re-applies it; if it is not, a
  replay is logged as a dedup hit. Both satisfy FR-012's "at most once" and FR-011's "never
  automatically re-applied". The 4b engineer records which one the stack exhibits in the PR
  (one line); neither needs code.
- **Why no `try/catch` around `SaveAsync`.** Catching the conflict and returning would let
  Wolverine flush the `WallSceneChangedV1` the domain-event handler already captured on the
  ambient context — announcing a switch that rolled back (spec §1.11). Escaping is the
  mechanism, not an omission.
- Destructure first (CLAUDE.md); no local may be named after another field of the record.
- New `[LoggerMessage]` entries in `Application/Log.cs`: `WallSwitchRequestWithoutFab`,
  `WallSwitchRequestForWallInAnotherFab`, `WallSwitchRequestForUnknownWall`,
  `WallSwitchRequestMalformedTarget`, `WallSwitchRequestSceneNotInSet`,
  `WallSwitchRequestSceneNotPublished`, `WallSwitchRequestDuplicate`,
  `RuleSwitchedWallScene`, `RuleWallSceneSwitchWasNoOp`. Each carries wall, rule and causing
  event.
- `EventHandlers/IWallSwitchRequestDedupStore.cs` — `Task<bool> TryReserveAsync(
  RuleIdentifier rule, CausingEventIdentifier causingEvent, WallIdentifier wall,
  CancellationToken)`. Placed in Application/EventHandlers exactly as
  `IVariableValueRequestDedupStore` is.

### 3.2 Domain — `IWallRepository`

`+ Task<Option<FabIdentifier>> FindFabAsync(WallIdentifier wall, CancellationToken ct)` —
the *unscoped* existence probe for FR-010 (b)/(c). Its doc must say why it is unscoped
(server-side log only; nothing crosses an HTTP boundary), so no one reuses it on a
request path. No `Wall` change: `SwitchTo`, `SceneSwitchCause.Rule` and both identifiers
shipped in US1.

### 3.3 Infrastructure

- `Persistence/WallRepository.cs`: `FindFabAsync` — a projection of `Fab` by id.
- `Persistence/WallSwitchRequestDedupStore.cs` — `INSERT INTO wall_switch_request_receipts
  (rule_id, causing_event_id, wall_id, received_at) VALUES (…, NOW()) ON CONFLICT
  (rule_id, causing_event_id) DO NOTHING`; `rowsAffected == 1`. Mirrors
  `VariableValueRequestDedupStore` line for line.
- Table `wall_switch_request_receipts`: PK `(rule_id, causing_event_id)`; `wall_id uuid not
  null` and `received_at timestamptz not null` for diagnosis. A keyless-entity or
  `modelBuilder.Entity` mapping only so the migration is generated — copy whatever
  SystemVariables did for `variable_value_request_dedup`. New migration via
  `MigrationRunner` (ADR-0067).
- **Wolverine failure rule (FR-011)** — in `LayoutCompositionInfrastructureModule`, through
  `AddWolverineForContext(..., configureMore: opts => …)`: for the handler chain of
  `WallSceneSwitchRequestedV1` only, `OnException<DbUpdateConcurrencyException>()` →
  move to error queue (dead letter), no retry. The call site carries the reason: ADR-0113
  forbids automatic retry; escaping discards the captured announcement. **The first
  Wolverine error policy in the repo** — scoped to one message type so it changes no
  existing handler. The exact Wolverine 6.40 API (a chain policy, or the handler-type
  `Configure(HandlerChain)` convention placed in Infrastructure) is the engineer's to pick;
  the tests in T113/T115 are decisive, not the API name.
- DI: `AddScoped<IWallSwitchRequestDedupStore, WallSwitchRequestDedupStore>()`.

## 4. Frontend (PR-B)

### 4.1 `apps/shared`

- `api/rules.api.ts`: `RULE_ACTION_SWITCH_WALL_SCENE`; the `RuleAction` interface's `kind`
  union gains it (the TS type is named `RuleAction`, mirroring the server's
  `RuleActionDto`); `wall`, `sceneTarget`, `targetLayout` fields, `string | null` like the
  existing variant fields.
- `api/rules.schema.ts`: `actionType` enum gains `'SwitchWallScene'`; `wallIdentifier`,
  `sceneTarget` (`'Next' | 'Layout'`), `targetLayoutIdentifier` optional fields;
  `superRefine` becomes a three-way switch (today it is `if SetVariableValue … else`, which
  would silently treat the new type as a highlight).

### 4.2 management-web

*Re-verified 2026-10-01 against `develop@a7b48a96`.* Since this plan was first written,
`RulesPage.tsx` was changed twice — #2635 (`StateBadge` now renders `<Badge>` through an
exhaustive `RULE_STATE_TONE: Record<RuleState, BadgeTone>`) and #2693 (mutation refusals on
`<FaultNotice>`, a failed list load on `<RetryBanner>`). Neither touched `describeAction`
or `RuleDialog.tsx`, so the work below is unchanged in kind. **PR-B does not touch
`StateBadge`, `RULE_STATE_TONE`, `FaultNotice` or `RetryBanner`**: they are already in
their consolidated shape. One architecture guard added since binds the new code:
`ConsoleTriadAlphaTests` (spec 298, ADR-0148) scans **all of `apps/management-web/src`**,
with no allowlist, for a triad colour carrying a call-site alpha modifier
(`bg-accent-fault/10`, `border-accent-fault/40`, …). Any status styling the new dialog or
action-column code needs goes through `Badge`/`FaultNotice`/`RetryBanner`, never a
hand-tinted class. T126 runs `Architecture.Tests` for this reason.

- `features/rules/RuleDialog.tsx`: `ACTION_OPTIONS` gains "Switch a wall's scene". Its
  fields: a wall `Select` fed by `useListWallsQuery`, and a target `Select` of "Next scene"
  + the chosen wall's `scenes` (layout identifiers) labelled by layout `name` from
  `useListLayoutsQuery`. Today one boolean, `setsVariable`, drives three binary branches:
  the `unregister` effect, the `renderedFields` list, and the keyed JSX ternary
  (`key="set-variable"` / `key="highlight-overlay"`). All three become one mapping keyed
  by `actionType` — e.g. a `Record<ActionType, readonly (keyof CreateRuleInput)[]>` of
  variant fields that both `unregister` (every *other* type's fields) and `renderedFields`
  read, so the "one source, no drift" property the existing comment defends survives the
  third case — plus a third keyed fragment (`key="switch-wall-scene"`). Switching type
  unregisters the other types' fields (US2-16's "no stale fields"). Changing the wall
  clears the chosen target.
  - **Which walls (assumption A4, flagged for the gate).** `Wall` carries `fab`. The
    select lists only walls whose `fab` equals the rule's fab — the chosen `fabId` when
    `mustChooseFab`, otherwise the operator's single fab — and is empty until a fab is
    chosen. A wall from another fab would author a rule that FR-010 (b) always drops.
    Spec FR-013 says only "the caller's walls"; this narrows it. Confirm at the gate.
  - The dialog's backend-error `<p role="alert">` stays as it is: converting it to
    `FaultNotice` is #2693's kind of consolidation, not this feature.
- `features/rules/RulesPage.tsx`: `describeAction` is today a binary ternary on
  `kind === SetVariableValue` whose else-branch renders *every other kind* as a highlight
  — the same silent fall-through as the schema's `superRefine`. It becomes an exhaustive
  `switch` on `kind` (a `never` default, in the spirit of `RULE_STATE_TONE`'s exhaustive
  record). **The wording US2 names ("Switch *Line 3 rotation* to *Line 3 fault view*")
  needs names the DTO does not carry**, so `RulesPage` also calls `useListWallsQuery` and
  `useListLayoutsQuery` and passes lookups into `describeAction`; while loading, or for a
  wall/layout the lookup lacks (a stale rule, R3), it falls
  back to the identifier rather than hiding the action. "Next" renders "… to its next
  scene". `RulesPage.test.tsx` mocks both lists accordingly.
- Radix `Select` + RHF + Zod — existing primitives only.

### 4.3 e2e

`e2e/rule-switches-a-wall.spec.ts` (**not** `wall-*.spec.ts`: that prefix is swept into the
kiosk wall-display project, the trap `wall-changes-its-scene.spec.ts` documents). Reuses
`createPublishedLayout`, the wall creation steps and kiosk session helpers; adds a
`createSwitchWallRule` helper to `support/management-rules.ts` (ADR-0162). Ingests via
`POST /event-ingestion/events/manual`.

## 5. Tests (phase 4a: all red)

| Layer | File | Covers |
|---|---|---|
| Contracts | `tests/Shared.Contracts.Tests/LayoutComposition/WallSceneSwitchRequestedV1Tests.cs` | shape, field order, "TargetLayout set iff Target == Layout" as documented contract |
| Audit | `V1ResourceMapTests` — `WallSceneSwitchRequestedCase()` row | FR-008 |
| Automation Domain | `tests/Automation.Domain.Tests/Rule/RuleActionSwitchWallSceneTests.cs`, `SceneTargetTests.cs`, `WallIdentifierTests.cs`, `LayoutIdentifierTests.cs` | FR-001, US2-7 shapes |
| Automation Infrastructure | existing `RuleActionColumnConverter` tests, extended | FR-002 both forms + malformed |
| Automation Application | `RuleEvaluatorTests`, `FabEventIngestedV1HandlerTests`, `DryRunRuleQueryHandlerTests`, `RuleMapper` tests — extended | FR-004 (rule id, metadata incl. RootIngestedAt), FR-005, DTO |
| LayoutComposition Application | `tests/LayoutComposition.Application.Tests/EventHandlers/WallSceneSwitchRequestedV1HandlerTests.cs` | FR-006, FR-010 (a)–(e) each with its own log, no-ops, FR-012 (dup, two rules both apply, drops consume no key), FR-011 (repository `SaveAsync` throws `DbUpdateConcurrencyException` → propagates, no publish observed) |
| Integration | `tests/Integration.Tests/Automation/RuleSwitchesAWallIntegrationTests.cs` | US2-1, 2, 3, 4, 5, 6 (invariant), 7, 8, 11, 12, 13, audit rows, hub frame |
| Integration (FR-011) | none by default | The dead-letter path is proven at Application level (exception propagates, nothing published) plus US2-6's integration invariant. A deterministic live race is not attempted; the PR says so. |
| Frontend unit | `rules.schema` tests, `RuleDialog.test.tsx`, `RulesPage.test.tsx` | FR-013, US2-16 |
| e2e | `rule-switches-a-wall.spec.ts` | US2-2 through the UI, kiosk follows |

**Shard filters.** Every new test class needs its `shard-N.filter` entry (repo lesson: a
missing entry fails deterministically). Part of each 4a task, not an afterthought.

## 6. Error / log catalogue

No new HTTP error codes: US2-7 is `400 RULE_INVALID_INPUT`; US2-11 is the existing 403;
US2-6's manual loser is the existing `409 WALL_STALE` or `409 AGGREGATE_VERSION_STALE`
(management-web treats every `*_STALE` alike). All rule-path refusals are log lines
(§3.1), never HTTP.

## 7. Messaging summary

`FabEventIngestedV1` → (Automation, evaluator) → **`WallSceneSwitchRequestedV1`** (new,
audited) → (LayoutComposition, `WallSceneSwitchRequestedV1Handler`) → `Wall.SwitchTo` →
`WallSceneSwitchedDomainEvent` → `WallSceneChangedV1` (shipped, audited) →
`WallSceneChangedV1Handler` (shipped) → `/hubs/layouts` `WallSceneChanged` → kiosk,
management-web. Only the bold hop and its two handlers are new.

## 8. Verification (Phase 5)

PR-A: walk spec US1's independent test live (steps 1–6), including PD-1 (step 5) and the
trace (step 6); read one audit pair; append to `verification.md`. Requires the machine's
one Aspire stack — if it is held, record the gap as spec 263's verification did, never a
fabricated pass. PR-B: the same walk through the dialog, plus the e2e.

## 9. Delivery: two PRs, sequenced

| PR | Content | Engineer | Reviewers | Observable alone? |
|---|---|---|---|---|
| **A** | T001, T110–T115, T118, T120–T123, T125, T130 | `test-writer` → `backend-engineer` | `backend-reviewer`, `security-reviewer` (fab scoping on a message path, cross-fab drop) | Yes — API + event + kiosk, no UI |
| **B** | T116–T117, T119, T124, T126, T131 | `test-writer` → `frontend-engineer` | `frontend-reviewer` | Yes — once A is on `develop` |

B is cut from `develop` **after A merges**, not stacked: it cannot run its e2e without A,
and stacking buys nothing when its unit work is small. Commit sequence within A (each
builds and passes alone, ADR-0087):

1. `docs(296)`: spec, plan, tasks (this phase).
2. contract + audit wiring + contract test + mapping row (T001).
3. Automation Domain + converter, with their tests (T110, T112 → T120).
4. Automation Application + Api, with their tests (T111 → T121).
5. LayoutComposition dedup store, migration, repository method, handler, failure rule,
   with their tests (T113, T114 → T122).
6. Integration tests (T115) — green once 3–5 are in.
7. `.http` examples, verification note.

The red evidence is the test-writer's **verbatim output**, captured before any
implementation and quoted in the PR body (ADR-0139/0144). It is not preserved as a red
commit: a commit whose tests reference types that do not exist yet does not compile, and
every SHA rebase-merged onto `develop` must build on its own. Each test file therefore
lands in the same commit as the code that turns it green.
