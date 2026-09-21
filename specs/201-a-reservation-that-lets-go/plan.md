# Plan — Spec 201, a reservation that lets go

**Feature:** #2290 · **Spec:** `spec.md` · **Phase-4a colour:** RED

## Context and layers

**No bounded context is involved.** All three stories live in
`src/ServiceDefaults/Idempotency/`, which is shared infrastructure referenced by
every context's `Infrastructure` and `Api` projects — the same place
`IdempotencyStore<TDbContext>`, `OutboxEventBus<TDbContext>` and
`OutboxBacklogHealthCheck` already live.

There is **no Domain and no Application layer in this slice**, which settles
three house rules up front:

- **Constitution §II (no primitives on a domain model) is not engaged.** No
  domain model is touched. `IdempotencyScope`'s `string Endpoint` and
  `string Caller` stay as they are; `PrimitiveBoundaryTests` scans Domain
  projects and will not see this code.
- **ADR-0141's `Option<T>` preference is not engaged.** Its stated scope is
  Domain and Application; ServiceDefaults keeps nullable references and
  framework vocabulary, for the same reason `Shared.Contracts` does.
- **The no-cross-context-reference rule is not engaged.** Nothing here
  references a bounded context, and no context gains a reference to another.
  NetArchTest's boundary suite is unaffected. No `Shared.Contracts` change
  means no integration-event version bump and no consumer migration.

| File | Story | Change |
|---|---|---|
| `src/ServiceDefaults/Idempotency/IdempotencyReclamation.cs` | US1 | **New.** The bound, and the one place it is written down |
| `src/ServiceDefaults/Idempotency/IdempotencyStore.cs` | US1 | `BeginAsync` — the `ON CONFLICT` action only |
| `src/ServiceDefaults/Idempotency/IdempotentRequest.cs` | US2 | The `catch` block at `:172-181` |
| `src/ServiceDefaults/Idempotency/IdempotencyReservationSweep.cs` | US3 | **New.** Hosted service, its options, its registration extension |
| `src/ServiceDefaults/Log.cs` | US3 | One `[LoggerMessage]` line for the sweep |
| `src/<seven contexts>/Infrastructure/*InfrastructureModule.cs` | US3 | One registration line each, beside the existing `AddScoped<IIdempotencyStore, …>` |
| `tests/Integration.Tests/CameraCatalog/StaleIdempotencyReservationIntegrationTests.cs` | US1, US3 | **New.** The red test and the two guards |
| `tests/ServiceDefaults.Tests/Idempotency/IdempotentRequestTests.cs` | US2 | Two new cases; **existing cases unmodified** |

**Not touched, and a task that proposes touching one has gone wrong:** the ten
endpoint files, `IdempotencyScope`, `IdempotencyKey`, `IdempotencyHeaders`,
`IIdempotencyStore`'s signature, `IdempotencyKeyTable`'s DDL (the column and
index it needs already exist), and every existing migration.

**No migration is needed by any story.** `reserved_at` is already `NOT NULL`
and already indexed. That is the point of the finding: the schema was built for
this and the code never arrived.

## US1 — reclaiming a stale reservation

### The design: one statement, not two

`BeginAsync`'s claim becomes:

```sql
INSERT INTO idempotency_key (key, endpoint, caller, reserved_at)
VALUES ({0}, {1}, {2}, NOW())
ON CONFLICT (key, endpoint, caller) DO UPDATE
   SET reserved_at = NOW()
 WHERE idempotency_key.resource_identifier IS NULL
   AND idempotency_key.reserved_at < NOW() - {3};
```

`{3}` is the `TimeSpan` from `IdempotencyReclamation.StaleAfter`, which Npgsql
maps to `interval` natively — passed as a parameter rather than interpolated
into the string, so the duration is never formatted and re-parsed.

**Everything downstream of the statement is unchanged.** `ExecuteSqlRawAsync`
reports `1` for a conflict-update exactly as it does for an insert, and `0`
when the `WHERE` excludes the update. So `if (inserted == 1) return
IdempotencyReservation.Reserved;` (`IdempotencyStore.cs:47-50`) keeps its
meaning — "this caller owns the reservation" — in both cases, and the fallback
`SELECT`, the `InProgress` branch and the `CompletedWith` branch are untouched.
The diff is the conflict action and nothing else.

### Why `SET reserved_at = NOW()` is load-bearing

It is not bookkeeping; it is what makes the reclaim atomic against a second
reclaimer. Two requests arriving together after the bound both attempt the
statement. Postgres takes a row lock, so one wins. The loser's `ON CONFLICT`
re-evaluates against the **updated** row, whose `reserved_at` is now `NOW()`,
so the `WHERE` fails, it gets `0`, and it falls through to the existing
`InProgress` path — which then waits out the new holder and replays its answer,
exactly as it does for any other live attempt.

Without the `SET`, both would match and both would run the work. The guarantee
would be gone in precisely the situation the story exists for.

### Why both guards are in the `WHERE`

- `resource_identifier IS NULL` — the ADR-0142 §Consequences boundary. It is
  the same guard `ReleaseAsync` already carries (`:96-102`) and keeps every
  completed key out of reach no matter how old it is.
- `reserved_at < NOW() - StaleAfter` — the liveness test.

`completed_at IS NULL` is deliberately **not** added. `CompleteAsync` sets both
columns in one statement (`:83-86`), so it is implied; adding it would be a
second spelling of one invariant, and two spellings drift.

### Where the bound lives

```csharp
public static class IdempotencyReclamation
{
    public static TimeSpan StaleAfter { get; } = TimeSpan.FromMinutes(10);
}
```

One public constant, read by US1's SQL and US3's sweep so the two can never
disagree. Its doc comment carries the arithmetic from `spec.md` §US1 — 120x the
in-progress wait, 60x the attempt timeout — so the next reader can re-derive
the number instead of trusting it.

**It is not configurable, and that is a decision rather than an omission.** A
per-environment knob for a bound this far above any real request adds a way to
set it wrong (a five-second override would double-apply creates) in exchange
for nothing anyone has asked for — ADR-0036's no-speculative-generality rule,
and there is no test seam argument either, because §*Testability* does not need
one.

### Testability: a dead reservation without a dead process

**The comparison is against Postgres `NOW()`, not the injected
`TimeProvider`.** The existing SQL already uses `NOW()` and US1 keeps it, for
the reason the file already gives: the claim has to be atomic, and the database
is the only clock every participant shares. A fake `TimeProvider` therefore
cannot age a row, and phase 4a must not try — **if a task reaches for
`InstantClock` here, the design has been misread.**

The row is aged in SQL instead. The recipe **derives the scope from the running
system rather than guessing it**, which matters more than it looks: `caller` is
the operator identifier the endpoint computes from the bearer token
(`CameraEndpoints.cs:184`), and a test that hand-wrote a plausible subject
string would insert a row in a *different* scope, leave the real claim
untouched, observe a perfectly ordinary `201`, and pass green while proving
nothing — an assertion that cannot fail.

So: make the system write the row, then damage it.

1. `POST /cameras` with `Idempotency-Key: K` and name `N1` → `201`. The row now
   exists with the correct `key`, `endpoint` and `caller`, completed.
2. Through `aspire.CreateCameraCatalogDbContextAsync()`, run

   ```sql
   UPDATE idempotency_key
      SET resource_identifier = NULL,
          completed_at        = NULL,
          reserved_at         = NOW() - INTERVAL '30 minutes'
    WHERE key = {0};
   ```

   That is byte-for-byte the state `IdempotencyKeyTable`'s own schema uses for
   "an attempt still running" (`:32-35`), aged past the bound.
3. `POST /cameras` with the same key `K` and a **different** name `N2`.
   - **Red today:** `409 IDEMPOTENT_REQUEST_IN_PROGRESS`, after ~5 s of polling.
   - **Green after:** `201`, and a camera named `N2` exists.

Step 3 using a *different* name is what makes success observable: a replay
would hand back `N1`'s identifier, so `N2` existing proves the work actually
ran on the reclaimed reservation rather than the response merely changing
shape.

The two control cases reuse the recipe with one value changed — `INTERVAL '10
seconds'` for the still-live case (must stay `409`), and an `UPDATE` that
backdates `reserved_at` to `INTERVAL '30 days'` while **leaving
`resource_identifier` intact** for the aged-completed case (must stay a replay
of `N1`). The third is the ADR-0142 guard and is the single most important
assertion in the suite; without it a sweep that ignored the identifier would
pass everything else.

**Why CameraCatalog and not Identity.** The fixture already exposes
`CreateCameraCatalogDbContextAsync` (`AspireFixture.Db.cs:61`) and there is no
Identity equivalent; `IdempotentCameraRegistrationIntegrationTests` in the same
folder supplies the request shape; and the generic store against a second
`DbContext` is the case worth proving, not Identity's Keycloak-flavoured one.
Registration is admin-scoped and name-unique, so a differing name gives a clean
observable.

**Isolation:** the integration database is shared across runs, so every key and
camera name is run-unique (`$"key-{Guid.CreateVersion7():N}"`), following the
note `EventTypeRegistryIdempotencyIntegrationTests` already records — a literal
key would be completed by the first run and replayed by the second, and the
test would pass while registering nothing.

## US2 — the release that must not speak over the failure

### Scope decision: in this spec, and why

The issue names it, and three things say it belongs here rather than in a
follow-up:

- **It is the second of the two paths `spec.md` §Problem identifies as wedging
  a row.** A spec that fixed one and filed the other would leave the issue half
  closed while claiming it.
- **It is in the same file family and would conflict.** `IdempotentRequest.cs`
  and `IdempotencyStore.cs` are two files in one folder; a second spec would
  either stack or collide for a change of about eight lines.
- **US1 is what makes the honest fix available.** Before US1, swallowing a
  failed release meant accepting a permanently wedged row, and the only
  defensible behaviour was arguably to shout. After US1 the row self-heals
  within the bound, so preferring the original exception costs nothing
  permanent. Fixing US2 *without* US1 would be the questionable order.

What is **not** in scope is the neighbouring success-path calls at `:185, 189`
— `spec.md` §Out of scope gives the reasoning and says to file them separately.
They look similar and are a different question.

### The change

```csharp
catch
{
    await ReleaseQuietlyAsync(execution.Store, scope);

    throw;
}
```

with

```csharp
private static async Task ReleaseQuietlyAsync(IIdempotencyStore store, IdempotencyScope scope)
{
    try
    {
        await store.ReleaseAsync(scope, CancellationToken.None);
    }
    catch (Exception releaseFailure)
    {
        // Not rethrown, and the codebase's own rule says a swallowed exception
        // is a review blocker - so this states its case. Rethrowing here takes
        // the catch block's place and the caller never learns what actually
        // failed; the release runs on the connection that just failed the work,
        // so the two failures are correlated and the second is the less
        // informative one. The row it could not delete is no longer permanent:
        // BeginAsync reclaims a reservation older than
        // IdempotencyReclamation.StaleAfter.
        Activity.Current?.AddException(releaseFailure);
    }
}
```

### Why `Activity`, not `ILogger`

`IdempotentRequest` is a static class with no logger and no DI. The obvious
alternative — thread `ILogger` through `IdempotentExecution` — costs **ten
endpoint edits across seven contexts**, because every call site builds that
record inside an `[AsParameters]` service record, and **no endpoint service
record in this repository injects a logger today** (grepped: zero
`[FromServices] ILogger` in `src/*/Api/`). That is a wide, novel diff across
seven contexts to observe a path that runs only when the database is already
failing — and `spec.md` §Out of scope forbids touching the call sites.

`Activity.Current?.AddException(...)` needs no DI, adds an exception event to
the span that already represents this request, and reaches the same OTLP sink
ADR-0050 mandates. It is not a new pattern: `JourneyOrigin.cs:56` already
reports failures through `Activity` in this very project. `AddException` is
available on `net10.0` (added in .NET 9). The null-conditional covers the
unsampled and untraced cases, which is exactly how `JourneyOrigin` handles the
same absence with its `InertJourney`.

### Testing US2

Unit level, in the existing `IdempotentRequestTests`, because `RecordingStore`
is already the right shape — it just needs a mode where `ReleaseAsync` throws.

- **Red:** work throws `InvalidOperationException`, store's `ReleaseAsync`
  throws `InvalidOperationException` with a distinguishable message; assert the
  escaping exception is **the work's**. Today the release's exception escapes,
  so this fails.
- **The activity assertion needs a listener.** `Activity.Current` is null in a
  bare xUnit test, so the assertion would vacuously pass — another assertion
  that cannot fail. The test must start an `ActivitySource` with an
  `ActivityListener` whose `Sample` returns `AllData`, start an activity around
  the call, and assert the exception event landed on it. If that is judged
  disproportionate, assert only the escaping exception and say so — but do not
  write an unlistened `Activity.Current` assertion.
- **Characterisation, unmodified:**
  `A_failed_attempt_releases_its_key_so_a_retry_can_claim_it` and
  `A_cancelled_attempt_still_releases_its_key` must pass with no edit. An
  assertion that has to be adjusted is evidence the behaviour moved; block
  rather than adjust.

## US3 — the sweep, and the index that finally has a reader

### Shape

`src/ServiceDefaults/Idempotency/IdempotencyReservationSweep.cs` holds three
things, mirroring `AuditRetentionHostedService` file-for-file:

```csharp
public sealed class IdempotencyReservationSweepOptions
{
    public const string SectionName = "Idempotency:ReservationSweep";

    public TimeSpan TickInterval { get; set; } = TimeSpan.FromHours(1);
}

public sealed class IdempotencyReservationSweepHostedService<TDbContext>(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<IdempotencyReservationSweepOptions> options,
    ILogger<IdempotencyReservationSweepHostedService<TDbContext>> logger)
    : BackgroundService
    where TDbContext : DbContext
```

- `ExecuteAsync` runs once at startup then loops a `PeriodicTimer` over
  `timeProvider`, catching `OperationCanceledException` for shutdown —
  `AuditRetentionHostedService.cs:38-58`, unchanged in shape.
- `RunOnceAsync` is **public**, for the same stated reason: tests drive the
  sweep without spinning a timer.
- The hosted service is a singleton and `DbContext` is scoped, so `RunOnceAsync`
  resolves it inside an `IServiceScopeFactory.CreateScope()` — the mistake
  `AuditRetentionHostedService.cs:70-73` documents.

The statement:

```sql
DELETE FROM idempotency_key
 WHERE resource_identifier IS NULL
   AND reserved_at < NOW() - {0};
```

Same two guards as US1, same `IdempotencyReclamation.StaleAfter`. The SQL is
written twice — once as a conflict action, once as a delete — because they are
genuinely different statements; what must not be written twice is the bound,
and it is not.

**US1 and US3 cannot race destructively.** Both take a row lock on the same
row. If the `DELETE` wins, the reclaimer's `INSERT` succeeds as a first
arrival; if the `UPDATE` wins, `reserved_at` is `NOW()` and the `DELETE`'s
predicate no longer matches. Either order leaves one owner and one row.

### Registration

An extension beside the store's, so the two lines sit together:

```csharp
builder.Services.AddScoped<IIdempotencyStore, IdempotencyStore<CameraCatalogDbContext>>();
builder.Services.AddIdempotencyReservationSweep<CameraCatalogDbContext>();
```

Seven files, one line each, disjoint — `[P]` in `tasks.md`. The seven are the
contexts already registering the store: Automation, CameraCatalog,
EventIngestion, Identity, LayoutComposition, OverlayDesigner, SystemVariables.
**AuditObservability and StreamDistribution register no store and get no
sweep** — adding one there would create a worker for a table that does not
exist.

### Logging

One `[LoggerMessage]` in `src/ServiceDefaults/Log.cs`, at **Information**, and
only when the count is non-zero. A swept reservation means an attempt died
somewhere; that is worth a line an operator can grep, and at the expected rate
(zero, most hours) it is not noise. A zero-count sweep logs nothing —
`AuditRetentionHostedService` logs its no-op at Debug, but this one runs hourly
in seven services and a Debug line x 7 x 24 buys nothing.

### Testing US3

Integration, in the same new file as US1, driving `RunOnceAsync` directly on a
resolved instance rather than waiting for a tick. Three rows seeded by the same
`UPDATE`-a-real-row recipe: stale-unfinished (must vanish), aged-completed
(must survive **with its identifier**), fresh-unfinished (must survive). The
middle one is the ADR-0142 guard again and is not optional.

## Alternatives considered

- **A sweep only, no inline reclaim** — the literal reading of the issue's
  "Done looks like". Rejected as the whole answer: recovery would then depend
  on a background tick, so the wedged client's wait is the bound *plus* up to
  one interval, and a service whose sweep worker failed to start would silently
  keep the defect. The inline reclaim is deterministic, needs no worker, and
  fixes the row at the moment someone actually cares about it.
- **An inline reclaim only, no sweep.** Genuinely defensible, and it is why US3
  is P3 and separately shippable. Rejected as the final state for two reasons:
  reservations under keys nobody retries would still grow without bound, and
  `ix_idempotency_key_reserved_at` would remain a zero-reader index — the exact
  signal that found this defect, left in place for the next reviewer to find
  again.
- **Read-then-delete-then-insert in `BeginAsync`** instead of `ON CONFLICT DO
  UPDATE`. Rejected for the reason `IdempotencyStore.cs:22-27` already gives
  about read-then-write: it races the request pair the mechanism exists for.
  Three statements where one will do, and the window between them is the bug.
- **A `TimeProvider`-driven bound** so tests could shorten it. Rejected: the
  comparison must happen in the database, and §*Testability* shows the test
  needs no seam.
- **Threading `ILogger` through `IdempotentExecution`** for US2 — see §*Why
  `Activity`, not `ILogger`*.
- **Deleting old completed keys as well.** Forbidden by ADR-0142
  §Consequences; `spec.md` §*Why this is not a TTL* records the distinction.

## Review focus for phase 6

1. **Does any statement touch a row with a non-null `resource_identifier`?**
   Both new statements must carry the guard. This is the one way this spec
   could turn a fix into a silent data-loss regression.
2. **Is `SET reserved_at = NOW()` present?** Without it, concurrent reclaimers
   both run the work.
3. **Does the red test derive `caller` from the system rather than a literal?**
   A hand-written subject string makes the whole suite vacuous.
4. **Do the existing `IdempotentRequestTests` cases pass unmodified?**
5. **Did anything edit an endpoint file, `IdempotencyScope`, or a migration?**
   All three are out of scope.
