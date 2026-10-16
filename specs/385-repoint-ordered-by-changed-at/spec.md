# Spec 385: An older address change cannot re-point a stream back over a newer one

**Feature Branch**: `fix/2991-repoint-ordered-by-changedat` (cut from `origin/develop` at `bd04dd8e`)

**Created**: 2026-10-16

**Status**: Implemented including the §8 clamp (T030-T032), CI pending (T021 review outstanding; T005 adversarial done, its residuals are in §8). Accepted residual: a backward clock step in CameraCatalog (#3008); an operator path to clear the stamp is not in scope. No `[NEEDS CLARIFICATION]` remains: the owner's delegated decision on #2991
(2026-10-16, last comment) settles every behavioural question. Judgement calls the decision left
to the architect are marked **[DECIDED]** with their reason; none is new architecture.

**Input**: Issue [#2991](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2991),
*An outbox retry of an older address change rewrites the stream back to that address*. Split out of
#2960 / spec 378.

**Owner decision (2026-10-16, on #2991), recorded here and not re-opened:** order repoints by the
event's own `ChangedAt`; **no contract change**. `CameraAddressChangedV1` already carries
`ChangedAt`, stamped by CameraCatalog — the single writer of a camera's address
(`src/CameraCatalog/Domain/Camera/Camera.cs:145`, `clock.UtcNow`). Rejected: matching on
`PreviousUrl` (drops a legitimately reordered pair) and a per-camera sequence on a `V2` contract
(ADR-0073; heavier than needed).

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (an older repoint is now ignored; today it
applies): phase 4a is **RED** (ADR-0139). **Adversarial pass required** (`test-adversary`): the
failure mode is a race. **Security review required**: stream attribution (a tile showing a camera's
old address — possibly another device — under the camera's identity).

**Spec number.** 385. Checked 2026-10-16 against `origin/develop` (highest: 383), every local and
remote head (`test/3001-crossfab-devices-webhook-403` holds 386; 384 is reserved for #2998 by a
sibling architect and is on no head yet). Nothing claims 385. Re-check before the PR is opened.

**ADRs.** ADR-0073 (integration-event versioning — why no `V2` is needed: no field is added),
ADR-0040 (domain vs integration events; the contract stays primitive, the value object stays inside
the context), ADR-0139 / ADR-0140 (constitution §II: `DateTimeOffset` is a banned primitive on a
domain model, so the recorded instant is a value object), ADR-0048 / ADR-0141 (a persisted absence
is a nullable value-object property, not `Option<T>`), ADR-0043 / ADR-0113 (the `version`
concurrency token that already detects a lost update), ADR-0067 (the `MigrationRunner` applies the
new migration), ADR-0142 / ADR-0143 (idempotency and retry semantics — **unchanged**), ADR-0050
(logging), ADR-0103 (no Testcontainers), ADR-0105 (`Ensure.That`), ADR-0036 (smallest change),
ADR-0144 (the lane implements, it does not decide). Spec 378 (the reconciler repairs MediaMTX to
the database) and spec 029 (FR-013/FR-014, the repoint itself) are prior art. **No new ADR is
needed**: the owner made the one decision there was.

**Latency budget.** N/A. Re-pointing is control-plane (an RTSP source correction via MediaMTX's
config API), on no constitution §IV leg.

---

## 1. Premise, verified (not assumed)

Read on `bd04dd8e`:

- `CameraAddressChangedIntegrationEventHandler.Handle` builds `RepointStreamCommand(camera, url)` —
  `ChangedAt` is deconstructed away (`var (cameraId, _, _, url, _, _, _) = message;`) — and throws
  on any failure so the message is retried.
- `RepointStreamCommandHandler` calls `stream.RepointTo(sourceUrl, clock)`, **saves**, then calls
  `IRtspGateway.RepointPathAsync`; an `HttpRequestException` there returns
  `RtspGatewayUnavailable` *after* the save.
- `Stream.RepointTo` is idempotent only on an **equal** URL; there is no ordering of any kind.

**The sequence, reproduced in-process** (the test that is RED today, plan §8 R1): a seeded stream
at `O`; a gateway that throws `HttpRequestException` on its first `RepointPathAsync` only; then

1. `Handle(CameraAddressChangedV1(Url = A, ChangedAt = t1))` → the aggregate is saved at `A`, the
   PATCH fails, the handler throws `InvalidOperationException` (the retry signal).
2. `Handle(Url = B, ChangedAt = t2 > t1)` → aggregate `B`, gateway told `B`.
3. `Handle(Url = A, ChangedAt = t1)` again (the retry) → **today**: `B != A`, so the aggregate is
   saved back to `A` and the gateway is told `A`.

Expected after this spec: aggregate `B`, gateway calls exactly `[B]` (the failed first call is not
recorded by `FakeRtspGateway`, whose hook runs before it records). Today: `SourceUrl == A`,
gateway calls `[B, A]` — fails on the first assertion.

**What reorders the deliveries is not load-bearing.** Single listener, `Inline` mode
(`WolverineDefaults`, StreamDistribution passes no `listenerCount`) narrows the in-process window,
but redelivery after a crash between save and ack, a dead-letter replay, a second replica, or the
publisher's outbox relay can each hand StreamDistribution an older change after a newer one. The
defect is that the handler's outcome depends on arrival order; that is what is fixed.

## 2. User stories

### US1 (P1) — A stream ends at the camera's newest address whatever order the changes arrive in

As an operator, when a camera's address is changed more than once, the wall shows that camera's
**latest** address, even if an older change is retried or redelivered after a newer one — so a
tile never shows an old address (possibly another device) under the camera's identity.

**Independent test:** the in-process harness of §1 (handler + `Stream` + a gateway that fails
once) ends with aggregate and gateway at `B`; on the real stack, an address change records its
`ChangedAt` on the stream row (plan §8 R6).

## 3. Acceptance scenarios

```gherkin
Feature: Repoints are ordered by the address change's own ChangedAt

  Background:
    Given a provisioned stream for camera X whose recorded change instant is <recorded>

  # Happy path — the issue's exact sequence
  Scenario: an older change retried after a newer one is ignored
    Given the gateway fails the first re-point only
    When CameraAddressChangedV1(Url=A, ChangedAt=t1) is handled and fails after saving A
    And CameraAddressChangedV1(Url=B, ChangedAt=t2) is handled, with t2 > t1
    And CameraAddressChangedV1(Url=A, ChangedAt=t1) is handled again
    Then the stream's source is B
    And MediaMTX was last told B, and never told A after B
    And the third handling succeeds (the message is not retried again)

  # Conflict — out-of-order arrival, null recorded value
  Scenario: an older change arriving after a newer one on a stream with no recorded instant
    Given <recorded> is null
    When B at t2 is handled, then A at t1
    Then the stream's source is B and its recorded instant is t2
    And the gateway was told B only

  # Conflict — a newer change back to the address already held still records
  Scenario: a newer change to the current address supersedes an older one
    Given the stream's source is O and <recorded> is null
    When CameraAddressChangedV1(Url=O, ChangedAt=t2) is handled, then (Url=B, ChangedAt=t1)
    Then the stream's source is O and its recorded instant is t2

  # Equal timestamps — pinned: the later-arriving of equals wins
  Scenario: two changes with the same ChangedAt apply in arrival order
    When B at t1 is handled, then C at t1
    Then the stream's source is C and the gateway was told B, then C

  # Replay — the retry that finishes a failed re-point still works (ADR-0142/0143 unchanged)
  Scenario: a redelivered change after a failed PATCH finishes the re-point
    Given the gateway fails the first re-point only
    When A at t1 is handled (fails), then A at t1 again
    Then the stream's source is A and the gateway was told A

  Scenario: a redelivered change after a successful PATCH patches again and changes nothing
    When A at t1 is handled twice
    Then the stream's source is A, its recorded instant t1, and the gateway was told A twice

  # Bad request — unchanged: an address this context rejects is refused before any lookup
  Scenario: an unparseable address is refused before ordering is considered
    When CameraAddressChangedV1(Url="http://not-rtsp.example/stream") is handled
    Then the result is STREAM_INVALID_RTSP_SOURCE and nothing is looked up or saved

  # Observability / attribution — the ignored change is visible without leaking the address
  Scenario: an ignored change is logged at Information with its origin only
    Given the stream recorded B at t2
    When Url="rtsp://10.0.8.1:8554/SECRET-path?token=SECRET" at t1 is handled
    Then exactly one Information entry names camera X, origin "rtsp://10.0.8.1:8554" and t1
    And no log entry contains "SECRET"
    And nothing is saved and the gateway is not called

  # Auth — N/A at this boundary (see FR-008)
```

## 4. Functional requirements

- **FR-001** `Stream` records the `ChangedAt` of the address change it last applied, as a value
  object `SourceChangedAt`; nullable (`SourceChangedAt?`) for rows written before this change and
  for streams never re-pointed.
- **FR-002** A repoint whose `ChangedAt` is **older** than the recorded value is ignored: success,
  no aggregate change, no save, no gateway call, one Information log line carrying the URL's
  **origin only** via `Shared.Kernel` `UrlRedaction.ToOrigin`.
- **FR-003** A repoint whose `ChangedAt` is **equal or newer** applies and records it — including
  when the URL equals the current source (the record still advances).
- **FR-004** A **null** recorded value applies and records.
- **FR-005** Equal `ChangedAt`: the later-arriving change wins. Pinned by a test.
- **FR-006** No contract change: `CameraAddressChangedV1` is unchanged and no `V2` is introduced.
- **FR-007** Outbox, retry and idempotency semantics (ADR-0142 / ADR-0143) are unchanged: a
  gateway failure still throws for a retry, a failed re-point is still finished by its own retry,
  the retired / no-stream skips are unchanged.
- **FR-008** Auth: N/A at this boundary. The handler is a RabbitMQ subscriber, not an HTTP
  endpoint; no scope, fab check or caller identity is added or removed (the existing "no fab check"
  reasoning in `CameraAddressChangedIntegrationEventHandler` stands).
- **FR-009** **[DECIDED]** Only a repoint records the instant. `Stream.Provision` (from
  `CameraRegisteredV1`) leaves it null — minimal, and safe: no address change can carry a
  `ChangedAt` earlier than the camera's registration, and a null applies.

## 5. Out of scope

- A `CameraAddressChangedV1` that arrives **before** its `CameraRegisteredV1` is still answered
  "no stream; nothing to re-point" and the stream is later provisioned at the registered address.
  Pre-existing; not this defect. Recorded in plan §9 as a follow-up candidate, not fixed here.
- Two replicas interleaving their **MediaMTX PATCHes** (DB right, MediaMTX briefly wrong) — spec
  378's sweep repairs MediaMTX to the database value, which after this spec is the right one.
- CameraCatalog clock monotonicity (plan §9).

## 6. Locked choices

.NET 10, EF Core + Npgsql (additive nullable `timestamp with time zone` column, ADR-0067 migration
via the `MigrationRunner`), Wolverine subscriber unchanged, xUnit + Shouldly + hand-written fakes
(ADR-0052), Aspire fixture for the one integration fact (ADR-0103).

## 7. Independent end-to-end test procedure

1. Unit (no Docker): `dotnet test tests/StreamDistribution.Application.Tests -c Release --filter
   "FullyQualifiedName~CameraAddressChangedOrderingTests"` — the §3 scenarios against the real
   handlers, `InMemoryStreamRepository` and `FakeRtspGateway`.
2. Model: `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter
   "FullyQualifiedName~StreamDistributionModelCharacterisationTests"` — the `source_changed_at`
   column is in the live model and in the committed snapshot.
3. Stack (CI authoritative): `RepointStreamIntegrationTests` — a real `PATCH /cameras/{id}` records
   a non-null `source_changed_at` inside the request window, proving the migration ran in the
   integration lane and the instant travelled from CameraCatalog.

---

## 8. Addendum (2026-10-16): a future `ChangedAt` must not pin a stream

Owner-delegated decision on #2991 (last comment), from the security review of the implementation.
Trusting `ChangedAt` has two consequences.

**(1) A future stamp pins a stream - IN SCOPE.** The broker has one shared credential
(`AppHost.cs:119` plus every `.WithReference(rabbitmq)`), so anything holding it can publish a
`CameraAddressChangedV1` with `ChangedAt = DateTimeOffset.MaxValue`; the stream records it, every
later legitimate correction is ignored, and spec 378's sweep keeps forcing MediaMTX back to the
attacker's URL. A fast CameraCatalog clock does the same non-adversarially. Before this spec the
next legitimate change undid a hijack; with it, the hijack would stick.

- **FR-010** The repoint handler compares the incoming `ChangedAt` with the receiving `IClock`'s
  `UtcNow`. A stamp **more than the tolerance ahead** of that now is **clamped to now** before it is
  compared with the recorded stamp and before it is recorded. Clamp, not reject: a genuine change
  from a slightly skewed clock still applies. A stamp exactly at `now + tolerance` is not clamped;
  one tick beyond is.
- **FR-011** Tolerance is configuration, `StreamRepoint:ChangedAtTolerance`, default `00:05:00`,
  validated `>= 0` and `<= 1 day`, `ValidateOnStart`, registered like `MediaMtxSourceRepairOptions`.
  Zero clamps every future stamp. The new options registration is an inherent, owner-authorised
  addition to `StreamDistributionRegistrationCharacterisationTests`' expected list (one
  `IConfigureOptions<>` and one `IValidateOptions<>` entry).
- **FR-012** A clamp is logged at **Warning**: camera id, the URL's **origin only**
  (`UrlRedaction.ToOrigin`), the stamp received and the receiver's now.
- **FR-013** The ignore log line (FR-002) is **Warning** when the recorded stamp is later than the
  receiver's now **plus the tolerance** (a poisoned stamp must not read like a routine reorder; a
  recorded stamp merely a few seconds ahead is routine skew, as test C6 pins), and that Warning
  carries both the incoming and the **recorded** stamp. Otherwise it stays the routine Information
  line with its existing text and the incoming stamp only. The origin-only rule is unchanged.

```gherkin
  Scenario: a maximum stamp is clamped, the repoint applies, the stream is not pinned
    Given the receiver's now is N
    When Url=U at ChangedAt=MaxValue is handled
    Then the stream's source is U and its recorded instant is N (not MaxValue)
    And one Warning names the camera, U's origin, MaxValue and N, and no entry contains the secret
    When Url=B at ChangedAt=N+1s is handled
    Then the stream's source is B

  Scenario: within tolerance nothing is clamped
    When ChangedAt=N+4min and ChangedAt=N+5min are handled (default tolerance)
    Then each is recorded as given and nothing is logged at Warning
    But ChangedAt=N+5min+1tick is clamped to N and a Warning is logged

  Scenario: a poisoned recorded stamp is loud
    Given the recorded stamp is N+1h
    When an older change is handled
    Then it is ignored and one Warning carries both stamps

  Scenario: tolerance 0 clamps any future stamp;  negative or > 1 day is refused at start
```

**(2) A backward clock step in CameraCatalog - accepted residual, NOT in scope.** NTP/PTP
correction makes wall-clock order non-monotonic, so a genuine fresh change stamped earlier than the
recorded one is silently dropped. The fix is ordering by a sequence the database serialises (the
camera aggregate's version carried on the event: a contract change, `V2`, ADR-0073). Recorded as an
accepted residual in the PR and tracked as a **follow-up decision issue**.

**Accepted residuals of the clamp (adversarial pass, T005).**

- **R-1** If CameraCatalog's clock runs **more than the tolerance ahead**, every stamp is clamped to
  handling time, so ordering follows handling order and a stale retry can win again. Rejecting
  instead is worse (the change is lost in every schedule); no clamp lets a forged `MaxValue` pin
  forever. The only cure is the sequence-number ordering tracked in #3008, which would delete the
  clamp and the option.
- **R-2** A stamp at exactly `now + tolerance` can pin a stream for up to the tolerance (bounded,
  5 minutes by default), and a broker-credential holder can re-pin repeatedly, leaving only
  Information lines. Such a holder can already repoint to any URL, so this adds nothing beyond
  persistence.
- **R-3** A publisher clock that runs ahead by an amount **straddling the tolerance** can lose a
  genuine newer change even with in-order delivery and no failures. Example: tolerance 5 min,
  publisher about 5 min ahead; change A stamped real+4:59 is kept as is; change B arrives 2 s later
  stamped real+5:03 is clamped to real+0:02, which ranks below A, so B is ignored (acked, never
  redelivered; the log shows one clamp Warning plus a routine Information "older" line). Distinct
  from R-1 (clock fully beyond the tolerance: ordering degrades to handling order and a stale retry
  can win). Clamping to now+tolerance instead would fix it but turns C1, C2 and C4-one-tick red
  (C2 = a legitimate change after a forged MaxValue applies at once), and FR-010 fixes "clamp to
  now", so changing it is an owner decision. Exposure is bounded by the tolerance. The
  sequence-number cure in #3008 removes it too.

Pre-existing or minor, not fixed here: a change arriving before its registration is acked and
dropped (separate queues per event type, `WolverineDefaults.cs:110`); a concurrent consumer's late
PATCH is bounded by the 10 s repair sweep (`listenerCount` is 1 today); a tie means the last handled
wins; Postgres precision rounds UP before year 2000 (harmless for real instants); recovering from a
future stamp already recorded is SQL only for the operator.

Out of scope too: an operator path to clear `source_changed_at`, and per-service broker publish
permissions (an ADR).

**Lane.** Behaviour-changing, phase 4a RED. Tests: `CameraAddressChangedFutureStampTests` (C1-C6,
compile today, default tolerance) and, pending the options type, C7 (tolerance zero) and C8
(validation) as untracked `*.cs.pending` files. No existing assertion changes (plan §10).
