# Plan — Spec 454, a replay refuses a client that has changed since its key was answered

**Issue:** #3096 · **Spec:** `spec.md` · **Tasks:** `tasks.md`
**Engineer:** backend (`backend-engineer`). No frontend, no AppHost change; migrations run through
the existing `MigrationRunner` (ADR-0067). **Reviewers:** `backend-reviewer`, `security-reviewer`
(mandatory).

## Context and layers

A **ServiceDefaults** extension (record an optional answered version with a completed key, hand
it to the replay), seven raw-SQL migrations (the store's SQL is shared, so every context that
registers `IdempotencyStore<T>` needs the column — the spec 302 precedent), and an **Identity**
change in Application (replay query decides "superseded"), Infrastructure (a no-tracking version
read) and Api (the rotate endpoint records and passes the version).

| Layer | Change |
|---|---|
| `src/ServiceDefaults/Idempotency/` | `IdempotentOutcome` + `IdempotencyReservation` carry `Option<AggregateVersion> ResourceVersion`; `IIdempotencyStore.CompleteAsync` overload with a version; store writes/reads `resource_version`; `IdempotentRequest.ExecuteAsync` overload whose replay receives `IdempotentReplay`; `IdempotencyKeyTable.AddResourceVersion` / `DropResourceVersion` |
| 7 x `src/<Context>/Infrastructure/Persistence/Migrations/` | `<ts>_AddIdempotencyResourceVersion.cs` (+ `.Designer.cs`): Automation, CameraCatalog, EventIngestion, Identity, LayoutComposition, OverlayDesigner, SystemVariables |
| `src/Identity/Application/Queries/` | `ReplayRegisteredClientQuery` gains `Option<AggregateVersion> AnsweredAt`; new `ReplayedKeySuperseded` error; handler checks before and after the Keycloak read |
| `src/Identity/Domain/RegisteredClient/IRegisteredClientRepository.cs` + Infrastructure | `GetVersionAsync(RegisteredClientIdentifier, CancellationToken) : Task<Option<AggregateVersion>>`, `AsNoTracking` |
| `src/Identity/Api/WebhookRotationEndpoints.cs` | record `dto.Version` on completion; versioned replay overload; NULL stored version → superseded |

Devices and kiosks (`DevicesEndpoints.cs`, `KiosksEndpoints.cs`) are **not edited**; the old
`ExecuteAsync(…, Func<Guid, …>)` overload and `AnsweredAt = None` keep their behaviour.

## 1. ServiceDefaults — the marker travels with the key

Policy-free: ServiceDefaults stores and returns the version; Identity decides what a mismatch
means.

```csharp
public sealed record IdempotentOutcome(Option<Guid> ResourceIdentifier, IResult Response)
{
    public Option<AggregateVersion> ResourceVersion { get; init; } = Option<AggregateVersion>.None;
    public static IdempotentOutcome Created(Guid id, IResult response);                    // unchanged
    public static IdempotentOutcome CreatedAt(Guid id, AggregateVersion version, IResult response);
}

public sealed record IdempotentReplay(Guid ResourceIdentifier, Option<AggregateVersion> ResourceVersion);

// IdempotentRequest — new overload; the Guid overload forwards to it with replay.ResourceIdentifier.
public static Task<IResult> ExecuteAsync(
    IdempotentExecution execution,
    Func<CancellationToken, Task<IdempotentOutcome>> work,
    Func<IdempotentReplay, CancellationToken, Task<IResult>> replay,
    CancellationToken cancellationToken);
```

- `IdempotencyReservation` gains a non-positional `Option<AggregateVersion> ResourceVersion
  { get; init; }` (default `None`) and `CompletedWith(Guid, Option<AggregateVersion>)`; the
  one-argument factory stays. Source-compatible: no existing construction changes.
- `IIdempotencyStore` gains `CompleteAsync(IdempotencyClaim, Guid, AggregateVersion,
  CancellationToken)`. `RunAndRecordAsync` calls it when `outcome.ResourceVersion` has a value,
  the existing overload otherwise. Both share one SQL statement with a typed nullable
  `NpgsqlParameter` (`NpgsqlDbType.Integer`, `DBNull` for none — the spec 302 lesson: an untyped
  null cannot be inferred). Fence and guards (`resource_identifier IS NULL AND reserved_at = …`)
  unchanged.
- `ReadBindingSql` selects `resource_version`; `Classify` passes it into `CompletedWith`. The
  Mismatched check still runs first (spec 302 I2) — a superseded answer is never reachable for a
  request bound to a different key.
- `IdempotencyKeyTable.AddResourceVersion`: `ALTER TABLE idempotency_key ADD COLUMN
  resource_version INTEGER NULL;` and the inverse `DROP COLUMN IF EXISTS`. `Create` is not widened
  (seven historical migrations call it — the reason spec 302 gave). Nullable for legacy rows and
  for every endpoint that does not record one.

**Invariants**

- **I1** A completed row's `resource_version`, when present, is the version **the original
  answer carried** (`dto.Version`), written in the same fenced `UPDATE` as `resource_identifier`.
- **I2** The store never compares versions. Only the replay that opted in reads it.
- **I3** Spec 302's I1–I4 are unchanged: binding checked before completion state; fence on
  `reserved_at`.

## 2. Identity Application — the replay decides

```csharp
public sealed record ReplayRegisteredClientQuery(
    RegisteredClientIdentifier Client,
    Option<AggregateVersion> AnsweredAt);          // None: unbound (devices, kiosks)

public sealed record ReplayedKeySuperseded(Guid Client)
    : ReplayRegistrationError(
        "IDEMPOTENCY_KEY_SUPERSEDED",
        "The client this Idempotency-Key was answered for has changed since, so that answer can no longer be replayed. Send a new key for a new request.",
        HttpStatusCode.Conflict);
```

Handler order (destructure first — two fields):

1. Row lookup → `ReplayedRegistrationMissing` if absent (unchanged).
2. If `AnsweredAt` is `Some(v)` and `row.Version != v` → `ReplayedKeySuperseded`. **Keycloak is
   not called.**
3. `ReadClientSecretAsync` → `ReplayedClientMissingInKeycloak` on not-found (unchanged).
4. If `AnsweredAt` is `Some(v)`: `clients.GetVersionAsync(id)` (no tracking — the tracked entity
   from step 1 would hand back its stale `Version` through identity resolution). Absent or
   `!= v` → `ReplayedKeySuperseded`; the secret from step 3 is dropped, never returned or logged.
5. Success as today.

**Why step 4 is sound:** a rotation commits the version bump (`SaveAsync` → `CommitAsync`) before
`RotateClientSecretAsync` (`RollSecretAsync`). If step 3 read a secret newer than the answer,
the bump committed before that read, so step 4 sees it. Step 2 alone would leave a window between
the check and the read. Step 2 is kept so a superseded key does not read a secret at all.

Error code: **409**, matching its two sibling replay refusals (`REPLAYED_REGISTRATION_MISSING`,
`REPLAYED_CLIENT_MISSING`) and the 409 the rotate route already declares. The detail names no
client, no version and no fab. The resilience handler does not retry 4xx (ADR-0143), so the
refusal is final. Rewrite the handler's and `ReplayedClientDto`'s *"What a replay does not
promise"* docs: unbound replays still return current state; bound replays refuse instead.

## 3. Identity Infrastructure

`RegisteredClientRepository.GetVersionAsync`: `AsNoTracking().Where(id).Select(c => c.Version)`
→ `Option<AggregateVersion>`. Identity migration `<ts>_AddIdempotencyResourceVersion` calls the
helper (mirror `20261006125600_AddIdempotencyRequestBinding.cs` exactly, including its
`ArgumentNullException.ThrowIfNull` — generated migrations are exempt from ADR-0105). The model
snapshot is unchanged (raw SQL; no EF entity).

## 4. Identity Api — `WebhookRotationEndpoints.cs`

- `RotationOutcome`: `IdempotentOutcome.CreatedAt(dto.RegisteredClientIdentifier,
  AggregateVersion.From(dto.Version), Results.Ok(dto))`.
- Call the versioned `ExecuteAsync` overload. `ReplayRotationAsync(IdempotentReplay replayed, …)`:
  `ResourceVersion` `None` (a row completed before this migration) → `new
  ReplayedKeySuperseded(id).ToProblem()` without calling the query (fail closed, as spec 302 I3
  does for a NULL fingerprint); `Some(v)` → query with `AnsweredAt = Some(v)`.
- No new `ProducesProblem`: 409 is declared. The route-table baseline must not move.

## 5. Messaging and boundaries

No domain or integration event (owner ruling). No `Shared.Contracts` change. No cross-context
reference: `AggregateVersion` lives in `Shared.Kernel`, already referenced by ServiceDefaults.
The six non-Identity contexts get a column they always leave NULL.

## 6. Security notes for phase 6

- No secret, hash or response body is stored; the new column is an integer.
- A superseded replay reads no secret (step 2) or discards it (step 4); verify no log line or
  exception message carries it.
- Spec 302's scope (caller, fab, fingerprint) is checked before any of this; the 422 order is
  unchanged.
- Residual: an unbound device/kiosk replay of a later-disabled client still returns its secret —
  #3123, explicitly out of scope.

## 7. Test plan (phase 4a, red first)

| Level | File | Cases |
|---|---|---|
| Unit (Application) | `tests/Identity.Application.Tests/Queries/ReplayRegisteredClientQueryHandlerSupersededTests.cs` (new) | bound + equal → success; bound + row moved → 409 superseded, Keycloak never called; bound + version moves between read and re-check → superseded, secret absent from the result; bound + row vanishes at re-check → superseded |
| Unit (ServiceDefaults) | `tests/ServiceDefaults.Tests/Idempotency/IdempotentResourceVersionTests.cs` (new) | `CreatedAt` completes with the version; versioned replay receives the stored version; `Created` completes without one; Guid overload unchanged |
| Endpoint (Architecture host) | `tests/Architecture.Tests/WebhookRotationReplaySupersededEndpointTests.cs` (new) | stored `None` → 409 superseded without the replay query; stored `Some` → query receives it |
| Integration (Aspire) | `tests/Integration.Tests/Identity/WebhookRotationReplaySupersededIntegrationTests.cs` (new) | spec §4 end to end; legacy row (`UPDATE … SET resource_version = NULL`) → 409; K2 still replays; body has no secret |
| Integration (store) | `tests/Integration.Tests/ServiceDefaults/IdempotencyResourceVersionStoreIntegrationTests.cs` (new) | version round-trips; absent version stays NULL; fence still rejects a late `CompleteAsync` with a version |

Every new integration class needs an entry in a `tests/Integration.Tests/ci-shards/shard-N.filter`
(a missing entry fails deterministically).
