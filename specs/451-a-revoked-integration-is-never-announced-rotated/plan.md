# Plan 451: A revoked integration is never announced rotated

**Spec:** [spec.md](spec.md) · **Issue:** #3125 · **Context:** Identity (Application layer only)

## Constitution / ADR check

- §II primitives: no domain model touched. §Testing: behaviour-changing → red first (ADR-0139/0144).
- No cross-context reference added; Identity still reads EventIngestion only through
  `IWebhookIntegrationStatusLookup` (spec 318's bounded exception). `Shared.Contracts` untouched.
- ADR-0185: no new message type; `WebhookClientRaceDisableFailed` and `WebhookIntegrationRotatedV1` stay declared.
- ADR-0049: the re-check runs on the existing `CompensationBound` token, never the request token.
- ADR-0084 metrics are advisory; the file is already over 300 LOC. Do not grow parameter counts — see §2.

## Design

### 1. Run the re-check on both branches

`RotateWebhookClientCommandHandler.HandleAsync` (`:76-81`): drop the `!existing.HasValue &&` gate so
`DisableIfRevokedSinceCommitAsync` runs after every successful `RotateOrRegisterAsync`. Placement after the secret
roll is deliberate: it is the latest point before the publish, so the residual window is smallest. The aggregate is
the tracked instance from `GetWithinFabAsync`; the inner `DisableWebhookClientCommandHandler` re-reads the same
instance through the scoped context, exactly as on the create branch.

### 2. Branch-aware policy and wording, without more parameters

Introduce, inside the handler file, a private `readonly record struct RaceCheck(ClientId ClientId, FabIdentifier Fab,
string IntegrationName, bool IsRotation)` and pass it through `DisableIfRevokedSinceCommitAsync`,
`DisableIfRevokedAsync`, `RetryAfterFailedDisableAsync` and `AcknowledgeRaceDisableAsync` in place of their
`clientId/fab/integrationName` triples (parameter counts go down, not up).

- Disable predicate: `Revoked`, or `Unverifiable` **only when `!IsRotation`** (FR-003). On the rotate branch an
  `Unverifiable` re-check logs a new warning and returns `false`.
- Logging: three new `[LoggerMessage]`s in `src/Identity/Application/Log.cs` for the rotate branch —
  `DisabledClientRotatedDuringRevokeRace`, `CouldNotDisableClientRotatedDuringRevokeRace`,
  `AlreadyDisabledConcurrentlyDuringRotateRevokeRace` — plus `RotationRecheckStatusUnverifiable` (Warning). The call
  sites pick by `IsRotation`. Create-branch messages and their pinned texts are unchanged.
- Update the class `<summary>` (`:29-36`) and `DisableIfRevokedSinceCommitAsync`'s doc to describe both branches.

### 3. Nothing else moves

`HandleAsync`'s publish/commit tail (`:95-118`), `RetryAfterFailedDisableAsync`'s `DisabledAt` predicate, the detach
logic, `WebhookClientRaceDisableFailedHandler`, the endpoint, and EventIngestion are untouched.
`CredentialsOf` already reads the aggregate's version after any disable save.

## Phase 4a

**Red.** New tests in `tests/Identity.Application.Tests/Commands/RotateWebhookClientOrderCharacterisationTests.cs`
(it owns the `Harness` and the call-log vocabulary), each observed failing on `develop`:

- revoke after pre-check on rotate → log `["status","lookup","save","rotate-secret","status","lookup",
  "disable-client","save"]`, no Rotated, client `DisabledAt` set, Keycloak disabled, 200 with rolled secret, rotate
  wording logged.
- inner disable answers NotFound / throws `DbUpdateConcurrencyException` on rotate → no Rotated, no retry, no commit.
- inner disable's Keycloak call fails on rotate → retry + Rotated published, one commit.
- `Active, Unverifiable` on rotate → no `disable-client`, Rotated published + committed, warning logged
  (this one asserts the *new* second status call, so it is red on `develop` too).

**Deliberate literal changes (declare in the PR, not silent edits):**
`The_rotate_branch_saves_then_rolls_the_secret_then_publishes_then_commits` gains a `"status"` after
`"rotate-secret"` and `Status.Calls.Count` 1 → 2; `A_rotation_of_an_existing_client_never_rechecks_the_status_after_its_save`
pins the defect itself and is **replaced** by the revoke-race test above (renamed, not weakened). Every other test in
`tests/Identity.Application.Tests` must pass unmodified — the trial run at `8479dcdb` showed exactly these two flip.

## Risks

- `RotateWebhookClientCancellationTests` / `TokenHonouringPorts`: the rotate branch now makes a second status call
  under the bound; the trial run stayed green, re-confirm.
- Shard filters: no new test class (new facts go into an existing class), so no `shard-N.filter` entry is needed.
