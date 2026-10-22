# Plan 434 — The compensation a disconnect cannot skip

**Spec**: [spec.md](spec.md) · **Issue**: #3080 · **Context**: Identity (Application + Infrastructure)
**ADR needed**: no (spec §3). **Shared.Contracts change**: none. **Migration**: none.

## 1. Constitution / ADR check

- No cross-context reference; Identity-internal only.
- Option/Result, `Ensure.That`, `var`, `[LoggerMessage]` unchanged in style.
- Metrics follow the existing static-`Meter` convention (spec §3). If the engineer finds the
  registration needs anything beyond `AddOpenTelemetry().WithMetrics(m => m.AddMeter(...))` in
  `IdentityInfrastructureModule` (mirroring `EventIngestionInfrastructureModule`), **stop and block**.

## 2. US1 — `RotateWebhookClientCommandHandler`

### 2.1 Which work moves off the request token

Everything after `RotateOrRegisterAsync` returns success:

| Step | Token today | Token after |
|---|---|---|
| post-commit `GetStatusAsync` + inner `DisableWebhookClientCommand` | request | **bounded** (2.2) |
| `PublishRetryAsync` (`WebhookClientRaceDisableFailed`) | request | `CancellationToken.None` |
| `PublishAsync(WebhookIntegrationRotatedV1)` | request | `CancellationToken.None` |
| `commit.CommitAsync` | request | `CancellationToken.None` |

Everything before (pre-flight lookup, fab-scoped lookup, Keycloak create/roll, the create's own
`SaveAsync`) stays on the request token: before the commit there is nothing to compensate.

The publish and commit use `CancellationToken.None`, following `IdempotentRequest`'s
`ReleaseAsync`/`CompleteAsync` precedent (*"a release that inherits the cancelled token would not run
at all"*). They are already bounded by their own transports (outbox publish is in-process; the
commit by Npgsql's command timeout). Giving them the bounded token instead would let a bound expiring
inside the disable also skip the retry message — the exact defect.

The rotate branch shares the same tail (publish + commit), so it moves too. Deliberate: it is one
code path, and an announcement skipped on the rotate branch has no compensation either.

### 2.2 The bound

`private static readonly TimeSpan CompensationBound = TimeSpan.FromSeconds(30);` — the standard
resilience handler's **total** request timeout (10 s per attempt, 30 s total), the neighbour cited by
`KioskPrivilegeSweepHostedService`. The status lookup and the Keycloak disable each sit behind that
pipeline, so 30 s lets one of them exhaust its own retries; it does not wait on a Keycloak that
never answers. Unlinked from the request token (not `CreateLinkedTokenSource`).

Construct with `new CancellationTokenSource(CompensationBound, timeProvider)`; inject
`TimeProvider` (registered as `TimeProvider.System` in `IdentityInfrastructureModule`; Application
handlers in AuditObservability already take it). Tests use the existing hand-written
`ManualTimeProvider` pattern (copy into Identity.Application.Tests/Fakes if not shared).

### 2.3 Bound expiry

A new catch in `DisableIfRevokedSinceCommitAsync`, ordered before the existing ones:
`catch (OperationCanceledException) when (bound.IsCancellationRequested)` → log
`CouldNotDisableClientCreatedDuringRevokeRace` (or a new `RaceDisableTimedOut` `[LoggerMessage]`),
publish the retry message, return `false`. Same for an expiry during the post-commit status lookup:
fail closed, matching the existing `Unverifiable` → disable rule; the retry handler disables
unconditionally, which is the fail-closed outcome (security reviewer to confirm, §5).

### 2.4 DI / characterisation

The handler gains one constructor parameter (`TimeProvider`). `IdentityRegistrationCharacterisationTests`
snapshots registrations, not constructors — expected unchanged; if it moves, that is a block. All
existing `RotateWebhookClientCommandHandlerTests` construct the handler directly and must be updated
**only** to pass a time provider (mechanical constructor change; no assertion edits).

## 3. US2 — dead-letter signal

### 3.1 Hook — the same line for both chains

In `WebhookIntegrationRevokedFailurePolicy.Apply`:

```csharp
chain.OnException<WebhookClientDisableFailedException>()
    .ScheduleRetry(ladder)
    .Then.MoveToErrorQueue()
    .And(RecordDeadLetterAsync, "Record the webhook-client disable dead letter");
```

Wolverine 6.51.0's XML docs list `IFailureActions.MoveToErrorQueue` and
`IAdditionalActions.And(Func<IWolverineRuntime, IEnvelopeLifecycle, Exception, ValueTask>, string)`.
**The engineer must decompile-verify** that (a) `.Then.MoveToErrorQueue()` after a 14-slot
`ScheduleRetry` applies at attempt 15 exactly as the default does today, and (b) `.And(...)` runs
only on that slot. If either fails, stop and report — do not substitute a global observer.

### 3.2 Signal

`src/Identity/Application/EventHandlers/WebhookClientDisableDeadLetters.cs` (static, mirroring
`IngestVolume`): `MeterName = "SmartSentinelEye.Identity.WebhookClientDisable"`, instrument
`sse.identity.webhook_client_disable.dead_lettered` (`{message}`), one tag `message` =
`revocation | race_disable`. Plus an `Error`-level `[LoggerMessage]` in Identity's Infrastructure
`Log` naming message type, integration name, fab (read from the envelope's message), and the
exception code. No secret, no token in the log.

Registration: `.AddMeter(WebhookClientDisableDeadLetters.MeterName)` in
`IdentityInfrastructureModule`, mirroring `EventIngestionInfrastructureModule`.

### 3.3 Existing assertion that may move

`The_fifteenth_failed_attempt_is_dead_lettered` / `..._race_path_...` assert the continuation's
type name is `MoveToErrorQueue`. If `.And(...)` wraps it in a composite, that assertion changes.
That is the new behaviour, not drift: the replacement must still prove dead-lettering (the composite
contains a `MoveToErrorQueue`) and must be called out in the PR body. If the type does **not**
change, the tests stay unmodified.

## 4. Tests (phase 4a, red) — see tasks.md T002–T004

Request-cancellation fakes must honour the token as the real collaborators do
(`ThrowIfCancellationRequested`); the current in-memory fakes ignore it, which would let A–D pass
green against today's code.

## 5. Review

`backend-reviewer` + **`security-reviewer` mandatory**: fab scoping of the disable (the inner
handler re-runs the fab-scoped lookup — unchanged), fail-closed on bound expiry (a timed-out
lookup ends in a disable via the retry handler), idempotency replay of a completed key after a
disconnect (spec §2), no secret in the new log.
