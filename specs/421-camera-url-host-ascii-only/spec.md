# Spec 421: A camera URL's plain DNS host is written in ASCII

**Feature Branch**: `fix/3023-ascii-only-plain-hosts` (cut from `origin/develop` at `3442dae0`).

**Created**: 2026-10-20

**Status**: Phases 1-3 written (Specify, Plan, Tasks). No `[NEEDS CLARIFICATION]` is left. The owner answered the one
question this spec raised at its premise check (§1.4) on 2026-10-20: the five test edits are authorised, and no ADR is
needed.

**Input**: Issue [#3023](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3023), item 3 (host half), with
every comment read. This includes the #3031 security-review row (invisible characters, both Cf ones such as U+200E,
U+202E and U+FEFF, and non-Cf ones such as U+3164 (Lo) and U+2800 (So)) and the owner decisions of 2026-10-19:
- **Q1**: plain DNS hosts are ASCII-only on write. The rule mirrors `HostIsPlainAscii`, and stored data is untouched
  (#2990 stays with the owner).
- **Q2**: paths are left alone.
- **Q3**: item 4 is closed as stale.

**Lane.** Autonomous (ADR-0144). One PR, colour **red** (`From` accepts less), with characterisation for every row §6
lists as unchanged.

**Security-sensitivity.** Moderate. This is input validation of an address that is stored verbatim, handed to MediaMTX
and shown in the UI (#3016). The exposure being closed is display spoofing: the stored string differs from the host
the parser reads. Phase 4 adds a `test-adversary` pass, and phase 6 adds `security-reviewer`.

**Spec number.** 421. Checked 2026-10-20 after `git fetch origin`: `origin/develop` (`3442dae0`) holds up to 419, open
PR #3066 holds 420, and `git ls-tree <ref> specs/` over every remote head finds nothing from 421 up. **Check again
before the PR is opened.**

**ADRs.**
- ADR-0038 / ADR-0046 / ADR-0066: hand-written value objects, built with `From` + `Ensure.That`.
- ADR-0105: messages never carry the value.
- ADR-0047 / ADR-0089: the existing `ApiError` codes, unchanged.
- ADR-0042 / ADR-0088: Wolverine's default failure policy, unchanged. Replay goes to the error queue.
- ADR-0139 / ADR-0144: the phase 4a colours, and the lane implements but does not decide.
- ADR-0065: coverage.
- ADR-0084: code metrics, advisory.
- ADR-0036: smallest change.
- Constitution §II (no primitive state added) and §III (shared code lives in `Shared.Kernel`, which both contexts
  already reference).

Prior art:
- spec 389: `FromStored`, and replay to the error queue.
- spec 394: a new rule goes last so older messages win. Also the fuzz corpus and its floors.
- specs 408 / 411 / 414: one predicate, one constant message, a single-source guard, an agreement test.
- specs 408 A-2 and 414 A-2: rows that pin acceptance are retired by the issue's own decision.

**No new ADR** (owner, 2026-10-20). Spec 411 §9 had said Q1 "may want a short decision record". The owner decided Q1
on #3023 and ruled that no record is needed. The change is one `Satisfies` rule and one span predicate. No contract,
scope, error code, persistence shape, Wolverine policy or migration changes.

**Latency budget.** N/A. Registration and address change are control-plane writes, on no §IV leg. The extra work per
write is one ordinal scan of at most 2048 characters.

---

## 1. Premise check on `3442dae0` (an experiment, not an assumption)

The check used a scratch probe: a console app in the session scratchpad, not committed. It references
`CameraCatalog.Domain` and `StreamDistribution.Domain`, and calls `RtspUrl.From`, `StreamSourceUrl.From`,
`RtspUrl.FromStored`, `UrlAuthority.TryReadHost` and `UrlRedaction.ToOrigin`. The two `From`s agreed on every row.

### 1.1 Non-ASCII in a plain host. **The premise holds.**

| Input (code points named) | `From` today | `Uri.Host` | `ToOrigin` |
|---|---|---|---|
| `rtsp://cam`+U+200E+`/p`, `…`+U+202E+`…`, a leading U+200E, `ca`+U+200E+`m` | **accepted** | `cam`: the parser *drops* the character | placeholder |
| `rtsp://cam`+U+FEFF / U+200B / U+3164 / U+2800 / U+115F / U+FFA0 / U+180E / U+034F / U+FE0F / U+00AD / U+2060+`/p` | **accepted** | `cam` + the character | placeholder |
| `rtsp://`+U+2800+`/p`, `rtsp://`+U+3164+`/x`, `rtsp://ü/x` | **accepted** | the character | placeholder |
| `rtsp://cam`+U+200E+`:554/p`, `…`+U+200E (no path), `…?q`, `…#f`, `rtsp://@cam`+U+200E+`/p`, `RTSP://CAM`+U+200E+`/X` | **accepted** | `cam` | placeholder |
| `rtsp://kamera-münchen.local/live`, `rtsp://caméra/x`, `rtsp://カメラ.example/live`, fullwidth `rtsp://ｃａｍ/live`, `rtsp://CAMİ/x` | **accepted** | as written (lower-cased) | placeholder |
| `rtsp://10.0.0.`+U+0666+`/x`, fullwidth `rtsp://１０.0.0.1/x`, `rtsp://cam`+U+0301+`/x` (host read `ca`+U+1E3F), `rtsp://cam`+😀+`/x` | **accepted** | non-ASCII | placeholder |

Because the parser drops U+200E and U+202E and folds U+0301 into a precomposed letter, **the rule must read the
written text**. A check on `Uri.Host`, which is what `HostIsPlainAscii` does, would miss them. The audit is already safe:
`ToOrigin` renders `<unparseable url>` for every row (`HostIsPlainAscii`, `AuthorityWasWrittenAsAscii`). What remains
is that the stored string is not the host every reader dials, and that #3016 shows it verbatim.

### 1.2 Already refused today (pinned unchanged, older message wins)

| Input | Message today |
|---|---|
| `rtsp://cam`+U+00A0, `rtsp://cam`+U+00A0+`/p`, `rtsp://cam`+U+3000+`/x`, `rtsp://cam`+U+2028+`/x` | control or whitespace (spec 408) |
| `rtsp://`+U+200E+`/p`, `rtsp://`+U+00AD+`/x` | empty host (spec 392) |
| `rtsp://cam`+U+200E+`:/x` | empty port (spec 414) |
| `rtsp://[fe80::1%25ü]/s`, `rtsp://[fe80::1%25eth`+U+200E+`]/s`, `rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live` | zone charset (spec 411) |
| `rtsp://[::1]`+U+200E+`/s` | text after `]` (spec 392) |
| `rtsp://`+U+200E+`@cam/p`, `rtsp://ü@cam/p`, `rtsp://`+U+200E+`@[::1]/s`, `rtsp://cam:554`+U+200E+`/x`, `rtsp://cam:٥٥٤/x`, `rtsp://[::1`+U+200E+`]/s`, `rtsp://`+U+200E+`[::1]/s`, `rtsp://caf%C3%A9/x`, `rtsp://c%41m/x`, `rtsp://ca!m/x` (and every other ASCII punctuation the probe tried), `rtsp://cam`+U+DC00 (lone surrogate) | userinfo (spec 001: the parser cannot read it) |

So **bracketed hosts, userinfo and ports already refuse non-ASCII**, and nothing is left for this spec there. Only
the plain host is open.

### 1.3 Unaffected and pinned: `rtsp://xn--kamera-mnchen-wob.local/live`, `rtsp://xn--caf-dma/x`, `rtsp://cam./x`, `rtsp://cam_1/x`, `rtsp://0/x`, `rtsp://CAM:554/S`, and non-ASCII in a path, query or fragment (`rtsp://cam/li`+U+3164+`ve`, `rtsp://cam/li`+U+202E+`ve`, `rtsp://cam/x?q=ü`, `rtsp://cam/x#ü`), which Q2 leaves alone.

### 1.4 Blast radius, measured by counterfactual

The rule (plan §3) was applied to both `From`s in the worktree, and then reverted (`git checkout -- src`). Results:
- **Green:** `Shared.Kernel.Tests` (17257), `AuditObservability.Application.Tests` (1133), `ScenarioSimulator.Tests`
  (431) and `Architecture.Tests` (2696). Architecture.Tests includes every fuzz floor of
  `CameraUrlZoneDelimiterFuzzAdversarialTests`, so **no corpus row is needed**.
- **Red:** 12 assertions in 5 files, all listed in plan §8. The owner authorised exactly these edits on 2026-10-20.

No non-ASCII host appears in `tests/Integration.Tests`, `e2e`, `apps`, `deploy`, the simulator or any seed.

### 1.5 Stored rows

`FromStored` and `EnsureStoredInvariants` are not touched. Both EF converters (`CameraConfiguration.cs:99`,
`StreamConfiguration.cs:74`) use `FromStored`, so every row in §6 "new" still loads. The only `From` callers are:
- `RegisterCameraRequest.cs:19`
- `CameraEndpoints.cs:323`
- `ProvisionStreamCommandHandler.cs:33`
- `RepointStreamCommandHandler.cs:108`

A rename (`PATCH` without `rtspUrl`) does not re-validate the address.

---

## 2. User stories

### US1 (P1): A host with a non-ASCII or invisible character is refused where it is written

As an **operator or integrator registering or correcting a camera**, I want `rtsp://cam`+U+200E+`/p` or
`rtsp://kamera-münchen.local/live` refused, with a message that tells me to write the host in ASCII (an
internationalised name as its `xn--` form). That way, no address is stored whose displayed host differs from the host
the readers dial.

**Independent test:**
- `RtspUrl.From` and `StreamSourceUrl.From` throw `ArgumentException` with the FR-001 message for every row of
  §6 "new".
- `POST /cameras` with `rtsp://kamera-münchen.local/live` answers 400 `CAMERA_INVALID_REQUEST`.
- `POST /cameras` with `rtsp://xn--kamera-mnchen-wob.local/live` answers 201.

### Stored rows (characterisation, not a story)

Rows stored with a non-ASCII host are read through `FromStored`, unchanged. `GET /cameras` and the stream reconciler
keep reading them.

---

## 3. Acceptance scenarios (Gherkin)

```gherkin
Feature: A camera address names its host in ASCII

  Background:
    Given an operator token with scope "sse.cameras.write" and membership in fab "munich"

  # happy path (unchanged)
  Scenario Outline: ASCII hosts, A-labels and non-ASCII paths are still accepted
    When the operator registers a camera with address "<address>"
    Then the response is 201 Created
    Examples:
      | address                                   |
      | rtsp://10.0.0.66:554/live                 |
      | rtsp://xn--kamera-mnchen-wob.local/live   |
      | rtsp://cam_1/x                            |
      | rtsp://cam/x?q=ü                          |

  # US1 bad request (new)
  Scenario Outline: A plain host holding a non-ASCII character is refused
    When the operator registers a camera with address "<address>"
    Then the response is 400 with title "CAMERA_INVALID_REQUEST" and the detail is the rendered FR-001 text
    And the body does not contain "<secret>"
    And no camera is stored and no CameraRegisteredV1 is published
    Examples:
      | address                                    | secret       |
      | rtsp://kamera-münchen.local/live           | münchen      |
      | rtsp://cam<U+200E>SECRET/p                 | SECRET       |
      | rtsp://SECRET<U+3164>/p                    | SECRET       |
      | rtsp://SECRET<U+2800>:554/p                | SECRET       |

  # older messages win
  Scenario Outline: An older rule keeps its message
    When the operator registers a camera with address "<address>"
    Then the response is 400 and the detail is the <older> text
    Examples:
      | address                     | older                       |
      | rtsp://cam<U+00A0>/p        | control or whitespace (408) |
      | rtsp://<U+200E>/p           | empty host (392)            |
      | rtsp://cam<U+200E>:/x       | empty port (414)            |
      | rtsp://[fe80::1%25ü]/s      | zone charset (411)          |
      | rtsp://ü@cam/p              | user:password@ (001)        |

  # conflict ordering (unchanged): the address is parsed before the version is checked
  Scenario: A refused address on PATCH answers 400, not 412, and the camera is unchanged
    Given a camera "line-3-inlet" at "rtsp://10.0.0.66:554/live" at version 2
    When the operator patches its address to "rtsp://kamera-münchen.local/live" with If-Match version 1
    Then the response is 400 "CAMERA_INVALID_REQUEST"
    And the camera's address is still "rtsp://10.0.0.66:554/live" at version 2

  # auth (unchanged)
  Scenario: A caller without the write scope is refused before the address is examined
    Given a token without scope "sse.cameras.write"
    When it registers a camera with address "rtsp://kamera-münchen.local/live"
    Then the response is 403 and the body does not contain the FR-001 text

  # stored rows (characterisation)
  Scenario: A camera stored before this change with a non-ASCII host is still read and renamed
    Given a cameras row whose rtsp_url is "rtsp://kamera-münchen.local/live", inserted directly
    When the operator lists the fab's cameras, reads that camera and renames it
    Then the reads answer 200 and include the camera, and the rename answers 204

  # audit (unchanged)
  Scenario: The audit renders what it rendered before
    Then ToOrigin of "rtsp://kamera-münchen.local/live" is "<unparseable url>"

  # replay (new outcome; spec 389 mechanism unchanged)
  Scenario: A pre-deploy CameraRegisteredV1 with a non-ASCII host is dead-lettered, not provisioned
    Given a CameraRegisteredV1 for a new camera with address "rtsp://kamera-münchen.local/live"
    When StreamDistribution handles it
    Then the handler throws, no stream is saved and the gateway is not called
    And one Warning names "STREAM_INVALID_RTSP_SOURCE" and does not contain "münchen"
```

---

## 4. Functional requirements

- **FR-001: The written plain host is ASCII (US1).** A new predicate,
  `UrlAuthority.NonAsciiCharacterInsideThePlainHost(string url)`, works on the written text. It reads
  `TextFromTheAuthority(url.TrimStart())`, cuts that at `IndexOfTheTerminator`, and keeps the text after its last `@`.
  It answers **true** when that text is non-empty, does not start with `[`, and is not `Ascii.IsValid`. It works on
  spans only, so it cannot throw.

  `RtspUrl.From` and `StreamSourceUrl.From` refuse it with a new **last** rule, after `WrittenPortIsEmpty`, so every
  input an older rule refuses keeps that rule's message. The message is a constant, is identical in both value
  objects, and never contains any part of the input:

  ```
  must write the host in ASCII: a non-ASCII or invisible character there is shown as written but read differently, or dropped, by the parser. Write an internationalised name in its xn-- form
  ```

  With the rule last, "not ASCII" and `HostIsPlainAscii`'s whitelist (letters, digits, `-`, `.`, `_`) refuse the
  same set: every ASCII non-whitelist character in a plain host is already refused by an earlier rule (§1.2).
- **FR-002: `ToOrigin` and the audit are unchanged.** `UrlRedaction` and `AuditPayloadRedaction` are not edited.
- **FR-003: Stored rows stay readable.** `FromStored` gains no rule. No migration and no rewrite (#2990).
- **FR-004: Replay is bounded and visible**, exactly as in spec 389 FR-006. **Replay note for the PR:** a
  `CameraRegisteredV1` or `CameraAddressChangedV1` published before the deploy whose host is not ASCII dead-letters on
  its first delivery to StreamDistribution (`STREAM_INVALID_RTSP_SOURCE`, one Warning, no part of the address). An
  *older* out-of-order `CameraAddressChangedV1` with such a host now **fails** instead of being ignored, the same
  outcome every other rejected address already has (`An_older_change_whose_address_this_context_rejects_fails_rather_than_being_ignored`).
  The CameraCatalog row is unaffected and readable. The cure is to correct the address with `PATCH /cameras/{id}`.
- **FR-005: Single source.** Only `UrlAuthority.cs` declares the predicate. `RtspUrl.cs` and `StreamSourceUrl.cs`
  each call it. A cross-row agreement test checks that the two `From`s give the same outcome and message on every row.
- **FR-006: No new cross-context reference, state or error code.** The message surfaces as the `detail` of
  CameraCatalog's existing 400 `CAMERA_INVALID_REQUEST`, and inside StreamDistribution's `STREAM_INVALID_RTSP_SOURCE`.
- **FR-007: Stale comments are corrected.** In `UrlAuthority.cs`, the comment on `IsPunctuationOnlyHost`
  ("Deliberately not 'holds a letter or digit', which would refuse internationalised hosts") and the one on
  `ContainsControlOrWhiteSpace` ("format characters such as U+200B stay accepted") stop being true of the address as a
  whole. Each is reworded to say that the ASCII rule (spec 421) refuses those characters in a host. The XML summary of
  both value objects gains the spec 421 sentence.

## 5. Out of scope

- Q2: invisible characters in a path, query or fragment. The owner decided to leave them.
- Item 4: closed as stale.
- Bracketed hosts, userinfo and ports, which already refuse non-ASCII (§1.2).
- #3016 (`CameraDto` serves stored URLs verbatim). #2990 (migrating stored rows).
- A UI-side check in `apps/shared/src/api/cameras.schema.ts`.
- Whether MediaMTX's Go resolver or mDNS resolves U-labels. An A-label does not resolve over mDNS (RFC 6762 §16); this
  is the cost the owner accepted.

## 6. Accept and reject sets (pinned in both value objects)

**Accept (unchanged, characterisation):** `rtsp://10.0.0.66:554/live`, `rtsp://xn--kamera-mnchen-wob.local/live`,
`rtsp://xn--caf-dma/x`, `rtsp://cam./x`, `rtsp://cam_1/x`, `rtsp://a-b/x`, `rtsp://0/x`, `rtsp://CAM:554/S`,
`rtsp://[fe80::1%25eth0]/s`, `rtsp://cam/li`+U+3164+`ve`, `rtsp://cam/li`+U+202E+`ve`, `rtsp://cam/x?q=ü`,
`rtsp://cam/x#ü`.

**Reject with an older message (unchanged, characterisation):** the §1.2 table, row for row, with the message observed
at T001.

**Reject with FR-001 (new, red):**
- Invisible characters, trailing: `rtsp://cam`+U+200E+`/p`, `rtsp://cam`+U+202E+`/p`, `rtsp://cam`+U+FEFF+`/p`,
  `rtsp://cam`+U+200B+`/p`, `rtsp://cam`+U+3164+`/p`, `rtsp://cam`+U+2800+`/p`, `rtsp://cam`+U+115F+`/p`,
  `rtsp://cam`+U+FFA0+`/p`, `rtsp://cam`+U+180E+`/x`, `rtsp://cam`+U+034F+`/x`, `rtsp://cam`+U+FE0F+`/x`,
  `rtsp://cam`+U+00AD+`/x`, `rtsp://cam`+U+2060+`/x`.
- Invisible characters, other positions: `rtsp://`+U+2800+`/p`, `rtsp://`+U+3164+`/x`, `rtsp://`+U+200E+`cam/p`,
  `rtsp://`+U+2800+`cam/p`, `rtsp://ca`+U+200E+`m/p`.
- With a port, no path, a query, a fragment, an empty userinfo or an upper-case scheme: `rtsp://cam`+U+200E+`:554/p`,
  `rtsp://cam`+U+200E, `rtsp://cam`+U+200E+`?q`, `rtsp://cam`+U+200E+`#f`, `rtsp://@cam`+U+200E+`/p`,
  `RTSP://CAM`+U+200E+`/X`.
- Non-ASCII letters: `rtsp://kamera-münchen.local/live`, `rtsp://caméra/x`, `rtsp://ü/x`, `rtsp://カメラ.example/live`,
  `rtsp://ｃａｍ/live`, `rtsp://CAMİ/x`.
- Non-ASCII digits, combining marks and emoji: `rtsp://10.0.0.`+U+0666+`/x`, `rtsp://１０.0.0.1/x`,
  `rtsp://cam`+U+0301+`/x`, `rtsp://cam`+U+D83D U+DE00+`/x`.
- The three rows moved out of existing tests: `rtsp://cam／token＝SECRET` (plan §8 E3), `rtsp://SECRET-MARKER.cаmera-sim:8554/x`
  (Cyrillic а, E4) and `rtsp://bücher.example/kamera` (E5).

**Hydrate through `FromStored` (characterisation):** every FR-001 row.

**`ToOrigin` (characterisation):** every FR-001 row renders `UrlRedaction.Unparseable`, exactly as today.

## 7. Assumptions

- **A-1: the five edits are authorised** (owner, 2026-10-20). Plan §8 lists each one with its before and after.
  **No other existing assertion may change.** One that would is a block, not an edit.
- **A-2: `ToOrigin` is not touched.** It already withholds every FR-001 row.
- **A-3: idempotency replay across the deploy** (as spec 414 A-4). A client that, after the deploy, retries a request
  that succeeded before it with a non-ASCII host gets 400, not the original 201. Accepted.
- **A-4: the predicate reads after `TrimStart`**, like `BracketedHostReadings`. Leading whitespace is refused first by
  spec 408 anyway.
- **A-5: the empty-userinfo reading.** The text after the *last* `@` is the host. Any non-empty userinfo is refused
  earlier by `HasNoUserInfo`, and an empty one (`rtsp://@cam`+U+200E+`/p`) leaves the host as the text after it, so
  the first and last `@` agree on every input that reaches the rule.

## 8. End-to-end test procedure (phase 5)

On a booted Aspire stack (one per machine; stop it before building; check that the AppHost PID started after the last
commit):

1. `POST /cameras` (operator token, fab `munich`) with `{"name":"idn","rtspUrl":"rtsp://kamera-münchen.local/live"}`.
   Expect 400 `CAMERA_INVALID_REQUEST`, with `detail` the rendered FR-001 text.
2. The same with `"rtsp://cam‎/p"` (JSON escape): expect 400, FR-001. With
   `rtsp://xn--kamera-mnchen-wob.local/live`: expect 201.
3. `PATCH` the camera from step 2 to `rtsp://kamera-münchen.local/live`: expect 400, and `GET` shows the address
   unchanged.
4. Insert a `cameras` row with `rtsp://kamera-münchen.local/live` directly (psql). `GET /cameras` and
   `GET /cameras/{id}` answer 200. A rename answers 204.
5. A refused write produces no audit row and no `CameraRegisteredV1`.

Latency is N/A (header).
