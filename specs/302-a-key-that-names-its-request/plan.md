# Plan — Spec 302, a key that names its request

**Issues:** #2424, #2492 · **Spec:** `spec.md` · **Tasks:** `tasks.md`
**Engineer:** backend (`backend-engineer`). No frontend, no AppHost/infra change — the migrations run through the existing `MigrationRunner` (ADR-0067).

## Context and layers

This is a **ServiceDefaults** change plus a one-line-per-site adoption in seven bounded
contexts' **Api** layers and one raw-SQL migration per context's **Infrastructure**. No
Domain or Application code changes; no `Shared.Contracts` change; no cross-context reference
(the helper lives in `ServiceDefaults`, which every Api already references).

| Layer | Change |
|---|---|
| `src/ServiceDefaults/Idempotency/` | new `IdempotencyFingerprint`; widen `IdempotencyScope`; `IdempotencyOutcome.Mismatched` + `IdempotencyReservation.Mismatched`; store writes/compares the binding; `IdempotentRequest` answers 422; `IdempotencyHeaders.ReusedErrorCode`; `IdempotencyKeyTable.AddRequestBinding` / `DropRequestBinding` |
| 7 x `src/<Context>/Infrastructure/Persistence/Migrations/` | `<ts>_AddIdempotencyRequestBinding.cs` (+ `.Designer.cs`) |
| 7 contexts' `Api/` (12 endpoints) | pass resolved fab + fingerprint to `IdempotencyScope.For`; declare 422 on the mapping chain |
| `tests/Architecture.Tests/StatusProducerDeclarationTests.cs` | census row M16 + guard |

**No entities / aggregates.** The "invariants" are the store's:

- **I1** A row's `(key, endpoint, caller)` names exactly one request: the `(fab, request_fingerprint)` written by whoever last *claimed* it (first insert or stale reclaim).
- **I2** A non-claiming arrival is answered from the row only if its `(fab, fingerprint)` equals the row's; otherwise `Mismatched`, regardless of completed/in-progress.
- **I3** NULL `request_fingerprint` never equals anything (legacy rows fail closed).
- **I4** `CompleteAsync` / `ReleaseAsync` are unchanged: they are keyed on PK + the `reserved_at` fence (#2491), and only the claim holder — whose binding is by I1 the row's — calls them.

**Messaging:** none. No domain or integration event; the idempotency table is a transport concern (ADR-0142 §Alternatives, "Put the key on the aggregate" rejected).

**Boundary rules:** unchanged. Each context keeps its own `idempotency_key` table in its own schema (ADR-0142 §Decision.4); `ServiceDefaults` takes the fab as `Option<string>` because `FabIdentifier` is a per-context value object (eight copies, none shared) and `ServiceDefaults` must not reference any context. `IdempotencyScope` already carries `Endpoint`/`Caller` as strings — it is a transport record, outside constitution §II's domain-model scope.

## 1. `IdempotencyFingerprint` (new, `src/ServiceDefaults/Idempotency/IdempotencyFingerprint.cs`)

```csharp
public sealed class IdempotencyFingerprint : IEquatable<IdempotencyFingerprint>
{
    public const int Length = 64;                       // SHA-256, lowercase hex
    public string Value { get; }

    public static IdempotencyFingerprint Of<TRequest>(TRequest request, params ReadOnlySpan<string> boundValues)
        where TRequest : notnull;
    // Equals/GetHashCode ordinal on Value; ToString => Value
}
```

- Serialises `new Envelope<TRequest>(request, [.. boundValues])` with a **private, frozen**
  `JsonSerializerOptions` (not the app's HTTP options — an app-wide option change must not
  silently re-fingerprint live keys), static type `TRequest` (no polymorphic surprises), then
  `SHA256.HashData` → `Convert.ToHexStringLower`.
- **Why the bound DTO and not the raw body:** minimal APIs have already consumed the body
  stream; buffering every keyed request would need middleware. Re-serialising the *bound* DTO
  normalises whitespace and property order away, which is what "the same request" should mean.
  Collections and `JsonElement` (`IngestManualEventRequest.Payload`) serialise deterministically.
- **Why a JSON envelope for the bound values:** an array is unambiguous; naive concatenation makes
  `("ab","c")` equal `("a","bc")`.
- Called **after** the endpoint's own validation (every site already parses before
  `ExecuteAsync`), inside `key.Map(...)` — so unkeyed requests never hash anything.
- Nothing is reversible from the stored value; no body is stored (ADR-0142 §Decision.3 holds).

## 2. `IdempotencyScope` (widened)

```csharp
public sealed record IdempotencyScope(
    IdempotencyKey Key, string Endpoint, string Caller, Option<string> Fab, IdempotencyFingerprint Fingerprint)
{
    public static IdempotencyScope For(
        IdempotencyKey key, string endpoint, string caller, Option<string> fab, IdempotencyFingerprint fingerprint);
}
```

Five parameters exceeds ADR-0084's advisory 4. Accepted and stated in the doc comment: it is a
record of five independent dimensions, and bundling two of them into a sub-record would only
move the count. (ADR-0084 is advisory, `warning`, carved out of `TreatWarningsAsErrors`.)
`Ensure.That(fingerprint).IsNotNull()` (ADR-0105); a `Some` fab must be non-blank.

**No 3-argument overload is kept.** The required parameter is the compile-time guarantee that
every call site — and every future one — states its binding.

The doc comment gains a paragraph per new dimension, in the same voice as the `Caller` paragraph:
`Fab` (#2492 — one operator, two plants), `Fingerprint` (#2424 — one key, two resources), and
the note that both are **compared**, not **keyed**: a key names one request.

## 3. Schema — `IdempotencyKeyTable`

```csharp
public static void AddRequestBinding(MigrationBuilder migrationBuilder)
    // ALTER TABLE idempotency_key
    //     ADD COLUMN fab                 VARCHAR(64) NULL,
    //     ADD COLUMN request_fingerprint CHAR(64)    NULL;
public static void DropRequestBinding(MigrationBuilder migrationBuilder)
    // ALTER TABLE idempotency_key DROP COLUMN IF EXISTS request_fingerprint, DROP COLUMN IF EXISTS fab;
```

- `Create` is **not** edited: seven historical migrations call it; a widened `Create` plus this
  `ALTER` would fail on a fresh database with "column already exists".
- `fab VARCHAR(64)`: every context's `FabIdentifier.MaximumLength` is 32; 64 is headroom, not a guess at a new rule.
- Both nullable: `fab` because overlays have no fab; `request_fingerprint` only for legacy rows (I3). New rows always write it.
- No backfill, no index: comparisons happen after a PK lookup.

**Migrations** — one per context, named `AddIdempotencyRequestBinding`, generated with
`dotnet ef migrations add` (so the `.Designer.cs` carries the right `[DbContext]`/`[Migration]`
and an unchanged model snapshot — raw SQL does not touch the model), then `Up`/`Down` replaced
with the two helper calls, mirroring `20260903071655_AddIdempotencyKey.cs` (including its
`ArgumentNullException.ThrowIfNull`, which the ADR-0105 guard carves out for generated
migrations). Contexts: Automation, CameraCatalog, EventIngestion, Identity, LayoutComposition,
OverlayDesigner, SystemVariables. **Verify with `git diff --stat` on each `Migrations/` folder** —
a green build never boots a database.

## 4. Store — `IdempotencyStore<TDbContext>.BeginAsync`

Claim (one statement, unchanged atomicity — the #2290 / #2491 reasoning in the file still holds):

```sql
INSERT INTO idempotency_key (key, endpoint, caller, fab, request_fingerprint, reserved_at)
VALUES ({0}, {1}, {2}, {3}, {4}, NOW())
ON CONFLICT (key, endpoint, caller) DO UPDATE
   SET reserved_at = NOW(),
       fab = EXCLUDED.fab,
       request_fingerprint = EXCLUDED.request_fingerprint
 WHERE idempotency_key.resource_identifier IS NULL
   AND idempotency_key.reserved_at < NOW() - {5}
RETURNING reserved_at AS "Value";
```

- A stale reclaim **takes over the binding** (I1). The dead attempt never completed, so there is
  no answer to protect; if it is a zombie, the `reserved_at` fence already voids its late `CompleteAsync`.
- `{3}` may be null: pass a typed `NpgsqlParameter` (`NpgsqlDbType.Varchar`, `DBNull.Value` for
  `None`) — an untyped `null` in the raw-SQL parameter array cannot be type-inferred.

Fall-through read returns all three columns (`SqlQueryRaw<StoredBinding>` with a private
`sealed record StoredBinding(Guid? ResourceIdentifier, string? Fab, string? RequestFingerprint)`
and matching `AS "…"` aliases), then, **in this order**:

1. no row → `InProgress` (unchanged: holder released between insert and read);
2. `Fab` ≠ scope fab (ordinal; `None` ↔ NULL) **or** `RequestFingerprint` ≠ scope fingerprint
   (NULL never equal) → `IdempotencyReservation.Mismatched`;
3. identifier NULL → `InProgress`; else `CompletedWith(identifier)`.

Step 2 before step 3 is the point: an in-flight mismatched row must be refused at once, not
polled for five seconds and then replayed.

`CompleteAsync`, `ReleaseAsync`, the sweep: **no change** (I4).

## 5. `IdempotentRequest.ExecuteAsync`

After `ClaimAsync`:

```csharp
if (reservation.Outcome == IdempotencyOutcome.Mismatched)
{
    return Results.Problem(
        title: IdempotencyHeaders.ReusedErrorCode,          // "IDEMPOTENCY_KEY_REUSED"
        detail: "This Idempotency-Key was first used for a different request. Send a new key for a new request.",
        statusCode: StatusCodes.Status422UnprocessableEntity);
}
```

- Neither `work` nor `replay` runs; nothing is released or completed (the row belongs to the other request).
- `ClaimAsync`'s loop already exits on any non-`InProgress` outcome — no change, but the unit test pins it.
- The detail names no fab, identifier or name of the first request. (Same caller, so it would not
  be a cross-tenant leak; it is still the wrong place for it.)

## 6. Call sites (12) — exact arguments

`fab` is the **resolved** `FabIdentifier.Value` already in scope at every site (spec table).
The fingerprint is computed inside the existing `key.Map(supplied => …)` lambda.

| # | File | `Option<string> fab` | `IdempotencyFingerprint.Of(…)` |
|---|---|---|---|
| 1 | `Automation/Api/RulesEndpoints.cs` | `Some(fab.Value)` | `body` |
| 2 | `CameraCatalog/Api/CameraEndpoints.cs` | `Some(fab.Value)` | `request` |
| 3 | `EventIngestion/Api/EventsEndpoints.Writes.cs` | `Some(fab.Value)` | `body` |
| 4 | `EventIngestion/Api/EventSourcesEndpoints.cs` | `Some(fab.Value)` | `body` — **and the endpoint argument returns to `DeclareEndpoint`** |
| 5 | `EventIngestion/Api/EventTypesEndpoints.cs` | `Some(fab.Value)` | `body` |
| 6 | `Identity/Api/DevicesEndpoints.cs` | `Some(fab.Value)` | `body` |
| 7 | `Identity/Api/KiosksEndpoints.cs` | `Some(fab.Value)` | `body` |
| 8 | `Identity/Api/WebhookRotationEndpoints.cs` | `Some(fab.Value)` | `body, name, <precondition>` — `*` for `If-None-Match: *`, the invariant-culture version digits for `If-Match` |
| 9 | `LayoutComposition/Api/LayoutEndpoints.Commands.cs` | `Some(fab.Value)` | `body` |
| 10 | `LayoutComposition/Api/WallEndpoints.Commands.cs` | `Some(fab.Value)` | `body` |
| 11 | `OverlayDesigner/Api/OverlayEndpoints.Commands.cs` | `Option<string>.None` | `body` |
| 12 | `SystemVariables/Api/SystemVariableEndpoints.cs` | `Some(fab.Value)` | `body` |

Row 8's precondition is included because a key reused across *create* and *rotate* of the same
integration is two operations; today the rotate would replay the create's secret and not rotate.

**Mapping chains** (`.ProducesProblem(StatusCodes.Status422UnprocessableEntity)`): the twelve
`MapPost` chains in `RulesEndpoints.cs`, `CameraEndpoints.cs`, `EventsEndpoints.cs` (`/manual`),
`EventSourcesEndpoints.cs`, `EventTypesEndpoints.cs`, `DevicesEndpoints.cs`, `KiosksEndpoints.cs`,
`WebhookRotationEndpoints.cs`, `LayoutEndpoints.cs`, `WallEndpoints.cs`, `OverlayEndpoints.cs`,
`SystemVariableEndpoints.cs`.

**Replay lambdas become correct by construction.** Devices (`body.DeviceType`/`DeviceIdentifier`),
webhook rotation (`name`), and the name-based `Location` builders (rules, event-types,
event-sources, system-variables) all read *this* request's values into a replayed answer. With I2
those values are now guaranteed to be the original request's. No edit to those lambdas.

## 7. Architecture guard — status declaration

`StatusProducerDeclarationTests.cs`: census row
`new("M16", "IdempotentRequest key-reuse refusal", "422", Visibility.HandlerBody, GuardSource, <guard name>)`
and a guard modelled on `PreconditionDeclarationTests` (handler-body visible, one hop): every
mapping whose handler body calls `IdempotentRequest.ExecuteAsync` or `ExecuteCreateAsync`
declares `Status422UnprocessableEntity` on its own chain. Population derived, not listed — a
thirteenth keyed endpoint fails the build until it declares the status.

## Testing strategy (feeds tasks.md)

Behaviour-changing → **RED first** (ADR-0139). Two kinds of red, deliberately sequenced:

1. **Assertion red, against today's tree** — HTTP-level integration tests compile against the
   current code (they use only `HttpClient`) and fail on status/identity assertions (`201`/`200`
   where `422` is expected). These are the exploitation-path tests and the evidence quoted in the PR.
2. **Compile red** — unit and store tests whose subject is a new symbol
   (`IdempotencyFingerprint`, `IdempotencyOutcome.Mismatched`, the 5-arg `For`). Their red is the
   compiler naming the missing member; quoted second.

Existing tests that construct `IdempotencyScope.For` (two sites: `IdempotentRequestTests.cs:17`,
`IdempotencyFencingIntegrationTests.cs:283`) are updated **mechanically by the test-writer** to
the 5-arg form with a fixed fab + fingerprint; **no assertion changes**. The one deliberate
assertion change is FR-009's inversion.

## Alternatives considered

- **Fold fab + resource into the primary key** (what `EventSourcesEndpoints` did locally). A
  differing request then executes as a fresh first arrival instead of 422. Rejected: the body is
  the resource at 11 of 12 sites and does not fit a key column; it silently changes what a key
  means per endpoint; it gives a script that reuses a key no signal that it is doing so; and it
  needs a PK migration on seven live tables.
- **Route value only** (the issue's first suggestion). Closes row 8 and nothing else.
- **Fingerprint the raw body bytes.** Needs request buffering middleware on every keyed route and
  makes a client's whitespace significant.
- **409 instead of 422.** See spec §*Why 422 and not 409*.
- **Accept legacy NULL-fingerprint rows as matching.** Re-opens the defect for every key written
  before deploy. Rejected; fail closed.
- **Two PRs (#2424 then #2492).** Two migrations per context and two passes over the same twelve
  files for one root cause. Rejected; the change is one coherent slice (see tasks.md §Size).

## Review focus for phase 6

- `security-reviewer`: I2 ordering (mismatch before in-progress/replay); no secret in the 422
  body; fab guard still precedes `ExecuteAsync` at every site (403 before 422); resolved-fab not
  raw query; fingerprint covers row 8's route value and precondition.
- `backend-reviewer`: `Create` untouched; seven migrations really exist (`git diff --stat`);
  typed null parameter; JSON options frozen and private; `HandlerDeconstructionTests` unaffected.
