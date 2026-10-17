# Plan 394: A camera URL with '@', '?' or '#' inside the IPv6 brackets is refused, and one the parser cannot read answers 400, not 500

**Spec**: [spec.md](./spec.md) · **Tasks**: [tasks.md](./tasks.md) · **Issue**: #3023 (items 1, 2, 5)

## 1. Constitution and ADR check

| Gate | Result |
|---|---|
| §II primitives on domain models | Pass. The value objects keep their single `Value`. New `UrlAuthority` members are stateless and return `bool` with an `out string`; `PrimitiveBoundaryTests` unaffected. |
| §III no cross-context references | Pass. CameraCatalog, StreamDistribution and AuditObservability already reference `Shared.Kernel`. No project reference added. The two value objects stay separate copies by design (`StreamSourceUrl`'s doc comment). |
| §IV latency | N/A (spec header). |
| ADR-0105 guards | Each new public `UrlAuthority` member guards `url` with `Ensure.That(url).IsNotNull()`. Messages carry no value. |
| ADR-0047/0089 errors | No new code. 400 `CAMERA_INVALID_REQUEST` (via `BoundaryParse`); `STREAM_INVALID_RTSP_SOURCE`. |
| ADR-0042/0088 Wolverine | No policy change; spec 389's `CameraEventFailurePolicyCharacterisationTests` stays the pin. |
| ADR-0139/0144 | Behaviour-changing: red for FR-001..FR-004 and FR-007; characterisation for every unchanged row of spec §5. No existing assertion is edited (§7). No ADR, no constitution change, no gate weakened. |
| ADR-0036 / no drive-by error handling | The `catch (IndexOutOfRangeException)` is at a trust boundary (untrusted text into a BCL parser), mirrors `UrlRedaction.TryParse`, and is reached by observed inputs. Consumers that no input reaches are not guarded (spec §7). |
| ADR-0065 coverage | New `UrlAuthority` members covered by the red rows plus direct post-green rows (T060), including the catch arms. |
| ADR-0084 metrics | Advisory. Each new member is a handful of lines. |
| New ADR needed? | **No** (spec header). |

## 2. Layers touched

| Context / project | Layer | Files |
|---|---|---|
| Shared.Kernel | — | `src/Shared.Kernel/UrlAuthority.cs` (three new public members), `src/Shared.Kernel/UrlRedaction.cs` (one `||` in `ToOrigin`) |
| CameraCatalog | Domain | `src/CameraCatalog/Domain/Camera/RtspUrl.cs` |
| StreamDistribution | Domain | `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` |

Not touched: `AuditPayloadRedaction.cs` (its outcome changes only through `ToOrigin`), the EF converters, any Api,
Application, Contracts, Infrastructure, migration, AppHost, ScenarioSimulator, frontend or Helm file.

## 3. Shapes

### 3.1 `Shared.Kernel.UrlAuthority` — three new public members

```csharp
// FR-001. True when any bracketed-host reading holds '@', '?' or '#' between its '[' and its first ']'.
// Spans only, no Uri, never throws (ToOrigin calls it on untrusted text).
public static bool DelimiterInsideTheBrackets(string url);

// FR-002 / FR-003. Uri.TryCreate(url, UriKind.Absolute, out var uri) and the one property read inside a single
// try; false when TryCreate fails or the read throws IndexOutOfRangeException (the shape of UrlRedaction.TryParse).
public static bool TryReadHost(string url, [NotNullWhen(true)] out string? host);
public static bool TryReadUserInfo(string url, [NotNullWhen(true)] out string? userInfo);
```

Names are fixed here because the structural guards (§5.3) are written before the code and scan for them; an
engineer who wants other names changes this plan first, not the test.

**`DelimiterInsideTheBrackets`**, following `SlashInsideTheBracketsFollowsNoAddress` exactly:

```csharp
Ensure.That(url).IsNotNull();

foreach (var host in BracketedHostReadings(url))
{
    var close = host.IndexOf(']', StringComparison.Ordinal);

    if (close > 0 && host.AsSpan(1, close - 1).IndexOfAny(BracketDelimiters) >= 0)
    {
        return true;
    }
}

return false;
```

with `private const string BracketDelimiters = "@?#";` declared next to `AuthorityTerminators`. It is a different
set from the terminator set (no `/`, which starts a prefix length; no `\`, spec A-1; plus `@`), so it is its own
constant, not a reuse of `AuthorityTerminators`. A bracket with no `]` answers false: the parser refuses it and the
userinfo rule answers first. The comment states *why* (RFC 6874 zones hold none of the three; a parser splitting
at the first `?` / `#`, or ending the userinfo at the last `@`, reads another host).

**`TryReadHost` / `TryReadUserInfo`**: one `try` around `Uri.TryCreate` and the read, `catch
(IndexOutOfRangeException)` only (spec A-4), `out` set to `null` on false. They are two members, not one reading
both, because the reads fail on different inputs: `UserInfo` reads cleanly on `rtsp://[1:2:3:4:5:6:7:8:/64]` while
`Host` throws, and a combined reader would make `HasNoUserInfo` refuse — and `FromStored` start refusing — an input
`FromStored` accepts today (spec FR-005). The comment names the observed shape (`a ':' left before a '/' prefix
length`) as the reason the guard exists.

### 3.2 `UrlRedaction.ToOrigin`

```csharp
if (UrlAuthority.AtSignFollowsTheAuthority(url)
    || UrlAuthority.TextFollowsTheClosingBracket(url)
    || UrlAuthority.SlashInsideTheBracketsFollowsNoAddress(url)
    || UrlAuthority.DelimiterInsideTheBrackets(url)
    || !TryParse(url, out var uri)
    ...
```

Nothing else in `UrlRedaction` changes. `TryParse` is **not** replaced by the new readers and the later `uri.Host`
/ `uri.Port` reads are **not** wrapped: no input reaches a throw there (spec §1.3, §7).

### 3.3 Value objects (identical shape in both)

```csharp
public static RtspUrl From(string value)
{
    EnsureStoredInvariants(value)
        .Satisfies(url => !UrlAuthority.AtSignFollowsTheAuthority(url), AtSignAfterAuthorityMessage)
        .Satisfies(url => UrlAuthority.TryReadHost(url, out _), UnreadableHostMessage)                    // new, FR-002
        .Satisfies(HasHost, EmptyHostMessage)
        .Satisfies(url => !UrlAuthority.TextFollowsTheClosingBracket(url), TextAfterBracketMessage)
        .Satisfies(url => !UrlAuthority.SlashInsideTheBracketsFollowsNoAddress(url), SlashInBracketsMessage)
        .Satisfies(url => !UrlAuthority.DelimiterInsideTheBrackets(url), DelimiterInsideBracketsMessage); // new, FR-001
    return new RtspUrl(value);
}

private static bool HasHost(string url) =>
    UrlAuthority.TryReadHost(url, out var host) && !UrlAuthority.IsBlankHost(host);

private static bool HasNoUserInfo(string url) =>
    UrlAuthority.TryReadUserInfo(url, out var userInfo) && userInfo.Length == 0;
```

`FromStored` and `EnsureStoredInvariants` keep their rule list; only `HasNoUserInfo`'s body changes (FR-003, spec
A-3). The two message literals are the spec FR-001 / FR-002 texts, declared once per value object as
`private const string`. The doc comment gains one sentence for the two rules. The order is fixed (spec FR-001,
FR-002); the syntax is the engineer's.

**Invariants after the change**

| Factory | blank / length / `rtsp://` | no userinfo (guarded read) | no raw `@` after authority | host readable | host not blank | no text after `]` | `/` in brackets after an address | no `@?#` in brackets |
|---|---|---|---|---|---|---|---|---|
| `From` (all writes) | yes | yes | yes | **yes** | yes | yes | yes | **yes** |
| `FromStored` (EF read only) | yes | yes | no | no | no | no | no | no |

## 4. Messaging

No domain event, integration event or contract changes. `CameraRegisteredV1` / `CameraAddressChangedV1` keep
carrying the full URL; the audit copy changes only through `ToOrigin` (FR-004). Replay: spec 389 §4 applies
unchanged — a pre-deploy event carrying an FR-001 address is moved to the error queue on its first attempt with one
Warning naming `STREAM_INVALID_RTSP_SOURCE` and no part of the address. The FR-002 shapes cannot be in any
pre-deploy event (registration threw on them); the handler row for them proves the typed failure only.

## 5. Testing strategy

Phase 4a is **red** for the new behaviour, with **characterisation** for every unchanged row, in separate files
and separate commits. Ambiguity resolves to red (ADR-0144). Red tests reach behaviour only through members that
exist at `fbd4176b` (`From`, `FromStored`, `ToOrigin`, `AuditPayloadRedaction.Serialize` / the audit handler, the
endpoint host, the stream handlers) or through source scanning; the three new `UrlAuthority` members are called
directly only in post-green rows (T060). A compile error is not a red.

### 5.1 Characterisation — captured green on `fbd4176b` before any production edit, unmodified after

| ID | Test | Guards |
|---|---|---|
| C1 | existing suites of §5.4 (all `RtspUrl*`, `StreamSourceUrl*`, `UrlAuthority*`, `UrlRedaction*`, `AuditPayload*`, `CameraUrl*`, `LegacyMisreadSource*`, `LegacyAuthorityHost*`, `FromStored*`, `CameraRegisterEndpoint*`, `CameraPatchEndpoint*`, `CameraEventFailurePolicyCharacterisationTests`, `StreamSourceUrlHydration*`, NetArchTest boundary suite, `PrimitiveBoundaryTests`, `HandlerDeconstructionTests`) | nothing spec 389/392 pinned moves; quote the pass counts |
| C2 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlZoneDelimiterCharacterisationTests.cs` | spec §5 accept set accepted by `From` and round-trips through `FromStored`; spec §5 "older message" set refused by `From` with today's exact message (raw-`@`, text-after-`]`, userinfo) |
| C3 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlZoneDelimiterCharacterisationTests.cs` | same for `StreamSourceUrl` |
| C4 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredZoneDelimiterTests.cs` and `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredZoneDelimiterTests.cs` | `FromStored` reads back every FR-001 and FR-002 row (US2a) |
| C5 | **new** `tests/Architecture.Tests/Persistence/CameraUrlZoneDelimiterHydrationCharacterisationTests.cs` | both EF converters' `ConvertFromProvider` return the stored value for every FR-001 and FR-002 row (pattern: `CameraUrlAuthorityHostHydrationCharacterisationTests`) |
| C6 | **new** `tests/Shared.Kernel.Tests/UrlRedactionZoneDelimiterCharacterisationTests.cs` | every FR-002 and FR-003 row -> placeholder (already, via the spec 392 slash rule); accept-set bracket rows keep their origins (`rtsp://[fe80::1%25eth0]/x` -> `rtsp://[fe80::1]`, `rtsp://[::1%25%40x]/s` -> `rtsp://[::1]`, `rtsp://@[fe80::1]/x` -> `rtsp://[fe80::1]`); and `ToOrigin` does not throw for any of them |
| C7 | **new** `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadZoneDelimiterCharacterisationTests.cs` | through `IntegrationEventAuditHandler` (harness of `AuditPayloadDetailChangedBracketTailTests`): `DetailChanged` is `JsonValueKind.Null` in both directions for every FR-001 row paired with `rtsp://[fe80::1]/x`, and for `rtsp://@[fe80::1%@10.0.0.66:8554]/live` -> `rtsp://@[fe80::1%@10.0.0.66:8554]/x`; the audit write does not throw for any FR-002 / FR-003 row |

No Aspire integration test is added: hydration runs through the converter (C5), which spec 389's
`StoredMisreadCameraAddressIntegrationTests` proved is what the running system uses; phase 5 asks the running
system once (spec §8 step 5). No `tests/Integration.Tests/ci-shards/shard-N.filter` entry is therefore needed.

### 5.2 Red — new behaviour, observed failing before production code

| ID | Test | Red today because |
|---|---|---|
| R1 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlZoneDelimiterAndUnreadableHostTests.cs`: each FR-001 row throws `ArgumentException`, message exactly `value: <FR-001> (Parameter 'value')`; each FR-002 row throws `ArgumentException` (`Should.Throw<ArgumentException>`, which a thrown `IndexOutOfRangeException` fails) with the FR-002 text; each FR-003 row throws `ArgumentException` with `value: must not contain a user:password@ segment (Parameter 'value')` from **`From` and `FromStored`**; `ToString()` of every exception free of the row's secret (`10.0.0.66`, `evil.com`, `x?y`, `x#y`, `eth0#`, `eth0@`), `ParamName` `value` | FR-001 rows accepted; FR-002 / FR-003 rows throw `IndexOutOfRangeException` |
| R2 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlZoneDelimiterAndUnreadableHostTests.cs`: same | same |
| R3 | **new** `tests/Architecture.Tests/Persistence/CameraUrlZoneDelimiterRefusalTests.cs`: FR-007 agreement over the union of spec §5 sets — `From` refusing a row implies `ToOrigin` is the placeholder; the two `From`s agree row by row (same outcome, same message); no row makes either `From` throw anything but `ArgumentException` | FR-001 rows accepted with a rendered origin; FR-002 rows throw IOOR |
| R4 | **new** `tests/Shared.Kernel.Tests/UrlRedactionZoneDelimiterTests.cs`: every FR-001 row, plus `rtsp://user@[::1%25@evil.com:554]/s`, -> `UrlRedaction.Unparseable` | today rendered (`rtsp://[fe80::1]`, `rtsp://[::1]`, `rtsp://[fe80::1]:554`) |
| R5 | **new** `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadZoneDelimiterTests.cs`, through `IntegrationEventAuditHandler`: `CameraRegisteredV1.Url` stored as `<unparseable url>` for every FR-001 row; `CameraAddressChangedV1` from each FR-001 row to `rtsp://[fe80::1]/x` stores `PreviousUrl` `<unparseable url>` and `Url` `rtsp://[fe80::1]`, and the reverse direction mirrors it; the stored payload free of `10.0.0.66` / `evil.com` | today the origin is rendered |
| R6 | **new** `tests/Architecture.Tests/CameraUrlZoneDelimiterEndpointTests.cs` (existing `CameraCatalogEndpointHost`, pattern `CameraUrlAuthorityHostEndpointTests`): `POST /cameras` and `PATCH /cameras/{id}` with `rtsp://@[fe80::1%@10.0.0.66:8554]/live`, `rtsp://[::1%25x?y]/s` -> 400 `CAMERA_INVALID_REQUEST`, detail the rendered FR-001 text, body free of the secret; with `rtsp://[1:2:3:4:5:6:7:8:/64]` -> 400, FR-002 text; with `RTSP://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s` -> 400, the userinfo text; the handler not invoked; one PATCH with a stale If-Match -> still 400 | FR-001: 201 / 204; FR-002 / FR-003: an unhandled `IndexOutOfRangeException` (the test-writer quotes what the host surfaces — a 500 or the exception) |
| R7 | **new** `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyZoneDelimiterReplayTests.cs` (harness of `LegacyAuthorityHostReplayTests`): `CameraRegisteredV1` with `rtsp://@[fe80::1%@10.0.0.66:8554]/live` -> `InvalidOperationException`, nothing saved, no gateway call, one Warning with `STREAM_INVALID_RTSP_SOURCE` and no `10.0.0.66`; newer `CameraAddressChangedV1` with `rtsp://[::1%25x?y]/s` onto a seeded stream -> throws, address unchanged, no gateway call; `CameraRegisteredV1` with `rtsp://[1:2:3:4:5:6:7:8:/64]` -> `InvalidOperationException` naming `STREAM_INVALID_RTSP_SOURCE` (not an escaped `IndexOutOfRangeException`) | FR-001 rows provision / re-point; the FR-002 row escapes as IOOR |

Expected red: R1/R2 "did not throw" for FR-001 rows and "expected ArgumentException but IndexOutOfRangeException
was thrown" for FR-002/FR-003 rows; R3 the agreement fails on FR-001 rows and the "only ArgumentException" clause
on FR-002 rows; R4 `should be "<unparseable url>" but was "rtsp://[fe80::1]"`; R5 the rendered origin; R6 201/204
and the escaped exception; R7 "did not throw", a gateway call recorded, and the IOOR.

### 5.3 Structural guards — red before the code

| ID | Test | Red today because |
|---|---|---|
| G1 | `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs`, **new** theory `Each_caller_asks_the_delimiter_predicate` over `RtspUrl.cs`, `StreamSourceUrl.cs`, `UrlRedaction.cs` matching `\bUrlAuthority\s*\.\s*DelimiterInsideTheBrackets\s*\(`, and **new** fact `Only_UrlAuthority_declares_the_delimiter_predicate` (pattern `\bbool\s+DelimiterInsideTheBrackets\s*\(`, expected `[src/Shared.Kernel/UrlAuthority.cs]`). Additive: no existing line of the file changes. | not declared or called |
| G2 | **new** `tests/Architecture.Tests/CameraUrlGuardedUriReadTests.cs`: over executable lines (comments stripped, slashes normalised — memory: source-scanning tests) of `RtspUrl.cs` and `StreamSourceUrl.cs`: no match of `\bUri\s*\.\s*TryCreate\s*\(`, `\.\s*Host\b`, `\.\s*UserInfo\b`; each matches `\bUrlAuthority\s*\.\s*TryReadHost\s*\(` and `\bUrlAuthority\s*\.\s*TryReadUserInfo\s*\(`. Plus the two counterfactual facts the house pattern uses (a copied `uri.Host` line is recognised; the doc-comment text naming the parser is not mistaken for a read). | both files call `Uri.TryCreate` and read `.Host` / `.UserInfo` directly |

### 5.4 Run commands

```sh
dotnet test tests/Shared.Kernel.Tests                                                                  # C1, C6, R4
dotnet test tests/AuditObservability.Application.Tests --filter "FullyQualifiedName~AuditPayload"     # C1, C7, R5
dotnet test tests/CameraCatalog.Domain.Tests --filter "FullyQualifiedName~RtspUrl"                     # C1, C2, C4, R1
dotnet test tests/StreamDistribution.Domain.Tests --filter "FullyQualifiedName~StreamSourceUrl"        # C1, C3, C4, R2
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~CameraUrl|FullyQualifiedName~UrlAuthoritySingleSource|FullyQualifiedName~FromStored|FullyQualifiedName~CameraRegisterEndpoint|FullyQualifiedName~CameraPatchEndpoint|FullyQualifiedName~PrimitiveBoundary"   # C1, C5, R3, R6, G1, G2
dotnet test tests/StreamDistribution.Application.Tests --filter "FullyQualifiedName~LegacyMisreadSource|FullyQualifiedName~LegacyAuthorityHost|FullyQualifiedName~LegacyZoneDelimiter|FullyQualifiedName~CameraAddressChanged|FullyQualifiedName~CameraRegistered"   # C1, R7
dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~StreamSourceUrlHydration|FullyQualifiedName~CameraEventFailurePolicy"   # C1
```

## 6. Commit plan — one PR

Each commit builds on its own (ADR-0087); the red commit leaves its suites failing until the next commit
(CLAUDE.md "Builds is not passes", accepted).

1. `test(3023): characterise the zone, delimiter and unreadable-host rows before the change` — C2-C7 (green; counts quoted).
2. `test(3023): a delimiter inside the brackets and an unreadable host are refused, not accepted or thrown (red)` — R1-R7, G1, G2.
3. `fix(3023): refuse '@', '?' and '#' inside the IPv6 brackets; an unreadable host or userinfo is a 400` — §3.1-§3.3. All of §5 green.
4. `test(3023): direct rows for the new UrlAuthority members` — T060 (post-green).
5. Adversary findings (T070), as separate `test` / `fix` commits.

## 7. Existing tests whose assertions change

**None.** Found by `grep -rnF -e '%@' -e '%25@' -e ':/64]' -e '7:8:/' -e '7:/64' -e 'a?b' -e 'x?y' -e '10.0.0.66'`
and `grep -rnoE '"[a-zA-Z]+:[^"]*\[[^]"]*[?#@][^]"]*\][^"]*"'` over `tests/`, `src/`, `apps/` at `fbd4176b`,
each hit read:

- `AuditPayloadDetailChangedUserinfoBracketTests.cs:44-45` (`rtsp://us[er@[fe80::1]/x`) — the bracket reading
  after the `@` is `[fe80::1]`, no delimiter; `DetailChanged` stays `null`.
- `UrlRedactionAuthorityHostAdversarialTests.cs:29` (`rtsp://[@]` among degenerate inputs that must not throw) —
  the new predicate is span-only and cannot throw; `ToOrigin` was already the placeholder.
- `RtspUrlAuthorityHostAdversarialFindingTests.cs:156` / its StreamDistribution twin (`/a?b#c` as a terminator
  piece, after the `]`) — outside the brackets; the fuzz asserts only that accepted inputs match the parser, and
  this spec only narrows the accepted set.
- No test pins `IndexOutOfRangeException` or a 500 for any FR-002 / FR-003 row
  (`grep -rn IndexOutOfRange tests` finds three unrelated comments).

If a branch merged after `fbd4176b` adds a row of this kind, T001 finds it and it is listed in the PR.

## 8. Risks

- **R-1 — the scan disagrees with the parser.** Over-flagging is fail-closed (placeholder; `From` refuses).
  Mitigation: R3 agreement, and the adversary pass (T070) fuzzing delimiters at every position inside and around the
  brackets, with and without userinfo.
- **R-2 — accepted input narrows** (spec A-6) for API clients and the UI. Decided by #3023's lane scope.
- **R-3 — stored FR-001 rows stay readable and visible in clear** through `CameraDto` until #3016, and the stream
  reconciler keeps re-provisioning them in MediaMTX (whose Go parser refuses the userinfo `@[`). Not made worse.
- **R-4 — a pre-deploy event with an FR-001 address dead-letters** (spec 389 R-2/R-3 apply unchanged).
- **R-5 — the FR-002 message wins over the spec 392 slash message** for inputs both would refuse; by position,
  and recorded (spec FR-002 ordering note).
- **R-6 — a throwing `Uri` read survives in a consumer this spec leaves alone** (`ToOrigin`'s later reads,
  `CarriesZone`, `RtspPath`). 0 reaching inputs in 700,000; the adversary is asked to find one (T070).
- **R-7 — wording drift between the two value objects.** R1/R2 pin the exact literals in both; R3 asserts the two
  `From`s agree.
