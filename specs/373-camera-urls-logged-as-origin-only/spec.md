# Spec 373: Camera URLs are logged as their origin only

**Feature Branch**: `fix/2908-redact-camera-url-in-logs` (cut from `origin/develop` at `d8070adc`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains: the one open question the
issue carried (redact vs reject) was decided by the owner on #2908 (option 1). One judgement call is
marked in §6 (two ScenarioSimulator log sites found during the premise check are reported, not
fixed).

**Input**: Issue [#2908](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2908),
*Camera RTSP URLs are logged in full at Information; only user:password@ is refused, so a
query-string credential would reach the sink*. Found by the security review of #2893.

**Owner decision (2026-10-15, on #2908), recorded here and not re-opened:** redact at the logging
boundary. One shared helper renders a camera URL for logs as `scheme://host[:port]` only, with no
userinfo, path or query, applied at the four call sites below. `RtspUrl` and `StreamSourceUrl`
accept exactly what they accept today, so there is no migration question.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (the rendered log text changes): phase 4a is
**red** (ADR-0139). Not also a refactor: no shape change is bundled.

**Spec number.** 373. Checked 2026-10-15 against `origin/develop` (highest: 372), every local and
remote branch head (`git ls-tree <ref> specs/`) and the four open PR branches (#2970, #2969, #2968,
#2811): nothing claims 373. Re-check before the PR is opened.

**ADRs.** ADR-0050 (logging: `ILogger<T>` + `[LoggerMessage]` source-gen + OTLP; the sink this
fixes), ADR-0139 / ADR-0144 (phase 4a red; the lane implements, it does not decide), ADR-0105
(`Ensure.That` guards), ADR-0065 (Shared >= 90% coverage, which the new helper falls under),
ADR-0036 (smallest change). Constitution §III (no cross-context reference; `Shared.Kernel` holds
language-level types only) governs where the helper lives. **No new ADR is needed**: ADR-0050
covers logging and the owner made the one decision there was to make.

**Latency budget.** N/A. All four sites are control-plane: stream provisioning and re-pointing
(gateway), the startup reconcile pass, and a camera address change. None is on any §IV leg of the
event-to-overlay path, and the helper is one `Uri` parse per log line on those paths.

---

## 1. What was found (premise verified, not assumed)

On `d8070adc` (= `origin/develop`, 2026-10-15), the four sites the issue names are exactly where it
says, byte for byte:

| # | Template | Declared | Called from | Argument today |
|---|---|---|---|---|
| S1 | `Reconciler re-added missing MediaMTX path {Path} -> {SourceUrl}.` | `src/StreamDistribution/Infrastructure/Log.cs:22` | `Reconciler/MediaMtxReconciler.cs:223` | `sourceUrl.Value` (a `StreamSourceUrl`) |
| S2 | `Registered MediaMTX path {Path} -> {Source}.` | `src/StreamDistribution/Infrastructure/Log.cs:37` | `Gateways/MediaMtxRtspGateway.cs:51` | `rtspSourceUrl` (`string`) |
| S3 | `Re-pointed MediaMTX path {Path} at {Source}.` | `src/StreamDistribution/Infrastructure/Log.cs:43` | `Gateways/MediaMtxRtspGateway.cs:67` | `rtspSourceUrl` (`string`) |
| S4 | `Changed camera {Camera} address to {Url}.` | `src/CameraCatalog/Application/Log.cs:31` | `Commands/Handlers/ChangeCameraAddressCommandHandler.cs:64` | `changing.Url` (an `RtspUrl`, rendered via `ToString`) |

All four are `LogLevel.Information`. Both value objects (`CameraCatalog/Domain/Camera/RtspUrl.cs`,
`StreamDistribution/Domain/Stream/StreamSourceUrl.cs`) refuse only a non-empty `Uri.UserInfo`; a
query string or a path segment carrying a credential passes both and reaches every site above.

**Other paths searched, and found clean:**

- Every `[LoggerMessage]` template in `src/` with a `{SourceUrl}`, `{Source}`, `{Url}`, `{RtspUrl}`,
  `{Address}` or `{Uri}` placeholder. The `{Source}` hits in EventIngestion and Automation are an
  event's *source name* (`plc`, `inference`), not a URL.
- Exception messages: `Ensure` (`Shared.Kernel/Ensure.cs`) never echoes the value it rejects
  (`value must start with 'rtsp://'.`, `value: must not contain a user:password@ segment`), so a
  refused URL does not reach a 400 body or a log through the guard. `EnsureSuccessStatusCode` in the
  gateway names the status, not the request body.
- OpenTelemetry HTTP spans for the MediaMTX calls carry the request path
  (`/v3/config/paths/add/cam-<guid>`), never the JSON body holding the source.
- No `Activity` tag, metric dimension, audit payload or Wolverine message-body audit in `src/`
  carries a camera URL.
- `StreamHealthWatcher` / `ParsePathHealth` report `"not ready"` / `"path not registered"`, never the
  source.

Two further sites in `src/ScenarioSimulator/Log.cs` print a URL; they are outside this spec (§6).

## 2. User story

### US1 (P1): A camera URL in a log never carries more than its origin

As a fab security officer, I want every log line that names a camera's address to show only
`scheme://host[:port]`, so that a credential a vendor URL carries in its path or query string can
never reach the OTLP sink, while an operator can still see *which device* a path points at.

**Why P1 / independently shippable:** one helper plus four call sites; observable end to end in the
captured log text of the gateway, the reconciler and the address-change handler.

## 3. Acceptance scenarios

```gherkin
Feature: camera URLs reach logs as their origin only

  Background:
    Given the shared helper renders a URL as scheme://host[:port]

  # happy path
  Scenario: registering a MediaMTX path logs the source's origin
    When the gateway registers path "cam-<guid>" with source "rtsp://cam/stream?token=SECRET"
    Then the log line reads "Registered MediaMTX path cam-<guid> -> rtsp://cam."
    And no captured log line contains "SECRET"
    But the source MediaMTX receives in the request body is "rtsp://cam/stream?token=SECRET"

  Scenario: re-pointing a MediaMTX path logs the new source's origin
    When the gateway re-points path "cam-<guid>" at "rtsp://cam/SECRET/path"
    Then the log line reads "Re-pointed MediaMTX path cam-<guid> at rtsp://cam."
    And no captured log line contains "SECRET"
    But the source MediaMTX receives in the request body is "rtsp://cam/SECRET/path"

  Scenario: the reconciler's re-add logs the origin
    Given a stream whose source is "rtsp://10.0.5.5/h264" has no MediaMTX path
    When the reconcile pass re-adds it
    Then the log line reads "Reconciler re-added missing MediaMTX path cam-<guid> -> rtsp://10.0.5.5."

  Scenario: changing a camera's address logs the new address's origin
    When an operator changes camera <id>'s address to "rtsp://10.0.5.44/stream?token=SECRET"
    Then the log line reads "Changed camera <id> address to rtsp://10.0.5.44."
    And no captured log line contains "SECRET"
    But the camera's stored address is "rtsp://10.0.5.44/stream?token=SECRET"

  # an explicit port is kept, everything after the authority is dropped
  Scenario: a non-default port survives
    When the helper renders "rtsp://cam:8554/x?y=z#frag"
    Then the result is "rtsp://cam:8554"

  # conflict: the value objects already refuse userinfo; the helper must not depend on that
  Scenario: userinfo is dropped even if it arrives
    When the helper renders "rtsp://user:pass@cam/x"
    Then the result is "rtsp://cam"

  # bad request: input that is not an absolute URL with a host is never echoed
  Scenario Outline: an unparseable input renders a fixed placeholder
    When the helper renders "<input>"
    Then the result is "<unparseable url>"
    And the result does not contain the input
    Examples:
      | input              |
      | not a url SECRET   |
      | /relative/SECRET   |
      |                    |

  # auth: unchanged; who may change an address or provision a stream is not touched
  Scenario: authorization is not part of this change
    Then every endpoint and scope that reaches these sites behaves as before
```

## 4. Requirements

- **FR-001** One helper, in `Shared.Kernel`, renders a URL string as its origin: scheme, `://`,
  host, and `:port` only when the parsed URL carries a port that is not its scheme's default.
  Userinfo, path, query and fragment are never in the result. (For `rtsp`, which .NET's `Uri` has no
  registered default for, this means "port iff written in the input".)
- **FR-002** An input that does not parse as an absolute URL **with a non-empty host** renders the
  fixed string `<unparseable url>`. Note: on Linux (CI), `Uri.TryCreate("/x/y", Absolute)` succeeds
  as a `file://` URI with an empty host; the non-empty-host condition is what keeps that case on the
  placeholder on both platforms.
- **FR-003** A `null` input is an argument-precondition failure (`Ensure.That(url).IsNotNull()`,
  ADR-0105). No caller can pass one: every site's value comes from a validated value object.
- **FR-004** S1–S4 log the helper's result in place of the full URL. Each template's text and its
  placeholder name (`{SourceUrl}`, `{Source}`, `{Url}`) are unchanged, so the structured attribute
  key in OTLP is unchanged; only its value narrows.
- **FR-005** Nothing but the logged value changes: the URL sent to MediaMTX, the URL persisted on
  `Camera` and `Stream`, the API responses, the integration events, and what `RtspUrl` /
  `StreamSourceUrl` accept are all byte-identical.
- **FR-006** No new project reference. No cross-context reference (constitution §III).

## 5. Independent end-to-end test procedure

1. Unit (no Docker): the helper's table (`Shared.Kernel.Tests`); the gateway's two sites against an
   in-file `HttpMessageHandler` stub with a capturing logger (`StreamDistribution.Infrastructure.Tests`);
   the address-change handler with a capturing logger (`CameraCatalog.Application.Tests`). Each
   asserts the exact rendered line, the absence of `SECRET`, and (FR-005) that the full URL still
   reached MediaMTX / the repository.
2. Integration (Aspire fixture, Docker): spec 351's C7
   (`MediaMtxReconcilerPassCharacterisationTests`) re-adds a missing stream and asserts the re-add
   line; its expected text changes from `-> rtsp://10.0.5.5/h264.` to `-> rtsp://10.0.5.5.`.
3. Phase 5: boot the stack, register a camera whose URL carries `?token=PHASE5SECRET`, change its
   address, and confirm in the Aspire dashboard's structured logs that `Registered MediaMTX path` and
   `Changed camera … address` show the origin and that a search for `PHASE5SECRET` finds no log
   entry from `stream-distribution` or `camera-catalog`. (MediaMTX's own container log is not ours
   and is out of scope; §6.)

## 6. Out of scope, and why

- **Rejecting such URLs at the value object** (issue option 2): declined by the owner.
- **`src/ScenarioSimulator/Log.cs:40`** `Registered camera '{Name}' -> {RtspUrl}.` — the URL is one
  the simulator builds itself from `ScenarioSimulator:RtspHost` and a scenario path
  (`CameraCatalogClient.cs:22`); it cannot carry a camera credential. Not the same defect.
- **`src/ScenarioSimulator/Log.cs:71`** `Camera URL {Url} has no path component; …` (called from
  `EventHandlers/CameraRegisteredSimHandler.cs:43` with `CameraRegisteredV1.RtspUrl`) — *judgement
  call*: this one **is** the same shape (a catalog URL with a query and no path, e.g.
  `rtsp://cam?token=x`, is logged in full). Left out because the simulator is a dev-only host
  (ADR-0111), gated off in CI, E2E and production, so it never runs against a fab's catalog; and
  because the handler has no test harness today (its constructor needs the provisioner, correlation
  table and wall seeder), so the fix is not "trivial and identical" once the red test is counted.
  Reported for a follow-up issue rather than folded in.
- **MediaMTX's own process log**, which may print a path's source when it starts pulling. That is a
  third-party container's output, not an `ILogger` site.
- `tests/Integration.Tests/Fixtures/AspireFixture.Db.cs:293` prints a source URL in a test-only
  exception message. Test code; not a sink.

## 7. Locked choices this spec relies on

`[LoggerMessage]` source-generated logging (ADR-0050); `Shared.Kernel` as the language-level shared
library every context already references (constitution §III); `Ensure.That` guards (ADR-0105);
xUnit + Shouldly, sentence-style names, hand-written fakes (ADR-0052/0053/0054); the Aspire fixture
for C7 (ADR-0103).
