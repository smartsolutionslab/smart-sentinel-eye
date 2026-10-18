# Plan 403: A camera's address changes reach its stream in the order the catalogue made them

**Spec**: [spec.md](./spec.md). **Issue**: #3008. **Contexts**: `Shared.Contracts` (one additive
member), CameraCatalog (publisher), StreamDistribution (consumer). Cross-context only through
`Shared.Contracts` (NetArchTest `BoundaryTests` unchanged).

## 1. Layers and files

| Context / layer | File | Change |
|---|---|---|
| Shared.Contracts | `CameraCatalog/CameraAddressChangedV1.cs` | trailing `int? PreviousVersion = null`; doc comment |
| CameraCatalog Domain | `Camera/Events/CameraAddressChangedDomainEvent.cs` | trailing `AggregateVersion PreviousVersion` |
| CameraCatalog Domain | `Camera/Camera.cs` (`ChangeAddress`, :140-146) | `PreviousVersion: Version` in the `Raise` |
| CameraCatalog Application | `EventHandlers/CameraAddressChangedDomainEventHandler.cs` | `PreviousVersion: domainEvent.PreviousVersion.Value` |
| StreamDistribution Domain | `Stream/SourceCameraVersion.cs` | **new** value object |
| StreamDistribution Domain | `Stream/Stream.cs` | `SourceCameraVersion?` property; `RepointTo` gains `Option<SourceCameraVersion>` and the §2.3 rule |
| StreamDistribution Application | `Commands/RepointStreamCommand.cs` | fourth component `Option<SourceCameraVersion> CameraVersion` |
| StreamDistribution Application | `Commands/Handlers/RepointStreamCommandHandler.cs` | `ChangedAtPolicy` → `IClock`; clamp and poisoned branch deleted; versioned ignore log |
| StreamDistribution Application | `EventHandlers/CameraAddressChangedIntegrationEventHandler.cs` | 8-arity deconstruct; version validated and passed |
| StreamDistribution Application | `Log.cs` | +1 Information message; −2 Warning messages |
| StreamDistribution Application | `Commands/ChangedAtPolicy.cs`, `Commands/StreamRepointOptions.cs`, `ChangedAtPair.cs` | **deleted** (FR-005) |
| StreamDistribution Infrastructure | `StreamDistributionInfrastructureModule.cs` | `BindStreamRepoint` (:331-342) and its call (:50) deleted |
| StreamDistribution Infrastructure | `Persistence/Configurations/StreamConfiguration.cs` | map `source_camera_version` |
| StreamDistribution Infrastructure | `Persistence/Migrations/<ts>_RecordStreamSourceCameraVersion{,.Designer}.cs` + snapshot | additive nullable `integer` column |

**Not touched:** AuditObservability (it reads by member access and rebuilds with `with`, which
carries the new member — `AuditPayloadRedaction.cs:33-44`); the CameraCatalog schema (the version
column exists); `AggregateVersionInterceptor`; `ChangeCameraAddressCommandHandler`; the outbox,
Wolverine and idempotency configuration; `IRtspGateway` / the MediaMTX gateway; the reconciler and
spec 378's source repair; `Stream.Provision`; `RepointStreamErrors.cs`.

## 2. Types and invariants

### 2.1 The wire member (FR-001)

```csharp
public sealed record CameraAddressChangedV1(
    Guid Camera, string Fab, string PreviousUrl, string Url, DateTimeOffset ChangedAt, Guid ChangedBy,
    EventMetadata Metadata,
    int? PreviousVersion = null) : IIntegrationEvent;
```

Trailing because a parameter with a default must follow those without, and `Metadata` stays where
every other contract has it. `int` because `AggregateVersion` is `int` (no widening to invent). The
name is `PreviousVersion`, sibling of `PreviousUrl`: the camera's version **before** this change.

**Why the loaded version and not the committed one [DECIDED].** At `Raise` and at dispatch the
aggregate still holds the loaded version; the interceptor bumps it inside the commit (spec §1.2). The
committed version would be `Version + 1`, which hard-codes the interceptor's increment into the
domain and goes wrong silently if that ever changes. The loaded version is strictly increasing
across committed changes on its own (one bump per committed change; the concurrency token stops two
writers of one loaded version both committing; the outbox rolls back with a losing write). Ordering
needs only strict monotonicity, so the loaded value is sufficient and coupling-free.

### 2.2 `SourceCameraVersion` (new, StreamDistribution `Domain/Stream/`)

Sibling of `SourceChangedAt` in shape, because §II (ADR-0139/0140) bans `int` as domain state:

```csharp
public sealed record SourceCameraVersion(int Value) : IValueObject<int>, IComparable<SourceCameraVersion>
// From(value): Ensure.That(value).AtLeast(0); CompareTo (null sorts first, as SourceChangedAt);
// < > <= >=; implicit int; ToString() invariant culture
```

`Stream`:

```csharp
/// The PreviousVersion of the address change this stream last applied (spec 403). Null for rows
/// written before it, for streams never re-pointed, and after a change that carried none.
public SourceCameraVersion? SourceCameraVersion { get; private set; }
```

### 2.3 The ordering rule (`Stream.RepointTo`, FR-004)

```csharp
public bool RepointTo(
    StreamSourceUrl sourceUrl,
    SourceChangedAt changedAt,
    Option<SourceCameraVersion> cameraVersion,
    IClock clock)
{
    // Ensure.That on the reference arguments; EnsureNotRetired(nameof(RepointTo)) - unchanged, first.
    if (IsOlderThanRecorded(changedAt, cameraVersion)) return false;
    SourceUrl = sourceUrl;
    SourceChangedAt = changedAt;
    SourceCameraVersion = cameraVersion.HasValue ? cameraVersion.Value : null;
    return true;
}

private bool IsOlderThanRecorded(SourceChangedAt changedAt, Option<SourceCameraVersion> cameraVersion)
{
    if (cameraVersion.HasValue)                                   // rules 1 and 2
        return SourceCameraVersion is not null && cameraVersion.Value < SourceCameraVersion;
    return SourceChangedAt is not null && changedAt < SourceChangedAt;   // rule 3 - spec 385 verbatim
}
```

Four parameters (ADR-0084's limit, advisory); `Option<T>` for the absent version (ADR-0141); the
property stays a nullable value object (ADR-0048 carve-out — EF maps it, it does not map
`Option<T>`). The engineer may shape the helper differently; the truth table is fixed:

| incoming version | recorded version | decided by | equal |
|---|---|---|---|
| some | some | version | applies (only a replay can be equal: two committed changes never share a loaded version) |
| some | null | — always applies (rule 2) | — |
| none | any | `ChangedAt` (spec 385) | applies |

**Why rule 2 [DECIDED].** The alternative — compare by `ChangedAt` whenever either side lacks a
version — is the textbook fallback, and it is unsafe **without the clamp**: a single forged (or
fast-clock) legacy-shaped message with `ChangedAt = MaxValue` recorded on a version-less row would
make every later message compare by `ChangedAt` against `MaxValue`, versioned ones included,
pinning the stream permanently. The owner removed the clamp, so the fallback cannot be kept. Rule 2
makes any legacy-shaped stamp lose to the next genuine (versioned) change, and also discharges the
spec's first-change-after-deploy case (a row recorded by spec 385 cannot block a versioned change
whose clock stepped back). Its cost is R-V2 (spec §8), bounded to replica overlap during a deploy.

**Why null on a version-less apply [DECIDED].** The recorded version means "the version of the
change last applied". Keeping the old version after a legacy change replaced it would let a
redelivery of that older versioned change (equal version → applies) overwrite the newer legacy one;
clearing it lets a lower-versioned stale retry apply instead. Both are R-V2 (overlap only); null is
the one that keeps the recorded fields describing a single change.

## 3. Command and caller ripple (StreamDistribution)

```csharp
public sealed record RepointStreamCommand(
    CameraIdentifier Camera,
    string RtspSourceUrl,
    SourceChangedAt ChangedAt,
    Option<SourceCameraVersion> CameraVersion)
    : ICommand<Result<Option<StreamIdentifier>, RepointStreamError>>;
```

`CameraAddressChangedIntegrationEventHandler.Handle`:

```csharp
var (cameraId, _, _, url, changedAt, _, _, previousVersion) = message;
var camera = CameraIdentifier.From(cameraId);
var cameraVersion = previousVersion is { } version
    ? Option<SourceCameraVersion>.Some(SourceCameraVersion.From(version))   // FR-008: -1 throws here
    : Option<SourceCameraVersion>.None;
var repointStreamCommand = new RepointStreamCommand(camera, url, SourceChangedAt.From(changedAt), cameraVersion);
```

FR-008: `SourceCameraVersion.From` refuses a negative value with `Ensure` before any lookup, which
fails the message as every other trust-boundary refusal in this handler does (Wolverine retries,
then dead-letters). **[DECIDED]** no new `RepointStreamError`: the refusal is of a malformed
message, not an outcome the command reports. The local is named `previousVersion`, its own field's
name (`HandlerDeconstructionTests`).

Every existing constructor of the command and caller of `RepointTo` gets `Option<…>.None` — the
legacy path, so each keeps its exact behaviour (plan §8.3 lists them).

## 4. Handler flow (`RepointStreamCommandHandler`)

Constructor: `(IStreamRepository streams, IRtspGateway rtsp, IClock clock,
ILogger<RepointStreamCommandHandler> logger)`. Flow unchanged up to `var stream = repointable.Value;`
then:

```csharp
if (!stream.RepointTo(sourceUrl, changedAt, cameraVersion, clock))
{
    return IgnoreOlderRepoint(stream, rtspSourceUrl, changedAt, cameraVersion);   // acked, Success(None)
}
return await SaveAndRepointGatewayAsync(stream, rtspSourceUrl, cancellationToken); // unchanged
```

`IgnoreOlderRepoint` logs `IgnoredOlderVersionedRepoint` when the incoming change carried a version,
else the existing `IgnoredOlderRepoint` with its **unchanged** text (spec 385's R4 literal holds).
`ClampFutureStamp`, the `IsPoisoned` branch and `changedAtPolicy.Clock` go.

## 5. Logging (ADR-0050)

```csharp
[LoggerMessage(Level = LogLevel.Information, Message =
    "Camera {Camera} address change to {Origin} made against camera version {CameraVersion} is older than the change its stream already applied; ignored.")]
public static partial void IgnoredOlderVersionedRepoint(this ILogger logger, CameraIdentifier camera, string origin, SourceCameraVersion cameraVersion);
```

Origin only via `UrlRedaction.ToOrigin`. Rendered for the test (§8 V9): `Camera
0197a1b2-0000-7000-8000-0000000000d2 address change to rtsp://10.0.8.1:8554 made against camera
version 3 is older than the change its stream already applied; ignored.` — the test-writer computes
the literal from this template (the camera id is whatever the harness uses). Deleted:
`ClampedFutureChangedAt`, `IgnoredAgainstFutureRecordedStamp` (and `ChangedAtPair`, used only by
them).

## 6. Persistence (StreamDistribution only)

New private `MapSourceCameraVersion` in `StreamConfiguration`, called beside `MapSourceChangedAt`:

```csharp
builder.Property(stream => stream.SourceCameraVersion)
    .HasColumnName("source_camera_version")
    .HasConversion(version => version!.Value, value => SourceCameraVersion.From(value))
    .IsRequired(false);
```

Migration (ADR-0067): `dotnet ef migrations add RecordStreamSourceCameraVersion --project
src/StreamDistribution/Infrastructure --output-dir Persistence/Migrations`. Expected `Up`:
`AddColumn<int>(name: "source_camera_version", table: "streams", type: "integer", nullable: true)`;
`Down`: `DropColumn`. **No backfill.** `StreamDistributionMigrator` calls `MigrateAsync`, so the
integration lane applies it unwired. **Proof is the file diff:** `git diff --stat origin/develop --
src/StreamDistribution/Infrastructure/Persistence/Migrations` lists the migration, its Designer and
the snapshot.

## 7. Messaging, compatibility and deploy order

- **Domain → integration**: `CameraAddressChangedDomainEvent.PreviousVersion` (value object) →
  `CameraAddressChangedV1.PreviousVersion` (`int?`, always set by the new publisher). ADR-0040 shape
  kept: primitives on the wire, value objects inside.
- **Old consumer, new message**: the unknown member is ignored by both System.Text.Json and
  Newtonsoft defaults. **New consumer, old message**: the missing constructor parameter takes its
  default, `null`, under both. Which one Wolverine 6.48.3 uses is measured in T003, not assumed; the
  integration fact R9 proves the member crosses the real broker.
- **Deploy order**: StreamDistribution before CameraCatalog, so no versioned message meets a
  consumer that would drop the version on the floor and record only its stamp (R-V2). In this repo's
  single AppHost every service starts together and there is no production deployment, so this is a
  note for the first Helm rollout, not a mechanism.
- **Concurrency inside StreamDistribution** (ADR-0043/0113): unchanged. Two replicas racing on one
  stream still produce `DbUpdateConcurrencyException` → throw → retry → the reread sees the newer
  record.
- Retry (`throw` on failure), outbox and `Idempotency-Key` semantics unchanged (FR-009).

## 8. Tests (phase 4a: RED; adversarial pass required)

### 8.1 Red on assertions, compiling against today's code (committed alone)

**V-series** — `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedVersionOrderingTests.cs`
(**new**). Harness as `CameraAddressChangedOrderingTests` (real event handler over the real command
handler, `InMemoryStreamRepository`, `FakeRtspGateway` with fail-once, `FixedClock(N)`,
`CapturingLogger<RepointStreamCommandHandler>`), constructed exactly as today's file does (so it
compiles today and takes the same one-line constructor edit as the other eleven harnesses, §8.3).
**Messages are built as they arrive off the wire:** serialize a `CameraAddressChangedV1` with
`System.Text.Json` defaults, add `"PreviousVersion": n` to the JSON object (omit it for a legacy
message), deserialize. Today the member is ignored; after the fix it binds. This compiles on both
sides and needs no type that does not exist.

| ID | Fact | Today |
|---|---|---|
| V1 | `A_later_change_stamped_earlier_after_a_backward_clock_step_applies` — B(v3, 08:10), C(v4, 08:05); `SourceUrl == C`; calls `[B, C]` | **red**: `SourceUrl` is B |
| V2 | `An_older_change_redelivered_after_a_newer_one_is_ignored_whatever_its_stamp` — fail-once; A(v3, t2) throws; B(v4, t1<t2); A(v3, t2) does not throw; `SourceUrl == B`; calls `[B]` | **red**: B is ignored, `SourceUrl` is A |
| V3 | `A_stale_retry_does_not_win_when_the_catalogue_runs_six_minutes_fast` — clock N; fail-once; A(v3, N+6m) throws; B(v4, N+6m1s); A(v3, N+6m); `SourceUrl == B` | **red**: all clamp to N, equal applies, `SourceUrl` is A |
| V4 | `A_genuine_newer_change_straddling_the_old_tolerance_applies` — A(v3, N+4m59s), B(v4, N+5m3s); `SourceUrl == B` | **red**: B clamps to N < A, ignored |
| V5 | `A_far_future_stamp_is_recorded_as_given_and_nothing_is_logged_at_warning` — U(v3, MaxValue); `Stream.SourceChangedAt == From(MaxValue)`; no Warning entry; then B(v4, N); `SourceUrl == B` | **red**: recorded N, one Warning |
| V6 | `The_first_versioned_change_applies_over_a_record_made_without_a_version` — legacy O2(no version, N) then B(v0, N−10m); `SourceUrl == B` | **red**: B is older by stamp, ignored |
| V9 | `An_ignored_older_versioned_change_is_logged_at_information_with_its_origin_and_version_only` — B(v4, t1); then `rtsp://10.0.8.1:8554/SECRET-path?token=SECRET`(v3, t2>t1); of the entries after B (count first), exactly one, Information, the §5 literal; none contains `SECRET`; `SaveCallCount` unchanged; calls `[B]` | **red**: newer stamp applies |
| V10 | `A_negative_version_is_refused_before_anything_is_looked_up_or_saved` — B(v −1); `Handle` throws; `SourceUrl == O`; calls empty; `SaveCallCount` unchanged | **red**: ignored member, B applies |
| G4 | `A_redelivery_of_the_same_versioned_change_finishes_a_failed_repoint` — fail-once; A(v3, t1) throws; A(v3, t1); `SourceUrl == A`; calls `[A]` | green — **guard** (equal applies) |
| G5 | `A_change_without_a_version_over_a_versioned_record_is_ordered_by_its_stamp` — B(v4, t2), C(none, t3>t2), D(none, t1); `SourceUrl == C` | green — **guard** (rule 3) |
| G6 | `An_older_versioned_change_with_an_older_stamp_is_ignored` — A(v3,t1) fails, B(v4,t2), A again; `SourceUrl == B` | green — **guard** (both rules agree) |

G4-G6 are green on arrival by nature and are not red evidence. Their counterfactuals, run by the
engineer after green and quoted in the PR: `<` → `<=` in rule 1 turns G4 red; deleting rule 3's
branch (treating a versionless message as always applying) turns G5 red.

**R8** — `tests/StreamDistribution.Infrastructure.Tests/StreamDistributionModelCharacterisationTests.cs`:
insert one `ExpectedModel` line between `mediamtx_path` and `source_changed_at` (ordinal column
order):

```
"  source_camera_version [SourceCameraVersion]: integer; SourceCameraVersion; null; converted to Int32",
```

Red today (the line is missing). The single inherent change of adding a column; no other line moves;
the snapshot test is not edited (green → green once the migration lands).

**R9** — `tests/Integration.Tests/StreamDistribution/RepointStreamIntegrationTests.cs` (existing
class; `shard-1.filter` already lists it — **no shard-filter edit**): new fact
`Correcting_a_cameras_address_records_the_camera_version_it_was_made_against` — register, wait for
the stream, read the camera's `version` from `GET` (as `PatchAddressAsync` does), `PATCH` with that
`If-Match`, `WaitForMediaMtxSourceAsync`, then raw SQL `SELECT source_camera_version AS "Value" FROM
streams WHERE camera_id = {camera}`; assert it equals the `If-Match` value. Red today with Postgres
`42703 column "source_camera_version" does not exist`. CI-authoritative if Docker is down. The
assertion checks a value the system produced (row) against a value the server issued (`GET`), not
the test's own input.

### 8.2 Compile-red until the types exist (written in 4a, committed with the fix)

- **K1** `tests/Shared.Contracts.Tests/CameraAddressChangedV1Tests.cs` (**new**, beside
  `CameraRegisteredV1Tests.cs`): exposes every member positionally including `PreviousVersion`;
  omitted `PreviousVersion` is `null`; JSON round trip preserves it; JSON **without** the member
  deserializes with `null` and every other field intact; JSON **with** it deserializes into a
  test-local seven-member `PreSpec403CameraAddressChangedV1` record (the old reader) with every field
  intact; records with the same payload are equal; implements `IIntegrationEvent`.
- **K2** `tests/CameraCatalog.Domain.Tests/Camera/CameraAddressChangeVersionTests.cs` (**new**): a
  camera put at version 5 (reflection on the non-public `Version` setter, as
  `tests/Shared.Kernel.Tests/AggregateVersions.cs:44-62` does — copy the technique, the project does
  not reference that helper) raises `PreviousVersion == 5`; a freshly registered camera's first
  change raises `0`; a no-op change raises nothing (unchanged rule, re-pinned here only if cheap).
- **K3** `tests/CameraCatalog.Application.Tests/EventHandlers/CameraAddressChangedDomainEventHandlerTests.cs`
  (**new**, mirrors `CameraRegisteredDomainEventHandlerTests`): the published `CameraAddressChangedV1`
  carries `PreviousVersion == domainEvent.PreviousVersion.Value`.
- **K4** `tests/StreamDistribution.Domain.Tests/Stream/SourceCameraVersionTests.cs` (**new**):
  orders by value; operators agree with `CompareTo`; `CompareTo(null) == 1`; `From(-1)` throws.
- **K5** `tests/StreamDistribution.Domain.Tests/Stream/StreamRepointVersionOrderingTests.cs`
  (**new**): every row of the §2.3 truth table, asserting the returned `bool` and both recorded
  fields; a versioned apply records the version; a legacy apply over a versioned record clears it to
  null; a retired stream still throws before ordering; `Provision` leaves `SourceCameraVersion` null.
- The mechanical edits of §8.3.

### 8.3 Existing tests: what changes and why

**Retired — required by the owner decision ("removes the clamp"), named per ADR-0144:**

| File:line | Assertion | Why it cannot stand |
|---|---|---|
| `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedFutureStampTests.cs:120` C1 | MaxValue clamps to now; one Warning with both stamps | the clamp is removed; V5 pins the opposite |
| same file `:139` C2 | a change after a clamped MaxValue applies | asserted for **legacy** messages; without the clamp a legacy MaxValue blocks later legacy ones (R-V3); V5 pins the versioned form |
| same file `:152` C3, `:164` C4 (exact) | within/at tolerance: recorded as given, no Warning | would pass, but the tolerance no longer exists and the harness constructs the deleted `ChangedAtPolicy`; V5 subsumes "recorded as given" |
| same file `:176` C4 (one tick) | clamped to now, Warning | the clamp is removed |
| same file `:189` C5 | ignored against a poisoned recorded stamp at **Warning** with both stamps | the poisoned branch is removed; the line is Information now |
| same file `:209` C6 | ignored at Information | would pass; retired with its file — the same behaviour stays pinned by `CameraAddressChangedOrderingTests.An_ignored_change_is_logged_at_information_with_its_origin_only` (:169) |
| `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedFutureStampToleranceTests.cs:53` C7 | tolerance zero clamps any future stamp | the option is removed |
| `tests/StreamDistribution.Infrastructure.Tests/StreamRepointToleranceValidationTests.cs:31,43,52` C8 ×3 | tolerance validation and default | the option is removed |
| `tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs:77-79` | three expected registration lines (`IConfigureOptions<StreamRepointOptions>`, `IValidateOptions<StreamRepointOptions>`, `ChangedAtPolicy`) | the registrations are removed; **only these three lines** go, nothing else in the list moves |

The three files above are deleted whole. Spec 385 FR-010-FR-013 are marked superseded (T020).

**Mechanical — argument/constructor edits only, no assertion edited** (every one exercises the
legacy path, so it behaves exactly as today):

| Site | Count | Edit |
|---|---|---|
| `new RepointStreamCommandHandler(…, new ChangedAtPolicy(<clock>, Options.Create(new StreamRepointOptions())), …)` → `<clock>` | 11 | `RepointStreamCommandHandlerTests.cs:206`, `RepointStreamOrderCharacterisationTests.cs:63`, `CameraAddressChangedAdversarialOrderingTests.cs:88`, `CameraAddressChangedOrderingTests.cs:67`, `LegacyAuthorityHostReplayAdversarialFindingTests.cs:88`, `LegacyAuthorityHostReplayAdversarialTests.cs:140`, `LegacyAuthorityHostReplayTests.cs:92`, `LegacyMisreadSourceRedeliveryAdversarialTests.cs:181`, `LegacyMisreadSourceReplayTests.cs:90`, `LegacyZoneDelimiterReplayAdversarialTests.cs:165`, `LegacyZoneDelimiterReplayTests.cs:92` (+ the new V-series file); drop the then-unused `using Microsoft.Extensions.Options;` (IDE0005) |
| `new RepointStreamCommand(…)` + `Option<SourceCameraVersion>.None` | 8 | `RepointStreamCommandHandlerTests.cs` (7), `RepointStreamOrderCharacterisationTests.cs` (1) |
| `.RepointTo(…)` + `Option<SourceCameraVersion>.None` | 31 | `StreamRepointAdversarialTests.cs` (14), `StreamRepointOrderingTests.cs` (12), `StreamRepointTests.cs` (4), `LegacyZoneDelimiterReplayAdversarialTests.cs` (1) |

Re-count with `grep -rn "\.RepointTo(\|new RepointStreamCommand(\|new ChangedAtPolicy(" tests` before
editing; a different count is a stop-and-report, not an improvisation.

**Must stay green unmodified:** `CameraAddressChangedOrderingTests` (all seven spec 385 facts — the
rolling-deploy guarantee), `CameraAddressChangedAdversarialOrderingTests`' assertions,
`RepointStreamOrderCharacterisationTests`' call logs and literals, `TimestampOrderingTests`,
`SourceChangedAtTests`, `StreamRepointOrderingTests`' assertions, `PrimitiveBoundaryTests`,
`HandlerDeconstructionTests`, `AuditContractPropertyShapeTests`, `AuditPayloadRedactionCoverageTests`,
`BoundaryTests`, every AuditObservability `CameraAddressChangedV1` test (≈25 files; they construct
seven positional arguments and must compile and pass unchanged), `CameraAddressAuditIntegrationTests`,
`V1ResourceMapTests`, both existing `RepointStreamIntegrationTests` facts, the reconciler and
source-repair suites.

### 8.4 Adversarial (test-adversary)

`tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedVersionOrderingAdversarialTests.cs`:
every arrival permutation of three-plus versioned changes with independent random stamps (ends on
the highest version; gateway's last call is it); failures injected at every handling; mixed legacy
and versioned sequences against the truth table; `int.MaxValue` version then a genuine change
(characterise R-V1: report, do not fix); `PreviousVersion` as JSON `null`, as a string, as a
fraction, as > `int.MaxValue` (what does the deserializer do, and does the message fail rather than
apply as legacy?); an `InMemoryStreamRepository.OnSave` hook committing a newer change between load
and save. Report R-V2 with its deploy-order mitigation.

## 9. Risks

1. **R-V1 forged high version** (spec §8): unbounded pin without broker publish permissions. Flagged
   for the security review; the lane does not invent a bound (that would be a decision).
2. **R-V2 replica overlap** during a CameraCatalog rolling deploy: bounded, documented, deploy
   StreamDistribution first.
3. **Serializer assumption** (§7): measured by T003 and proven end to end by R9.
4. **A future second bump per save** (an owned child, a second behaviour in one command) would not
   break ordering — the loaded version still strictly increases — which is the point of §2.1.
5. **Recorded stamp keeps growing on the versioned path** (`SourceChangedAt` is still written as
   given, `MaxValue` included): it only matters to a later legacy message (R-V3).
