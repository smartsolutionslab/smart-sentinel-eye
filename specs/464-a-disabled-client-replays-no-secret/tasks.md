# Tasks 464 — A disabled client replays no secret

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3123 (feature-level issue; must be on Project #13).
Engineer: **backend**. Phase 4a colour: **behaviour-changing → red first**. No foundational task;
everything is Identity-internal. One story, so the task chain is mostly sequential.

## User story 1 (P1) — a replay never returns a disabled client's secret

- [ ] **T001 [US1]** (test-writer) Extend `tests/Identity.Application.Tests/Queries/ReplayRegisteredClientQueryHandlerTests.cs`:
  unbound replay of a disabled client → `REPLAYED_CLIENT_DISABLED`, Keycloak not read; bound replay
  (`AnsweredAt` = current version) of a disabled client → same, Keycloak not read; refusal message names
  no client id, fab or secret. Run; quote the **red** output (compile failure on the missing
  `ReplayedClientDisabled` is acceptable only if the assertion-level red is also captured after T003's
  record lands — prefer asserting on `error.Code` strings so the test compiles and fails on behaviour).
- [ ] **T002 [P] [US1]** (test-writer) New `tests/Architecture.Tests/ReplayOfDisabledClientEndpointTests.cs`
  via `IdentityEndpointHost`: rotate replay of a disabled held client → 409 titled `REPLAYED_CLIENT_DISABLED`
  and `ReadClientSecretAsync` never invoked; device register replay → 409 same title. Run; quote red.
  (Disjoint file from T001.)
- [ ] **T003 [US1]** (backend) `src/Identity/Application/Queries/ReplayRegistrationErrors.cs`: add
  `ReplayedClientDisabled(Guid Client)` — `REPLAYED_CLIENT_DISABLED`, `HttpStatusCode.Conflict`, message
  naming no client/version/fab/secret. Depends on T001, T002 (red observed).
- [ ] **T004 [US1]** (backend) `src/Identity/Application/Queries/Handlers/ReplayRegisteredClientQueryHandler.cs`:
  refuse with `ReplayedClientDisabled` when `registered.DisabledAt is not null`, after line 58 and before
  the version check; add one `<para>` to the class summary. Depends on T003. Tests T001/T002 must pass
  **unmodified**.
- [ ] **T005 [US1]** (backend) Run `Identity.Application.Tests`, `Architecture.Tests`, and confirm the
  untouched suites stay green unmodified: `ReplayRegisteredClientQueryHandlerSupersededTests`,
  `WebhookRotationReplaySupersededEndpointTests`, `DeviceAndKioskEndpointCharacterisationTests`,
  rotate-handler tests. `dotnet format --verify-no-changes` on touched projects; Release build clean. Depends on T004.

## Dependencies

T001 ‖ T002 → T003 → T004 → T005.

## Not tasks (explicitly)

- No change to `RotateWebhookClientCommandHandler`, the endpoints, the idempotency completion,
  `IRegisteredClientRepository`, Shared.Contracts, migrations or frontend.
