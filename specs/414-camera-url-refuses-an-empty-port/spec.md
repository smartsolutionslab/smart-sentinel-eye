# Spec 414: A camera URL with a ':' and no port is refused, and the three guarded `Uri` reads fold into one helper

**Feature Branch**: `fix/3031-camera-url-residuals` (cut from `origin/develop` at `18d967db`) for PR 1;
`refactor/3031-guarded-uri-read-fold`, cut from `develop` after PR 1 merges, for PR 2.

**Created**: 2026-10-18

**Status**: Phases 1-3 written (Specify, Plan, Tasks). No `[NEEDS CLARIFICATION]` is left in the lane scope. Item 6
is **dropped from the lane** and put to the owner (§9 Q1), because the premise check found it rewrites a message that
49 distinct rows pin on purpose. The places where this spec reads the issue are marked **[Assumption]** in §7 and
flagged for the owner in each PR.

**Input**: Issue [#3031](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3031), with every comment read.
The enrolment comment of 2026-10-18 17:32 narrows the lane to items 3 (empty port), 6 (stale 400 text) and 7 (fold the
repeated try/catch). Items 1 and 2 shipped in #3047 / #3048 (spec 408). Items 4, 5 and 8 are out of scope.

**Lane.** Autonomous (ADR-0144). Two PRs, two colours, never mixed:

| PR | Story | #3031 item | Colour | Why |
|---|---|---|---|---|
| 1 | US1 (empty port) | 3 | **red**, plus characterisation for every row §6 lists as unchanged | `From` accepts less |
| 2 | US2 (one guarded-read helper) | 7 | **characterisation, observed green**, unmodified after | it changes shape only. An assertion that has to change is a block |
| - | - | 6 | **dropped** (owner, §9 Q1) | the fix is a product-wording decision across 23 test files |

**Security-sensitivity.** Low. US1 narrows input validation of an address that is stored verbatim and handed to MediaMTX;
no reader is led to a different host (§1.1). Phase 6 for PR 1 is `backend-reviewer` plus `security-reviewer`, because
it is a trust-boundary rule; PR 2 is `backend-reviewer` only.

**Spec number.** 414. Checked 2026-10-18 after `git fetch origin`: `origin/develop` holds 407-411 and 413, so 412 is a
gap. `git ls-tree <ref> specs/` over every local and remote head finds no 412 and nothing from 414 up. `gh pr list
--state open` lists one PR (#2811, a devcontainer, no spec). 412 is skipped rather than reused, because a gap in a
sequence that parallel architects write into is more likely a claim not yet pushed than a free number. **Check again
before each PR is opened** (memory: two unmerged branches can both claim a number).

**ADRs.** ADR-0038 / ADR-0046 / ADR-0066 (hand-written value objects, `From` + `Ensure.That`), ADR-0105 (`Ensure`;
messages never carry the value), ADR-0047 / ADR-0089 (existing `ApiError` codes, unchanged), ADR-0042 / ADR-0088
(Wolverine's default failure policy, unchanged: replay goes to the error queue), ADR-0139 / ADR-0144 (phase 4a
colours; the lane implements, it does not decide), ADR-0065 (coverage), ADR-0084 (metrics, advisory), ADR-0036
(smallest change; never mix a refactor with a fix). Constitution §II (no primitive state is added) and §III (shared
code lives in `Shared.Kernel`, which every context touched here already references). Prior art:
- spec 389: `FromStored`, and replay to the error queue.
- spec 392: **A-1, "an empty port after `]` stays accepted"**. This spec reverses that assumption (A-2 below), because
  #3031 item 3 now names the empty port as the defect and the owner's enrolment takes it red-first.
- spec 394: the new rule goes last so older messages win; the fuzz corpus and its floors.
- spec 408 / 411: one predicate, one constant message, a single-source guard, an agreement test; 408 A-2 retires
  existing rows "by the issue's own decision".

**No new ADR.** PR 1 adds one `Satisfies` rule with its own constant message, placed after the older rules, and one
span predicate in `UrlAuthority`. PR 2 moves code inside `Shared.Kernel`. No contract, scope, error code, persistence
shape, Wolverine policy, log message or migration changes. Q1 (item 6) is a wording decision, not an architectural one.

**Latency budget.** N/A. Registration and address change are control-plane writes, and `ToOrigin` runs on the audit
consumer. Neither is on a §IV leg. The extra work per write is one ordinal scan of at most 2048 characters.

---

## 1. Premise check on `18d967db` (an experiment, not an assumption)

A scratch probe (a console app in the session scratchpad, not committed) references `CameraCatalog.Domain`,
`StreamDistribution.Domain` and the built `Architecture.Tests` assembly, and calls `RtspUrl.From`, `StreamSourceUrl.From`,
`RtspUrl.FromStored`, `UrlRedaction.ToOrigin` and, by reflection, the spec 394 fuzz test's `Generate` and `Inspect`. The
two `From`s agreed on every row.

### 1.1 Item 3: an empty port. **The premise holds.**

| Input | `From` today | `ToOrigin` | `Uri` reads |
|---|---|---|---|
| `rtsp://10.0.0.66:/live`, `rtsp://[::1]:/live` (the issue's rows) | **accepted** | `rtsp://10.0.0.66`, `rtsp://[::1]` | host as written, `Port` -1, `IsDefaultPort` true |
| `rtsp://cam:`, `rtsp://cam:/x`, `rtsp://cam:?q`, `rtsp://cam:#f`, `rtsp://@cam:/x`, `RTSP://CAM:/S` | **accepted** | `rtsp://cam` | same |
| `rtsp://[fe80::1]:`, `…]:/`, `…]:?q`, `…]:#f`, `…]:/s`, `rtsp://[fe80::1%25eth0]:/s`, `rtsp://[fe80::1/64]:/x`, `rtsp://[1:2:3:4:5:6:7::/64]:/s` | **accepted** | the bracketed origin, no port | same |

`Uri` reads the empty port as the default (554); ffmpeg's `atoi` reads 0, and no connection reaches port 0; Go's
`net/url` accepts `port = *DIGIT` and leaves `Port()` empty, so what MediaMTX dials depends on its own default
(reasoned from the source, **not run**). Every reader reads the same host, so this is not host confusion. The harm is the one
#3031 filed: a stored address that one reader dials and another cannot. **Spec 392 A-1 decided to keep it accepted**;
this spec reverses that (A-2).

Not affected, and pinned unchanged: an explicit port (`:554`, `:0554`, `:0`, `:00`), and a `:` in the path, query or
fragment (`rtsp://cam/x:`, `rtsp://cam/:/x`, `rtsp://cam/x?a=:`, `rtsp://cam/x#:`). **An explicit port 0 stays
accepted** (`rtsp://cam:0/x`, `rtsp://[::]:0/x`, pinned in `RtspUrlAuthorityHostAdversarialTests.cs:131`); nobody filed
it, and it is out of scope (§5).

### 1.2 Item 3 hits the spec 394 fuzz floor. **Measured, and the cure is a corpus row, not a lower floor.**

`CameraUrlZoneDelimiterFuzzAdversarialTests.The_corpora_reach_the_shapes_the_fix_is_about` asserts at least 400 accepted
inputs over its five corpora (249,382 inputs). Measured on `18d967db`:

| Corpus | accepted today | of which empty port | accepted after FR-001 |
|---|---|---|---|
| tokens / 3023 | 110 | 16 | 94 |
| tokens / 1 | 122 | 16 | 106 |
| alphabet / 3023 | 11 | 0 | 11 |
| structured / 3023 | 78 | 15 | 63 |
| structured / 11 | 90 | 10 | 80 |
| **five corpora** | **411** | **57** | **354 (below 400)** |
| tokens / 3031 (candidate sixth) | 118 | 14 | 104 |

So FR-001 alone takes the floor below 400, and spec 411 A-8 already ruled that **lowering the floor is a block**. The
cure in this spec (A-3) is to add a sixth corpus row, `{ "tokens", 3031 }`, and leave every threshold as written: with
it, 354 + 104 = **458** accepted. The other floors only rise: delimiter 71 + 6 = 77 (floor 60), unreadable 23 + 0 = 23
(floor 20), `Host` throws rises with the corpus (floor 150). `Inspect` over the 49,710 inputs of tokens / 3031 finds
**0 defects** today (run by reflection against the built assembly), so the row brings no unrelated failure.

### 1.3 Item 6: stale 400 text. **The premise holds, and the fix is a wording decision. Dropped (§9 Q1).**

`EnsureStoredInvariants` runs `HasNoUserInfo` first, and `HasNoUserInfo` is false when `UrlAuthority.TryReadUserInfo`
cannot read the URL at all. So every shape `Uri.TryCreate` refuses (or whose `UserInfo` throws) answers
`must not contain a user:password@ segment`: `rtsp://[[::1]]/live` and `rtsp://@@10.0.0.66/live` (the issue's rows),
and also `rtsp://cam:65536/s`, `rtsp://a b/x`, `rtsp://[]`, `rtsp://.../x`, `rtsp://cam::/x`.

The existing tests pin this on purpose. 23 files use `UserInfoRefusal` or the literal text; of the 64 distinct
`{ address, UserInfoRefusal }` rows, **49 are unparseable shapes** (the other 15 carry a real userinfo). They include the
400 body of the endpoint suites (`CameraUrlControlCharacterEndpointCharacterisationTests.cs:18`,
`CameraUrlZoneDelimiterEndpointTests.cs:29`) and the cross-context agreement suites. Spec 411's own Gherkin pins
`rtsp://.../x` as "user:password@ (spec 001)" under "older messages win". And several unparseable rows *do* carry a
credential (`rtsp://admin:s3cr3t#@cam/s`, `rtsp://a:4242@c@cam/s`), where today's text is the helpful one. Choosing a
replacement text, and whether to split it by cause, is product wording; the lane does not decide it.

### 1.4 Item 7: three copies of one try/catch. **The premise holds.**

| Member | Guards | Throwing input covered by an existing test |
|---|---|---|
| `UrlAuthority.TryReadHost` (`UrlAuthority.cs:139-153`) | `Uri.TryCreate` and `uri.Host` | `rtsp://[1:2:3:4:5:6:7:8:/64]` (`UrlAuthorityZoneDelimiterAdversarialTests.cs:86-97`) |
| `UrlAuthority.TryReadUserInfo` (`:155-169`) | `Uri.TryCreate` and `uri.UserInfo` | `rtsp://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s` (same file, `:111-119`) |
| `UrlRedaction.TryParse` (`UrlRedaction.cs:42-54`, private) | `Uri.TryCreate` only | `FILE://` + U+200E + `/x` and the `ParserThrowsRows` set (`UrlRedactionAuthorityLocationTests.cs:57-98`) |

Each is `try { … Uri.TryCreate … } catch (IndexOutOfRangeException) { … = null; }`. Probe: `Uri.TryCreate` itself throws
for `FILE://`+U+200E+`/x` and `file:/\`+U+200E; `Host` and `UserInfo` both throw for `rtsp://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s`;
only `Host` throws for `rtsp://[1:2:3:4:5:6:7:8:/64]`. The two-class shape (two in `UrlAuthority`, one in `UrlRedaction`)
means the shared helper is `internal` in `UrlAuthority`, not `private` (same assembly; plan §4).

`UrlRedaction.ToOrigin`'s later `uri.Host` reads stay unguarded. That is #3031 item 4, not enrolled; PR 2 does not
touch it.

### 1.5 Stored rows

`FromStored` and `EnsureStoredInvariants` are not touched by either PR. Every FR-001 row hydrates. `CameraDto` serves
stored URLs verbatim (#3016); migrating stored rows is #2990. Renaming a camera does not re-validate its address
(`PATCH /cameras/{id}` parses `rtspUrl` only when it is changed), so an existing camera with an empty port stays
editable by name.

---

## 2. User stories

### US1 (P1, PR 1): An address with a ':' and no port is refused where it is written

As an **operator or integrator registering or correcting a camera**, I want `rtsp://10.0.0.66:/live` refused with a
message that tells me to write the port or drop the `:`, so that no address is stored that MediaMTX and ffmpeg read
as different ports.

**Independent test:** `RtspUrl.From` and `StreamSourceUrl.From` throw `ArgumentException` with the FR-001 message for
every row of §6 "new". `POST /cameras` with `rtsp://10.0.0.66:/live` answers 400 `CAMERA_INVALID_REQUEST`; with
`rtsp://10.0.0.66:554/live` it answers 201.

### US2 (P3, PR 2, behaviour-preserving): One guarded `Uri` read

As a **maintainer**, I want the three copies of the `IndexOutOfRangeException` guard folded into one helper, so that the
next parser read that needs guarding is written once, in one place.

**Independent test:** every existing suite that reaches the three members passes unmodified, the new characterisation
of the three catch arms (plan §6.5 C-F1) is green before and after, and removing the helper's catch turns it red.

### Stored rows (characterisation, not a story)

Rows stored with an empty port are read through `FromStored`, unchanged, so `GET /cameras` and the stream reconciler
keep reading them.

---

## 3. Acceptance scenarios (Gherkin)

```gherkin
Feature: A camera address names its port or leaves the ':' out

  Background:
    Given an operator token with scope "sse.cameras.write" and membership in fab "munich"

  # happy path (unchanged)
  Scenario Outline: An explicit port, no port, and a ':' after the authority are still accepted
    When the operator registers a camera with address "<address>"
    Then the response is 201 Created
    Examples:
      | address                       |
      | rtsp://10.0.0.66:554/live     |
      | rtsp://10.0.0.66/live         |
      | rtsp://[::1]:8554/live        |
      | rtsp://[fe80::1]:0554/s       |
      | rtsp://cam/x?a=:              |
      | rtsp://cam:0/x                |

  # US1 bad request (new)
  Scenario Outline: A ':' with no port after the host is refused
    When the operator registers a camera with address "<address>"
    Then the response is 400 with title "CAMERA_INVALID_REQUEST" and the detail is the rendered FR-001 text
    And no camera is stored and no CameraRegisteredV1 is published
    Examples:
      | address                     |
      | rtsp://10.0.0.66:/live      |
      | rtsp://[::1]:/live          |
      | rtsp://cam:                 |
      | rtsp://[fe80::1%25eth0]:/s  |

  # older messages win
  Scenario Outline: An older rule keeps its message
    When the operator registers a camera with address "<address>"
    Then the response is 400 and the detail is the <older> text
    Examples:
      | address                      | older                          |
      | rtsp://cam::/x               | user:password@ (spec 001)      |
      | rtsp:///cam:/x               | empty host (spec 392)          |
      | rtsp://[fe80::1%25eth0%0D]:/s| zone charset (spec 411)        |
      | rtsp://-:/x                  | punctuation-only host (411)    |

  # conflict ordering (unchanged): the address is parsed before the version is checked
  Scenario: A refused address on PATCH answers 400, not 412, and the camera is unchanged
    Given a camera "line-3-inlet" at "rtsp://10.0.0.66:554/live" at version 2
    When the operator patches its address to "rtsp://10.0.0.66:/live" with If-Match version 1
    Then the response is 400 "CAMERA_INVALID_REQUEST"
    And the camera's address is still "rtsp://10.0.0.66:554/live" at version 2

  # auth (unchanged)
  Scenario: A caller without the write scope is refused before the address is examined
    Given a token without scope "sse.cameras.write"
    When it registers a camera with address "rtsp://10.0.0.66:/live"
    Then the response is 403 and the body does not contain the FR-001 text

  # audit (unchanged: A-1)
  Scenario: The audit renders what it rendered before
    Then ToOrigin of "rtsp://10.0.0.66:/live" is "rtsp://10.0.0.66"
    And ToOrigin of "rtsp://[fe80::1]:/s" is "rtsp://[fe80::1]"

  # stored rows (characterisation)
  Scenario: A camera stored before this change with an empty port is still read
    Given a cameras row whose rtsp_url is "rtsp://10.0.0.66:/live", inserted directly
    When the operator lists the fab's cameras and reads that camera
    Then both answer 200 and include the camera

  # replay (new outcome; spec 389 mechanism unchanged)
  Scenario: A pre-deploy CameraRegisteredV1 with an empty port is dead-lettered, not provisioned
    Given a CameraRegisteredV1 for a new camera with address "rtsp://10.0.0.66:/live"
    When StreamDistribution handles it
    Then the handler throws, no stream is saved and the gateway is not called
    And one Warning names "STREAM_INVALID_RTSP_SOURCE" and does not contain "10.0.0.66"

  # US2 (characterisation, PR 2)
  Scenario: The fold changes no answer
    Given every input the existing UrlAuthority and UrlRedaction suites and the spec 394 fuzz corpus use
    Then TryReadHost, TryReadUserInfo and ToOrigin answer exactly as before the fold, and none throws
```

---

## 4. Functional requirements

- **FR-001: The written port is not empty (US1).** New predicate `UrlAuthority.WrittenPortIsEmpty(string url)`. It
  works on the written text, with no parse, because `Uri` reports an empty port and no port identically (`Port` -1,
  `IsDefaultPort` true, `Authority` without the `:`). It is **true** when:
  - the host is bracketed, and on a `BracketedHostReadings` reading the text between the first `]` and the next
    `/`, `?`, `#` or `\` is exactly `:`; or
  - the host is not bracketed, and the authority (from `TextFromTheAuthority(url.TrimStart())`, cut at the first
    terminator, after its last `@`) is non-empty and ends with `:`.

  The bracket arm matters: `rtsp://[1:2:3:4:5:6:7::/64]/s` has a `/` inside the brackets, so a naive "the authority up
  to the first terminator ends with `:`" scan reads `[1:2:3:4:5:6:7::` and refuses an accepted address. Over the
  fuzz corpus the naive scan errs both ways on accepted inputs: it refuses `RTSP://[2821::/64]/` (no port) and misses
  `rtsp://[7E8C::2214%251/64]:/s` (an empty port after a prefix length). The bracket-aware form finds 57 empty-port
  inputs among the 411 accepted (§1.2).

  `RtspUrl.From` and `StreamSourceUrl.From` refuse it with a new **last** rule, after `HostIsNotPunctuationOnly`, so
  every input an older rule refuses keeps that rule's message. The message is identical in both value objects, is a
  constant, and never contains any part of the input:

  ```
  must write a port after the ':' that follows the host, or leave the ':' out: an empty port is read as the default port by some readers and as port 0 by others
  ```

- **FR-002: `ToOrigin` and the audit are unchanged** ([Assumption] A-1). The predicate is not added to
  `UrlRedaction.ToOrigin`. `AuditPayloadRedaction` is not edited.
- **FR-003: Stored rows stay readable.** `FromStored` gains no rule. No migration, no rewrite (#2990).
- **FR-004: Replay is bounded and visible**, exactly as in spec 389 FR-006. **Replay note for PR 1:** a
  `CameraRegisteredV1` or `CameraAddressChangedV1` published before the deploy whose address has an empty port
  dead-letters on its first delivery to StreamDistribution (`STREAM_INVALID_RTSP_SOURCE`, one Warning, no part of the
  address), as in specs 389, 392, 394, 408 and 411. The CameraCatalog row is unaffected and readable. The cure is to
  correct the address with `PATCH /cameras/{id}`.
- **FR-005: Single source.** Only `UrlAuthority.cs` declares the predicate; `RtspUrl.cs` and `StreamSourceUrl.cs` each
  call it. A cross-row agreement test checks that the two `From`s agree row by row (same outcome, same message).
- **FR-006: No new cross-context reference, state or error code.** The message surfaces as the `detail` of
  CameraCatalog's existing 400 `CAMERA_INVALID_REQUEST` and inside StreamDistribution's `STREAM_INVALID_RTSP_SOURCE`.
  NetArchTest, `PrimitiveBoundaryTests` and `CameraUrlGuardedUriReadTests` pass unmodified.
- **FR-007: The fold (US2, PR 2).** `UrlAuthority` gains one `internal` generic helper that runs `Uri.TryCreate` and a
  read of the parsed `Uri` inside one `catch (IndexOutOfRangeException)`. `TryReadHost`, `TryReadUserInfo` and
  `UrlRedaction.ToOrigin` (in place of its private `TryParse`, which is deleted) call it. Their public signatures,
  `Ensure.That` guards and answers are unchanged; `ToOrigin`'s later unguarded `uri.Host` reads are not moved into it
  (#3031 item 4). After the fold `src/Shared.Kernel` holds exactly one `catch (IndexOutOfRangeException)`.

## 5. Out of scope

- #3031 item 6 (§9 Q1), item 4 (`ToOrigin` / `CarriesZone` read unguarded behind rule order), item 5
  (`BracketedHostReadings` blind spot, pinned), item 8 (`FromStored` exception type, informational).
- An explicit port 0 (`rtsp://cam:0/x`), which ffmpeg and `Uri` read alike (0) and which nobody filed. Pinned unchanged.
- #3016 (`CameraDto` serves stored URLs verbatim). #2990 (migrating stored rows).
- A UI-side check in `apps/shared/src/api/cameras.schema.ts`.
- Running MediaMTX or ffmpeg on these inputs; their behaviour is reasoned from source.

## 6. Accept and reject sets (pinned in both value objects)

**Accept (unchanged, characterisation):** `rtsp://10.0.0.66:554/live`, `rtsp://10.0.0.66/live`, `rtsp://cam:554/x`,
`rtsp://cam:0/x`, `rtsp://cam:00/x`, `rtsp://[::]:0/x`, `rtsp://[fe80::1]:0554/s`, `rtsp://[::1]:8554/live`,
`rtsp://cam/x:`, `rtsp://cam/:/x`, `rtsp://cam/x?a=:`, `rtsp://cam/x#:`, `rtsp://[1:2:3:4:5:6:7::/64]/s`,
`rtsp://[fe80::1/64]/x`, `rtsp://cam`.

**Reject with an older message (unchanged, characterisation):** `rtsp://cam::/x`, `rtsp://[::1]::/x`, `rtsp://u@cam:/x`,
`rtsp://..:/x` (userinfo); `rtsp:///cam:/x` (empty host); `rtsp://[fe80::1]x:/s` (text after `]`);
`rtsp://[1:2:3:4:5:6:7:8:/64]:/s` (unreadable host); `rtsp://[::1%25x?y]:/s` (delimiter in brackets); `rtsp://cam: `
(control or whitespace, spec 408); `rtsp://[fe80::1%25eth0%0D]:/s` (zone, spec 411); `rtsp://-:/x` (punctuation-only
host, spec 411). Messages exactly as observed at T010.

**Reject with FR-001 (new, red):** `rtsp://10.0.0.66:/live`, `rtsp://[::1]:/live`, `rtsp://cam:`, `rtsp://cam:/x`,
`rtsp://cam:?q`, `rtsp://cam:#f`, `rtsp://@cam:/x`, `rtsp://cam.:/x`, `RTSP://CAM:/S`, `rtsp://cam:/x?a=b`,
`rtsp://xn--kamera-mnchen-wob.local:/live`, `rtsp://[fe80::1]:`, `rtsp://[fe80::1]:/`, `rtsp://[fe80::1]:?q`,
`rtsp://[fe80::1]:#f`, `rtsp://[fe80::1]:/s`, `rtsp://[fe80::1]:/s?x#y`, `rtsp://[fe80::1%25eth0]:/s`,
`rtsp://@[::1]:/x`, `rtsp://[::ffff:10.0.0.1]:/s`, `rtsp://[fe80::1/64]:/x`, `rtsp://[fe80::1/64%25eth0]:/x`,
`rtsp://[1:2:3:4:5:6:7::/64]:/s`, and `rtsp://cam` + U+200E + `:/p` (accepted today; its `ToOrigin` is already the
placeholder).

**Hydrate through `FromStored` (characterisation):** every FR-001 row.

**`ToOrigin` (characterisation, A-1):** each FR-001 row written in ASCII renders its origin without a port
(`rtsp://10.0.0.66`, `rtsp://[fe80::1]`, `rtsp://[1:2:3:4:5:6:7:0]`, `rtsp://cam.`), exactly as today; the U+200E row
renders the placeholder. The pinned rows `UrlRedactionAuthorityHostCharacterisationTests.cs:32`,
`UrlRedactionAuthorityHostAdversarialTests.cs:73` and `AuditPayloadDetailChangedLocatorCharacterisationTests.cs:43` are
unchanged.

## 7. Assumptions

- **[Assumption] A-1: `ToOrigin` keeps rendering an empty-port address.** Specs 389-408 added a rule to `ToOrigin` when
  readers disagreed on the *host* or the text carried injection characters into an origin. Neither applies: every
  reader reads the same host, and the rendered origin is the host plus the default port that `Uri` dials. Withholding
  it would retire three pinned `ToOrigin` / audit rows for no gain. The cost is that `From` and `ToOrigin` disagree on
  these rows (refused, but rendered), in the direction that is safe for the write, as spec 408 A-1 and spec 411 A-1
  also accept. Flagged for the owner.
- **[Assumption] A-2: spec 392 A-1 is reversed, and the rows that pin it move.** Spec 392 kept the empty port accepted
  because no issue asked otherwise; #3031 item 3 now does, and the enrolment takes it red-first. As spec 408 A-2 did,
  the rows that pin acceptance are retired by the issue's own decision, each named in plan §8 and in the PR:
  - `RtspUrlAuthorityHostAdversarialTests.cs:141-144, 151` and `StreamSourceUrlAuthorityHostAdversarialTests.cs:141-144, 151`
    (five rows each in `AddressesThatWorkedBeforeAndStillDo`);
  - `RtspUrlAuthorityHostCharacterisationTests.cs:33` and `StreamSourceUrlAuthorityHostCharacterisationTests.cs:33`
    (`rtsp://[fe80::1]:/s` in `Accepted`);
  - `tests/Architecture.Tests/Persistence/CameraUrlAuthorityHostRefusalTests.cs:61` (`rtsp://[fe80::1]:/s`, counted
    as accepted in `The_corpus_exercises_every_outcome_class_row_by_row`).

  If the owner wants spec 392 A-1 to stand, item 3 is closed as won't-fix and PR 1 is not opened.
- **[Assumption] A-3: the fuzz floor is held by adding a corpus row, not by lowering it.** §1.2. Adding
  `{ "tokens", 3031 }` to `CameraUrlZoneDelimiterFuzzAdversarialTests.Corpora` and updating its "Measured on the five
  seeds" comment is a strictly stronger test: every existing input and every threshold stays, about 50,000 inputs are
  added, and all of them pass `Inspect` today. It lands in the characterisation commit (green before the fix). It costs
  about 20 % more runtime on that test. Flagged for the owner; if the owner reads a corpus change as moving a gate,
  PR 1 blocks and item 3 waits.
- **A-4: idempotency replay across the deploy** (as spec 394 A-5, 408 A-7, 411 A-7). A client that, after the deploy,
  retries a request that succeeded before it with an empty port gets 400, not the original 201. Accepted.
- **A-5: the predicate sees the written text after `TrimStart`**, like `BracketedHostReadings`. A trailing run of
  whitespace or controls after the `:` never reaches the new rule: spec 408's rule refuses it first, with its own
  message (`rtsp://cam: ` is in §6's older-message set).

## 8. End-to-end test procedure (phase 5, PR 1)

On a booted Aspire stack (one per machine; stop it before building; check that the AppHost PID started after the last
commit):

1. `POST /cameras` (operator token, fab `munich`) with `{"name":"empty-port","rtspUrl":"rtsp://10.0.0.66:/live"}`.
   Expect 400 `CAMERA_INVALID_REQUEST`, `detail` the rendered FR-001 text.
2. The same with `rtsp://[::1]:/live`: expect 400, FR-001. With `rtsp://10.0.0.66:554/live`: expect 201.
3. `PATCH` the camera from step 2 to `rtsp://10.0.0.66:/live`: expect 400; `GET` shows the address unchanged. Rename it
   (`{"name":"renamed"}`): expect 204.
4. Insert a `cameras` row with `rtsp://10.0.0.66:/live` directly (psql). `GET /cameras` and `GET /cameras/{id}` answer 200.
5. Read the audit row of step 2's registration: `Url` is `rtsp://10.0.0.66:554`. A refused write produces no audit row.

PR 2 has no end-to-end step: it changes shape only, and the evidence is the characterisation output (ADR-0037 phase 5,
"explicit run/test"). Latency is N/A (header).

## 9. Owner questions (excluded from the lane)

- **Q1: what should an unparseable address answer (#3031 item 6)?** Today every shape `Uri` cannot read answers the
  userinfo text. Options:
  1. **Leave it** and close item 6: it is a typed 400, it has been since spec 001, and for unparseable shapes that carry
     a credential (`rtsp://admin:s3cr3t#@cam/s`) it is the right hint.
  2. **One new constant message** for "the parser cannot read this address", as a rule in `EnsureStoredInvariants`
     *before* `HasNoUserInfo` (so `FromStored` refuses exactly the same set, only with the new text). Cost: about 49
     pinned rows in 23 files move, including endpoint 400 bodies, and the credential-bearing rows lose their hint.
  3. **Split by cause**: keep the userinfo text when the written authority holds an `@` before its first terminator, use
     the new text otherwise. Fewer rows move (the credential rows stay), at the price of one more written-text predicate.

  **The architect recommends 1**, or 3 if the wording is wanted. Either 2 or 3 is a phase 1 of its own, with the
  wording supplied by the owner.
- **Q2: confirm A-2 and A-3**, the reversal of spec 392 A-1 and the sixth fuzz corpus row. The lane proceeds on both
  as recommendations; a "no" on either closes item 3 instead.
