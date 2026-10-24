# Plan 443: A disabled client is never announced as rotated

[spec](spec.md) · [tasks](tasks.md) · Issue #3112

## Slice

One slice, one file of production code. Engineer: **backend-engineer**.

## Bounded context and layers

Identity / **Application** only:
`src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`. No Domain, Infrastructure, Api,
Contracts, migration or AppHost change.

## Change

`DisableIfRevokedAsync` has two catches that end the same way once Keycloak has acted, but only one checks:

| Exit | Today | After |
|---|---|---|
| `OperationCanceledException` (`:455-477`) | `DisabledAt` null → retry, `false`; set → retry, commit, `true` | unchanged |
| catch-all `when (disabling)` (`:478-490`) | detach if `DbUpdateException`, retry, **`false` always** | detach if `DbUpdateException`, retry; then **same `DisabledAt` split as the OCE exit** |
| `DbUpdateConcurrencyException` (`:442-454`) | detach, `true`, no retry | unchanged |

Shape: extract the OCE exit's tail into one private method both catches call, e.g.

```csharp
// Keycloak has disabled the client once DisabledAt is set; only the row's save failed.
private async Task<bool> RetryAfterFailedDisableAsync(RegisteredClientAggregate aggregate, string integrationName, FabIdentifier fab)
```

returning `false` after the retry publish when `aggregate.DisabledAt is null`, otherwise publishing the retry,
`await commit.CommitAsync(CancellationToken.None)` and returning `true`. The OCE catch keeps its own log line and
its comment (the "cancellation inside the transaction commit" reasoning moves with the tail). The catch-all keeps
`Detach` **before** the call — the commit must not replay a failed `DbUpdateException` UPDATE. The OCE exit's
behaviour is preserved by its existing tests (characterisation). Update the comment block at `:83-92` (the
"nothing left to flush" list gains this exit) and the `<summary>` at `:390-395` ("which the caller's single commit
flushes" is no longer true for every failure).

Pre-existing behaviour accepted, not changed: on a still-dead connection the in-branch publish or commit throws and
the request fails 500 after the create committed — exactly what the caller's commit at `:115` does today. Fail-closed:
nothing is announced.

## Entities, value objects, invariants

No new state. Invariant enforced: **`aggregate.DisabledAt is not null` ⇒ no `WebhookIntegrationRotatedV1` is
published by this call.** `DisabledAt` is set only by `RegisteredClient.Disable`, which
`DisableWebhookClientCommandHandler` calls only after `keycloak.DisableClientAsync` returned, so the predicate means
"Keycloak has disabled the client".

## Messaging

Unchanged types. `WebhookClientRaceDisableFailed` (durable, ADR-0185 row 3) is still published exactly once on every
failure exit except the concurrency one, and always before the single commit that flushes it.
`WebhookIntegrationRotatedV1` (row 4) is suppressed on two more exits. The retry handler
(`WebhookClientRaceDisableFailedHandler` → `WebhookClientDisabling.DisableOrThrowForRetryAsync`) already treats a
disabled row as *nothing to disable* and an enabled row as a fresh disable, so both outcomes of the in-branch commit
are covered.

## Boundary rules

No new references. Identity → EventIngestion only through `Shared.Contracts` events, as today.

## Phase 4a — colours (declared, ADR-0144)

**Red** (behaviour-changing). Add to the **existing** class `tests/Identity.Application.Tests/Commands/RotateWebhookClientCommandHandlerTests.cs`
(a new class would need a CI shard-filter entry):

- **R1** race-disable save throws a non-`DbUpdateException` (e.g. `TimeoutException`; use `NpgsqlException` only if
  the test project already references Npgsql) via `InMemoryRegisteredClientRepository.FailSecondSaveWith`, status
  `Active, Revoked`, `SnapshottingTransactionalCommit`. Assert 200; `bus.Published.OfType<WebhookIntegrationRotatedV1>()`
  empty; retry single; `commit.Snapshots` single and containing the retry. **Expected red on develop:** Rotated present.
- **R2** same with `new DbUpdateException(...)`. Same assertions, same expected red.

**Characterisation** (observed green before the change, unmodified after):

- **C1** existing, no new test: `When_the_race_disable_itself_fails_the_create_still_returns_success_and_a_warning_is_logged`
  (`RotateWebhookClientCommandHandlerTests.cs:738-768`) — Keycloak disable fails → Rotated **is** published. Guards
  against an over-broad fix that suppresses Rotated on every failure.
- **C2** existing: `RotateWebhookClientRaceDisableOutboxCharacterisationTests` (3 facts),
  `A_race_disable_that_succeeds_or_finds_the_row_already_disabled_publishes_no_retry_message`,
  `When_the_race_disable_save_fails_without_a_conflict_a_retry_message_is_captured`,
  `RotateWebhookClientCancellationTests`, `RotateWebhookClientOrderCharacterisationTests`.

Note for the test-writer: `RepositoryBackedTransactionalCommit` re-throws after *any* failed save
(`HasUnresolvedConcurrencyFailure`), so R1/R2 must use `SnapshottingTransactionalCommit`, not it.

### Counterfactual (phase 4b, quoted in the PR)

Change the new predicate to always return `false` → R1 and R2 fail, C1 passes. Change it to always return `true`
(every exit treated as disabled) → C1 still passes, because it goes through `AcknowledgeRaceDisableAsync`; what fails is
`RotateWebhookClientOrderCharacterisationTests` `Any_other_exception_from_the_inner_disable_is_logged_as_not_disabled_and_the_rotation_is_announced`
(`:415`), `A_cancellation_inside_the_inner_disable_is_a_failed_disable_since_spec_434` (`:438`) and the new
`Keycloak_never_disabled_the_client_when_a_non_database_exception_escapes_the_inner_disable_so_the_rotation_is_published_exactly_once`. Revert.

## Risks

- **R-1** An extra `CommitAsync` call in the `DbUpdateException` path: entries are detached first, so it flushes only
  the outbox. Reviewer confirms `Detach` precedes it.
- **R-2** Spec-number collision with `sse-3101` (spec §Premise 5). **Resolved:** the #3101 spec was renumbered to 444.
