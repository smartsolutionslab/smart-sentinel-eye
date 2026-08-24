# Tasks: Read a single camera, and correct one

**Input**: Design documents from `/specs/029-camera-read-edit/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/cameras-api.md)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to
- Exact file paths in every description

## No setup phase, and no migration

Nothing to initialise: the projects, the packages and the schema all exist.

**In particular there is no migration task.** Research §4 verified that `Version`
is already mapped by `CameraConfiguration` with `.IsConcurrencyToken()`, that
`Camera` already inherits it from `AggregateRoot<TIdentifier>`, and that
`source_url` already exists on the stream because the startup reconciler needs
it. Nothing here adds state.

If implementation finds a migration is needed after all, that contradicts
research §4 — raise it, do not absorb it.

**And the check research §4 actually asks for is the other one.** Spec 028's
research verified the schema and inferred that no production code was needed;
`ExistsByNameAsync` enforced the same rule one layer above with no status filter
and all three US2 tests failed on the first run that reached them. So for every
rule this feature relies on, check **every layer that enforces it**, not just
the innermost.

## A note on ordering

Phases follow **user-story priority** (US1 → US2 → US3), which is spec 028's
shape and the one the workflow asks for. That puts US3 after plan.md's Phase 2
slot, deliberately: US3's whole assertion is that the two endpoints refuse
identically, and the second endpoint does not exist until US2 is done. Testing
it earlier would test half of it and call it done.

---

## Phase 1: User Story 1 — Ask about one camera without asking about all of them (P1) 🎯 MVP

**Goal**: One camera can be read by its identifier, and it carries the version
that makes editing possible.

**Independent test**: Register a camera, read it back, and check the `ETag` and
the body's `version` agree. Ships alone — it is the first way to ask about one
camera, and **nothing exposes a version today**, which is why US2 cannot start
until this is done.

- [ ] T001 [P] [US1] `CameraDto` in `src/CameraCatalog/Application/DTOs/CameraDto.cs` — identifier, version, fab, name, rtspUrl, registeredAt, status per data-model.md
- [ ] T002 [P] [US1] Add `Version` to `CameraSummaryDto` in `src/CameraCatalog/Application/DTOs/CameraSummaryDto.cs` — mirroring `RuleDto`, so the listing hands every row a version without a per-row fetch
- [ ] T003 [P] [US1] `GetCameraQuery` + `GetCameraErrors` in `src/CameraCatalog/Application/Queries/` — `CameraNotFound` only; another fab's camera resolves to the *same* error, never a distinct one (FR-006)
- [ ] T004 [US1] `GetCameraQueryHandler` in `src/CameraCatalog/Application/Queries/Handlers/GetCameraQueryHandler.cs` — loads through `GetWithinFabAsync` so another fab's row is never materialised; **returns retired cameras with their status** (FR-002)
- [ ] T005 [P] [US1] Handler tests in `tests/CameraCatalog.Application.Tests/Queries/GetCameraQueryHandlerTests.cs` — found in own fab; unknown identifier → `CameraNotFound`; **another fab's camera → the same `CameraNotFound`, not a distinct error**; a retired camera is returned, not hidden
- [ ] T006 [US1] Project `Version` in `src/CameraCatalog/Application/Queries/Handlers/ListCamerasQueryHandler.cs` — the listing's rows gain it alongside the read-one
- [ ] T007 [P] [US1] Extend `tests/CameraCatalog.Application.Tests/Queries/ListCamerasQueryHandlerTests.cs` — every listed row carries a version, and it is the aggregate's own rather than a constant
- [ ] T008 [US1] `GET /cameras/{camera}` in `src/CameraCatalog/Api/CameraEndpoints.cs` — 200 + `ETag` via `ConcurrencyHeaders.ETag`, `sse.cameras.read`, spec 015 fab resolution, per contracts/cameras-api.md
- [ ] T009 [US1] Integration in `tests/Integration.Tests/CameraCatalog/GetCameraIntegrationTests.cs` — read returns the camera; **`ETag` and the body's `version` agree**; a retired camera comes back with status `Decommissioned`

**Checkpoint**: US1 is shippable here.

---

## Phase 2: User Story 2 — Correct a camera that was recorded wrongly (P2)

**Goal**: A camera's address can be corrected, safely, without losing its
identity.

**Independent test**: Read a camera, correct its address quoting the version,
read it back changed. Requires US1 for the version.

**Ships with Phase 4, not alone** — see that phase.

- [ ] T010 [P] [US2] `CameraAddressChangedDomainEvent` in `src/CameraCatalog/Domain/Camera/Events/CameraAddressChangedDomainEvent.cs` — camera, fab, **previousUrl**, url, changedBy, changedAt per data-model.md
- [ ] T011 [US2] `Camera.ChangeAddress(RtspUrl, OperatorIdentifier, IClock)` in `src/CameraCatalog/Domain/Camera/Camera.cs` — replaces `Url`, raises T010's event, **refuses a retired camera**, and **raises nothing when the address is unchanged**
- [ ] T012 [P] [US2] Domain tests in `tests/CameraCatalog.Domain.Tests/Camera/CameraAddressChangeTests.cs` — address replaced and exactly one event raised; **re-submitting the same address raises none**; **a retired camera is refused by the aggregate**; the event carries the previous address, not only the new one
- [ ] T013 [P] [US2] `ChangeCameraAddressCommand` + `ChangeCameraAddressErrors` in `src/CameraCatalog/Application/Commands/` — `CameraNotFound`, `CameraRetired`, `VersionMismatch`; another fab's camera resolves to `CameraNotFound` (FR-006)
- [ ] T014 [US2] `ChangeCameraAddressCommandHandler` in `src/CameraCatalog/Application/Commands/Handlers/ChangeCameraAddressCommandHandler.cs` — loads within the caller's fab, compares the expected version, calls `ChangeAddress`, saves. **No retry on conflict** (ADR-0113)
- [ ] T015 [P] [US2] Handler tests in `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressCommandHandlerTests.cs` — happy path; unknown → `CameraNotFound`; **another fab's → `CameraNotFound`**; stale version → `VersionMismatch`; retired → `CameraRetired`; **same address → success that publishes nothing**
- [ ] T016 [US2] `PATCH /cameras/{camera}` in `src/CameraCatalog/Api/CameraEndpoints.cs` — 204, `If-Match` via `ConcurrencyHeaders`, `sse.cameras.write`. **Fab resolved before the `If-Match` header is looked at** (FR-007), per contracts/cameras-api.md
- [ ] T017 [US2] Integration in `tests/Integration.Tests/CameraCatalog/ChangeCameraAddressIntegrationTests.cs` — corrected and readable back; stale `If-Match` → 412; absent `If-Match` → 428; retired camera → 409; a rejected change leaves the stored address untouched (FR-010)

**Checkpoint**: the catalogue can be corrected — but see Phase 4 before shipping.

---

## Phase 3: User Story 3 — Another plant's cameras stay invisible (P3)

**Goal**: The refusals are indistinguishable, on both endpoints.

**Independent test**: Ask for another fab's camera and for an identifier that
never existed, and compare the two responses **field by field**. Requires US1
and US2 — the point is that *both* endpoints behave identically, so it cannot
be finished before both exist.

- [ ] T018 [US3] Integration in `tests/Integration.Tests/CameraCatalog/CameraNonEnumerationIntegrationTests.cs` — `GET` another fab's camera vs a never-registered identifier: **same status and same body, compared field by field**, not by status code (SC-003)
- [ ] T019 [P] [US3] Integration in the same file — the identical comparison for `PATCH`. The edit has four more ways to fail than the read, and each is a chance to answer something more specific
- [ ] T020 [US3] Integration in the same file — **`PATCH` with no `If-Match` at all on another fab's camera returns 404, not 428**. This is the sharp one: a 428 confirms the camera exists, so the header must never be read before the fab is resolved (FR-007)
- [ ] T021 [P] [US3] Integration in the same file — an operator holding **both** fabs reads and edits it successfully, so the refusal is scoping and not a blanket denial

---

## Phase 4: FR-013 — the stream follows the address

**Not a user story.** The cross-context half, adopted at the Phase 2 gate from
research §2.

**Phases 2 and 4 ship together or not at all.** Phase 2 alone corrects the
catalogue and leaves the SFU pulling the old address: the API reports the new
address while the system serves the old one, which looks like success until
somebody watches the wrong feed. That is worse than not shipping the edit.

- [ ] T022 [P] `CameraAddressChangedV1` in `src/Shared.Contracts/CameraCatalog/CameraAddressChangedV1.cs` — primitives only, carrying **both** the previous and the new address, mirroring `CameraRetiredV1`
- [ ] T023 `CameraAddressChangedDomainEventHandler` in `src/CameraCatalog/Application/EventHandlers/CameraAddressChangedDomainEventHandler.cs` — publishes T022's event. Request-driven, so it inherits its cause; **no `IJourneyOrigin`** (spec 027 survey), matching `CameraRetiredDomainEventHandler`
- [ ] T024 [P] `Stream.RepointTo(StreamSourceUrl, IClock)` in `src/StreamDistribution/Domain/Stream/Stream.cs` — replaces `SourceUrl`, idempotent, and **refuses a retired stream**, mirroring the guard spec 028 put on the health reports
- [ ] T025 [P] Domain tests in `tests/StreamDistribution.Domain.Tests/Stream/StreamRepointTests.cs` — re-point from Provisioning, Healthy, Degraded and Offline; **re-pointing to the current URL raises nothing**; **a retired stream refuses**
- [ ] T026 `RepointStreamCommand` + handler in `src/StreamDistribution/Application/Commands/` — updates the aggregate and re-points the MediaMTX path. **A camera with no provisioned stream is a success carrying `None`**, not a failure, or the outbox redelivers forever (spec 028's lesson)
- [ ] T027 [P] Handler tests in `tests/StreamDistribution.Application.Tests/Commands/RepointStreamCommandHandlerTests.cs` — the path is re-pointed to the new source; **a gateway failure does not lose the change** (the aggregate must still hold the new URL); a camera with no stream succeeds with nothing done
- [ ] T028 `CameraAddressChangedIntegrationEventHandler` in `src/StreamDistribution/Application/EventHandlers/CameraAddressChangedIntegrationEventHandler.cs` — mirrors `CameraRetiredIntegrationEventHandler`
- [ ] T029 Integration in `tests/Integration.Tests/StreamDistribution/RepointStreamIntegrationTests.cs` — after correcting the address, **assert MediaMTX's own configured source for that path is the new URL**, and that the **path name is unchanged** (FR-014)
- [ ] T030 [P] Integration in the same file — the correction succeeds while the SFU is unreachable (FR-013a): the catalogue records what is true even when teardown cannot complete

---

## Phase 5: Polish & Cross-Cutting

- [ ] T031 [P] Register `CameraAddressChangedV1` in `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs` — `Architecture.Tests` fails the build without it, which is how spec 028 caught the same gap
- [ ] T032 [P] Integration — `audit_events` holds **one** row per real change, naming the operator rather than the system actor, and **a no-op change adds none** (FR-011)
- [ ] T033 Full suite, nothing excluded or weakened; Release build with analyzers clean
- [ ] T034 Verification note on the PR following [quickstart.md](./quickstart.md), including the SFU source check and the missing-`If-Match`-on-another-fab's-camera check

---

## Dependencies

```
T001, T002, T003 ─┐
                  ├─► T004 ─► T005
                  │      └──► T008 ─► T009            US1 ✅ MVP
T006 ─► T007      │                     ↓
                  │              T010 ─► T011 ─► T012
                  │                       └─► T013 ─► T014 ─► T015
                  │                                     └─► T016 ─► T017   US2
                  │                                                 ↓
                  └──────────────────────────────► T018 … T021      US3
                                                          ↓
                                                    T022 … T030      FR-013
                                                          ↓
                                                    T031 … T034
```

**T022 is the seam.** Everything in StreamDistribution needs only the contract;
the two contexts are otherwise independent, exactly as in spec 028.

## Parallel opportunities

- **T001, T002, T003** — three different files, no shared state.
- **T005 and T007** — different test files, different handlers.
- **T012 and T015** — domain and application tests, different projects.
- **T019 and T021** — independent assertions once T018's comparison helper exists.
- **T024 and T025** — the aggregate behaviour and its tests.
- **T031 and T032** — the audit registration and the assertion about it.

## Implementation strategy

**MVP is Phase 1.** Being able to ask about one camera is the feature; it is
also the only way to obtain a version, so everything else waits on it.

**Phases 2 and 4 ship together.** Correcting the catalogue while leaving the SFU
on the old address is worse than not correcting it.

**Phase 3 is last by necessity, not priority.** Its assertion is that both
endpoints refuse identically, and the second endpoint arrives in Phase 2.

---

## Three things most likely to go wrong

**The re-point test asserts the wrong thing.** Every convenient assertion passes
while the defect is live: that `CameraAddressChangedV1` was published, that
`Stream.SourceUrl` changed, that the endpoint returned 204. MediaMTX will
happily keep pulling the old address through all three. **T029 must read the
SFU's own configured source for the path** — nothing else distinguishes a
working re-point from a believed one.

**`If-Match` is validated before the fab is resolved.** It is the cheaper check
and it reads like a guard clause, so it will drift to the top of the handler.
Then a `428 IF_MATCH_REQUIRED` for a Munich camera tells a Dresden operator that
camera exists — the enumeration FR-006 exists to prevent, reintroduced by a
refactor that looks like tidying. **T020 is the test that fails**, and it is
worth its own task for exactly that reason.

**Idempotency implemented as "no error" instead of "no event".** Re-submitting
the same address must not raise, because raising would put a second row in the
audit trail for a change that did not happen and would tell StreamDistribution
to re-point a path that never moved. From the endpoint both look like 204.
**T012 and T015 assert the event count**, not the return value — the same trap
spec 028 documented and the same place it hides.
