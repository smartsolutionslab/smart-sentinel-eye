# Tasks: Spec 320, the client a cancel left behind

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2181 (the existing feature-level issue, already on Project #13 "Smart Sentinel Eye", checked by `projectItems` on 2026-10-09; no new issue was filed, and no per-task issues are filed). The PR uses **`Fixes #2181`**, and the issue state is checked after the merge.
**Branch:** `fix/2181-orphaned-keycloak-client-on-aborted-register`
**Phase-4a colour:** **RED** (behaviour-changing: a new background pass that disables clients). I1 and I1k must be observed failing, and I0a/I0b observed green as declared controls, with output quoted verbatim in the PR (ADR-0139, constitution §Testing).
**Engineer:** `test-writer` for 4a, then `backend-engineer` for 4b. Identity backend only. **No handler change, no AppHost, no realm, no migration, no Shared.\*, no frontend.**
**New ADR:** none (spec §6).
**For the gate reviewer:** spec §4.1 (disable, not delete) and §4.3 (the mass-disable guard) are this spec's choices inside the user's decision. Overturning either changes T003, T005 and T011 only.

`[P]` = disjoint files, may run concurrently (ADR-0109).
**Foundational:** T001 (port, repository and pass **signatures only**). Every test task names these types. After T001, T002–T007 fan out.

---

## Phase 4a: tests first (`test-writer`). Phase 4b may not edit any test file below.

- [ ] **T001 [US1] (foundational)** Declarations only, so the tests compile against the real names and fail on behaviour:
  - `src/Identity/Application/KeycloakAdmin/StampedClient.cs`: the record (plan §3).
  - `src/Identity/Application/KeycloakAdmin/IKeycloakAdminClient.cs`: `GetStampedClientsAsync`, `GetServiceAccountCreatedAtAsync`, with the plan §3 docs.
  - `src/Identity/Domain/RegisteredClient/IRegisteredClientRepository.cs`: `GetActiveClientIdsAsync`.
  - `src/Identity/Application/KeycloakAdmin/OrphanedClientSweep.cs`: the class, its constructor, `GraceWindow`, `SweptKinds`, `OrphanedClientSweepOutcome(int Examined, int Disabled, bool Refused, IReadOnlyList<string> Unreachable)`, and `SweepAsync` throwing `NotImplementedException`.
  - `src/Identity/Infrastructure/KeycloakAdmin/OrphanedClientSweepHostedService.cs`: the class, its constructor, `TickInterval`, and `RunOnceAsync`/`ExecuteAsync` throwing `NotImplementedException`. **Not registered.**
  - Production implementers (`HttpKeycloakAdminClient`, `RegisteredClientRepository`) get members that throw `NotImplementedException`, so the solution builds.
  - Contract, not behaviour, so no red of its own. **Depends on:** nothing. **Blocks:** T002–T007.

- [ ] **T002 [P] [US1]** Test doubles (plan §8.2, §8.5): `tests/Identity.Application.Tests/Fakes/FakeKeycloakAdminClient.cs` (stamped clients, created-at map, call order, per-client failure hooks) and `Fakes/InMemoryRegisteredClientRepository.cs` (`GetActiveClientIdsAsync`). Plumbing for the other implementers named in plan §8.5. **No existing assertion changes. If one seems necessary, stop and escalate.**
  - **Depends on:** T001. **Blocks:** T003.

- [ ] **T003 [US1]** `tests/Identity.Application.Tests/KeycloakAdmin/OrphanedClientSweepTests.cs`: facts U1–U12 (plan §8.2). Red: `NotImplementedException` on every fact.
  - **Depends on:** T002.

- [ ] **T004 [P] [US1]** `tests/Identity.Infrastructure.Tests/KeycloakAdmin/StampedClientQueryTests.cs` and `ServiceAccountCreatedAtTests.cs` (plan §8.3), with a stub `HttpMessageHandler` as `EnrolledKioskQueryTests` uses. Red: `NotImplementedException`.
  - **Depends on:** T001.

- [ ] **T005 [P] [US1]** `tests/Identity.Infrastructure.Tests/KeycloakAdmin/OrphanedClientSweepHostedServiceTests.cs`: H1–H3 (plan §8.3), reusing `Fakes/ManualTimeProvider.cs`, `Fakes/CapturingLogger.cs` and `Fakes/SilentKeycloakAdminClient.cs` (the never-answering Keycloak). Red: `NotImplementedException`.
  - **Depends on:** T001.

- [ ] **T006 [P] [US1]** `tests/Architecture.Tests/OrphanedClientSweepRegistrationTests.cs` (plan §8.4), with the "declaration only" doc copied in spirit from `KioskPrivilegeSweepRegistrationTests`. Red: no registration.
  - **Depends on:** T001.

- [ ] **T007 [P] [US1]** `tests/Integration.Tests/Identity/OrphanedClientSweepIntegrationTests.cs`: I0a, I0b, I1, I1k, I2, I3, I4 (plan §8.1). Real `identity-db` connection string from the fixture, a fixed `IClock` replacing the registered one, `RealmProbe` for planting and cleanup.
  - **And** add `|FullyQualifiedName~SmartSentinelEye.Integration.Tests.Identity.OrphanedClientSweepIntegrationTests.` to `tests/Integration.Tests/ci-shards/shard-1.filter` (fewest entries on 2026-10-09). A missing entry fails CI deterministically.
  - **Depends on:** T001.

- [ ] **T008 [US1]** Run and return **verbatim** output:
  - `dotnet test tests/Identity.Application.Tests tests/Identity.Infrastructure.Tests tests/Architecture.Tests`: every new fact red, on `NotImplementedException` or on the missing registration. Every pre-existing fact green.
  - Stop any running AppHost (one stack per machine), then `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~OrphanedClientSweepIntegrationTests"`. Quote **I0a green (409 `DEVICE_ALREADY_REGISTERED`)** and **I0b green (`createdTimestamp` present)** as the declared controls, and **I1/I1k red**.
  - **If I0b is red, stop and escalate**: plan §3's premise is false on Keycloak 26.6.4, and the fallback changes the handlers, which needs the gate.
  - Any red not listed here is an escalation, not a step.
  - **Depends on:** T002–T007.

## Phase 4b: implementation (`backend-engineer`). No test file in the diff after T008.

- [ ] **T009 [P] [US1]** `src/Identity/Infrastructure/KeycloakAdmin/HttpKeycloakAdminClient.cs`: `GetStampedClientsAsync`, `GetServiceAccountCreatedAtAsync`, `ClientDetailRow.Enabled`, `ServiceAccountUser.CreatedTimestamp` (plan §3). Rewrite `TryDeleteClientAsync`'s doc: a surviving half-made client is now disabled by `OrphanedClientSweep` after the grace window, so "Nothing else will" is no longer true. Makes T004 green.
  - **Depends on:** T008.

- [ ] **T010 [P] [US1]** `src/Identity/Infrastructure/Persistence/RegisteredClientRepository.cs`: `GetActiveClientIdsAsync` (plan §3).
  - **Depends on:** T008.

- [ ] **T011 [P] [US1]** `src/Identity/Application/KeycloakAdmin/OrphanedClientSweep.cs`: `SweepAsync` per plan §4. Add the four `[LoggerMessage]`s to `src/Identity/Application/Log.cs`. In `KioskPrivilegeSweep.cs`'s doc, name the new sweep where it says what it leaves behind. Makes T003 green.
  - **Depends on:** T008.

- [ ] **T012 [US1]** `src/Identity/Infrastructure/KeycloakAdmin/OrphanedClientSweepHostedService.cs` per plan §5. Add `OrphanedClientSweepFailed` to `src/Identity/Infrastructure/Log.cs`. Register `AddScoped<OrphanedClientSweep>()` and `AddHostedService<OrphanedClientSweepHostedService>()` in `IdentityInfrastructureModule.cs`, next to the kiosk sweep's lines. Makes T005 and T006 green.
  - **Depends on:** T009–T011 (the registration resolves them; `Infrastructure/Log.cs` is touched only here).

- [ ] **T013 [US1]** Gates on the touched projects: `dotnet build -c Release` (`TreatWarningsAsErrors`); `dotnet format --verify-no-changes`; `dotnet test tests/Identity.Application.Tests tests/Identity.Infrastructure.Tests tests/Architecture.Tests tests/MigrationRunner.Tests`, all green with **no test file in the diff since T008**. Identity coverage gates: Application ≥ 80%. Run `coverage-check.ps1` under PS 5.1 from a scratch copy if `pwsh` is absent.
  - **Depends on:** T012.

- [ ] **T014 [US1]** `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~OrphanedClientSweepIntegrationTests|FullyQualifiedName~KioskPrivilegeSweepStartupIntegrationTests|FullyQualifiedName~DisabledClientReregistration|FullyQualifiedName~Identity.Device|FullyQualifiedName~Identity.Kiosk"`: the new class green. Existing Identity registration, enrolment and sweep classes pass **unmodified**. Return verbatim output.
  - **Depends on:** T013.

## Phase 5: verify (`/verify`)

- [ ] **T015 [US1]** Run spec §9 steps 1–8 against a live stack, including the **10-minute wait** (step 5) and the restart-driven startup pass, which no automated test covers end to end. Write the verification note on the PR: observed statuses, the Warning and Information lines verbatim, the Keycloak `enabled` readings, and the trace from step 8. Latency: N/A (spec §7).
  - **Depends on:** T014.

## At the gate (orchestrator, not an engineer task)

- **File the webhook follow-up** (spec §5): `RotateWebhookClientCommandHandler`'s register branch has the same create-then-save hole, and is deliberately outside this sweep. Add it to Project #13.
- Relay spec §4.1 (disable, not delete) and §4.3 (the mass-disable guard) to the human as this spec's own choices.
- Re-check the spec number (320) against `origin/develop` and all open branches before opening the PR.

## Dependency summary

```
T001 ──┬─ T002 ── T003 ─┐
       ├─ T004 [P] ─────┤
       ├─ T005 [P] ─────┼─ T008 ──┬─ T009 [P] ─┐
       ├─ T006 [P] ─────┤         ├─ T010 [P] ─┼─ T012 ── T013 ── T014 ── T015
       └─ T007 [P] ─────┘         └─ T011 [P] ─┘
```

Fan-out after T001: T002→T003 (Application tests), T004+T005 (Infrastructure tests; separate
files), T006 (architecture) and T007 (integration) own disjoint files. After T008: T009
(Keycloak adapter), T010 (repository) and T011 (pass + Application `Log.cs`) own disjoint
files. T012 owns the hosted service, Infrastructure `Log.cs` and the module.
