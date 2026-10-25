# Plan 460: A disabled client is never logged as rotated

**Spec:** [spec.md](spec.md) · **Issue:** #3139 · **Colour:** red · **ADR:** none new

## Constitution / ADR check

- §Testing / ADR-0139: behaviour-changing → the new assertions are observed red on `develop` first.
- ADR-0050: log line stays a `[LoggerMessage]` source-generated call; template untouched.
- §II, ADR-0105, ADR-0141: no new parameters, types or guards.
- Boundaries: Identity.Application only; no `Shared.Contracts`, no cross-context reference.
- §IV latency: N/A (not on the event→overlay path).

## Design

**Bounded context / layer:** Identity → Application → `Commands/Handlers/RotateWebhookClientCommandHandler.cs`.
No domain, infrastructure, API or migration change. No entities/value objects/events added.

**Change (the whole fix):** move `logger.RotatedWebhookIntegration(integrationName, clientId);` from after the
`if (!disabledDuringRace) { ... }` block (`:117`) to the end of that block, after `commit.CommitAsync(...)`.
The log then shares the publish's guard, which is FR-001's invariant: logged iff announced. Logging after the
commit (not before the publish) keeps "rotated" meaning "announced and flushed"; if the commit throws, the request
fails and nothing claims a rotation.

Update the comment block above the gate with one clause (the line is gated for the same reason as the event);
no other comment edits.

**Messaging:** unchanged. **Invariant:** `Logger.Named("RotatedWebhookIntegration").Count ==
Bus.Published.OfType<WebhookIntegrationRotatedV1>().Count` on every exit.

## Phase 4a — tests (all in `tests/Identity.Application.Tests/Commands/RotateWebhookClientOrderCharacterisationTests.cs`)

Existing class, existing `Harness` / `CapturingLogger` / `ScriptedDisableHandler` / `Repository.FailSecondSaveWith`
— no new fakes, no new class (so no shard-filter entry needed).

Red on `develop` (each currently finds one entry):

1. **Declared literal change** — `A_revoke_landing_during_the_create_disables_the_client_and_neither_publishes_nor_commits`
   (`:313-314`): replace the `ShouldHaveSingleItem().Message.ShouldBe("Rotated webhook integration ...")` with
   `harness.Logger.Named("RotatedWebhookIntegration").ShouldBeEmpty();`.
2. New `A_rotation_disabled_by_its_own_race_check_is_never_logged_as_rotated` — seed version 1, statuses
   `Active, Revoked`, `RotateIntent(1)`; assert `DisabledClientRotatedDuringRevokeRace` single and
   `RotatedWebhookIntegration` empty.
3. New `A_rotation_whose_client_was_already_disabled_is_never_logged_as_rotated` — `ScriptedDisableHandler` returning
   `WebhookClientNotFound`, rotate branch; assert `AlreadyDisabledConcurrentlyDuringRotateRevokeRace` single,
   `RotatedWebhookIntegration` empty.
4. New `A_rotation_that_lost_the_disable_to_a_concurrent_handler_is_never_logged_as_rotated` — `ScriptedDisableHandler`
   throwing `DbUpdateConcurrencyException`, rotate branch; same two assertions.
5. New `A_rotation_whose_disabled_row_failed_to_save_is_never_logged_as_rotated` — seed, statuses `Active, Revoked`,
   `Repository.FailSecondSaveWith = new DbUpdateException("the race disable's save failed")` (real inner disable:
   Keycloak disables, row's `DisabledAt` set, save fails → `RetryAfterFailedDisableAsync` returns `true`); assert
   `CouldNotDisableClientRotatedDuringRevokeRace` single, `WebhookIntegrationRotatedV1` none,
   `RotatedWebhookIntegration` empty. (If the harness's `FailSecondSaveWith` does not reach that exit, the
   test-writer reports it rather than inventing a fake.)
6. New `A_create_whose_client_was_already_disabled_is_never_logged_as_rotated` — `ScriptedDisableHandler` NotFound,
   `CreateIntent()`; assert `AlreadyDisabledConcurrentlyDuringRevokeRace` single, `RotatedWebhookIntegration` empty.

Stay green (guards against over-suppression; may be added as single extra assertions, must pass before and after):

- `The_rotate_branch_saves_then_rolls_the_secret_then_publishes_then_commits` and the create happy path — unchanged
  single entry, unchanged text.
- `A_failed_race_disable_on_a_rotation_is_retried_durably_and_the_rotation_is_still_announced` — add
  `harness.Logger.Named("RotatedWebhookIntegration").ShouldHaveSingleItem();` (green before and after: Keycloak never
  disabled, so it is still rotated).
- `An_unverifiable_status_after_the_rotation_leaves_the_client_enabled_and_still_announces_the_rotation` — same added
  assertion, green both sides.

## Risks

- Over-suppression on the "failed disable, Keycloak never disabled" exit — covered by the green guards above.
- None to latency, contracts, persistence or auth.
