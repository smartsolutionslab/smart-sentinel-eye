# Spec 408: A camera URL holding a control character or whitespace is refused, and the simulator drops an address the parser cannot read instead of retrying it

**Feature Branch**: `fix/3031-url-control-characters` (cut from `origin/develop` at `c8c3773c`)

**Created**: 2026-10-18

**Status**: Phases 1-3 written (Specify, Plan, Tasks). No `[NEEDS CLARIFICATION]` remains: the scope is the lane
comment on #3031 (items 1 and 2 only; "the owner may overrule"), the stored-row direction is the one decided on
#3012 and applied by specs 392/394 (`FromStored` unchanged, no migration), and the two places where the lane
comment's wording meets an older pinned decision are resolved and recorded as **[Assumption]** in §6 (A-1, A-2).
Both are flagged for the owner in the PR.

**Input**: Issue [#3031](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3031), *Camera URL: refuse
control characters and whitespace URL-wide; simulator RtspPath throw; empty port; ToOrigin/CarriesZone independent
of rule order* — items 1 and 2. Items 3-8 stay open on the issue and out of this spec (§7). Both items are
follow-ups of #3023 (spec 394), found by its adversary, security and backend reviews.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** throughout: the accepted input of two value objects
narrows, `ToOrigin` renders the placeholder for more shapes, an audit `DetailChanged` moves from `true` to `null`
for one class of pairs, and a simulator retry becomes a log-and-drop. Phase 4a **red**, with characterisation for
every row §5 lists as unchanged. No refactor rides along.

**Security-sensitive.** Item 1 is input validation of an address that is stored verbatim and handed to MediaMTX as
the pull source: a raw CR/LF in it is request-line / header injection for any consumer that writes the stored
string unvalidated. Phase 4 includes a `test-adversary` pass; phase 6 includes `security-reviewer`.

**Spec number.** 408. Checked 2026-10-18 after `git fetch origin`: `origin/develop` highest 405; `git ls-tree
<ref> specs/` over every remote head finds nothing in 406-409; sibling worktrees hold 406
(`sse-3025`, `406-rtsp-path-refuses-authority-less-urls`, unmerged, no PR yet) and 407 (`sse-3027`). Re-check
before the PR is opened.

**ADRs.** ADR-0038 / ADR-0046 / ADR-0066 (hand-written value objects, `From` + `Ensure.That`), ADR-0105
(`Ensure.That`; a message never carries the value), ADR-0047 / ADR-0089 (existing `ApiError` codes, unchanged),
ADR-0042 / ADR-0088 (Wolverine default failure policy, unchanged), ADR-0111 (Scenario Simulator, dev tooling),
ADR-0050 (logging; the existing skip message is reused), ADR-0139 / ADR-0144 (phase 4a colours; the lane
implements, it does not decide), ADR-0065 (Domain >= 90%, Application >= 80%, Shared >= 90%), ADR-0084 (metrics,
advisory), ADR-0036 (smallest change; no drive-by error handling). Constitution §II (no primitive state added) and
§III (shared code in `Shared.Kernel`, which every touched context already references). Prior art: spec 394 (the
last-rule placement so older messages win, the guarded `Uri` reads, the single-source guard, the agreement test),
spec 392 (§7.1 recorded trailing whitespace as an accepted residual), spec 389 (`FromStored`, replay to the error
queue), `UrlRedaction.TryParse` / `UrlAuthority.TryReadHost` (the `IndexOutOfRangeException` guard shape item 2
mirrors).

**No new ADR.** Every mechanism is reused: one more `Satisfies` rule with its own constant message placed after the
older rules (spec 394 FR-001), one more `||` in `ToOrigin` (specs 392/394), a span predicate in `UrlAuthority`, the
single-source guard, and the `catch (IndexOutOfRangeException)` trust-boundary guard. No contract, scope, error
code, persistence shape, Wolverine policy, log message or migration changes.

**Latency budget.** N/A. Registration, address change and re-point are control-plane writes; the audit redaction
runs on the audit consumer; the simulator is dev tooling, off in CI and outside run mode. None is on a §IV leg. The
added work is one ordinal scan of a <= 2048 character string per write and per `ToOrigin` call.

---

## 1. What was found (premises verified by experiment on `c8c3773c`, not assumed)

A scratch probe (a file-based app in the session scratchpad referencing `CameraCatalog.Domain` and
`StreamDistribution.Domain`, with `RtspPath.TryExtract`'s body copied verbatim; not committed) enumerated every
UTF-16 code unit, then called both `From`s, `RtspUrl.FromStored`, `UrlRedaction.ToOrigin` and the `RtspPath` body
on each of 84 characters (§1.1) inserted at 17 positions. A **counterfactual** (the rule of §4 applied to the
worktree, every unit suite of plan §5.4 run, then `git checkout -- src`) found every existing assertion the rule
contradicts (§1.4).

### 1.1 The character set, exactly

| Predicate | Members (UTF-16) | Count |
|---|---|---|
| `char.IsControl` (Unicode Cc) | U+0000-U+001F, U+007F-U+009F | 65 |
| `char.IsWhiteSpace` | U+0009-U+000D, U+0020, U+0085, U+00A0, U+1680, U+2000-U+200A, U+2028, U+2029, U+202F, U+205F, U+3000 | 25 |
| both | U+0009-U+000D, U+0085 | 6 |
| **union (the rule's set)** | C0, U+007F, C1, and the 19 whitespace characters that are not controls | **84** |

Every Cc, Zs, Zl and Zp code point is in the BMP, so a `char` scan is exact: no surrogate pair encodes a member, and
a lone surrogate is not one. `Rune.IsWhiteSpace` agrees with `char.IsWhiteSpace` on every BMP scalar (probe).

**Not in the set (and not widened to, per the issue and lane comment): Unicode format characters (Cf)** — U+200B
ZERO WIDTH SPACE, U+200E LEFT-TO-RIGHT MARK, U+202E RIGHT-TO-LEFT OVERRIDE, U+FEFF, U+00AD SOFT HYPHEN, U+180E
MONGOLIAN VOWEL SEPARATOR (`char.IsWhiteSpace` is false for it since Unicode 6.3). The issue's "fullwidth and bidi
characters in the zone" are not controls or whitespace either; `ToOrigin` already renders the placeholder for them
(non-ASCII host text), and `From` keeps accepting them (residual, §7). `UrlAuthority.IsBlankHost` (spec 392) uses a
**wider** set (whitespace + control + Cf) but only to decide whether a host is empty; the two are deliberately not
unified.

**Percent-encoded forms are not raw characters and stay accepted**: `rtsp://cam/x%0D%0AX`, `rtsp://cam/x%20y`
(probe: accepted, origin rendered).

### 1.2 Where the set is accepted today

| Position (template) | `From` (both, identical) | `ToOrigin` |
|---|---|---|
| path `rtsp://10.0.0.66/li{c}ve`, query `?q{c}x`, fragment `#f{c}x`, path end `/live{c}`, `rtsp://@cam/li{c}ve`, `rtsp://[fe80::1]/live{c}` | **accepted, all 84** | **renders the origin, all 84** |
| IPv6 zone `[fe80::1%25eth0{c}]`, `[fe80::1%25e{c}th0]` | **accepted, all 84** | renders for the 34 ASCII ones (C0, U+0020, U+007F); placeholder for the 50 non-ASCII ones |
| host end, nothing after `rtsp://cam{c}` | **accepted for 22**: U+0009, U+000A, U+000D, U+0020 (the parser trims them) and the 18 non-ASCII whitespace | renders for the 4 trimmed ones |
| inside a plain host `rtsp://cam{c}/live`, `rtsp://10.0.{c}0.66/live`, `rtsp://{c}cam/live` | refused for 66 (unparseable: the userinfo message, #3031 item 6); **accepted for the 18 non-ASCII whitespace** | placeholder (non-ASCII host) |
| port `:85{c}54`, `:8554{c}/` | refused, all 84 (userinfo message) | placeholder |
| after `]` `rtsp://[fe80::1]{c}/live` | refused, all 84 (spec 392 text-after-`]` message) | placeholder |
| before the scheme `{c}rtsp://cam/live`, `rtsp:/{c}/cam/live` | refused, all 84 (the `rtsp://` prefix rule) | as today (pinned to render for leading whitespace, §6 A-1) |

The issue's named rows, measured:

| Input | `From` (both) | `FromStored` | `ToOrigin` |
|---|---|---|---|
| `rtsp://10.0.0.66/live\r\nX-Inject: 1` | **accepted** | accepted | `rtsp://10.0.0.66` |
| `rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live` | **accepted** | accepted | `rtsp://[fe80::1]:8554` |
| `rtsp://[fe80::1%25eth0\r\nCSeq: 9]/s` (spec 394 adversary) | **accepted** | accepted | `rtsp://[fe80::1]` |
| `rtsp://[fe80::1%25\u0000]/x`, `rtsp://10.0.0.66/live x`, `rtsp://10.0.0.66/live\t` | **accepted** | accepted | rendered |
| `rtsp://a b/x` | refused (userinfo message) | refused | placeholder |

What `Uri` does with them (probe): it **trims** leading and trailing U+0009/U+000A/U+000D/U+0020 only; inside the
path, query and fragment it **escapes** every member (`/live%0D%0AX-Inject:%201`) in `AbsolutePath` /
`AbsoluteUri`, while `OriginalString` — and so the value object's `Value`, which is the stored and published
string — keeps the raw characters. Inside a zone it drops the zone from `Host`. **No host confusion**: every
modelled reader agrees on the host for every accepted row (the issue's own finding); the defect is the raw
characters reaching MediaMTX (`StreamSourceUrl` is the pull source) and any other consumer that writes the stored
string unvalidated. MediaMTX's Go `net/url` refuses control characters (reasoned from its source, not run; §7).

**`From` does not trim** (it stores the input verbatim). Leading whitespace is already refused by the `rtsp://`
prefix rule; trailing whitespace is accepted today (spec 392 §7.1 recorded it as an accepted residual, not as a
decision). The management UI schema (`apps/shared/src/api/cameras.schema.ts`) does not trim the address either, so
a pasted trailing newline reaches the server and will now get the 400 (§6 A-6).

**Ordering**: every older rule is guarded (spec 394), so none throws; the new rule placed **last** keeps every older
message for every input an older rule refuses (length cap 2048 first, then the prefix and userinfo invariants, then
raw `@`, unreadable host, empty host, text after `]`, slash in brackets, delimiter in brackets).

### 1.3 Item 2 — `RtspPath.TryExtract` throws, and the simulator retries instead of dropping

`src/ScenarioSimulator/CameraSim/RtspPath.cs:14-21` calls `Uri.TryCreate` and then reads `uri.AbsolutePath`
unguarded. Measured with the method body verbatim:

| Input | `TryCreate` | `AbsolutePath` | `TryExtract` today | `From` today (both) | `From` before spec 392 (`623f3d38^`) |
|---|---|---|---|---|---|
| `rtsp://[1:2:3:4:5:6:7:8:/64]` | true | **throws** | **throws `IndexOutOfRangeException`** | refused (unreadable host, spec 394) | **accepted** |
| `rtsp://[1:2:3:4:5:6:7:8:/64]?x` | true | **throws** | **throws** | refused | **accepted** |
| `rtsp://[1:2:3:4:5:6:7:8:/64]#x` | true | **throws** | **throws** | refused | **accepted** |
| `rtsp://[1:2:3:4:5:6:7:8:/64]x/s` | true | **throws** | **throws** | refused | **accepted** |
| `rtsp://[1:2:3:4:5:6:7:8:/64]/live` | true | `/live` (`Host` throws) | true, `live` | refused | **accepted** |
| `rtsp://[::1:2:3:4:5:6:7:/64]/s` | true | `/s` (`Host` throws) | true, `s` | refused | **accepted** |
| `FILE://‎/x` (spec 394's `UrlsTheParserThrowsOnWhileCreatingTheUri`) | **throws** | — | **throws** | refused (prefix) | refused (prefix) |

**The premise holds, and the reaching path is real.** Before spec 392 (`623f3d38`, 2026-10-17) `RtspUrl.From` was
the stored invariants plus the raw-`@` rule (`git show 623f3d38^:src/CameraCatalog/Domain/Camera/RtspUrl.cs`); it
read only `UserInfo`, which is `''` for these shapes, so it accepted all six and `CameraRegisteredV1` was published
with them between spec 001 (2026-05-25) and spec 392. A replay of such an event, or any message on the simulator's
queue, reaches `CameraRegisteredSimHandler.Handle` (`:38`), where the escaped exception sends the message through
Wolverine's default failure policy to the error queue instead of the handler's own log-and-drop branch (`:38-45`,
"log and drop, don't retry"). The `FILE://` row cannot come from a published event (no write ever accepted it) and
is included only because the guard covers `TryCreate` too, as `UrlRedaction.TryParse` does.

A seeded sweep (one character from U+0000-U+02FF, then every 97th code unit, inserted at every position of three
bases): `TryCreate` threw 0 times on `rtsp://` inputs; `AbsolutePath` threw 1,772 times, every one on the
`[1:2:3:4:5:6:7:8:/64]` family.

**Sibling work on the same file.** Spec 406 (`sse-3025`, #3025, committed, not merged, no PR) adds
`|| uri.Host.Length == 0` to the same `if` in `TryExtract`. `uri.Host` throws on **all six** rows above, including
the two whose `AbsolutePath` reads cleanly — so spec 406 alone turns the `/live` and `/s` rows from "true" into a
throw. This spec's guard must cover every `Uri` read in `TryExtract`, whichever lands first (plan §3.4, tasks T050).

### 1.4 Existing assertions the rule contradicts (counterfactual, every hit read)

Run on the worktree with the rule of §4 applied to both `From`s and `ToOrigin`, then reverted. Suites:
`Shared.Kernel.Tests` (15,240), `CameraCatalog.Domain.Tests` (574), `StreamDistribution.Domain.Tests` (657),
`AuditObservability.Application.Tests` (1,084), `ScenarioSimulator.Tests` (329, green),
`StreamDistribution.Application.Tests` (218, green), `Architecture.Tests` URL filter of plan §5.4 (793, green).

**With `ToOrigin` checking the whole string** (the literal reading of "placeholder for the same shapes"): **~100
failing rows across 11 test methods**, ~90 of them leading or trailing whitespace that four earlier specs pin as
rendering an origin on purpose — `UrlRedactionLeadingWhitespaceBracketTailTests` (4 rows),
`UrlRedactionHostCharsetAdversarialTests.Non_ascii_text_outside_the_authority_still_renders_the_origin` (16) and
`.An_ascii_authority_in_an_unusual_spelling_renders_its_origin` (2), `AuditPayloadDetailChangedLeadingWhitespaceBracketTailTests`
(2), `AuditPayloadDetailChangedLocatorCharacterisationTests` (2), `AuditPayloadDetailChangedZoneAdversarialTests`
(row `:47`, `"  rtsp://[fe80::1%25eth0]/x  "`, failing ~64 pair indices), `AuditPayloadRedactionPostFixAdversarialTests.DetailChanged_edges`
(`:98`). Retiring those is not justified by #3031, which says nothing about the parser-trimmed edges; that reading
is **rejected** (A-1).

**With `ToOrigin` ignoring leading and trailing runs of the set (FR-002, the reading taken): 6 rows in 5 files**,
each retired by the issue's own decision (A-2):

| # | File:line | Row | Today | After | Why retiring is justified |
|---|---|---|---|---|---|
| 1 | `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAuthorityHostAdversarialTests.cs:151` | `"rtsp://[fe80::1]/x "` in `AddressesThatWorkedBeforeAndStillDo` | accepted | refused (FR-001) | #3031 item 1: "whitespace anywhere in the URL"; spec 392 §7.1 recorded trailing whitespace as an accepted residual, never a decision |
| 2 | `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAuthorityHostAdversarialTests.cs:151` | same | accepted | refused | same |
| 3 | `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadZoneDelimiterAdversarialTests.cs:59-69` (`A_zone_holding_a_line_break_is_dropped_from_the_origin_with_the_rest_of_the_zone`, address `:62`, assertion `:67`) | `rtsp://[fe80::1%25eth0\r\nCSeq: 9]/s` -> `Url` `rtsp://[fe80::1]` | rendered | placeholder | it is the spec 394 adversary finding #3031 item 1 was filed from; the lane comment names this outcome |
| 4 | `tests/Shared.Kernel.Tests/UrlRedactionUserInfoAdversarialTests.cs:162` | `rtsp://admin:12345 @cam/x` -> `rtsp://cam` | rendered | placeholder | interior whitespace (lane: "placeholder for the same shapes"); `From` refuses it on the older userinfo rule, so no write ever carried it; the change fails closed and exposes nothing |
| 5 | `tests/Shared.Kernel.Tests/UrlRedactionUserInfoAdversarialTests.cs:163` | `rtsp://admin:12345\t@cam/x` -> `rtsp://cam` | rendered | placeholder | same |
| 6 | `tests/Shared.Kernel.Tests/UrlRedactionUserInfoAsHostTests.cs:147` | `rtsp://admin:12345 @cam/s` -> `rtsp://cam` in `Unchanged` | rendered | placeholder | same |

Rows 1-3 retire the exact behaviour the issue asks to change. Rows 4-6 are the owner's call in substance (A-2): they
are listed so the PR can say which assertions moved and why, not discovered in review.

No other row in the corpora changes: the spec 389/392/394 refused rows carrying whitespace or controls
(`rtsp://admin:4242#@cam/s `, `rtsp://admin:4242 @cam/s`, `rtsp://[fe80::1] /x`, `rtsp://[fe80::1]\u0000/x`,
`rtsp://\u0001/`, `rtsp://[fe80::1%25 ?evil]/s`, `rtsp://[fe80::1]:554 /x`, ...) keep their older message because the
new rule is last; the simulator's log-redaction rows (`rtsp://cam.example/CTRL\u0007PATHMARK`) assert absence of the
path, which a placeholder satisfies; `StreamDistribution.Application.Tests` and the Architecture filter stay green.

---

## 2. User stories

### US1 (P1) — An address holding a control character or whitespace is refused where it is written

As an **operator or integrator registering or correcting a camera through the API**, I want an address such as
`rtsp://10.0.0.66/live\r\nX-Inject: 1` or `rtsp://[fe80::1%25eth0 ]/live` refused with a message that says what is
wrong and does not repeat the address, so that no raw line break, tab, NUL or space is stored, published to
StreamDistribution and handed to MediaMTX as a pull source.

**Why P1:** input validation at a trust boundary for a string that is replayed verbatim into a request line.

**Independent test:** `RtspUrl.From` and `StreamSourceUrl.From` throw `ArgumentException` with the FR-001 message
for each of the 84 characters at each accepting position of §1.2; `POST /cameras` with
`rtsp://10.0.0.66/live\r\nX-Inject: 1` answers 400 `CAMERA_INVALID_REQUEST` with a body free of `X-Inject`.

### US1a (P1, same slice) — The audit and the logs never render a clean origin for such an address

As an **auditor**, I want an address with a control character or whitespace inside it recorded as the placeholder,
and a change to or from one recorded with `DetailChanged: null` (unknown), so the audit never claims "same host,
different detail" for an address no write would accept.

**Independent test:** `UrlRedaction.ToOrigin("rtsp://10.0.0.66/live\r\nX-Inject: 1")` is `<unparseable url>`; a
`CameraAddressChangedV1` from `rtsp://10.0.0.66/live` to it stores `PreviousUrl` `rtsp://10.0.0.66`, `Url`
`<unparseable url>`, `DetailChanged` `null` (today `rtsp://10.0.0.66` and `true`).

### US2 (P2) — The simulator logs and drops an address the parser cannot read

As a **developer running the scenario simulator**, I want a `CameraRegisteredV1` whose address makes `Uri` throw
(`rtsp://[1:2:3:4:5:6:7:8:/64]`) skipped with the existing "not a simulated camera" log line, so that a replayed
pre-spec-392 event is acknowledged rather than retried into the error queue.

**Why P2:** dev tooling (ADR-0111), off in CI; reachable only by replay or a hand-sent message.

**Independent test:** `RtspPath.TryExtract("rtsp://[1:2:3:4:5:6:7:8:/64]", out var path)` returns false with
`path == ""` and does not throw; the handler rig records one `SkippedNonSimulatedCamera` entry with
`<unparseable url>`, no camera-sim request, no correlation entry, and `Handle` completes.

### Stored rows (characterisation, not a story)

Rows already stored with a §1.2 shape are read through `FromStored`, which gains no rule, so `GET /cameras`,
`GET /cameras/{id}` and the stream reconciler keep working for them. Green before and after.

---

## 3. Acceptance scenarios (Gherkin)

```gherkin
Feature: A camera address carries no control character and no whitespace

  Background:
    Given an operator token with scope "sse.cameras.write" and membership in fab "munich"

  # happy path (unchanged)
  Scenario Outline: An ordinary, zoned, percent-encoded or format-character address is still accepted
    When the operator registers a camera with address "<address>"
    Then the response is 201 Created
    Examples:
      | address                          |
      | rtsp://10.0.5.12/h264            |
      | rtsp://[fe80::1%25eth0]:8554/x   |
      | rtsp://cam/x%0D%0Ay%20z          |
      | rtsp://cam/li​ve            |

  # US1 bad request (new)
  Scenario Outline: A control character or whitespace anywhere is refused, and the address is not echoed
    When the operator registers a camera with address "<address>"
    Then the response is 400 with title "CAMERA_INVALID_REQUEST" and the detail is the rendered FR-001 text
    And the response body does not contain "<secret>"
    And no camera is stored and no CameraRegisteredV1 is published
    Examples:
      | address                                          | secret   |
      | rtsp://10.0.0.66/live\r\nX-Inject: 1             | X-Inject |
      | rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live | X-Inject |
      | rtsp://10.0.0.66/live?token=a\tSECRET            | SECRET   |
      | rtsp://cam /live                            | cam      |
      | rtsp://10.0.0.66/live\u0000SECRET                | SECRET   |
      | rtsp://10.0.0.66/live                            | n/a      |
    # last row: one trailing U+0020 after "live"

  # older messages win where an older rule already refuses
  Scenario Outline: An older rule keeps its message
    When the operator registers a camera with address "<address>"
    Then the response is 400 and the detail is the <older> text
    Examples:
      | address                       | older                       |
      | rtsp://a b/x                  | user:password@ (spec 001)   |
      | rtsp://[fe80::1] /x           | text after ']' (spec 392)   |
      | rtsp://admin:4242#@cam/s      | raw-@ (spec 389)            |
      | rtsp://[fe80::1%25 ?evil]/s   | delimiter in brackets (394) |
    # row 3 carries a trailing U+0020; row 1's space is inside the host; row 4's is inside the zone

  # conflict ordering (unchanged): the address is parsed before the version is checked
  Scenario: A refused address on PATCH answers 400, not 412, and the camera is unchanged
    Given a camera "line-3-inlet" at "rtsp://10.0.5.12/h264" at version 2
    When the operator patches its address to "rtsp://10.0.5.12/h264\r\nX: 1" with If-Match version 1
    Then the response is 400 "CAMERA_INVALID_REQUEST"
    And the camera's address is still "rtsp://10.0.5.12/h264" at version 2

  # auth (unchanged)
  Scenario: A caller without the write scope is refused before the address is examined
    Given a token without scope "sse.cameras.write"
    When it registers a camera with address "rtsp://10.0.0.66/live\r\nX-Inject: 1"
    Then the response is 403 and the body contains neither the FR-001 text nor "X-Inject"

  # US1a audit (new)
  Scenario: A registration or address change over such an address records the placeholder
    When CameraRegisteredV1 with "rtsp://10.0.0.66/live\r\nX-Inject: 1" is audited
    Then the stored Url is "<unparseable url>"
    When CameraAddressChangedV1 from "rtsp://10.0.0.66/live" to "rtsp://10.0.0.66/live\r\nX-Inject: 1" is audited
    Then the stored PreviousUrl is "rtsp://10.0.0.66", Url is "<unparseable url>" and DetailChanged is null
    And the stored payload does not contain "X-Inject"

  Scenario: A zoned address with a line break keeps DetailChanged null (unchanged) and now stores the placeholder
    When CameraAddressChangedV1 from "rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live" to "rtsp://[fe80::1]:8554/live" is audited
    Then the stored PreviousUrl is "<unparseable url>" and DetailChanged is null

  # parser-trimmed edges (unchanged, pinned by earlier specs)
  Scenario: Leading or trailing whitespace alone does not make ToOrigin withhold the origin
    Then ToOrigin of " rtsp://cam/s" is "rtsp://cam" and ToOrigin of "rtsp://cam/x " is "rtsp://cam"

  # stored rows (characterisation)
  Scenario: A camera stored before this change with a now-refused address is still read
    Given a cameras row whose rtsp_url is "rtsp://10.0.0.66/live\r\nX-Inject: 1", inserted directly
    When the operator lists the fab's cameras and reads that camera
    Then both answer 200 and include the camera
    # The value is returned as stored; masking it in CameraDto is #3016, out of scope.

  # replay (new outcome; mechanism of spec 389 unchanged)
  Scenario: A pre-deploy CameraRegisteredV1 with such an address is dead-lettered, not provisioned
    Given a CameraRegisteredV1 for a new camera with address "rtsp://10.0.0.66/live\r\nX-Inject: 1"
    When StreamDistribution handles it
    Then the handler throws, no stream is saved and the gateway is not called
    And one Warning names "STREAM_INVALID_RTSP_SOURCE" and does not contain "X-Inject"

  # US2 simulator (new)
  Scenario Outline: The simulator skips an address the parser cannot read
    When the simulator handles CameraRegisteredV1 with address "<address>"
    Then Handle completes without throwing
    And one "has no path component" Information entry is logged with origin "<unparseable url>"
    And no camera-sim request is made and no camera is recorded
    Examples:
      | address                          |
      | rtsp://[1:2:3:4:5:6:7:8:/64]     |
      | rtsp://[1:2:3:4:5:6:7:8:/64]?x   |
      | rtsp://[1:2:3:4:5:6:7:8:/64]#x   |
      | rtsp://[1:2:3:4:5:6:7:8:/64]x/s  |
```

Gherkin strings use C# escapes (`\r\n`, `\t`, ` `) for the raw characters; the tests send the characters
themselves (JSON-escaped on the wire), never the backslash text.

---

## 4. Functional requirements

- **FR-001 — No control character or whitespace anywhere (item 1, write side).** A new shared predicate
  `UrlAuthority.ContainsControlOrWhiteSpace(url)` is true when any UTF-16 code unit of `url` satisfies
  `char.IsControl(c) || char.IsWhiteSpace(c)` (the 84 of §1.1). `RtspUrl.From` and `StreamSourceUrl.From` refuse it
  as their **last** rule (after spec 394's delimiter rule), so every input an older rule refuses keeps that rule's
  message. Message, identical in both value objects, a constant that never contains any part of the input:

  ```
  must not contain a control character or whitespace anywhere: a line break, tab or space is copied into the request a reader sends. Percent-encode a space in a path or query as %20
  ```

  Rendered by `Ensure` as `value: <message> (Parameter 'value')`. Format characters (Cf) are **not** in the set.
- **FR-002 — `ToOrigin` withholds the same shapes, except at the parser-trimmed edges (item 1, redaction side).**
  A second shared predicate `UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText(url)` is true when a member of
  the set occurs after the first and before the last character that is *not* a member — i.e. leading and trailing
  runs of the set are ignored. `UrlRedaction.ToOrigin` returns `UrlRedaction.Unparseable` when it is true; it is
  one more `||` after `DelimiterInsideTheBrackets`. The edge exemption is A-1. Consequences, through `ToOrigin`
  only (`AuditPayloadRedaction` is not edited): `CameraRegisteredV1.Url` and each side of `CameraAddressChangedV1`
  carrying an interior member store the placeholder; `DetailChanged` becomes `null` for a pair with such a side
  (today `true` when the two origins match), and **stays** `null` for the zoned pairs (`CarriesZone` already
  answers there).
- **FR-003 — Stored rows stay readable.** `FromStored` gains no rule and its accept set is unchanged for every
  input. No migration, no rewrite (#2990). `FromStoredCallSiteTests` / `FromStoredCallerIlTests` keep guarding the
  bypass.
- **FR-004 — Replay is bounded and visible**, exactly as spec 389 FR-006: the two StreamDistribution integration
  event handlers keep throwing on `STREAM_INVALID_RTSP_SOURCE`, no failure policy is added, the default moves the
  message to the error queue on the first attempt. **Replay note for the PR:** a `CameraRegisteredV1` or
  `CameraAddressChangedV1` published before this deploy whose address holds a member of the set dead-letters on
  its first delivery to StreamDistribution, as in specs 389/392/394; the camera row in CameraCatalog is unaffected
  and readable; the cure is to correct the address through `PATCH /cameras/{id}`.
- **FR-005 — The simulator logs and drops an address the parser cannot read (item 2).** `RtspPath.TryExtract`
  makes every `Uri` call it makes — `Uri.TryCreate` and each property read (`AbsolutePath` today; `Host` too once
  spec 406 lands) — inside one `try`, and answers false with `path` empty on `IndexOutOfRangeException`, the shape
  of `UrlRedaction.TryParse`. `CameraRegisteredSimHandler` is **not** edited: its existing false branch logs
  `SkippedNonSimulatedCamera(camera, OriginOf(url))` — the origin is the placeholder for every such shape — and
  returns, so Wolverine acknowledges the message. No new log message (A-4).
- **FR-006 — Single source.** Only `UrlAuthority.cs` declares the two predicates; `RtspUrl.cs` and
  `StreamSourceUrl.cs` call `ContainsControlOrWhiteSpace`; `UrlRedaction.cs` calls
  `ContainsControlOrWhiteSpaceWithinTheText`. A cross-row agreement test asserts that every row the new rule
  refuses that has a non-member at both ends renders the placeholder, and that the two `From`s agree row by row.
- **FR-007 — No new cross-context reference, no new state, no new error code.** All four contexts already
  reference `Shared.Kernel`; `UrlAuthority` stays a stateless static class. The message surfaces as the `detail` of
  CameraCatalog's existing 400 `CAMERA_INVALID_REQUEST` (via `BoundaryParse.TryParse`, which catches
  `ArgumentException`) and inside StreamDistribution's existing `STREAM_INVALID_RTSP_SOURCE`. NetArchTest boundary
  suite, `PrimitiveBoundaryTests` and `CameraUrlGuardedUriReadTests` pass unmodified.

## 5. Accept / reject sets (pinned by tests in both value objects)

**Accept (unchanged, characterisation):** `rtsp://10.0.5.12/h264`, `rtsp://[fe80::1%25eth0]:8554/x`,
`rtsp://cam/x%0D%0Ay`, `rtsp://cam/x%20y`, `rtsp://cam/x%09y`, `rtsp://cam/li​ve`, `rtsp://cam/li‎ve`, `rtsp://cam/li‮ve`, `rtsp://cam/li﻿ve`,
`rtsp://cam/li­ve`, `rtsp://cam/li᠎ve` (format characters, A-3), plus every spec 389/392/394 accept row
except retired rows 1-2 of §1.4.

**Reject with an older message (unchanged, characterisation):** `rtsp://a b/x`, `rtsp://10.0.\u00000.66/live`,
`rtsp://cam\u0000/live`, `rtsp://10.0.0.66:85 54/live` (userinfo, spec 001 — unparseable); ` rtsp://cam/live`,
`\trtsp://cam/live` (the `rtsp://` prefix rule); `rtsp://[fe80::1] /live`, `rtsp://[fe80::1]\u0000/live`
(text after `]`, spec 392); `rtsp://admin:4242#@cam/s ` (raw `@`, spec 389); `rtsp://[fe80::1%25 ?evil]/s`
(delimiter in brackets, spec 394); a 2049-character address ending in a space (length). Messages exactly as
observed at T010.

**Reject with the FR-001 message (new, red):** every one of the 84 characters at each of the accepting templates of
§1.2 (path, query, fragment, path end, after `@cam/`, zone end, zone middle, `rtsp://[fe80::1]/live{c}`), the 22
host-end characters at `rtsp://cam{c}`, and the 18 non-ASCII whitespace inside a plain host at
`rtsp://cam{c}/live`; plus the named rows `rtsp://10.0.0.66/live\r\nX-Inject: 1`,
`rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live`, `rtsp://[fe80::1%25eth0\r\nCSeq: 9]/s`,
`rtsp://[fe80::1]/x ` (retired row), `rtsp://cam ` (trailing U+0020), `RTSP://cam/x `.

**Hydrate via `FromStored` (characterisation, green before and after):** every FR-001 named row and one row per
template.

**`ToOrigin` (new, red):** every FR-001 row whose member is interior -> placeholder (path, query, fragment, zone,
`rtsp://@cam/li{c}ve`, and the named rows), plus retired rows 3-6 of §1.4. **`ToOrigin` (characterisation):** the
leading/trailing-only rows keep their origins (`rtsp://cam/x ` -> `rtsp://cam`, ` rtsp://cam/s` -> `rtsp://cam`,
`rtsp://10.0.0.66/live\t` -> `rtsp://10.0.0.66`, `rtsp://cam\t` -> `rtsp://cam`); percent-encoded and
format-character rows keep their origins; rows already rendering the placeholder stay so.

**`RtspPath.TryExtract` (new, red):** the four throwing rows of §1.3 and `FILE://‎/x` -> false, `path` empty,
no throw. **Characterisation:** `rtsp://camera-sim:8554/station-4-roughing` -> `station-4-roughing`,
`rtsp://cam.example/hand/made` -> `hand/made`, `rtsp://cam` -> false; and, on a tip **without** spec 406,
`rtsp://[1:2:3:4:5:6:7:8:/64]/live` -> `live` (on a tip with spec 406 that row is false — plan §3.4).

## 6. Assumptions

- **[Assumption] A-1 — `ToOrigin` ignores leading and trailing runs of the set; `From` does not.** The lane comment
  asks for "`ToOrigin` placeholder for the same shapes". Read literally (the whole string), it contradicts ~90 rows
  in nine test methods from earlier specs that pin a leading- or trailing-whitespace address as rendering its
  origin (§1.4), a decision #3031 neither mentions nor revisits. Those edges never change the rendered origin: the
  parser trims SP/TAB/CR/LF at both ends, and every other edge member either sits in the path (host unaffected) or
  makes the host unparseable or non-ASCII (placeholder already). So `ToOrigin` treats an edge run as outside the
  address, and `From` — which stores the string verbatim and hands it to MediaMTX — refuses it. The two disagree
  only on edge-only rows, and in the fail-safe direction for the write. If the owner wants the literal reading, it
  needs those nine methods retired by a decision of its own; that is not this lane's to make.
- **[Assumption] A-2 — the six retired rows of §1.4 are retired, not blocked.** Rows 1-3 are the behaviour the issue
  asks to change. Rows 4-6 (`ToOrigin` of an address with whitespace in its userinfo, which `From` refuses on the
  older userinfo rule) follow from "the same shapes" applied to interior whitespace; the new answer is the
  placeholder instead of `rtsp://cam`, which fails closed and is reachable only for addresses no write accepted.
  The test-writer moves each row into the new red file with the new expectation and deletes it from the old
  table in the same red commit; the engineer edits none of them. Flagged for the owner in the PR.
- **[Assumption] A-3 — format characters stay out.** The issue and the lane comment say "controls and whitespace";
  U+200B/U+200E/U+202E/U+FEFF/U+00AD/U+180E are Cf and stay accepted by `From` (pinned green). #3023 item 3 /
  #3031's "bidi characters in the zone" is the place to widen it.
- **[Assumption] A-4 — the simulator reuses its existing skip log line.** "Camera {Camera} at <unparseable url> has
  no path component; not a simulated camera, skipping." is not literally true for `rtsp://[...:/64]x/s`, but the
  placeholder makes the cause legible, spec 406 reuses the same line for its refusal, and a new `LoggerMessage`
  would be a second branch for one dev-tooling case.
- **[Assumption] A-5 — only `IndexOutOfRangeException` is caught in `RtspPath`**, as in `UrlRedaction.TryParse` and
  `UrlAuthority.TryReadHost`. No other exception type was observed from `Uri` (spec 394: 700,000 inputs; here the
  sweep of §1.3); catching more would hide a different defect.
- **A-6 — Accepted input narrows**, including for the management UI, whose schema does not trim the address: a
  pasted trailing space or newline now meets the server's 400 with the FR-001 detail instead of being stored. Making
  the UI trim is a frontend change out of this spec (§7).
- **A-7 — Idempotency replay across the deploy** (as spec 394 A-5): a client retrying, after the deploy, a request
  that succeeded before it with an FR-001 address gets 400, not the original 201. Accepted.
- **A-8 — #3023 item 4 changes shape as a side effect.** Its plain-host half (`rtsp://cam ` accepted while
  `rtsp://[fe80::1] /x` is refused) becomes "both refused, with different messages" (FR-001 vs text-after-`]`). The
  pinned bracket false positive is untouched. The PR comments this on #3023; the item is not closed here.

## 7. Out of scope

- #3031 items 3 (empty port), 4 (`ToOrigin`/`CarriesZone` read unguarded behind rule order), 5
  (`BracketedHostReadings` and an `@` in the userinfo), 6 (stale userinfo message for unparseable shapes — the 66
  in-host rows of §1.2 keep it), 7 (fold the three try/catch blocks), 8 (`FromStored` exception type). They stay
  open on the issue.
- Format characters (A-3); fullwidth characters in a zone; the full RFC 6874 zone character set.
- A UI-side trim or check in `cameras.schema.ts` (A-6); #3016 (`CameraDto` serves stored URLs verbatim); #2990
  (stored-row migration).
- Spec 406's authority-less refusal in `RtspPath` (#3025). This spec only guards the reads.
- The simulator's handling of an address with control characters (`rtsp://cam/live%0D%0A...` paths from a legacy
  event): it is dev tooling, nothing in #3031 asks for it, and the path it extracts is already escaped by `Uri`.
- Running MediaMTX on these inputs; its refusal is reasoned from Go's `net/url`, not observed.

## 8. End-to-end test procedure (phase 5)

On a booted Aspire stack (one per machine; stop it before building; check the AppHost PID's start time is after the
last commit):

1. `POST /cameras` (operator token, fab `munich`) with `{"name":"crlf","rtspUrl":"rtsp://10.0.0.66/live\r\nX-Inject: 1"}`
   (JSON `\r\n`) -> 400 `CAMERA_INVALID_REQUEST`, `detail` the rendered FR-001 text, body free of `X-Inject`.
2. Same with a trailing space (`"rtsp://10.0.0.66/live "`) -> 400, FR-001 text. With `"rtsp://a b/x"` -> 400, the
   userinfo text (older message wins).
3. Register `rtsp://10.0.0.66/live`, then `PATCH` it to `rtsp://10.0.0.66/live\tx` -> 400; `GET` shows the address
   unchanged.
4. Insert a `cameras` row with `rtsp://10.0.0.66/live\r\nX-Inject: 1` directly (psql) -> `GET /cameras` and
   `GET /cameras/{id}` answer 200.
5. Read the audit row of step 3's registration: `Url` `rtsp://10.0.0.66`. (A refused write produces no audit row.)
6. Simulator: with the simulator running, publish a `CameraRegisteredV1` whose `Url` is
   `rtsp://[1:2:3:4:5:6:7:8:/64]` to its queue (RabbitMQ management UI) -> one "has no path component" log line with
   `<unparseable url>`, the message acknowledged, nothing in `scenario-simulator.*` error queues.

Latency: N/A (header).
