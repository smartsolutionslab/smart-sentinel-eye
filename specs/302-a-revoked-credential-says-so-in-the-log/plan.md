# Plan 302 — A revoked credential says so in the log

**Spec:** `spec.md` · **Issue:** #2205 · **Context:** EventIngestion (Api layer only)

## 1. Shape

One bounded context, one layer. No Domain, Application, Infrastructure,
Shared.Contracts, migration or AppHost change.

| File | Change |
|---|---|
| `src/EventIngestion/Api/Log.cs` | **New.** `internal static partial class Log`, `[ExcludeFromCodeCoverage]`, mirroring `src/StreamDistribution/Api/Log.cs`. The Application `Log` is `internal` to Application and not visible here. |
| `src/EventIngestion/Api/EventsEndpoints.Writes.cs` | Split the `:211` condition; log on the revoked branch; thread an `ILogger` in. |
| `tests/Integration.Tests/EventIngestion/RevokedWebhookDeliveryIsLoggedIntegrationTests.cs` | **New** test class. |
| `tests/Integration.Tests/ci-shards/shard-1.filter` | Add the new class (a missing entry fails CI deterministically). |

## 2. Why an application log, not an AuditObservability row

AuditObservability is fed exclusively by integration events
(`IntegrationEventAuditHandler`) and its remit is config writes, ingested events
and variable changes (constitution §1, item 9; §Security "admin and config
writes"). Writing a refusal there would need:

- a new `Shared.Contracts` V1 message published from an **unauthenticated**
  path — any anonymous caller who knows a revoked name could then write rows
  into a table retained 365 days (storage amplification), plus an outbox write
  on every refusal;
- a new cross-context flow, which is architecture and would need an ADR.

The revocation *itself* is already audited (`WebhookIntegrationRevokedV1` →
`IntegrationEventAuditHandler.cs:44`). The operator therefore gets the pair
they need: the log says "refused because revoked at T", the audit trail says who
revoked it at T. "Audit line" in the #2205 decision is satisfied by the log
record; it is not an audit-table row.

## 3. The change at the call site

`AuthenticateWebhookAsync` (`EventsEndpoints.Writes.cs:185`) gains an
`ILogger logger` parameter (before `CancellationToken`, ADR-0049). The
condition at `:211` splits:

```csharp
if (!found.HasValue)
{
    return null;
}

if (found.Value.RevokedAt is { } revokedAt)
{
    logger.RevokedWebhookIntegrationDeliveryRefused(
        found.Value.Name, found.Value.Id, found.Value.Fab, revokedAt);
    return null;
}
```

The pattern match is the same test as `IsRevoked` (`WebhookIntegration.cs:92`
is `RevokedAt is not null`) and binds the value without a null-forgiving `!`.

Both branches still return `null`, so `:138-141` (`Results.Unauthorized()`) is
untouched and the response is byte-for-byte unchanged. Position matters: the
revoked branch stays **before** the fab comparison (`:226`) and the mode branch
(`:231`), exactly where spec 102 requires the revocation clause to sit.

`IngestWebhook` (`:121`) takes `[FromServices] ILoggerFactory loggerFactory` and
passes `loggerFactory.CreateLogger(typeof(EventsEndpoints))` — category
`SmartSentinelEye.EventIngestion.Api.EventsEndpoints`, which `appsettings.json`
(`Default: Information`) admits. No `[FromServices] ILogger` precedent exists in
`src/`; `ILoggerFactory` avoids the weak `ILogger<Program>` category. No service
locator through `request.HttpContext.RequestServices`.

The `<summary>` on `AuthenticateWebhookAsync` (`:176-184`) gains one sentence:
the revoked path also writes an internal record; the response does not differ.

## 4. The log method

```csharp
[LoggerMessage(Level = LogLevel.Warning,
    Message = "Refused a delivery to revoked webhook integration '{Name}' ({Identifier}) in fab {Fab}; revoked at {RevokedAt}.")]
public static partial void RevokedWebhookIntegrationDeliveryRefused(
    this ILogger logger, WebhookIntegrationName name, WebhookIntegrationIdentifier identifier,
    FabIdentifier fab, RevokedAt revokedAt);
```

- **Distinct from** Application `Log.WebhookIntegrationRevoked` ("Revoked
  webhook integration '{Name}'…"), which fires once at revocation time. Both
  name the integration, so the test must key on the phrase
  `Refused a delivery to revoked webhook integration '<name>'`, never on the
  name alone.
- **Never** carries the bearer, the request body, or the caller-supplied
  `fabId` (caller-controlled; the integration's own `Fab` is the useful one).
- `Warning`: it is a refusal an operator may need to act on, matching
  `UnregisteredEventTypeRefused`. Not rate-limited — see spec §8.

## 5. Tests (§Testing, ADR-0103, ADR-0053)

New class `RevokedWebhookDeliveryIsLoggedIntegrationTests`,
`[Collection(AspireCollection.Name)]`, **no `[Trait("Category", …)]`**. Reads
logs with `aspire.CaptureLogs("event-ingestion")` (`AspireFixture.LogCapture.cs`),
the idiom `WhepAuthorizeRateLimitTests` uses; never the 400-line ring directly.
Helpers (register/capture token, revoke with `If-Match`, post) are copied from
`WebhookRevocationRefusesDeliveryIntegrationTests` — not extracted from it,
because that file is characterisation and must not be edited.

1. `A_delivery_to_a_revoked_integration_is_logged_as_revoked` — register,
   revoke, open capture, POST; assert 401 first (premise), then poll the capture
   until it contains `Refused a delivery to revoked webhook integration '<name>'`
   and `munich`. Red today: the phrase is never written.
2. `A_delivery_to_an_integration_that_never_existed_logs_no_revocation` —
   open capture; POST to a never-registered name `Y` (assert 401); **then**
   POST to a revoked integration `X` as a sentinel; wait for X's record; assert
   no record names `Y`. The sentinel makes the absence meaningful (one process,
   ordered output) and makes the fact red today, because the sentinel never
   appears. It catches an implementation that logs on `!found.HasValue` too.
3. `The_revoked_delivery_record_never_carries_the_bearer` — after (1)'s record
   appears, assert no captured line contains the token string.

Characterisation, unmodified: the three facts of
`WebhookRevocationRefusesDeliveryIntegrationTests`, plus
`AnonymousIngestIsRefusedTests` and `WebhookBearerValidationIntegrationTests`.

## 6. Boundaries / contention

No cross-context reference. No ADR-0109 contention file (`AppHost`,
`Shared.*`, `Directory.*`). Can run concurrently with any other slice.

## 7. The doc correction #2205 also asked for

**Already done on `develop`.** `AnonymousIngestIsRefusedTests.cs:20-28` now
says the revoked-integration claim was wrong and names spec 102 as the coverage
(commit `5e754a1f`, contained in `origin/develop`). No task.
