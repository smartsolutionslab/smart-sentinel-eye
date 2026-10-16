# Plan 379: URL redaction refuses a host that is not plain ASCII

**Spec**: [spec.md](./spec.md) | **Issue**: #2989 | **ADRs**: 0036, 0050, 0065, 0105, 0139, 0144

## 1. Bounded context and layers

**`Shared.Kernel` only** for production code: one file, `src/Shared.Kernel/UrlRedaction.cs`. No
bounded context, no Domain/Application/Infrastructure/Api file, no contract, no migration, no
AppHost resource. Test files are added in four existing test projects; no test project gains a
reference.

**Boundary rules.** Unchanged. `Shared.Kernel` keeps no domain and no I/O; every caller (spec §1,
C1–C7) already references it. NetArchTest rules are untouched.

## 2. Mechanism

After the existing `Uri.TryCreate` / empty-host check, and before the origin is composed:

```
if (!IsPlainAsciiHost(uri.Host, uri.HostNameType) || !IsAuthorityAsWrittenAscii(url)) return Unparseable;
```

- **(a) `IsPlainAsciiHost`** (private static): true iff every character of `host` is an ASCII
  letter, ASCII digit, `-`, `.` or `_`; when `hostNameType == UriHostNameType.IPv6`, `[`, `]` and
  `:` are also allowed. Suggested shape: `static readonly SearchValues<char>` and
  `host.AsSpan().ContainsAnyExcept(...)`, or a loop with `char.IsAsciiLetterOrDigit`; the engineer
  picks, both fit ADR-0084's limits. It runs for every `HostNameType`, IPv6 included, so it pins I1
  independently of what the parser does. On its own it is **defeated** by U+200E/U+200F/U+202A–U+202E
  (stripped) and U+212A (case-mapped to `k`) — spec §1.
- **(b) `IsAuthorityAsWrittenAscii`** (private static, on the raw `url`): true if `url` is all
  ASCII (`Ascii.IsValid(url)`); otherwise find the first `//`; if none, false; else the span from
  just after it to the next `/`, `?`, `#` or backslash (or the end) must be all ASCII. It reads the text
  the caller wrote, before any parser normalisation, so it cannot be defeated by stripping or
  mapping. The all-ASCII short-cut makes (b) a no-op for every all-ASCII input, which is what keeps
  I2 exact. Final rule (third and fourth pass): for a non-ASCII input the authority is located only in
  the strict `scheme://` form, runs to the first `/`, `?`, `#` or backslash, and **an empty written
  authority is refused**. The earlier claims that this span is never shorter than the real authority,
  and that (b) implies (a), are false: for `file:////cam<U+200E>SECRET/x` the written authority is
  empty, so (b) held vacuously while `System.Uri` read the host after the extra separators (spec §8, D6).
- (a) is kept: it is the output-side guarantee (I1), (b) the input-side one, and (b) alone is not sufficient;
  they are one boolean expression, so there is no unreachable line.
- Upper-case ASCII is allowed by the set even though `Uri` lower-cases hosts today, so the check
  does not depend on that normalisation.
- Nothing else in the method moves: `Ensure.That(url).IsNotNull()`, `$"{uri.Scheme}://{uri.Host}"`,
  the `IsDefaultPort` rule, the `Unparseable` constant.

**Why this is exact for ASCII input, not just conservative** (spec §1): outside an IPv6 literal the
parser keeps no non-alphanumeric ASCII in `Uri.Host` except `-`, `.`, `_`, and (b) short-cuts on
all-ASCII input. So the output changes only for inputs with a non-ASCII character in the authority
as written (or with a non-ASCII character and no `//`), and for no all-ASCII input. That is the
characterisation half of this change. Modelled in the scratch probe over every pinned input of
spec §1, only `rtsp://kämera.example/SECRET` moved; over the BMP sweep, 0 offending outputs remain.

**Rejected alternatives.**

| Option | Why not |
|---|---|
| A deny-list of look-alike code points | 63,327 of 63,360 BMP code points leak (spec §1); a list is the shape that was wrong twice on the issue already. |
| `Uri.IdnHost` (punycode) | 42 of 78 look-alike probes carry the marker, some as ASCII `=`, `&`, `%`, `.`; it throws for others. |
| A charset check on `Uri.Host` alone (the issue's literal wording) | Accepts `rtsp://cam{U+200E}SECRET/x` -> `rtsp://camsecret` and the Kelvin-sign equivalent: the parser has already normalised the host to ASCII (spec §1). |
| Re-locating the host inside the written authority (skip userinfo, port) | Re-implements the parser's userinfo/port rules; a mismatch is a new bypass. Refusing the whole authority is simpler and fail-closed (spec §6). |
| Refusing any input with a non-ASCII character anywhere | Would change `rtsp://cam/x／SECRET` (non-ASCII only in the path; no leak today) from `rtsp://cam` to the placeholder. |
| `HostNameType == Dns` gate alone | Every leaking case is `HostNameType.Dns`; it distinguishes nothing. |
| `Uri.DnsSafeHost` | Returns the same Unicode text as `Host` for every probe. |
| LDH only (no `_`) | Changes `rtsp://cam_1:8554`, an all-ASCII origin (spec §6 [Assumption]). |
| Rejecting in `RtspUrl` / `StreamSourceUrl` | Out of scope (validation change); and C7's MediaMTX-reported source never passes through them. |

## 3. Entities, value objects, invariants

No entity or value object changes. The invariant `ToOrigin` now holds:

- **I0** No character above U+007F in the authority as written can influence a rendered origin:
  such an input renders `Unparseable`.
- **I1** The output is `Unparseable` or matches
  `^[a-z][a-z0-9+.\-]*://([A-Za-z0-9._\-]+|\[[0-9A-Fa-f:.]+\])(:[0-9]+)?$` — in particular it is pure
  ASCII. (Spec 377's `BareOrigin` regex remains true and is weaker.)
- **I2** For an input whose `Uri.Host` is all ASCII, the output equals the pre-change output.
- **I3** Idempotence holds: the placeholder maps to itself (it is unparseable), and an accepted origin
  is all-ASCII, so it re-parses to itself.

## 4. Tests (phase 4a: RED, then adversarial)

Every red test compiles on unchanged code (`ToOrigin` and every sink exist today), so each must be
seen **failing on an assertion**, not on a compile error. "Contains the marker" is always checked
**case-insensitively** — `ToOrigin` lower-cases, and the existing #2908 sink tests' ordinal
`"SECRET"` check would miss this leak (spec §1).

### 4.1 `Shared.Kernel.Tests` — new file `UrlRedactionHostCharsetTests.cs`

The existing `UrlRedactionTests.cs` and `UrlRedactionAdversarialTests.cs` are **not edited**.

- **K1 (red, theory, generated)** `A_host_carrying_a_non_ascii_character_renders_the_placeholder`:
  rows generated from the 26 code points × 3 schemes × the **13 leaking combinations** of spec §1's
  table (all 5 shapes leaking in both variants, plus after-kv, after-kv-path and userinfo in the
  fullwidth variant only) = **1014 rows**. Assert first `ShouldNotContain("SECRET",
  Case.Insensitive)`, then `ShouldBe(UrlRedaction.Unparseable)`. Expected red: **all 1014 fail**
  on the first assertion (proved by the scratch probe: 1014/1014 leak on runtime 10.0.13).
  The row source is a `TheoryData<string>` built in code from the code-point list and shape
  templates, so the 1014 count is checkable; name each code point in a comment once.
- **K2 (green characterisation, theory)** the 234 non-leaking combinations (ASCII `=` / `:` variant of
  after-kv, after-kv-path, userinfo) return `Unparseable` — **expected green now and after**. Proves
  the generator's split is the real one and that those inputs were already safe.
- **K3 (red, fact)** `No_code_point_above_ascii_survives_in_a_rendered_host`: for every BMP code
  point U+0080–U+FFFF except surrogates, inputs `rtsp://cam{c}SECRET/x` and `rtsp://SECRET{c}cam/x`
  (rtsp only; the probe showed rtsps/http identical), collect every output that is not
  `Unparseable` and either contains a non-ASCII character or contains `SECRET` case-insensitively;
  assert the collection is empty, with a message giving the count and the first ten code points.
  Expected red: **126,654 offending outputs** (63,327 per shape). Both conditions are needed: the
  non-ASCII one catches the seven combining marks that defeat a `SECRET` substring, the marker one
  catches the 20 outputs that are already pure ASCII (spec §1's ten code points, two shapes each).
  Runtime ~127k calls: seconds, measured in the probe.
- **K4 (red, theory)** astral and IDN: `rtsp://cam😀SECRET/x`, `rtsp://SECRET😀cam/x`,
  `rtsp://bücher.example/x`, `rtsp://BÜCHER.example/x`, `rtsp://cam／x/SECRET` (no marker today, but
  a non-ASCII host), `rtsp://ｃａｍ/x` (fullwidth letters), `rtsp://cam。example/x`,
  `rtsp://user：pass@cam/x` (today `rtsp://cam`; spec §6 [Assumption]),
  `rtsp://[fe80::1%25ｅth0]/x` (today `rtsp://[fe80::1]`) -> `Unparseable`. Expected red: all nine.
- **K7 (red, theory)** the normalisation bypass of spec §1, named so the reason survives:
  `rtsp://cam{c}SECRET/x` for c in U+200E, U+200F, U+202A, U+202B, U+202C, U+202D, U+202E, U+212A,
  U+037E, U+1FEF -> `Unparseable`, no `SECRET` case-insensitively. Expected red: all ten (today
  `rtsp://camsecret`, `rtsp://camksecret`, `rtsp://cam;secret`, ``rtsp://cam`secret``). The first
  eight are the rows that a host-only check would let through; the adversary must confirm that a
  build with only part (a) fails them.
- **K5 (green characterisation, theory)** all-ASCII forms render as before:
  `rtsp://cam_1:8554/x` -> `rtsp://cam_1:8554`; `rtsp://-cam-/x` -> `rtsp://-cam-`;
  `rtsp://127.1/x` -> `rtsp://127.1`; `rtsp://0x7f000001/x` -> `rtsp://0x7f000001`;
  `http://0x7f000001/x` -> `http://127.0.0.1`; `rtsp://[::ffff:10.0.0.1]/x` ->
  `rtsp://[::ffff:10.0.0.1]`; `rtsp://[FD00::ABCD]/x` -> `rtsp://[fd00::abcd]`;
  `rtsp://cam./x` -> `rtsp://cam.`; `rtsp://xn--bcher-kva.example/x` and
  `rtsp://XN--BCHER-KVA.example/x` -> `rtsp://xn--bcher-kva.example`; `rtsp://cam/x／SECRET` ->
  `rtsp://cam`; `rtsp://cam/x?{U+200E}` -> `rtsp://cam`; `rtsp:{U+200E}//cam/x` -> `Unparseable`.
  **Expected green now and after** (13 rows). Pins I2 at the forms most likely to be disturbed, and
  that a non-ASCII character outside the authority leaves the origin alone.
- **K6 (green characterisation, theory)** idempotence over K1's and K4's inputs:
  `ToOrigin(ToOrigin(x)) == ToOrigin(x)`. It holds today too, so it is **not** red evidence; it must
  stay green across the change (I3).

### 4.2 Sink tests (new files; existing sink test files not edited)

- **S1 (red)** `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadHostCharsetRedactionTests.cs`,
  using the same handler construction as `AuditPayloadRedactionPostFixAdversarialTests` (copy the
  private `Build`/`Metadata` helpers; do not edit that file):
  (a) `CameraRegisteredV1` with `rtsp://cam／token＝SECRET` -> stored `Url` is `Unparseable`; raw
  payload and `AuditRowMapper.Map(...).Payload` contain no `SECRET` case-insensitively;
  (b) `CameraAddressChangedV1` with `rtsp://user：SECRET＠cam:554` -> `rtsp://SECRET＠cam/x`: both
  fields `Unparseable`, no marker;
  (c) `PreviousUrl` `rtsp://cam／a＝SECRET1`, `Url` `rtsp://cam／a＝SECRET2` -> both `Unparseable`,
  `DetailChanged` **true** (today `false`: the leaked origins differ). Expected red: every marker and
  value assertion; (c)'s `DetailChanged`.
- **S2 (red)** `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayHostCharsetLogRedactionTests.cs`
  (copy `NewGateway`/`SourceOf` from `MediaMtxRtspGatewayLogRedactionTests`; use the existing
  `Fakes/`): `AddPathAsync` and `RepointPathAsync` with `rtsp://cam／token＝SECRET` and
  `rtsp://SECRET＠cam/x` -> log message `Registered MediaMTX path {path} -> <unparseable url>.` /
  `Re-pointed MediaMTX path {path} at <unparseable url>.`; no entry contains `SECRET`
  case-insensitively. Plus a **green characterisation** fact: the body sent to MediaMTX still carries
  the source verbatim.
- **S3 (red)** `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressHostCharsetLogRedactionTests.cs`
  (copy `NewHandler`/`SeedAsync`/`CommandFor` from `ChangeCameraAddressLogRedactionTests`): new
  address `rtsp://cam／token＝SECRET` -> success; log `Changed camera {id} address to <unparseable url>.`;
  no marker case-insensitively; stored `Url` unchanged (green characterisation fact).
- **Reconciler sinks (C4, C7): no new test.** C4's only test is the Aspire-stack reconciler
  characterisation (Integration.Tests, CI-sharded); C7 lives on an unmerged branch. Both call the same
  `ToOrigin`; K1/K3 cover them. The adversary may add a C7 fact if spec 378 has merged first.

No Integration.Tests class is added, so no `ci-shards/shard-N.filter` edit is needed.

### 4.3 Adversarial pass (test-adversary, after K1–K4 and S1–S3 are green)

Brief: assume the check is wrong. First, **prove each half by counterfactual**: on a scratch copy,
remove (b) and show K7's bidi/Kelvin rows go red; remove (a) and report whether anything goes red
(if nothing does, say so — (a) is then defence in depth, which is the plan's stated intent). Probe
astral planes beyond one emoji (U+10000–U+10FFFF sample,
lone surrogates `\uD800`), every `HostNameType` (`Basic`, `Unknown`), IPv6 with embedded IPv4 and
zone ids carrying non-ASCII (`[fe80::1%25ｅth0]`), schemes `file`, `ftp`, `ws`, uppercase schemes,
`Uri` implicit-file and UNC inputs, bidi controls (U+200E, U+202E, U+2066–U+2069), the C1 range,
leading/trailing whitespace around a look-alike host, percent-encoded non-ASCII in the host
(`%EF%BC%8F`), and very long non-ASCII hosts. Every case: output is `Unparseable` or matches I1, and
contains no marker case-insensitively. New facts go in a new file
`tests/Shared.Kernel.Tests/UrlRedactionHostCharsetAdversarialTests.cs`.

### 4.4 Commands

```sh
dotnet test tests/Shared.Kernel.Tests -c Release --filter "FullyQualifiedName~UrlRedaction"
dotnet test tests/AuditObservability.Application.Tests -c Release --filter "FullyQualifiedName~AuditPayload"
dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~MediaMtxRtspGateway"
dotnet test tests/CameraCatalog.Application.Tests -c Release --filter "FullyQualifiedName~ChangeCameraAddress"
# after the fix, full projects + gates
dotnet test tests/Shared.Kernel.Tests -c Release
dotnet test tests/AuditObservability.Application.Tests -c Release
dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release
dotnet test tests/CameraCatalog.Application.Tests -c Release
dotnet test tests/Architecture.Tests -c Release
dotnet build -c Release
dotnet format --verify-no-changes
# CI only (Docker): Integration.Tests, unchanged classes (CameraAddressAuditIntegrationTests,
# MediaMtxReconcilerPassCharacterisationTests) stay green
```

Coverage: `Shared.Kernel` stays >= 90% (ADR-0065); the new branch is hit by K1 and K5.

## 5. Messaging

None. No domain or integration event, no contract, no queue changes.

## 6. Risks

- **A real camera addressed by a Unicode IDN** loses its host in logs and audit rows (placeholder).
  Accepted (spec §6); none exists in the repo, and fab cameras are addressed by IP or ASCII name.
- **`DetailChanged` semantics shift for non-ASCII addresses** (S1c): two different non-ASCII
  addresses now read as "same origin, detail changed". Same as spec 377 already gives two unparseable
  inputs; recorded, not mitigated.
- **Ordinal `"SECRET"` assertions in the #2908 sink tests** remain blind to lower-cased leaks. Not
  edited (forbidden); the new sink tests are case-insensitive. Worth a follow-up issue from phase 6
  if the reviewer agrees.
- **Spec 378 ordering.** If `fix/2960-...` merges first, C7 inherits the fix with no file overlap
  (this spec touches no StreamDistribution production file). If this merges first, 378 rebases with
  no conflict. Re-check the spec number either way.

## 7. Third pass (D4, D5)

Rule (b) is amended: for a non-ASCII input the authority is located only in the strict form
`scheme://` (scheme of ASCII letters/digits/`+.-`), ending at the first `/`, `?`, `#` or backslash; any other form
yields the placeholder (fail-closed; `Uri` also takes two backslashes, slash-backslash, backslash-slash and slashless markers).
`Uri.TryCreate` is wrapped in a narrow `catch (IndexOutOfRangeException)` returning the placeholder
(a BCL defect on untrusted text; a deliberate boundary catch, to be stated in the PR body).
All-ASCII inputs are untouched. Accepted over-refusals: non-ASCII IPv6 zone, and the UNC form (two leading backslashes, `kämera`, backslash, `x`).
