# Spec 403: A camera's address changes reach its stream in the order the catalogue made them, whatever its clock says

**Feature Branch**: `fix/3008-repoint-ordering-aggregate-version` (cut from `origin/develop` at `b7001237`)

**Created**: 2026-10-18

**Status**: Phases 1-3 written; awaiting phase 4. No `[NEEDS CLARIFICATION]` remains: the owner's
decision on #3008 (2026-10-17, second-to-last comment) settles the behavioural questions. Judgement
calls the decision left to the architect are marked **[DECIDED]** with their reason; none is new
architecture. One consequence of the decision is recorded as a residual the security review must
confirm (§8 R-V1); it is not an open question.

**Input**: Issue [#3008](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3008),
*Repoint ordering by wall-clock ChangedAt silently drops a genuine fresh change after a backward
clock step in CameraCatalog*. Residual of #2991 / spec 385.

**Owner decision (2026-10-17, on #3008), recorded here and not re-opened:** order repoints by a
per-camera **monotonic aggregate version** instead of wall-clock `ChangedAt`: an **additive field on
`CameraAddressChangedV1`**, versioned per the Shared.Contracts rules; **a message without the field
keeps the existing `ChangedAt` behaviour**, so a rolling deploy does not drop changes. **The clamp
is removed** ("the R-3 clamp": spec 385 §8 FR-010-FR-013, whose residuals R-1 and R-3 the issue's
2026-10-16 comment named). If a contract change needed an ADR, block.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing**: a genuine change stamped earlier than the
last one now applies (today it is ignored), and a far-future stamp is no longer clamped. Phase 4a is
**RED** (ADR-0139). **Adversarial pass required** (`test-adversary`): ordering under redelivery is a
race. **Security review required**: stream attribution, and the clamp this removes was a security
mitigation (§8).

**Spec number.** 403, assigned by the orchestrator (402 is open PR #3041 — disjoint files, checked
with `gh pr diff 3041 --name-only`; 404 and 405 are taken by sibling architects). Checked
2026-10-18: no `specs/403*` on `origin/develop`, no remote head names 403. Re-check before the PR.

**ADRs.** ADR-0073 (integration-event versioning: an additive optional field with a safe default
consumers can ignore **does not bump the version** — so no `V2`, no ADR), ADR-0040 (domain vs
integration event; the contract stays primitive), ADR-0043 / ADR-0113 (the aggregate `Version`, the
`If-Match` expected version and the EF concurrency token: the source of the sequence), ADR-0088
(transactional outbox: an event leaves only with its commit), ADR-0139 / ADR-0140 (constitution §II:
`int` is banned as domain state, so the recorded version is a value object), ADR-0048 / ADR-0141
(nullable value-object property for a persisted absence; `Option<T>` for the absent parameter),
ADR-0067 (`MigrationRunner`), ADR-0142 / ADR-0143 (idempotency and retry — **unchanged**), ADR-0050
(logging), ADR-0084 (four-parameter limit, advisory), ADR-0103 (no Testcontainers), ADR-0105
(`Ensure.That`), ADR-0144 (the lane implements decisions). Prior art: spec 385 (what this replaces),
spec 029 FR-013 (the repoint), spec 021 FR-001 (dispatch into the outbox before the commit, one
transaction). **No new ADR is needed** (§1.4).

**Latency budget.** N/A. Re-pointing is control-plane (an RTSP source correction via MediaMTX's
config API), on no constitution §IV leg.

---

## 1. Premise, verified on `b7001237` (not assumed)

### 1.1 The defect is live

- `Camera.ChangeAddress` stamps `ChangedAt: clock.UtcNow`
  (`src/CameraCatalog/Domain/Camera/Camera.cs:145`); the domain-event handler copies it to
  `CameraAddressChangedV1.ChangedAt`
  (`src/CameraCatalog/Application/EventHandlers/CameraAddressChangedDomainEventHandler.cs:33`).
- `Stream.RepointTo` refuses `changedAt < SourceChangedAt`
  (`src/StreamDistribution/Domain/Stream/Stream.cs:209`), and the handler acks the refusal with one
  Information line (`RepointStreamCommandHandler.cs:94-115`, `Log.cs:38`). So the issue's example
  holds: B stamped 08:10, clock corrected, C stamped 08:05 → C ignored, catalogue says C, wall says B,
  the API answered 204.
- The clamp is live: `ChangedAtPolicy.Clamp` (`Application/Commands/ChangedAtPolicy.cs:23-34`),
  applied at `RepointStreamCommandHandler.cs:41`, tolerance `StreamRepoint:ChangedAtTolerance`
  (`StreamRepointOptions.cs:18`, bound at `StreamDistributionInfrastructureModule.cs:50,331-342`). Its
  residuals R-1 (publisher more than the tolerance ahead: ordering degrades to handling order, a
  stale retry wins) and R-3 (publisher straddling the tolerance drops a genuine newer change) are
  recorded at `specs/385-repoint-ordered-by-changed-at/spec.md:265-283`.

### 1.2 A monotonic per-camera sequence already exists, and is serialised by the database

- `AggregateRoot<T>.Version` (`src/Shared.Kernel/AggregateRoot.cs:15`), an `AggregateVersion(int)`
  (`src/Shared.Kernel/Primitives/AggregateVersion.cs`), mapped on `cameras.version` as a concurrency
  token (`CameraConfiguration.cs:37-40`).
- `AggregateVersionInterceptor` sets `Current = Original + 1` on every **Modified** root at
  `SavingChanges` (`src/ServiceDefaults/Persistence/AggregateVersionInterceptor.cs:93-104`); EF puts
  the original in the `WHERE`, so two writers who loaded the same version cannot both commit.
- `ChangeCameraAddressCommandHandler` refuses `changing.Version != expectedVersion` (the `If-Match`,
  `ChangeCameraAddressCommandHandler.cs:42-47`), then `ChangeAddress` modifies `Url` → the root is
  Modified → **exactly one bump per committed address change** (one aggregate, one save per request).
- `CameraRepository.SaveAsync` **dispatches the domain events into the outbox before the commit, in
  the same transaction** (`CameraRepository.cs:98-111`, spec 021 FR-001). Consequence 1: an event
  whose write loses the concurrency race is rolled back with it, so no two published address changes
  of one camera share a loaded version. Consequence 2: **at raise and dispatch time `Version` is
  still the loaded (pre-change) value** — the bump happens later, inside the commit.

So the loaded version at `ChangeAddress` is **strictly increasing across a camera's committed address
changes**: change *k* loaded `v_k` and committed `v_k + 1`; change *k+1* loads at least `v_k + 1`.
**[DECIDED]** that loaded value is what travels (plan §2.1): it is exactly what the domain sees, and
needs no knowledge of the interceptor's `+1` rule. It is named `PreviousVersion`, the sibling of the
record's existing `PreviousUrl` — "the camera's version before this change", which is also the
`If-Match` the PATCH named.

### 1.3 The contract, its rules and its guards

- `CameraAddressChangedV1` is a positional record of seven members, `Metadata` last
  (`src/Shared.Contracts/CameraCatalog/CameraAddressChangedV1.cs:28-35`). Prior art for a version on
  the wire: `WallSceneChangedV1.SceneVersion`, `ResolvedOverlayTextChangedV3.Version`.
- ADR-0073: "Additive non-breaking changes (a new optional field with a safe default that consumers
  can ignore) do NOT bump the version." A trailing `int? PreviousVersion = null` is exactly that:
  every existing positional/named construction keeps compiling (≈40 test sites, plus the publisher).
- **One** production deconstruction of the record exists and its arity moves from 7 to 8:
  `CameraAddressChangedIntegrationEventHandler.cs:31`. AuditObservability reads it by member access
  and rebuilds it with `with { … }` (`AuditPayloadRedaction.cs:33-44`), which carries the new member
  through. No other production reader.
- Guards: `AuditContractPropertyShapeTests` (a `Nullable<int>` unwraps to `int`, an accepted scalar:
  `tests/Architecture.Tests/AuditContractPropertyShapeTests.cs:15-25,99`),
  `AuditPayloadRedactionCoverageTests` (classifies `string`/`Uri` only — an `int?` needs no
  decision), `HandlerDeconstructionTests` (the new local must carry its own field's name). There is
  **no** `CameraAddressChangedV1Tests` in `tests/Shared.Contracts.Tests` today.
- Wire compatibility: no serializer is configured anywhere (`UnmappedMemberHandling`,
  `UseSystemTextJsonForSerialization`, `UseNewtonsoft…`: zero hits in `src`), so Wolverine 6.48.3's
  default applies. Under either System.Text.Json or Newtonsoft defaults, an **unknown** member is
  ignored (old consumer, new message) and a **missing** constructor parameter takes its default (new
  consumer, old message → `null`). Which serializer the pinned Wolverine uses is **not** asserted
  here (both strings are in `Wolverine.dll`); plan §8 makes it a measured task, not a claim.

### 1.4 No ADR is needed

The field is additive and optional (ADR-0073 says no version bump); the outbox is unchanged
(ADR-0088); the sequence is the existing aggregate version (ADR-0043/0113), read, not altered; the
only persistence change is an additive nullable column in StreamDistribution (ADR-0067, as spec 385
did). Removing the clamp is the owner's decision. Nothing here decides architecture.

### 1.5 Legacy rows and events in flight

- **Rows**: every `streams` row has `source_changed_at` (nullable, spec 385) and will get a null
  `source_camera_version`. **No backfill** — the versions live in camera-catalog-db.
- **Events in flight at deploy**: CameraCatalog's outbox and StreamDistribution's queue may hold
  `CameraAddressChangedV1` without the field. They deserialize with `PreviousVersion = null` and are
  ordered by `ChangedAt` exactly as today, minus the clamp (FR-006).
- **Idempotent replay**: a redelivery of the same change carries the same version; equal applies
  (FR-003), so a retry after a failed MediaMTX PATCH still finishes the re-point.

## 2. User stories

### US1 (P1) — A stream follows the catalogue's order of address changes, not its clock

As an operator, when I change a camera's address, the wall shows the address I set last, even if
CameraCatalog's clock stepped backwards between two changes, ran ahead, or an older change is
redelivered after a newer one — and a camera's stream cannot be left on an address the catalogue no
longer holds while the API answered 204.

**Independent test:** in-process, the real `CameraAddressChangedIntegrationEventHandler` over the
real `RepointStreamCommandHandler`, `InMemoryStreamRepository` and `FakeRtspGateway`, fed messages
**deserialized from JSON exactly as they arrive off the wire**: a later change stamped earlier ends
the stream on the later change. On the real stack: a `PATCH /cameras/{id}` with `If-Match: "v"`
records `source_camera_version = v` on the stream row (plan §8 R9).

## 3. Acceptance scenarios

```gherkin
Feature: Repoints are ordered by the camera version the change was made against

  Background:
    Given a provisioned stream for camera X at address O
    And messages are CameraAddressChangedV1 as deserialized from the wire

  # Happy path - the issue's exact example
  Scenario: a later change stamped earlier after a backward clock step applies
    When (Url=B, PreviousVersion=3, ChangedAt=08:10) is handled
    And (Url=C, PreviousVersion=4, ChangedAt=08:05) is handled
    Then the stream's source is C and its recorded camera version is 4
    And MediaMTX was told B, then C

  # Conflict - a stale retry after a newer change, with the clocks inverted
  Scenario: an older change redelivered after a newer one is ignored whatever its stamp
    Given the gateway fails the first re-point only
    When (Url=A, PreviousVersion=3, ChangedAt=t2) is handled and fails after saving A
    And (Url=B, PreviousVersion=4, ChangedAt=t1), with t1 < t2, is handled
    And (Url=A, PreviousVersion=3, ChangedAt=t2) is handled again
    Then the stream's source is B and the third handling succeeds
    And MediaMTX was told B only

  # Conflict - publisher clock beyond the old tolerance (spec 385 R-1)
  Scenario: a stale retry does not win when CameraCatalog runs six minutes fast
    Given the receiver's now is N and the gateway fails the first re-point only
    When (A, PreviousVersion=3, ChangedAt=N+6m) fails, then (B, 4, N+6m1s) applies
    And (A, 3, N+6m) is redelivered
    Then the stream's source is B

  # Conflict - publisher straddling the old tolerance (spec 385 R-3)
  Scenario: a genuine newer change is not lost to a clamp
    When (A, PreviousVersion=3, ChangedAt=N+4m59s) then (B, 4, N+5m3s) are handled
    Then the stream's source is B

  # The clamp is gone
  Scenario: a far-future stamp is recorded as given and pins nothing
    When (U, PreviousVersion=3, ChangedAt=MaxValue) is handled
    Then the stream's source is U, its recorded instant MaxValue, and nothing is logged at Warning
    When (B, PreviousVersion=4, ChangedAt=N) is handled
    Then the stream's source is B

  # Rolling deploy - a versioned change over a row recorded before this spec
  Scenario: the first versioned change applies over a version-less record, whatever its stamp
    Given the stream recorded a change without a version at ChangedAt=N
    When (B, PreviousVersion=0, ChangedAt=N-10m) is handled
    Then the stream's source is B and its recorded camera version is 0

  # Rolling deploy - a message without the field keeps today's ChangedAt behaviour
  Scenario: changes without a version are ordered by ChangedAt as before
    When (B, no version, t2) then (A, no version, t1) are handled, with t1 < t2
    Then the stream's source is B
    # every spec 385 ordering fact (CameraAddressChangedOrderingTests R1-R4, G1-G3) holds unmodified

  Scenario: a change without a version over a versioned record is compared by ChangedAt
    Given the stream recorded (B, PreviousVersion=4, ChangedAt=t2)
    When (C, no version, t3) is handled, with t3 > t2
    Then the stream's source is C and its recorded camera version is null
    When (D, no version, t1) is handled, with t1 < t3
    Then the stream's source is C

  # Replay - ADR-0142/0143 unchanged
  Scenario: a redelivery of the same change finishes a failed re-point
    Given the gateway fails the first re-point only
    When (A, PreviousVersion=3, t1) is handled (fails), then again
    Then the stream's source is A and MediaMTX was told A

  # Observability - origin only
  Scenario: an ignored older versioned change is logged at Information with its origin only
    Given the stream recorded (B, PreviousVersion=4)
    When (Url="rtsp://10.0.8.1:8554/SECRET-path?token=SECRET", PreviousVersion=3) is handled
    Then exactly one Information entry names camera X, origin "rtsp://10.0.8.1:8554" and version 3
    And no entry contains "SECRET", nothing is saved and the gateway is not called

  # Bad request - refused at the trust boundary before any lookup
  Scenario: a negative version is refused
    When (B, PreviousVersion=-1) is handled
    Then the handling fails, nothing is looked up or saved, and the gateway is not called
  Scenario: an unparseable address is still refused before ordering is considered
    When (Url="http://not-rtsp.example/stream", PreviousVersion=3) is handled
    Then the result is STREAM_INVALID_RTSP_SOURCE and nothing is looked up or saved

  # Publisher
  Scenario: CameraCatalog announces the version the change was made against
    Given camera X at version 5
    When its address is changed with If-Match "5"
    Then CameraAddressChangedDomainEvent.PreviousVersion is 5
    And the published CameraAddressChangedV1.PreviousVersion is 5

  # Auth - N/A at this boundary (FR-011)
```

## 4. Functional requirements

- **FR-001** `CameraAddressChangedV1` gains a trailing optional member `int? PreviousVersion = null`
  (ADR-0073 additive; **no `V2`**). Doc comment: the camera's aggregate version the change was made
  against — the `If-Match` it was accepted under — strictly increasing across a camera's committed
  address changes; `null` on messages published before this spec.
- **FR-002** CameraCatalog: `CameraAddressChangedDomainEvent` gains `AggregateVersion
  PreviousVersion`; `Camera.ChangeAddress` sets it to the camera's `Version` at the time of the
  change; `CameraAddressChangedDomainEventHandler` copies `.Value` to the contract. Nothing else in
  CameraCatalog changes (no migration: the column exists).
- **FR-003** StreamDistribution records, on `Stream`, the version of the address change it last
  applied, as a value object `SourceCameraVersion` (`SourceCameraVersion?`; null for rows written
  before this spec, for streams never re-pointed, and after a change that carried none).
- **FR-004** Ordering rule (`Stream.RepointTo`), first match wins:
  1. **Incoming has a version and the record has one** → ignore when incoming `<` recorded;
     otherwise (equal or newer) apply. `ChangedAt` is not consulted.
  2. **Incoming has a version, the record has none** → apply. **[DECIDED]** (plan §2.3): a
     versioned change is by construction from the new CameraCatalog, so it outranks any record a
     wall clock produced; this is what lets the clamp go without leaving a legacy-shaped stamp able
     to pin a stream.
  3. **Incoming has no version** → today's rule: ignore when `ChangedAt` `<` the recorded
     `SourceChangedAt`; otherwise apply (equal applies, null applies) — spec 385 FR-002-FR-005.
  On apply: `SourceUrl`, `SourceChangedAt` (always, as given) and `SourceCameraVersion` (the
  incoming version, **or null** when it carried none — **[DECIDED]**, plan §2.3) are all set.
- **FR-005** The clamp is removed: `ChangedAtPolicy`, `ChangedAtClamp`, `StreamRepointOptions`, the
  `StreamRepoint` binding and validation, `ChangedAtPair`, and the `ClampedFutureChangedAt` and
  `IgnoredAgainstFutureRecordedStamp` log messages. Spec 385 FR-010-FR-013 are superseded. No stamp
  is altered by the receiver.
- **FR-006** A message **without** `PreviousVersion` is ordered by `ChangedAt` exactly as spec 385
  FR-001-FR-005 specify (rule 3); the clamp no longer applies to it either. **[DECIDED]**: "keeps the
  existing `ChangedAt` behaviour" is read as spec 385's ordering, not its §8 clamp, because the owner
  removed the clamp; rule 2 is why that is safe (plan §2.3).
- **FR-007** An ignored versioned change is acked: success, no save, no gateway call, one
  **Information** line naming the camera, the URL's **origin only** (`UrlRedaction.ToOrigin`) and the
  incoming version. The legacy-path ignore line (`IgnoredOlderRepoint`) keeps its exact text.
- **FR-008** A negative `PreviousVersion` is refused at the trust boundary (the integration event
  handler) before any lookup: the handling fails and the message is not applied. A version is never
  negative (`AggregateVersion.From` refuses it), so only a malformed or forged message carries one.
- **FR-009** Outbox, retry and idempotency semantics (ADR-0142 / ADR-0143) are unchanged: a gateway
  failure still throws for a retry; a failed re-point is still finished by its own redelivery; the
  retired and no-stream skips, and their order relative to ordering, are unchanged.
- **FR-010** `Stream.Provision` sets no version (as spec 385 FR-009 for the instant): null applies.
- **FR-011** Auth: N/A at this boundary — a RabbitMQ subscriber; no scope, fab check or caller
  identity is added or removed. The publisher's PATCH already requires `If-Match` and its scope; it
  is unchanged.

## 5. Out of scope

- Per-service broker publish permissions (an ADR; §8 R-V1 is why it matters more now).
- An operator path to clear a recorded version or stamp.
- An address change arriving before its registration (pre-existing; spec 385 §5).
- Inter-replica MediaMTX PATCH interleaving (spec 378's sweep repairs MediaMTX to the database).
- Retiring the legacy rule-3 path. Once no message without the field can be in flight it is dead
  code for genuine traffic, but it is the owner's rolling-deploy requirement; deleting it later is a
  separate, behaviour-changing issue.

## 6. Locked choices

.NET 10; `Shared.Contracts` record with a trailing optional parameter (ADR-0073); EF Core + Npgsql,
additive nullable `integer` column `source_camera_version` on `streams` via an ADR-0067 migration;
Wolverine subscriber and outbox unchanged; xUnit + Shouldly + hand-written fakes (ADR-0052); Aspire
fixture for the one integration fact (ADR-0103).

## 7. Independent end-to-end test procedure

1. Unit, no Docker:
   `dotnet test tests/StreamDistribution.Application.Tests -c Release --filter "FullyQualifiedName~CameraAddressChangedVersionOrderingTests|FullyQualifiedName~CameraAddressChangedOrderingTests"`
   — the §3 stream scenarios over the real handlers, messages deserialized from JSON; the spec 385
   facts unmodified.
2. Contract and publisher:
   `dotnet test tests/Shared.Contracts.Tests -c Release --filter "FullyQualifiedName~CameraAddressChangedV1Tests"`,
   `dotnet test tests/CameraCatalog.Domain.Tests -c Release --filter "FullyQualifiedName~CameraAddressChange"`,
   `dotnet test tests/CameraCatalog.Application.Tests -c Release --filter "FullyQualifiedName~CameraAddressChangedDomainEventHandlerTests"`.
3. Model: `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~StreamDistributionModelCharacterisationTests"`.
4. Stack (CI-authoritative): `RepointStreamIntegrationTests.Correcting_a_cameras_address_records_the_camera_version_it_was_made_against`
   — a real `PATCH` with `If-Match: "v"` records `source_camera_version = v`, proving the field
   crossed the broker under Wolverine's real serializer and the migration ran.

## 8. Residuals (for the security review; not open questions)

- **R-V1 — a forged high version pins a stream.** The broker has one shared credential (spec 385
  §8: `AppHost.cs:119` plus every `.WithReference(rabbitmq)`). A holder can publish
  `PreviousVersion = int.MaxValue`; the stream records it and every later genuine change (a lower
  version) is ignored until an operator intervenes (SQL, or retiring and re-registering the camera).
  The clamp bounded the **timestamp** form of this attack to the tolerance; a version has no
  receiver-side "now" to clamp against, so removing the clamp (the owner's decision) leaves the
  **version** form unbounded. The 2026-10-16 comment said the version "removes the future-stamp
  class too": true for a fast or stepped **clock**, not for **forgery**. The legacy-shaped form is
  bounded (rule 2: the next genuine change applies). The cure is per-service publish permissions
  (§5, an ADR). Accepted as a consequence of the decision **only if the security review concurs**;
  otherwise phase 6 reports it to the owner rather than inventing a bound.
- **R-V2 — CameraCatalog replicas of two code versions overlapping.** Rule 2 lets a stale
  versioned retry (from a new replica) apply over a newer change from an old replica recorded
  without a version; FR-004's "null when it carried none" on apply lets the same happen after a
  legacy change applies over a versioned record. Both need old and new CameraCatalog publishing changes to
  one camera within seconds, plus a redelivery. Bounded to the deploy window, and there is no
  production deployment yet. Deploy StreamDistribution **before** CameraCatalog (plan §7) so no
  versioned message ever meets an old consumer.
- **R-V3 — a far-future `ChangedAt` on the legacy path** is now recorded as given; it ignores
  later **legacy** changes only, and the first versioned change clears it (rule 2).
