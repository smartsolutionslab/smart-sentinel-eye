# Tasks: Spec 318, the rotate that forgot the revoke

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2628 (the PR uses **`Fixes #2628`**, and the issue state is checked after the merge) · **Branch:** `fix/2628-rotate-checks-revocation`
**Phase-4a colour:** **RED** (behaviour-changing). I2–I4 (T004) must be observed failing against the live stack, and their output quoted verbatim in the PR (ADR-0139, constitution §Testing).
**Engineer:** `test-writer` for 4a, then `backend-engineer` for 4b. Backend only, plus one AppHost line (T009). **No EventIngestion change, no frontend change, no realm change, no migration.**
**New ADR:** none (spec §5). A non-blocking recommendation for the human to codify the bounded-exception pattern is recorded there. The lane does not act on it.

`[P]` = disjoint files, may run concurrently (ADR-0109).
**Foundational:** T001 (the port + enum + the two error variants, as **signatures only**). Every test and implementation task names these types. Nothing in `Shared.Kernel` or `Shared.Contracts` changes. AppHost (T009) blocks only the integration run in T010 and T014, not the unit work.

---

## Phase 4a: tests first (`test-writer`). Phase 4b may not edit any test file below.

- [ ] **T001 [US1] (foundational)** Declarations only, so the tests compile against the real names and fail on behaviour, not on missing types:
  - `src/Identity/Application/WebhookIntegrations/WebhookIntegrationStatus.cs`: the enum (plan §4).
  - `src/Identity/Application/WebhookIntegrations/IWebhookIntegrationStatusLookup.cs`: the interface (plan §4).
  - `src/Identity/Application/Commands/RotateWebhookClientCommand.cs`: add `WebhookIntegrationRevoked` and `WebhookIntegrationStatusUnavailable` variants and their `RotateWebhookClientFailures` factories, with codes, statuses and messages exactly as in plan §4.
  - **Not** the handler change and **not** the adapter. Those are 4b.
  - This is contract, not behaviour, so it has no red of its own. **Depends on:** nothing. **Blocks:** T002–T008.

- [ ] **T002 [P] [US1]** `tests/Identity.Application.Tests/Fakes/FakeWebhookIntegrationStatusLookup.cs` (plan §8.3), then **constructor plumbing** at the 10 existing `RotateWebhookClientCommandHandler` construction sites: `Commands/RotateWebhookClientCommandHandlerTests.cs` (8), `Commands/StaleVersionRejectionTests.cs:182`, and `KeycloakAdmin/RuntimeClientAudienceTests.cs:196`. Add `new FakeWebhookIntegrationStatusLookup()` in the position plan §4 fixes (after `keycloak`). **No assertion changes. If one seems necessary, stop and escalate.**
  Red: compile (the handler has no such parameter yet).
  - **Depends on:** T001. **Blocks:** T003 (same file).

- [ ] **T003 [US1]** `tests/Identity.Application.Tests/Commands/RotateWebhookClientCommandHandlerTests.cs`: add facts H1–H7 and the error-shape fact (plan §8.3). Red: compile, then behaviour (the handler ignores the lookup).
  - **Depends on:** T002 (same file, so not `[P]` with it).

- [ ] **T004 [P] [US1]** `tests/Integration.Tests/Identity/RotateRevokedWebhookIntegrationIntegrationTests.cs`: facts I1–I4 (plan §8.1). I3 and I4 carry the spec 264 disable as a **control** (poll until `enabled=false`) *before* the rotation under test, so the closing assertions can only fail because of the rotation. Reuse `RealmProbe`, `aspire.CreateAdminClientAsync("event-ingestion" | "identity")`, and the register/rotate/revoke helpers' shape from `WebhookRevocationDisablesClientIntegrationTests.cs`.
  - **And** add `|FullyQualifiedName~SmartSentinelEye.Integration.Tests.Identity.RotateRevokedWebhookIntegrationIntegrationTests.` to `tests/Integration.Tests/ci-shards/shard-3.filter`. A missing entry fails CI deterministically.
  - **Depends on:** T001 (build order only; it uses HTTP).

- [ ] **T005 [P] [US1]** `tests/Identity.Infrastructure.Tests/WebhookIntegrations/EventIngestionWebhookIntegrationStatusLookupTests.cs`: facts A1–A9 (plan §8.2), against a stub `HttpMessageHandler`. The adapter's class name and constructor (`HttpClient`, `ILogger<…>`) are as in plan §2 and §4. Red: compile.
  - **Depends on:** T001.

- [ ] **T006 [P] [US1]** `tests/Identity.Infrastructure.Tests/WebhookIntegrations/CallerTokenForwardingHandlerTests.cs`: the two facts in plan §8.2. Red: compile.
  - **Depends on:** T001.

- [ ] **T007 [P] [US1]** `tests/Architecture.Tests/ConcurrencyConflictDeclarationTests.cs:375-377`: extend the rotate route's mechanism text (plan §8.4). Re-measure the class doc's refusal count by the three methods it names. Change a figure only if the measurement moves it, and say which way in the 4a report.
  - **Depends on:** T001.

- [ ] **T008 [US1]** Run and return **verbatim** output:
  - `dotnet test tests/Identity.Application.Tests tests/Identity.Infrastructure.Tests`: expect a **build failure** naming the handler's missing constructor parameter, `EventIngestionWebhookIntegrationStatusLookup` and `CallerTokenForwardingHandler` (Identity namespace). That is the compile red.
  - Stop any running AppHost (one stack per machine), then
    `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~RotateRevokedWebhookIntegrationIntegrationTests"`.
    Quote **I2 red on 200**, **I3 red on 200 (new Keycloak id, enabled)** and **I4 red on 412 `WEBHOOK_CLIENT_NOT_FOUND`**, with **I1 green** as the declared control. This is the load-bearing red. If I3's control (`enabled=false` after revoke) itself fails, that is spec 264's regression: escalate, do not proceed.
  - `dotnet test tests/Architecture.Tests`: expect green (T001 adds no cross-context reference).
  - Any red not listed here is an escalation, not a step.
  - **Depends on:** T002–T007.

## Phase 4b: implementation (`backend-engineer`). No test file in the diff after T008.

- [ ] **T009 [P] [US1]** `src/AppHost/AppHost.cs`: on `identity` (`:511-519`), add `.WithReference(eventIngestion)` with a short why-comment (plan §7). **Not `WaitFor`.** Boot the stack once and confirm that Identity resolves `http://event-ingestion` and the AppHost accepts the mutual reference. If it does not, apply the plan §7 fallback and record which shipped in the PR body.
  - **Depends on:** T008. `[P]` with T010–T012 (disjoint files).

- [ ] **T010 [P] [US1]** `src/Identity/Infrastructure/WebhookIntegrations/CallerTokenForwardingHandler.cs`: copy LayoutComposition's (plan §3), with the namespace changed and the XML doc kept, amended to name this second use. Makes T006 green.
  - **Depends on:** T008.

- [ ] **T011 [US1]** `src/Identity/Infrastructure/WebhookIntegrations/EventIngestionWebhookIntegrationStatusLookup.cs` (plan §2 mapping table) + its Warning `[LoggerMessage]` in `src/Identity/Infrastructure/Log.cs` + the registrations in `src/Identity/Infrastructure/IdentityInfrastructureModule.cs` (`AddHttpContextAccessor`, `AddTransient<CallerTokenForwardingHandler>`, typed `AddHttpClient<IWebhookIntegrationStatusLookup, …>` with `http://event-ingestion` and the S1075/S5332 pragma pair, as plan §2). **No `RetryEveryMethod()`.** It is a `GET` (ADR-0143). Makes T005 green.
  - **Depends on:** T010 (registers its type).

- [ ] **T012 [P] [US1]** `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`: the new constructor parameter and the check block at the position plan §4 fixes (after the `ClientId` parse, before `GetWithinFabAsync`, outside the try/catch). Add the two `[LoggerMessage]`s in `src/Identity/Application/Log.cs`. Update the class `<summary>` with one sentence on the precondition. Makes T003 green.
  - **Depends on:** T008. `[P]` with T010/T011 (Application vs Infrastructure files). DI resolution of the new parameter needs T011 only at runtime, which is T014.

- [ ] **T013 [US1]** Gates, on the touched projects: `dotnet build -c Release` (`TreatWarningsAsErrors`); `dotnet format --verify-no-changes`; `dotnet test tests/Identity.Application.Tests tests/Identity.Infrastructure.Tests tests/Architecture.Tests`, all green with **no test file in the diff since T008**. Identity coverage gates: Application ≥ 80%. Run `coverage-check.ps1` under PS 5.1 from a scratch copy if `pwsh` is absent.
  - **Depends on:** T009–T012.

- [ ] **T014 [US1]** `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~RotateRevokedWebhookIntegrationIntegrationTests|FullyQualifiedName~WebhookRevocationDisablesClientIntegrationTests|FullyQualifiedName~CrossFabWebhookRotation|FullyQualifiedName~IdempotencyKeyReuseIdentityIntegrationTests|FullyQualifiedName~RegisteredClientConcurrencyIntegrationTests"`: the new class is green, and every existing rotation class passes **unmodified** (spec §7, SC3). Return verbatim output.
  - **Depends on:** T013.

## Phase 5: verify (`/verify`)

- [ ] **T015 [US1]** Run spec §8 steps 1–6 against a live stack, including **step 5 (fail closed with `event-ingestion` stopped)**, which no automated test covers end to end. Write the verification note on the PR with observed statuses, codes, the Keycloak `id`/`enabled` readings, and the outbound span from step 6. Latency: N/A (spec §6).
  - **Depends on:** T014.

## At the gate (orchestrator, not an engineer task)

- File the **"rotation of a never-registered name"** follow-up (spec §3) if the reviewer wants it.
- Relay the **ADR recommendation** (spec §5) to the human. Do not draft the ADR.
- Re-check the spec number (318) against `origin/develop` and all open branches before opening the PR.

## Dependency summary

```
T001 ──┬─ T002 ── T003 ─┐
       ├─ T004 [P] ─────┤
       ├─ T005 [P] ─────┼─ T008 ──┬─ T009 [P] ─────────────┐
       ├─ T006 [P] ─────┤         ├─ T010 [P] ── T011 ─────┼─ T013 ── T014 ── T015
       └─ T007 [P] ─────┘         └─ T012 [P] ─────────────┘
```

Fan-out after T001: T002/T003 (Application tests), T004 (integration), T005+T006
(Infrastructure tests) and T007 (architecture) own disjoint files. After T008: T009
(AppHost), T010→T011 (Infrastructure) and T012 (Application) own disjoint files.
