# Plan 385: An older address change cannot re-point a stream back over a newer one

**Spec**: [spec.md](./spec.md). **Issue**: #2991. **Context**: StreamDistribution only.

## 1. Context and layers

One bounded context, four layers, no other context touched:

| Layer | File | Change |
|---|---|---|
| Domain | `src/StreamDistribution/Domain/Stream/SourceChangedAt.cs` | **new** value object |
| Domain | `src/StreamDistribution/Domain/Stream/Stream.cs` | `SourceChangedAt?` property; `RepointTo` gains the instant and returns whether it applied |
| Application | `Commands/RepointStreamCommand.cs` | third component `SourceChangedAt ChangedAt` |
| Application | `Commands/Handlers/RepointStreamCommandHandler.cs` | ignored repoint: log, no save, no gateway call, `Success(None)` |
| Application | `EventHandlers/CameraAddressChangedIntegrationEventHandler.cs` | stops discarding `ChangedAt` |
| Application | `Log.cs` | one Information message |
| Infrastructure | `Persistence/Configurations/StreamConfiguration.cs` | map `source_changed_at` |
| Infrastructure | `Persistence/Migrations/<ts>_RecordStreamSourceChangedAt{,.Designer}.cs` + snapshot | additive nullable column |

**Not touched:** `Shared.Contracts` (FR-006), CameraCatalog, `RepointStreamErrors.cs`,
`IRtspGateway` / `MediaMtxRtspGateway`, the reconciler and spec 378's `MediaMtxSourceRepair`,
`Stream.Provision` (FR-009), Wolverine configuration, the idempotency store.

## 2. Types and invariants

### `SourceChangedAt` (new, `Domain/Stream/`)

A byte-for-byte sibling of `LastSuccessAt` / `ProvisionedAt` (same folder), because constitution
§II (ADR-0139/0140) bans `DateTimeOffset` as state on a domain model and those two are the
context's existing answer:

```csharp
public sealed record SourceChangedAt(DateTimeOffset Value)
    : IValueObject<DateTimeOffset>, IComparable<SourceChangedAt>
// From(value) => new(value.ToUniversalTime()); CompareTo; < > <= >=; implicit DateTimeOffset; ToString("O")
```

`ToUniversalTime()` is load-bearing beyond style: Npgsql refuses to write a `DateTimeOffset` with a
non-zero offset to `timestamp with time zone`.

### `Stream`

```csharp
/// The CameraCatalog-stamped instant of the address change this stream last applied (spec 385).
/// Null for rows written before it and for streams never re-pointed (ADR-0048 carve-out, as LastSuccessAt).
public SourceChangedAt? SourceChangedAt { get; private set; }

public bool RepointTo(StreamSourceUrl sourceUrl, SourceChangedAt changedAt, IClock clock)
```

Invariant: **the recorded instant never moves backwards.** `RepointTo`:

1. `Ensure.That` on all three; `EnsureNotRetired(nameof(RepointTo))` — unchanged, still first.
2. `if (SourceChangedAt is not null && changedAt < SourceChangedAt) return false;` — FR-002.
3. `SourceChangedAt = changedAt; SourceUrl = sourceUrl; return true;` — FR-003/004/005. Equal and
   newer both apply; an equal URL with a newer instant still records (spec §3 third scenario —
   without it a reordered A→B→A pair lands on B).

The `bool` is a computed value, outside §II's scope (ADR-0140: "`Contains(...)` returning `bool`").
**[DECIDED]** `bool`, not an outcome type: two outcomes, one caller (ADR-0036). `Provision` sets
nothing (FR-009). No domain event is raised by `RepointTo` today and none is added.

## 3. Command and caller ripple

```csharp
public sealed record RepointStreamCommand(
    CameraIdentifier Camera,
    string RtspSourceUrl,
    SourceChangedAt ChangedAt)
    : ICommand<Result<Option<StreamIdentifier>, RepointStreamError>>;
```

The value object, not a primitive: `SourceChangedAt.From` cannot fail (normalisation only), so there
is nothing to re-validate at the trust boundary, unlike the URL. Every constructor of the command
— the complete list, grep `new RepointStreamCommand` on `bd04dd8e`:

| Caller | Sites | Change |
|---|---|---|
| `CameraAddressChangedIntegrationEventHandler.Handle` | 1 | `var (cameraId, _, _, url, changedAt, _, _) = message;` → `new RepointStreamCommand(camera, url, SourceChangedAt.From(changedAt))` (hoisted to a named local if it wraps). `HandlerDeconstructionTests`: `changedAt` is its own field's name. |
| `tests/.../Commands/RepointStreamCommandHandlerTests.cs` | 7 | argument added |
| `tests/.../Commands/RepointStreamOrderCharacterisationTests.cs` | 1 (`Harness.RepointAsync`) | argument added — `SourceChangedAt.From(Moment)` |

`Stream.RepointTo` callers: the command handler (1) and `tests/.../Stream/StreamRepointTests.cs`
(4) — argument added; the returned `bool` is discarded there. No other production caller; the
registration line in `StreamDistributionRegistrationCharacterisationTests` names the command type
only and does not move.

## 4. Handler flow (`RepointStreamCommandHandler`)

Unchanged up to `var stream = repointable.Value;`, then:

```csharp
if (!stream.RepointTo(sourceUrl, changedAt, clock))
{
    logger.IgnoredOlderRepoint(camera, UrlRedaction.ToOrigin(rtspSourceUrl), changedAt);
    return Success(Option<StreamIdentifier>.None);
}
// save → RepointPathAsync → catch HttpRequestException, exactly as today
```

`None` for "nothing re-pointed", matching the retired and no-stream skips. The event handler sees
success and does not throw, so the stale retry is **acked and ends** — that is the fix. Applied
repoints keep the exact order `lookup-camera, save, repoint` and the same single log line, so the
characterisation file's assertions do not move (§8.3).

## 5. Logging (ADR-0050)

```csharp
[LoggerMessage(Level = LogLevel.Information, Message =
    "Camera {Camera} address change to {Origin} made at {ChangedAt} is older than the change its stream already applied; ignored.")]
public static partial void IgnoredOlderRepoint(this ILogger logger, CameraIdentifier camera, string origin, SourceChangedAt changedAt);
```

Four parameters including `this` (the ADR-0084 limit specs 380-383 just brought this repo under —
the recorded instant is therefore not logged; it is one SQL read away). Origin only, via
`UrlRedaction.ToOrigin` (spec 381's userinfo hardening applies); the path and query never reach a
log line. Rendered for the test in §8: `Camera 0197a1b2-0000-7000-8000-0000000000d2 address change
to rtsp://10.0.8.1:8554 made at 2026-10-14T09:00:00.0000000+00:00 is older than the change its
stream already applied; ignored.` — the test-writer computes the literal from this template.

## 6. Persistence

`StreamConfiguration.MapLastSuccessAndLastError` gains (renamed only if it must — prefer a new
private `MapSourceChangedAt` called after `MapPathSourceStateAndTranscodeMode`):

```csharp
builder.Property(stream => stream.SourceChangedAt)
    .HasColumnName("source_changed_at")
    .HasConversion(at => at!.Value, value => SourceChangedAt.From(value))
    .IsRequired(false);
```

**Migration** (ADR-0067): `dotnet ef migrations add RecordStreamSourceChangedAt --project
src/StreamDistribution/Infrastructure --output-dir Persistence/Migrations` (global `dotnet-ef`
10.0.11 is installed; `DesignTimeDbContextFactory` lives in the project). Expected `Up`:
`AddColumn<DateTimeOffset>(name: "source_changed_at", table: "streams", type: "timestamp with time
zone", nullable: true)`; `Down`: `DropColumn`. **No backfill** — the instants live in
camera-catalog-db, and null applies (FR-004). Naming follows the three existing migrations
(`<yyyyMMddHHmmss>_PascalCase`, namespace `...Infrastructure.Persistence.Migrations`).
`StreamDistributionMigrator` calls `Database.MigrateAsync`, so the integration lane's
`MigrationRunner` applies it with no wiring change. **Proof is the file diff, not a narrative:**
`git diff --stat origin/develop -- src/StreamDistribution/Infrastructure/Persistence/Migrations`
must list the new migration, its Designer and `StreamDistributionDbContextModelSnapshot.cs`.

Postgres keeps microseconds, .NET ticks are 100 ns: a reloaded instant can be up to 0.9 µs earlier
than the event's. Harmless for replay (the event then reads *newer*, which applies); it could only
invert two changes made within one microsecond, which CameraCatalog's HTTP + DB round trip cannot
produce. **[DECIDED]** no truncation in `From`, to stay identical to the sibling value objects.

## 7. Messaging and boundaries

No new message, no contract change (ADR-0073: nothing is added to the wire). Domain → integration
mapping is untouched; the instant enters the context through the existing
`CameraAddressChangedV1.ChangedAt`. No cross-context reference; `Shared.Kernel.UrlRedaction` is
already referenced from Application's siblings. Retry (`throw` on failure), outbox and
`Idempotency-Key` behaviour unchanged (ADR-0142/0143, FR-007).

**Concurrency.** The `version` concurrency token (ADR-0043/0113, `AggregateVersionInterceptor`)
already turns a lost update between two replicas into `DbUpdateConcurrencyException` → throw →
retry → the reread sees the newer instant. One residual: an equal-instant replay that changes no
column issues no `UPDATE`, so it is not version-checked, and its PATCH can land after a concurrent
newer one on another replica — DB right, MediaMTX briefly wrong, repaired by spec 378's sweep to
the (now correct) database value. Accepted; the adversarial pass characterises it.

## 8. Tests (phase 4a: RED; adversarial pass required)

### 8.1 Red on assertions, compiling against today's code (committed alone)

`tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedOrderingTests.cs`
(**new**). Harness: real `CameraAddressChangedIntegrationEventHandler` over the real
`RepointStreamCommandHandler`, `InMemoryStreamRepository`, `FakeRtspGateway`, `FixedClock`,
`CapturingLogger<RepointStreamCommandHandler>`; stream seeded with `StreamBuilder` at
`rtsp://camera-sim:8554/original`; messages are `new CameraAddressChangedV1(...)` with
`EventMetadata` built as `CameraRegisteredIntegrationEventHandlerTests` does. Uses no new type.

| ID | Fact | Today |
|---|---|---|
| R1 | `An_older_change_retried_after_a_newer_one_leaves_the_stream_and_the_sfu_at_the_newer_address` — the §1 sequence; A(t1) throws `InvalidOperationException`; B(t2); A(t1) does not throw; `SourceUrl == B`; `RepointCalls == [(path, B)]` | **red**: `SourceUrl` is A |
| R2 | `An_older_change_after_a_newer_one_is_ignored_on_a_stream_with_nothing_recorded` — B(t2), A(t1); `SourceUrl == B`; calls `[B]` | **red** |
| R3 | `A_newer_change_back_to_the_current_address_still_supersedes_an_older_one` — O(t2), B(t1); `SourceUrl == O`; calls `[O]` | **red** |
| R4 | `An_ignored_change_is_logged_at_information_with_its_origin_only` — B(t2), then `rtsp://10.0.8.1:8554/SECRET-path?token=SECRET`(t1); of the `Logger.Entries` added after the B handling (`CapturingLogger` has no `Clear`; take the count first), exactly one entry, Information, the §5 literal; no entry contains `SECRET`; `SaveCallCount` unchanged by the second handling; calls `[B]` | **red** |
| G1 | `Two_changes_with_the_same_timestamp_apply_in_arrival_order` — B(t1), C(t1); `SourceUrl == C`; calls `[B, C]` | green — **guard** |
| G2 | `A_redelivered_change_after_a_failed_patch_finishes_the_repoint` — fail-once; A(t1) throws; A(t1); `SourceUrl == A`; calls `[A]` | green — **guard** |
| G3 | `A_redelivered_change_after_a_successful_patch_patches_again` — A(t1) twice; calls `[A, A]` | green — **guard** |

G1-G3 are green on arrival **by nature**: today's code applies everything, and these pin the
"equal applies" half of the rule. They exist to catch an over-strict `<=` in step 2 of §2; the
counterfactual (change `<` to `<=` and see G1-G3 red) is run by the engineer after green and quoted
in the PR. They are not offered as red evidence.

R5 — `tests/StreamDistribution.Infrastructure.Tests/StreamDistributionModelCharacterisationTests.cs`:
insert into `ExpectedModel`, between the `mediamtx_path` and `source_url` lines (ordinal column
order):

```
"  source_changed_at [SourceChangedAt]: timestamp with time zone; SourceChangedAt; null; converted to DateTimeOffset",
```

Red today (`The_stream_distribution_model_has_exactly_this_shape`: the line is missing). This is
**the single inherent change** of adding a column: the test enumerates every column, so a new
column must add exactly one line and move none — the neighbouring lines are byte-identical and the
second test (`..._the_committed_snapshot_describes`) is **not edited**: it compares live model to
snapshot and goes from green (neither has it) to green (both have it) once the migration lands. Any
other line moving is a block.

R6 — `tests/Integration.Tests/StreamDistribution/RepointStreamIntegrationTests.cs` (existing class;
`shard-1.filter` already lists it, so **no shard-filter edit**): new fact
`Correcting_a_cameras_address_records_when_the_change_was_made` — register, wait for the stream,
`before = UtcNow`, `PatchAddressAsync`, `WaitForMediaMtxSourceAsync` (the save precedes the PATCH),
`after = UtcNow`; then via `aspire.CreateStreamDistributionDbContextAsync()`
`Database.SqlQuery<DateTimeOffset?>($"SELECT source_changed_at AS \"Value\" FROM streams WHERE camera_id = {camera}")`;
assert non-null and within `[before - 1 s, after + 1 s]`. Raw SQL so it compiles today; red today
with Postgres `42703 column "source_changed_at" does not exist`. CI-authoritative if Docker is down.

### 8.2 Compile-red until the types exist (written in 4a, committed with the fix)

Application.Tests references Domain.Tests (`StreamBuilder`), so these cannot be committed ahead of
the type without breaking the 8.1 suite's build.

- D1 `tests/StreamDistribution.Domain.Tests/Stream/SourceChangedAtTests.cs` (**new**, not an edit to
  `TimestampOrderingTests`): orders by instant, operators agree with `CompareTo`, `From` normalises
  to UTC without moving the instant, `CompareTo(null) == 1`.
- D2 `tests/StreamDistribution.Domain.Tests/Stream/StreamRepointOrderingTests.cs` (**new**): newer
  applies and records (`true`); older returns `false` and changes neither `SourceUrl` nor
  `SourceChangedAt`; equal applies (`true`); null applies and records; equal URL + newer instant
  records; a retired stream still throws before ordering is considered; `Provision` leaves
  `SourceChangedAt` null.
- The mechanical argument additions of §3 (12 sites in three existing files).

### 8.3 Existing tests: what changes and why

| File | Change | Authorised because |
|---|---|---|
| `StreamDistributionModelCharacterisationTests` | +1 `ExpectedModel` line (R5) | inherent to the column; single line; nothing else moves |
| `StreamRepointTests` (4), `RepointStreamCommandHandlerTests` (7), `RepointStreamOrderCharacterisationTests` (1) | constructor / call argument added; **no assertion edited** | the signature is the decision's ("`RepointTo` / `RepointStreamCommand` ignores…"). Every seeded stream has a null recorded instant, so every existing call applies exactly as today — the characterisation file's call logs, log literals and `SaveCallCount` hold unmodified |

**Must stay green unmodified:** `StreamDistributionRegistrationCharacterisationTests`,
`PrimitiveBoundaryTests` (a nullable value object, as `LastSuccessAt`), `HandlerDeconstructionTests`,
`TimestampOrderingTests`, the reconciler and source-repair suites (spec 378),
`MediaMtxReconcilerPassCharacterisationTests`, `StreamRepositoryVersionAssertionIntegrationTests`,
both existing `RepointStreamIntegrationTests` facts, `CameraRegisteredIntegrationEventHandlerTests`.

### 8.4 Adversarial (test-adversary)

`tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedOrderingAdversarialTests.cs`:
three or more changes in every permutation of arrival (ends at the max-instant URL, gateway's last
call is that URL); failures injected at every handling; same instant expressed in two offsets
(`+02:00` vs `Z`) compares equal; `DateTimeOffset.MinValue` as `ChangedAt` (ignored once anything
is recorded — report, do not fix); a sub-microsecond pair; an `InMemoryStreamRepository.OnSave`
hook committing a newer change between load and save. Report the equal-instant no-`UPDATE` replica
race (§7) with its 378 repair as the stated mitigation.

## 9. Risks

1. **Clock steps backwards in CameraCatalog** (NTP/PTP correction, or two replicas with skew): a
   genuinely later change stamped earlier is ignored and the stream keeps the previous address.
   Recovery: another address change once the clock has passed the recorded instant. CameraCatalog
   emits nothing for a no-op `PATCH` to the same URL, so "re-save the address" is not a recovery.
   Bounded by fab clock discipline; flagged for the security review.
2. **A forged far-future `ChangedAt`** on the broker would now pin a stream against every later
   legitimate change. Anyone with publish rights could already repoint; this adds persistence.
   The trust boundary is RabbitMQ publish rights; review, do not mitigate here.
3. **Equal-instant replica race** (§7) — MediaMTX wrong for at most one spec-378 sweep.
4. **Address change before registration** (spec §5) — pre-existing, unchanged; follow-up candidate.
5. A repoint ignored by this spec is **acked**, so it leaves no dead-letter trace; the Information
   line is the only record. Intended (it is not a failure).

---

## 10. Addendum (2026-10-16): clamping a future `ChangedAt` (spec §8)

> **Superseded by spec 403 (#3008), 2026-10-18:** the clamp was removed.

**Where.** The clamp lives in the `ChangedAtPolicy` Application service (clock and options; keeps the
handler at four constructor parameters, ADR-0084), applied by `RepointStreamCommandHandler` before
`RepointTo`: `effective = changedAt > clock.UtcNow + tolerance ? SourceChangedAt.From(clock.UtcNow) : changedAt`.
The poisoned-stamp test is `recorded > now + tolerance` (as C6 pins; a recorded stamp 5 s ahead is
routine skew), not "later than now".
`Stream.RepointTo` is unchanged (it keeps comparing and recording whatever it is given). The tolerance
arrives as `IOptions<StreamRepointOptions>` (assumed: `ChangedAtTolerance`, `SectionName = "StreamRepoint"`,
default 5 min), bound and validated in the Infrastructure module beside `BindMediaMtxSourceRepair`
(`>= 0`, `<= 1 day`, `ValidateOnStart`). The production code decides the names; the tests adapt only
their constructor/binder call.

**Logs (`Log.cs`).** New Warning for the clamp (camera, origin, received, now). `IgnoredOlderRepoint`
gains the recorded stamp and becomes Warning when recorded > now + tolerance. To keep the existing
`An_ignored_change_is_logged_at_information_with_its_origin_only` assertion (exact message text)
passing unmodified, the routine Information line must keep its current text; the recorded stamp and
the Warning level apply to the poisoned case (a second message, or the recorded stamp carried only
where it is later than now). If that is not achievable, STOP: editing that assertion is an owner call.

**Existing tests.** Every existing handler test uses `FixedClock(Moment)` with stamps equal to or
within seconds after `Moment`, far inside the 5-minute tolerance: none is affected by the clamp, and
no harness edit is needed. `StreamDistributionRegistrationCharacterisationTests` gains the two
options entries (authorised, FR-011).

**Risks revisited.** Risk 2 (forged far-future stamp) is now mitigated by the clamp. Risk 1 (backward
clock step) stays an accepted residual; follow-up decision issue for a serialised sequence (V2).
A clamped stamp equal to "now" still outranks a legitimate change stamped earlier than the
receiver's now within the tolerance window; that bounded window is the price of clamp-not-reject.
The same holds in the other direction: a publisher clock ahead by an amount straddling the tolerance
can lose a genuine newer change (a stamp clamped to now ranks below an earlier unclamped one); spec §8
R-3 records it, and clamping to now+tolerance instead is an owner decision.
