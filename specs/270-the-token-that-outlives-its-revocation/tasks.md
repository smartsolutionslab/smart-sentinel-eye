# Tasks: Spec 270, the token that outlives its revocation

**Spec:** `spec.md` · **Plan:** `plan.md` · **ADR:** ADR-0160 · **Issue:** #2241 · **Branch:** `fix/2241-request-time-disabledat-check`
**Phase-4a colour:** **RED** (behaviour-changing). Every new-behaviour test below must be observed failing before 4b, and T009's output quoted verbatim in the PR (ADR-0139, constitution §Testing).
**Engineers:** `test-writer` (4a), then `backend-engineer` (ServiceDefaults, Identity, StreamDistribution) and `infra-engineer` (realm, AppHost) for 4b.

`[P]` = disjoint files, may run concurrently (ADR-0109). `[Found]` = foundational, blocks both stories. `[US1]` = WHEP. `[US2]` = the nine REST APIs.

**Board.** The feature issue is #2241. It is already on Project #13, status *In Progress*
(item `PVTI_lADOC-sCX84BYuvOzg6XMow`, verified 2026-09-27 by `content.url`). No per-task
issues (CLAUDE.md, Phase 3).

**Contention files** (one writer at a time): `src/ServiceDefaults/Log.cs`,
`src/AppHost/Realms/smart-sentinel-eye-realm.json`, `src/AppHost/AppHost.cs`,
`tests/Integration.Tests/ci-shards/shard-3.filter`.

**Fan-out after the foundation.** Once T010 (the ServiceDefaults core) lands,
T015 (US2, `AuthenticationDefaults.cs`) and T016 (US1, `WhepAuthValidator.cs`) own
disjoint files and run in parallel. T011 (Identity) and T012/T014 (realm, AppHost)
are independent of T010 and of each other's files.

---

## Phase 4a: tests first (`test-writer`). Phase 4b may not edit any test file below.

- [ ] **T001 [Found]** `src/Shared.Contracts/Identity/RevokedClientsResponse.cs`: `sealed record RevokedClientsResponse(IReadOnlyList<RevokedClientEntry> Clients)` and `sealed record RevokedClientEntry(string ClientId, DateTimeOffset DisabledAt)`, namespace `SmartSentinelEye.Shared.Contracts.Identity`, XML summaries in the style of the siblings in that folder (plan §4.1). This is a contract, not behaviour, so it has no red of its own. The test-writer creates it because the tests compile against it.
  - **Depends on:** nothing. **Blocks:** T002–T008, T010, T011, T013.

- [ ] **T002 [P] [Found]** `tests/ServiceDefaults.Tests/Revocation/RevokedClientSnapshotTests.cs`: the plan §2 truth table against `RevokedClientSnapshot.Refuses(string? azp, DateTimeOffset? issuedAt)`:
  - listed + `iat` before `DisabledAt` → refused
  - listed + `iat` = `DisabledAt` + 4 min 59 s → refused (tolerance)
  - listed + `iat` = `DisabledAt` + 5 min 1 s → admitted (re-registration)
  - unlisted → admitted
  - `azp` null → admitted
  - listed + `iat` null → refused
  - two entries for one id, the later wins
  - client-id match is ordinal and case-sensitive

  Red: compile.
  - **Depends on:** T001.

- [ ] **T003 [P] [Found]** `tests/ServiceDefaults.Tests/Revocation/RevokedClientRefresherTests.cs`: drive `RefreshOnceAsync` directly, with a hand-written `IRevokedClientSource` fake and a hand-written `TimeProvider` (the `AdvanceableClock` shape at `tests/ServiceDefaults.Tests/ClientCredentialsTokenProviderTests.cs:183`; ADR-0054, no `FakeTimeProvider`). Cover:
  - the registry admits everything before the first load
  - a successful load swaps the snapshot
  - a throwing source keeps the previous snapshot
  - `RevokedClientSnapshotHealthCheck` is `Degraded` before the first load, `Healthy` after it, and `Degraded` again once 30 s pass without a success
  - one Warning on the first failure and none on the second, one Information on recovery (capture with a recording `ILogger`)
  - an `OperationCanceledException` from the source propagates and does not count as a failure

  Red: compile.
  - **Depends on:** T001.

- [ ] **T004 [P] [US2]** `tests/ServiceDefaults.Tests/Revocation/BearerRevocationHookTests.cs`: build a host with `AddBearerAuthentication()` (the way `tests/ServiceDefaults.Tests/BearerAudienceTests.cs` does), replace `IRevokedClientRegistry` with a stub, resolve `JwtBearerOptions`, and invoke `Events.OnTokenValidated` with a constructed `TokenValidatedContext` carrying a `JsonWebToken`. Cover:
  - a refused `azp` → `context.Result` is a failure
  - an admitted `azp` → no result set
  - a pre-existing `OnTokenValidated` configured via `Configure<JwtBearerOptions>` **before** `AddBearerAuthentication` still runs
  - a later `Configure<JwtBearerOptions>` that does `Events ??= new(); Events.OnMessageReceived = …` (LayoutComposition's exact shape, `src/LayoutComposition/Api/Program.cs:18-22`) leaves `OnTokenValidated` wired

  Red: assertion (the hook is absent) or compile (the registry type is absent).
  - **Depends on:** T001.

- [ ] **T005 [P] [US1]** `tests/StreamDistribution.Infrastructure.Tests/Auth/WhepRevocationTests.cs`: through the internal constructor seam (#2099) with in-memory OIDC metadata, following the construction used in the sibling files in that folder (`WhepAuthValidatorResolutionTests.cs`, `WhepAudienceTests.cs`). Sign tokens with the in-memory key, including `azp` and `iat`. Cover:
  - a stub registry refusing the `azp` → `WhepAuthFailure.TokenRejected`
  - a registry admitting it → success with the same subject and scopes as today
  - a revocation refusal triggers **no** `RequestRefresh` on the metadata source (a counting fake)

  Red: compile (the constructor lacks the registry).
  - **Depends on:** T001.

- [ ] **T006 [P] [Found]** `tests/Identity.Application.Tests/Queries/ListRevokedClientsQueryHandlerTests.cs`: over the in-memory `IRegisteredClientQuerySource` fake the sibling list-handler tests use. Cover:
  - active rows excluded
  - disabled kiosk, device and webhook rows all included
  - rows from two fabs both included
  - the same client id disabled twice (disable → re-register → disable) yields one entry with the later `DisabledAt`

  Red: compile.
  - **Depends on:** T001.

- [ ] **T007 [P] [US1] [US2]** `tests/Integration.Tests/Identity/RevokedTokenRefusedIntegrationTests.cs` (Aspire fixture, ADR-0103; `[Collection]`/`[Trait]` per `IntegrationTestSelectionTests`). Enrol **two** kiosks, a subject and a **control**, via Identity. Mint T1 and Tc with `client_credentials` from `aspire.CreateKeycloakClient()`. Then:
  - **Controls first:** T1 and Tc get 200 on `GET /cameras` (CameraCatalog, direct) and on `POST /streams/authorize` for a camera provisioned in the kiosks' fab.
  - `DELETE /kiosks/{subject}`.
  - Poll 500 ms / 20 s: T1 gets 401 on both calls, and Tc still gets 200 on both at the moment T1 flips. The REST 401 carries `WWW-Authenticate` containing `invalid_token`.
  - Separate fact: `GET /registered-clients/revoked` on Identity answers 401 with no token and 403 with an operator token lacking the scope. With a `revocation-list-reader` token it answers 200 and lists the subject with a `DisabledAt`.

  Use the 20 s / 500 ms idiom from `CrossFabWebhookRotationEffectIntegrationTests.cs` and the enrolment helpers already used by `CrossFabDisableIntegrationTests`.
  - **And** add `|FullyQualifiedName~SmartSentinelEye.Integration.Tests.Identity.RevokedTokenRefusedIntegrationTests.` to `tests/Integration.Tests/ci-shards/shard-3.filter`. A missing entry fails deterministically in CI (memory).
  - **Depends on:** T001.

- [ ] **T008 [P] [Found]** `tests/Integration.Tests/Identity/RealmImportMirrorTests.ProductionDefaults.cs`: add the row pairing the AppHost default `RevocationListReaderClientSecret` = `dev-only-revocation-list-reader-secret` with realm client `revocation-list-reader` (the existing P1–P5 row shape). Red: `SecretForClient` throws "no such client".
  - **Depends on:** nothing.

- [ ] **T009** Run the suites and return **verbatim** output:
  - `dotnet test tests/ServiceDefaults.Tests tests/StreamDistribution.Infrastructure.Tests tests/Identity.Application.Tests`: expect a build failure naming the missing `RevokedClientSnapshot`, `IRevokedClientRegistry`, `IRevokedClientSource`, `RevokedClientRefresher`, `RevokedClientSnapshotHealthCheck`, `ListRevokedClientsQuery*`, and the `WhepAuthValidator` constructor arity. That is the compile red.
  - `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~RevokedTokenRefusedIntegrationTests|FullyQualifiedName~RealmImportMirrorTests"`. Stop any running AppHost first: **one stack per machine**. Expected:
    - **the load-bearing red:** T1 still 200 at the 20 s ceiling, with the controls green before it
    - the revocation-list fact 404
    - the mirror row throwing
  - Any other red is an escalation, not a step.
  - **Depends on:** T002–T008.

## Phase 4b: implementation. No test file in the diff after T009.

### Foundation

- [ ] **T010 [Found]** ServiceDefaults core, new `src/ServiceDefaults/Revocation/`: `RevokedClientSnapshot` (FrozenDictionary + the §2 rule + `ClockTolerance`), `IRevokedClientSource`, `IRevokedClientRegistry` / `RevokedClientRegistry` (volatile `Option<RevokedClientSnapshot>`), `RevokedClientRefresher` (`PeriodicTimer(5 s)` over `RefreshOnceAsync`, transition logging via `Interlocked.Exchange`), and `RevokedClientSnapshotHealthCheck` (Degraded, tag `ready`, ADR-0154). Edit `src/ServiceDefaults/Log.cs` to add the four `[LoggerMessage]`s (plan §4.3). Edit `SmartSentinelEye.ServiceDefaults.csproj` to add the `Shared.Contracts` `ProjectReference`. Greens T002 and T003.
  - **Depends on:** T009. **Blocks:** T013, T015, T016.

- [ ] **T011 [P] [Found]** Identity (backend-engineer):
  - `Application/Queries/ListRevokedClientsQuery.cs` and `Queries/Handlers/ListRevokedClientsQueryHandler.cs` over `IRegisteredClientQuerySource` (plan §4.2; no `.Value` inside the expression tree, per `DisabledAt.cs:42`)
  - `Infrastructure/Revocation/LocalRevokedClientSource.cs` (singleton, resolves the handler per call from a scope)
  - `Infrastructure/IdentityInfrastructureModule.cs`: register the handler, and `Replace` `IRevokedClientSource` with the local source
  - `Api/RevocationEndpoints.cs`: `GET /registered-clients/revoked`, `RequireAuthorization(Scope.Sse.Identity.Revocations.Read)`, no fab resolution, and a summary that states why
  - `Api/Program.cs`: map it

  Greens T006.
  - **Depends on:** T009, T012 (the scope constant). Does not depend on T010 at compile time except for `IRevokedClientSource`. **Sequence it after T010** if one engineer holds both, or take the interface from T010 first.

- [ ] **T012 [P] [Found]** Scope and realm (infra-engineer):
  - `src/ServiceDefaults/Authorization/Scope.cs`: `Sse.Identity.Revocations.Read = "sse.identity.revocations.read"`, plus its `Scope.All` entry
  - `src/AppHost/Realms/smart-sentinel-eye-realm.json`: the client scope, shaped like its siblings, and the client `revocation-list-reader`. The client is confidential and service-accounts-only, with `secret: "dev-only-revocation-list-reader-secret"` and default scopes `sse-audience` + `sse.identity.revocations.read`. It gets **no** `sse-groups` and belongs to no fab group. Its **`description` stays ≤ 255 chars**; a longer one kills the import (memory).

  Keeps `ScopeGrantTests`, `RealmIdentityTests` and `EndpointScopeDeclarationTests` green. **Delete the local Keycloak volume** before any local boot (memory).
  - **Depends on:** T009.

- [ ] **T013 [Found]** ServiceDefaults HTTP source, `src/ServiceDefaults/Revocation/`: `HttpRevokedClientSource` (typed client, base `https+http://identity`, reads `RevokedClientsResponse`), `RevocationListOptions` (`ClientId`, `ClientSecret`), and `RevocationListTokenProvider` + `RevocationListAuthorizationHandler` mirroring `src/StreamDistribution/Infrastructure/Attribution/CameraCatalog{TokenProvider,AuthorizationHandler}.cs` over `ClientCredentialsTokenProvider` / `AuthorizingHandler`. The token `HttpClient` gets `.RetryEveryMethod()` with the justification comment the five existing mints carry (ADR-0143).
  - **Depends on:** T010 (same project; `Log.cs` contention).

- [ ] **T014 [P] [Found]** AppHost (infra-engineer), `src/AppHost/AppHost.cs`:
  - `AddOverridableParameter("RevocationListReaderClientSecret", "dev-only-revocation-list-reader-secret", secret: true)`.
  - For each of the eight non-Identity APIs (camera-catalog, stream-distribution, layout-composition, event-ingestion, overlay-designer, system-variables, audit-observability, automation):
    - `.WithReference(identity)`, with **no `WaitFor`**; comment the reason (ADR-0160 §3, and the existing "must not gate host start" precedent at `:378`)
    - `RevocationList__ClientId` / `RevocationList__ClientSecret` environment
  - `identity` is declared at `:505`, so apply the edits after it. Use one small local helper rather than eight copies.

  Greens T008. Keeps `AppHostReplicaCountTests` and the other AppHost tests green.
  - **Depends on:** T012 (client id and secret must match).

### US2: the nine REST APIs

- [ ] **T015 [P] [US2]** `src/ServiceDefaults/AuthenticationDefaults.cs`, in `AddBearerAuthentication`:
  - `TryAdd` the registry, the refresher (hosted), the health check and the HTTP source with its typed clients and options (T013).
  - Inside `AddJwtBearer(options => …)`, create `options.Events` if null and set `OnTokenValidated` to chain any existing delegate first. It then reads `azp` from `context.Principal` and `IssuedAt` from `context.SecurityToken` (`JsonWebToken`; `DateTime.MinValue` means absent), logs, and calls `context.Fail(...)` when the registry refuses.
  - Greens T004. Also greens the REST half of T007 once T011, T012 and T014 are in.
  - **Depends on:** T010, T013.

### US1: WHEP

- [ ] **T016 [P] [US1]** `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs`:
  - Both constructors take `IRevokedClientRegistry` (`Ensure.That`). The internal seam stays `internal`.
  - After `ValidateToken(..., out SecurityToken validated)` and the `sub` check, it refuses with `TokenRejected` when the registry refuses the `azp` / `JwtSecurityToken.IssuedAt`, with **no** `RequestRefresh`.
  - `IWhepAuthValidator`, `AuthorizeWhepCommandHandler` and `StreamEndpoints` stay untouched.
  - Update `tests/StreamDistribution.Application.Tests/Fakes/FakeWhepAuthValidator.cs` **only if** the build requires it. It implements the interface, which does not change, so it should not.

  Greens T005 and the WHEP half of T007.
  - **Depends on:** T010.

### Close-out

- [ ] **T017** Full check, **per commit** (rebase-merge lands them individually):
  - `dotnet build -c Release` (analyzers, collection expressions, S-rules)
  - `dotnet format --verify-no-changes`
  - all unit projects touched, then T009's integration filter, now green
  - `NFR001_JwtValidationLatencyTests` green and unmodified
  - coverage gates (ADR-0065)
  - **Depends on:** T010–T016.

## Phase 5: verify (`/verify`)

- [ ] **T018** Run spec §7's procedure on a fresh stack (Keycloak volume deleted). Record in the PR's verification note:
  - the observed disable → 401 delay on CameraCatalog **and** on `/streams/authorize`, twice (memory: *measurement runs need repeating*)
  - the `revocation-snapshot` health transitions with Identity stopped and restarted
  - that an operator session is unaffected

  §IV: state **N/A** with the reason (spec §6).
  - **Depends on:** T017.

## Phase 6: QA

- [ ] **T019** `/code-review`, and **`/security-review`**, which is mandatory because this is an auth change at every trust boundary. Also `infra-reviewer` for the realm and AppHost, and `security-reviewer` with the scope catalogue in hand. Specific asks:
  - the fail-open-before-first-load choice
  - the shared service account
  - that `revocation-list-reader` cannot reach anything but the list
  - that no endpoint now answers differently for "revoked" vs "expired"
  - **Depends on:** T018.

## Phase 7: PR

- [ ] **T020** `gh pr create --base develop`. The body:
  - quotes T009's verbatim red
  - cites ADR-0160 and the product-owner comment of 2026-09-26
  - says **"Closes #2241"**: the webhook half was closed by PR #2630, so both halves are then delivered; check the state after merge (memory)
  - re-checks that spec number 270 and ADR number 0160 are still free on `origin/develop` and on open PRs first
  - **Depends on:** T019.

- [ ] **T021** File the follow-up the spec defers: *"A revoked client's established WebRTC session and SignalR connection outlive the revocation"*. Reference ADR-0160's Consequences. Add it to Project #13 (`gh project item-add 13 --owner smartsolutionslab --url <url>`). Do **not** label it `agent:ready`; it needs a decision first.
  - **Depends on:** T020.

- [ ] **T022** File the second follow-up phase-6 security review found: a wrong (not missing) `RevocationList__ClientSecret` produces the same Degraded-forever `revocation-snapshot` health check as an unreachable Identity, with no separate signal that it will never self-recover. Reference ADR-0160's Consequences. Add it to Project #13. Do **not** label it `agent:ready`; it needs a decision on the health-check contract (whether "never loaded after N minutes" should become Unhealthy) before implementation.
  - **Depends on:** T020.
