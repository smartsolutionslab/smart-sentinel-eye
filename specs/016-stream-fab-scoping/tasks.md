# Tasks: Fab-scope stream distribution

**Input**: Design documents from `/specs/016-stream-fab-scoping/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/streams-api.md](./contracts/streams-api.md)

**Tests**: Included. ADR-0052 mandates TDD for the domain, and two things here
are only ever caught by a test: the six `FabIdentifier` copies drifting apart,
and FR-009's fail-closed behaviour, which is invisible when it works.

**Depends on**: spec 015, merged. This reads `CameraRegisteredV1.Metadata.Fab`
and resolves existing streams against a fab-scoped camera catalogue.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: US1–US3 from spec.md
- Exact file paths in every task

---

> **What this feature does NOT have, and why the task list looks short**
>
> No `FabResolution`, no `?fabId=` on a write, no `STREAM_FAB_REQUIRED`, no
> ambiguity error, no UI work, and **no migration backfill**.
>
> There is no operator-driven write in this context — a stream is provisioned
> by an event handler — so the whole ADR-0114 decision table is irrelevant.
> Specs 013–015 each carry those tasks; adding them by symmetry is what left
> spec 015 with three withdrawn requirements
> ([research.md](./research.md) §4).
>
> Phase 2 lands the aggregate and the derivation together, as spec 015 did, so
> no placeholder fab is ever written.

---

## Phase 1: Setup

- [x] T001 [P] Add `src/StreamDistribution/Domain/Stream/FabIdentifier.cs` as a `StringValueObject` mirroring `src/CameraCatalog/Domain/Camera/FabIdentifier.cs` exactly: 2–32 chars, lowercase letters/digits/`-`, starting with a letter. Per-context by ADR-0044 — the sixth copy.
- [x] T002 [P] Add `tests/StreamDistribution.Domain.Tests/Stream/FabIdentifierTests.cs` covering the grammar, rejections, boundary lengths and equality. **The only thing keeping six copies in step.** Use plain `null`, not `null!` — SonarAnalyzer S8970 fails the Release build on a null-forgiving operator where NRT is disabled, which caught spec 015 at T002.

**Checkpoint**: The context has a fab type. Nothing uses it.

---

## Phase 2: Foundational (blocking) — the aggregate and the derivation together

- [x] T003 Add nullable `Fab` to `src/StreamDistribution/Domain/Stream/Stream.cs`: required by `Provision`, **no setter of any kind**. FR-002 says a stream's fab and its camera's must not be able to differ; the guarantee is that the aggregate cannot express it. Nullable because existing rows have none yet — see [data-model.md](./data-model.md).
- [x] T004 Add `WithFab` to the stream builder in `tests/StreamDistribution.Domain.Tests/`, defaulting to `munich`.
- [x] T005 Assert in `tests/StreamDistribution.Domain.Tests/Stream/` that `Fab` survives `ReportDegraded` and every other state transition unchanged, plus a structural guard that no public setter exists. **Check first which transitions actually exist** — spec 015's T005 asserted against a decommission that was never implemented.
- [x] T006 Map the column in `src/StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs`: `fab` **nullable**, max length 32, value-converted. Add a plain index on `fab`; **no unique index** — a stream is keyed by its camera, which is already globally unique.
- [x] T007 Generate the migration under `src/StreamDistribution/Infrastructure/Persistence/Migrations/`. It adds the column and the index and **nothing else**. Delete any scaffolded `defaultValue`. **There is deliberately no backfill**: cameras are in another database, and this feature refuses to guess ([research.md](./research.md) §5). This is the first fab migration in the product with no `DO $$` block — the absence is the design, not an omission.
- [x] T008 Thread the fab through `ProvisionStreamCommand` and its handler in `src/StreamDistribution/Application/Commands/`, taking it from the command rather than any caller.
- [x] T009 Read `Metadata.Fab` in `src/StreamDistribution/Application/EventHandlers/CameraRegisteredIntegrationEventHandler.cs` and pass it to the command. **A message carrying no fab provisions nothing and is logged** (FR-004) — a distinct message, not shared with any other drop.

**Checkpoint**: New streams carry their camera's fab. `git grep "Placeholder fab" -- src/StreamDistribution/` must return nothing.

---

## Phase 3: User Story 2 — A stream inherits its camera's fab (P1) 🎯 MVP

> US2 before US1 deliberately: a scoped read over unattributed streams shows
> nothing to anyone, so the derivation must work before the scoping is
> meaningful.

- [x] T010 [P] [US2] Add cases to the handler tests under `tests/StreamDistribution.Application.Tests/EventHandlers/` asserting a dresden camera's event provisions a dresden stream. **Assert dresden, not munich** — everything else defaults to munich and a hard-coded fab would pass.
- [x] T011 [P] [US2] Add a case asserting an event with no fab provisions **nothing** and logs it (FR-004). Assert the downstream effect — that no stream was added — not merely that nothing threw.
- [x] T012 [US2] Add `tests/Integration.Tests/StreamDistribution/StreamFabDerivationIntegrationTests.cs`: register a camera in each fab over real HTTP, then assert each provisioned stream carries its camera's fab. Covers SC-003.

**Checkpoint**: SC-003 observed. New streams are attributed.

---

## Phase 4: User Story 1 — An operator sees only their own plant's video (P1)

- [x] T013 [US1] Thread the caller's fabs into both queries and handlers in `src/StreamDistribution/Application/Queries/`. The filter is `fab IN (caller's fabs)` — **NULL satisfies no `IN`**, so FR-009 falls out of the query rather than needing a special case.
- [x] T014 [US1] Add fab resolution to the two read endpoints in `src/StreamDistribution/Api/StreamEndpoints.cs` using `FabClaims` and `FabResolution.ResolveForReadAsync` **unchanged**. **Do not touch `POST /authorize`** — the caller is MediaMTX and holds no fab ([contracts/streams-api.md](./contracts/streams-api.md)).
- [x] T015 [US1] Return **404** for a stream in a fab the caller lacks, byte-identical to a camera with no stream (FR-006). No 403 — a stream record carries the MediaMTX path its video is served on.
- [x] T016 [P] [US1] Add `Fab` to the stream DTO and its mapper in `src/StreamDistribution/Application/DTOs/`, so a multi-fab operator can see which plant a stream belongs to without cross-referencing the camera catalogue.
- [x] T017 [US1] Declare **403** on both scoped endpoints in `src/StreamDistribution/Api/StreamEndpoints.cs`; it became reachable with this feature. Spec 013 shipped this wrong on one endpoint and it took a review to catch.
- [x] T018 [P] [US1] Add handler tests under `tests/StreamDistribution.Application.Tests/Queries/` for the scoping and the not-found path.
- [x] T019 [US1] Add `tests/Integration.Tests/StreamDistribution/StreamFabScopingIntegrationTests.cs` with `op-dresden@dresden.test` and `op-multi@smart-sentinel-eye.test`: listing scoped, another fab's stream 404 **compared field by field** with `traceId` removed, and 403 for a fab not held. Covers SC-001 and SC-002.

**Checkpoint**: The video is closed. SC-001 and SC-002 observed.

---

## Phase 5: User Story 3 — Streams that predate this feature acquire their fab (P2)

- [ ] T020 [US3] Add `src/StreamDistribution/Infrastructure/Attribution/StreamFabAttributionService.cs` as an `IHostedService` **separate from `MediaMtxReconciler`** ([research.md](./research.md) §1). It selects streams where `fab IS NULL`, resolves each from CameraCatalog over HTTP, and sets it.
- [ ] T021 [US3] Register the camera-catalog client by name so Aspire service discovery resolves it, mirroring `ReverseIndexSeederHostedService` in SystemVariables. **This is the first HTTP call from this context to another** — plan.md §III records it as a bounded exception.
- [ ] T022 [US3] Log **both** the count attributed and the count unresolved (FR-008, FR-010), and log **nothing** when there are no null fabs — the steady state must be silent. A stream whose camera cannot be resolved stays null; it is never defaulted.
- [ ] T023 [US3] Make the failure path **deliberate**: an unreachable CameraCatalog must leave streams unattributed and not block host start, asserted by a test. plan.md §III flags that this behaviour is currently inherited from an existing `try/catch` in the reconciler rather than chosen.
- [ ] T024 [P] [US3] Add tests under `tests/StreamDistribution.Infrastructure.Tests/` (create the project if absent, mirroring `CameraCatalog`) covering: attribution fills from the camera, an unresolvable camera stays null, and a second run with no null fabs does nothing.
- [ ] T025 [US3] Add an integration case asserting a stream with a null fab is returned to **nobody** — not to its own fab's operator, not to a multi-fab operator. Covers SC-004 and is the only test of FR-009; when it works it is invisible.

**Checkpoint**: SC-004 observed. Existing streams attributed or explicitly unresolved.

---

## Phase 6: Polish

- [x] T026 [US1] Establish a latency baseline for the read path **before** T013 lands, and re-measure after. SC-005 says no measurable regression; measured afterwards only, it compares the new code against itself. *If T013 has already landed when this is picked up, say so on the PR rather than measuring twice after the fact.*
- [x] T027 Run `scripts/coverage-check.ps1 -Configuration Release` and confirm `StreamDistribution.Domain` clears 90% and `Application` 80%.
- [ ] T028 Walk [quickstart.md](./quickstart.md) end to end and record the observations on the PR. **"Done" is the observations.** Step 1 is the one that cannot be faked: blank the fabs, restart, and confirm streams land in *their own* fabs — if everything lands in munich, the derivation silently fell back to a default.
- [ ] T029 Comment on #1155 that StreamDistribution is no longer among the contexts missing the guard, and on #1397 that only LayoutComposition remains. **Write `Closes #N, closes #M`** — the keyword must precede each number, and it only fires on merge to the default branch. Both traps caught spec 015.

---

## Dependencies

```text
Phase 1 (T001–T002)      setup
      ↓
Phase 2 (T003–T009)      BLOCKING: aggregate + derivation, no placeholder
      ↓
Phase 3 US2 (T010–T012)  🎯 MVP — derivation works
      ↓
Phase 4 US1 (T013–T019)  scoping is meaningful only once US2 holds
      ↓
Phase 5 US3 (T020–T025)  independent of US1; needs Phase 2
      ↓
Phase 6 (T026–T029)      polish
```

**US1 depends on US2**, unusually. A scoped read over unattributed streams
shows nothing to anyone, so scoping first would look like a broken listing.
US3 is independent of both once Phase 2 lands.

## Parallel opportunities

- **Phase 1**: T001 and T002 together.
- **Phase 3**: T010 and T011 together.
- **Phase 4**: T016 and T018 alongside T013–T015.
- **Phase 5**: T024 alongside T020–T023.
- **Across phases**: US3 can proceed concurrently with US1 — different files.

## Implementation strategy

**MVP is Phases 1–3.** New streams carry their camera's fab. Shippable and
independently valuable even before the reads are scoped.

**Phase 4 is the security half** and should not lag far behind — a stream
record carries the path its video is served on.

**T026 must be taken before T013**, or SC-005 cannot be assessed. This is the
same gate spec 014 used for its reverse-index rewrite, and the reason it was a
task rather than an afterthought.
