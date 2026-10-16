# Plan 381: URL redaction refuses a userinfo the parser reads as host and port

**Spec**: [spec.md](./spec.md) | **Issue**: #2993 | **ADRs**: 0036, 0050, 0065, 0084, 0105, 0139, 0144

## 1. Bounded context and layers

**`Shared.Kernel` only** for production code: one file, `src/Shared.Kernel/UrlRedaction.cs`. No bounded
context, no Domain/Application/Infrastructure/Api file, no contract, no migration, no AppHost resource.
New test files go in four existing test projects; no test project gains a reference.

**Boundary rules.** Unchanged. `Shared.Kernel` keeps no domain and no I/O; every caller already
references it. NetArchTest rules are untouched. No integration test is added, so no
`tests/Integration.Tests/ci-shards/shard-N.filter` entry is needed.

## 2. Mechanism

One new check, evaluated **first** in `ToOrigin` (before `TryParse`; it reads only the raw string, so it
cannot be affected by the parser throwing or normalising):

```
if (AtSignFollowsTheAuthority(url) || !TryParse(url, out var uri) || ...existing checks...) return Unparseable;
```

**`AtSignFollowsTheAuthority(string url)`** (private static):

1. `start` = 0. If `url` begins with an ASCII letter and every character before its first `:` is a scheme
   character (reuse the existing `IsSchemeCharacter`), `start` = index after that `:`.
2. Advance `start` past any run of `/` and `\` (the authority marker, in every spelling the parser accepts:
   `//`, two backslashes, `/\`, `\/`, none, three or more).
3. `end` = first index of any `AuthorityTerminators` character (`/ ? # \`, the existing constant) at or after
   `start`. If none, return false.
4. Return whether `url` contains `@` at or after `end`.

Spans (`url.AsSpan(...)`, `IndexOfAny`, `Contains('@')`) keep it allocation-free; the engineer picks the
shape within ADR-0084's limits. Nothing else in the method moves: `Ensure.That(url).IsNotNull()`, the
typed `IndexOutOfRangeException` catch, `HostIsPlainAscii`, `AuthorityWasWrittenAsAscii`, the origin
composition, the `Unparseable` constant.

**Why the authority start is located permissively here but strictly in `AuthorityWasWrittenAsAscii`.**
Spec 379's check refuses non-ASCII input in any non-strict form, so strictness there is fail-closed. Here
strictness would be fail-*open*: a check that only looks after a literal `://` misses `rtsp:\\admin:12345#@cam/s`
(14,976 sweep leaks, spec §1.4 CF "parser-style marker location"). Skipping every `/` `\` run after the
scheme matches what the parser accepts; an input the scan mis-locates (leading whitespace, no scheme)
starts at 0, where the `/` of the marker itself is a terminator, so any `@` refuses — the fail-closed side.

**Rejected alternatives** (measured, spec §1.4):

| Option | Why not |
|---|---|
| Any `@` in the input / after `://` (the issue's simplest form) | Changes the pinned exact outputs `rtsp://user:pass@cam/x` -> `rtsp://cam` (`UrlRedactionTests`) and `rtsp://admin:SECRET@cam/x?token=SECRET` -> `rtsp://cam` (`MediaMtxSourceRepairAdversarialTests`): a test edit the lane may not make. The `://` form also leaks 14,976 backslash-marker rows. |
| `@` present and `Uri.UserInfo` empty | Leaks `rtsp://a@admin:12345#@cam/s` -> `rtsp://admin:12345`; re-calls a parser that throws on some inputs (spec 379 D5). |
| Text before the last `@` must be RFC 3986 userinfo | Closes the leaks but refuses more (`rtsp://admin:12345 @cam/s`), for no gain. |
| A `user:digits` shape match | The secret also leaks alphabetic or hex as **host** (`rtsp://SECRET#@cam/s` -> `rtsp://secret`, `0x7f000001`); a shape list is what #2989 showed to be wrong. |
| Re-locating userinfo inside the parsed `Uri` | `Uri` has already discarded the information (empty `UserInfo`); positions in the normalised string do not map back to the input. |
| Rejecting in `RtspUrl` / `StreamSourceUrl` | Out of scope (changes accepted input); and the MediaMTX-reported source never passes through them. Owner follow-up (spec §6). |

## 3. Entities, value objects, invariants

No entity or value object changes. Invariants `ToOrigin` now holds, in addition to spec 379's I0-I3:

- **I4** If the output is not the placeholder, the input has no `@` after the authority: every `@` in it
  precedes the first `/ ? # \` past the marker, so the parser consumed it as the userinfo delimiter and
  `Uri.Host` is the text after the last `@`.
- **I5** For an input with no `@` after the authority, the output equals the pre-change output (FR-002).
- **I6** Idempotence (FR-003): neither the placeholder nor any rendered origin contains `@`, so I4's check
  is a no-op on `ToOrigin`'s own output and spec 379's I3 carries over.

## 4. Tests

All new files. Test naming sentence-style (ADR-0053), Shouldly, no AutoFixture (ADR-0054). Every
existing test file stays **unmodified**.

### 4.1 `tests/Shared.Kernel.Tests/UrlRedactionUserInfoAsHostTests.cs` (T001)

Shared generator (private), reproducing spec §1.2 exactly:

- schemes `rtsp`, `rtsps`, `http`, `https`;
- shapes: `user:pass` = `admin:{s}{d}@`; `user:pass-tail` = `operator:{s}{d}!@`; `user-only` = `{s}{d}@`;
  `empty-user` = `:{s}{d}@`;
- secrets `12345`, `4711`, `1`, `0x7f000001`, `abc`; delimiters `#`, `?`, `/`, `\`;
- host parts `cam.local/s`, `10.0.0.5:554/h264`, `[fd00::5]:8554/x`;
- input `{scheme}://{shape}{hostpart}`; label `"{scheme} {shape} {secret} {delimiter} {host}"`;
- `LeaksToday(row)` = `delimiter != "\\" && shape != "empty-user" && (shape == "user-only" || secret.All(char.IsAsciiDigit))`.

| ID | Kind | Rows | Assertion | Colour before / after |
|---|---|---|---|---|
| U1 | Theory | 396 (`LeaksToday`) | `ToOrigin(input).ShouldBe(Unparseable, label)` | **red** / green |
| U2 | Theory | 564 (`!LeaksToday`) | `ShouldBe(Unparseable, label)` | green / green |
| U3 | Fact | — | matrix has 960 rows, 396 with `LeaksToday` | green / green |
| U4 | Fact | 49,152 generated | count of offending outputs `ShouldBe(0)`, message names the count and first ten | **red** (`768`) / green |
| U5 | Theory | 18 pinned | `ShouldBe(Unparseable)` | **red** / green |
| U6 | Theory | 6 pinned (accepted over-refusals) | `ShouldBe(Unparseable)` | **red** / green |
| U7 | Theory | 22 pinned (input, origin) | `ShouldBe(origin)` | green / green |
| U8 | Theory | U1 ∪ U5 ∪ U6 | `ToOrigin(ToOrigin(x)).ShouldBe(ToOrigin(x))` | green / green |

**U4 generator** (built in the test body, not as theory rows — U+0000 does not survive theory
serialisation): for scheme ∈ the four, marker ∈ {`//`, two backslashes, `/\`, `\/`, empty, `///`},
userinfo ∈ {`admin:12345`, `12345`, `admin:1`, `abc`}, suffix ∈ {``, `x`}, host ∈ {`cam`,
`10.0.0.5:554`}, c ∈ U+0000..U+007F: input `{scheme}:{marker}{userinfo}{c}{suffix}@{host}/s`.
Offending = output is neither `Unparseable` nor the expected origin, where the expected origin is pinned
in U7 (`{scheme}://cam`, `{scheme}://10.0.0.5:554` for all four schemes — port 554 is **not** default for
any of them on 10.0.13). Measured: 49,152 inputs, **768** offending today, 0 under the rule.

**U5 rows** (today's output in parentheses — each a different leak shape): `rtsp://admin:12345#@cam.local/s`
(`rtsp://admin:12345`); `rtsp://admin:4711?@cam.local/s`; `rtsp://admin:8080/x@cam.local/s`;
`rtsp://operator:2468#!@10.0.0.5:554/h264`; `rtsp://12345#@cam/s` (`rtsp://12345`);
`rtsp://0x7f000001#@cam/s`; `https://1#@cam/s` (`https://0.0.0.1`); `http://12345#@cam.local/s`
(`http://0.0.48.57`); `rtsp://SECRET#@cam/s` (`rtsp://secret`); `rtsp://a@admin:12345#@cam/s`;
`rtsp:\\admin:12345#@cam/s` (two backslashes); `http:/\admin:12345#@cam/s`; `" rtsp://admin:12345#@cam/s"`
(leading space); `rtsp://[fd00::5]#@cam/s` (`rtsp://[fd00::5]`); `rtsps://admin:12345#@cam.local:8554/s`;
`rtsp://admin:12345#@[fd00::5]:8554/x`; `rtsp://ADMIN:12345#@cam/s`; `rtsp://admin:12345#@cam/s#frag@x`.

**U6 rows** (today renders the origin in parentheses; spec §6 accepted cost): `rtsp://cam/x@y`
(`rtsp://cam`); `rtsp://cam:554/x?mail=a@b` (`rtsp://cam:554`); `rtsp://cam/x#a@b`; `rtsp://cam:554/x?@`;
`rtsp://user:pass@cam/x@y` (`rtsp://cam`); `" rtsp://user:pass@cam/x"` (`rtsp://cam`).

**U7 rows**: `rtsp://user:pass@cam/x` -> `rtsp://cam`; `rtsp://admin:SECRET@cam/x?token=SECRET` ->
`rtsp://cam`; `rtsp://admin:hunter2@10.0.5.12/h264` -> `rtsp://10.0.5.12`;
`rtsp://user:secret@camera-sim:8554/station-4` -> `rtsp://camera-sim:8554`; `rtsp://admin:12345%23@cam/s`,
`...%2F@cam/s`, `...%3F@cam/s`, `...%5C@cam/s`, `rtsp://admin%40x:12345@cam/s`, `rtsp://admin:12345;@cam/s`,
`rtsp://admin:12345 @cam/s`, `rtsp://cam/x%40y` -> `rtsp://cam`; `rtsp://cam:554/stream` -> `rtsp://cam:554`;
`rtsp://10.0.5.5/h264` -> `rtsp://10.0.5.5`; and the eight U4 oracles `{scheme}://cam/` -> `{scheme}://cam`,
`{scheme}://10.0.0.5:554/` -> `{scheme}://10.0.0.5:554`. (The U4 oracle is thus pinned by a separate
assertion on an `@`-free input, not computed from the subject under test.)

**Red messages expected** (quote in the PR): U1 e.g. `should be "<unparseable url>" but was
"rtsp://admin:12345"`; U4 `should be 0 but was 768`; U5 e.g. `but was "http://0.0.48.57"`; U6 e.g.
`but was "rtsp://cam:554"`.

### 4.2 Sink tests (T002-T004)

Mirror the spec 379 sink classes (`AuditPayloadHostCharsetRedactionTests`,
`MediaMtxRtspGatewayHostCharsetLogRedactionTests`, `ChangeCameraAddressHostCharsetLogRedactionTests`) and
the repair fixtures in `MediaMtxSourceRepairAdversarialTests`. The leaked text is asserted absent as the
origin form `operator:2468` (not bare digits, which a GUID in the payload could contain by chance).

- **S1** `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadUserInfoRedactionTests.cs`:
  (a) `CameraRegisteredV1` with Url `rtsp://operator:2468#!@10.0.0.5:554/h264` -> stored `Url` is the
  placeholder; raw payload and `AuditRowMapper.Map(...).Payload` do not contain `operator:2468` (red);
  (b) `CameraAddressChangedV1` `rtsp://operator:2468#@cam/s` -> `rtsp://operator:1357#@cam/s`: both URL
  fields the placeholder and `DetailChanged` **true** (red — today the two misread origins differ, so it is
  `false`); (c) `rtsp://user:pass@cam/x` -> `rtsp://cam2/x`: `PreviousUrl` `rtsp://cam`, `Url`
  `rtsp://cam2`, `DetailChanged` false (green, characterisation of the kept userinfo path).
- **S2** `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayUserInfoLogRedactionTests.cs`:
  register and re-point with source `rtsp://operator:2468#!@10.0.0.5:554/h264` -> log line names the
  placeholder, no entry contains `operator:2468` (red ×2); the body sent to MediaMTX carries the source
  unchanged (green ×2).
- **S3** `tests/StreamDistribution.Infrastructure.Tests/Reconciler/MediaMtxSourceRepairUserInfoLogRedactionTests.cs`:
  observed source `rtsp://operator:2468#@cam/x`, committed `rtsp://10.0.8.1/h264` -> the entry
  `Reconciler repaired MediaMTX path {path} source <unparseable url> -> rtsp://10.0.8.1.` exists and no
  entry contains `operator:2468` (red); the gateway is still configured with the committed source (green).
- **S4** `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressUserInfoLogRedactionTests.cs`:
  change to `rtsp://operator:2468#!@10.0.0.5:554/h264` succeeds (`RtspUrl.From` accepts it) and logs
  `Changed camera {id} address to <unparseable url>.` (red); the stored address is the full URL (green).

### 4.3 Adversarial pass (T020)

`tests/Shared.Kernel.Tests/UrlRedactionUserInfoAsHostAdversarialTests.cs`, written by `test-adversary`
against the fixed code. Directions to probe: multiple `@` in every position; `@` immediately after the
marker (`rtsp://@cam/x`, `rtsp://#@cam`); the marker run with mixed slashes and leading/trailing
whitespace or controls; a scheme with `+ . -`; uppercase schemes; UNC (`\\admin:1#@cam\x`) and `file:`
forms; an `@` after a bare `\` in the authority; very long inputs (70,000 chars, the spec 377 limit row);
IPv6 literals with zone ids in userinfo position; and the claim that no all-ASCII `@`-free input changes
(a generated sweep compared against a copy of the pre-change outputs is acceptable only if the copy is
committed as data, not recomputed). Every new fact passes against T010; any failure is a finding
returned verbatim (a follow-up T011 commit, red first), never an edited test.

### 4.4 Full verification (T010, after green)

`dotnet test` for `tests/Shared.Kernel.Tests`, `tests/AuditObservability.Application.Tests`,
`tests/StreamDistribution.Infrastructure.Tests`, `tests/CameraCatalog.Application.Tests`,
`tests/Architecture.Tests` (all `-c Release`), `dotnet build -c Release`, and
`dotnet format --verify-no-changes`. Coverage: `Shared.Kernel` >= 90% (ADR-0065).

## 5. Messaging

None. No domain or integration event changes; `CameraRegisteredV1` / `CameraAddressChangedV1` carry the
full URL as before and are redacted only in the audit copy (spec 377).

## 6. Risks

- **Runtime variance.** The leak set and the `\` result were measured on 10.0.13. B does not depend on the
  parser (it reads the raw string), so a runtime that parses differently changes which rows leak *today*
  (U1/U4 counts), not whether the rule closes them. U3 pins the count, so a runtime bump that moves it
  fails loudly rather than silently shrinking the red set.
- **The spec 379 ordinal-`"SECRET"` weakness** in the #2908 sink tests is unchanged and out of scope; the
  new sink tests assert on `operator:2468`, which the host lower-casing cannot hide (digits and `:` have no
  case; `operator` is already lower-case).
