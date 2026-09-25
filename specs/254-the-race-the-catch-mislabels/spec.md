# Spec 254: The race the catch mislabels

**Issue**: #2570 (feature-level; no per-task issues) · **Branch**: `fix/2570-webhook-rotation-concurrency-errorcode`
**Status**: Phase 3 complete. Tasks written; phase 4a next. ·
**Lane**: autonomous (ADR-0144)
**ADRs**: ADR-0113 (two-layer optimistic concurrency: Layer 2's `DbUpdateConcurrencyException` "is
translated to **409** by one shared mapping … in middleware — not in a `try`/`catch` in each … handler",
and "a mocked throw proves nothing about the EF wiring"), ADR-0047 (infrastructure signals belong to
middleware), ADR-0036 (smallest change), ADR-0139 (red first), ADR-0144 (lane).
Origin: spec 238 §4 **O1**, filed there as T006 → this issue.
**Latency budget**: N/A. This is an admin write path (webhook credential rotation), not on the
event→overlay path (constitution §IV).
**No new ADR.** The decision already exists (ADR-0113's single middleware mapping). This spec makes one
handler stop pre-empting it.

---

## 1. The premise, re-verified (2026-09-25, this branch, cut from `origin/develop`)

The issue's stated mechanism is **wrong, and the defect is real anyway**, by a simpler route.

**What the issue says:** EF/Npgsql wraps the Layer-2 loser's `DbUpdateConcurrencyException` in an
`InvalidOperationException`, and the filter's exclusion of `InvalidOperationException` lets it slip
through to `KEYCLOAK_UNAVAILABLE`.

**Why that is not the mechanism.** Spec 238 §1 and its table row S4 (verified against the pinned
Npgsql 10.0.3): `NpgsqlExecutionStrategy` wraps only exceptions `ShouldRetryOn` calls transient.
`DbUpdateConcurrencyException` has no provider exception inside it, so it is never wrapped. It reaches
application code raw. There is also a logic slip in the issue: a *wrapped* exception would be an
`InvalidOperationException`, which this filter **excludes**. It would escape the catch, not be caught by it.

**What actually happens:** `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`

- `:95-97`: on the rotate branch, `aggregate.Rotate(clock)` then `await clients.SaveAsync(...)`.
  `RegisteredClientRepository.SaveAsync` ends in `OutboxTransactionalCommit.CommitAsync` →
  `SaveChangesAndFlushMessagesAsync`, where the `Version` concurrency token
  (`RegisteredClientConfiguration.cs:76`, `IsConcurrencyToken()`, bumped by `AggregateVersionInterceptor`)
  is enforced. The comment at `:84-89` says so itself: "only Layer 2 (the EF token on this save) picks a
  winner."
- `:139-143`: `catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)`
  → `KeycloakUnavailable` (502 `KEYCLOAK_UNAVAILABLE`).
- A raw `DbUpdateConcurrencyException` is neither excluded type, so **the handler catches it** and
  returns 502. `ConcurrencyConflictExceptionHandler` (`src/ServiceDefaults/Persistence/…:41`) is an
  ASP.NET Core `IExceptionHandler` (registered in `AuthenticationDefaults.cs:92`, reached through
  `app.UseExceptionHandler()` in `src/Identity/Api/Program.cs:17`). It only sees exceptions that escape
  the endpoint, so it never gets this one.

**Why it went unseen.** The filter dates from `829ba245` (2026-05-29). At that point the `try` held only
Keycloak calls, and the filter was correct for it. `5d5b7abc` (2026-08-02, "close the four holes in the
webhook rotation gate") moved the rotate branch's `SaveAsync` *into* the `try`, ahead of the Keycloak
roll, and left the filter alone. `RegisteredClientConcurrencyIntegrationTests` covers Layer 1 only: its
second rotation re-reads the committed version and is refused before `SaveAsync`.

**The register already claims the correct behaviour.** `tests/Architecture.Tests/ConcurrencyConflictDeclarationTests.cs:360-362`
declares that `Identity POST /webhook-integrations/{name}/rotate` answers 409 through "lost update on
the branch that rotates an existing client", which is the `ConcurrencyConflictExceptionHandler` route
(its class doc, `:40-43`). The code currently contradicts its own declaration.

**Verdict: confirmed at code-reading level, not yet observed.** ADR-0113 says "a mocked throw proves
nothing about the EF wiring", and the issue asks for genuine concurrency, so the defect counts as
confirmed only when phase 4a observes a real two-request race return 502 `KEYCLOAK_UNAVAILABLE`
against the Aspire stack (US1 AS-1, red). See §5 for the other reds phase 4a might see instead.

**Which 409.** The correct loser answer is **`409 AGGREGATE_VERSION_STALE`**, produced by
`ConcurrencyConflictExceptionHandler.ErrorCode`, the one shared Layer-2 mapping ADR-0113 prescribes.
It is **not** `WEBHOOK_CLIENT_STALE`, which is this handler's typed Layer-1 refusal. No third code is
introduced.

---

## 2. User stories

### US1 (P1): A rotation that loses the database race is told it conflicted, not that Keycloak is down

*As an admin rotating a webhook credential at the same moment as another admin*, I want the losing
request answered `409 AGGREGATE_VERSION_STALE`, *so that* I re-read and decide. The alternative is
retrying against an "outage" that never happened, and a retry would roll the secret the winner has
just been handed.

```gherkin
Background:
  Given webhook client "webhook-<unique>" exists in fab "munich" at version V
  And the caller is an admin with access to fab "munich"

Scenario: AS-1 (happy/conflict, the defect): the Layer-2 loser gets 409 AGGREGATE_VERSION_STALE
  When several rotations, each sending If-Match: "V", are dispatched together
  And at least two of them pass the version check before either commits
  Then exactly one answers 200 with a new secret and version > V
  And every other answers 409 with title WEBHOOK_CLIENT_STALE or AGGREGATE_VERSION_STALE
  And at least one answers AGGREGATE_VERSION_STALE (the race reached Layer 2)
  And none answers 502 KEYCLOAK_UNAVAILABLE
  And the winner's secret still authenticates with a client_credentials grant

Scenario: AS-2 (conflict, Layer 1, unchanged): a sequential stale rotation keeps its typed refusal
  Given one rotation at If-Match: "V" has already succeeded
  When a second rotation sends If-Match: "V"
  Then it answers 409 WEBHOOK_CLIENT_STALE
  # Existing test A_rotation_superseded_by_another_admin_leaves_the_live_secret_working, must stay green unmodified.

Scenario: AS-3 (infrastructure failure, unchanged): a genuine Keycloak failure still reports KEYCLOAK_UNAVAILABLE
  Given Keycloak's admin API fails
  When a rotation is attempted
  Then it answers 502 KEYCLOAK_UNAVAILABLE
  # Existing unit test Keycloak_transport_failure_returns_KeycloakUnavailable, must stay green unmodified.

Scenario: AS-4 (the handler contract, deterministic): the handler does not swallow a concurrency exception
  Given the rotate branch's save throws DbUpdateConcurrencyException
  When the handler runs
  Then the DbUpdateConcurrencyException propagates out of HandleAsync
  And the Keycloak secret is not rolled
```

**Bad-request / auth scenarios: unchanged, N/A to this fix.** 400 `WEBHOOK_INVALID_INPUT`, 428
without a precondition, and the 403 fab guard are all decided before the `try` block and are
covered by existing tests (`A_rotation_without_a_precondition_is_refused_with_428`, spec 182's
cross-fab tests). This change does not touch them.

### Independent end-to-end test procedure

1. Boot the stack through the Aspire fixture (one machine, one stack).
2. `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~RegisteredClientConcurrencyIntegrationTests"`.
   AS-1 fires a round of `Racers` concurrent rotations at one version. It repeats rounds against the
   new version until a Layer-2 loser has been observed or the round cap is reached. Every answer is
   quoted in the assertion message.
3. `dotnet test tests/Identity.Application.Tests --filter "FullyQualifiedName~RotateWebhookClientCommandHandlerTests"` (AS-4).

---

## 3. Requirements

- **FR-001** On the rotate branch, a `DbUpdateConcurrencyException` from `clients.SaveAsync` must
  propagate out of `RotateWebhookClientCommandHandler.HandleAsync`. It must not be converted to
  `KeycloakUnavailable`.
- **FR-002** Every other exception keeps its current classification: Keycloak failures on both
  branches → `KeycloakUnavailable`; `OperationCanceledException` and `InvalidOperationException` →
  propagate. The existing `RotateWebhookClientCommandHandlerTests`, `StaleVersionRejectionTests` and
  `RegisteredClientConcurrencyIntegrationTests` tests stay green **unmodified**.
- **FR-003** The loser's HTTP answer is `ConcurrencyConflictExceptionHandler`'s: 409, title
  `AGGREGATE_VERSION_STALE`. No new error code or `RotateWebhookClientError` variant.
- **FR-004** Colour: **behaviour-changing → red first** (ADR-0139). AS-1 and AS-4 must be observed
  red on unchanged production code, with the failure quoted, before the fix.
- **FR-005** AS-1 must drive **genuine concurrency over HTTP against the real stack** (ADR-0113
  "verification is behavioural"; the issue's item 1). AS-4 is a deterministic supplement pinning
  the handler contract. It cannot confirm the defect on its own.
- **FR-006** AS-1 must not pass vacuously. A run in which no racer reaches Layer 2 fails as
  *inconclusive* with its own message, never green.

## 4. Scope decisions

- **`DbUpdateConcurrencyException` only, not the whole `DbUpdateException` family.** The issue also
  names "any non-transient `DbUpdateException`". Excluded deliberately:
  (1) On the rotate branch an `UPDATE` of `registered_clients` has no reachable unique/check violation.
  (2) On the register branch the save follows `CreateClientAsync`. Keycloak refuses a duplicate
      `clientId` first, so a unique race on `ux_registered_clients_clientid_active` is not reachable
      through this route in practice.
  (3) Widening to the base type would add `409 RESOURCE_ALREADY_EXISTS` (unique race) to the route's
      answer set. `ConcurrencyConflictDeclarationTests.cs:360-362` does not declare that for this route,
      and a register-branch unique violation would surface *after* a Keycloak client was minted,
      leaving an orphan that a 409 would misdescribe too. That is a different defect with its own design
      question.
  Recorded here so the narrowing is visible rather than rediscovered.
- **Not restructuring the `try`.** Wrapping only the Keycloak calls would be cleaner attribution, but it
  changes the classification of every other non-Keycloak failure inside `SaveAsync` (domain-event
  dispatch, outbox flush) from 502 to 500. That is a wider behavioural surface this issue did not ask
  about. See O1.

## 5. Assumptions (each checked by phase 4a, not trusted)

- **A1**: In the real stack, the Layer-2 loser's exception leaves `SaveAsync` as a **raw**
  `DbUpdateConcurrencyException`. Wolverine's `SaveChangesAndFlushMessagesAsync` does not wrap it, and
  neither does `NpgsqlExecutionStrategy` (spec 238 S4). **If AS-1's pre-fix red is a 500 rather than a 502**,
  the exception arrived wrapped: that is the issue's original mechanism, and neither this filter nor
  `ConcurrencyConflictExceptionHandler` (which matches the exact type) handles it. **STOP and report
  verbatim**, because the fix would then belong in the middleware, not here.
- **A2**: `Racers` concurrent HTTP rotations reliably put ≥ 2 requests between `GetWithinFabAsync` and
  commit within a bounded number of rounds. The loser's `UPDATE … WHERE version = V` blocks on the
  winner's row lock and then affects 0 rows (READ COMMITTED). **If the pre-fix run only ever produces
  `WEBHOOK_CLIENT_STALE`**, the harness did not reach Layer 2. That inconclusive red is **not** the
  defect's red (see the memory note "the wrong red matches"). Report it rather than tuning blindly.
- **A3**: A loser's DbContext holds a failed tracked `Modified` aggregate. The idempotency release path
  uses raw SQL (`IdempotencyStore.ReleaseAsync`), so it does not re-save that entity. AS-1 sends no
  `Idempotency-Key`, so this path is not exercised here anyway.

## 6. Out of scope, recorded

- **O1: other non-Keycloak failures inside the `try` are still labelled `KEYCLOAK_UNAVAILABLE`.**
  A domain-event handler throwing inside `SaveAsync`'s dispatch, or a non-transient outbox/SQL error
  other than the concurrency one, still returns 502 naming Keycloak. Same misattribution class,
  unobserved, no reported impact. The orchestrator may file it (board-checked first). Not fixed here.
- **O2: the register-branch race.** Two concurrent `If-None-Match: *` creates both pass the
  not-exists check. The second `CreateClientAsync` is refused by Keycloak (duplicate `clientId`) and
  reported as 502 `KEYCLOAK_UNAVAILABLE`, not 409. That is a Keycloak response being misclassified,
  not a database exception, so it is outside this issue. Recorded only.
