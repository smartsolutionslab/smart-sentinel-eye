# Tasks 454: A replay refuses a client that has changed since its key was answered

Spec: [spec.md](./spec.md) · Plan: [plan.md](./plan.md) · Issue #3096 (feature-level; no
per-task issues). **Phase 4a: red** (behaviour-changing), plus the characterisation set in spec
§5, which must pass **unmodified** (a test double may gain the new `IIdempotencyStore` member and
nothing else).

Agents: phase 4a `test-writer`; phase 4b `backend-engineer`; phase 6 `backend-reviewer` +
**`security-reviewer` (mandatory)**.

**Parallelism.** T001–T005 own disjoint new test files and may run in parallel. In 4b, **T006
(ServiceDefaults) blocks everything else**: the store SQL names the new column and the Identity
code uses the new types. After T006, the seven migrations T007–T013 are `[P]` (one context each,
disjoint directories), and T014 → T015 → T016 (Identity) run in order. Every commit must build on
its own (ADR-0087), so the migrations land in the same commit as, or before, the store SQL that
reads the column — never after.

## Phase 4a: red (test-writer)

- [ ] **T001 [P] [US1]** `tests/Identity.Application.Tests/Queries/ReplayRegisteredClientQueryHandlerSupersededTests.cs`:
  the four handler cases in plan §7 with a hand-written repository fake whose `GetVersionAsync`
  can answer differently from the first read, and a Keycloak fake that counts reads. Red by
  compilation (`AnsweredAt`, `ReplayedKeySuperseded`, `GetVersionAsync` do not exist).
- [ ] **T002 [P] [US1]** `tests/ServiceDefaults.Tests/Idempotency/IdempotentResourceVersionTests.cs`:
  the four cases in plan §7 against a recording store. Red by compilation.
- [ ] **T003 [P] [US1]** `tests/Architecture.Tests/WebhookRotationReplaySupersededEndpointTests.cs`
  on `IdentityEndpointHost`: a completed key with no stored version answers 409
  `IDEMPOTENCY_KEY_SUPERSEDED` and never invokes the replay query; with a stored version the
  query receives it.
- [ ] **T004 [P] [US1]** `tests/Integration.Tests/Identity/WebhookRotationReplaySupersededIntegrationTests.cs`:
  spec §4 steps 1–5, the legacy-NULL row, K2 still replaying, and the 409 body containing neither
  secret. Add the class to a `ci-shards/shard-N.filter`.
- [ ] **T005 [P] [US1]** `tests/Integration.Tests/ServiceDefaults/IdempotencyResourceVersionStoreIntegrationTests.cs`:
  round-trip, NULL when absent, fence still holds with a version. Add to a shard filter.
- [ ] **T005a** Run T001–T005 and the spec §5 characterisation set; return the **verbatim**
  output. Expected: T001–T005 red (compile or assertion), characterisation green.
  Commit: `test(3096): pin the superseded refusal for a replay after a rotation`.

## Phase 4b: green (backend-engineer; may not edit T001–T005)

- [ ] **T006 [US1]** ServiceDefaults per plan §1: `IdempotentOutcome.ResourceVersion` +
  `CreatedAt`; `IdempotentReplay`; `IdempotencyReservation.ResourceVersion` +
  `CompletedWith(Guid, Option<AggregateVersion>)`; `IIdempotencyStore.CompleteAsync` versioned
  overload; store SQL (typed nullable parameter); `IdempotentRequest` versioned overload with the
  Guid overload forwarding; `IdempotencyKeyTable.AddResourceVersion` / `DropResourceVersion`.
  Test doubles implementing `IIdempotencyStore` gain the member only.
- [ ] **T007 [P] [US1]** Automation migration `<ts>_AddIdempotencyResourceVersion` (+ Designer).
- [ ] **T008 [P] [US1]** CameraCatalog migration, same shape.
- [ ] **T009 [P] [US1]** EventIngestion migration, same shape.
- [ ] **T010 [P] [US1]** Identity migration, same shape.
- [ ] **T011 [P] [US1]** LayoutComposition migration, same shape.
- [ ] **T012 [P] [US1]** OverlayDesigner migration, same shape.
- [ ] **T013 [P] [US1]** SystemVariables migration, same shape.
  Mirror each context's `*_AddIdempotencyRequestBinding.cs`; generate with `dotnet ef migrations
  add` so the Designer file exists; the model snapshot must not change. Verify with `git diff
  --stat` on every `Migrations/` directory (seven new pairs).
- [ ] **T014 [US1]** Identity Domain + Infrastructure: `IRegisteredClientRepository.GetVersionAsync`
  and its `AsNoTracking` implementation.
- [ ] **T015 [US1]** Identity Application per plan §2: `AnsweredAt`, `ReplayedKeySuperseded`,
  the pre- and post-read checks, the rewritten doc comments on the handler and `ReplayedClientDto`.
  Devices and kiosks pass `Option<AggregateVersion>.None` at their existing call sites (the only
  edit to those two files).
- [ ] **T016 [US1]** Identity Api per plan §4: `CreatedAt` with `dto.Version`, the versioned
  overload, NULL → superseded. Confirm `IdentityRouteTableCharacterisationTests` is unmodified and
  green; if the baseline moves, stop and report.
- [ ] **T017** Run every test from T005a plus the full Identity, ServiceDefaults and Architecture
  suites, the Release build (`TreatWarningsAsErrors`), `dotnet format --verify-no-changes`, and the
  coverage gates (Application ≥ 80%, Shared/ServiceDefaults ≥ 90%). Commits per ADR-0030, e.g.
  `feat(3096): record the answered version with a completed idempotency key`,
  `fix(3096): refuse a rotate replay once the client has moved on`.

## Phase 5–6

- [ ] **T018** `/verify`: run spec §4 against the live stack (check the AppHost PID started after
  the last commit) and quote the 409 body and the `resource_version` rows in the PR.
- [ ] **T019** `backend-reviewer` + `security-reviewer`: secret never logged or returned on a
  refusal; 422 still precedes 409; fence intact; the six non-Identity contexts' idempotency
  integration tests green.
