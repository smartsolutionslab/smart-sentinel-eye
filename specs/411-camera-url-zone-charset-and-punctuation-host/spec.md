# Spec 411: A camera URL's IPv6 zone holds only printable ASCII, a host of only dots names nothing, and the audit's bracket scan folds onto `UrlAuthority`

**Feature Branch**: `fix/3023-camera-url-authority-residuals` (cut from `origin/develop` at `00a66722`) for PR 1;
`refactor/3023-bracket-scan-fold`, cut from `develop` after PR 1 merges, for PR 2.

**Created**: 2026-10-18

**Status**: Phases 1-3 written (Specify, Plan, Tasks). No `[NEEDS CLARIFICATION]` is left in the lane scope. Three
questions are **excluded from the lane and put to the owner** (§9, Q1-Q3), because answering them narrows what
registration accepts in a way #3023 itself calls a separate decision. The places where this spec reads the issue
are marked **[Assumption]** in §7 and flagged for the owner in each PR.

**Input**: Issue [#3023](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3023), the owner's enrolment
comment of 2026-10-18 11:29 (items 3, 4, 6 and 7, plus two rows from the #3031 security review), and the residuals
the security review of PR #3047 recorded: (a) `rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live`, a
percent-encoded control character in an IPv6 zone, is accepted; (b) invisible characters are accepted, both Cf
(U+200E, U+202E, U+FEFF) and not Cf (U+3164, U+115F, U+1160, U+FFA0 are Lo; U+2800 is So). The reviewer's point:
widening spec 408's set to Cf would still miss U+3164 and U+2800.

**Lane.** Autonomous (ADR-0144). Two PRs, two colours, never mixed:

| PR | Stories | Colour | Why |
|---|---|---|---|
| 1 | US1 (zone charset), US2 (punctuation-only host) | **red**, plus characterisation for every row §6 lists as unchanged | `From` accepts less |
| 2 | US3 (fold the bracket scan) | **characterisation, observed green**, unmodified after | it changes shape only (#3023 item 6). An assertion that has to change is a block |

**Security-sensitive (PR 1).** US1 is input validation of an address that is stored verbatim and handed to MediaMTX as
the pull source. Phase 4 includes a `test-adversary` pass; phase 6 includes `security-reviewer`.

**Spec number.** 411. Checked 2026-10-18 after `git fetch origin`: `origin/develop` has specs up to 410. `git ls-tree
<ref> specs/` over every local and remote head finds nothing from 411 up. No sibling worktree (`sse-3025b`, `sse-3028`)
holds a 411 directory. Parallel architects are writing 412 and 413. **Check again before each PR is opened**
(memory: two unmerged branches can both claim a number).

**ADRs.** ADR-0038 / ADR-0046 / ADR-0066 (hand-written value objects, `From` + `Ensure.That`), ADR-0105 (`Ensure`;
messages never carry the value), ADR-0047 / ADR-0089 (existing `ApiError` codes, unchanged), ADR-0042 / ADR-0088
(Wolverine's default failure policy, unchanged: replay goes to the error queue), ADR-0139 / ADR-0144 (phase 4a
colours; the lane implements, it does not decide), ADR-0065 (coverage: Domain >= 90 %, Application >= 80 %,
Shared >= 90 %), ADR-0084 (metrics, advisory), ADR-0036 (smallest change; never mix a refactor with a fix).
Constitution §II (no primitive state is added) and §III (shared code lives in `Shared.Kernel`, which every context
touched here already references). Prior art:
- spec 389: `FromStored`, and replay to the error queue.
- spec 392: `IsBlankHost` and the empty-host rule.
- spec 394: the new rule goes last so older messages win, `BracketedHostReadings` supplies the reading set, and an
  agreement test checks the two `From`s against each other.
- spec 408: one predicate, one constant message, a single-source guard. Its decision **A-3** is that Cf is *not* in
  the control and whitespace set.

**No new ADR.** PR 1 reuses spec 394's and spec 408's mechanisms: one more `Satisfies` rule per story, each with its
own constant message, placed after the older rules, and one span predicate each in `UrlAuthority`. PR 2 moves code.
No contract, scope, error code, persistence shape, Wolverine policy, log message or migration changes. **Q1 (§9)
might need a decision record.** That is the owner's call, and it is outside this spec.

**Latency budget.** N/A. Registration and address change are control-plane writes. The audit redaction runs on the
audit consumer. Neither is on a §IV leg. The extra work per write is one ordinal scan of the bracket text, which is
at most 2048 characters.

---

## 1. Premise check on `00a66722` (an experiment, not an assumption)

A scratch probe was used. It is a console app in the session scratchpad that references `CameraCatalog.Domain` and
`StreamDistribution.Domain`, reaches `UrlAuthority.BracketedHostReadings` by reflection, and loads the built
`Architecture.Tests` assembly so it can call the spec 394 fuzz generator. It is not committed. The probe called
`RtspUrl.From`, `StreamSourceUrl.From`, `RtspUrl.FromStored` and `UrlRedaction.ToOrigin` on every row below. The two
`From`s agreed on every row.

### 1.1 Items already delivered (checked, no work)

| #3023 item | Row | `From` today | Delivered by |
|---|---|---|---|
| 1 | `rtsp://@[fe80::1%@10.0.0.66:8554]/live`, `rtsp://@[::1%25@evil.com:554]/s` | refused | #3032 (spec 394) |
| 2 | `rtsp://[1:2:3:4:5:6:7:8:/64]`, `rtsp://[::1:2:3:4:5:6:7:/64]/s` | refused (typed 400) | #3032 (spec 394) |
| 5 | `rtsp://[::1%25x?y]/s`, `rtsp://[::1%25x#y]/s` | refused | #3032 (spec 394) |
| #3031 item 1 | raw CR/LF, TAB, SP or NUL anywhere, including in a zone | refused | #3047 (spec 408) |

### 1.2 Residual (a): percent-encoded characters in a zone. **The premise holds.**

| Input | `From` | `ToOrigin` | `Uri.DnsSafeHost` |
|---|---|---|---|
| `rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live` | **accepted** | `rtsp://[fe80::1]:8554` | `fe80::1%25eth0%0D%0AX-Inject:%201` (kept encoded) |
| `rtsp://[fe80::1%25eth0%00]/s`, `…%7F]`, `…%20x]`, `…%C2%85]` (NEL), `…%E2%80%8E]` (U+200E) | **accepted** | `rtsp://[fe80::1]` | kept encoded |
| `rtsp://[fe80::1/64%25eth0%0D]/s` (zone after a prefix length) | **accepted** | `rtsp://[fe80::1]` | `fe80::1` (zone dropped) |
| `rtsp://[fe80::1%25eth0]/live%0D%0AX-Inject:%201` (in the path) | accepted | `rtsp://[fe80::1]` | n/a. **Not a finding**: spec 408 decided that percent-encoded text in a path stays accepted |

`Uri` never decodes the zone. Go's `net/url`, which MediaMTX uses, does decode it when it parses the host. That is
reasoned from `parseHost` / `unescape(…, encodeZone)` and was **not run**. Go refuses an escape that decodes to a
control character or to a byte >= 0x80, accepts `%20`, and accepts raw non-ASCII bytes. So no modelled reader is
led to a different host. The defect is that a stored and published address holds a zone that one reader refuses
outright, and that another (anything that decodes the zone without validating it) would turn into a raw CR/LF.

### 1.3 Residual (b): invisible characters. The premise holds, and the character's **position** decides who owns it.

| Input | `From` | `ToOrigin` | `Uri.Host` | Where the cure belongs |
|---|---|---|---|---|
| `rtsp://[fe80::1%25eth0‎]/s` (U+200E), `…‮]` (U+202E), `…ㅤ]` (U+3164), `…⠀]` (U+2800) | **accepted** | placeholder (non-ASCII) | `[fe80::1]` | **US1**. RFC 6874 makes a ZoneID `unreserved / pct-encoded`, which is ASCII only, so no valid URI carries these |
| `rtsp://cam‎/p` (U+200E), `rtsp://cam‮/p` (U+202E) | **accepted** | placeholder | **`cam`**. The parser *strips* them, while Go keeps the bytes | **Q1 (owner)** |
| `rtsp://cam﻿/p` (U+FEFF), `rtsp://cam​/p` (U+200B) | **accepted** | placeholder | `cam` + the character | **Q1** |
| `rtsp://camㅤ/p` (U+3164), `…ᅟ`, `…ᅠ`, `…ﾠ` (U+115F, U+1160, U+FFA0) | **accepted** | placeholder | `Host` reads; `IdnHost` / `DnsSafeHost` **throw `UriFormatException`** | **Q1** |
| `rtsp://cam⠀/p` (U+2800), `rtsp://⠀/p` | **accepted** | placeholder | the character itself (`IdnHost` `xn--6ii`) | **Q1** |
| `rtsp://‎/p` | refused (empty host, spec 392 `IsBlankHost`) | placeholder | `''` | done |
| `rtsp://cam/liㅤve`, `rtsp://cam/li‮ve` (path) | accepted | `rtsp://cam` | `cam` | **Q2 (owner)** |

Today the audit is already safe for every one of these rows: `ToOrigin` renders the placeholder for any non-ASCII
host (`HostIsPlainAscii`, `AuthorityWasWrittenAsAscii`). And `CarriesZone`, which reads `DnsSafeHost`, is reached
only after a non-placeholder origin, so the `UriFormatException` rows never get to it. What remains is display
spoofing wherever the stored string is shown verbatim (`CameraDto`, the UI: #3016), and on the write side a host
that `Uri` and Go read differently.

### 1.4 Item 3, the "ASCII-only hosts on write" half. **Not lane-safe; this is Q1.**

| Input | `From` | `ToOrigin` |
|---|---|---|
| `rtsp://kamera-münchen.local/live`, `rtsp://caméra/x`, `rtsp://カメラ.example/live`, `rtsp://ｃａｍ/live` (fullwidth, `IdnHost` `cam`) | **accepted** | placeholder |
| `rtsp://xn--kamera-mnchen-wob.local/live` (A-label) | accepted | rendered |

An ASCII-only host rule would refuse all four Unicode rows. §9 Q1 sets out the cost.

### 1.5 Item 4: **stale on the write side** (superseded by spec 408)

| Input | `From` today | Message | `ToOrigin` |
|---|---|---|---|
| `rtsp://cam ` | refused | spec 408 control/whitespace | `rtsp://cam` (spec 408 A-1 edge exemption) |
| `rtsp://cam /x` | refused | userinfo (unparseable) | placeholder |
| `rtsp://[fe80::1] ` | refused | spec 392 text-after-`]` | placeholder |
| `rtsp://[fe80::1] /x` | refused | spec 392 text-after-`]` | placeholder |

The issue's premise ("a plain host with trailing whitespace is accepted") no longer holds. Two asymmetries are left,
and earlier specs pinned both: the message differs (spec 408 A-8), and so does `ToOrigin`'s edge (spec 408 A-1,
`UrlRedactionLeadingWhitespaceBracketTailTests`). §9 Q3 asks to close the item.

### 1.6 Item 7: dot-only hosts. **The premise holds, and the class is wider than the issue names.**

| Input | `From` | `ToOrigin` | `Uri.Host` / `HostNameType` |
|---|---|---|---|
| `rtsp://./`, `rtsp://.`, `rtsp://..`, `rtsp://../live`, `rtsp://..?x`, `rtsp://..#x` | **accepted** | `rtsp://.` / `rtsp://..` | `.` / `..`, `Basic` |
| `rtsp://-/x`, `rtsp://_/x` | **accepted** | `rtsp://-` / `rtsp://_` | `Basic` |
| `rtsp://cam./x` (fully qualified, trailing dot), `rtsp://cam_1/x`, `rtsp://0/x`, `rtsp://[::]/x` | accepted (**must stay**) | rendered | `Dns` / `IPv6` |
| `rtsp://.../x`, `rtsp://.cam/x`, `rtsp://a..b/x`, `rtsp://.:554/x`, `rtsp://..:554/x` | refused (userinfo: `TryCreate` fails) | placeholder | n/a |

`.` is the DNS root and `..` is not a valid name. `-` and `_` are single-label names that cannot be an RFC 1123
hostname or a k8s Service. Every reader reads the same (useless) host, so there is no host confusion: the camera
registers and the stream never connects. That is the same harm as #3031's empty port, which was filed "harmless".

### 1.7 Item 6: the bracket scan exists twice. **The premise holds, but a naive fold is not behaviour-preserving.**

`AuditPayloadRedaction.BracketContentIsAddressOnly` (`src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs:157-184`)
finds the authority, the userinfo's last `@` and the bracket on its own. `UrlAuthority.BracketedHostReadings`
(`src/Shared.Kernel/UrlAuthority.cs:219-232`) does the same with both the first-`@` and last-`@` readings. Measured
over the spec 394 fuzz corpus (5 seeds, 249,382 inputs, plus 7 hand-written rows), on the domain where `CarriesZone`'s
bracket check is actually reached (`ToOrigin` renders an origin, `HostNameType` is IPv6, `DnsSafeHost` has no `%`),
which is **725 inputs**:

- A fold that only asks "is every `BracketedHostReadings` content address text?" **diverges on 110**, all of them a
  bracket in the userinfo (`rtsp://[]@[FE80::1]`, `rtsp://[x]@[::1]:/s?x#y`). Today these are "not address-only",
  so they count as zoned and `DetailChanged` is `null`. After that fold they would be address-only and get `true`
  or `false`.
- A fold that **keeps the "bracket in the userinfo, so not address-only" arm diverges on 0**.

So US3 is lane-safe only in the second form, and its characterisation must contain the userinfo-bracket rows (the
counterfactual that proves the oracle can fail).

### 1.8 Stored rows

`FromStored` and `EnsureStoredInvariants` are not touched. Every refused row in §6 hydrates. `CameraDto` serves
stored URLs verbatim (#3016). Migrating or rewriting stored rows is #2990. Both belong to the owner and are out of
scope. Renaming a camera does **not** re-validate its address: `PATCH /cameras/{id}` parses `rtspUrl` only when it is
the attribute being changed (`src/CameraCatalog/Api/CameraEndpoints.cs:300-324`). So an existing camera whose address
is now refused stays editable by name, and only an address change meets the new rule.

---

## 2. User stories

### US1 (P1, PR 1): A zone that is not printable ASCII is refused where it is written

As an **operator or integrator registering or correcting a camera**, I want an address such as
`rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live` or `rtsp://[fe80::1%25eth0‎]/s` refused with a message that does not
repeat the address. That way, no zone that one reader decodes into a control character, and another reader shows as
a different interface name, is stored and handed to MediaMTX.

**Independent test:** `RtspUrl.From` and `StreamSourceUrl.From` throw `ArgumentException` with the FR-001 message for
every row of §6 "zone, new". `POST /cameras` with the first row answers 400 `CAMERA_INVALID_REQUEST`, and the body
does not contain `X-Inject`.

### US2 (P2, PR 1): A host of only dots, hyphens or underscores is refused

As an **operator**, I want `rtsp://..` or `rtsp://./` refused at registration, so that a camera is not registered
whose stream can never connect.

**Independent test:** both `From`s throw with the FR-002 message for `rtsp://./`, `rtsp://..`, `rtsp://-/x` and
`rtsp://_/x`. `rtsp://cam./x` is still accepted.

### US3 (P3, PR 2, behaviour-preserving): One bracket locator

As a **maintainer**, I want the audit's zone check to ask `UrlAuthority` for the bracketed host instead of locating it
itself, so that a future change to how a bracketed host is read happens in one place.

**Independent test:** every audit `DetailChanged` outcome is the same before and after. That is proven by an oracle
copied from today's code, over the fuzz corpus and the existing corpora. Every existing suite passes unmodified.

### Stored rows (characterisation, not a story)

Rows stored with a shape from §6 are read through `FromStored`, which is unchanged, so `GET /cameras` and the stream
reconciler keep reading them. They are green before and after.

---

## 3. Acceptance scenarios (Gherkin)

```gherkin
Feature: A camera address's IPv6 zone is printable ASCII and its host names something

  Background:
    Given an operator token with scope "sse.cameras.write" and membership in fab "munich"

  # happy path (unchanged)
  Scenario Outline: An ASCII zone, a percent-encoded printable zone and an ordinary host are still accepted
    When the operator registers a camera with address "<address>"
    Then the response is 201 Created
    Examples:
      | address                           |
      | rtsp://[fe80::1%25eth0]:8554/x    |
      | rtsp://[fe80::1%2512]/s           |
      | rtsp://[fe80::1%25eth0.100]/s     |
      | rtsp://[fe80::1%25eth0:1]/s       |
      | rtsp://[::1%25%40x]/s             |
      | rtsp://cam./x                     |
      | rtsp://cam_1/x                    |

  # US1 bad request (new)
  Scenario Outline: A zone holding a decoded control, space or non-ASCII character is refused without an echo
    When the operator registers a camera with address "<address>"
    Then the response is 400 with title "CAMERA_INVALID_REQUEST" and the detail is the rendered FR-001 text
    And the response body does not contain "<marker>"
    And no camera is stored and no CameraRegisteredV1 is published
    Examples:
      | address                                               | marker   |
      | rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live  | X-Inject |
      | rtsp://[fe80::1%25eth0%00SECRET]/s                    | SECRET   |
      | rtsp://[fe80::1/64%25eth0%0ASECRET]/s                 | SECRET   |
      | rtsp://[fe80::1%25eth0‎SECRET]/s                 | SECRET   |
      | rtsp://[fe80::1%25eth0%E2%80%AESECRET]/s              | SECRET   |

  # US2 bad request (new)
  Scenario Outline: A host of only '.', '-' or '_' is refused
    When the operator registers a camera with address "<address>"
    Then the response is 400 "CAMERA_INVALID_REQUEST" and the detail is the rendered FR-002 text
    Examples:
      | address    |
      | rtsp://./  |
      | rtsp://..  |
      | rtsp://-/x |

  # older messages win
  Scenario Outline: An older rule keeps its message
    When the operator registers a camera with address "<address>"
    Then the response is 400 and the detail is the <older> text
    Examples:
      | address                          | older                         |
      | rtsp://[fe80::1%25eth0\r\nX]/s   | control/whitespace (spec 408) |
      | rtsp://[fe80::1%25%0D@x]/s       | delimiter in brackets (394)   |
      | rtsp://.../x                     | user:password@ (spec 001)     |

  # conflict ordering (unchanged): the address is parsed before the version is checked
  Scenario: A refused address on PATCH answers 400, not 412, and the camera is unchanged
    Given a camera "line-3-inlet" at "rtsp://[fe80::1%25eth0]/h264" at version 2
    When the operator patches its address to "rtsp://[fe80::1%25eth0%0D]/h264" with If-Match version 1
    Then the response is 400 "CAMERA_INVALID_REQUEST"
    And the camera's address is still "rtsp://[fe80::1%25eth0]/h264" at version 2

  # auth (unchanged)
  Scenario: A caller without the write scope is refused before the address is examined
    Given a token without scope "sse.cameras.write"
    When it registers a camera with address "rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]/s"
    Then the response is 403 and the body contains neither the FR-001 text nor "X-Inject"

  # audit (unchanged: A-1)
  Scenario: The audit renders what it rendered before
    Then ToOrigin of "rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live" is "rtsp://[fe80::1]:8554"
    And ToOrigin of "rtsp://.." is "rtsp://.."
    And a CameraAddressChangedV1 between two zoned addresses still records DetailChanged null

  # stored rows (characterisation)
  Scenario: A camera stored before this change with a now-refused address is still read
    Given a cameras row whose rtsp_url is "rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]/s", inserted directly
    When the operator lists the fab's cameras and reads that camera
    Then both answer 200 and include the camera

  # replay (new outcome; spec 389 mechanism unchanged)
  Scenario: A pre-deploy CameraRegisteredV1 with such an address is dead-lettered, not provisioned
    Given a CameraRegisteredV1 for a new camera with address "rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]/s"
    When StreamDistribution handles it
    Then the handler throws, no stream is saved and the gateway is not called
    And one Warning names "STREAM_INVALID_RTSP_SOURCE" and does not contain "X-Inject"

  # US3 (characterisation, PR 2)
  Scenario: The fold changes no audit outcome
    Given every pair drawn from the spec 394 fuzz corpus and the existing DetailChanged corpora
    Then Serialize records the same DetailChanged after the fold as the pre-fold oracle computes
```

Gherkin strings use C# escapes for raw characters (`‎`, `\r\n`). Tests send the characters themselves.

---

## 4. Functional requirements

- **FR-001: The zone holds only printable ASCII (US1).** New predicate `UrlAuthority.NonPrintableCharacterInsideTheZone(url)`.
  For each reading from `BracketedHostReadings` (the reading set spec 394 already uses) that has a closing `]`, take
  the content between `[` and the first `]`:
  - The **zone** is the text after the first `%` in that content, minus a leading `25`. Both `%25` and a bare `%`
    delimit a zone, because `Uri` accepts both. A zone written after a prefix length (`/64%25eth0`) is a zone too.
  - Scan the zone. `%` followed by two ASCII hex digits is one octet. Every other code unit is itself, including a
    malformed `%`.
  - The predicate is **true** when any octet or code unit lies outside U+0021-U+007E.

  `RtspUrl.From` and `StreamSourceUrl.From` refuse it with a new **last** rule, so every input an older rule refuses
  keeps that rule's message. A raw control character or whitespace in the zone keeps spec 408's message. The message
  is identical in both value objects, is a constant, and never contains any part of the input:

  ```
  must hold only printable ASCII in the zone of an IPv6 address (after the '%'), written or percent-encoded: a control character, space or invisible character there is decoded by some readers and not by others
  ```

- **FR-002: A host holds a letter or digit (US2).** New predicate `UrlAuthority.IsPunctuationOnlyHost(host)`: true
  when `host` is non-empty and every character is `.`, `-` or `_`. Its shape mirrors `IsBlankHost(host)`. The rule
  applies it to the host `UrlAuthority.TryReadHost` reads, and it is placed after FR-001's rule. Constant message:

  ```
  must name a host holding a letter or digit: a host of only '.', '-' or '_' names no machine
  ```

- **FR-003: `ToOrigin` and the audit are unchanged** ([Assumption] A-1). Neither predicate is added to
  `UrlRedaction.ToOrigin`. `AuditPayloadRedaction` is not edited in PR 1.
- **FR-004: Stored rows stay readable.** `FromStored` gains no rule. No migration, no rewrite (#2990).
  `FromStoredCallSiteTests` and `FromStoredCallerIlTests` keep guarding the bypass.
- **FR-005: Replay is bounded and visible**, exactly as in spec 389 FR-006. **Replay note for PR 1:** a
  `CameraRegisteredV1` or `CameraAddressChangedV1` published before the deploy, whose address has an FR-001 zone or
  an FR-002 host, dead-letters on its first delivery to StreamDistribution (`STREAM_INVALID_RTSP_SOURCE`, one Warning,
  no part of the address), as in specs 389, 392, 394 and 408. The CameraCatalog row is unaffected and readable. The
  cure is to correct the address with `PATCH /cameras/{id}`.
- **FR-006: Single source.** Only `UrlAuthority.cs` declares the two predicates. `RtspUrl.cs` and
  `StreamSourceUrl.cs` each call both. A cross-row agreement test checks that the two `From`s agree row by row
  (same outcome, same message).
- **FR-007: No new cross-context reference, state or error code.** The messages surface as the `detail` of
  CameraCatalog's existing 400 `CAMERA_INVALID_REQUEST` and inside StreamDistribution's `STREAM_INVALID_RTSP_SOURCE`.
  NetArchTest, `PrimitiveBoundaryTests` and `CameraUrlGuardedUriReadTests` pass unmodified.
- **FR-008: The fold (US3, PR 2).** `UrlAuthority` gains one public member,
  `UrlAuthority.BracketContentIsAddressOnly(string url)`. It answers what
  `AuditPayloadRedaction.BracketContentIsAddressOnly` answers today on every input `CarriesZone` reaches (§1.7).
  - It is built on `BracketedHostReadings`. A bracket in the userinfo still means "not address-only", and no
    readable bracket means "not address-only".
  - `AuditPayloadRedaction` deletes its private `BracketContentIsAddressOnly` and `IsAddressText` and calls the new
    member. `CarriesZone`'s `Uri` arm is untouched (#3031 item 4 is not enrolled).
  - No behaviour changes, so every assertion in every suite stays as written.

## 5. Out of scope

- **Q1-Q3 (§9).** Invisible or non-ASCII characters in a **host** or a **path**. Closing item 4.
- Malformed escapes in a zone (`%25eth0%zz`, a trailing `%`), which stay accepted (A-5).
- #3031 items 3-8 (not enrolled).
- #3016 (`CameraDto` serves stored URLs verbatim). #2990 (migrating stored rows).
- A UI-side check in `apps/shared/src/api/cameras.schema.ts`.
- Running MediaMTX on these inputs. Its behaviour is reasoned from Go's source.

## 6. Accept and reject sets (pinned in both value objects)

**Accept (unchanged, characterisation):**
- zones: `rtsp://[fe80::1%25eth0]:8554/x`, `rtsp://[fe80::1%2512]/s`, `rtsp://[fe80::1%eth0]/s`,
  `rtsp://[fe80::1%09]/s` (a bare-`%` zone `09`, A-6), `rtsp://[fe80::1%25eth0:1]/s`, `rtsp://[fe80::1%25eth0.100]/s`,
  `rtsp://[fe80::1%25br-lan_1~x]/s`, `rtsp://[fe80::1%25eth0!$&'()*+,;=]/s`, `rtsp://[fe80::1%25eth0%3A1]/s`,
  `rtsp://[::1%25%40x]/s`, `rtsp://[::1%25x%3Fy]/s` (pinned by spec 394), `rtsp://[fe80::1%25eth0%2525]/s`,
  `rtsp://[fe80::1%25eth0%zz]/s` (A-5), `rtsp://[fe80::1%25]/s`, `rtsp://[fe80::1/64%25eth0]/s`;
- hosts: `rtsp://cam./x`, `rtsp://cam_1/x`, `rtsp://a-b/x`, `rtsp://0/x`, `rtsp://[::]/x`,
  `rtsp://xn--kamera-mnchen-wob.local/live`;
- **pending Q1/Q2, pinned so that a later decision moves them visibly:** `rtsp://kamera-münchen.local/live`,
  `rtsp://cam‎/p`, `rtsp://camㅤ/p`, `rtsp://cam⠀/p`, `rtsp://cam/liㅤve`, `rtsp://cam/li‮ve`.

**Reject with an older message (unchanged, characterisation):** `rtsp://[fe80::1%25eth0\r\nX]/s` and
`rtsp://[fe80::1%25 x]/s` (spec 408); `rtsp://[fe80::1%25%0D@x]/s` and `rtsp://[::1%25x?y]/s` (delimiter in brackets,
spec 394); `rtsp://[fe80::1%25eth0]%0D/s` (text after `]`, spec 392); `rtsp://.../x`, `rtsp://.cam/x`,
`rtsp://..:554/x` (userinfo); `rtsp://‎/p` (empty host, spec 392). Messages are exactly as observed at T010.

**Reject with FR-001 (new, red):**
- the named rows `rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live` and `rtsp://@[fe80::1%25eth0%0D]/s`;
- the zone `%25eth0{e}` for each `{e}` in `%00`, `%09`, `%0A`, `%0D`, `%1F`, `%20`, `%7F`, `%80`, `%C2%85`, `%C2%A0`,
  `%E2%80%8E`, `%E2%80%AE`, `%EF%BB%BF`, `%E3%85%A4`, `%E2%A0%80`;
- the zone `%25eth0{c}` for each raw `{c}` in U+200E, U+202E, U+FEFF, U+200B, U+3164, U+115F, U+1160, U+FFA0,
  U+2800, U+00FC, U+FF45;
- the prefix-length form `rtsp://[fe80::1/64%25eth0%0D]/s`;
- the bare-`%` form `rtsp://[fe80::1%eth0%0D]/s`;
- `RTSP://[FE80::1%25ETH0%0A]/s`.

**Reject with FR-002 (new, red):** `rtsp://./`, `rtsp://.`, `rtsp://..`, `rtsp://../live`, `rtsp://..?x`,
`rtsp://..#x`, `rtsp://-/x`, `rtsp://_/x`, `rtsp://-._/x`, `RTSP://../x`.

**Hydrate through `FromStored` (characterisation):** every FR-001 and FR-002 row above.

**`ToOrigin` (characterisation, A-1):** the FR-001 rows written in ASCII render `rtsp://[fe80::1]` (`:8554` where it
is written). The rows with raw non-ASCII render the placeholder (as today). `rtsp://..` renders `rtsp://..`,
`rtsp://-/x` renders `rtsp://-`. The pinned row `("rtsp://[fe80::1%25%20]/x", "rtsp://[fe80::1]", true)` in
`AuditPayloadDetailChangedZoneAdversarialTests.cs:41` is unchanged.

## 7. Assumptions

- **[Assumption] A-1: `ToOrigin` withholds neither shape.** Specs 389-408 added each new rule to `ToOrigin`
  because readers disagreed on the host, or because the text carried request-injection characters into an origin.
  Neither applies here:
  - The origin always drops the zone, so a rendered `rtsp://[fe80::1]` is the host every reader dials.
  - `DetailChanged` is already `null` for every zoned pair (`CarriesZone`).
  - `rtsp://..` renders exactly what was written and read.

  Withholding them would retire at least one pinned row (the `%25%20` row of spec 387) with no gain in safety.
  The cost: `From` and `ToOrigin` disagree on these rows (refused, but rendered), in the direction that is safe for
  the write, as spec 408 A-1 also accepts. Flagged for the owner.
- **[Assumption] A-2: `%20` in a zone is refused**, even though Go accepts it there (for Windows interface names
  such as `Ethernet 2`). MediaMTX runs in a Linux container, where no such interface exists, and spec 408 already
  refuses a raw space anywhere. A decoded space is refused for the same reason.
- **[Assumption] A-3: raw non-ASCII in a zone is refused, which includes Cf and U+3164/U+2800 in the zone only.**
  This does **not** reopen spec 408 A-3. Spec 408's set (`char.IsControl ∪ char.IsWhiteSpace`, 84 code units) is
  unchanged and still applies URL-wide. FR-001 is a whitelist for one RFC-bounded field (RFC 6874: a ZoneID is ASCII
  `unreserved / pct-encoded`), so it misses nothing, which answers the #3031 reviewer's objection to a Cf blacklist.
  Hosts and paths keep accepting these characters until the owner answers Q1/Q2.
- **[Assumption] A-4: `-` and `_` join `.`.** The issue names `./` and `..`. The premise check found `rtsp://-/x` and
  `rtsp://_/x` in the same class (`HostNameType` `Basic`, no letter or digit, never resolvable). The rule is
  "a host of only `.`, `-`, `_`". It is **not** "a host must hold an ASCII letter or digit", because that would
  refuse IDN hosts and so decide Q1 by the back door.
- **A-5: malformed escapes in a zone stay accepted** (`%25eth0%zz`, `%25eth0%`, `%25eth0%2`). No reader decodes them
  into a control character (Go refuses them at parse). Nobody filed them. A `%` that does not start a valid escape
  is scanned as itself, and it is printable.
- **A-6: a bare-`%` zone is read the way `Uri` reads it.** `rtsp://[fe80::1%0D]/s` has zone `0D`, which is printable,
  so it is accepted. Go has no `%25` there, treats `%0D` as an escape in the host and refuses it, so no reader turns
  it into a CR. Pinned as characterisation.
- **A-7: idempotency replay across the deploy** (as spec 394 A-5 and spec 408 A-7). A client that, after the deploy,
  retries a request that succeeded before it with such an address gets 400, not the original 201. Accepted.
- **A-8: the spec 394 fuzz floor holds, with a margin of 11.** `CameraUrlZoneDelimiterFuzzAdversarialTests.The_corpora_reach_the_shapes_the_fix_is_about`
  asserts at least 400 accepted inputs. The probe measured 430 accepted today and 411 with FR-001 and FR-002
  applied (19 newly refused by FR-001, 0 by FR-002). The delimiter, unreadable-host and host-throws floors are not
  affected, because the new rules are last. If T001's re-measurement falls below 400, that is a **block**: lowering
  the floor would weaken a gate.

## 8. End-to-end test procedure (phase 5, PR 1)

On a booted Aspire stack (one per machine; stop it before building; check that the AppHost PID started after the
last commit):

1. `POST /cameras` (operator token, fab `munich`) with `{"name":"zone-crlf","rtspUrl":"rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live"}`.
   Expect 400 `CAMERA_INVALID_REQUEST` with `detail` the rendered FR-001 text, and a body free of `X-Inject`.
2. The same with `"rtsp://[fe80::1%25eth0‎]/s"` (JSON escape): expect 400, FR-001. With `"rtsp://.."`: expect 400,
   FR-002. With `"rtsp://cam./x"`: expect 201.
3. Register `rtsp://[fe80::1%25eth0]/live`, then `PATCH` it to `rtsp://[fe80::1%25eth0%0D]/live`: expect 400. `GET`
   shows the address unchanged. Rename it (`{"name":"renamed"}`, a name-only PATCH): expect 204.
4. Insert a `cameras` row with `rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]/s` directly (psql). `GET /cameras` and
   `GET /cameras/{id}` answer 200.
5. Read the audit row of step 3's registration: `Url` is `rtsp://[fe80::1]`. A refused write produces no audit row.

PR 2 has no end-to-end step: it changes shape only, and the evidence is the characterisation output (ADR-0037
phase 5, "explicit run/test"). Latency is N/A (header).

## 9. Owner questions (excluded from the lane)

- **Q1: should registration accept only ASCII hosts (#3023 item 3, the host half)?** The cure that would close §1.3's
  host rows is a whitelist, the write-side twin of `UrlRedaction.HostIsPlainAscii`. Its cost:
  1. Every Unicode (U-label) hostname is refused: `kamera-münchen.local`, `カメラ.example`, fullwidth `ｃａｍ`.
  2. The workaround (write the A-label, `xn--…`) works for unicast DNS but **not for mDNS**. RFC 6762 §16 names are
     raw UTF-8, and an A-label does not resolve over mDNS.
  3. Nobody knows whether MediaMTX's Go resolver inside k3s resolves either form. Not run.

  Evidence for how much it would hit: **no production deployment exists** (CLAUDE.md, observability row), and no
  non-ASCII camera host appears anywhere in `src`, `deploy` or `apps`. The audit already renders every non-ASCII host
  as `<unparseable url>`, so such cameras are already second-class there.

  The alternatives:
  - (ii) Refuse a fixed invisible set in hosts: Default_Ignorable_Code_Point plus U+2800 plus the Hangul fillers.
    This keeps IDN, but it needs a hard-coded Unicode table and is incomplete by construction, which is the #3031
    reviewer's point.
  - (iii) Change nothing on write and cure the display through #3016.

  **The architect recommends (i)** if no deployment uses U-label or mDNS UTF-8 names. Q1 may want a short decision
  record, because it fixes the host vocabulary for every camera address.
- **Q2: should invisible characters in a path, query or fragment be refused?** (`rtsp://cam/li‮ve`.) The origin is
  unaffected, and the exposure is display only, through #3016. **The architect recommends no**: cure the display, not
  the write.
- **Q3: close #3023 item 4 as superseded by spec 408?** Both trailing-whitespace shapes are now refused (§1.5). What is
  left are two pinned decisions (spec 408 A-1 and A-8, spec 392's bracket edge). **The architect recommends closing
  it** with a comment that links this table.
