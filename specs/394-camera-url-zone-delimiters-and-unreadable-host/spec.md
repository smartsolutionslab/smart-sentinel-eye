# Spec 394: A camera URL with '@', '?' or '#' inside the IPv6 brackets is refused, and one the parser cannot read answers 400, not 500

**Feature Branch**: `fix/3023-camera-url-authority-residuals` (cut from `origin/develop` at `fbd4176b`)

**Created**: 2026-10-17

**Status**: Phases 1-3 written (Specify, Plan, Tasks). No `[NEEDS CLARIFICATION]` remains: the scope is the lane
comment on #3023 (items 1, 2 and 5 only; "the owner may overrule"), the stored-row direction is the one decided on
#3012 and applied by spec 392 (`FromStored` unchanged, no migration), and the boundaries the issue leaves open are
resolved and recorded as **[Assumption]** in §6 (A-1 to A-4).

**Input**: Issue [#3023](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3023), *Camera URL
authority: remaining accepted shapes found while closing #3012 and #3017* — items 1, 2 and 5. Items 3, 4, 6 and 7
stay open on the issue and out of this spec (§7). All three items were recorded as residuals in spec 392 §7.1.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** throughout: the accepted input of two value objects narrows,
`ToOrigin` renders the placeholder for more shapes, and a 500 becomes a 400. Phase 4a **red**, with
characterisation for every row §5 lists as unchanged. No refactor rides along.

**Security-sensitive.** Item 1 is a host-confusion defect: the address registered and audited as `[fe80::1]` is read
as host `10.0.0.66` by a parser that ends the userinfo at the last `@` (ffmpeg-style). Phase 4 includes a
`test-adversary` pass; phase 6 includes `security-reviewer`.

**Spec number.** 394. Checked 2026-10-17 after `git fetch origin`: `origin/develop` highest 392; `git ls-tree <ref>
specs/` over every remote head finds 393 only on `origin/fix/3019-camera-sim-span-path` (PR #3026, unmerged) and
nothing above it; the other open PRs (#2982, #2981, #2979, #2978, #2970, #2811) add no spec; worktrees
`smart-sentinel-eye`, `sse-3019`, `sse-3023` hold nothing above 393. Re-check before the PR is opened.

**ADRs.** ADR-0038 / ADR-0046 / ADR-0066 (hand-written value objects, `From` + `Ensure.That`), ADR-0105
(`Ensure.That`; a message never carries the value), ADR-0047 / ADR-0089 (existing `ApiError` codes, unchanged),
ADR-0042 / ADR-0088 (Wolverine default failure policy, unchanged), ADR-0139 / ADR-0144 (phase 4a colours; the lane
implements, it does not decide), ADR-0065 (Domain >= 90%, Application >= 80%, Shared >= 90%), ADR-0084 (metrics,
advisory), ADR-0036 (smallest change). Constitution §II (no primitive state added) and §III (shared code in
`Shared.Kernel`, which all three contexts already reference). Prior art: spec 392 (`UrlAuthority`'s bracketed-host
readings, FR-001's rule ordering, `FromStored`), spec 389 (`FromStored`, the raw-`@` rule), spec 387 (zone
handling in the audit), spec 384 (the `null` `DetailChanged`), `UrlRedaction.TryParse` (the trust-boundary guard
around a throwing BCL parser that item 2 asks to mirror).

**No new ADR.** Every mechanism is reused: a `Satisfies` rule with its own constant message placed after the
older rules (spec 392 FR-001), a span predicate over `UrlAuthority.BracketedHostReadings` (spec 392 FR-011/012),
the placeholder in `ToOrigin` (specs 377/392), the `UrlRedaction.TryParse` guard shape, the source-scanning
single-source guard (spec 389/392). No contract, scope, error code, persistence shape, Wolverine policy or
migration changes.

**Latency budget.** N/A. Registration, address change and re-point are control-plane writes and the audit
redaction runs on the audit consumer; none is on a §IV leg. The added work is one ordinal scan of a <= 2048
character string and at most two guarded `Uri` reads per write.

---

## 1. What was found (premises verified by experiment on `fbd4176b`, not assumed)

A scratch probe (a file-based app in the session scratchpad referencing `CameraCatalog.Domain`,
`StreamDistribution.Domain`, `AuditObservability.Application` and `ScenarioSimulator`; not committed) called
`Uri.TryCreate` and each `Uri` property, both `From`s, both `FromStored`s, `UrlRedaction.ToOrigin`,
`AuditPayloadRedaction.Serialize` for `CameraRegisteredV1` and `CameraAddressChangedV1`, and
`RtspPath.TryExtract`. A seeded fuzz (seed 394; 300,000 bracketed inputs, then 400,000 mixed bracketed and
plain-host inputs) ran the same calls.

### 1.1 Item 1 — an `@` inside the brackets hides a second host

| Input | `Uri` userinfo / host / `DnsSafeHost` | `From` (both) | `FromStored` (both) | `ToOrigin` |
|---|---|---|---|---|
| `rtsp://@[fe80::1%@10.0.0.66:8554]/live` | `''` / `[fe80::1]` / `fe80::1%@10.0.0.66:8554` | **accepted** | accepted | `rtsp://[fe80::1]` |
| `rtsp://@[::1%25@evil.com:554]/s` | `''` / `[::1]` / `::1%25@evil.com:554` | **accepted** | accepted | `rtsp://[::1]` |
| `rtsp://@[fe80::1%25eth0@]/s` | `''` / `[fe80::1]` | **accepted** | accepted | `rtsp://[fe80::1]` |
| `rtsp://[fe80::1%@10.0.0.66:8554]/live` (no leading `@`) | no parse | refused (userinfo message) | refused | placeholder |
| `rtsp://user@[::1%25@evil.com:554]/s` | `user` / `[::1]` | refused (userinfo message) | refused | **`rtsp://[::1]`** |
| `rtsp://@[::1%25x?@evil.com]/s` | `''` / `[::1]` | refused (raw-`@` message, spec 389) | accepted | placeholder |

The shape is reachable only with an empty userinfo before the bracket (`rtsp://@[`): without it the BCL parser
refuses the input, and with a non-empty userinfo the userinfo rule refuses it. Spec 389 §7.1 accepts
`rtsp://@cam/s` (empty userinfo) on purpose and a test pins it, so the cure "refuse any `@` in the authority when
`Uri.UserInfo` is empty" would edit a pinned decision; the cure taken is the issue's first: **refuse a raw `@`
inside the brackets** (RFC 6874: `ZoneID = 1*( unreserved / pct-encoded )` never holds one). Go's `net/url`
(MediaMTX) refuses the userinfo `@[fe80::1%`, as spec 392 recorded; MediaMTX was not run on these inputs.

For the audit: `DetailChanged` is already `null` for every row (the zone arm, `CarriesZone`, sees `%` in
`DnsSafeHost`), but the stored origin for the side with the hidden host is `rtsp://[fe80::1]` — a host the
address does not unambiguously name. `CameraRegisteredV1.Url` is stored the same way.

### 1.2 Item 5 — `?` or `#` inside the zone

| Input | `Uri` host / `DnsSafeHost` | `From` (both) | `FromStored` | `ToOrigin` |
|---|---|---|---|---|
| `rtsp://[::1%25x?y]/s`, `rtsp://[::1%25x#y]/s`, `rtsp://[fe80::1%25a?b]/x`, `rtsp://[fe80::1%x?y]/s`, `rtsp://[::1%25x?y]` | `[…]` / zone keeps `?y` / `#y` | **accepted** | accepted | `rtsp://[…]` |
| `rtsp://[fe80::1%25eth0#]:554/s` | `[fe80::1]`, port 554 | **accepted** | accepted | `rtsp://[fe80::1]:554` |
| `rtsp://[::1%25x?y]/a@b` | `[::1]` | refused (raw-`@` message: the locator ends the authority at the `?`) | accepted | placeholder |
| `rtsp://[::1%25x?y]x/s` | `[::1]` | refused (text-after-bracket message, spec 392) | accepted | placeholder |

The BCL parser closes the brackets before it honours a terminator; a parser that splits at the first `?` or `#`
(as RFC 3986 §3's generic split does) reads the authority `[::1%25x` and a different, or no, host. In the fuzz,
`From` accepted 98 inputs with `@`, `?`, `#` or `\` inside the brackets (62 with `?`/`#`, 9 with `@`, 35 with
`\`; overlapping).

Percent-encoded forms stay as they are (`rtsp://[::1%25%40x]/s`, `rtsp://[::1%25x%3Fy]/s`: accepted, origin
rendered): no parser splits on an encoded delimiter.

### 1.3 Item 2 — `Uri` throws `IndexOutOfRangeException`, and the API answers 500

The issue names `uri.UserInfo` / `uri.Host` "inside `HasNoUserInfo`". Measured, there are **two** throwing reads,
in two different rules, with different consequences:

| Input | `TryCreate` | `UserInfo` | `Host` | `From` (both) | `FromStored` (both) | `ToOrigin` |
|---|---|---|---|---|---|---|
| `rtsp://[1:2:3:4:5:6:7:8:/64]` | true | `''` | **throws** | **throws IOOR** (in `HasHost`) | accepted | placeholder |
| `rtsp://[::1:2:3:4:5:6:7:/64]/s` | true | `''` | **throws** | **throws IOOR** (in `HasHost`) | accepted | placeholder |
| `rtsp://[1:2:3:4:5:6:7:8:/64]/live`, `rtsp://[1:2:3:4:5:6:7:8:/64]x/s`, `RTSP://@[1:2:3:4:5:6:7:8:/64%25eth0]` | true | `''` | **throws** | **throws IOOR** (in `HasHost`) | accepted | placeholder |
| `RTSP://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s`, `rtsp://[]@[1:2:3:4:5:6:7:8:/64]x/s` | true | **throws** | throws | **throws IOOR** (in `HasNoUserInfo`) | **throws IOOR** | placeholder |

- **`Host` throws** for an IPv6 literal with a stray `:` before a `/` prefix length (37 of 400,000 mixed inputs;
  every one has that shape). `HasHost` is a `From`-only rule, so `FromStored` accepts these and is unaffected.
- **`UserInfo` throws** when, additionally, a userinfo of `[]` precedes the bracket (4 of 400,000). `HasNoUserInfo`
  is in the invariants `From` and `FromStored` share, so both throw.
- `BoundaryParse.TryParse` (`src/ServiceDefaults/BoundaryParse.cs`) catches only `ArgumentException`, so
  `POST /cameras` (`RegisterCameraRequest.cs:19`) and `PATCH /cameras/{id}` (`CameraEndpoints.cs:312`) let the
  `IndexOutOfRangeException` escape: **500 instead of 400 `CAMERA_INVALID_REQUEST`**. `ProvisionStreamCommandHandler`
  (`:33`) and `RepointStreamCommandHandler` (`:126`) also catch only `ArgumentException`.

Every other consumer the issue asks about, checked on the same 700,000 inputs:

| Consumer | Throws? | Why |
|---|---|---|
| `UrlRedaction.ToOrigin` | **never** | `TryParse` guards `TryCreate`; every input on which `Host` throws is caught first by `UrlAuthority.SlashInsideTheBracketsFollowsNoAddress` (spec 392 FR-012) — 0 of 37 escaped it |
| `AuditPayloadRedaction.Serialize` (both events; `CarriesZone` reads `Uri.DnsSafeHost` unguarded) | **never** | `CarriesZone` runs only after `ToOrigin` read the host successfully |
| `RtspPath.TryExtract` (ScenarioSimulator, `uri.AbsolutePath`) | throws on 31 inputs | **none of them is accepted by `From`**; the simulator reads only `CameraRegisteredV1`, published after `From` accepted the address |
| The two EF converters (`FromStored`) | as `FromStored` above | a row of a throwing shape cannot have been written through `From`, which has thrown on it since spec 001 |

So item 2 is cured in the two value objects only. `ToOrigin`, `AuditPayloadRedaction` and `RtspPath` are
characterised, not changed (§7).

### 1.4 Writers, readers and the UI

Unchanged since spec 392 §1.4 (`grep -rn "RtspUrl\.From(\|StreamSourceUrl\.From(" src` at `fbd4176b`): writers W1
`RegisterCameraRequest.cs:19`, W2 `CameraEndpoints.cs:312`, W3 `ProvisionStreamCommandHandler.cs:33`, W4
`RepointStreamCommandHandler.cs:126`; readers are the two EF converters (`CameraConfiguration.cs:99`,
`StreamConfiguration.cs:73`), which call `FromStored`. The management UI (`apps/shared/src/api/cameras.schema.ts`)
mirrors spec 001 FR-008 only and is not changed; it shows the server's 400.

---

## 2. User stories

### US1 (P1) — An address with `@`, `?` or `#` inside the IPv6 brackets is refused where it is written, and never audited as a host it does not name

As an **operator or integrator registering or correcting a camera through the API**, I want an address such as
`rtsp://@[fe80::1%@10.0.0.66:8554]/live` or `rtsp://[::1%25x?y]/s` refused with a message that says what is wrong,
and as an **auditor**, I want such an address recorded as the placeholder, so that no camera is registered, streamed
or audited as `[fe80::1]` while another parser connects to `10.0.0.66`.

**Why P1:** host confusion on a credential-free path (item 1) — a security defect — and the same parser-split
ambiguity for `?` / `#` (item 5).

**Independent test:** `RtspUrl.From` and `StreamSourceUrl.From` throw for every FR-001 row of §5 with the FR-001
message and none of the address; `UrlRedaction.ToOrigin` returns `<unparseable url>` for them; `POST /cameras` with
`rtsp://@[fe80::1%@10.0.0.66:8554]/live` answers 400 `CAMERA_INVALID_REQUEST` with a body free of `10.0.0.66`.

### US2 (P1) — An address the parser cannot read answers 400, not 500

As an **integrator**, I want `POST /cameras` and `PATCH /cameras/{id}` with `rtsp://[1:2:3:4:5:6:7:8:/64]` to
answer 400 `CAMERA_INVALID_REQUEST` with a message, so that a malformed address is a validation error I can fix,
not a server fault that pages someone and leaks nothing useful.

**Why P1:** a 500 on input validation is a defect at a trust boundary (item 2).

**Independent test:** both `From`s throw `ArgumentException` (not `IndexOutOfRangeException`) for every FR-002 and
FR-003 row of §5 with the stated message; `POST /cameras` with `rtsp://[1:2:3:4:5:6:7:8:/64]` answers 400.

### US2a (P1, same slice) — A row stored before this change stays readable

Rows already stored with an FR-001 shape (accepted until now) are read through `FromStored`, which gains no new
rule, so `GET /cameras`, `GET /cameras/{id}` and the stream reconciler keep working for them. Characterised (green
before and after), not a story of its own. The FR-002 shapes are accepted by `FromStored` and stay so; the FR-003
shapes were never storable (§1.3).

---

## 3. Acceptance scenarios (Gherkin)

```gherkin
Feature: The IPv6 brackets of a camera URL hold an address and a zone, nothing a parser splits on

  Background:
    Given an operator token with scope "sse.cameras.write" and membership in fab "munich"

  # happy path (unchanged)
  Scenario Outline: An ordinary or zoned address is still accepted
    When the operator registers a camera with address "<address>"
    Then the response is 201 Created
    Examples:
      | address                        |
      | rtsp://[fe80::1%25eth0]/x      |
      | rtsp://[fe80::1/64%25eth0]/s   |
      | rtsp://@[fe80::1]/x            |
      | rtsp://[::1%25%40x]/s          |

  # US1 bad request (new)
  Scenario Outline: A delimiter inside the brackets is refused, and the hidden host is not echoed
    When the operator registers a camera with address "<address>"
    Then the response is 400 with title "CAMERA_INVALID_REQUEST" and the detail is the rendered FR-001 text
    And the response body does not contain "<secret>"
    And no camera is stored and no CameraRegisteredV1 is published
    Examples:
      | address                                  | secret    |
      | rtsp://@[fe80::1%@10.0.0.66:8554]/live   | 10.0.0.66 |
      | rtsp://@[::1%25@evil.com:554]/s          | evil.com  |
      | rtsp://[::1%25x?y]/s                     | x?y       |
      | rtsp://[fe80::1%25eth0#]:554/s           | eth0#     |

  # US2 bad request (new; today 500)
  Scenario Outline: An address whose host the parser cannot read is refused with a message
    When the operator registers a camera with address "<address>"
    Then the response is 400 with title "CAMERA_INVALID_REQUEST" and the detail is the rendered <message> text
    Examples:
      | address                                   | message                         |
      | rtsp://[1:2:3:4:5:6:7:8:/64]              | FR-002                          |
      | rtsp://[::1:2:3:4:5:6:7:/64]/s            | FR-002                          |
      | RTSP://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s  | user:password@ (FR-003, existing) |

  # conflict ordering (unchanged): the address is parsed before the version is checked
  Scenario: A refused address on PATCH answers 400, not 412, and the camera is unchanged
    Given a camera "line-3-inlet" at "rtsp://10.0.5.12/h264" at version 2
    When the operator patches its address to "rtsp://[1:2:3:4:5:6:7:8:/64]" with If-Match version 1
    Then the response is 400 "CAMERA_INVALID_REQUEST"
    And the camera's address is still "rtsp://10.0.5.12/h264" at version 2

  # older messages win where an older rule already refuses
  Scenario Outline: An older rule keeps its message
    When the operator registers a camera with address "<address>"
    Then the response is 400 and the detail is the <older> text
    Examples:
      | address                              | older                         |
      | rtsp://@[::1%25x?@evil.com]/s        | raw-@ (spec 389)              |
      | rtsp://[::1%25x?y]x/s                | text after ']' (spec 392)     |
      | rtsp://user@[::1%25@evil.com:554]/s  | user:password@ (spec 001)     |

  # auth (unchanged)
  Scenario: A caller without the write scope is refused before the address is examined
    Given a token without scope "sse.cameras.write"
    When it registers a camera with address "rtsp://@[fe80::1%@10.0.0.66:8554]/live"
    Then the response is 403 and the body contains neither FR-001 text nor "10.0.0.66"

  # US1 audit (new)
  Scenario: A registration or address change over a delimiter in the brackets records the placeholder
    When CameraRegisteredV1 with "rtsp://@[fe80::1%@10.0.0.66:8554]/live" is audited
    Then the stored Url is "<unparseable url>"
    When CameraAddressChangedV1 from "rtsp://@[fe80::1%@10.0.0.66:8554]/live" to "rtsp://[fe80::1]/x" is audited
    Then the stored PreviousUrl is "<unparseable url>" and Url is "rtsp://[fe80::1]"
    And DetailChanged is null, in both directions (unchanged)

  # US2a stored rows (characterisation)
  Scenario: A camera stored before this change with a now-refused address is still read
    Given a cameras row whose rtsp_url is "rtsp://@[fe80::1%@10.0.0.66:8554]/live", inserted directly
    When the operator lists the fab's cameras and reads that camera
    Then both answer 200 and include the camera
    # The value is returned as stored; masking it in CameraDto is #3016, out of scope.

  # replay (new outcome; mechanism of spec 389 unchanged)
  Scenario: A pre-deploy CameraRegisteredV1 with a delimiter in the brackets is dead-lettered, not provisioned
    Given a CameraRegisteredV1 for a new camera with address "rtsp://@[fe80::1%@10.0.0.66:8554]/live"
    When StreamDistribution handles it
    Then the handler throws, no stream is saved and the gateway is not called
    And one Warning names "STREAM_INVALID_RTSP_SOURCE" and does not contain "10.0.0.66"
```

---

## 4. Functional requirements

- **FR-001 — No `@`, `?` or `#` inside the IPv6 brackets (items 1 and 5).** A new shared predicate
  `UrlAuthority.DelimiterInsideTheBrackets(url)` is true when any bracketed-host reading
  (`UrlAuthority.BracketedHostReadings`, spec 392 FR-011) holds `@`, `?` or `#` between its `[` and its first `]`.
  `RtspUrl.From` and `StreamSourceUrl.From` refuse it as their **last** rule (after the spec 392 slash rule), so
  every input an older rule refuses keeps that rule's message. Message, identical in both value objects:

  ```
  must not have '@', '?' or '#' inside the brackets of an IPv6 address: a zone never holds one, and a parser that splits there reads a different host
  ```
- **FR-002 — A host the parser cannot read is refused, not thrown (item 2, `Host`).** A new shared reader
  `UrlAuthority.TryReadHost(url, out host)` runs `Uri.TryCreate(url, UriKind.Absolute, …)` and reads `uri.Host`
  inside one `try`, and answers false on `IndexOutOfRangeException` — the shape of `UrlRedaction.TryParse`. Both
  `From`s add a rule **immediately after the raw-`@` rule and before the empty-host rule** (the empty-host rule
  reads the host, so this is its precondition), and the empty-host rule reads through `TryReadHost`. Message,
  identical in both value objects:

  ```
  must name a host the parser can read: the address inside the brackets is malformed, for example a ':' left before a '/' prefix length
  ```

  Ordering note: every input this rule refuses threw `IndexOutOfRangeException` before it reached any later rule,
  so no input loses an older message. Some of them (`rtsp://[1:2:3:4:5:6:7:8:/64]`) would also meet the spec 392
  slash message if it were reached; the FR-002 message wins for them, by position.
- **FR-003 — Userinfo the parser cannot read is refused, not thrown (item 2, `UserInfo`).** A new shared reader
  `UrlAuthority.TryReadUserInfo(url, out userInfo)`, same shape. `HasNoUserInfo` (in the invariants `From` and
  `FromStored` share) reads through it: a throwing read is treated as unparseable, exactly as a failed `TryCreate`
  is today, and refused with the **existing** message `must not contain a user:password@ segment`. No new message:
  every input in this class carries a userinfo (`[]@`), and unparseable input has always met this message.
- **FR-004 — `ToOrigin` withholds a delimiter in the brackets.** `UrlRedaction.ToOrigin` returns
  `UrlRedaction.Unparseable` when `UrlAuthority.DelimiterInsideTheBrackets(url)` is true. Consequently
  `AuditPayloadRedaction` stores the placeholder for that side of `CameraAddressChangedV1` and for
  `CameraRegisteredV1.Url`. `DetailChanged` is already `null` for every such pair (the zone arm) and stays so —
  characterised, not changed; `AuditPayloadRedaction` itself is not edited.
- **FR-005 — Stored rows stay readable.** `FromStored` gains no rule. Its **accept set is unchanged** for every
  input: it keeps accepting every FR-001 and FR-002 row. The one observable difference is FR-003's: for the inputs
  on which `UserInfo` throws, `FromStored` (and so the EF converter) now throws `ArgumentException` with the
  userinfo message instead of `IndexOutOfRangeException` — it refused them before and refuses them after, and no
  such row can have been written through `From` (A-3). No migration, no rewrite (#2990).
  `FromStoredCallSiteTests` / `FromStoredCallerIlTests` keep guarding the bypass.
- **FR-006 — Replay is bounded and visible**, exactly as spec 389 FR-006: the two StreamDistribution integration
  event handlers keep throwing on `STREAM_INVALID_RTSP_SOURCE`, no failure policy is added, the default moves the
  message to the error queue on the first attempt. For the FR-002 shapes the exception changes from an escaped
  `IndexOutOfRangeException` to that typed failure.
- **FR-007 — The value objects read `Uri` only through the guarded readers.** `RtspUrl.cs` and `StreamSourceUrl.cs`
  contain no `Uri.TryCreate(` and no `.Host` / `.UserInfo` read of their own; they call
  `UrlAuthority.TryReadHost` and `UrlAuthority.TryReadUserInfo`. `RtspUrl.cs`, `StreamSourceUrl.cs` and
  `UrlRedaction.cs` each call `UrlAuthority.DelimiterInsideTheBrackets`, and only `UrlAuthority.cs` declares it.
  A cross-row agreement test asserts that whatever `From` refuses on FR-001 or FR-002, `ToOrigin` renders as the
  placeholder, and that the two `From`s agree row by row.
- **FR-008 — No new cross-context reference, no new state, no new error code.** All three contexts already
  reference `Shared.Kernel`; `UrlAuthority` stays a stateless static class. The messages surface as the `detail`
  of CameraCatalog's existing 400 `CAMERA_INVALID_REQUEST` and inside StreamDistribution's existing
  `STREAM_INVALID_RTSP_SOURCE`, rendered by `Ensure` as `value: <message> (Parameter 'value')` and containing no
  part of the input. NetArchTest boundary suite and `PrimitiveBoundaryTests` pass unmodified.

## 5. Accept / reject sets (pinned by tests in both value objects)

**Accept (unchanged, characterisation):** `rtsp://[fe80::1%25eth0]/x`, `rtsp://[fe80::1/64%25eth0]/s`,
`rtsp://[fe80::1%25eth0/64]/s`, `rtsp://[fe80::1]/x`, `rtsp://@[fe80::1]/x`, `rtsp://@cam/s`,
`rtsp://[::1%25%40x]/s`, `rtsp://[::1%25x%3Fy]/s`, `rtsp://[fe80::1]?q`, `rtsp://[fe80::1]#f`,
`rtsp://[fe80::1]:554/s`, plus the spec 392 §5 accept set (its suites stay green unmodified).

**Reject with an older message (unchanged, characterisation):** `rtsp://@[::1%25x?@evil.com]/s`,
`rtsp://[::1%25x?y]/a@b` (raw-`@`, spec 389); `rtsp://[::1%25x?y]x/s` (text after `]`, spec 392);
`rtsp://user@[::1%25@evil.com:554]/s`, `rtsp://[fe80::1%@10.0.0.66:8554]/live`, `rtsp://[::1%25@evil.com:554]/s`
(userinfo, spec 001).

**Reject with the FR-001 message (new, red):** `rtsp://@[fe80::1%@10.0.0.66:8554]/live`,
`rtsp://@[::1%25@evil.com:554]/s`, `rtsp://@[fe80::1%25eth0@]/s`, `rtsp://[::1%25x?y]/s`, `rtsp://[::1%25x#y]/s`,
`rtsp://[fe80::1%25a?b]/x`, `rtsp://[fe80::1%x?y]/s`, `rtsp://[::1%25x?y]`, `rtsp://[fe80::1%25eth0#]:554/s`,
`rtsp://@[::1%25x?y]/s`.

**Reject with the FR-002 message (new, red — today `IndexOutOfRangeException`):** `rtsp://[1:2:3:4:5:6:7:8:/64]`,
`rtsp://[::1:2:3:4:5:6:7:/64]/s`, `rtsp://[1:2:3:4:5:6:7:8:/64]/live`, `rtsp://[1:2:3:4:5:6:7:8:/64]x/s`,
`RTSP://@[1:2:3:4:5:6:7:8:/64%25eth0]`.

**Reject with the existing userinfo message via FR-003 (red — today `IndexOutOfRangeException`), in `From` and
`FromStored`:** `RTSP://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s`, `rtsp://[]@[1:2:3:4:5:6:7:8:/64]x/s`.

**Hydrate via `FromStored` (characterisation, green before and after):** every FR-001 and FR-002 row, plus the
accept set.

**`ToOrigin` (new, red):** every FR-001 row -> placeholder; `rtsp://user@[::1%25@evil.com:554]/s` -> placeholder
(today `rtsp://[::1]`). **`ToOrigin` (characterisation):** every FR-002 and FR-003 row -> placeholder (already);
the accept set's bracket rows keep their origins (`rtsp://[fe80::1%25eth0]/x` -> `rtsp://[fe80::1]`,
`rtsp://[::1%25%40x]/s` -> `rtsp://[::1]`).

## 6. Assumptions

- **[Assumption] A-1 — the delimiter set is `@`, `?`, `#`; `\` is left out.** `\` inside the brackets
  (`rtsp://[::1%25x\y]/s`) is accepted today and is the same class (it is in `UrlAuthority`'s own terminator set),
  but #3023 item 5 names `?` and `#` only and the lane comment scopes the work to items 1, 2 and 5. `/` cannot be
  in the set: it introduces a prefix length (`[fe80::1/64]`, `[fe80::1%25eth0/64]`), and `/` inside a zone already
  fails to parse. Recorded as a residual (§7); the owner may widen it.
- **[Assumption] A-2 — the rule reads the whole bracket text, not only the zone.** An `@`, `?` or `#` before the
  `%` makes the address unparseable, so it is refused by the userinfo rule first; scanning the whole bracket text
  is simpler and fails closed. A zone written as raw `%eth0` (no `25`) is in scope the same way.
- **[Assumption] A-3 — the `FromStored` exception-type change is accepted.** FR-005: `FromStored` refuses the same
  inputs before and after; only the exception type for the FR-003 shapes changes. Keeping it bit-for-bit would need
  a second, unguarded copy of `HasNoUserInfo` for `FromStored` only, which keeps a known throwing read alive to
  preserve a crash. Those shapes have thrown in `From` since spec 001, so no stored row can carry one except by a
  direct database insert, where hydration fails either way.
- **[Assumption] A-4 — only `IndexOutOfRangeException` is caught**, as in `UrlRedaction.TryParse`. No other
  exception type was observed from `Uri` on 700,000 inputs; catching more would hide a different defect.
- **A-5 — Idempotency replay across the deploy** (as spec 389 A-3 / spec 392 A-4): a client retrying, after the
  deploy, a request that succeeded before it with an FR-001 address gets 400, not the original 201. Accepted.
- **A-6 — Accepted input narrows.** Every FR-001 row was accepted by `POST /cameras`, `PATCH /cameras/{id}`,
  provisioning and re-pointing before this spec. That is the change #3023 asks for.

## 7. Out of scope

- #3023 items 3 (invisible characters in a plain DNS host), 4 (trailing whitespace after a plain host / the pinned
  bracket false positive), 6 (the second bracket scan in `AuditPayloadRedaction`) and 7 (`rtsp://./`,
  `rtsp://..`). They stay open on the issue.
- **`\` inside the brackets** (A-1) — residual, accepted; 35 of 400,000 fuzz inputs.
- **The cure "refuse any `@` in the authority when `Uri.UserInfo` is empty"** — it would refuse `rtsp://@cam/s`,
  which spec 389 §7.1 accepts and a test pins.
- **Guarding `UrlRedaction.ToOrigin`'s own `Uri` reads, `AuditPayloadRedaction.CarriesZone` and the ScenarioSimulator's
  `RtspPath.TryExtract`.** None throws on any input it can be given (§1.3: `ToOrigin` 0 of 700,000; the audit
  0 of 700,000; `RtspPath` throws only on inputs `From` refuses, and it reads only `CameraRegisteredV1`). A guard
  with no input that reaches it cannot be observed red (ADR-0139), and would be the speculative error handling
  ADR-0036 rules out. Recorded so the test-adversary can try to reach them; a reaching input is a new finding, not
  a widening of this spec.
- The full RFC 6874 zone character set (`unreserved / pct-encoded`); only the three delimiters are refused.
- #3016 (`CameraDto` / the UI serve stored URLs verbatim), #2990 (stored-row migration), UI validation in
  `cameras.schema.ts`.

## 8. End-to-end test procedure (phase 5)

On a booted Aspire stack (one per machine; stop it before building; check the AppHost PID's start time is after
the last commit):

1. Mint a write token from Aspire's proxied Keycloak endpoint for an operator in `munich`.
2. `POST /cameras?fabId=munich` with `rtspUrl` `rtsp://@[fe80::1%@10.0.0.66:8554]/live` -> 400
   `CAMERA_INVALID_REQUEST`, detail the rendered FR-001 text, body free of `10.0.0.66`. Repeat with
   `rtsp://[::1%25x?y]/s` -> 400, FR-001 text.
3. `POST` with `rtsp://[1:2:3:4:5:6:7:8:/64]` -> **400** (not 500), detail the rendered FR-002 text; with
   `RTSP://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s` -> 400, the userinfo text. Confirm in the Aspire dashboard that
   neither request logged an unhandled exception.
4. `POST` `rtsp://camera-sim:8554/station-4-roughing` -> 201; `PATCH` its address to `rtsp://[1:2:3:4:5:6:7:8:/64]`
   with the ETag -> 400; `GET` shows the original address and version.
5. Insert a row directly (`psql`, camera-catalog database) with
   `rtsp_url = 'rtsp://@[fe80::1%@10.0.0.66:8554]/live'`; `GET /cameras?fabId=munich` and `GET /cameras/{id}` ->
   200.
6. Record status codes and bodies on the PR. Latency: N/A.
