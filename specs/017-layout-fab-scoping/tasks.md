# Tasks: Fab-scope layout composition

**Input**: Design documents from `/specs/017-layout-fab-scoping/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/layouts-api.md](./contracts/layouts-api.md), [contracts/hub-frames.md](./contracts/hub-frames.md)

**Tests**: Included. ADR-0052 mandates TDD for the domain, and three things here are only ever caught by a test: the seven `FabIdentifier` copies drifting apart, FR-011/FR-013 (a frame reaching *nobody*, which looks identical to a broken push), and FR-015 (an unknown camera must be refused, or FR-014 is decorative).

**Depends on**: spec 016, merged. Nothing in the code, but the fab programme's conventions and ADR-0116's precedent for the §III argument.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: US1–US5 from spec.md
- Exact file paths in every task

---

> **The two halves must not blur into each other**
>
> Half A (US1, US2, US5) is the fifth application of ADR-0114 and should look
> like specs 013–015. Half B (US3) touches **no aggregate, no column, no
> domain type** — it is a join and two handlers.
>
> **If a Half B task starts wanting a column, it has drifted into giving an
> overlay a fab, which ADR-0115 forbids.** That is the one way this feature
> goes wrong.

---

## Phase 1: Setup

- [x] T001 [P] Add `src/LayoutComposition/Domain/Layout/FabIdentifier.cs` as a `StringValueObject` mirroring `src/StreamDistribution/Domain/Stream/FabIdentifier.cs` exactly: 2–32 chars, lowercase letters/digits/`-`, starting with a letter. Per-context by ADR-0044 — the **seventh** copy.
- [x] T002 [P] Add `tests/LayoutComposition.Domain.Tests/Layout/FabIdentifierTests.cs` covering the grammar, rejections, boundary lengths and equality. **The only thing keeping seven copies in step.** Use plain `null`, not `null!` — SonarAnalyzer S8970 fails the Release build on a null-forgiving operator where NRT is disabled, which caught specs 015 and 016 at the same task.

**Checkpoint**: The context has a fab type. Nothing uses it.

---

## Phase 2: Foundational (blocking) — the aggregate, the column and the backfill

- [x] T003 Add `Fab` to `src/LayoutComposition/Domain/Layout/Layout.cs`: required by `CreateDraft`, **no setter and no `MoveToFab`** (FR-002). Non-nullable — unlike spec 016 there is no transitional phase, because the backfill runs in the same migration.
- [x] T004 Add `WithFab` to `tests/LayoutComposition.Domain.Tests/Layout/Builders/LayoutBuilder.cs`, defaulting to `munich`.
- [x] T005 Assert in `tests/LayoutComposition.Domain.Tests/Layout/LayoutTests.cs` that `Fab` survives every revision transition unchanged, plus a structural guard that no public setter exists. **Check which transitions actually exist first** — publish, archive, branch, edit, revert; spec 015's equivalent asserted against a decommission that was never implemented.
- [x] T006 Map the column in `src/LayoutComposition/Infrastructure/Persistence/Configurations/LayoutConfiguration.cs`: `fab` **NOT NULL**, max length 32, value-converted. Replace `ix_layouts_name` with `ix_layouts_fab_name` on `(fab, name)`, and add a plain `ix_layouts_fab`. **Neither unique** — the name rule is application-level today and promoting it is a separate decision ([data-model.md](./data-model.md)).
- [x] T007 Add a plain index on `layout_revision_tiles(overlay_id)` in the same configuration. The column exists but has never been queried; the Half B join scans every tile in the product without it.
- [x] T008 Generate the migration under `src/LayoutComposition/Infrastructure/Persistence/Migrations/`. **Hand-correct it to three steps**: add nullable, `DO $$` backfill to `'munich'` with `RAISE WARNING` naming the count, then `SET NOT NULL` — mirroring `20260810164633_FabScopeCameras`. **Delete any scaffolded `defaultValue`**: `AddColumn(nullable: false, defaultValue: "")` writes `fab = ''`, which is not a valid `FabIdentifier`, so every layout fails to materialise on the next read. That is exactly what spec 015 caught.
- [x] T009 Thread the fab through `CreateLayoutDraftCommand` and its handler in `src/LayoutComposition/Application/Commands/`, taking it from the command rather than any caller.
- [x] T010 Make the name-uniqueness check fab-scoped (FR-019): `GetByNameAsync` on `ILayoutRepository` takes the fab, and `CreateLayoutDraftCommandHandler` passes it. **This is a leak, not a tidy-up** — a global check answers `409 LAYOUT_NAME_TAKEN` for a layout the caller cannot see.

**Checkpoint**: A layout has a fab, existing rows are attributed, and the name check no longer spans fabs. Nothing is scoped yet.

---

## Phase 3: User Story 1 — An operator works only in their own plant's layouts (P1) 🎯 MVP

**Goal**: The API is closed. **Independent test**: as a Dresden-only operator, list layouts and address a Munich one by identifier.

- [x] T011 [US1] Add fab resolution to `POST /layouts` in `src/LayoutComposition/Api/LayoutEndpoints.Commands.cs` using `FabResolution.ResolveForWriteAsync` **unchanged**, with error code `LAYOUT_FAB_REQUIRED`. Mirror `CameraEndpoints.ResolveWriteFabAsync`, including its **per-entry** parse of the caller's groups — an unusable group must not fail the whole request.
- [x] T012 [US1] Thread the caller's fabs into both queries and handlers in `src/LayoutComposition/Application/Queries/`. The filter is `fab IN (caller's fabs)`.
- [x] T013 [US1] Add fab resolution to `GET /layouts` and `GET /layouts/{id}` in `src/LayoutComposition/Api/LayoutEndpoints.Queries.cs` using `FabResolution.ResolveForReadAsync` **unchanged**.
- [x] T014 [US1] Return **404** for a layout in a fab the caller lacks, byte-identical to one that never existed (FR-006). No 403 — the caller addressed a layout, so the answer is about the layout.
- [x] T015 [US1] Apply the same 404 to **all five remaining writes** in `src/LayoutComposition/Api/LayoutEndpoints.Commands.cs` — publish, archive, branch, edit, revert. **None of them takes `?fabId=`**: the layout already has a fab, and letting the caller name one would allow it to disagree.
- [x] T016 [US1] **Resolve the fab before reading any precondition** on those five. Answering "revision not found" for a layout in another fab confirms the layout exists ([contracts/layouts-api.md](./contracts/layouts-api.md)).
- [x] T017 [P] [US1] Add `Fab` to `src/LayoutComposition/Application/DTOs/LayoutDto.cs` and its mappers, so a multi-fab operator can tell two plants' layouts apart without a second request.
- [x] T018 [US1] Declare **403** on all eight endpoints in `src/LayoutComposition/Api/LayoutEndpoints.cs`; it became reachable with this feature. Spec 013 shipped this wrong on one endpoint and it took a review to catch.
- [x] T019 [P] [US1] Add handler tests under `tests/LayoutComposition.Application.Tests/Queries/` for the scoping and the not-found path.
- [x] T020 [P] [US1] Add a case to `tests/LayoutComposition.Application.Tests/Commands/CreateLayoutDraftCommandHandlerTests.cs` asserting the same name is accepted in a second fab and still refused within one (FR-019). Covers SC-007.
- [x] T021 [US1] Add `tests/Integration.Tests/LayoutComposition/LayoutFabScopingIntegrationTests.cs` with `op-dresden@dresden.test` and `op-multi@smart-sentinel-eye.test`: listing scoped, another fab's layout 404 **compared field by field** with `traceId` removed, 403 for a fab not held, and the full ADR-0114 write table. **Assert dresden, not munich** — everything else defaults to munich and a broken inference would pass. Covers SC-001 and SC-002.

**Checkpoint**: SC-001, SC-002 and SC-007 observed. The API is closed; the hub is not.

---

## Phase 4: User Story 2 — A kiosk hears only about its own plant's layouts (P1)

**Goal**: Two of the four frames. **Independent test**: connect a Dresden kiosk, publish and archive in both fabs, record every frame.

- [x] T022 [US2] Add `Fab` to `LayoutRevisionPublishedNotification` and `LayoutRevisionArchivedNotification` in `src/LayoutComposition/Domain/Layout/ILayoutLifecycleBroadcaster.cs`.
- [x] T023 [US2] Fill it in `src/LayoutComposition/Application/EventHandlers/LayoutRevisionPublishedDomainEventHandler.cs` and `LayoutRevisionArchivedDomainEventHandler.cs`. The layout is in hand there — **no query is needed**, and adding one is a sign the fab was taken from the wrong place.
- [x] T024 [US2] Swap `Clients.All` for `Clients.Group(LayoutLifecycleHub.FabGroup(...))` on the two layout frames in `src/LayoutComposition/Infrastructure/Broadcasting/SignalRLayoutLifecycleBroadcaster.cs`, matching the two already-scoped sends.
- [x] T025 [P] [US2] Extend `tests/LayoutComposition.Domain.Tests/Layout/LifecycleNotificationTests.cs` for the new field.

**Checkpoint**: Layout lifecycle no longer crosses fabs.

---

## Phase 5: User Story 3 — An overlay's lifecycle reaches only the plants that use it (P1)

> **No aggregate, no column, no domain type.** If this phase grows one, it has
> drifted into giving an overlay a fab (ADR-0115).

- [x] T026 [US3] Add a read-side query to `src/LayoutComposition/Application/Queries/` answering "which fabs have a **published** layout whose tiles carry this overlay", per the join in [data-model.md](./data-model.md). `state = 'Published'` is FR-013 and lives **only** here.
- [x] T027 [US3] Add `Fabs` (a set) to `OverlayLifecyclePublishedNotification` and `OverlayLifecycleArchivedNotification` in `src/LayoutComposition/Domain/Layout/ILayoutLifecycleBroadcaster.cs`. **Plural, unlike the layout frames** — an overlay is used by however many fabs.
- [x] T028 [US3] Resolve the fabs in `src/LayoutComposition/Application/EventHandlers/OverlayRevisionPublishedV1Handler.cs` and `OverlayRevisionArchivedV1Handler.cs`. **Not in the broadcaster** — it maps and sends, and a query there would make it the only piece of `Infrastructure/Broadcasting` that reads state.
- [x] T029 [US3] Send once per resolved fab in `SignalRLayoutLifecycleBroadcaster.cs`. **An empty set must send nothing at all** (FR-011) — not a send to an empty group.
- [x] T030 [P] [US3] Add handler tests under `tests/LayoutComposition.Application.Tests/EventHandlers/` for: referenced by one fab, by both, by **none**, and by a **draft only**. The last two are FR-011 and FR-013 and are invisible when they work.
- [ ] T031 [US3] Add `tests/Integration.Tests/LayoutComposition/OverlayFrameFabScopingIntegrationTests.cs` driving a real hub connection per fab. Assert on the **absence** of a frame over a bounded wait, not on "nothing threw". Covers SC-004.

**Checkpoint**: SC-003 and SC-004 observed. #1397 is closed.

---

## Phase 6: User Story 5 — A layout cannot borrow another plant's camera (P1)

> US5 before US4 deliberately: US4 is a one-time transition and US5 is an open
> hole. Numbered 5 in the spec because it was found late, not because it matters
> least.

- [ ] T032 [US5] Add `ICameraFabGuard` to `src/LayoutComposition/Application/Tiles/`, returning **the offending camera identifiers** rather than a boolean — US5 scenario 1 requires the refusal to name the tile, and a boolean cannot.
- [ ] T033 [US5] Implement `CameraCatalogFabGuard` in `src/LayoutComposition/Infrastructure/Cameras/`, calling `GET /cameras?fabId=<layout fab>` with **the caller's own token forwarded**. No service account — that is what makes this a smaller §III exception than ADR-0116's ([research.md](./research.md) §1).
- [ ] T034 [US5] Register the camera-catalog client by name in `src/LayoutComposition/Infrastructure/LayoutCompositionInfrastructureModule.cs` so Aspire service discovery resolves it, and add `.WithReference(cameraCatalog)` to `layout-composition` in `src/AppHost/AppHost.cs`. **This is the first HTTP call from this context to another** — plan.md §III records it as a bounded exception.
- [ ] T035 [US5] Enforce the guard in `CreateLayoutDraftCommandHandler` and `EditDraftRevisionCommandHandler`. **An unresolvable camera is refused by the same path** (FR-015) — a separate branch treating "unknown" leniently makes FR-014 bypassable.
- [ ] T036 [US5] Record a refused cross-fab tile in `src/LayoutComposition/Application/Log.cs` (constitution §VII). It is an attempt to cross a boundary, not a typo.
- [ ] T037 [P] [US5] Add handler tests with a fake `ICameraFabGuard` under `tests/LayoutComposition.Application.Tests/Commands/`: same-fab accepted, cross-fab refused naming the tile, unknown camera refused.
- [ ] T038 [US5] Add an integration case to `LayoutFabScopingIntegrationTests.cs`: a dresden operator's tile naming a munich camera is refused. Covers SC-006.

**Checkpoint**: SC-006 observed. The route around the isolation is closed.

---

## Phase 7: User Story 4 — Layouts that predate this feature acquire a fab (P2)

- [ ] T039 [US4] Verify the backfill on populated data: `SELECT count(*) FROM layouts WHERE fab IS NULL` → **0**, and the `RAISE WARNING` count appears in the migration-runner log. The notice reaches a log at all only because #1395 wired the Npgsql notice handler.
- [ ] T040 [US4] Confirm pre-existing tiles are **not** retro-validated against FR-014 (FR-018). The mismatch would come from the migration's own guess; failing over it blocks the deployment the migration exists to fix.

**Checkpoint**: SC-005 observed.

---

## Phase 8: Polish

- [ ] T041 Establish a push-path latency baseline **before** T024 lands, and re-measure after. SC-008 says no measurable regression; measured afterwards only, it compares the new code against itself. *If T024 has already landed when this is picked up, say so on the PR rather than measuring twice after the fact.*
- [ ] T042 Confirm `ResolvedOverlayTextChanged` (#1396) and `OverlayHighlightChanged` (#1398) still behave exactly as before. `ResolvedOverlayTextChanged` is the one frame on the latency-critical leg (event → overlay ≤ 200 ms).
- [ ] T043 Run `scripts/coverage-check.ps1 -Configuration Release` and confirm `LayoutComposition.Domain` clears 90% and `Application` 80%.
- [ ] T044 Walk [quickstart.md](./quickstart.md) end to end and record the observations on the PR. **"Done" is the observations.** Step 4 is the one that cannot be faked: a two-fab kiosk session, asserting on frames that must *not* arrive.
- [ ] T045 Comment on #1155 that no context is now missing the guard, and on #1397 that all six frames are scoped — **then close #1397**. **Write `Closes #N, closes #M`**: the keyword must precede each number, and it only fires on merge to the default branch. Both traps caught spec 015; repeating the keyword per number is what made spec 016's 29 issues close.

---

## Dependencies

```text
Phase 1 (T001–T002)      setup
      ↓
Phase 2 (T003–T010)      BLOCKING: aggregate + column + backfill + name scope
      ↓
Phase 3 US1 (T011–T021)  🎯 MVP — the API is closed
      ↓
Phase 4 US2 (T022–T025)  layout frames — needs only Phase 2
Phase 5 US3 (T026–T031)  overlay frames — needs Phase 2 (for layouts.fab)
Phase 6 US5 (T032–T038)  independent of US2/US3; needs Phase 2
Phase 7 US4 (T039–T040)  verification of Phase 2's migration
      ↓
Phase 8 (T041–T045)      polish
```

**US2, US3 and US5 are mutually independent** once Phase 2 lands — different
files, different concerns. US1 comes first because it is the MVP, not because
the others need it.

**US3 depends on Phase 2, not on US1.** The join reads `layouts.fab`, which
Phase 2 provides; it does not need the endpoints scoped.

## Parallel opportunities

- **Phase 1**: T001 and T002 together.
- **Phase 3**: T017, T019 and T020 alongside T011–T016.
- **Phase 4**: T025 alongside T022–T024.
- **Phase 5**: T030 alongside T026–T029.
- **Phase 6**: T037 alongside T032–T036.
- **Across phases**: US2, US3 and US5 can proceed concurrently — the only
  shared file is `SignalRLayoutLifecycleBroadcaster.cs` (US2 and US3 touch
  different methods in it).

## Implementation strategy

**MVP is Phases 1–3.** The API is closed and a layout has a fab. Shippable and
independently valuable even before the hub is scoped.

**Phases 4 and 5 are the reason the feature exists** — #1397 is a push leak,
not an API leak — and should not lag far behind.

**Phase 6 (US5) is the one that is not in #1397 at all.** It was found while
writing the spec: cameras have had a fab since spec 015, so a cross-fab tile
became expressible then. Skipping it leaves a route around everything the other
five phases build.

**T041 must be taken before T024**, or SC-008 cannot be assessed. Same gate
spec 016 used for its read path, and the reason it is a task rather than an
afterthought.
