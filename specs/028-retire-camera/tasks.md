# Tasks: Retire a camera

**Input**: Design documents from `/specs/028-retire-camera/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/cameras-api.md)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to
- Exact file paths in every description

## No setup phase, and no migration

Nothing to initialise: the projects, the packages and the schema all exist.

**In particular there is no migration task.** Research §1 verified that
`ux_cameras_fab_name_normalized_active … WHERE status <> 'Decommissioned'`
already excludes retired cameras and that `CameraStatus.Decommissioned`
persists as that literal string. FR-006 is therefore *assertion* work.

If implementation finds a migration is needed after all, that contradicts
research §1 — raise it, do not absorb it.

---

## Phase 1: User Story 1 — Retire a camera that no longer exists (P1) 🎯 MVP

**Goal**: A camera can reach the terminal state, and the retirement is announced.

**Independent test**: Retire a registered camera; status changes, `CameraRetiredV1`
is announced, and a second retire changes nothing. Ships alone — the catalogue
starts telling the truth about which hardware exists even if nothing consumes
the announcement.

- [ ] T001 [P] [US1] `CameraRetiredDomainEvent` in `src/CameraCatalog/Domain/Camera/Events/CameraRetiredDomainEvent.cs` — camera, fab, name, retiredBy, retiredAt per data-model.md
- [ ] T002 [P] [US1] `CameraRetiredV1` in `src/Shared.Contracts/CameraCatalog/CameraRetiredV1.cs` — primitives only, mirroring `CameraRegisteredV1`
- [ ] T003 [US1] `Camera.Retire(OperatorIdentifier, IClock)` in `src/CameraCatalog/Domain/Camera/Camera.cs` — sets `CameraStatus.Decommissioned`, raises T001's event, returns without raising when already retired
- [ ] T004 [P] [US1] Domain tests in `tests/CameraCatalog.Domain.Tests/Camera/CameraRetirementTests.cs` — transitions to Decommissioned; raises exactly one event; **a second Retire raises none**; no behaviour leaves the terminal state
- [ ] T005 [P] [US1] `RetireCameraCommand` + `RetireCameraErrors` in `src/CameraCatalog/Application/Commands/` — `CameraNotFound` only; another fab's camera resolves to the same error (FR-004)
- [ ] T006 [US1] `RetireCameraCommandHandler` in `src/CameraCatalog/Application/Commands/Handlers/RetireCameraCommandHandler.cs` — loads within the caller's fab, calls `Retire`, saves
- [ ] T007 [P] [US1] Handler tests in `tests/CameraCatalog.Application.Tests/Commands/RetireCameraCommandHandlerTests.cs` — happy path; unknown camera → `CameraNotFound`; **another fab's camera → `CameraNotFound`, not a distinct error**; retiring twice publishes one event
- [ ] T008 [US1] Repository read-within-fab in `src/CameraCatalog/Infrastructure/Persistence/CameraRepository.cs` — find by identifier scoped to fab, so cross-fab is indistinguishable from missing
- [ ] T009 [US1] `CameraRetiredDomainEventHandler` in `src/CameraCatalog/Application/EventHandlers/CameraRetiredDomainEventHandler.cs` — publishes `CameraRetiredV1`. Message-driven, so it inherits its cause; **no `IJourneyOrigin` here** (spec 027 survey)
- [ ] T010 [US1] `POST /cameras/{camera}/retire` in `src/CameraCatalog/Api/CameraEndpoints.cs` — 204, `sse.cameras.write`, spec 015 fab resolution, per contracts/cameras-api.md
- [ ] T011 [US1] Integration in `tests/Integration.Tests/CameraCatalog/RetireCameraIntegrationTests.cs` — retire via API returns 204; retiring again returns 204; **`audit_events` holds exactly one retirement**
- [ ] T012 [US1] Integration in the same file — another fab's camera returns **404**, byte-identical to an unregistered identifier

**Checkpoint**: US1 is shippable here.

---

## Phase 2: User Story 2 — Reuse a retired camera's name (P2)

**Goal**: The name comes back, in its own fab and nowhere else.

**Independent test**: Retire, then register the same name in the same fab.
Requires US1. **No production code** — research §1 says the index already does
this, and these tests are what prove it.

- [ ] T013 [US2] Integration in `tests/Integration.Tests/CameraCatalog/RetireCameraIntegrationTests.cs` — retire `line-3-inlet` in `munich`, register `line-3-inlet` in `munich`, accepted
- [ ] T014 [P] [US2] Integration — while the camera is **active**, the same registration is still refused 409. Without this, T013 passes against a catalogue with no uniqueness at all
- [ ] T015 [P] [US2] Integration — the retirement in `munich` does not change what `dresden` may register, in either direction
- [ ] T016 [US2] Integration — case-insensitivity survives reuse: retire `Line-3-Inlet`, then `line-3-inlet` is accepted in that fab (guards the #1434 index against a regression from this feature)

---

## Phase 3: User Story 3 — Retired cameras stay out of the way (P3)

**Goal**: The default listing shows cameras that exist.

**Independent test**: Retire a camera, list the fab; absent by default, present
when asked for.

- [ ] T017 [US3] Exclude retired from the default listing query in `src/CameraCatalog/Application/Queries/` — filter on status, not in the endpoint
- [ ] T018 [US3] `includeRetired` query parameter on `GET /cameras` in `src/CameraCatalog/Api/CameraEndpoints.cs`, default `false`, per contracts/cameras-api.md
- [ ] T019 [P] [US3] Query/handler tests — default excludes; `includeRetired=true` includes and each carries its status
- [ ] T020 [US3] Integration — a retired camera is absent from the default listing and present with `includeRetired=true`

---

## Phase 4: FR-008 — the stream follows the camera

**Not a user story.** This is the cross-context half of FR-008, which was
settled by the assistant's recommendation rather than a user decision. **If that
is overturned, this entire phase drops out** and Phases 1–3 and 5 stand — except
that Phase 5 becomes unnecessary too, because nothing would retire the stream.

- [ ] T021 [P] Terminal value on `StreamState` in `src/StreamDistribution/Domain/Stream/StreamState.cs`
- [ ] T022 `Stream.Retire(IClock)` in `src/StreamDistribution/Domain/Stream/Stream.cs` — idempotent, **and `ReportHealthy`/`ReportDegraded`/`ReportOffline` must refuse a retired stream**
- [ ] T023 [P] Domain tests in `tests/StreamDistribution.Domain.Tests/Stream/StreamRetirementTests.cs` — retire from Provisioning, Healthy, Degraded and Offline; **a health report after retirement is refused from each**
- [ ] T024 `RetireStreamCommand` + handler in `src/StreamDistribution/Application/Commands/` — retires the aggregate and calls `IRtspGateway.RemovePathAsync`; the row is **kept**
- [ ] T025 [P] Handler tests in `tests/StreamDistribution.Application.Tests/` — path removed; aggregate terminal; **a gateway failure does not lose the retirement** (the row must still be terminal)
- [ ] T026 `CameraRetiredIntegrationEventHandler` in `src/StreamDistribution/Application/EventHandlers/CameraRetiredIntegrationEventHandler.cs` — mirrors `CameraRegisteredIntegrationEventHandler`
- [ ] T027 Integration in `tests/Integration.Tests/StreamDistribution/` — retiring a camera removes its MediaMTX path, leaves the stream terminal, and **leaves the row present**
- [ ] T028 [P] Integration — retirement succeeds while the SFU is unreachable (FR-008a): the camera is retired even when teardown cannot complete

---

## Phase 5: The sweep stops looking (research §4)

**Last, and non-optional.** `StreamHealthWatcher` lists every stream and probes
each one. A retired stream whose path has been removed probes, fails, and — since
#1801 was fixed and the watcher announces *every* health change rather than one
per sweep — becomes a permanent source of announcements and audit rows for
hardware that does not exist.

Shipping Phase 4 without this makes the system noisier than before the feature.

- [ ] T029 Exclude retired streams from the sweep in `src/StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs` — filter in the listing query
- [ ] T030 [P] Test in `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/` — a retired stream is never probed and opens no scope, using the counting scope factory already there from #1804
- [ ] T031 Integration — after retiring, **no further `StreamHealthChangedV1` for that camera**, asserted over a window rather than once

---

## Phase 6: Polish & Cross-Cutting

- [ ] T032 [P] Confirm the retirement is audited (FR-010) — `audit_events` holds one `CameraRetiredV1` per retirement, with the operator
- [ ] T033 [P] Reinstate the withdrawn entry in `specs/015-camera-fab-scoping/contracts/cameras-api.md` — point it at this feature and note the key changed from name to identifier
- [ ] T034 Full suite, nothing excluded or weakened; Release build with analyzers clean
- [ ] T035 Verification note on the PR following [quickstart.md](./quickstart.md), including the trace across both contexts and the "no further health announcements" check

---

## Dependencies

```
T001, T002  ─┐
             ├─► T003 ─► T004
             │      └──► T005 ─► T006 ─► T007
             │                    └─► T008
             │                    └─► T009 ─► T010 ─► T011, T012     US1 ✅ MVP
             │                                          ↓
             │                                    T013 … T016        US2
             │                                          ↓
             │                                    T017 … T020        US3
             │                                          ↓
             └────────────────────────────────► T021 … T028          FR-008
                                                       ↓
                                                 T029 … T031         the sweep
                                                       ↓
                                                 T032 … T035
```

**T002 is the only thing Phase 4 needs from Phase 1.** The contract is the seam;
everything else in the two contexts is independent.

## Parallel opportunities

- **T001 and T002** — different projects, no shared file.
- **T004 and T007** — domain and application tests, different projects.
- **T014, T015** — independent assertions once T013's shape exists.
- **T021 and T023** — the state value and its tests.
- **T032 and T033** — documentation and audit assertion.

## Implementation strategy

**MVP is Phase 1.** A camera reaching its terminal state is the feature; the
rest is payoff.

**Phase 2 writes no production code.** If any task there needs some, research §1
was wrong and that is a finding.

**Phases 4 and 5 ship together or not at all.** Phase 4 without Phase 5 leaves
the health watcher sweeping streams for cameras that no longer exist, which is
worse than shipping neither.

---

## Three things most likely to go wrong

**A late health probe resurrects a retired stream.** Nothing in the aggregate
prevents it today — `ReportHealthy` sets state unconditionally. The watcher and
the retirement race by construction, and T023 is where that is caught.

**Idempotency implemented as "no error" instead of "no event".** T004 and T007
both assert the *event count*, not the return value, because a handler that
succeeds while re-raising looks correct from the endpoint and double-announces
to every consumer — the audit trail would show a camera retired twice.

**404 quietly becoming 403.** FR-004 is a security property: a distinguishable
refusal lets one fab enumerate another's camera names. It is exactly the kind of
thing a later change "improves" into a more helpful error, which is why T012
asserts the two responses are identical rather than merely both refusals.
