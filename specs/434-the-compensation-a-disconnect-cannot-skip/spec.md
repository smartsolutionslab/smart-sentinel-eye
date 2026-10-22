# Feature Specification: The compensation a disconnect cannot skip

**Feature Branch**: `fix/3080-webhook-race-residuals`

**Created**: 2026-10-22

**Status**: Draft — phases 1-3, autonomous lane (ADR-0144).

**Issue**: #3080 (*a webhook client created in a revoke race can stay enabled when the request is
cancelled, and a dead-lettered race-disable raises no alert*)

**ADRs**: ADR-0160 (a revoked client is refused at request time from a `DisabledAt` snapshot),
ADR-0088 (Wolverine defaults, Postgres outbox), ADR-0042 / ADR-0057 (Wolverine behind the handler
interfaces), ADR-0049 (`CancellationToken` last parameter), ADR-0142 / ADR-0143 (retry safety,
`Idempotency-Key`), ADR-0050 (`[LoggerMessage]`, OTLP), ADR-0118 (one telemetry sink per
environment), ADR-0139 / constitution §Testing (new behaviour starts red), ADR-0144 (the lane; it
may not write an ADR).

**Predecessors**: spec 318 (#2628, the create-branch read-after-write re-check), spec 328 (#2629,
`WebhookIntegrationRevokedFailurePolicy`), spec 426 (#2789 / PR #3079, the
`WebhookClientRaceDisableFailed` outbox retry). Option C (extending `OrphanedClientSweep`) stays
deferred.

> Constructs are named, not line-numbered. Premise verified on `0598f594` (origin/develop).

---

## 1. The binding decision

Owner, 2026-10-21, comment on #3080:

> Items 1 and 2 only. (1) Run the post-commit race-path compensation in
> `RotateWebhookClientCommandHandler` on a bounded non-request token so a client disconnect after
> the create commits cannot skip the race disable, the retry message and the rotated announcement.
> (2) Add an operator signal for dead-lettered race-disable messages (log/metric on dead-letter; the
> revocation chain has the same gap — cover it with the same mechanism only if it is the same line
> of code, otherwise note it). Option C stays deferred. Item 3 cannot be verified without a live
> stack: record it as unverified, do not guess. No ADR; if the alert needs a new metrics convention,
> block rather than invent one.

## 2. Premise, re-verified at HEAD

| # | Claim in the issue | At `0598f594` |
|---|---|---|
| P1 | Everything after `RegisterAsync`'s save runs on the request token | **True.** `DisableIfRevokedSinceCommitAsync` (status lookup, inner disable, `PublishRetryAsync`), `events.PublishAsync(WebhookIntegrationRotatedV1 …)` and `commit.CommitAsync` all receive the handler's `cancellationToken`, which the endpoint passes from `IdempotentRequest.ExecuteAsync`'s request token. |
| P2 | Both catches exclude `OperationCanceledException` | **True.** `CallKeycloakAsync`'s `catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)` and the race catch `when (ex is not OperationCanceledException)`. Also: `EventIngestionWebhookIntegrationStatusLookup` rethrows OCE when its token is cancelled, so a cancelled request at the **post-commit** lookup throws before any disable is attempted. |
| P3 | A cancellation after the create commits skips the disable, the retry and the announcement | **True** (follows from P1 + P2). The Keycloak client and the `RegisteredClient` row exist; nothing disables them; EventIngestion never binds `KeycloakClientId`, so `IsRevokedByKeycloakClientIdAsync` is false for it and ADR-0160's snapshot misses it. |
| P4 | Dead letters raise no signal | **True.** `WebhookIntegrationRevokedFailurePolicy.Apply` is one line per chain: `chain.OnException<WebhookClientDisableFailedException>().ScheduleRetry(ladder)`; attempt 15 falls through to Wolverine's default `MoveToErrorQueue` with no hook. No `Meter` exists in `src/Identity`; Wolverine's own meter is not registered in `ServiceDefaults`. |
| P5 | The revocation chain has the same gap | **True, and on the same line of code**: the `foreach` in `Apply` attaches the identical rule to both `WebhookIntegrationRevokedV1` and `WebhookClientRaceDisableFailed`. The owner's condition for covering it is met. |
| P6 | Unit of work | The create's own `SaveAsync` commits the row; the caller's single `ITransactionalCommit.CommitAsync` flushes the outbox (retry message / rotated announcement). A non-request token changes **which token** these run on, not the order, the context or the number of commits. |

**Idempotency consequence (P1 → new behaviour, intended).** Today a request cancelled after the
create commits throws OCE out of `work(...)`, so `IdempotentRequest` *releases* the key; a same-key
retry finds the row and answers `409 WEBHOOK_CLIENT_ALREADY_EXISTS` (the secret is lost to the
caller). After this spec the handler returns success, so the key is *completed* (on
`CancellationToken.None`, as `IdempotentRequest` already does) and a same-key retry replays `200`
with the credentials via `ReplayRegisteredClientQuery`. Scope (caller + fab + fingerprint, spec 302)
is unchanged.

## 3. Metrics convention — exists, not invented

The repo already has one: a static class holding a `private static readonly Meter`, a public
`MeterName` const, an `sse.`-prefixed instrument name, and an `.AddMeter(X.MeterName)` registration
in the owning context's infrastructure module (`IngestVolume`, `IngestChannelSaturation` →
`EventIngestionInfrastructureModule`; `LatencyBudget`, `WallSkew`, `LabelDelay` →
`ServiceDefaults`). This spec adds one instance of that convention in Identity. **No new convention,
no ADR.** There is no alerting rule anywhere in the repo (ADR-0118: the dev/CI sink is the Aspire
dashboard; the production sink is deferred), so the "operator signal" is an `Error`-level structured
log plus a counter, both visible on the dashboard. An alert *rule* is out of scope by ADR-0118, not
by choice here.

## 4. User stories

### US1 (P1) — A disconnect after the create commits still disables a revoked-in-race client

A caller rotates (create branch) a webhook integration that is revoked between the pre-flight check
and the commit, and drops the connection after the commit.

**Acceptance**

```gherkin
Scenario: cancelled after commit, integration revoked — the client is still disabled
  Given the post-commit status is Revoked
  And the request token is cancelled immediately after the create's SaveAsync
  When the rotation completes
  Then the Keycloak client is disabled and the RegisteredClient row is Disabled
  And no WebhookIntegrationRotatedV1 is published
  And the handler returns success rather than throwing OperationCanceledException

Scenario: cancelled after commit, race disable fails — the retry message is still committed
  Given the post-commit status is Revoked and Keycloak fails the disable
  And the request token is cancelled after the create's SaveAsync
  Then WebhookClientRaceDisableFailed is published and CommitAsync runs

Scenario: cancelled after commit, integration active — the announcement is still committed
  Given the post-commit status is Active
  And the request token is cancelled after the create's SaveAsync
  Then WebhookIntegrationRotatedV1 is published and CommitAsync runs

Scenario: the bound elapses during the race disable — treated as a failed disable
  Given the post-commit status is Revoked and the Keycloak disable never answers
  When the compensation bound elapses
  Then WebhookClientRaceDisableFailed is published and CommitAsync runs

Scenario: cancelled before the create commits — unchanged
  Given the request token is cancelled before the create's SaveAsync
  Then OperationCanceledException propagates and nothing is committed
```

Bad-request and auth paths are untouched (pre-commit, request token, existing tests).

### US2 (P1) — A dead-lettered disable is visible to an operator

**Acceptance**

```gherkin
Scenario Outline: the 15th failed attempt dead-letters and records a signal
  Given a <message> whose disable has failed 14 times
  When the 15th attempt throws WebhookClientDisableFailedException
  Then the message is moved to the error queue
  And one sse.identity.webhook_client_disable.dead_lettered measurement is recorded tagged message=<tag>
  And one Error-level log names the integration and fab
  Examples:
    | message                        | tag                 |
    | WebhookClientRaceDisableFailed | race_disable        |
    | WebhookIntegrationRevokedV1    | revocation          |

Scenario: a retried attempt records nothing
  Given attempt 14 fails
  Then a retry is scheduled and no dead-letter measurement or Error log is recorded
```

## 5. Independent end-to-end test

Unit-level only (Identity.Application.Tests, Identity.Infrastructure.Tests). A live disconnect
cannot be staged deterministically, and the dead-letter path needs 15 attempts spread over ~1 h.

## 6. Unverified — item 3 (recorded, not guessed)

The duplicate run of `WebhookClientRaceDisableFailed` (local `BufferedInMemory` copy plus the
durable RabbitMQ copy) and what Wolverine's default policy does with the losing copy's
`DbUpdateConcurrencyException` are **unverified**: they need a live stack. With US2, a losing copy
that dead-letters would now record a measurement only if it dead-letters via
`WebhookClientDisableFailedException`; a `DbUpdateConcurrencyException` dead-letter (default policy)
records nothing. Recorded in the PR as unverified.

## 7. Out of scope

Option C (sweep); dead letters from any exception other than `WebhookClientDisableFailedException`
(default policy, no hook); an alerting rule (ADR-0118); the cancellation window *inside*
`CallKeycloakAsync`'s create (a Keycloak client without a row is `OrphanedClientSweep`'s job).

## 7a. Consequences of the bound (recorded, not loosened)

- **Every cancellation after the create's commit is a failed disable.** No request token reaches
  those awaits, so a cancellation that is not the bound's is an internal timeout
  (`HttpClient.Timeout`); it is handled like the bound's expiry rather than escaping as a 500 with
  the key released and an enabled client. Deliberate. If the cancelled call was the disabled row's
  own save (Keycloak already disabled), the row is committed here and no rotation is announced for
  it; otherwise the retry message is published.
- **When the 30 s bound expires, the caller still gets 200 with a secret** for a client that stays
  enabled in Keycloak until the retry disable succeeds or EventIngestion consumes the Rotated
  event. In that gap the azp is not linked, so a minted token passes the `/events/manual` revoked
  check. This widens an existing pattern on the disable-failure path; it does not introduce one.
- **Availability trade-off.** A slow EventIngestion already leads to `Unverifiable`, which disables.
  The bound adds a second route to the same unconditional retry disable. The caller gets no
  signal; fab scoping holds. Documented, not loosened.
- **`CommitAsync(CancellationToken.None)` is bounded only by Npgsql's defaults** (15 s connect,
  30 s command).
- **Idempotency-Key replay.** A retried keyed request replays the original answer, including the
  secret, for a client the compensation may since have disabled. Follow-up: #3096.

## 8. Latency

N/A — Identity admin path; not on the event-to-overlay path (constitution §IV).
