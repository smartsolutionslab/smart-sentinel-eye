# Spec 381: URL redaction refuses a userinfo the parser reads as host and port

**Feature Branch**: `fix/2993-url-redaction-userinfo-as-host` (cut from `origin/develop` at `860a7542`,
which contains spec 379's `UrlRedaction` rule)

**Created**: 2026-10-16

**Status**: Implemented. Phase 4a red observed (421 failures at 89c871aa); T010 green (9cb12308); adversarial pass and review findings applied; the PR and CI tasks (T030-T032) are open. No `[NEEDS CLARIFICATION]` remains: the direction was
delegated by the owner on #2993 ("refuse, return the placeholder"). One finding **narrows** the owner's
suggested form, because that form changes two existing exact-output rows (§1.4, FR-001); two judgement
calls are marked **[Assumption]** in §6.

**Input**: Issue [#2993](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2993),
*UrlRedaction.ToOrigin renders 'admin:12345' as the origin when a password's unencoded #, ? or / ends
the authority early*. Found by the #2960 security review (spec 378); widened by the #2989 security
review (issue comment: the secret also comes out as the **host**, and IPv4 shorthand is normalised).

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (what `ToOrigin` returns for an input with an
`@` after the authority changes): phase 4a is **red** (ADR-0139). **Security-sensitive** (a credential
reaching logs and the audit trail): phase 4 includes a `test-adversary` pass and phase 6 includes
`security-reviewer`.

**Spec number.** 381. Checked 2026-10-16 against `origin/develop` (highest: 379), every local and remote
branch head (`git ls-tree <ref> specs/`: 380 on `refactor/2971-eventingestion-api-s107`, unmerged,
worktree `D:\Github\sse-2971`) and every open PR branch (#2983, #2982, #2981, #2979, #2978, #2970,
#2811): nothing claims 381. Re-check before the PR is opened.

**ADRs.** ADR-0139 / ADR-0144 (phase 4a red; the lane implements, it does not decide), ADR-0105
(`Ensure.That`, unchanged), ADR-0065 (Shared >= 90% coverage), ADR-0084 (metrics, advisory), ADR-0036
(smallest change), ADR-0050 (log templates unchanged). Constitution §III (the helper stays in
`Shared.Kernel`; no context gains a reference). **No new ADR**: the owner delegated the direction;
nothing here changes a contract, a scope, a persistence shape, a template or what any value object
accepts.

**Latency budget.** N/A. `ToOrigin` runs on log and audit paths only (spec 379 §1, callers C1-C7, all
now merged), none on a §IV leg. The added work is one ordinal scan of the raw string, before the parse.

---

## 1. What was found (premise verified by experiment, not assumed)

**Method.** A scratch console project outside the repo
(`%TEMP%\claude\...\scratchpad\probe2993`, `ProjectReference` to the real
`src/Shared.Kernel/SmartSentinelEye.Shared.Kernel.csproj` at `860a7542`) ran the real
`UrlRedaction.ToOrigin` on .NET runtime **10.0.13** (SDK 10.0.402). A row **leaks** when the output is
neither the placeholder nor the origin of the intended host — `ToOrigin("{scheme}://{host}/")` for the
text after the last `@`. That catches a secret rendered in a changed form (`12345` -> `0.0.48.57`), which
a substring check would miss.

### 1.1 The defect, reproduced

| Input | `ToOrigin` today | `HasNoUserInfo` (RtspUrl / StreamSourceUrl) |
|---|---|---|
| `rtsp://admin:12345#@cam.local/s` | `rtsp://admin:12345` | accepts |
| `rtsp://admin:4711?@cam.local/s` | `rtsp://admin:4711` | accepts |
| `rtsp://admin:8080/x@cam.local/s` | `rtsp://admin:8080` | accepts |
| `rtsp://operator:2468#!@10.0.0.5:554/h264` | `rtsp://operator:2468` | accepts |
| `rtsp://12345#@cam/s` | `rtsp://12345` (secret as **host**) | accepts |
| `rtsp://0x7f000001#@cam/s` | `rtsp://0x7f000001` | accepts |
| `https://1#@cam/s` | `https://0.0.0.1` (IPv4 shorthand) | accepts |
| `http://12345#@cam.local/s` | `http://0.0.48.57` (12345 = 0x3039, recoverable) | accepts |
| `rtsp://SECRET#@cam/s` | `rtsp://secret` (alphabetic secret as host) | accepts |
| `rtsp://a@admin:12345#@cam/s` | `rtsp://admin:12345` (two `@`) | refuses |
| `rtsp:\\admin:12345#@cam/s` (two backslashes) | `rtsp://admin:12345` | accepts |
| `http:/\admin:12345#@cam/s` | `http://admin:12345` | accepts |
| `rtsp://admin:12345#@cam/s#frag@x` | `rtsp://admin:12345` | accepts |

Mechanism: an unencoded `#`, `?` or `/` ends the authority, so the parser reads everything before it as
`host[:port]` and the real userinfo/host as path, query or fragment. `Uri.UserInfo` is then empty.

### 1.2 The leak matrix (which shapes leak today)

**Curated matrix** (the one the red test generates; plan §4.1 U1): schemes `rtsp`, `rtsps`, `http`,
`https` × 4 userinfo shapes × 5 secrets × 4 delimiters × 3 hosts = **960 rows; 396 leak**, the other 564
already render the placeholder, **0** render the correct origin.

| Shape (`{s}` secret, `{d}` delimiter) | `12345` / `4711` / `1` | `0x7f000001` / `abc` |
|---|---|---|
| `admin:{s}{d}@` | leaks for `#` `?` `/` | placeholder (non-numeric port) |
| `operator:{s}{d}!@` | leaks for `#` `?` `/` | placeholder |
| `{s}{d}@` (secret as host) | leaks for `#` `?` `/` | **leaks** for `#` `?` `/` |
| `:{s}{d}@` (empty user) | placeholder (empty host) | placeholder |

Identical for all four schemes (99 each). A backslash delimiter never leaks on this runtime (the parser
refuses the result). Exact predicate: *leaks iff delimiter ≠ `\` and shape ≠ empty-user and (shape =
secret-as-host or the secret is all decimal digits)*.

**Broad sweep**: 4 schemes × 6 authority markers (`//`, two backslashes, `/\`, `\/`, none, `///`) × 5
users × 6 passwords (none, empty, `12345`, `1`, `abc`, `65536`) × 140 delimiters (every U+0000-U+007F,
`%23 %3F %2F %5C %40 %3B %20`, and fullwidth `＃ ？ ／ ＼ ＠`) × 3 positions × 4 hosts × 2 tails =
**2,419,200 inputs; 19,968 leak**, and **only** for the three delimiters `#`, `?`, `/` (6,656 each).
No percent-encoded form leaks (the authority is not ended), no control character, no `;`, no space, no
fullwidth form (spec 379 already refuses a non-ASCII authority). That last claim holds only under this
section's own leak definition (output is neither the placeholder nor the real origin): the address
`rtsp://admin:12345#＠cam/s` with U+FF20 still renders `rtsp://admin:12345`, which is what
`rtsp://admin:12345#cam` renders too, so nothing can tell a host from a credential there, and U+FF20 is not a
delimiter to any parser; the row is pinned in `UrlRedactionUserInfoAdversarialTests`.

### 1.3 What `ToOrigin` does today with a proper userinfo

`rtsp://user:pass@cam/x` -> `rtsp://cam`: the parser consumes `user:pass` as userinfo and the userinfo is
dropped. **No leak; must stay unchanged** (pinned by `UrlRedactionTests` and by
`MediaMtxSourceRepairAdversarialTests` for `rtsp://admin:SECRET@cam/x?token=SECRET` -> `rtsp://cam`).
Percent-encoded delimiters in a password (`admin:12345%23@cam`, `%2F`, `%3F`, `%5C`, `%40`) also render
`rtsp://cam` and stay unchanged.

### 1.4 Candidate rules, measured

| Rule | Residual leaks, curated / broad | Existing rows whose output changes | Verdict |
|---|---|---|---|
| A1: any `@` in the input | 0 / 0 | **13** Shared.Kernel rows, incl. the exact-output `UrlRedactionTests` row `rtsp://user:pass@cam/x` -> `rtsp://cam`, and the exact-output `MediaMtxSourceRepairAdversarialTests` row | **blocked** (changes a pinned exact output) |
| A2: `@` after the first `://` (the owner's "simplest conservative form") | 0 / **14,976** (`rtsp:\\admin#@cam/s` -> `rtsp://admin`) | 13, same as A1 | blocked twice: changes pinned rows **and** misses backslash markers |
| C: `@` present and `Uri.UserInfo` empty | 0 / 0 | 2 | rejected: misses `rtsp://a@admin:12345#@cam/s` (two `@`, `UserInfo` = `a`); re-calls a parser that throws (spec 379 D5) |
| B2: text before the last `@` is not RFC 3986 userinfo | 0 / 0 | 2 | works, but wider than B (also refuses `rtsp://admin:12345 @cam/s` and `https://reader:{Secret}@…`, neither of which leaks) |
| **B (chosen): an `@` at or after the first `/ ? # \` past the authority marker** | **0 / 0** | **2** (one input, `rtsp://cam/x@SECRET`, in two theories; both still pass) | **chosen** |

**Counterfactuals on B** (each part is load-bearing or honestly not):

| B without... | Residual leaks (broad) | Existing rows changed |
|---|---|---|
| `#` as a terminator | 6,656 | 2 |
| `?` as a terminator | 6,656 | 2 |
| `/` as a terminator | 6,656 | 0 |
| skipping the marker run (`//`, `\\`, `/\`, `\/`) after `scheme:` | 0 | **13** (degenerates to A1) |
| parser-style marker location (only after a literal `://`) | **14,976** | 2 |
| `\` as a terminator | **0** — not load-bearing on 10.0.13 | 2 |

`\` is kept anyway (**[Assumption]**, §6): it is already in `UrlRedaction.AuthorityTerminators`, the
parser treats it as a path separator for these schemes, and it only adds refusals for inputs with an `@`
after a backslash, all of which render the placeholder today.

**Why nothing narrower works.** Every leak needs an `@` that the parser placed outside its authority (the
text before it was read as `host[:port]`). B refuses exactly the inputs with an `@` after the authority
ends. A narrower rule would have to tell `rtsp://cam/x@y` (no credential) from `rtsp://admin:8080/x@cam/s`
(credential `8080/x`) — the same shape, so it cannot.

### 1.5 Existing tests and real URLs

- **Existing test rows.** All 22,981 string arguments of every `UrlRedaction*Tests` theory (reflected
  out of the built test assembly) were run through B: **one input changes**, `rtsp://cam/x@SECRET`
  (`rtsp://cam` -> placeholder), in `UrlRedactionAdversarialTests.No_part_of_a_credential_survives_...`
  (asserts no marker and placeholder-or-bare-origin: still true) and `..._is_idempotent_...` (placeholder
  maps to itself: still true). The same input is in `AuditPayloadRedactionPostFixAdversarialTests`
  `Spellings` (no-marker assertions only: still true). **No existing assertion changes; no existing test
  file is edited.**
- **Real URLs.** 903 distinct URL literals in `src/`, `tests/`, `apps/`, `docs/`, `specs/`, `deploy/`
  (quoted literals plus bare `rtsp|http(s)://` in Markdown): 19 contain `@`, every one a proper
  `user:pass@host` (amqp connection strings, userinfo test fixtures) except the adversarial
  `rtsp://cam/x@SECRET`. **No camera URL, AppHost resource or simulator URL has an `@` after its
  authority.** Under B, one literal changes (`rtsp://cam/x@SECRET`).
- **Idempotence.** 0 non-idempotent outputs over the 2.42M sweep. Structurally: neither the placeholder
  nor a rendered origin contains `@`, so B never fires on `ToOrigin`'s own output.
- **Inputs without `@`** are untouched by construction (B tests for an `@` first in effect: no `@`, no
  refusal), so every `@`-free all-ASCII origin — `rtsp://cam:554/stream`, `rtsp://10.0.5.5/h264` —
  renders byte for byte as before.

### 1.6 Reach

`RtspUrl.From` and `StreamSourceUrl.From` accept every leaking `rtsp://` row above (their
`HasNoUserInfo` reads the empty `Uri.UserInfo`), so an operator can register such a camera. Sinks (all
merged): the #2908 log lines (CameraCatalog change-address handler; StreamDistribution gateway register
and re-point; reconciler re-add), the #2960 repair line (renders the MediaMTX-reported source, never
validated), and the #2977 audit payload plus its `DetailChanged` rule. No caller changes; each inherits
the fix.

## 2. User story

### US1 (P1): a redacted camera address never shows a userinfo the parser misread as host or port

As a fab security officer, I want every log line and audit row that names a camera address by its origin
to show either the real `scheme://host[:port]` or `<unparseable url>`, so that a camera password
containing an unencoded `#`, `?` or `/` (common in generated passwords) cannot come out as the "host" or
"port" of the origin.

**Why P1 / independently shippable:** one check in one function in `Shared.Kernel`; every sink inherits
it; observable in a unit test per sink and in the audit row of a real registration.

## 3. Acceptance scenarios

```gherkin
Feature: an origin is shown only when no @ follows the authority

  # happy path: ordinary origins and proper userinfo are unchanged
  Scenario Outline: an origin renders exactly as before
    When ToOrigin is given "<input>"
    Then it returns "<origin>"
    Examples:
      | input                                         | origin                  |
      | rtsp://cam:554/stream                         | rtsp://cam:554          |
      | rtsp://10.0.5.5/h264                          | rtsp://10.0.5.5         |
      | rtsp://user:pass@cam/x                        | rtsp://cam              |
      | rtsp://admin:SECRET@cam/x?token=SECRET        | rtsp://cam              |
      | rtsp://user:secret@camera-sim:8554/station-4  | rtsp://camera-sim:8554  |
      | rtsp://admin:12345%23@cam/s                   | rtsp://cam              |
      | rtsp://admin%40x:12345@cam/s                  | rtsp://cam              |
      | rtsp://cam/x%40y                              | rtsp://cam              |

  # the defect: port position, host position, normalised forms
  Scenario Outline: a userinfo cut short by # ? or / renders the placeholder
    When ToOrigin is given "<input>"
    Then it returns "<unparseable url>"
    Examples:
      | input                                    |
      | rtsp://admin:12345#@cam.local/s          |
      | rtsp://admin:4711?@cam.local/s           |
      | rtsp://admin:8080/x@cam.local/s          |
      | rtsp://operator:2468#!@10.0.0.5:554/h264 |
      | rtsp://12345#@cam/s                      |
      | rtsp://0x7f000001#@cam/s                 |
      | https://1#@cam/s                         |
      | http://12345#@cam.local/s                |
      | rtsp://a@admin:12345#@cam/s              |

  # the cost, accepted: an @ after the authority with no credential
  Scenario: an @ in the path, query or fragment renders the placeholder
    When ToOrigin is given "rtsp://cam:554/x?mail=a@b"
    Then it returns "<unparseable url>"

  # sinks
  Scenario: a cut-short userinfo is audited as the placeholder
    When CameraRegisteredV1 arrives with Url "rtsp://operator:2468#!@10.0.0.5:554/h264"
    Then the stored payload's "Url" is "<unparseable url>"
    And neither the stored payload nor the AuditRowDto payload contains "operator:2468"

  Scenario: two cut-short addresses on one camera read as a detail change
    When CameraAddressChangedV1 arrives with PreviousUrl "rtsp://operator:2468#@cam/s" and Url "rtsp://operator:1357#@cam/s"
    Then both URL fields are "<unparseable url>" and "DetailChanged" is true

  Scenario: a cut-short source is logged as the placeholder and sent to MediaMTX in full
    When the gateway registers or re-points a path with source "rtsp://operator:2468#!@10.0.0.5:554/h264"
    Then the log line names "<unparseable url>" and no entry contains "operator:2468"
    And the body sent to MediaMTX carries the source unchanged

  Scenario: a cut-short source MediaMTX reports is logged as the placeholder by the repair
    When the reconciler repairs a path whose MediaMTX source is "rtsp://operator:2468#@cam/x"
    Then the repair line reads "... source <unparseable url> -> rtsp://10.0.8.1."

  Scenario: a cut-short camera address change is logged as the placeholder and stored in full
    When an admin changes a camera's address to "rtsp://operator:2468#!@10.0.0.5:554/h264"
    Then the change succeeds and stores the full address
    And the log line reads "Changed camera <id> address to <unparseable url>."

  # bad request / conflict: unchanged, restated so the reviewer sees they were considered
  Scenario: what RtspUrl and StreamSourceUrl accept is unchanged
    Then registering "rtsp://admin:12345#@cam.local/s" still succeeds (not refused by this spec)

  # auth: unchanged
  Scenario: no scope or authorization changes
    Then no endpoint, scope or RequireScope call is touched
```

## 4. Functional requirements

- **FR-001** `UrlRedaction.ToOrigin` returns `UrlRedaction.Unparseable` when the raw input has an `@` at
  or after the first `/`, `?`, `#` or `\` that follows the authority start. The authority start is: past
  `scheme:` when the input begins with a scheme (an ASCII letter, then ASCII letters, digits, `+`, `.`,
  `-`, up to the first `:`), else position 0; then past any run of `/` and `\`. This check is **added**
  to the existing ones (spec 379 FR-001 stands unchanged).
- **FR-002** For every input FR-001 does not refuse — in particular every input without an `@`, and every
  input whose only `@` sits inside the authority (`user:pass@host`) — the output is byte-identical to
  today's.
- **FR-003** Idempotence holds: `ToOrigin(ToOrigin(x)) == ToOrigin(x)` for every pinned row.
- **FR-004** No caller of `ToOrigin` changes. `RtspUrl`, `StreamSourceUrl`, the log templates, the audit
  register and the `DetailChanged` rule are unchanged; `DetailChanged` follows mechanically.
- **FR-005** Every red test in the plan is observed failing on unchanged production code, on an
  assertion, and the failure is quoted in the PR (ADR-0139).

## 5. Independent end-to-end test procedure

1. In-process (no Docker): `dotnet test tests/Shared.Kernel.Tests -c Release --filter
   "FullyQualifiedName~UrlRedaction"`: U1 (396), U4 (sweep, `768`), U5 (18), U6 (6) red before and green
   after; U2, U3, U7, U8 and every pre-existing row green throughout.
2. In-process, per sink: the four new sink test classes (plan §4.2) red before, green after; their
   "still sends / still stores" facts green throughout.
3. Against the Aspire stack (phase 5, only if no other stack is running on the machine): change a
   camera's address to `rtsp://operator:2468#!@10.0.0.5:554/h264-<guid>`, then read the CameraCatalog and
   StreamDistribution structured logs and `GET /audit/{id}` for the change: the address shows as
   `<unparseable url>` and `operator:2468` appears nowhere. If a stack is already running, step 2 stands
   as the evidence and the PR says so.

## 6. Judgement calls

- **[Assumption] The owner's simplest form is narrowed.** "Refuse if the text after `://` contains `@`
  at all" turns `rtsp://user:pass@cam/x` (and the repair test's `rtsp://admin:SECRET@cam/x?token=SECRET`)
  from `rtsp://cam` into the placeholder — an edit to two pinned exact-output rows, which this lane may
  not make — and it still leaks for backslash markers (14,976 sweep rows). B closes the same leaks with
  neither cost. Reviewer may overturn by a decision to refuse every userinfo; that is a test edit and
  belongs in its own issue.
- **[Assumption] `\` stays a terminator** although not load-bearing on 10.0.13 (§1.4): reuse of the
  existing `AuthorityTerminators` constant, defence against parser variance across runtimes, and it only
  refuses inputs that render the placeholder today.
- **Accepted over-refusals** (render an origin today, the placeholder after; none occurs in the repo):
  `rtsp://cam/x@y`, `rtsp://cam:554/x?mail=a@b`, `rtsp://cam/x#a@b`, `rtsp://cam:554/x?@`,
  `rtsp://user:pass@cam/x@y`, and a whitespace-padded URL with userinfo (` rtsp://user:pass@cam/x`; the
  leading space defeats the scheme check, and `RtspUrl` refuses it anyway). Cost in the audit: such an
  address shows as `<unparseable url>`; a change between two of them reads `DetailChanged: true`, the
  outcome spec 377 already gives two unparseable inputs.
- **Accepted over-refusal found by the adversarial pass: leading whitespace or control characters before the
  scheme.** A credentialed ` rtsp://user:pass@cam/x` (develop: `rtsp://cam`) renders the placeholder,
  because the leading character defeats scheme detection, so the authority start is taken at position 0 and
  the first slash of the double slash counts as the terminator, with the at sign after it. It fails closed.
  It is unreachable through `RtspUrl` (which requires the text to start with `rtsp://`) and reachable only
  through MediaMTX-observed source text; accepted in writing.
- **Follow-ups filed from this work.** #2998: `DetailChanged` claims "same origin" when both sides redact to
  the placeholder, so a camera host change can be hidden in the audit trail. #2999: `RtspUrl` and
  `StreamSourceUrl` accept exactly the misread credential-bearing forms, which is the owner question below.
- **Owner follow-up question (tracked as #2999, not decided here):** should `RtspUrl.From` /
  `StreamSourceUrl.From` reject an `@` after the authority, so such URLs cannot be stored? That changes
  accepted input (already-stored rows, a migration or tolerance question) and does not cover the
  MediaMTX-reported source, which never passes through them.
- **Recorded, not fixed:** `ToOrigin` is not idempotent for some `mailto:` ASCII inputs
  (`mailto:admin:s3cr@cam.example/s` -> `mailto://cam.example` -> placeholder; #2993 addendum). No secret
  leaks, unreachable through `RtspUrl`; FR-001 does not change it (the `@` precedes the first `/`). It
  contradicts the class remark in `AuditPayloadRedaction`; raise in the PR body as a separate issue.

## 7. Out of scope

Changing what `RtspUrl` / `StreamSourceUrl` accept; rewriting stored rows, blobs or logs; the `mailto:`
idempotence remark; editing any existing test; the ScenarioSimulator log sites (#2976).
