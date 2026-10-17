# Spec 392: A camera URL whose written authority is longer, or shorter, than the host the parser read is refused on write and redacted on read

**Feature Branch**: `fix/3012-authority-longer-or-shorter-than-host` (cut from `origin/develop` at `de9802b1`)

**Created**: 2026-10-17

**Status**: Phases 1-3 written (Specify, Plan, Tasks); phase 4 implementation done (T025, T040-T043) and amended by the fixup round for the adversary and review findings (FR-011 to FR-013, §7.1). No `[NEEDS CLARIFICATION]` remains: the stored-row
question was decided on #3012 (lane decision comment, "owner may overrule", applied to #3017 by its own
comment), and the shapes are the issues' own. Three places where the issues leave a boundary open are
resolved and recorded as **[Assumption]** in §6 (A-1 to A-3).

**Input**: Issues [#3012](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3012) (*text after the
closing `]` of an IPv6 host is dropped from the origin and DetailChanged reads true*) and
[#3017](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3017) (*RtspUrl/StreamSourceUrl accept an
empty authority followed by credentials*). Both found by test-adversary passes (spec 387 and spec 389). One spec,
as both issues propose: "the authority is longer, or shorter, than the host that was read".

**Lane.** Autonomous (ADR-0144). Two kinds of change, kept in separate commits (plan §6):

- **Behaviour-preserving** — US3, `UrlAuthority` becomes the only place the authority locator and the
  terminator set are written; `AuditPayloadRedaction` calls it. Characterisation captured green first, unmodified
  after. The structural guard (`UrlAuthoritySingleSourceTests`) is tightened first and is red until the move lands.
- **Behaviour-changing** — US1 and US2, the accepted input of two value objects narrows and `ToOrigin` renders
  the placeholder for one more shape. Phase 4a **red**.

**Security-sensitive** (camera credentials stored, published on the bus and returned by the API; an audit claim of
"same origin" over text the origin dropped): phase 4 includes a `test-adversary` pass, phase 6 includes
`security-reviewer`.

**Spec number.** 392. Checked 2026-10-17 after `git fetch origin`: `origin/develop` highest 391; `git ls-tree
<ref> specs/` over every local and remote head finds nothing above 391; open PRs (#2982, #2981, #2979, #2978,
#2970, #2811) are Dependabot, an ADR-0084 doc and a stale Claude branch, none adding a spec; the two worktrees
(`smart-sentinel-eye`, `sse-3012`) hold nothing above 391. Re-check before the PR is opened.

**ADRs.** ADR-0038 / ADR-0046 / ADR-0066 (hand-written value objects, `From` + `Ensure.That`), ADR-0105
(`Ensure.That`; a message never carries the value), ADR-0047 / ADR-0089 (existing `ApiError` codes, unchanged),
ADR-0042 / ADR-0088 (Wolverine default failure policy, unchanged), ADR-0139 / ADR-0144 (phase 4a colours; the lane
implements, it does not decide), ADR-0065 (Domain >= 90%, Application >= 80%, Shared >= 90%), ADR-0084 (metrics,
advisory), ADR-0036 (smallest change). Constitution §II (no primitive state added) and §III (shared code lives in
`Shared.Kernel`, which all three contexts already reference). Prior art: spec 389 (`FromStored`, the raw-`@`
rule, `UrlAuthority`), spec 387 (`BracketContentIsAddressOnly`), spec 384 (the `null` `DetailChanged`).

**No new ADR.** Every mechanism is reused: `FromStored` (spec 389, after `BearerTokenHash.FromStored`), a
`Satisfies` rule with its own message (spec 389 FR-002/003), the placeholder and the `null` `DetailChanged`
(specs 377/384), a source-scanning single-source guard (spec 389 T050). No contract, scope, persistence shape,
Wolverine policy or migration changes. The stored-row direction was decided on #3012.

**Latency budget.** N/A. Registration, address change and re-point are control-plane writes, and the audit
redaction runs on the audit consumer; none is on a §IV leg. The added work is two ordinal scans of a <= 2048
character string per write and one per audited address.

---

## 1. What was found (premises verified by experiment on `de9802b1`, not assumed)

A scratch probe (file-based app in the session scratchpad referencing `CameraCatalog.Domain`,
`StreamDistribution.Domain` and `AuditObservability.Application`; not committed) called `Uri.TryCreate`,
`UrlAuthority.AtSignFollowsTheAuthority`, `UrlRedaction.ToOrigin`, both `From`s, both `FromStored`s and
`AuditPayloadRedaction.Serialize(CameraAddressChangedV1)`.

### 1.1 #3012 — text after the closing `]` is read as the path, and the audit claims "same origin"

| Input | `Uri` host / path | `ToOrigin` | `From` (both) | `FromStored` (both) |
|---|---|---|---|---|
| `rtsp://[fe80::1]%25eth0/x` | `[fe80::1]` / `/%25eth0/x` | `rtsp://[fe80::1]` | **accepted** | accepted |
| `rtsp://[fe80::1]%eth0/x` | `[fe80::1]` / `/%25eth0/x` | `rtsp://[fe80::1]` | **accepted** | accepted |
| `rtsp://[fe80::1]garbage.example/x` | `[fe80::1]` / `/garbage.example/x` | `rtsp://[fe80::1]` | **accepted** | accepted |
| `rtsp://[fe80::1]x:554/s` | `[fe80::1]`, port default / `/x:554/s` | `rtsp://[fe80::1]` (the written port is dropped too) | **accepted** | accepted |
| `rtsp://[fe80::1]]/x` | `[fe80::1]` / `/]/x` | `rtsp://[fe80::1]` | **accepted** | accepted |
| `rtsp://[::ffff:10.0.0.1]x/s`, `rtsp://[fe80::1/64]x/s`, `rtsp://[fe80::1]%20/s`, `RTSP://[FE80::1]GARBAGE/x` | bracket host / tail in path | origin rendered | **accepted** | accepted |
| `rtsp:\\[fe80::1]garbage/x` | `[fe80::1]` / `/garbage/x` | `rtsp://[fe80::1]` | refused (scheme) | refused (scheme) |
| `rtsp://[fe80::1]:554x/s`, `rtsp://[fe80::1]\x`, `rtsp://[fe80::1]:65536/s` | no parse | placeholder | refused (userinfo message) | refused |

`AuditPayloadRedaction.Serialize` stores `PreviousUrl` = `Url` = `rtsp://[fe80::1]` and **`"DetailChanged":true`**
for the issue's three pairs, and for `rtsp://[fe80::1]x:554/s` -> `rtsp://[fe80::1]/s` and
`rtsp://[fe80::1]]/x` -> `rtsp://[fe80::1]/x`. The issue's premise holds exactly.

Shapes that must stay as they are (accepted, origin rendered): `rtsp://[fe80::1]:554/s`, `rtsp://[fe80::1]:0554/s`
(origin `:554`), `rtsp://[fe80::1]:/s` (empty port, origin without port), `rtsp://[fe80::1]?q`,
`rtsp://[fe80::1]#f`, `rtsp://[fe80::1]`, `rtsp://[fe80::1%25eth0]/x` and `rtsp://[fe80::1/64%25eth0]/s` (zone
inside the brackets, spec 387: `DetailChanged` null). With userinfo, `rtsp://user:pass@[fd00::5]:8554/x` and
`rtsp://[]@[fe80::1/64%25eth0]/s` keep their origins `rtsp://[fd00::5]:8554` and `rtsp://[fe80::1]` (refused at
registration on the userinfo rule, as today).

Not run, as in the issue: whether MediaMTX (Go `net/url`) rejects these as an invalid port. It lowers
exploitability (such a camera probably never streams); it does not change the audit's false claim.

### 1.2 #3017 — an empty host is accepted, and the two checks each assume the other covers it

| Input | `Uri` host / userinfo / path | predicate | `ToOrigin` | `From` (both) | `FromStored` (both) |
|---|---|---|---|---|---|
| `rtsp://` | `''` / `''` / `/` | false | placeholder | **accepted** | accepted |
| `rtsp:///` | `''` / `''` / `/` | false | placeholder | **accepted** | accepted |
| `rtsp:///cam`, `RTSP:///cam` | `''` / `''` / `/cam` | false | placeholder | **accepted** | accepted |
| `rtsp:///admin:4242@cam/s` | `''` / `''` / `/admin:4242@cam/s` | **false** | placeholder | **accepted** | accepted |
| `rtsp:////admin:4242@cam/s` | `''` / `''` / `//admin:4242@cam/s` | **false** | placeholder | **accepted** | accepted |
| `rtsp://?x`, `rtsp://#x`, `rtsp:///?x` | `''` / `''` / `/` | false | placeholder | **accepted** | accepted |
| `rtsp:///admin:4242#@cam` | `''` | true | placeholder | refused (raw-`@` message, spec 389) | accepted |
| `rtsp://:554/cam`, `rtsp:// /cam` | no parse | false | placeholder | refused (userinfo message) | refused |

The mechanism is as the issue states: `UrlAuthority.AtSignFollowsTheAuthority` skips the whole `///` run and reads
`admin:4242@cam` as the authority, so the `@` is userinfo to it and it answers false; the BCL parser reads an empty
host with everything in the path, so `UserInfo` is empty and `HasNoUserInfo` passes. `ToOrigin` already renders
the placeholder for every empty-host row — only because `uri.Host` is empty — so `DetailChanged` is already `null`
for them (pinned today by `AuditPayloadDetailChangedAdversarialTests` row `rtsp:///s`). The defect for #3017 is
the write side only.

The predicate's comment ("The authority is located the way the parser does … past any run of / and \\") is wrong
for a hierarchical scheme such as `rtsp:`: there a third `/` ends an empty authority. It is right for `file:` and
for UNC paths (`file:////host`, `\\[fe80::1]\x` both parse with the host after the run). Changing the predicate
would change `ToOrigin` for those schemes and edit pinned rows
(`UrlAuthorityTerminatorTableTests.An_at_sign_before_the_first_terminator_is_userinfo_to_the_predicate`
`"rtsp:///admin:1@cam/s"`, `An_address_with_no_authority_text_is_not_flagged` `"rtsp://"`, `"rtsp:///"`), so it is
**not** changed; the empty-host rule refuses the shape independently and the comment is corrected (FR-007).

Backslash / UNC handling, verified: `rtsp:\\[fe80::1]/x` and `\\[fe80::1]\x` parse (as `rtsp` and `file`), so the
locator's skipping of `\` matches the parser for those; both are refused at registration by the `rtsp://` prefix
rule. `rtsp://\cam/s` and `rtsp://\admin:1@cam/s` do not parse and are refused on the userinfo rule, as today.

### 1.3 The audit copy of the locator — what differs, and whether it can matter

`AuditPayloadRedaction.BracketContentIsAddressOnly` (`AuditPayloadRedaction.cs:158-194`) writes its own locator
(`:168`, `IndexOfAnyExcept('/', '\\')`) and its own terminator literal (`:176`, `"/?#\\"`).
`UrlAuthoritySingleSourceTests` records it by name as the one permitted copy. It differs from
`UrlAuthority.AtSignFollowsTheAuthority` in two ways:

1. **It trims** (`url.AsSpan().Trim()`). Observable: ` rtsp://[fe80::1]/x ` -> `rtsp://[fe80::1]/y` stores
   `DetailChanged` **true**; without the trim the host would not start with `[` and it would be `null`. The trim
   stays at the audit call site.
2. **It does not check the scheme characters** before stripping up to the first `:`. Unreachable: the method runs
   only after `Uri.TryCreate` succeeded with an IPv6 host (`CarriesZone`), and `Uri` refuses a scheme holding a
   non-scheme character (`r_t://`, `r tsp://`, `rt\tsp://`, `r~t://` all fail to parse). A 2,000,000-input seeded
   fuzz over scheme, separator, bracket, whitespace and control pieces found 45,105 inputs reaching the method and
   **0** on which the two locators (audit trim applied to both) return different text.

So routing the audit method through the shared locator, with the trim kept at its call site, is
behaviour-preserving. It fixes no bug; it is a refactor and is delivered as one (plan §6).

### 1.4 Writers, readers and the UI

Unchanged since spec 389 §1.3 (`grep -rn "RtspUrl\.From(\|StreamSourceUrl\.From(" src` at `de9802b1`): writers
W1 `RegisterCameraRequest.cs:19`, W2 `CameraEndpoints.cs:312`, W3 `ProvisionStreamCommandHandler.cs:33`, W4
`RepointStreamCommandHandler.cs:126`; readers are the two EF converters, which already call `FromStored`.

The management UI (`apps/shared/src/api/cameras.schema.ts`) already refuses `rtsp:///admin:4242@cam/s` (its
`^rtsp:\/\/[^@\s]+@` matches `/admin:4242@`) but **accepts** `rtsp://`, `rtsp:///cam` and every bracket-tail
form. After this spec the API refuses them with 400 and the UI shows the server's message; no UI change is made
(the zod schema mirrors spec 001 FR-008 only).

---

## 2. User stories

### US1 (P1) — An address whose host is empty is refused where it is written

As an **integrator or operator registering or correcting a camera through the API**, I want an `rtsp://` address
that names no host (`rtsp://`, `rtsp:///cam`, `rtsp:///admin:4242@cam/s`) to be refused with a message that says
what is wrong, so that a password written after a third `/` is never stored, published on the bus or returned by
the API.

**Why P1:** #3017 — credentials pass both checks today and are stored in clear.

**Independent test:** `RtspUrl.From` and `StreamSourceUrl.From` throw for every empty-host row of §5 with the
FR-002 message and nothing of the address; `POST /cameras` with `rtsp:///admin:4242@cam/s` answers 400
`CAMERA_INVALID_REQUEST` with that message and a body free of `4242`.

### US2 (P1) — Text after the closing `]` is refused on write and never claimed as "same origin"

As an **auditor reading a camera's address history**, I want an address change whose written authority holds
text after the IPv6 host's `]` (other than `:port`) to record the placeholder for that side and `DetailChanged`
unknown (`null`), and as an **operator**, I want such an address refused at registration, so that the audit never
says "same origin, only the detail changed" about text the origin silently dropped.

**Why P1:** #3012 — the audit makes a false claim today, and the shapes are accepted on write.

**Independent test:** the issue's three pairs stored through `AuditPayloadRedaction.Serialize` carry
`PreviousUrl` = `Url` = `<unparseable url>` and `"DetailChanged":null` in both directions; `RtspUrl.From` /
`StreamSourceUrl.From` refuse every bracket-tail row of §5 with the FR-003 message; `POST /cameras` with
`rtsp://[fe80::1]%25eth0/x` answers 400 `CAMERA_INVALID_REQUEST`.

### US2a (P1, same slice) — A row stored before this change stays readable

The decision on #3012/#3017: rows already stored are read through `FromStored`, with no migration. `FromStored`
does not gain either new rule, so `GET /cameras`, `GET /cameras/{id}` and the stream reconciler keep working for
a camera stored at `rtsp:///cam` or `rtsp://[fe80::1]%25eth0/x`. Not a story of its own because nothing changes:
it is characterised (green before and after), and it is what makes US1/US2 shippable.

### US3 (P2) — One place locates the authority

As a **maintainer**, I want the authority locator and the terminator set written once, in `UrlAuthority`, and
called by `AuditPayloadRedaction`, so that a fix to how the authority is found (this spec is the third in a row)
cannot reach two of three readers.

**Why P2:** no observable behaviour changes; it removes the recorded copy that #3017 names.

**Independent test:** `UrlAuthoritySingleSourceTests` finds the locator and the terminator set only in
`src/Shared.Kernel/UrlAuthority.cs`, and finds `AuditPayloadRedaction.cs` calling the shared locator; every
`AuditPayload*` suite passes unmodified.

---

## 3. Acceptance scenarios (Gherkin)

```gherkin
Feature: A camera URL's written authority is the host that was read

  Background:
    Given an operator token with scope "sse.cameras.write" and membership in fab "munich"

  # happy path (unchanged)
  Scenario Outline: An ordinary or bracketed address is still accepted
    When the operator registers a camera with address "<address>"
    Then the response is 201 Created
    Examples:
      | address                       |
      | rtsp://cam:554/path?x=y       |
      | rtsp://[fe80::1]:554/s        |
      | rtsp://[fe80::1%25eth0]/s     |
      | rtsp://[fe80::1]?q            |

  # US1 bad request (new)
  Scenario Outline: An empty host is refused, and nothing written after it is echoed
    When the operator registers a camera with address "<address>"
    Then the response is 400 with title "CAMERA_INVALID_REQUEST" and the detail is the rendered FR-002 text
    And the response body does not contain "<secret>"
    And no camera is stored and no CameraRegisteredV1 is published
    Examples:
      | address                     | secret |
      | rtsp:///admin:4242@cam/s    | 4242   |
      | rtsp:////admin:4242@cam/s   | 4242   |
      | rtsp:///cam                 | /cam   |
      | rtsp://                     | n/a    |

  # US2 bad request (new)
  Scenario Outline: Text after the closing bracket is refused
    When the operator registers a camera with address "<address>"
    Then the response is 400 with title "CAMERA_INVALID_REQUEST" and the detail is the rendered FR-003 text
    And the response body does not contain "<secret>"
    Examples:
      | address                            | secret           |
      | rtsp://[fe80::1]%25eth0/x          | eth0             |
      | rtsp://[fe80::1]garbage.example/x  | garbage.example  |

  # conflict ordering (unchanged): the address is parsed before the version is checked
  Scenario: A refused address on PATCH answers 400, not 412, and the camera is unchanged
    Given a camera "line-3-inlet" at "rtsp://10.0.5.12/h264" at version 2
    When the operator patches its address to "rtsp:///admin:4242@cam/s" with If-Match version 1
    Then the response is 400 "CAMERA_INVALID_REQUEST"
    And the camera's address is still "rtsp://10.0.5.12/h264" at version 2

  # the older messages win where an older rule already refuses
  Scenario: A raw @ after an empty host keeps the spec 389 message
    When the operator registers a camera with address "rtsp:///admin:4242#@cam"
    Then the response is 400 and the detail is the spec 389 raw-@ text

  # auth (unchanged)
  Scenario: A caller without the write scope is refused before the address is examined
    Given a token without scope "sse.cameras.write"
    When it registers a camera with address "rtsp:///admin:4242@cam/s"
    Then the response is 403 and the body contains neither FR-002 text nor "4242"

  # US2 audit (new)
  Scenario Outline: An address change over a bracket tail records unknown, not "same origin"
    When CameraAddressChangedV1 from "<previous>" to "<url>" is audited
    Then the stored PreviousUrl and Url are "<unparseable url>" for each side with a tail
    And DetailChanged is null, in both directions
    Examples:
      | previous                           | url                         |
      | rtsp://[fe80::1]%25eth0/x          | rtsp://[fe80::1]%25eth1/x   |
      | rtsp://[fe80::1]%eth0/x            | rtsp://[fe80::1]/x          |
      | rtsp://[fe80::1]garbage.example/x  | rtsp://[fe80::1]/x          |

  Scenario: An address change between two plain bracket hosts keeps its existing answer
    When CameraAddressChangedV1 from "rtsp://[fe80::1]:554/x" to "rtsp://[fe80::1]:554/y" is audited
    Then DetailChanged is true

  # US2a stored rows (characterisation)
  Scenario: A camera stored before this change with a now-refused address is still read
    Given a cameras row whose rtsp_url is "rtsp:///admin:4242@cam/s", inserted directly
    When the operator lists the fab's cameras and reads that camera
    Then both answer 200 and include the camera
    # The value is returned as stored; masking it in CameraDto is #3016, out of scope.

  # replay (new outcome for these shapes; mechanism of spec 389 unchanged)
  Scenario: A pre-deploy CameraRegisteredV1 with an empty host is dead-lettered, not provisioned
    Given a CameraRegisteredV1 for a new camera with address "rtsp:///admin:4242@cam/s"
    When StreamDistribution handles it
    Then the handler throws, no stream is saved and the gateway is not called
    And one Warning names "STREAM_INVALID_RTSP_SOURCE" and does not contain "4242"
```

---

## 4. Functional requirements

- **FR-001 — Order and scope of the two new rules.** `RtspUrl.From` and `StreamSourceUrl.From` add two
  `Satisfies` checks **after** the existing raw-`@` check, in this order: the empty-host rule (FR-002), then the
  bracket-tail rule (FR-003). Every input refused today keeps exactly today's message; the reject set grows only by
  inputs every earlier check passes. `FromStored` gains neither (FR-005).
- **FR-002 — Empty host.** Refused when `Uri.TryCreate(url, Absolute)` succeeds and `uri.Host` is empty (an
  unparseable URL never reaches this check: the userinfo rule already refuses it). Message, identical in both value
  objects:

  ```
  must name a host directly after 'rtsp://': a further '/', or nothing at all, leaves the host empty and the rest is read as a path
  ```
- **FR-003 — Text after the closing bracket.** Refused when the new shared predicate
  `UrlAuthority.TextFollowsTheClosingBracket(url)` is true (plan §3.1 defines it on the written text). Message,
  identical in both value objects:

  ```
  must not have text after the closing ']' of an IPv6 address other than ':' and a port: the parser drops it from the host. Write a zone inside the brackets, percent-encoded as %25
  ```

  Both FR-002 and FR-003 are rendered by `Ensure` as `value: <message> (Parameter 'value')`, contain no part of the
  input, and surface as the `detail` of CameraCatalog's existing 400 `CAMERA_INVALID_REQUEST` and inside
  StreamDistribution's existing `STREAM_INVALID_RTSP_SOURCE`. No new error code.
- **FR-004 — `ToOrigin` withholds a bracket tail.** `UrlRedaction.ToOrigin` returns `UrlRedaction.Unparseable`
  when `UrlAuthority.TextFollowsTheClosingBracket(url)` is true. Consequently `AuditPayloadRedaction` stores the
  placeholder for that side and `DetailChanged` `null` (the existing `IsPlaceholder` arm; no change to
  `AuditPayloadRedaction`'s rule). An empty host already yields the placeholder; that is characterised, not changed.
- **FR-005 — Stored rows stay readable.** `RtspUrl.FromStored` and `StreamSourceUrl.FromStored` are unchanged and
  keep accepting every FR-002/FR-003 row; the EF converters already call them. No migration, no rewrite of a stored
  value (rewriting is #2990). `FromStoredCallSiteTests` / `FromStoredCallerIlTests` keep guarding the bypass.
- **FR-006 — Replay is bounded and visible**, exactly as spec 389 FR-006: the two StreamDistribution integration
  event handlers keep throwing on `STREAM_INVALID_RTSP_SOURCE`, no failure policy is added, the default moves the
  message to the error queue on the first attempt.
- **FR-007 — The predicate's comment is corrected, not its behaviour.** The `UrlAuthority.AtSignFollowsTheAuthority`
  comment stops claiming the authority is "located the way the parser does" for every scheme: it states that the
  skip of a `/` and `\` run models `file:` and UNC parsing, over-reaches for hierarchical schemes such as `rtsp:`
  (where a third `/` leaves the authority empty), and that the empty-host rule (value objects) and the empty-host
  check (`ToOrigin`) cover that shape. Its behaviour, and every `UrlAuthority*Tests` row, is unchanged.
- **FR-008 — One locator, one terminator set (US3).** `UrlAuthority` exposes the locator and the terminator search
  as public members (plan §3.1 names them). `AtSignFollowsTheAuthority`, `TextFollowsTheClosingBracket` and
  `AuditPayloadRedaction.BracketContentIsAddressOnly` all use them; the `IndexOfAnyExcept('/', '\\')` locator and the
  `"/?#\\"` literal appear as code only in `src/Shared.Kernel/UrlAuthority.cs`. The audit method keeps its own
  `Trim()` at the call site (§1.3). Behaviour identical for every input reaching it.
- **FR-009 — The three callers cannot fall out of step.** `UrlAuthoritySingleSourceTests` requires
  `RtspUrl.cs`, `StreamSourceUrl.cs` and `UrlRedaction.cs` each to call `UrlAuthority.TextFollowsTheClosingBracket`
  (as it already requires for `AtSignFollowsTheAuthority`), only `UrlAuthority.cs` to declare it, and
  `AuditPayloadRedaction.cs` to call the shared locator. A cross-row agreement test asserts that whatever `From`
  refuses on FR-002 or FR-003, `ToOrigin` renders as the placeholder, and that nothing `From` accepts is flagged by
  the new predicate.
- **FR-010 — No new cross-context reference, no new state.** All three contexts already reference
  `Shared.Kernel`. `UrlAuthority` stays a stateless static class; NetArchTest boundary suite and
  `PrimitiveBoundaryTests` pass unmodified.
- **FR-011 — Every userinfo reading is tried for a bracket tail (fixup, adversary F1).** The parser ends the
  userinfo at the first `@`, other parsers at the last, and an `@` after the closing `]` is moved into the path
  (`rtsp://@[fe80::1]admin:4242@cam/s` reads host `[fe80::1]`). `TextFollowsTheClosingBracket` therefore reads the
  host from after the first `@` and from after the last, or from the authority's start when there is no `@`; a
  reading with nothing, or only whitespace or control characters, after its `@` falls back to the authority's start
  (`rtsp://[fe80::1]x@` keeps the bracket). It is true when any reading has a tail. Leading whitespace is skipped
  as `Uri.TryCreate` skips it, so a prefix cannot hide the tail from the shared predicate (`ToOrigin` and
  `DetailChanged`). `AtSignFollowsTheAuthority` is deliberately not trimmed: `" rtsp://user:pass@cam/x"` renders
  the placeholder today and a test pins it.
- **FR-012 — A `/` inside the brackets must follow a whole IPv6 address (fixup, adversary F2).** The parser reads
  the address before the `/` as a prefix-length introducer (`[fe80::1/64]`). `UrlAuthority.SlashInsideTheBracketsFollowsNoAddress`
  is true when the bracket text holds a `/` and the text before the first `/` or `%` is not an IPv6 address on its
  own (`[fe80/::1]` is read as `[fe80::]`, `[0/::1]` as `[::]`). Same readings as FR-011. `RtspUrl.From` and
  `StreamSourceUrl.From` refuse it with a third message (no part of the input), after the FR-003 check; `ToOrigin`
  renders the placeholder. `[fe80::1/64]`, `[fe80::1/64%25eth0]` and `[fe80::1%25eth0/64]` behave as before.
- **FR-013 — A host of only blank characters names no host (fixup, adversary F3).** The FR-002 rule refuses a host
  that is empty or made only of whitespace, control and format characters (`UrlAuthority.IsBlankHost`: U+00A0,
  U+200B, U+3000, U+FEFF, U+2028, U+00AD and the like), which the parser keeps as a host. Message unchanged.

## 5. Accept / reject sets (pinned by tests in both value objects)

**Accept (unchanged, characterisation):** `rtsp://10.0.5.12/h264`, `rtsp://cam:554/path?x=y`, `RTSP://CAM/s`,
`rtsp://cam/s`, `rtsp://@cam/s` (empty userinfo, spec 389 §7.1), `rtsp://[::1]:554/s`, `rtsp://[fe80::1]/s`,
`rtsp://[fe80::1]`, `rtsp://[fe80::1]:554/s`, `rtsp://[fe80::1]:0554/s`, `rtsp://[fe80::1]:/s`,
`rtsp://[fe80::1]?q`, `rtsp://[fe80::1]#f`, `rtsp://[fe80::1%25eth0]/x`, `rtsp://[fe80::1/64%25eth0]/s`,
`rtsp://[::ffff:10.0.0.1]/x`.

The accept set above also lists `rtsp://cam/s`, which the `CameraUrlAuthorityHostRefusalTests` corpus (Architecture.Tests) does not carry; it is pinned in `RtspUrlAuthorityHostCharacterisationTests` and its StreamDistribution twin instead.

**Reject with an older message (unchanged, characterisation):** `rtsp:///admin:4242#@cam`, `rtsp:////admin:4242#@cam`
(raw-`@`, spec 389); `rtsp://[fe80::1]:554x/s`, `rtsp://[fe80::1]\x`, `rtsp://:554/cam`, `rtsp://u@[fe80::1]x/s`,
`rtsp://[fe80::1]evil@cam/x` (userinfo); `rtsp:\\[fe80::1]garbage/x` (scheme).

**Reject with the FR-002 message (new, red):** `rtsp://`, `rtsp:///`, `rtsp:///cam`, `RTSP:///cam`,
`rtsp:///admin:4242@cam/s`, `rtsp:////admin:4242@cam/s`, `rtsp://?x`, `rtsp://#x`, `rtsp:///?x`.

**Reject with the FR-003 message (new, red):** `rtsp://[fe80::1]%25eth0/x`, `rtsp://[fe80::1]%eth0/x`,
`rtsp://[fe80::1]garbage.example/x`, `rtsp://[fe80::1]garbage.example`, `rtsp://[fe80::1]x:554/s`,
`rtsp://[fe80::1]]/x`, `RTSP://[FE80::1]GARBAGE/x`, `rtsp://[::ffff:10.0.0.1]x/s`, `rtsp://[fe80::1/64]x/s`,
`rtsp://[fe80::1]%20/s`.

**Hydrate via `FromStored` (characterisation, green before and after):** every FR-002 and FR-003 row above, plus
the accept set.

**`ToOrigin` (new, red):** every FR-003 row -> placeholder; also `rtsp:\\[fe80::1]garbage/x` and
`http://[fe80::1]garbage/x` -> placeholder (today rendered). **`ToOrigin` (characterisation):** every FR-002 row
-> placeholder; the accept set's bracket rows keep their origins; `rtsp://user:pass@[fd00::5]:8554/x` ->
`rtsp://[fd00::5]:8554`; `rtsp://[]@[fe80::1/64%25eth0]/s` -> `rtsp://[fe80::1]`; `\\[fe80::1]\x` and
`file:////[fe80::1]/x` -> `file://[fe80::1]`.

## 6. Assumptions

- **[Assumption] A-1 — an empty port after `]` stays accepted.** `rtsp://[fe80::1]:/s` is accepted today and its
  origin is `rtsp://[fe80::1]`. RFC 3986 defines `port = *DIGIT`, the parser reads the default port, and nothing
  written is dropped except the `:` itself, so FR-003 allows `:` followed by zero or more ASCII digits. Refusing it
  would be a widening the issues did not ask for.
- **[Assumption] A-2 — the bracket rule is scoped to bracketed hosts.** "Authority longer than the host" also
  describes other shapes (`#2989`/`#2993` handled the host charset; spec 389 the raw `@`). FR-003 inspects only a
  host that starts with `[`; a registered-name or IPv4 host is unaffected. Recorded so a reviewer does not read the
  spec title as a general rule.
- **[Assumption] A-3 — the empty-host rule reads the parsed host, not the written text.** Using `uri.Host` refuses
  exactly what the parser would address as nothing; a written-text rule would need the scheme-dependent separator
  logic that FR-007 records as the predicate's known over-reach. The two value objects already parse with `Uri` for
  the userinfo rule, so this adds no new parser dependency.
- **A-4 — Idempotency replay across the deploy** (as spec 389 A-3): a client retrying, after the deploy, a request
  that succeeded before it with a now-refused address gets 400, not the original 201. Accepted.
- **A-5 — Accepted input narrows.** Every FR-002 and FR-003 row was accepted by `POST /cameras`, `PATCH
  /cameras/{id}`, provisioning and re-pointing before this spec. That is the decided change (#3012 decision).
  The management UI accepts most of them client-side and will now show the server's 400 (§1.4).

## 7. Out of scope

- U+FF20 fullwidth `@` (spec 389 §7.1; no parser treats it as a delimiter).
- #3016 — `CameraDto` / the management UI serve stored URLs verbatim, including rows this spec now refuses.
- #2990 — migrating, flagging or rewriting stored rows.
- Changing `UrlAuthority.AtSignFollowsTheAuthority`'s behaviour for `rtsp:///…` (FR-007), or unifying it with
  `UrlRedaction.AuthorityWasWrittenAsAscii`'s strict `://` form (the existing comment's "do not unify" holds).
- Text after a non-bracketed host (A-2); a zone written after `]` being *supported* (it is refused, with the fix
  "write it inside the brackets").
- UI validation in `cameras.schema.ts`.

## 7.1 Accepted / recorded

- The bracket rule is a scan of the written text; it agrees with the parser on every row of §1.1, and where it
  over-flags (a tail the parser would also refuse), `From` refuses on an earlier rule and `ToOrigin` renders the
  placeholder either way — fail closed.
- `ToOrigin` now renders the placeholder for `rtsp://u@[fe80::1]x/s`-style inputs (userinfo plus bracket tail),
  which no registration accepts; no test pins today's rendering of that shape.
- MediaMTX's own parse of these shapes was not observed (§1.1).
- **Residuals of the fixup round (accepted, recorded).**
  - Invisible characters inside a plain DNS host (a U+200B between `ca` and `m`) are accepted: FR-013 refuses only a host
    made of nothing else.
  - Trailing whitespace after a plain host (`rtsp://cam/x `) is accepted; the parser trims it.
  - A bracketed address with only trailing whitespace (`rtsp://[fe80::1] /x`) is refused with the FR-003 message.
    That is a false positive (the parser trims it), pinned green by `RtspUrlAuthorityHostAdversarialTests`.
  - A zone containing `?` or `#` (`rtsp://[fe80::1%25a?b]/x`) is accepted; the scan reads the bracket text, not the
    zone's content.
  - The bracket scan is written twice: `UrlAuthority.BracketedHostReadings` and
    `AuditPayloadRedaction.CarriesZone` / `BracketContentIsAddressOnly` (which still ends the userinfo at the last
    `@`). Accepted for this PR; the audit copy runs only after `ToOrigin` has accepted the address, so a tail the
    shared predicate refuses never reaches it.
  - `rtsp://./` is accepted: a host of one dot is not blank.

## 8. End-to-end test procedure (phase 5)

On a booted Aspire stack (one per machine; stop it before building; check the AppHost PID's start time is after
the last commit):

1. Mint a write token from Aspire's proxied Keycloak endpoint for an operator in `munich`.
2. `POST /cameras?fabId=munich` with `rtspUrl` `rtsp:///admin:4242@cam/s` -> 400 `CAMERA_INVALID_REQUEST`, detail
   the rendered FR-002 text, body free of `4242`. Repeat with `rtsp://[fe80::1]%25eth0/x` -> 400, FR-003 text.
3. `POST` `rtsp://camera-sim:8554/station-4-roughing` -> 201; `PATCH` its address to `rtsp:///cam` with the ETag ->
   400; `GET` shows the original address and version.
4. Insert a row directly (`psql`, camera-catalog database) with `rtsp_url = 'rtsp:///admin:4242@cam/s'` and one with
   `'rtsp://[fe80::1]%25eth0/x'`; `GET /cameras?fabId=munich` and `GET /cameras/{id}` -> 200 for both.
5. `PATCH` a valid camera from `rtsp://[fe80::1]/x` to `rtsp://[fe80::1]/y` -> 204; read its audit row: origin
   `rtsp://[fe80::1]` both sides, `DetailChanged` true (unchanged). The bracket-tail audit pair cannot be produced
   through the API any more; it is covered by the handler-level tests (plan §5.2 R4).
6. Record status codes and bodies on the PR. Latency: N/A.
