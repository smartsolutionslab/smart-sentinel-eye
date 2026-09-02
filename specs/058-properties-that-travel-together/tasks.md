---

description: "Task list for 058 — properties that travel together become one value object"
---

# Tasks: Properties that travel together become one value object

**Input**: Design documents from `/specs/058-properties-that-travel-together/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/README.md)

**Tests**: Test tasks are included and are **not optional here**. FR-009 requires covering tests to exist and pass before each change, and ADR-0065 gates every Domain assembly at 90% — ten new types land in gated assemblies, and spec 057 failed CI for exactly this.

**Organization**: Grouped by user story. Within US1 the work is further ordered by the plan's slices, smallest-risk first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: `[US1]`–`[US4]` from [spec.md](./spec.md)

---

## Phase 1: Setup

**Purpose**: Establish what "unchanged" means before changing anything. Both tasks produce a baseline that later verification is compared against — without them, a pre-existing problem reads as one this feature caused.

- [X] T001 Record the baseline `has-pending-model-changes` result for all nine contexts and commit it to `specs/058-properties-that-travel-together/baseline-schema.md`, noting which contexts already report the `version` nullability drift of issue #2022 so it is never mistaken for this feature's doing
- [X] T002 Record the baseline test and coverage figures for every Domain and Application assembly this feature touches, in the same file, using the manual reportgenerator route in [quickstart.md](./quickstart.md) (PowerShell 7 is unavailable on this machine, so `scripts/coverage-check.ps1` cannot run locally)
- [X] T003 [P] Audit each of the nine sites for an existing test that asserts both halves of the pair survive a round trip, and list in `baseline-schema.md` the sites that have none — those need a covering test written first, per FR-009

---

## Phase 2: Foundational

**Purpose**: None. **This phase is deliberately empty, and that is a design outcome rather than an omission.**

FR-002 forbids a shared or generic composite, so there is no common type, base class or helper for the ten composites to depend on. Each context declares its own. The absence of foundational work is what makes every user story below independently shippable — there is nothing all of them must wait for.

*(No tasks.)*

---

## Phase 3: User Story 1 — An aggregate names the concept, not the two fields (Priority: P1)

**Goal**: Nine timestamp/actor pairs across seven contexts become seven composite types.

**Independent test**: Replace StreamDistribution's pair with `Provisioning`; confirm `Stream` exposes one property, `has-pending-model-changes` reports no new change, and existing tests pass adjusted only where they name the property.

### Slice 1 — StreamDistribution (the proof)

**Do this slice alone and stop.** It is the smallest possible instance of the whole pattern; if the shape reads badly, little was spent.

- [X] T004 [P] [US1] Create `Provisioning(ProvisionedAt At, OperatorIdentifier By)` in `src/StreamDistribution/Domain/Stream/Provisioning.cs` with a guarded `From` and no member that has no caller
- [X] T005 [P] [US1] Create `tests/StreamDistribution.Domain.Tests/Stream/ProvisioningTests.cs` covering construction, both-halves-required, and value equality
- [X] T006 [US1] Replace `ProvisionedAt` + `ProvisionedBy` with one `Provisioning` on `src/StreamDistribution/Domain/Stream/Stream.cs`, including the factory that sets it
- [X] T007 [US1] Map it as an owned reference on the existing `provisioned_at` / `provisioned_by` columns in `src/StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs`, **including `builder.Navigation(...).IsRequired()`** — without that line both columns become nullable and no test fails (research R1)
- [X] T008 [US1] Follow the compiler through `src/StreamDistribution/Application/**` and the StreamDistribution test builders, renaming readers to `stream.Provisioning.At` / `.By` while leaving every DTO field name unchanged (FR-008)
- [X] T009 [US1] Verify slice 1: `has-pending-model-changes` reports no change beyond the T001 baseline, and the StreamDistribution Domain, Application and Infrastructure suites plus `Architecture.Tests` are green

### Slice 2 — CameraCatalog and Identity (`Registration`)

Two contexts, identical shape, no revisions. Confirms the pattern transfers.

- [X] T010 [P] [US1] Create `Registration(RegisteredAt At, OperatorIdentifier By)` in `src/CameraCatalog/Domain/Camera/Registration.cs` plus `tests/CameraCatalog.Domain.Tests/Camera/RegistrationTests.cs`
- [X] T011 [P] [US1] Create `Registration(RegisteredAt At, OperatorIdentifier By)` in `src/Identity/Domain/RegisteredClient/Registration.cs` plus `tests/Identity.Domain.Tests/RegisteredClient/RegistrationTests.cs` — a separate type from CameraCatalog's by FR-002, not a copy to be deduplicated later
- [X] T012 [P] [US1] Replace the pair on `src/CameraCatalog/Domain/Camera/Camera.cs` and map it in `src/CameraCatalog/Infrastructure/Persistence/Configurations/CameraConfiguration.cs` with the required navigation
- [X] T013 [P] [US1] Replace the pair on `src/Identity/Domain/RegisteredClient/RegisteredClient.cs` and map it in `src/Identity/Infrastructure/Persistence/Configurations/RegisteredClientConfiguration.cs` with the required navigation
- [X] T014 [P] [US1] Rename readers across `src/CameraCatalog/Application/**` and the CameraCatalog test builders
- [X] T015 [P] [US1] Rename readers across `src/Identity/Application/**` and the Identity test builders
- [X] T016 [US1] Verify slice 2: no schema change in either context beyond baseline; both contexts' suites and `Architecture.Tests` green

### Slice 3 — Automation and SystemVariables (`Creation`)

- [X] T017 [P] [US1] Create `Creation(CreatedAt At, OperatorIdentifier By)` in `src/Automation/Domain/Rule/Creation.cs` plus `tests/Automation.Domain.Tests/Rule/CreationTests.cs`
- [X] T018 [P] [US1] Create `Creation(CreatedAt At, OperatorIdentifier By)` in `src/SystemVariables/Domain/Variable/Creation.cs` plus `tests/SystemVariables.Domain.Tests/Variable/CreationTests.cs`
- [X] T019 [P] [US1] Replace the pair on `src/Automation/Domain/Rule/Rule.cs` and map it in `src/Automation/Infrastructure/Persistence/Configurations/RuleConfiguration.cs` with the required navigation
- [X] T020 [P] [US1] Replace the pair on `src/SystemVariables/Domain/Variable/Variable.cs` and map it in `src/SystemVariables/Infrastructure/Persistence/Configurations/VariableConfiguration.cs` with the required navigation
- [X] T021 [P] [US1] Rename readers across `src/Automation/Application/**` and the Automation test builders
- [X] T022 [P] [US1] Rename readers across `src/SystemVariables/Application/**` and the SystemVariables test builders
- [X] T023 [US1] Verify slice 3: no schema change in either context beyond baseline; both contexts' suites and `Architecture.Tests` green

### Slice 4 — LayoutComposition and OverlayDesigner (`Creation`, nested)

**The nested case**: each context uses its `Creation` twice, once on the aggregate and once inside an owned collection of revisions. Research R1 proved the columns land correctly at that depth; this is where it is proven on real code.

- [X] T024 [P] [US1] Create `Creation(CreatedAt At, OperatorIdentifier By)` in `src/LayoutComposition/Domain/Layout/Creation.cs` plus `tests/LayoutComposition.Domain.Tests/Layout/CreationTests.cs`
- [X] T025 [P] [US1] Create `Creation(CreatedAt At, OperatorIdentifier By)` in `src/OverlayDesigner/Domain/Overlay/Creation.cs` plus `tests/OverlayDesigner.Domain.Tests/Overlay/CreationTests.cs`
- [X] T026 [US1] Replace the pair on **both** `src/LayoutComposition/Domain/Layout/Layout.cs` and `src/LayoutComposition/Domain/Layout/Revision.cs`, leaving `PublishedAt` and `ArchivedAt` untouched as bare timestamps (FR-010)
- [X] T027 [US1] Map both in `src/LayoutComposition/Infrastructure/Persistence/Configurations/LayoutConfiguration.cs` — the revision one nested inside the existing `OwnsMany`, each with its required navigation
- [X] T028 [US1] Replace the pair on **both** `src/OverlayDesigner/Domain/Overlay/Overlay.cs` and `src/OverlayDesigner/Domain/Overlay/Revision.cs`, likewise leaving publish/archive timestamps alone
- [X] T029 [US1] Map both in `src/OverlayDesigner/Infrastructure/Persistence/Configurations/OverlayConfiguration.cs`, each with its required navigation
- [X] T030 [P] [US1] Rename readers across `src/LayoutComposition/Application/**` and its test builders
- [X] T031 [P] [US1] Rename readers across `src/OverlayDesigner/Application/**` and its test builders
- [X] T032 [US1] Verify slice 4: no schema change in either context beyond baseline — **check the revisions tables specifically**, since that is the depth research R1 covered but no shipped code has yet exercised — and both contexts' suites plus `Architecture.Tests` green

**Checkpoint**: US1 complete. Nine sites, seven types, no schema movement. The feature can stop here with the codebase consistent.

---

## Phase 4: User Story 2 — A payload cannot disagree with its own size (Priority: P1)

**Goal**: `AuditEvent.Payload` + `PayloadSizeBytes` become one `StoredPayload` whose size is derived, never supplied.

**Independent test**: Build the composite from content containing multi-byte characters; confirm the size is the UTF-8 byte count and that no public factory accepts a size.

- [X] T033 [P] [US2] Create `StoredPayload` in `src/AuditObservability/Domain/AuditEvent/StoredPayload.cs` with `From(string content)` deriving the size, and an internal-only reconstruction path for rows already stored (FR-005)
- [X] T034 [P] [US2] Create `tests/AuditObservability.Domain.Tests/AuditEvent/StoredPayloadTests.cs` proving the derived size counts UTF-8 bytes not characters, that content and size cannot be supplied independently, and that a stored row reconstructs to the same pair
- [X] T035 [US2] Replace `Payload` + `PayloadSizeBytes` with one `StoredPayload` on `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs`, including `AuditEvent.From`
- [X] T036 [US2] Map it onto the existing `payload` (jsonb) and `payload_size_bytes` columns in `src/AuditObservability/Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs` with the required navigation
- [X] T037 [US2] Update the two paths that read the properties directly rather than through the shared mapping: the hand-authored `INSERT` in `src/AuditObservability/Infrastructure/Persistence/AuditEventRepository.cs` and the projection in `src/AuditObservability/Infrastructure/Archive/MinioAuditChunkArchiver.cs` (FR-007)
- [X] T038 [US2] Update `src/AuditObservability/Application/Queries/Handlers/AuditRowMapper.cs` so `AuditRowDto` receives the same flat `Payload` and `PayloadSizeBytes` fields it receives today (FR-008, [contracts](./contracts/README.md))
- [X] T039 [US2] Verify US2: no schema change beyond baseline, AuditObservability Domain and Application suites green, and the `AuditRowDto` shape unchanged

---

## Phase 5: User Story 3 — An actor is one thing, with an optional name (Priority: P2)

**DECLINED — not deferred.** An index spans the composite and the row
(`ix_audit_actor_occurred`), and EF cannot express that with either an owned
reference or a complex type. Building it requires dropping the index, which
FR-004 forbids. Evidence and the rejected workarounds are in
[verification.md](./verification.md). The six tasks below are ticked because
they were **decided**, not implemented.

**Goal**: `AuditEvent.Actor` + `ActorUsername` become one `Actor`, required, with an optional username.

**Independent test**: Build an actor with and without a username; confirm the system actor is recognisable in both the composite and the audit projection.

- [X] T040 [P] [US3] Create `Actor(ActorIdentifier Identifier, ActorUsername? Username)` in `src/AuditObservability/Domain/AuditEvent/Actor.cs`, moving `IsSystem` onto it and keeping the existing `ActorIdentifier` type unchanged (FR-006)
- [X] T041 [P] [US3] Create `tests/AuditObservability.Domain.Tests/AuditEvent/ActorTests.cs` covering an actor with a username, the system actor without one, and the refusal to build one with no identifier
- [X] T042 [US3] Replace the two properties with one `Actor` on `src/AuditObservability/Domain/AuditEvent/AuditEvent.cs` — the property name does not change, only its type — and update `AuditEvent.From` and its `V1Envelope` handling
- [X] T043 [US3] Map it onto the existing `actor_identifier` and `actor_username` columns in `AuditEventConfiguration.cs`, keeping `actor_username` nullable and `actor_identifier` not, with the required navigation
- [X] T044 [US3] Update the hand-authored `INSERT`, the archiver projection and `AuditRowMapper` so `ActorIdentifier`, `ActorIsSystem` and `ActorUsername` reach `AuditRowDto` exactly as they do today
- [X] T045 [US3] Verify US3: no schema change beyond baseline; confirm the actor-filtered audit query still translates server-side against `ix_audit_actor_occurred` (research R2 predicts it does — read the generated SQL rather than assuming)

---

## Phase 6: User Story 4 — A trigger is one thing (Priority: P3)

**DECLINED — not deferred**, for the same reason as US3:
`ix_rules_fab_trigger_state` spans the row and the composite. Confirmed in an
isolated scratch model, for both an owned reference and a complex type. See
[verification.md](./verification.md). The tasks below are ticked because they
were **decided**, not implemented.

**Goal**: `Rule.TriggerSource` + `TriggerKind` become one `Trigger`.

**Independent test**: Replace the pair on `Rule`; confirm rule evaluation, persistence and projection are unchanged.

*May be folded into slice 3's Automation pass to save a second sweep of the same files; kept as its own phase so it remains separately shippable and separately revertible.*

- [X] T046 [P] [US4] Create `Trigger(TriggerSource Source, TriggerKind Kind)` in `src/Automation/Domain/Rule/Trigger.cs` plus `tests/Automation.Domain.Tests/Rule/TriggerTests.cs`
- [X] T047 [US4] Replace the pair on `src/Automation/Domain/Rule/Rule.cs` and map it onto the existing `trigger_source` / `trigger_kind` columns in `RuleConfiguration.cs` with the required navigation
- [X] T048 [US4] Rename readers across `src/Automation/Application/**`, including rule evaluation and the rule DTO mapper, leaving the DTO field names unchanged
- [X] T049 [US4] Verify US4: no schema change beyond baseline; Automation Domain, Application and Infrastructure suites green

---

## Phase 7: Polish & Cross-Cutting

- [ ] T050 Run every context's `has-pending-model-changes` and diff the results against `baseline-schema.md`, confirming this feature added no pending change anywhere (SC-002)
- [ ] T051 Confirm no file under `src/Shared.Contracts/` was modified and no DTO record definition changed — only the mapper expressions that fill them (FR-008, SC-005)
- [ ] T052 Measure the Domain coverage figure for every touched context and confirm each is at or above its ADR-0065 floor, using the manual route in [quickstart.md](./quickstart.md); **delete any composite member that has no caller rather than writing a test to cover it**
- [ ] T053 Full Release build and the complete unit suite green; record which suites could not run locally (integration and e2e need Docker) so the PR states it rather than implying a full run
- [ ] T054 Record in the PR body the two findings this feature surfaced but did not fix: publish and archive carry no actor (FR-010), and whether any stored audit row's size already disagrees with its content is untested for

---

## Dependencies

```text
Phase 1 (Setup) ─────► everything
Phase 2 (Foundational) — empty by design, blocks nothing

US1 slice 1 ──► slice 2 ──► slice 3 ──► slice 4      (ordered by risk, not by need)
US2 ──► US3                                          (same file: AuditEvent.cs)
US4 — independent; touches Rule.cs, so serialise against US1 slice 3

Phase 7 ──► after whichever stories were delivered
```

**Story independence**: US1, US2 and US4 are mutually independent and may ship in any order. US3 follows US2 only because both edit `AuditEvent.cs` and its two direct-write paths; done together they are one sweep, done apart they are two.

**Within US1** the slice order is a risk ordering, not a dependency: any slice can be done alone. Slice 1 is first because it is the cheapest way to discover the shape reads badly.

---

## Parallel execution examples

**Setup**: T003 runs alongside T001–T002.

**US1 slice 2**: T010–T015 are six tasks across two contexts that share no file — the two contexts can be worked simultaneously, and within each, the composite, the aggregate and the readers are separate files.

**US1 slice 4**: T024, T025, T030 and T031 are parallel; T026–T029 serialise per context because the aggregate and its configuration are edited together.

**US2/US3**: T033–T034 and T040–T041 are parallel with each other only if US2 and US3 are being done as one sweep; otherwise each story's type and tests are parallel within the story.

---

## Implementation strategy

**MVP**: US1 slice 1 alone — StreamDistribution's `Provisioning`. One aggregate, one configuration, six tasks. It delivers the pattern and proves the schema does not move; everything after it is the same move repeated.

**Incremental delivery**: each slice and each story leaves the codebase consistent and shippable (SC-006). The feature may stop after any of them.

**Stop conditions**: if a slice reports a pending schema change that is not in the T001 baseline, stop and read the generated migration before continuing — a nullable column means a missing required-navigation line, and shipping it would create another instance of issue #2022.

---

## Phase 3 gate (CLAUDE.md)

Per-task issues are **not** created — that convention ended after spec 028, and `tasks.md` is the artifact this work is tracked against. The gate is a **feature-level issue on Project #13**, added by hand:

```sh
gh project item-add 13 --owner smartsolutionslab --url <issue-url>
```

`item-add` prints nothing on success, and `item-list` defaults to 30 items — verify with `--limit 2000`, and match on the issue URL rather than the number.
