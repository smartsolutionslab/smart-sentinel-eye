# Spec 309 — A path that outlived its row

**Issue:** [#2658](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2658)
— *An orphaned MediaMTX path (provisioning failed after AddPathAsync) admits any fab through WHEP
authorize*. Label `agent:ready`; Project #13, status **In Progress** (verified 2026-10-07,
`gh issue view 2658 --json projectItems`).
**Branch:** `fix/2658-orphaned-mediamtx-path` (cut from `origin/develop` @ `0d1864f5`)
**Created:** 2026-10-07
**Lane:** autonomous (ADR-0144).

**Spec number.** On 2026-10-07 `origin/develop` tops out at **308**. The only open PR
(#2739, `docs/2544-layout-effect-token-sync`) carries nothing above 308; none of the 25 most
recently committed remote branches carries `specs/309-*`. **309** is free *today*. Re-check after
every parked-PR merge and immediately before opening the PR (memory: *spec number: origin/develop
isn't enough*) — the main checkout is concurrently on another spec's branch.

**The design decision was taken by a human before this spec**, recorded on the issue
([comment 6032165587](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2658#issuecomment-6032165587)):

> Reorder provisioning instead of refusing in the WHEP hook. `ProvisionStreamCommandHandler` saves
> the DB record before calling `AddPathAsync`, and treats "path already exists" as success, so
> provisioning failure after the path is created no longer leaves an orphaned, unregistered path
> admitting any fab. Mechanical fix, no new ADR.

This spec decides nothing beyond that. It names the exact step order, what "already exists is
success" means in the gateway contract, and the one consequence the decision implies but does not
spell out (§1.3: a redelivery must re-assert the path, or save-first trades a security gap for a
liveness one).

## 1. The issue's premise, re-checked on this tree (`0d1864f5`)

### 1.1 Confirmed

- `ProvisionStreamCommandHandler.HandleAsync`
  (`src/StreamDistribution/Application/Commands/Handlers/ProvisionStreamCommandHandler.cs:48-61`):
  `streams.Add(stream)` → `rtsp.AddPathAsync(...)` → `streams.SaveAsync(...)`. A throw from
  `SaveAsync` leaves a live MediaMTX path with no `streams` row. Confirmed.
- `MediaMtxRtspGateway.AddPathAsync`
  (`src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs:19-36`) posts
  `/v3/config/paths/add/{name}` and calls `EnsureSuccessStatusCode()` — MediaMTX answers **4xx for
  an existing path**, so a redelivery throws `HttpRequestException` and the handler returns
  `RtspGatewayUnavailable`. Confirmed; the module's own retry comment
  (`StreamDistributionInfrastructureModule.cs:99-106`) records the same 4xx.
- `AuthorizeWhepCommandHandler.cs:89-104`: `!stream.HasValue` falls through to `Success(path)`.
  Confirmed, and ADR-0161 §Decision records exactly this orphan gap as *filed separately*.
- `MediaMtxReconciler` is the only orphan sweeper, and it runs once per StreamDistribution start.
  Confirmed.

### 1.2 A second trigger the issue does not name — closed by the same change

The MediaMTX client opts into `RetryEveryMethod()` (ADR-0143). If the first `POST add/` succeeds but
its response is lost (timeout, reset), the resilience handler's retry receives the 4xx, the
gateway throws, the handler returns `RtspGatewayUnavailable` **without saving** — another orphan,
with no DB fault at all. Treating "already exists" as success (FR-002) closes it.

### 1.3 The consequence the decision implies

Save-first alone is not enough. Today a redelivery with an existing row short-circuits to
`Success(existing.Id)` without touching MediaMTX (`ProvisionStreamCommandHandler.cs:41-46`). After
the reorder, "row saved, `AddPathAsync` failed" becomes the new partial state; Wolverine redelivers
(`CameraRegisteredIntegrationEventHandler` re-throws on failure), the redelivery finds the row and
short-circuits — **the path is never created** until the next StreamDistribution restart, and the
health watcher reports the camera broken in the meantime. So a redelivery onto an existing,
non-retired row must re-assert the path; FR-002 is what makes that safe.

## 2. User stories

### US1 (P1): A failed provisioning never leaves video reachable by every fab

As a fab operator, I need a camera whose provisioning failed half-way to be unwatchable by callers
outside my fab, so a database blip cannot expose my plant's video to another plant.

**Why P1 / the only story:** it is the whole issue; nothing smaller ships the fix.

**Independent test:** §7.

#### Acceptance scenarios

```gherkin
Feature: Provisioning a stream never strands an unregistered MediaMTX path

  Background:
    Given a camera registered in fab "munich" with RTSP source "rtsp://10.0.5.1/h264"

  # Happy path
  Scenario: Provisioning saves the stream, then registers its path
    When StreamDistribution provisions the camera's stream
    Then the stream row is saved before MediaMTX is asked to add "cam-<camera>"
    And MediaMTX has path "cam-<camera>"
    And the result is the new stream's identifier

  # Failure: the trigger in the issue
  Scenario: The save fails
    Given saving the stream row throws
    When StreamDistribution provisions the camera's stream
    Then MediaMTX is never asked to add "cam-<camera>"
    And the failure propagates so the outbox redelivers

  # Failure after the save: the new partial state
  Scenario: MediaMTX is unreachable after the save
    Given MediaMTX refuses the connection
    When StreamDistribution provisions the camera's stream
    Then the stream row exists, attributed to fab "munich"
    And the result is RtspGatewayUnavailable (503)

  # Conflict: redelivery heals the partial state
  Scenario: A redelivery after a failed path registration
    Given a previous attempt saved the stream but failed to add its path
    And MediaMTX is reachable again
    When CameraRegisteredV1 is redelivered
    Then MediaMTX is asked to add "cam-<camera>"
    And the result is the same stream identifier as before
    And there is still exactly one stream row for the camera

  # Conflict: the path already exists
  Scenario: Adding a path MediaMTX already has
    Given MediaMTX already has path "cam-<camera>"
    When the gateway adds "cam-<camera>"
    Then it completes without error
    And MediaMTX still has exactly one path "cam-<camera>"

  # Bad request: a 4xx that is not "already exists" still fails
  Scenario: MediaMTX rejects the add for another reason
    Given MediaMTX does not have path "cam-<camera>"
    When the gateway adds "cam-<camera>" with a source MediaMTX rejects
    Then the gateway throws HttpRequestException

  # Retired stream: re-registration does not resurrect a path
  Scenario: A redelivery for a retired stream
    Given the camera's stream is Retired
    When CameraRegisteredV1 is redelivered
    Then MediaMTX is not asked to add "cam-<camera>"
    And the result is the retired stream's identifier

  # Auth: unchanged, and stated so nobody "fixes" it here
  Scenario: The WHEP hook's treatment of an unregistered path is unchanged
    Given no stream row exists for path "cam-<random guid>"
    When a caller holding sse.streams.read authorizes a WHEP read for it
    Then the hook answers 200, as WhepAuthIntegrationTests already asserts
```

## 3. Scope

### 3.1 In

- `ProvisionStreamCommandHandler` step order and existing-row branch.
- `MediaMtxRtspGateway.AddPathAsync`: an already-existing path is success; the `IRtspGateway`
  contract documents it.
- The `RetryEveryMethod()` justification comment in `StreamDistributionInfrastructureModule.cs`,
  which describes the 4xx as an error and becomes false.
- Tests per §6.

### 3.2 Out, and why

- **`AuthorizeWhepCommandHandler` and `WhepAuthIntegrationTests`** — untouched. Refusing unregistered
  paths in the hook was the *other* direction; the human decision rejected it. The fact
  `Authorize_with_a_valid_admin_token_returns_200` (an unregistered `cam-{guid}` admitted) stays
  byte-identical.
- **ADR-0161's "Filed separately" paragraph** — the lane may not edit an ADR (ADR-0144). The PR body
  states that #2658 closes the gap that paragraph records; whether to annotate the ADR is a human's
  call.
- **A periodic reconciler.** The decision removes the producer of orphans; a sweeper would be a new
  mechanism nobody decided on.
- **Re-pointing an existing path whose source differs.** "Already exists" is accepted as-is (§8 A2).
- **Orphans created before this ships.** Deploying restarts StreamDistribution, and
  `MediaMtxReconciler`'s startup pass removes every `cam-*` path with no row. No migration needed.

## 4. Functional requirements

- **FR-001** `ProvisionStreamCommandHandler` persists a new `Stream` (`streams.SaveAsync`) **before**
  calling `IRtspGateway.AddPathAsync`. A throw from `SaveAsync` propagates (as today) and
  `AddPathAsync` is not called.
- **FR-002** `IRtspGateway.AddPathAsync` is idempotent on the path name: if MediaMTX already has the
  path, the call completes successfully. Any other non-success response still throws
  `HttpRequestException`.
- **FR-003** When `AddPathAsync` throws `HttpRequestException` after the save, the handler returns
  `RtspGatewayUnavailable` (503) as today; the saved row stays.
- **FR-004** When `GetByCameraAsync` finds an existing stream that is **not Retired**, the handler
  calls `AddPathAsync(existing.Path, existing.SourceUrl.Value, …)` and returns `Success(existing.Id)`,
  or `RtspGatewayUnavailable` if that call throws. When the existing stream **is Retired**, it
  returns `Success(existing.Id)` without calling the gateway (today's behaviour for every existing
  row).
- **FR-005** No change to the WHEP authorize hook or its tests.

## 5. No new ADR

The human decision says so, and it is right on the merits: ADR-0161 §Decision already identifies
this exact gap ("`ProvisionStreamCommandHandler` calls `MediaMtxRtspGateway.AddPathAsync` before
`streams.SaveAsync` … **Filed separately**") and names "reordering provisioning to
save-before-register" as one of the two closures. Choosing between two closures an accepted ADR
already enumerated, for a defect it already recorded, is applying existing policy — the same
save-first order `RetireStreamCommandHandler` (spec 028 FR-008a) already uses. ADR-0143's
`RetryEveryMethod()` justification for this client is *strengthened*, not changed: `add/` becomes
idempotent in fact rather than "answers 4xx, so not retried anyway".

## 6. Phase-4a colour: **RED**

Behaviour-changing: the step order, the existing-row branch, and the gateway's response to an
existing path all change. New tests must be observed failing for the stated reason first.

**Declared assertion edits** (each is the old behaviour this issue removes, not a weakened gate):

1. `ProvisionStreamCommandHandlerTests.Provision_when_the_RTSP_gateway_is_unreachable_returns_RtspGatewayUnavailable`
   — `streams.SaveCallCount.ShouldBe(0)` becomes `ShouldBe(1)` and gains `streams.Streams.Count.ShouldBe(1)`
   (FR-003: the row now survives a gateway failure).
2. `ProvisionStreamCommandHandlerTests.Provision_for_an_existing_camera_returns_the_existing_identifier_and_does_not_re_register`
   — renamed `..._and_re_asserts_its_path`; `gateway.AddCalls.Count.ShouldBe(1)` becomes `ShouldBe(2)`
   (FR-004). Identifier and row-count assertions unchanged.
3. `CameraRegisteredIntegrationEventHandlerTests.On_redelivery_is_idempotent_because_the_command_handler_is`
   — same scenario as #2, reached through the integration-event handler rather than the command
   handler directly: a redelivered `CameraRegisteredV1` for an existing, non-Retired stream now
   re-asserts its MediaMTX path. Originally and incorrectly listed below as characterisation —
   missed when this table was first drafted, corrected under issue #2658. Renamed
   `On_redelivery_re_asserts_the_path_because_the_command_handler_does`;
   `gateway.AddCalls.Count.ShouldBe(1)` becomes `ShouldBe(2)`. Row-count assertion unchanged.

**Characterisation (green before and after, unmodified):** the rest of
`ProvisionStreamCommandHandlerTests` and the rest of `CameraRegisteredIntegrationEventHandlerTests`,
`ProvisionStreamIntegrationTests` (including its redelivery fact, which after FR-004 exercises
FR-002 against real MediaMTX), `MediaMtxReconcilerIntegrationTests`, `RetireStreamIntegrationTests`,
`WhepAuthIntegrationTests`.

## 7. Independent end-to-end test procedure (phase 5)

1. Boot the Aspire stack.
2. Run the new `MediaMtxRtspGatewayIntegrationTests` and the StreamDistribution integration classes
   above against real MediaMTX `1.21.0-ffmpeg` and Postgres — the gateway's existing-path answer is
   MediaMTX's, not a fake's.
3. Register a camera via `POST /cameras`; confirm `GET /streams?camera=…` returns its stream and
   `GET {mediamtx:api}/v3/config/paths/get/cam-<id>` answers 200.
4. Replay the camera's `CameraRegisteredV1` (or re-run the handler via the redelivery integration
   fact); confirm one row, one path, success, and an Information log "stream already exists".
5. Quote the outputs in the verification note. Latency: **N/A** (below).

## 8. Latency budget

**N/A.** Provisioning is control-plane work triggered by camera registration; no leg of the
event → overlay path (constitution §IV) runs through it.

## 9. Risks and assumptions

- **A1 (verified at phase 4a, not assumed):** MediaMTX 1.21.0 answers `400` to `add/` for an
  existing path, and `GET /v3/config/paths/get/{name}` answers `200` for a configured path and `404`
  otherwise. The integration tests pin both against the pinned image; if either is wrong the red
  tests will say so.
- **A2 (accepted):** "Already exists" is accepted without comparing sources. The path name derives
  from the camera identifier and a redelivery carries the same source URL. A differing source
  requires a pre-fix orphan of a camera that was re-pointed while orphaned; the deploy-time
  reconciler pass removes such orphans first.
- **A3 (accepted):** Between "row saved" and "path added" the stream exists in `Provisioning` with
  no path; the health watcher may report it degraded until the redelivery succeeds. That is the
  same observable state as MediaMTX being down after a successful provision today, and strictly
  better than an orphan admitting every fab.
- **R1:** Spec-number collision (header). Re-check before the PR.
