# Tasks: Fab-scope system variables

**Input**: Design documents from `/specs/014-system-variable-fab-scoping/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/system-variables-api.md](./contracts/system-variables-api.md)

**Tests**: Included. ADR-0052 mandates TDD for the domain, and two findings in
research.md make tests part of the work rather than a follow-up: the shipped
`InMemoryReverseIndex` has none, and the latency leg this feature touches has
no baseline. Both are addressed before the code they cover changes.

**Organization**: Grouped by user story, in three delivery slices. Phases 1–5
close the data half of #1310 and are worth shipping on their own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: US1–US5 from spec.md
- Exact file paths in every task

---

## Phase 1: Setup

**Purpose**: Record the decision, and give the reverse index a test before
anything changes it. No project initialisation — this is an existing context.

- [x] T001 Amend `docs/adr/0114-fab-inferred-for-single-fab-operators.md`: fab inference now covers the SystemVariables endpoints as well as Automation's rule endpoints. Amend rather than supersede — the decision is unchanged, only its scope widens, and ADR-0114 says extending it is a new decision requiring exactly this. Note the amendment in the status line as spec 013 did.
- [x] T002 [P] Add `tests/SystemVariables.Infrastructure.Tests/SmartSentinelEye.SystemVariables.Infrastructure.Tests.csproj` referencing `src/SystemVariables/Infrastructure` and `tests/SystemVariables.Domain.Tests` (for builders). Register it in `SmartSentinelEye.slnx`. Mirrors `Automation.Infrastructure.Tests`; `coverage-check.ps1` globs `tests/` so it is picked up with no CI change, and `SystemVariables.Infrastructure` is not a gated assembly under ADR-0065.
  *Done without the `SystemVariables.Domain.Tests` reference: the reverse index
  deals only in `Guid` and `string`, so no builder is needed. Add it if T036 or
  a later task turns out to want one.*
- [x] T003 Add `tests/SystemVariables.Infrastructure.Tests/Resolution/InMemoryReverseIndexTests.cs` against the **shipped** `src/SystemVariables/Infrastructure/Resolution/InMemoryReverseIndex.cs` — add, remove, lookup, re-publishing an overlay with a different variable set, and concurrent access. Closes #461. **This must land before T033 changes the key**: today the only `InMemoryReverseIndex` under `tests/` is a hand-written double, so "the fab keying works" would otherwise be asserted against a copy that the change also has to be applied to. That is precisely how the two drift.

**Checkpoint**: The component slice 3 rewrites now has tests of its own.

---

## Phase 2: Foundational (blocking)

**Purpose**: The value object and the aggregate field every later phase needs.

- [ ] T004 [P] Add `src/SystemVariables/Domain/Variable/FabIdentifier.cs` as a `StringValueObject` with `From(...)` + `Ensure.That(...)`, mirroring `src/Automation/Domain/Rule/FabIdentifier.cs` exactly: 2–32 chars, lowercase letters/digits/`-`, starting with a letter. Per-context by ADR-0044; the grammar must match or a fab string one context accepts and another rejects strands variables that can never resolve.
- [ ] T005 [P] Add `tests/SystemVariables.Domain.Tests/Variable/FabIdentifierTests.cs` covering the grammar, rejection of null/whitespace/too-short/uppercase/leading-digit, and equality.
- [ ] T006 Add `Fab` to `src/SystemVariables/Domain/Variable/Variable.cs`: private setter, required by `Define`, never mutated afterwards. Do **not** add a `MoveToFab` — moving a variable would silently repoint every overlay resolving it, and is out of scope by decision.
- [ ] T007 Add `WithFab` to `tests/SystemVariables.Domain.Tests/Variable/VariableBuilder.cs`, defaulting to `munich` so existing call sites stay readable.
- [ ] T008 Extend `tests/SystemVariables.Domain.Tests/Variable/VariableStateMachineTests.cs` to assert `Fab` survives value changes and archiving unchanged.

**Checkpoint**: The domain carries a fab. Nothing persists it yet.

---

## Phase 3: User Story 1 — Two fabs keep their own values (P1) 🎯 MVP

**Goal**: Munich and Dresden can each hold `oeeLine1`, and neither overwrites
the other.

**Independent test**: Define `oeeLine1` in both fabs, drive an event in each,
read both back.

- [ ] T009 [US1] Map the column in `src/SystemVariables/Infrastructure/Persistence/Configurations/VariableConfiguration.cs`: `fab` NOT NULL, max length 32, value-converted. Replace `ux_system_variables_name_active` with `ux_system_variables_fab_name_active` on `(fab, name)`, **keeping** the `state <> 'Archived'` partial filter — archiving has always released a name for reuse and scoping to a fab must not quietly take that away.
- [ ] T010 [US1] Generate the EF migration under `src/SystemVariables/Infrastructure/Persistence/Migrations/`. Hand-correct the scaffold to the four-step form in data-model.md: add nullable → backfill → NOT NULL → swap indexes. `dotnet ef` will generate a single `AddColumn(nullable: false, defaultValue: "")`, which sets every existing variable's fab to the empty string — not a valid `FabIdentifier`, so those rows would fail to materialise on the next read.
- [ ] T011 [US1] Make the backfill announce itself in the migration from T010 under `src/SystemVariables/Infrastructure/Persistence/Migrations/`: wrap the `UPDATE` in a `DO $$` block that captures `ROW_COUNT` and `RAISE WARNING` naming the count. The assumption "everything that exists belongs to munich" cannot be checked from inside the database — the old rows are exactly the ones with no fab. Spec 013's `FabScopeRules` does this and it fired for real when the quickstart was walked, naming four rules.
- [ ] T012 [US1] Document in the same migration file under `src/SystemVariables/Infrastructure/Persistence/Migrations/` that `Down` discards each variable's fab and that rolling forward re-attributes everything to munich. The index conflict is the louder failure and the lesser one.
- [ ] T013 [US1] Scope the duplicate-name check in `src/SystemVariables/Application/Commands/Handlers/DefineVariableCommandHandler.cs` to the variable's fab, and reword `VARIABLE_NAME_TAKEN` so it says the name is taken *in that fab*.
- [ ] T014 [P] [US1] Add cases to `tests/SystemVariables.Application.Tests/Commands/DefineVariableCommandHandlerTests.cs` asserting the same name is accepted in a second fab and refused in the same fab.
- [ ] T015 [US1] Change `GetByNameAsync` to take a `FabIdentifier` in `src/SystemVariables/Domain/Variable/IVariableRepository.cs` and its implementation, and update `tests/SystemVariables.Application.Tests/Fakes/InMemoryVariableRepository.cs` to filter on fab and name together.
- [ ] T016 [US1] Add `tests/Integration.Tests/SystemVariables/CrossFabVariableIntegrationTests.cs`: seed a variable of the same name in two fabs, set one, assert the other is untouched, and assert the unique index is `(fab, name)` and not `(name)` by defining the same name in both fabs successfully. Covers SC-001 and SC-003.

**Checkpoint**: Two fabs can hold the same variable name. Values still arrive
through a consumer that ignores the fab — Phase 4 closes that.

---

## Phase 4: User Story 1 (cont.) + User Story 5 — The write is fab-scoped (P1/P3)

**Goal**: A value-change applies only within its own fab, and one that cannot
be applied says so.

- [ ] T017 [US1] Read `Metadata.Fab` in `src/SystemVariables/Application/EventHandlers/SystemVariableValueRequestedV1Handler.cs` and resolve `(fab, name)`. Return without effect when the message carries no fab (FR-006).
- [ ] T018 [US1] Add the fab to the dedup key: `TryReserveAsync(fab, variableName, causingEventIdentifier)` in `src/SystemVariables/Application/EventHandlers/IVariableValueRequestDedupStore.cs` and `src/SystemVariables/Infrastructure/Persistence/VariableValueRequestDedupStore.cs`, including whatever backs the reservation. Without this, two fabs' rules reacting to the same ingested event share a causing event identifier and a variable name, so the second fab's legitimate change is swallowed as a redelivery of the first — the normal case once both fabs run rules on the same trigger, not an edge one.
- [ ] T019 [US5] Add a distinct log message in `src/SystemVariables/Application/Log.cs` for a value-change naming a variable absent from its own fab, carrying **both** the fab and the variable name. It must not share a message with malformed input: #1252 hid for a release behind exactly that shared silence, and spec 013's remedy was a distinct message naming the offending value (FR-005, SC-006).
- [ ] T020 [P] [US1] Add cases to `tests/SystemVariables.Application.Tests/EventHandlers/SystemVariableValueRequestedV1HandlerTests.cs` asserting the **downstream effect**, not just that nothing threw: a munich request changes munich's variable and leaves dresden's untouched; a request with no fab changes nothing; a request naming another fab's variable changes nothing.
- [ ] T021 [P] [US5] Add a case asserting the cross-fab miss is logged with the fab and the name, using a capturing logger. The handler fails closed either way, so "published nothing" cannot tell a diagnosable failure from a silent one — mirror `tests/Automation.Application.Tests/Fakes/CapturingLogger.cs`.
- [ ] T022 [P] [US1] Add `tests/SystemVariables.Infrastructure.Tests/Persistence/VariableValueRequestDedupStoreTests.cs` asserting two fabs' identical `(name, causingEvent)` pairs both reserve successfully, and that a genuine redelivery within one fab still does not.

**Checkpoint**: #1310's data half is closed. Stored values no longer collide.
Shippable on its own.

---

## Phase 5: User Story 3 + 4 — The endpoints are guarded (P2)

**Goal**: An operator sees and changes only their own fabs' variables, and is
asked which fab only when there is a choice.

- [ ] T023 [US3] [US4] Add fab resolution to all five endpoints in `src/SystemVariables/Api/SystemVariableEndpoints.cs` using `FabResolution` and `FabClaims` from `ServiceDefaults` **unchanged** — both already exist, both are tested against all four rows of the decision table, and both are driven over real HTTP by `RuleFabResolutionIntegrationTests`. This feature adds no resolution mechanism.
- [ ] T024 [US3] Thread the fab into `src/SystemVariables/Application/Queries/ListVariablesQuery.cs`, `GetVariableQuery.cs`, `GetOverlaySnapshotQuery.cs` and their handlers in `src/SystemVariables/Application/Queries/Handlers/`. A variable in a fab the caller lacks returns the **not-found** response, byte-identical to a name that was never used (FR-009) — a 403 would confirm it exists and let an operator enumerate another fab's names one guess at a time.
- [ ] T025 [US3] Thread the fab into `src/SystemVariables/Application/Commands/ArchiveVariableCommand.cs` and `Handlers/ArchiveVariableCommandHandler.cs`, comparing the resolved fab before the `If-Match` precondition is read.
- [ ] T026 [US4] Thread the resolved fab into `src/SystemVariables/Application/Commands/DefineVariableCommand.cs` and `Handlers/DefineVariableCommandHandler.cs`; add `VariableFabRequired` and `VariableFabAmbiguous` to `src/SystemVariables/Application/Commands/DefineVariableErrors.cs` per contracts/system-variables-api.md.
- [ ] T027 [US3] Add `Fab` to `src/SystemVariables/Application/DTOs/VariableDto.cs` and its mapper, so a multi-fab operator can tell two rows apart.
- [ ] T028 [P] [US3] [US4] Add handler tests under `tests/SystemVariables.Application.Tests/` for the refusal paths, asserting a foreign variable is reported as not found and that an ambiguous name names its candidate fabs.
- [ ] T029 [US3] [US4] Declare the newly reachable statuses on every endpoint in `src/SystemVariables/Api/SystemVariableEndpoints.cs` — 400 and 403 where they became possible — so the generated OpenAPI does not claim they cannot happen. Spec 013 shipped this wrong on one endpoint and it took a code review to catch.
- [ ] T030 [US3] [US4] Add `tests/Integration.Tests/SystemVariables/VariableFabResolutionIntegrationTests.cs` driving the resolution table over real HTTP with `op-dresden@dresden.test` and `op-multi@smart-sentinel-eye.test`: refused without `fabId`, accepted when named, 403 for a fab not held, inference for a single-fab operator asserted as **dresden** (not munich, which everything else defaults to), and both sides of the ambiguity. Covers SC-002 and SC-007.

**Checkpoint**: The SystemVariables slice of #1155 is closed. Stored values and
access are both fab-correct; the screen is not yet.

---

## Phase 6: User Story 2 — The screen agrees with the store (P1)

**Goal**: A kiosk resolves only its own fab's values.

> **Gate**: T003 (shipped-class tests) and T031 (the baseline) must both be
> merged before T033 changes the key. This is the slice with the latency risk
> and the untested component; the ordering is the mitigation.

- [ ] T031 [US2] Add `tests/Integration.Tests/SystemVariables/NFR_VariableResolutionLatencyTests.cs` measuring value-change → resolved overlay text, **against the current global-keyed implementation**. Warm, then measure, and record the figure in the test as the baseline. Closes the measurement half of #749. Taking this after T033 would compare the new code against itself and pass trivially — which is why it is a task and not an afterthought. The 200 ms event-to-overlay leg (constitution §IV) is the product's load-bearing NFR and currently has nothing watching it.
- [ ] T032 [US2] Record each overlay's fab when it is indexed: `src/SystemVariables/Infrastructure/Resolution/ReverseIndexSeederHostedService.cs` and `src/SystemVariables/Application/EventHandlers/OverlayRevisionPublishedV1Handler.cs` / `OverlayRevisionArchivedV1Handler.cs`.
- [ ] T033 [US2] Key `IReverseIndex` on `(fab, variableName)` in `src/SystemVariables/Application/Resolution/IReverseIndex.cs` and `src/SystemVariables/Infrastructure/Resolution/InMemoryReverseIndex.cs`. **Widen the key — do not filter after lookup**: filtering would make cost grow with the number of overlays in other fabs, on a path inside the 200 ms leg.
- [ ] T034 [US2] Update `tests/SystemVariables.Application.Tests/Fakes/InMemoryReverseIndex.cs` to match. The fab must be part of the key here exactly as in production — a fake keyed on the name alone would return another fab's overlays and every resolution test would still pass, which is the shape of the bug being fixed reproduced in the thing meant to detect it.
- [ ] T035 [US2] Scope the fan-out in `src/SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs` to the changed variable's fab, so a live update reaches only screens in that fab (FR-015).
- [ ] T036 [P] [US2] Extend `tests/SystemVariables.Infrastructure.Tests/Resolution/InMemoryReverseIndexTests.cs` (from T003) with the fab cases: an overlay in another fab is not returned; the same variable name in two fabs occupies two buckets; removing one fab's overlay leaves the other's.
- [ ] T037 [P] [US2] Add cases to `tests/SystemVariables.Application.Tests/Queries/GetOverlaySnapshotQueryHandlerTests.cs` asserting an overlay resolves only its own fab's values, and renders the literal placeholder for a variable absent from its fab.
- [ ] T038 [US2] Add `tests/Integration.Tests/SystemVariables/CrossFabResolutionIntegrationTests.cs`: overlays in two fabs referencing one variable name, change each fab's value in turn, assert each snapshot shows its own. Covers SC-004. **Assert the dresden-change case explicitly** — steps that only change munich's value pass even if resolution is still global.
- [ ] T039 [US2] Re-run T031 against the fab-keyed implementation and record both figures on the PR. SC-005 is "no measurable regression against the baseline", and the PR must cite which leg it affects per constitution §IV.

**Checkpoint**: US2 closed. The screen agrees with the store.

---

## Phase 7: Polish

- [ ] T040 [P] Update `apps/` if the management UI lists or defines variables, so a multi-fab operator can author and read them — the same gap #1303 was for rules. If it does not, record that in the PR rather than leaving it unstated.
- [ ] T041 [P] Add an e2e case to `e2e/` only if T040 changed the UI. Do not add a skipped spec: #1292 sat skipped for two releases asserting against a UI that did not exist.
- [ ] T042 Run `scripts/coverage-check.ps1 -Configuration Release` and confirm `SystemVariables.Domain` still clears 90% and `SystemVariables.Application` 80%.
- [ ] T043 Walk `quickstart.md` end to end against a live stack and record the observations on the PR. "Done" is the observations, not the walk. Run the migration step against a database that predates this feature, or it proves nothing — a fresh database makes the backfill a no-op and the warning never fires.
- [ ] T044 Close #1310 naming the cross-fab test; comment on #1155 that SystemVariables is no longer one of the contexts missing the guard; close #461 (T035 reverse-index tests) and the measurement half of #749.

---

## Dependencies

- **Phase 1 → everything**: T003 gates T033 (do not change an untested component).
- **Phase 2 → Phases 3-6**: the value object and the aggregate field.
- **Phase 3 → Phase 4**: the consumer needs somewhere to resolve `(fab, name)`.
- **Phases 3-4 → Phase 5**: the endpoints guard a model that already has a fab.
- **T031 → T033**: the baseline must precede the change it measures.
- **T033 → T034**: production first, then the double, so the double is written against the shipped shape rather than beside it.

Phases 5 and 6 are independent of each other and could run in parallel by
separate people — different files, no shared state.

## Implementation Strategy

**MVP = Phases 1–4.** That is the value object, the aggregate field, the
migration, and the fab-scoped write. It closes the data half of #1310 — two
fabs stop overwriting each other — with no authorization change and no touch on
the latency path. Independently shippable and demonstrable.

**Phase 5** closes the access gap (#1155's SystemVariables slice), which needs a
person to act and is bounded today by there being one live fab in production.

**Phase 6** is last on purpose. It is the visible half — the kiosk — but it is
the one carrying the latency risk, and it is the only slice that changes code
inside the 200 ms budget. Doing it last means the measurement is taken against
otherwise-final code.

**Do not reorder so resolution lands first.** The screen being wrong is what
someone will notice; the store being wrong is what is corrupting data.
