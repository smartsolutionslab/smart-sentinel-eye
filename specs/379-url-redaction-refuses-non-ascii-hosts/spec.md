# Spec 379: URL redaction refuses a host that is not plain ASCII

**Feature Branch**: `fix/2989-url-redaction-host-charset` (cut from `origin/develop` at `72e9766a`)

**Created**: 2026-10-16

**Status**: Implemented (fourth pass, D4/D5/D6, folded into FR-001 (b)); CI pending, PR not yet opened. No `[NEEDS CLARIFICATION]` remains: the direction was
delegated by the owner on #2989 (conservative host check, placeholder otherwise). Three judgement
calls are marked **[Assumption]** in §6 for the reviewer to overturn if wanted. One finding widens
the owner's wording: a check on `Uri.Host` alone is defeated by the parser's own normalisation, so
the rule also checks the authority as written (§1, FR-001 (b)).

**Input**: Issue [#2989](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2989),
*UrlRedaction keeps Unicode look-alike delimiters as host text, so `rtsp://cam／token=SECRET`
renders as `rtsp://cam／secret`*. Found by the #2977 adversarial pass (spec 377); widened by the
#2977 security review and the #2960 adversarial pass (issue comments).

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (what `ToOrigin` returns for a non-ASCII
host changes): phase 4a is **red** (ADR-0139). **Security-sensitive** (a redaction bypass reaching
logs and the audit trail): phase 4 includes a `test-adversary` pass and phase 6 includes
`security-reviewer`.

**Spec number.** 379. Checked 2026-10-16 against `origin/develop` (highest: 377), every local and
remote branch head (`git ls-tree <ref> specs/`: 378 on `fix/2960-reconciler-repairs-source-mismatch`,
unmerged, worktree `D:\Github\sse-2960`) and every open PR branch (#2983, #2982, #2981, #2979, #2978,
#2970, #2811): nothing claims 379. Re-check before the PR is opened.

**ADRs.** ADR-0139 / ADR-0144 (phase 4a red; the lane implements, it does not decide), ADR-0105
(`Ensure.That`, unchanged), ADR-0065 (Shared >= 90% coverage), ADR-0036 (smallest change), ADR-0050
(structured logging; the log templates are unchanged). Constitution §III (the helper stays in
`Shared.Kernel`; no context gains a reference). **No new ADR**: the owner delegated the direction;
nothing here changes a contract, a scope, a persistence shape or a template.

**Latency budget.** N/A. `ToOrigin` runs on log and audit paths (CameraCatalog command handler,
StreamDistribution gateway and reconciler, the AuditObservability subscriber's own queue), none on a
§IV leg of the event-to-overlay path. The added work is one scan of the host's characters.

---

## 1. What was found (premise verified by experiment, not assumed)

**Method.** A scratch console project outside the repo
(`%TEMP%\claude\...\scratchpad\probe2989`, `ProjectReference` to the real
`src/Shared.Kernel/SmartSentinelEye.Shared.Kernel.csproj` at `72e9766a`) ran the real
`UrlRedaction.ToOrigin` on .NET runtime **10.0.13** (SDK 10.0.402). "Leaks" means the output
contains `SECRET` compared case-insensitively (`ToOrigin` lower-cases the host).

**Matrix.** 26 code points (the 14 named on #2989 — U+FF0F, U+FF1F, U+FF03, U+FF20, U+FF1A, U+FF06,
U+FF1D, U+2044, U+2215, U+FE6B, U+00AD, U+3002, U+200B, U+2060 — plus U+FF3C, U+FE56, U+FE5F,
U+FF0E, U+FF61, U+200C, U+200D, U+FEFF, U+2216, U+29F8, U+FF05, U+FE68) × 8 shapes × {ASCII,
fullwidth} following delimiter (`=` / `＝`, `:` / `：`) × schemes `rtsp`, `rtsps`, `http`
= **1248 cases; 1014 leak.** The shapes, with `L` the code point and `E`/`C` the variant's equals and
colon:

| Shape | Host part after `scheme://` | ASCII variant | Fullwidth variant |
|---|---|---|---|
| after-direct | `camLSECRET` | leaks | leaks |
| after-kv | `camLtokenESECRET` | placeholder | leaks |
| after-kv-path | `camLtokenESECRET/x` | placeholder | leaks |
| before | `SECRETLcam` | leaks | leaks |
| before-path | `SECRETLcam/x` | leaks | leaks |
| before-port | `SECRETLcam:554/x` | leaks | leaks |
| both | `SECRETLcamLSECRET` | leaks | leaks |
| userinfo | `userCSECRETLcam:554/x` | placeholder | leaks |

The result is **identical for all 26 code points and all three schemes**: 13 leaking combinations
each (26 × 13 × 3 = 1014), 3 non-leaking (26 × 3 × 3 = 234, every one `<unparseable url>`).

**The defect is not a look-alike list.** An exhaustive sweep over every BMP code point from U+0080 to
U+FFFF (surrogates excluded), shapes `cam{c}SECRET/x` and `SECRET{c}cam/x`, for each of the three
schemes: **63,327 (after) and 63,320 (before) of 63,360 code points leak.** The only ones that do not:
the C1 controls U+0080–U+009F and U+FFFE (the parse fails), and, in the "before" shape only, seven
combining marks (U+0307, U+030C, U+0323, U+0326, U+0327, U+032D, U+0331) that compose with the
final `T` under normalisation so that `SECRE` survives but `SECRET` does not. An astral code point
leaks the same way (`rtsp://cam😀SECRET/x` -> `rtsp://cam😀secret`). `Uri` treats a non-ASCII host as
an IRI host and keeps it verbatim (lower-cased) in `Uri.Host`; there is nothing special about
delimiter look-alikes except that they make a paste look like a path.

**The two earlier probes on #2989 are both right.** The leak needs every character between `://`
and the first ASCII delimiter to be host text. An ASCII `=`, `:`, `/`, `?` or `#` after the
look-alike ends or invalidates the host (`cam／token=SECRET` is unparseable; `cam／x/SECRET` renders
`rtsp://cam／x`, no marker, because `SECRET` is in the real path). That is why the #2960 pass saw
U+FF0F etc. "not leak" before ASCII path text, and the #2977 pass saw them leak with fullwidth `＝`.
Which side of the look-alike the secret is on does not matter.

**A check on `Uri.Host` alone is not enough (new finding).** For 10 code points the parser turns a
non-ASCII host into **pure ASCII** before `Uri.Host` is read, so a charset check on `Uri.Host` would
accept the leak:

| Code point | What `Uri` does | `ToOrigin("rtsp://cam{c}SECRET/x")` today |
|---|---|---|
| U+200E, U+200F, U+202A–U+202E (bidi controls) | strips them from the IRI | `rtsp://camsecret` |
| U+212A KELVIN SIGN | case-maps to ASCII `k` | `rtsp://camksecret` |
| U+037E GREEK QUESTION MARK | normalises to ASCII `;` | `rtsp://cam;secret` |
| U+1FEF GREEK VARIA | normalises to ASCII `` ` `` | ``rtsp://cam`secret`` |

The last two leave a character a host charset refuses; the first eight do not. So the rule must also
look at the authority **as the caller wrote it**, before the parser normalised it (FR-001 (b)).
Modelled in the scratch probe, the two-part rule leaves **0** offending outputs over the full BMP
sweep (all three schemes, both shapes), against **126,654** per scheme today (63,327 per shape:
output not the placeholder and either non-ASCII or containing the marker; 20 of them pure ASCII).

**Punycode would not fix it.** `Uri.IdnHost` over the 26 × 3 shapes {`camLSECRET`, `SECRETLcam`,
`camLtoken＝SECRET`}: 36 throw `UriFormatException`, **42 contain the marker**, several as ASCII
delimiters that are not even DNS (`cam=secret`, `cam&token=secret`, `cam%secret`, `cam.secret`
from U+3002 / U+FF0E / U+FF61, `camsecret` from U+00AD / U+200B / U+2060 / U+FEFF,
`xn--camsecret-sn0e` from U+2044). UTS #46 mapping turns look-alikes into the very delimiters they
imitate.

**Which ASCII the parser keeps in a host.** Sweeping U+0000–U+007F in three positions for
`rtsp`, `rtsps`, `http`, `file`: the only non-alphanumeric ASCII characters `Uri.Host` ever retains
(outside an IPv6 literal) are `-`, `.` and `_`. So "the host is ASCII letters, digits, `-`, `.`,
`_`" is exactly "the host is ASCII", and every all-ASCII origin renders as it does today.

**Reach.** `RtspUrl.From` and `StreamSourceUrl.From` accept a look-alike host (`Uri.TryCreate`
succeeds and `Uri.UserInfo` is empty, so even `rtsp://user：SECRET＠cam:554` passes
`HasNoUserInfo`). Callers of `ToOrigin` on `72e9766a`:

| # | Caller | Sink |
|---|---|---|
| C1 | `src/CameraCatalog/Application/Commands/Handlers/ChangeCameraAddressCommandHandler.cs:64` | log (#2908) |
| C2 | `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs:51` | log (#2908) |
| C3 | `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs:67` | log (#2908) |
| C4 | `src/StreamDistribution/Infrastructure/Reconciler/MediaMtxReconciler.cs:224` | log (#2908) |
| C5 | `src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs:87` | audit payload (#2977) |
| C6 | `src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs:117` | `DetailChanged` (#2977) |
| C7 | `MediaMtxSourceRepair.cs:68-69, 78-79` on the **unmerged** `fix/2960-...` branch | repair log (#2960), renders the MediaMTX-reported source, never validated |

No caller needs to change: each inherits the fix.

**Tests that pin `ToOrigin` output** (must stay green **unmodified**):
`tests/Shared.Kernel.Tests/UrlRedactionTests.cs` (all rows ASCII; unaffected);
`tests/Shared.Kernel.Tests/UrlRedactionAdversarialTests.cs` (one row moves:
`rtsp://kämera.example/SECRET` goes from `rtsp://kämera.example` to the placeholder; its three
assertions — no marker, placeholder-or-bare-origin, idempotent — all still hold);
`tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadRedactionTests.cs`,
`AuditPayloadRedactionAdversarialTests.cs`, `AuditPayloadRedactionPostFixAdversarialTests.cs`
(contains the same `kämera` row with a no-marker assertion only; `DetailChanged_edges` rows are all
ASCII); `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayLogRedactionTests.cs`,
`tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressLogRedactionTests.cs`,
`tests/Integration.Tests/CameraCatalog/CameraAddressAuditIntegrationTests.cs` and the reconciler pass
characterisation (all ASCII inputs). A scratch model of the rule over every one of those inputs
moved only the `kämera` row.

**A weakness found in the existing sink tests, not fixed here:** the two #2908 log-redaction test
classes assert `!Message.Contains("SECRET", StringComparison.Ordinal)`. `ToOrigin` lower-cases the
host, so they would pass with this very leak in the log. The new sink tests compare
case-insensitively; the old ones are left unmodified (forbidden to edit), recorded here.

## 2. User story

### US1 (P1): a redacted camera address never carries text past a plain ASCII host

As a fab security officer, I want every log line and audit row that names a camera address by its
origin to show either a plain ASCII `scheme://host[:port]` or `<unparseable url>`, so that a
credential pasted next to a Unicode character that `System.Uri` accepts as host text (a fullwidth
solidus from a CJK keyboard, a soft hyphen from a word processor) cannot survive redaction.

**Why P1 / independently shippable:** one check in one function in `Shared.Kernel`; every sink
inherits it; observable in a unit test per sink and in the audit row of a real registration.

## 3. Acceptance scenarios

```gherkin
Feature: an origin is shown only when its host is plain ASCII

  # happy path: ordinary origins are unchanged
  Scenario Outline: an all-ASCII origin renders exactly as before
    When ToOrigin is given "<input>"
    Then it returns "<origin>"
    Examples:
      | input                          | origin                         |
      | rtsp://10.0.5.5/h264           | rtsp://10.0.5.5                |
      | rtsp://CAM.Example/x           | rtsp://cam.example             |
      | rtsp://cam:8554/x?y=z#frag     | rtsp://cam:8554                |
      | rtsp://[fe80::1%25eth0]/x      | rtsp://[fe80::1]               |
      | rtsp://[FD00::ABCD]/x          | rtsp://[fd00::abcd]            |
      | rtsp://cam_1:8554/x            | rtsp://cam_1:8554              |
      | rtsp://xn--bcher-kva.example/x | rtsp://xn--bcher-kva.example   |
      | rtsp://127.1/x                 | rtsp://127.1                   |
      | http://0x7f000001/x            | http://127.0.0.1               |
      | rtsp://cam./x                  | rtsp://cam.                    |

  # the defect: a look-alike delimiter on either side of the secret
  Scenario Outline: a host carrying a non-ASCII character renders the placeholder
    When ToOrigin is given "<input>"
    Then it returns "<unparseable url>"
    And the result does not contain "secret" in any case
    Examples:
      | input                              |
      | rtsp://cam／token＝SECRET           |
      | rtsp://SECRET＠cam/x                |
      | rtsp://user：SECRET＠cam:554        |
      | rtsps://cam⁄live？token＝SECRET＆x＝y |
      | http://ca{U+00AD}mSECRET/x         |
      | rtsp://cam😀SECRET/x               |
      | rtsp://cam{U+200E}SECRET/x           |
      | rtsp://cam{U+212A}SECRET/x           |
      | rtsp://user：pass@cam/x            |
    # {U+XXXX} stands for that one invisible or confusable character, not literal text

  Scenario: a non-ASCII character after the authority does not affect the origin
    When ToOrigin is given "rtsp://cam/x／SECRET"
    Then it returns "rtsp://cam"

  # IDN decision
  Scenario: a Unicode IDN host renders the placeholder, not its punycode
    When ToOrigin is given "rtsp://bücher.example/x"
    Then it returns "<unparseable url>"

  # sinks
  Scenario: a look-alike address is audited as the placeholder
    When CameraRegisteredV1 arrives with Url "rtsp://cam／token＝SECRET"
    Then the stored payload's "Url" is "<unparseable url>"
    And the stored payload and the AuditRowDto payload contain no "secret" in any case

  Scenario: two look-alike addresses on one camera read as a detail change
    When CameraAddressChangedV1 arrives with PreviousUrl "rtsp://cam／a＝SECRET1" and Url "rtsp://cam／a＝SECRET2"
    Then both URL fields are "<unparseable url>" and "DetailChanged" is true

  Scenario: a look-alike source is logged as the placeholder and sent to MediaMTX in full
    When the gateway registers a path with source "rtsp://cam／token＝SECRET"
    Then the log line reads "Registered MediaMTX path <path> -> <unparseable url>."
    And no log entry contains "secret" in any case
    And the body sent to MediaMTX carries the source unchanged

  Scenario: a look-alike camera address change is logged as the placeholder
    When an admin changes a camera's address to "rtsp://cam／token＝SECRET"
    Then the change succeeds and stores the full address
    And the log line reads "Changed camera <id> address to <unparseable url>."

  # bad request / conflict: unchanged, restated so the reviewer sees they were considered
  Scenario: what RtspUrl and StreamSourceUrl accept is unchanged
    Then registering "rtsp://cam／token＝SECRET" still succeeds (not refused by this spec)

  # auth: unchanged
  Scenario: no scope or authorization changes
    Then no endpoint, scope or RequireScope call is touched
```

## 4. Functional requirements

- **FR-001** `UrlRedaction.ToOrigin` returns `UrlRedaction.Unparseable` unless **both** hold:
  (a) every character of `Uri.Host` is an ASCII letter, ASCII digit, `-`, `.` or `_` (plus `[`, `]`,
  `:` when `HostNameType` is `IPv6`); and (b) the input is all ASCII, or it is in the strict
  form `scheme://` (a scheme of ASCII letters, digits, `+`, `.`, `-` immediately followed by `://`)
  and the authority as written — the text after that `://` up to the next `/`, `?`, `#` or backslash (or
  the end) — is non-empty and all ASCII (an empty written authority, as in `file:////host`, fails (b)); a non-ASCII input in any other form (backslash or single-slash markers,
  or no slashes at all as in `mailto:`) fails (b). `Uri.TryCreate` throwing is treated as
  unparseable (placeholder). A non-ASCII character in the
  path, query or fragment therefore does not affect the origin; one in the userinfo, host or port
  does.
- **FR-002** For every input whose host passes FR-001, the output is byte-identical to today's:
  `scheme://host[:port]`, lower-case as `Uri` renders it, default port omitted, IPv6 zone identifier
  dropped. The placeholder constant and the null guard are unchanged.
- **FR-003** A Unicode IDN host (`bücher.example`) renders the placeholder. An IDN already written in
  its ASCII `xn--` form passes FR-001 and renders as written (lower-cased). `Uri.IdnHost` is not used.
- **FR-004** No caller of `ToOrigin` changes. `RtspUrl`, `StreamSourceUrl`, the log templates, the
  audit register and the `DetailChanged` rule (spec 377 FR-003) are unchanged; `DetailChanged`
  follows mechanically from the new outputs.
- **FR-005** Every red test in the plan is observed failing on unchanged production code, on an
  assertion, and the failure is quoted in the PR (ADR-0139).

## 5. Independent end-to-end test procedure

1. In-process (no Docker): `dotnet test tests/Shared.Kernel.Tests -c Release --filter
   "FullyQualifiedName~UrlRedaction"`: the 1014-row matrix, the BMP sweep and the IDN rows go red
   before and green after; every pre-existing row green throughout.
2. In-process, per sink: the three new sink test classes (plan §4) red before, green after.
3. Against the Aspire stack (phase 5, only if no other stack is running on the machine): register a
   camera with `rtsp://cam／token＝SECRET-<guid>`, then read the CameraCatalog and StreamDistribution
   structured logs and `GET /audit/{id}` for the registration: the address shows as
   `<unparseable url>` and the marker is absent in every case. If a stack is already running, step 2
   stands as the evidence and the PR says so.

## 6. Judgement calls

- **[Assumption] `_` is allowed.** The owner's wording was "letters, digits, hyphen, dot". `Uri`
  keeps `_` in a host (`HostNameType.Dns`), it is ASCII and no URL delimiter, so admitting it opens
  no path past the host. Refusing it would turn an all-ASCII origin such as `rtsp://cam_1:8554` into
  the placeholder, which contradicts the issue's "do not change how ordinary origins render" and
  would cost diagnostics for Docker Compose-style service names. Today no camera host in the repo has
  one (no `_` in any URL host under `src/` or `tests/`; Aspire resource names and Kubernetes Service
  names cannot contain `_`), so either choice is safe now; this one keeps FR-002 exact. Overturning it
  is a one-character change plus one test row.
- **[Assumption] Unicode IDN renders the placeholder, not punycode.** Punycode (`Uri.IdnHost`)
  applies UTS #46 mapping, which maps look-alikes onto the ASCII they imitate and keeps ASCII runs
  verbatim; on this runtime it carried the marker in 42 of 78 look-alike probes (§1). Distinguishing
  a "legitimate" IDN from a look-alike would need its own IDNA validation policy, which is a
  decision, not a fix. The cost: an operator who names a camera by a Unicode IDN sees
  `<unparseable url>` in logs and audit rows, and an address change between two Unicode IDN hosts
  records `DetailChanged: true` with both origins as the placeholder (the same outcome spec 377
  already gives two unparseable inputs). IP cameras on a fab network are addressed by IP or ASCII
  name; none in the repo uses an IDN.
- **[Assumption] A non-ASCII userinfo also yields the placeholder.** FR-001 (b) looks at the whole
  authority, so `rtsp://user：pass@cam/x` goes from `rtsp://cam` (no leak today) to the placeholder.
  Locating the host inside the written authority would mean re-implementing the parser's userinfo
  and port rules; refusing the whole authority is the fail-closed reading of the owner's
  "conservative", and costs nothing real (`RtspUrl` refuses userinfo, and no camera in the repo has
  a non-ASCII one).
- **Not fixed, recorded:** `RtspUrl` / `StreamSourceUrl` still accept a non-ASCII host; such a camera
  cannot connect, but the full URL is stored. Rejecting it at registration is a validation change
  with its own compatibility question, outside #2989. Existing log lines and audit rows already
  written with a leaked host are not rewritten (spec 377 FR-009's follow-up covers stored rows).

## 7. Out of scope

Changing what `RtspUrl` / `StreamSourceUrl` accept; rewriting stored rows, blobs or logs; the
ScenarioSimulator log sites (#2976); editing the existing case-sensitive assertions in the #2908
sink tests; the C7 repair log line beyond inheriting the fix (spec 378 owns that file).

## 8. Third pass: findings D4 and D5

- **D4 (authority located differently from the parser).** `System.Uri` accepts two backslashes, slash-backslash, backslash-slash and,
  for schemes such as `mailto`, no slashes as the authority marker, so locating the authority at the
  first `//` of the raw text let `rtsp:` + two backslashes + `cam<U+200E>SECRET/x//y` render `rtsp://camsecret`. FR-001 (b)
  now requires, for a non-ASCII input, the strict `scheme://` form; anything else is the placeholder.
  All-ASCII inputs are unchanged (`rtsp:` + ONE backslash + `cam:554/x` already gave the placeholder (with two backslashes it gives `rtsp://cam:554`), `http:/\cam/x`
  gives `http://cam`).
- **D5 (BCL defect).** `file://` followed by U+200E (alone, or before `/x`), and `file:` with two backslashes then U+200E, make
  `Uri.TryCreate` throw `IndexOutOfRangeException` (U+200E/200F/202A-202E after the authority
  marker). The call is wrapped in a deliberate, typed trust-boundary catch of exactly that exception
  type, returning the placeholder: an exception would fail an audit write and retry-loop the message.
- **Accepted over-refusals (listed by the adversary):** a non-ASCII IPv6 zone identifier and the UNC
  form (two backslashes, `kämera`, backslash, `x`) now render the placeholder. Fail-closed, accepted.
- **D6 (empty written authority, `file:` UNC, fourth pass).** For `file:////cam<U+200E>SECRET/x` the text between `://` and the first terminator is EMPTY, which passed the written-ASCII check vacuously, while `System.Uri` reads the host after the extra separators and renders `file://secret`. FR-001 (b) now refuses an empty written authority for non-ASCII input; all-ASCII input is unchanged. Evidence: `UrlRedactionFileAuthorityTests` 768 red, green after the change; without the refusal the same 768 rows fail. A sweep of `file`, `FILE`, `rtsp`, `rtsps`, `http` x every 0-3 extra slash/backslash combination x 15 stripped or mapped code points (54,000 inputs) gave 0 leaking outputs (3,072 leaks with the refusal reverted); 64,818 ASCII inputs (generated plus the URL literals in `src`) render identically to develop.
- Evidence: `UrlRedactionAuthorityLocationTests` 792 red, green after the change. Counterfactuals:
  without the strict-form requirement 624 fail; without the typed catch 168 fail (624 + 168 = 792).
