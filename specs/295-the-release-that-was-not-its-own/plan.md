# Plan — Spec 295, the release that was not its own

**Feature:** #2491 · **Spec:** `spec.md` · **Phase-4a colour:** RED

## Context and layers

**No bounded context is involved.** Everything lives in `src/ServiceDefaults/Idempotency/`,
the shared infrastructure every context's `Infrastructure` and `Api` projects reference — the
same place spec 201 worked. This settles four house rules up front, exactly as spec 201's plan
did:

- **Constitution §II is not engaged.** No domain model is touched; `PrimitiveBoundaryTests`
  scans Domain projects. The token's `DateTime` sits in ServiceDefaults beside
  `IdempotencyScope`'s `string Endpoint`.
- **ADR-0141 (`Option<T>`) is not engaged** — its scope is Domain and Application. The new
  optional field on `IdempotencyReservation` uses `Option<T>` anyway, because the record's
  existing optional field (`ResourceIdentifier`) already does; mirroring beats mixing.
- **No cross-context reference, no `Shared.Contracts` change**, no integration-event version,
  no consumer migration.
- **No migration.** The token is `reserved_at`, which exists, is `NOT NULL` and is written by
  every claim.

| File | Change |
|---|---|
| `src/ServiceDefaults/Idempotency/IIdempotencyStore.cs` | New `IdempotencyClaim` record; `IdempotencyReservation` carries `Option<IdempotencyClaim>`; `Reserved` singleton → `ReservedAs(claim)`; `CompleteAsync` / `ReleaseAsync` take the claim instead of the scope |
| `src/ServiceDefaults/Idempotency/IdempotencyStore.cs` | Claim statement gains `RETURNING reserved_at`; `CompleteAsync` and `ReleaseAsync` gain `AND reserved_at = {token}` |
| `src/ServiceDefaults/Idempotency/IdempotentRequest.cs` | Threads the claim from `ClaimAsync` to `RunAndRecordAsync` / `ReleaseQuietlyAsync` in place of the scope |
| `tests/ServiceDefaults.Tests/Idempotency/IdempotentRequestTests.cs` | `RecordingStore` fake's member signatures and `Next` default only — **no test body changes** |
| `tests/Integration.Tests/ServiceDefaults/IdempotencyFencingIntegrationTests.cs` | **New.** The red suite and its guards |
| `tests/Integration.Tests/ci-shards/shard-4.filter` | One entry for the new class, beside its siblings |
| `specs/201-a-reservation-that-lets-go/spec.md` | US2: the corrected sentence and a dated pointer here |

**Not touched, and a task that proposes touching one has gone wrong:** the 11 endpoint files,
`IdempotencyScope`, `IdempotencyKey`, `IdempotencyHeaders`, `IdempotencyKeyTable`,
`IdempotencyReclamation`, `IdempotencyReservationSweep.cs`, the seven
`*InfrastructureModule.cs` files, every migration, and every existing *assertion* in
`IdempotentRequestTests` and `StaleIdempotencyReservationIntegrationTests`.

## The token

### Why `reserved_at`, not a new column

Every successful claim writes `reserved_at = NOW()` — the insert and the reclaiming conflict
action alike (`IdempotencyStore.cs:57-62`). So `reserved_at` already *is* a per-claim value:

- **It changes on every takeover.** A reclaim re-stamps it; a sweep deletes the row and the next
  insert stamps a new one. The old holder's value can never match the new holder's row.
- **It cannot collide.** A takeover requires the old value to be at least
  `IdempotencyReclamation.StaleAfter` (10 minutes) older than `NOW()`, so the new value differs
  by ≥ 10 minutes, far above `timestamptz`'s microsecond resolution. For a sweep-then-insert the
  same bound holds (the swept row was ≥ 10 minutes old).
- **It costs nothing.** No column, no DDL change in `IdempotencyKeyTable`, no new migration in
  seven context schemas.

**Rejected: a `reservation_token UUID` column.** It would be *more obviously* unique and is the
textbook fencing token, but it buys nothing the bound does not already guarantee, and it costs
seven migrations plus a `IdempotencyKeyTable` change — a schema change in every context for a
race that is implausible by design. If `reserved_at` ever stops being written by every claim, this
reasoning breaks; the `IdempotencyClaim` doc comment must say so.

### Round-trip exactness — the one thing that can make the fence match nothing

The fence compares a value **read back** from Postgres with the stored one. That is exact only
if the CLR type preserves microseconds and the parameter is sent as `timestamptz`:

- `timestamptz` is microsecond-resolution; `DateTime` is 100 ns, so every stored value is
  representable.
- Npgsql (6+) reads `timestamptz` as `DateTime` with `Kind = Utc`, and writes a `Utc` `DateTime`
  as `timestamptz`. The token is only ever a value Npgsql handed back, so its `Kind` is right by
  construction. **Never build a token from `DateTime.UtcNow` or the injected `TimeProvider`** —
  a client clock value would match nothing.

If this assumption is wrong for the pinned Npgsql, the **G1/G2 guards fail** (spec §US1 last two
scenarios): every own-release and own-completion would silently no-op. That is precisely why the
guards exist, and why phase-5 half A exercises the real service.

## The shape

### `IdempotencyClaim` — scope and token travel together

```csharp
/// The reservation this request won — the only thing entitled to complete or release it.
public sealed record IdempotencyClaim(IdempotencyScope Scope, DateTime ReservedAt);
```

**Why a record carrying the scope, rather than a token parameter beside it.** The two are never
meaningful apart: a token with the wrong scope matches nothing, and the whole defect is a scope
used without its token. Bundling them makes the unfenced call impossible to write. It also keeps
parameter lists at their current length (ADR-0084): `RunAndRecordAsync` would otherwise grow to
five parameters.

```csharp
public sealed record IdempotencyReservation(
    IdempotencyOutcome Outcome, Option<Guid> ResourceIdentifier, Option<IdempotencyClaim> Claim)
{
    public static IdempotencyReservation ReservedAs(IdempotencyClaim claim) => …;   // replaces the Reserved singleton
    public static IdempotencyReservation InProgress { get; } = …;                    // unchanged meaning
    public static IdempotencyReservation CompletedWith(Guid resourceIdentifier) => …; // unchanged meaning
}

public interface IIdempotencyStore
{
    Task<IdempotencyReservation> BeginAsync(IdempotencyScope scope, CancellationToken cancellationToken); // unchanged
    Task CompleteAsync(IdempotencyClaim claim, Guid resourceIdentifier, CancellationToken cancellationToken);
    Task ReleaseAsync(IdempotencyClaim claim, CancellationToken cancellationToken);
}
```

`Reserved` becomes a factory because a singleton cannot carry a per-claim value — that singleton
is, structurally, the defect.

### `BeginAsync` — the claim returns what it wrote

```sql
INSERT INTO idempotency_key (key, endpoint, caller, reserved_at)
VALUES ({0}, {1}, {2}, NOW())
ON CONFLICT (key, endpoint, caller) DO UPDATE
   SET reserved_at = NOW()
 WHERE idempotency_key.resource_identifier IS NULL
   AND idempotency_key.reserved_at < NOW() - {3}
RETURNING reserved_at AS "Value";
```

Executed through `dbContext.Database.SqlQueryRaw<DateTime>(…).ToArrayAsync(…)` instead of
`ExecuteSqlRawAsync`. One row → `ReservedAs(new IdempotencyClaim(scope, rows[0]))`; zero rows →
the existing fall-through (`SELECT`, `InProgress`, `CompletedWith`) **unchanged**. Postgres
returns no row when the conflict action's `WHERE` excludes the update, which is exactly the
`inserted == 0` case today. Still one statement, one round trip.

**Verify at 4b, do not assume:** that EF sends a non-`SELECT` `SqlQueryRaw` without composing
over it. The existing fall-through already uses `SqlQueryRaw<Guid?>` with `AS "Value"` and
`ToArrayAsync()` and no LINQ operator, which is the non-composing path; the claim must be
consumed the same way (no `.Single()`, `.FirstOrDefault()` or other operator EF could push into
SQL). If EF composes it anyway, fall back to a `DbCommand` from
`dbContext.Database.GetDbConnection()` — and say so in the PR. The existing
`Two_concurrent_reclaims_of_one_stale_reservation_only_one_wins` integration test is the
characterisation that the claim's atomicity survived the rewrite.

### `CompleteAsync` and `ReleaseAsync` — one predicate each

```sql
UPDATE idempotency_key
SET resource_identifier = {3}, completed_at = NOW()
WHERE key = {0} AND endpoint = {1} AND caller = {2}
  AND resource_identifier IS NULL AND reserved_at = {4};

DELETE FROM idempotency_key
WHERE key = {0} AND endpoint = {1} AND caller = {2}
  AND resource_identifier IS NULL AND reserved_at = {3};
```

Parameters from `claim.Scope` and `claim.ReservedAt`. **The existing `resource_identifier IS
NULL` guards stay.** With the fence they are nearly redundant; removing them is a refactor, not
this fix (ADR-0036), and they cost nothing.

The comment above each statement replaces "a release arriving late cannot delete a reservation
that has since completed" / #2290's paragraph with the full reason: the token is what makes a
late call from an *overtaken* attempt a no-op, whether the new holder has completed or not — the
case #2290's guard could not see.

### `IdempotentRequest` — thread the claim

- `ExecuteAsync`: after the replay and in-progress branches, the remaining outcome is `Reserved`,
  so `reservation.Claim.Value` is present; pass it to `RunAndRecordAsync` in place of `scope`.
- `RunAndRecordAsync(execution, claim, work, cancellationToken)` — four parameters, as today.
- `ReleaseQuietlyAsync(store, claim)`; the success-path `CompleteAsync` / `ReleaseAsync` calls
  take `claim`.
- **No other change.** The `CancellationToken.None` comments, `RecordQuietlyAsync`'s swallowing
  and its stated case, the poll loop — all untouched. `RecordQuietlyAsync`'s comment about a
  failed `CompleteAsync` stays true.

## Testing

### Why an integration suite, and why through `IdempotentRequest`

The fix is two `WHERE` predicates and one `RETURNING`; a fake store proves nothing about any of
them (spec 201's reasoning, unchanged). Going through `IdempotentRequest.ExecuteAsync` rather than
calling the store directly additionally proves the **threading**: a claim that was not the one
`BeginAsync` returned makes the G1/G2 guards fail.

### The recipe: overtake A from inside A's `work()`

A slow attempt can only be overtaken while its `work()` is running, so that is where the test
does it. New class `IdempotencyFencingIntegrationTests` in `tests/Integration.Tests/ServiceDefaults/`,
`[Collection(AspireCollection.Name)]`, `AspireFixture` by primary constructor:

1. Build a scope with a run-unique key (`$"fence-{Guid.CreateVersion7():N}"`), an endpoint label
   no real route uses (e.g. `"TEST /idempotency-fencing"`) and a fixed caller label. **A literal
   caller is correct here, unlike spec 201's endpoint tests:** no HTTP request derives the scope;
   the test hands the same scope to every store call itself, so there is no second derivation to
   disagree with. Say so in a comment, or a reviewer will flag it by precedent.
2. Store A = `new IdempotencyStore<CameraCatalogDbContext>(dbA)`, store B on a second context
   (`aspire.CreateCameraCatalogDbContextAsync()` each) — two requests, two connections.
3. Run `IdempotentRequest.ExecuteAsync(new IdempotentExecution(Some(scope), storeA, TimeProvider.System), work, replay, ct)`
   where `work` does the takeover, then throws or returns:
   - age A's row: `UPDATE idempotency_key SET reserved_at = NOW() - INTERVAL '30 minutes' WHERE key = {0} AND endpoint = {1}`;
   - `storeB.BeginAsync(scope)` → assert `Reserved`, keep `claimB`;
   - then `throw` (release scenarios) or `return IdempotentOutcome.Created(ra, …)` (completion
     scenarios).
4. Assert on the row, read with the same `SqlQueryRaw` shape
   `StaleIdempotencyReservationIntegrationTests` uses.

| Test | Colour at 4a | Asserts |
|---|---|---|
| `A_reclaimed_attempt_that_fails_does_not_release_the_new_holders_reservation` | **red** | row exists, unfinished, `reserved_at == claimB.ReservedAt` |
| `A_reclaimed_attempt_that_succeeds_does_not_complete_the_new_holders_reservation` | **red** | after `storeB.CompleteAsync(claimB, rb)`, row carries `rb` (today: `ra`) |
| `A_swept_attempt_that_fails_does_not_release_the_next_arrivals_reservation` | **red** | takeover via the real sweep's `RunOnceAsync` then `storeC.BeginAsync`; row exists with C's `reserved_at` |
| `An_attempt_nobody_overtook_still_releases_its_own_reservation` | green, guard | no row remains |
| `An_attempt_nobody_overtook_still_completes_its_own_reservation` | green, guard | row carries `ra` |
| `A_late_completion_after_the_new_holder_completed_is_still_a_no_op` | green, characterisation | row carries `rb` |

The sweep test reuses `StaleIdempotencyReservationIntegrationTests.CreateSweepAsync`'s shape
(copy the helper; do not make that file's private helper shared — a cross-file refactor is out of
scope). The sweep deletes *every* stale unfinished row in `camera-catalog-db`; that is safe
because the Aspire collection runs its classes sequentially, which is the same assumption the
existing sweep tests make.

**The red three fail on the defect, not on a missing token.** At 4a the claim is a placeholder
(see *Commit order*), so `claimB.ReservedAt` is meaningless — but each red test's **first**
assertion is the one the defect breaks (row missing; row carries `ra`). Order the assertions so;
a red caused only by a placeholder comparison is the wrong red.

### Characterisation — unmodified

These run green before the shape change and after every commit, **with no assertion edited**:

- `tests/ServiceDefaults.Tests/Idempotency/IdempotentRequestTests.cs` — every case. Only
  `RecordingStore`'s member signatures and its `Next` default change (it must construct
  `ReservedAs(new IdempotencyClaim(scope, …))`); that is a compile consequence of the interface,
  not a behaviour edit, and the diff must show no test method body changed.
- `StaleIdempotencyReservationIntegrationTests` — all eight cases, especially
  `Two_concurrent_reclaims_of_one_stale_reservation_only_one_wins` (the claim rewrite's
  atomicity) and `A_completed_key_is_replayed_however_old_it_is` (ADR-0142's guard).
- `IdempotentCameraRegistrationIntegrationTests`, `IdempotentRegistrationIntegrationTests`,
  `EventTypeRegistryIdempotencyIntegrationTests` — the endpoint-level replay suites, which fail
  if a real service's completion stops matching.

### Commit order — each commit builds on its own

1. **Shape (behaviour-preserving).** New types and signatures; `IdempotentRequest` threads the
   claim; `RecordingStore` follows. `BeginAsync` returns `ReservedAs(new IdempotencyClaim(scope,
   default))` from the *unchanged* `ExecuteSqlRawAsync` path; `CompleteAsync`/`ReleaseAsync`
   accept the claim and use only `claim.Scope`. **No SQL text changes.** Characterisation suites
   green, unmodified.
2. **Red tests** — the new integration class and its shard entry. The three red cases fail on the
   defect; the three others pass.
3. **Real token** — `BeginAsync` uses `RETURNING`. Characterisation still green; red still red.
4. **Fence** — `AND reserved_at = {…}` on both statements. Everything green.
5. **US2 docs** — any position; touches only `specs/201-…/spec.md`.

The placeholder `default` exists for exactly one commit and must not survive commit 3; the
phase-6 reviewer checks for it by name.

## US2 — correcting spec 201

In `specs/201-a-reservation-that-lets-go/spec.md` §US1, the paragraph *"A reclaim is not a
promise about the dead attempt's side effects"*: replace the clause claiming the domain's
uniqueness rules "answer `409` for a genuine duplicate" with the accurate limit — some endpoints
refuse a re-run as a duplicate (a camera name, for instance), **others do not**, and name
`POST /events` with its citation (`EventsEndpoints.Writes.cs:94-97`). Add a one-line dated note:
*Corrected 2026-09-30 by spec 295 (#2491).* Do **not** enumerate the other ten endpoints'
behaviour — nobody has verified them, and an unverified list is the drift this repository keeps
having to correct.

## Alternatives considered

- **A token parameter beside the scope** (`CompleteAsync(scope, token, id, ct)`) — the issue's
  first suggestion. Rejected: a scope and a token are never meaningful apart, the parameter lists
  grow (`RunAndRecordAsync` to five), and the unfenced call remains writable.
- **A `reservation_token UUID` column** — see §*Why `reserved_at`*.
- **Compare with `>=` / a range instead of equality** — no. Equality is the fence; anything looser
  re-admits the older holder.
- **Detect-and-report instead of fence** (read `reserved_at`, compare in C#, log). Rejected: a
  read-then-write races the thing it detects, which is the argument `IdempotencyStore.cs:22-27`
  already makes.
- **Bound `work()` with a request timeout instead**, so the "merely slow" premise cannot occur.
  Rejected for this slice: a timeout cancels the *request*, not already-committed side effects,
  and it would be a new cross-cutting runtime policy — that would want an ADR. The fence is local
  and needs none.

## Review focus for phase 6

1. **Do both `CompleteAsync` and `ReleaseAsync` carry `AND reserved_at = {token}`**, and does
   each still carry `resource_identifier IS NULL`?
2. **Is the token only ever a value `BeginAsync` read back from Postgres?** Any `DateTime.UtcNow`,
   `TimeProvider` or `default` reaching a store call is a fence that matches nothing. The commit-1
   placeholder must be gone.
3. **Is `BeginAsync` still one statement**, consumed without a LINQ operator EF could compose?
4. **Did any existing assertion change?** `RecordingStore`'s signatures may; test bodies may not.
5. **Did anything touch an endpoint file, `IdempotencyScope`, `IdempotencyKeyTable`, the sweep, or
   a migration?** All out of scope.
6. **Does the new class carry its shard-filter entry?** A missing entry fails deterministically.
