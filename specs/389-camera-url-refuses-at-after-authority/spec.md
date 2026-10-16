# Spec 389: Camera URLs refuse a raw `@` after the authority on write, and stay readable on read

**Feature Branch**: `fix/2999-reject-at-after-authority` (cut from `origin/develop` at `2159a482`, rebased onto `b5a29905`)

**Created**: 2026-10-16

**Status**: Phases 1-3 written (Specify, Plan, Tasks). No `[NEEDS CLARIFICATION]` remains: the direction
was delegated by the owner on #2999 (DECISION comment, 2026-10-16, binding). Two places where the decision's
own wording cannot all hold at once are resolved in favour of its primary instruction and recorded as
**[Assumption]** in §6 (A-1, A-2); neither changes the shape of the code.

**Input**: Issue [#2999](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2999), *RtspUrl and
StreamSourceUrl accept exactly the credential-bearing forms (a raw @ after the authority) that the parser
misreads as host and port*. Found by the security review of #2993 (spec 381 §6).

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (the accepted input of two value objects narrows):
phase 4a is **red** for the value objects, the two API write paths and the two event handlers. The predicate
extraction inside `Shared.Kernel` is **behaviour-preserving** and is guarded by characterisation (the
existing `UrlRedaction*` suites, unmodified). Read-side hydration is **behaviour-preserving** and gets new
characterisation tests captured green **before** the change. **Security-sensitive** (camera credentials
reaching the management UI, the API and the message bus): phase 4 includes a `test-adversary` pass, phase 6
includes `security-reviewer`.

**Spec number.** 389. Checked 2026-10-16 against `origin/develop` (highest: 386), every local and remote
branch head (`git ls-tree <ref> specs/`: 387 on `fix/3006-detailchanged-ipv6-zone` / PR #3015, 388 on
`fix/2994-source-repair-log-volume` / PR #3014) and every open PR head (#3015, #3014, #2982, #2981, #2979,
#2978, #2970, #2811): nothing claims 389. Re-check before the PR is opened.

**ADRs.** ADR-0038 / ADR-0046 / ADR-0066 (hand-written value objects, `From` + `Ensure.That`), ADR-0105
(`Ensure.That`; messages never carry the value), ADR-0047 / ADR-0089 (`ApiError` codes, unchanged),
ADR-0042 / ADR-0088 (Wolverine dispatch, per-module queues, **default** failure policy unchanged), ADR-0139 /
ADR-0144 (phase 4a colours; the lane implements, it does not decide), ADR-0065 (Domain >= 90%, Shared >= 90%),
ADR-0036 (smallest change). Constitution §II (no primitive state added) and §III (the predicate lives in
`Shared.Kernel`, which both contexts already reference; no new project reference). Spec 001 FR-008
(credentials in a camera URL are deferred) and spec 381 (the predicate's origin).

**No new ADR.** The owner delegated the direction. The non-throwing hydration factory has a precedent in this
repository (`BearerTokenHash.FromStored`, `EventIngestion`, mapped in `WebhookIntegrationConfiguration`), so
it is a reused pattern, not a new decision. No contract, scope, persistence shape, Wolverine policy or
migration changes.

**Latency budget.** N/A. Registration, address change and re-point are control-plane writes; none is on a
§IV leg. The added work is one ordinal scan of a <= 2048-character string at write time.

---

## 1. What was found (premise verified by experiment, not assumed)

### 1.1 The issue's five forms are still accepted, and the predicate already refuses them

A scratch probe (file-based app in the session scratchpad, referencing both Domain projects at `2159a482`;
not committed) called `RtspUrl.From`, `StreamSourceUrl.From`, `UrlRedaction.ToOrigin` and, by reflection,
the private `UrlRedaction.AtSignFollowsTheAuthority` (`src/Shared.Kernel/UrlRedaction.cs:38-57`):

| Input | predicate | `ToOrigin` | `RtspUrl.From` | `StreamSourceUrl.From` |
|---|---|---|---|---|
| `rtsp://admin:12345#@cam/s` | true | `<unparseable url>` | **accepted** | **accepted** |
| `rtsp://admin:4711?@cam/s` | true | placeholder | **accepted** | **accepted** |
| `rtsp://admin:8080/x@cam/s` | true | placeholder | **accepted** | **accepted** |
| `RTSP://ADMIN:12345#@CAM` | true | placeholder | **accepted** | **accepted** |
| `rtsp://3232235777:12345#@cam` | true | placeholder | **accepted** | **accepted** |
| `rtsp://cam/s?mail=a@b` | **true** | placeholder | accepted | accepted |
| `rtsp://cam/a@b` | true | placeholder | accepted | accepted |
| `rtsp://cam/s#frag@x` | true | placeholder | accepted | accepted |
| `rtsp://admin:pass@cam/s` | false | `rtsp://cam` | rejected (userinfo) | rejected (userinfo) |
| `rtsp://admin:12345%23@cam/s` | false | `rtsp://cam` | rejected (userinfo) | rejected (userinfo) |
| `rtsp://cam@` | false | placeholder | rejected (userinfo) | rejected (userinfo) |
| `rtsp://cam:554/path?x=y` | false | `rtsp://cam:554` | accepted | accepted |
| `rtsp://[::1]:554/s`, `rtsp://[fe80::1]/s` | false | origin | accepted | accepted |
| `rtsp:\\admin:1#@cam` | true | placeholder | rejected (scheme) | rejected (scheme) |

The existing rejection text is `value: must not contain a user:password@ segment (Parameter 'value')`.

### 1.2 Hydration goes through the validating factory today

`CameraConfiguration.cs:99` maps `Url` with `HasConversion(url => url.Value, value => RtspUrl.From(value))`;
`StreamConfiguration.cs:73` does the same with `StreamSourceUrl.From`. No compiled model exists (no
`UseModel` / `RuntimeModel` in either context), so the converter lambda is what EF runs on materialisation.
Tightening `From` without touching these two lines would make every stored misread row throw on read —
`GET /cameras` would 500 for the whole fab, and the stream reconciler would fail its startup pass. This is
the hazard the decision (item 2) names. It **can** be avoided without a larger change: a second factory that
keeps today's invariants and omits the new one, used only by the two converters (§4 FR-005).

### 1.3 Every writer of the two types

`grep -rn "RtspUrl\.From\|StreamSourceUrl\.From" src` at `2159a482`:

| # | Site | Kind | After this spec |
|---|---|---|---|
| W1 | `CameraCatalog/Api/Requests/RegisterCameraRequest.cs:19` (`POST /cameras`, via `BoundaryParse`) | write | `From` — refuses (400 `CAMERA_INVALID_REQUEST`) |
| W2 | `CameraCatalog/Api/CameraEndpoints.cs:312` (`PATCH /cameras/{id}` address, via `BoundaryParse`) | write | `From` — refuses (400 `CAMERA_INVALID_REQUEST`) |
| W3 | `StreamDistribution/Application/Commands/Handlers/ProvisionStreamCommandHandler.cs:33` (from `CameraRegisteredV1`) | write | `From` — refuses (`STREAM_INVALID_RTSP_SOURCE`) |
| W4 | `StreamDistribution/Application/Commands/Handlers/RepointStreamCommandHandler.cs:126` (from `CameraAddressChangedV1`) | write | `From` — refuses (`STREAM_INVALID_RTSP_SOURCE`) |
| R1 | `CameraCatalog/Infrastructure/Persistence/Configurations/CameraConfiguration.cs:99` | read | `FromStored` — never refuses a row today's `From` accepted |
| R2 | `StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs:73` | read | `FromStored` |

Not writers, verified: the ScenarioSimulator builds `rtsp://{RtspHost}/{path}` from configuration
(`CameraCatalogClient.cs:41`) and posts it over HTTP, so it goes through W1; `MediaMtxReconciler` /
`MediaMtxSourceRepair` / `StreamSourceSnapshot` read persisted values through R2 and compare them with the
raw string MediaMTX reports, which never becomes a value object (issue #2999: "`ToOrigin` keeps covering that
one"); `AuditObservability` consumes the contracts as strings. Test builders (`CameraBuilder`,
`StreamBuilder`) use plain addresses with no `@`.

The management UI already refuses every one of the new forms client-side: `apps/shared/src/api/cameras.schema.ts:13`
rejects `^rtsp:\/\/[^@\s]+@`, i.e. any `@` after `rtsp://`, including `?mail=a@b`. So the server change
narrows only what API clients and the simulator can send; no UI change is needed and none is made.

### 1.4 What an invalid source does to a Wolverine handler today

Both `CameraRegisteredIntegrationEventHandler` and `CameraAddressChangedIntegrationEventHandler` turn any
command failure, `STREAM_INVALID_RTSP_SOURCE` included, into an `InvalidOperationException`.
`StreamDistributionInfrastructureModule` registers no failure policy, so Wolverine's default applies.
Decompiled from the pinned `WolverineFx 6.48.3` (`Wolverine.ErrorHandling.FailureRuleCollection`):
`CombineRules` sets `MaximumAttempts = MaximumAttempts ?? parent.MaximumAttempts ?? 3`, which synthesises two
`RequeueContinuation` slots, and `DetermineExecutionContinuation` falls through to `new MoveToErrorQueue(e)`.
CORRECTION (observed by the tests, which supersede the first reading of the decompilation): the synthesised
`RequeueContinuation` slots are not reached for a handler exception that has no rule of its own. With no
failure rule registered, `DetermineExecutionContinuation` returns `MoveToErrorQueue` on the **first** attempt
(it does so for attempts 1, 2 and 3 alike). A message whose handler throws is moved to the error queue **at
once, with no retry**. That is bounded; it is not a poison loop. It is pinned by
`CameraEventFailurePolicyCharacterisationTests` (`aec00bd7`), which reaches into Wolverine internals by
reflection for the combined rules and adds a source scan of StreamDistribution for any rule of its own (the
module's options lambda cannot run in a unit test). Spec 203's plan records the same default and keeps it on purpose
("a dead letter remains the right answer for a message that genuinely cannot be processed").

(The comment at `RepointStreamCommandHandler.cs:98-99`, "failing would redeliver it forever", overstates
this; it is not changed here, see §7.)

---

## 2. User stories

### US1 (P1) — An address the parser would misread is refused where it is written, with a fix the caller can act on

As an **integrator or operator registering or correcting a camera through the API**, I want an address whose
`@` comes after the first `/ ? # \` past `rtsp://` to be refused with a message that says what to change, so
that a camera password never gets stored in a form that the parser reads as host and port and that the API,
the UI and the message bus then show in clear.

**Why P1:** this is the issue. Today the only credential-bearing URLs the system will store are exactly the
misread ones (§1.1).

**Independent test:** `POST /cameras` with `rtsp://admin:12345#@cam/s` answers 400 `CAMERA_INVALID_REQUEST`
with the fix text and no part of the URL; `PATCH /cameras/{id}` with the same address answers the same and
leaves the camera unchanged; `RtspUrl.From` and `StreamSourceUrl.From` throw for every row of §5's reject set
and accept every row of its accept set.

### US2 (P1) — A row stored before this change stays readable

As an **operator with cameras registered before this change**, I want the camera list, the camera detail and
the stream reconciler to keep working for a camera whose stored address the new rule would refuse, so that
tightening validation does not take a fab's camera list down.

**Why P1, same slice:** shipping US1 without US2 breaks every read of a fab holding such a row (§1.2). They
are one vertical: the tightening is not shippable without the non-throwing read.

**Independent test:** with a misread address inserted directly into `cameras.rtsp_url` (raw SQL, bypassing
the API), `GET /cameras/{id}` answers 200 and `GET /cameras` lists the camera; the EF value converters for
both columns materialise every row of §5's reject set without throwing.

### US3 (P1) — An event carrying a now-refused address fails once, visibly, and does not loop

As **the operator of the message bus**, I want a `CameraRegisteredV1` or `CameraAddressChangedV1` published
before this change (still in the outbox at deploy, or replayed from the error queue) whose address the new
rule refuses to end on the error queue on the first attempt (Wolverine's default with no failure rule, §1.4), with a Warning that names
the camera and the error code but not the address, and with no stream provisioned or re-pointed — so that a
camera left without a stream is visible, and nothing retries forever.

**Independent test:** the real `CameraRegisteredIntegrationEventHandler` → `ProvisionStreamCommandHandler`
chain and the real `CameraAddressChangedIntegrationEventHandler` → `RepointStreamCommandHandler` chain, over
the in-memory repository and gateway fakes, each throw `InvalidOperationException` for a misread address,
write nothing, call no gateway, and log one Warning whose text contains `STREAM_INVALID_RTSP_SOURCE` and
none of the address; and Wolverine's discovered chains for the two messages carry no failure rule of their
own, so the default policy (error queue on the first attempt) is the one in force.

---

## 3. Acceptance scenarios (Gherkin)

```gherkin
Feature: Camera URLs refuse a raw @ after the authority on write

  Background:
    Given an operator token with scope "sse.cameras.write" and membership in fab "munich"

  # US1 — happy path (unchanged)
  Scenario: An ordinary address is still accepted
    When the operator registers a camera with address "rtsp://cam:554/path?x=y"
    Then the response is 201 Created with a Location header

  # US1 — bad request (new)
  Scenario Outline: A misread credential is refused on registration with the fix, not the URL
    When the operator registers a camera with address "<address>"
    Then the response is 400 with title "CAMERA_INVALID_REQUEST"
    And the detail is the fix text of FR-003
    And the response body does not contain "<secret>"
    And no camera is stored and no CameraRegisteredV1 is published
    Examples:
      | address                        | secret     |
      | rtsp://admin:12345#@cam/s      | 12345      |
      | rtsp://admin:4711?@cam/s       | 4711       |
      | rtsp://admin:8080/x@cam/s      | admin      |
      | RTSP://ADMIN:12345#@CAM        | 12345      |
      | rtsp://3232235777:12345#@cam   | 3232235777 |

  Scenario: A misread credential is refused on address change and the camera keeps its address
    Given a camera "line-3-inlet" at "rtsp://10.0.5.12/h264" at version 1
    When the operator patches its address to "rtsp://admin:12345#@cam/s" with If-Match version 1
    Then the response is 400 with title "CAMERA_INVALID_REQUEST" and the fix text of FR-003
    And the camera's address is still "rtsp://10.0.5.12/h264" at version 1
    And no CameraAddressChangedV1 is published

  # US1 — the existing rejections keep their existing message
  Scenario: A proper userinfo segment is still refused with the userinfo message
    When the operator registers a camera with address "rtsp://admin:pass@cam/s"
    Then the response is 400 with detail "value: must not contain a user:password@ segment (Parameter 'value')"

  # US1 — auth (unchanged; validation does not run for an unauthorised caller)
  Scenario: A caller without the write scope is refused before the address is examined
    Given a token without scope "sse.cameras.write"
    When it registers a camera with address "rtsp://admin:12345#@cam/s"
    Then the response is 403 and the body does not contain the fix text

  # US1 — conflict ordering (unchanged)
  Scenario: The address is refused before a stale precondition is reported
    Given a camera at version 2
    When the operator patches its address to "rtsp://admin:12345#@cam/s" with If-Match version 1
    Then the response is 400 "CAMERA_INVALID_REQUEST", not 412
    # PATCH parses the attribute before it applies the command; the version check is the handler's.

  # US2 — stored rows
  Scenario: A camera stored with a now-refused address is still listed and readable
    Given a row in cameras whose rtsp_url is "rtsp://admin:12345#@cam/s", inserted directly
    When the operator lists the fab's cameras and reads that camera
    Then both answer 200 and include the camera
    # The value is returned as stored; masking it in CameraDto is out of scope (decision item 5).

  # US3 — replay
  Scenario: A pre-deploy CameraRegisteredV1 carrying a misread address is dead-lettered, not looped
    Given a CameraRegisteredV1 for a new camera with address "rtsp://admin:12345#@cam/s"
    When StreamDistribution handles it
    Then the handler throws, no stream is saved and the gateway is not called
    And one Warning names the camera and "STREAM_INVALID_RTSP_SOURCE" and does not contain "12345"
    And under Wolverine's default policy the message is moved to the error queue on its first attempt, with no retry

  Scenario: A pre-deploy CameraAddressChangedV1 carrying a misread address is dead-lettered, not looped
    Given a stream at "rtsp://camera-sim:8554/b"
    And a newer CameraAddressChangedV1 for its camera with address "rtsp://admin:12345#@cam/s"
    When StreamDistribution handles it
    Then the handler throws and the stream is still at "rtsp://camera-sim:8554/b"
    And the gateway is not called and no log line contains "12345"
```

---

## 4. Functional requirements

- **FR-001 — One predicate.** `UrlRedaction`'s private `AtSignFollowsTheAuthority` becomes a single public
  member of `Shared.Kernel`: `public static bool AtSignFollowsTheAuthority(string url)` on a new
  `public static class UrlAuthority` (`src/Shared.Kernel/UrlAuthority.cs`). `UrlRedaction.ToOrigin`,
  `RtspUrl.From` and `StreamSourceUrl.From` all call it; no copy of the scan remains anywhere. Behaviour is
  identical for every input — the existing `UrlRedaction*` test suites are the proof and pass unmodified.
- **FR-002 — Write paths refuse it.** `RtspUrl.From` and `StreamSourceUrl.From` add one
  `.Satisfies(url => !UrlAuthority.AtSignFollowsTheAuthority(url), <FR-003 text>)` **after** the existing
  `HasNoUserInfo` check, so every input rejected today keeps exactly today's message. The reject set grows by
  exactly the inputs for which the predicate is true and every earlier check passed.
- **FR-003 — The message says what to fix and never echoes the URL.** The `Satisfies` message, identical in
  both value objects, is:

  ```
  must not contain a raw '@' after the first '/', '?', '#' or '\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either
  ```

  `Ensure` renders it as `value: <message> (Parameter 'value')`. The rendered text contains no part of the
  input. It surfaces as the `detail` of CameraCatalog's existing 400 `CAMERA_INVALID_REQUEST` (via
  `BoundaryParse`) and inside StreamDistribution's existing `STREAM_INVALID_RTSP_SOURCE` (`RTSP source
  rejected: <rendered>`). No new error code.
- **FR-004 — Accepted shapes stay accepted.** Every row of §5's accept set is accepted by both value objects
  before and after the change (characterisation).
- **FR-005 — Hydration never throws for a row today's rules accepted.** Each value object gains
  `public static <T> FromStored(string value)` that applies the invariants `From` applies at `2159a482`
  (not null/whitespace, `MaximumLength`, `rtsp://` prefix ignoring case, no userinfo) and **not** the FR-002
  rule. `CameraConfiguration` and `StreamConfiguration` switch their `HasConversion` read lambda from `From`
  to `FromStored`. Nothing else calls `FromStored` (FR-008). No migration, no rewrite of a stored value.
- **FR-006 — Replay is bounded and visible.** The two integration event handlers keep throwing on
  `STREAM_INVALID_RTSP_SOURCE`; no failure policy is added; the default (the error queue on the first
  attempt, no retry, §1.4) is what bounds a replay. Both handlers' comments that call an invalid source "cannot occur without
  drift" are corrected to name the second cause (an event published before this change). The Warning they
  already log carries the camera, the code and the FR-003 message, never the address.
- **FR-007 — No new cross-context reference.** Both contexts already reference `Shared.Kernel`; nothing else
  is added. The NetArchTest boundary suite and `PrimitiveBoundaryTests` pass unmodified (a static class with a
  `bool`-returning method holds no state).
- **FR-008 — `FromStored` cannot quietly become a write path.** A source-scanning architecture test fails if
  `RtspUrl.FromStored` or `StreamSourceUrl.FromStored` is referenced from any file other than its own
  context's `Infrastructure/Persistence/Configurations/*Configuration.cs` (or its own declaration). Proven by
  a permanent synthetic check (a fixture file that must be flagged) rather than a temporary-call
  counterfactual, with slash-normalised paths. The text scan `FromStoredCallSiteTests` is bypassed by `using static`
  or a split line, so `FromStoredCallerIlTests` (`fb7dd0c1`) scans the compiled IL as well.

## 5. Accept / reject sets (pinned by tests in both value objects)

**Accept (unchanged, characterisation):** `rtsp://10.0.5.12/h264`, `rtsp://cam:554/path?x=y`,
`RTSP://CAM/s`, `rtsp://[::1]:554/s`, `rtsp://[fe80::1]/s`, `rtsp://cam/s;x`, `rtsp://cam/%40x`,
`rtsp://cam/s?mail=a%40b`.

**Reject with today's message (unchanged, characterisation):** `rtsp://admin:pass@cam/s`,
`rtsp://admin:12345%23@cam/s` (percent-encoded userinfo), `rtsp://cam@`, `http://10.0.5.12/h264`, blank,
over-length.

**Reject with the FR-003 message (new, red):** `rtsp://admin:12345#@cam/s`, `rtsp://admin:4711?@cam/s`,
`rtsp://admin:8080/x@cam/s`, `RTSP://ADMIN:12345#@CAM`, `rtsp://3232235777:12345#@cam`,
`rtsp://12345#@cam/s`, `rtsp://admin:12345#@[fd00::5]:8554/x`, `rtsp://cam/s?mail=a@b`, `rtsp://cam/a@b`,
`rtsp://cam/s#frag@x`.

**Hydrate via `FromStored` (characterisation through the EF converter, green before and after):** every row
of the new reject set above, plus the accept set.

## 6. Assumptions

- **[Assumption] A-1 — `?mail=a@b` is refused at registration.** Decision item 4 says registration "need not"
  refuse `?mail=a@b` URLs, but item 1 requires the *same* predicate as `ToOrigin`, which refuses them (§1.1),
  and the decided reject set includes `rtsp://admin:4711?@cam/s`, which no scan of the written text can tell
  apart from a query holding an `@`. Item 1 is the primary, explicit instruction and item 4's "need not" is
  permissive, so the shared predicate wins. Cost: an `@` in a path, query or fragment must be written `%40`
  — which the FR-003 text says. The management UI already refuses these (§1.3), so no console user loses
  anything. **Not a block**; flagged for the owner in the PR body.
- **[Assumption] A-2 — the fix text points at a further refusal for credentials.** Decision item 3 asks the
  message to say "percent-encode `#`, `?`, `/` in the password"; item 4 keeps `user:pass@host` refused. A
  password encoded as told therefore meets the existing userinfo rejection. The FR-003 text states both
  facts in one message, so it does not imply the encoded form will be accepted. Credentialed cameras remain
  unsupported (spec 001 FR-008), exactly as today.
- **A-3 — an `Idempotency-Key` replay across the deploy.** `POST /cameras` parses the body before it
  consults the idempotency store, so a client retrying, after the deploy, a request that succeeded before it
  with a now-refused address gets 400 instead of the original 201. The camera exists; the caller is told
  what to fix. Accepted; no change to the order (pinned by `CameraRegisterEndpointCharacterisationTests`).

## 7. Out of scope

- **#3012** (text after the closing `]` of an IPv6 host dropped from the origin — the same "written authority
  longer than the host that was read" class). Not implemented. The shape chosen here makes it cheap: it adds
  a sibling predicate on `UrlAuthority`, one `||` in `ToOrigin` and one `Satisfies` line plus message in each
  value object; the hydration path (FR-005), the replay decision (FR-006) and the `FromStored` guard (FR-008)
  carry over unchanged, and those are the expensive half. #3012's spec should consider folding both
  predicates behind one umbrella member so the three call sites cannot fall out of step.
- Masking `CameraDto.RtspUrl` / `CameraSummaryDto.RtspUrl` in the API and the management UI (decision item 5,
  separate issue). Stored misread rows remain visible there in clear until that lands.
- Migrating, flagging or rewriting stored rows (decision item 2).
- Supporting credentials in camera URLs (decision item 4; spec 001 FR-008).
- The ScenarioSimulator log lines (#2976).
- The overstated "redeliver it forever" comment in `RepointStreamCommandHandler.IgnoreOlderRepoint` (§1.4):
  accurate in direction (failing would not help), wrong in mechanism; noted, not touched — it is not on a
  path this spec changes.

Follow-up issues: #3016 (`CameraDto` and the management UI serve stored URLs verbatim, including legacy
credential rows), #3012 (text after `]`), #3017 (an empty authority followed by credentials, e.g.
`rtsp:///admin:4242@cam/s`; the predicate comment's claim that the authority is "located the way the parser
does" is also wrong for `rtsp:`).

## 7.1 Accepted / recorded

- U+FF20 fullwidth `@` is accepted: no parser treats it as a delimiter; only IME typos produce it.
- `rtsp://@cam/s` (empty userinfo) and control characters are accepted; pre-existing.
- A `with` expression on a `StringValueObject` bypasses both factories; pre-existing, no use in `src`.
- Reflection bypasses both `FromStored` guards.
- `AuditPayloadRedaction.cs:168,176` keeps its own `'/?#\\'` locator (spec 387), pinned by name in
  `UrlAuthoritySingleSourceTests`; #3017 proposes unifying it.
- `ProvisionStreamCommandHandler` validates the URL before the existing-row lookup, so a redelivered
  `CameraRegisteredV1` for a stream stored at a misread source never reaches the spec 309 FR-004 "reassert the
  path" branch; the reconciler restores the path through `FromStored`.
- The fix text only shows when `Uri` parses the misread form (a numeric "password" <= 65535, or the `/` `?`
  forms). A non-numeric `password#@` gets the older userinfo message, and was refused before this change too.
- `POST` checks the address before resolving the fab; pre-existing.

## 8. End-to-end test procedure (phase 5)

On a booted Aspire stack (one per machine; stop it before building):

1. Mint a write token from Aspire's proxied Keycloak endpoint for an operator in `munich`.
2. `POST /cameras?fabId=munich` with `{"name":"e2e-389-a","rtspUrl":"rtsp://admin:12345#@cam/s"}` → 400,
   `title` `CAMERA_INVALID_REQUEST`, `detail` the FR-003 text, body free of `12345`.
3. `POST /cameras?fabId=munich` with `{"name":"e2e-389-b","rtspUrl":"rtsp://camera-sim:8554/station-4-roughing"}`
   → 201; then `PATCH /cameras/{id}` with `{"rtspUrl":"rtsp://admin:4711?@cam/s"}` and the ETag → 400, and
   `GET /cameras/{id}` still shows the original address and version.
4. Insert a row directly (`psql` into the camera-catalog database) with `rtsp_url = 'rtsp://admin:8080/x@cam/s'`;
   `GET /cameras?fabId=munich` and `GET /cameras/{that id}` → 200, the camera listed.
5. Record the observed status codes and bodies on the PR. Latency: N/A (§ header).
