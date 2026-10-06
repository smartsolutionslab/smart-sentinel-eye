# Tasks — Spec 302, a key that names its request

**Issues:** #2424, #2492 (both on Project #13, *In Progress*) · **Spec:** `spec.md` · **Plan:** `plan.md`
**Phase-4a colour:** **RED** — behaviour-changing. A test that arrives green is a phase-4 failure (ADR-0139).
**Engineer:** `backend-engineer`. **Reviewers (phase 6):** `security-reviewer` (mandatory — secret disclosure), `backend-reviewer`.
**New ADR:** none (spec §*Why no new ADR*).

## Size — one PR

One coherent slice, one PR. Roughly: 5 ServiceDefaults files, 14 migration files (7 x `.cs` +
`.Designer.cs`, generated), 12 endpoint call-site edits of 1–3 lines, 12 one-line `ProducesProblem`
additions, ~8 test files. Comparable to spec 201. Splitting by issue would cost two migrations
per context and two passes over the same twelve files for one root cause; no split is proposed.

## Parallelism (ADR-0109)

- **Phase 4a:** T001–T004 and T005 own disjoint test files → `[P]`. T007/T008 are `[P]` with each other.
- **Phase 4b foundational:** **T010–T014 (ServiceDefaults) block everything else.** T011 changes
  `IdempotencyScope.For`'s signature, so the build is broken until every call site (T015–T021) is done.
- **Phase 4b fan-out:** T015–T021 are `[P]` — seven bounded contexts, disjoint files (each owns
  its `Api/` endpoint files and its `Migrations/` folder). They may be edited in parallel but
  land in the **same commit** as T011 (see §Commits).
- **One shared-file hazard:** `tests/Integration.Tests/ci-shards/shard-*.filter` — every new
  integration class needs an entry (a missing entry fails CI deterministically). T001–T004 and
  T008 each append; serialise those appends or let one task add all five.

## Commits (each must build on its own — rebase-merge, ADR-0087)

1. `test(2424): …` — T001–T006 (HTTP + architecture tests; compile against today's tree, fail on assertions).
2. `feat(2424): add the idempotency request-binding columns` — T012 + the seven migrations' files from T015–T021 (builds; behaviour-preserving — nothing reads the columns yet).
3. `fix(2424): bind an Idempotency-Key to its fab and request` — T010, T011, T013, T014, the call-site and `ProducesProblem` halves of T015–T021, and the T007/T008 tests (which only compile once their symbols exist; their red was observed in the working tree and quoted).

Commit messages: Conventional Commits, no `Co-Authored-By` (ADR-0030, ADR-0086); PR body
references `Closes #2424`, `Closes #2492` and quotes both reds.

---

## US1 (P1) — A key reused for a different request is refused, never replayed

### Phase 4a — RED (`test-writer`)

Copy request shapes and helpers from the neighbouring suites named per task; use run-unique
keys (`$"…-{Guid.CreateVersion7():N}"`) and run-unique names — the integration database
persists across runs (see `EventTypeRegistryIdempotencyIntegrationTests`' key comment).
Every 422 assertion also asserts the **title** `IDEMPOTENCY_KEY_REUSED` and a **side effect**
(nothing created / rotated), so a test cannot pass on a status alone.

**T001 [P] [US1] Identity — resource dimension (#2424).** New
`tests/Integration.Tests/Identity/IdempotencyKeyReuseIdentityIntegrationTests.cs`
(helpers from `IdempotentRegistrationIntegrationTests.cs`, `RegisteredClientConcurrencyIntegrationTests.cs`):
- `A_key_reused_to_rotate_a_different_webhook_integration_is_refused_and_discloses_no_secret` — alpha (`If-None-Match: *`, key K) → 200; beta same key → 422, response body has no `clientSecret`, beta does not exist afterwards. **Headline exploit.**
- `A_key_reused_to_rotate_the_same_integration_under_a_different_precondition_is_refused` — create alpha (`If-None-Match: *`, K) → 200; `If-Match: "<v>"` same K → 422; alpha's version unchanged.
- `A_repeated_rotation_with_the_same_key_and_request_still_replays_the_same_secret` — positive control.
- `A_key_reused_to_register_a_different_device_is_refused_and_returns_no_secret` — D1 → 201; D2 same key → 422, D2 not listed.
- `A_key_reused_to_enroll_a_different_kiosk_is_refused_and_returns_no_secret`.

**T002 [P] [US1] CameraCatalog — fab dimension (#2492).** New
`tests/Integration.Tests/CameraCatalog/IdempotencyKeyFabBindingIntegrationTests.cs`
(`op-multi@smart-sentinel-eye.test` / `op-dresden@dresden.test` as in `CameraFabResolutionIntegrationTests.cs`):
- `A_key_reused_for_the_same_camera_in_another_fab_is_refused_and_creates_nothing` — op-multi, munich body X key K → 201; dresden same body + key → 422; dresden lists no X. **Headline exploit.**
- `A_key_reused_for_a_different_camera_in_the_same_fab_is_refused` — body dimension.
- `The_fab_guard_refuses_before_a_reused_key_is_examined` — dresden-only operator reuses its key against munich → 403, not 422.
- `A_key_reused_by_another_caller_is_a_fresh_request` — unchanged caller scoping still yields 201 for a second subject (guards against over-refusal).

**T003 [P] [US1] EventIngestion.**
- **Invert** `EventTypeRegistryIdempotencyIntegrationTests.A_key_reused_for_a_different_kind_replays_the_first_registration` → rename `A_key_reused_for_a_different_kind_is_refused_and_registers_nothing`: second request → 422 `IDEMPOTENCY_KEY_REUSED`, `CountOf(rows, secondKind) == 0`. Rewrite its doc comment to cite spec 302 / #2424's decision (FR-009). **This is the one deliberate assertion change in the PR.**
- Add to the same class: `A_retry_that_names_the_fab_it_first_left_implicit_still_replays` — dresden single-fab operator registers without `fabId`, repeats with `fabId=dresden`, same body + key → 201, same identifier (proves the *resolved* fab is compared, not the query).
- New `tests/Integration.Tests/EventIngestion/IdempotencyKeyReuseEventIngestionIntegrationTests.cs`: `A_key_reused_to_declare_a_different_source_is_refused` (`POST /event-sources`; today answers 201 because of the local fold — behaviour change) and `A_key_reused_for_a_different_manual_event_is_refused` (`POST /events/manual`, munich). Restore any declared source in `finally` as `EventSourceModeIntegrationTests` does.

**T004 [P] [US1] Remaining contexts — one per endpoint, proving each call site and each context's migration.** New
`tests/Integration.Tests/ServiceDefaults/IdempotencyKeyReuseAcrossContextsIntegrationTests.cs`, one `[Fact]` each:
`POST /rules` (Automation), `POST /layouts` and `POST /walls` (LayoutComposition), `POST /overlays` (OverlayDesigner — fab-less path), `POST /system-variables` (SystemVariables). Each: first keyed create → 201; same key, one field changed → 422; listing shows the second was not created. Together with T001–T003 every one of the 12 endpoints and all 7 contexts are hit.

**T005 [P] [US1] Architecture — status declaration.** In
`tests/Architecture.Tests/StatusProducerDeclarationTests.cs`: census row
`M16 "IdempotentRequest key-reuse refusal" "422" Visibility.HandlerBody` naming a new guard
`Every_endpoint_that_runs_an_idempotent_request_declares_the_422_it_answers` (pattern:
`PreconditionDeclarationTests`; population derived from handler bodies calling
`IdempotentRequest.ExecuteAsync`/`ExecuteCreateAsync`, not listed). Red today: 12 of 12 chains lack the declaration. Prove the guard by counterfactual (remove one declaration after green → it fails naming that file).

**T006 [US1] Shard filters + run step 1.** Add T001, T002, T003's new class and T004 to a
`tests/Integration.Tests/ci-shards/shard-N.filter` (shard 4 holds the existing idempotency
suites; balance if needed). Run T001–T005 against the **unmodified** tree and record the
**verbatim** failures (expected: `201`/`200` where `422` expected; the inverted test's
identifier equality; the guard listing 12 files). Any test arriving green → stop, report.

**T007 [P] [US1] Unit — `tests/ServiceDefaults.Tests/Idempotency/`.**
- New `IdempotencyFingerprintTests.cs`: equal inputs → equal value; one body field differs → differs; one bound value differs → differs; `("ab","c")` ≠ `("a","bc")`; value is 64 lowercase hex; the same DTO bound from two JSON property orders → equal; null request guarded.
- `IdempotentRequestTests.cs`: update `Scope` (line 17) to the 5-arg `For` (mechanical, no assertion change); add `A_key_bound_to_a_different_request_is_refused_with_422_and_neither_work_nor_replay_runs` (title, status, `WorkRuns == 0`, no replay, `Released == 0`, `Completed is null`) and `A_mismatched_key_is_refused_without_waiting_out_the_in_progress_poll` (`Begins == 1`).

**T008 [P] [US1] Store — real Postgres.** New
`tests/Integration.Tests/ServiceDefaults/IdempotencyRequestBindingStoreIntegrationTests.cs`
(shape of `IdempotencyFencingIntegrationTests.cs`, against `CameraCatalogDbContext`):
- `A_completed_key_presented_with_a_different_fingerprint_is_mismatched`
- `A_completed_key_presented_with_a_different_fab_is_mismatched`
- `An_unfinished_key_presented_with_a_different_fingerprint_is_mismatched_not_in_progress`
- `A_completed_key_presented_with_the_same_binding_still_replays`
- `A_fabless_scope_does_not_match_a_row_written_with_a_fab` (and the converse)
- `A_row_written_before_the_binding_existed_is_mismatched` — **damage a system-written row** (`UPDATE … SET request_fingerprint = NULL`), never hand-insert one (spec 201 precedent).
- `A_stale_reservation_reclaimed_by_a_different_request_takes_over_its_binding`
Also update `IdempotencyFencingIntegrationTests.cs:283` to the 5-arg `For` (mechanical). Add the new class to a shard filter.

**T009 [US1] Run step 2.** Run T007/T008; record the verbatim compile errors naming the missing
members (`IdempotencyFingerprint`, `IdempotencyOutcome.Mismatched`, `For` arity). Hand both
outputs (T006, T009) to the engineer as the brief.

### Phase 4b — GREEN (`backend-engineer`) — may not edit any test from 4a

**Foundational (blocks T015–T021):**

**T010 [US1]** New `src/ServiceDefaults/Idempotency/IdempotencyFingerprint.cs` per plan §1.
**T011 [US1]** Widen `IdempotencyScope` (plan §2); add `IdempotencyOutcome.Mismatched` and
`IdempotencyReservation.Mismatched` (`IIdempotencyStore.cs`); add
`IdempotencyHeaders.ReusedErrorCode = "IDEMPOTENCY_KEY_REUSED"`.
**T012 [US1]** `IdempotencyKeyTable.AddRequestBinding` / `DropRequestBinding` (plan §3). Do **not** edit `Create`.
**T013 [US1]** `IdempotencyStore.BeginAsync` claim + fall-through per plan §4 (typed null parameter; mismatch before in-progress). `CompleteAsync`/`ReleaseAsync` untouched.
**T014 [US1]** `IdempotentRequest.ExecuteAsync` 422 branch per plan §5; update the class's doc where it enumerates outcomes.

**Per context `[P]` — migration (`dotnet ef migrations add AddIdempotencyRequestBinding`, then
`Up`/`Down` → the two helpers, mirroring `*_AddIdempotencyKey.cs`) + call sites per plan §6 +
`.ProducesProblem(StatusCodes.Status422UnprocessableEntity)` on each mapping chain:**

**T015 [P] [US1]** Automation — `RulesEndpoints.cs` (call site + chain).
**T016 [P] [US1]** CameraCatalog — `CameraEndpoints.cs`.
**T017 [P] [US1]** EventIngestion — `EventsEndpoints.Writes.cs` + `EventsEndpoints.cs` (chain); `EventSourcesEndpoints.cs` (endpoint argument back to `DeclareEndpoint`); `EventTypesEndpoints.cs`.
**T018 [P] [US1]** Identity — `DevicesEndpoints.cs`, `KiosksEndpoints.cs`, `WebhookRotationEndpoints.cs` (fingerprint includes `name` + precondition).
**T019 [P] [US1]** LayoutComposition — `LayoutEndpoints.Commands.cs` + `LayoutEndpoints.cs`; `WallEndpoints.Commands.cs` + `WallEndpoints.cs`.
**T020 [P] [US1]** OverlayDesigner — `OverlayEndpoints.Commands.cs` (`Option<string>.None`) + `OverlayEndpoints.cs`.
**T021 [P] [US1]** SystemVariables — `SystemVariableEndpoints.cs`.

**T022 [US1] Green gate.** `git diff --stat` shows a new migration pair in each of the seven
`Migrations/` folders. Stop any running AppHost first. Release build clean (analyzers,
`dotnet format --verify-no-changes`); `ServiceDefaults.Tests`, `Architecture.Tests` and the
integration shards containing every new/changed class green; coverage gate (Shared ≥ 90%,
ADR-0065) holds; T005 guard proven by counterfactual.

---

## Phase 5 — Verify (`verify`)

Run `spec.md` §*Independent end-to-end test procedure* against a booted stack; include the
`\d idempotency_key` output for at least two contexts and the 422 body (showing no secret).
Latency: N/A beyond spec §*Latency-budget impact* (keyed `POST /events/manual` only, negligible).

## Phase 6 — QA

`/security-review` (mandatory) and `/code-review`, focus per `plan.md` §*Review focus*.

## Phase 7 — PR

To `develop` (`gh pr create --base develop`), body quotes the T006 and T009 reds, `Closes #2424`,
`Closes #2492`; check both issues' state after merge.

## Follow-ups (human, not lane work)

- ADR-0142: optional dated clarification that a key binds one request (fab + fingerprint) and a mismatched reuse answers 422.
- CLAUDE.md house rule "A retried `POST` must not apply twice" → "The scope includes the authenticated caller" paragraph should also name fab and the request binding.
- `.claude/agents/security-reviewer.md` §Replay, retry and idempotency — same addition.
