# Plan 411: A camera URL's IPv6 zone holds only printable ASCII, a host of only dots names nothing, and the audit's bracket scan folds onto `UrlAuthority`

**Spec**: [spec.md](./spec.md) · **Tasks**: [tasks.md](./tasks.md) · **Issue**: #3023 (items 3 [zone half], 6, 7; the
#3031 security-review residuals). Item 4 and item 3's host half are owner questions (spec §9).

## 1. Constitution and ADR check

| Gate | Result |
|---|---|
| §II primitives on domain models | Pass. The value objects keep their single `Value`. The new `UrlAuthority` members are stateless and return `bool`. |
| §III no cross-context references | Pass. CameraCatalog, StreamDistribution and AuditObservability already reference `Shared.Kernel`. No project reference is added. PR 2 *removes* duplicated locating code from AuditObservability in favour of `Shared.Kernel`. |
| §IV latency | N/A (spec header). |
| ADR-0105 guards | Each new public `UrlAuthority` member starts with `Ensure.That(url).IsNotNull()` (or `host`). Messages carry no value. |
| ADR-0047/0089 errors | No new code: 400 `CAMERA_INVALID_REQUEST` (through `BoundaryParse`) and `STREAM_INVALID_RTSP_SOURCE`. |
| ADR-0042/0088 Wolverine | No policy change. Replay dead-letters as in spec 389 (spec FR-005). |
| ADR-0139/0144 colours | PR 1 is **red** for FR-001, FR-002 and FR-006, with characterisation for every unchanged row of spec §6. **No existing row moves**: a probe of the 920 `rtsp://` literals in `tests/` found 4 that the new rules would refuse, and all 4 sit in `ToOrigin` / audit suites, which A-1 leaves unchanged; T001 confirms by counterfactual. PR 2 is **characterisation** only. The two never share a PR. No ADR, no constitution change, no gate weakened. The fuzz floor (A-8) is a block, never an edit. |
| ADR-0036 | No drive-by changes. #3031 items 3-8 are not touched. PR 2 does not touch `CarriesZone`'s `Uri` arm. |
| ADR-0065 coverage | New members are covered by red rows plus direct post-green rows (T060). PR 2's moved member is covered by the existing audit suites and C-F1. |
| ADR-0084 metrics | Advisory. `NonPrintableCharacterInsideTheZone` is likely to exceed complexity 10 if written in one block: extract the zone scan (`ZoneHasNonPrintable(ReadOnlySpan<char>)`), as spec 408 extracted for S1541. |
| New ADR needed? | **No** for PR 1 and PR 2. Q1 (spec §9) might be, and it is the owner's. |

## 2. Layers touched

| PR | Context / project | Layer | Files |
|---|---|---|---|
| 1 | Shared.Kernel | n/a | `src/Shared.Kernel/UrlAuthority.cs` (two public members, one private helper) |
| 1 | CameraCatalog | Domain | `src/CameraCatalog/Domain/Camera/RtspUrl.cs` |
| 1 | StreamDistribution | Domain | `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` |
| 2 | Shared.Kernel | n/a | `src/Shared.Kernel/UrlAuthority.cs` (one public member, built on `BracketedHostReadings`) |
| 2 | AuditObservability | Application | `src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs` (two private members deleted, one call) |

Not touched by either PR: `UrlRedaction.cs` (spec A-1), `FromStored` / `EnsureStoredInvariants`, the EF converters,
Api, Contracts, Infrastructure, migrations, AppHost, frontend, Helm and the simulator.

**`UrlAuthority.cs` is touched by both PRs, so they run in sequence (PR 1, then PR 2) and never in parallel.** PR 2
is cut from `develop` after PR 1 merges. It is not stacked (CLAUDE.md: stack only when a PR cannot build on
`develop` alone).

## 3. Shapes (PR 1)

### 3.1 `Shared.Kernel.UrlAuthority`: two new public members

```csharp
// FR-001. RFC 6874 makes a zone ASCII (unreserved / pct-encoded); a reader that decodes it (Go net/url) and one
// that does not (Uri) see different interface names, and a decoded control character is a CR/LF in the address.
public static bool NonPrintableCharacterInsideTheZone(string url);

// FR-002. A host of only '.', '-' or '_' (".", "..", "-") is never resolvable; deliberately not "has an ASCII
// letter or digit", which would refuse IDN hosts (spec §9 Q1 is the owner's).
public static bool IsPunctuationOnlyHost(string host);
```

The names are fixed here because the structural guard (§5.3 G1) is written before the code and scans for them. An
engineer who wants other names changes this plan first, not the test.

- `NonPrintableCharacterInsideTheZone` iterates `BracketedHostReadings(url)` like the three bracket predicates next to
  it (spans only, no `Uri`, cannot throw). For each reading that has a `]` at index > 0, it takes `content = host[1..close]`.
  `zone = content[(content.IndexOf('%') + 1)..]` when there is a `%`, minus a leading `"25"`. A private
  `ZoneHasNonPrintable(ReadOnlySpan<char> zone)` then scans it:
  - `%` followed by two `char.IsAsciiHexDigit` characters is one octet (`HexConverter`-free: two nibble reads).
  - Everything else is one code unit.
  - It answers true when any value is `< 0x21 || > 0x7E`.

  The behaviour is fixed by spec FR-001 and the §6 rows. The syntax is the engineer's.
- `IsPunctuationOnlyHost(host)`: `host.Length > 0 && host.All(c => c is '.' or '-' or '_')`, declared next to
  `IsBlankHost`.
- Each comment says *why* and is one or two lines (CLAUDE.md: no drive-by comments).

### 3.2 Value objects (identical shape in both)

```csharp
EnsureStoredInvariants(value)
    .Satisfies(url => !UrlAuthority.AtSignFollowsTheAuthority(url), AtSignAfterAuthorityMessage)
    .Satisfies(url => UrlAuthority.TryReadHost(url, out _), UnreadableHostMessage)
    .Satisfies(HasHost, EmptyHostMessage)
    .Satisfies(url => !UrlAuthority.TextFollowsTheClosingBracket(url), TextAfterBracketMessage)
    .Satisfies(url => !UrlAuthority.SlashInsideTheBracketsFollowsNoAddress(url), SlashInBracketsMessage)
    .Satisfies(url => !UrlAuthority.DelimiterInsideTheBrackets(url), DelimiterInsideBracketsMessage)
    .Satisfies(url => !UrlAuthority.ContainsControlOrWhiteSpace(url), ControlOrWhiteSpaceMessage)
    .Satisfies(url => !UrlAuthority.NonPrintableCharacterInsideTheZone(url), NonPrintableZoneMessage)      // new
    .Satisfies(HostIsNotPunctuationOnly, PunctuationOnlyHostMessage);                                       // new

private static bool HostIsNotPunctuationOnly(string url) =>
    UrlAuthority.TryReadHost(url, out var host) && !UrlAuthority.IsPunctuationOnlyHost(host);
```

`TryReadHost` is already known to be true at that point, because an older rule refused otherwise, so the `&&` form
cannot refuse anything new by accident. The two messages are `private const string`, declared once per value object,
holding the exact spec FR-001 and FR-002 texts. The doc comment gains one sentence ("… a zone holding anything but
printable ASCII, and a host of only '.', '-' or '_' (spec 411)").

**Invariants after PR 1**

| Factory | stored invariants | raw `@` | host readable | host not blank | text after `]` | `/` in brackets | `@?#` in brackets | control / whitespace | **zone printable ASCII** | **host not punctuation-only** |
|---|---|---|---|---|---|---|---|---|---|---|
| `From` (all writes) | yes | yes | yes | yes | yes | yes | yes | yes | **yes** | **yes** |
| `FromStored` (EF read only) | yes | no | no | no | no | no | no | no | no | no |

## 4. Shapes (PR 2): the fold

```csharp
// Shared.Kernel.UrlAuthority
// The audit asks whether a bracketed host holds only address text (no zone, prefix length or other text), because
// the parser drops a zone written behind a prefix length from DnsSafeHost. A bracket in the userinfo, or no closing
// ']', is "not address-only": it must read as unknown, never as the same host.
public static bool BracketContentIsAddressOnly(string url);
```

- It is built on the existing private `BracketedHostReadings(url)`. It answers true only when at least one reading
  has a `]` at index > 0 and **every** such reading's content is non-empty ASCII hex, `:` or `.`.
- It **keeps the userinfo-bracket arm** that the audit has today: locate the authority with
  `TextFromTheAuthority(url.AsSpan().Trim())`; if a `[` or `]` occurs before the last `@` of the userinfo, answer
  false. Without that arm the fold diverges on 110 of the 725 reachable fuzz inputs (spec §1.7). With it, 0 diverge.
- `AuditPayloadRedaction.CarriesZone` becomes
  `… && (uri.DnsSafeHost.Contains('%') || !UrlAuthority.BracketContentIsAddressOnly(url))`. The private
  `BracketContentIsAddressOnly` and `IsAddressText` are deleted. The `Uri.TryCreate` / `DnsSafeHost` arm stays exactly
  as it is (#3031 item 4 is not enrolled).
- If the engineer finds a cleaner shape (for example exposing the readings themselves), the obligation does not
  change: C-F1 and every existing suite pass **unmodified**. A row that would have to change is a block, filed as its
  own issue. It is never adjusted.

## 5. Messaging

No domain event, integration event or contract changes in either PR. Replay (PR 1): spec 389 §4 applies unchanged.
A pre-deploy `CameraRegisteredV1` / `CameraAddressChangedV1` carrying an FR-001 or FR-002 address goes to the error
queue on its first StreamDistribution attempt, with one Warning that names `STREAM_INVALID_RTSP_SOURCE` and no part
of the address (`ProvisionStreamCommandHandler.cs:33`, `RepointStreamCommandHandler.cs:108`). The audit consumer does
not call `From` and is unaffected (A-1). The simulator does not call `From`.

## 6. Testing strategy

### 6.1 PR 1, characterisation (captured green on the branch tip before any production edit, unmodified after)

| ID | Test | Guards |
|---|---|---|
| C1 | existing suites of §6.4 | quote the pass counts; nothing that specs 389/392/394/408 pinned moves |
| C2 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlZoneCharsetCharacterisationTests.cs` | spec §6 accept set (incl. the "pending Q1/Q2" rows, each named in its display name) accepted by `From` and round-tripping through `FromStored`; spec §6 "older message" set refused with today's exact message |
| C3 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlZoneCharsetCharacterisationTests.cs` | same for `StreamSourceUrl` |
| C4 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredZoneCharsetTests.cs`, `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredZoneCharsetTests.cs` | `FromStored` reads back every FR-001 and FR-002 row |
| C5 | **new** `tests/Architecture.Tests/Persistence/CameraUrlZoneCharsetHydrationCharacterisationTests.cs` | both EF converters' `ConvertFromProvider` return the stored value for the C4 rows (pattern `CameraUrlControlCharacterHydrationCharacterisationTests`) |
| C6 | **new** `tests/Shared.Kernel.Tests/UrlRedactionZoneCharsetCharacterisationTests.cs` | `ToOrigin` for every FR-001 and FR-002 row exactly as spec §6 "`ToOrigin` (characterisation)" (A-1) |
| C7 | **new** `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadZoneCharsetCharacterisationTests.cs` | through `IntegrationEventAuditHandler`: `DetailChanged` is `null` for `rtsp://[fe80::1%25eth0]/s` to `rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]/s` in both directions; `rtsp://../a` to `rtsp://../b` stays `true`; stored `Url` as C6 |

C2-C7 are characterisation of rows the new rules *do not* change, plus the A-1 decision. They cannot be red. No
Aspire integration test is added (hydration goes through the converter, as in specs 394 and 408; phase 5 asks the
running system once), so no `ci-shards/shard-N.filter` entry is needed.

### 6.2 PR 1, red (observed failing before production code)

| ID | Test | Red today because |
|---|---|---|
| R1 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlZoneCharsetTests.cs`: every spec §6 FR-001 row throws `ArgumentException`, message exactly `value: <FR-001> (Parameter 'value')`, `ParamName` `value`, `ToString()` free of the row's marker (`X-Inject`, `SECRET`) | accepted |
| R2 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlPunctuationHostTests.cs`: every FR-002 row, exact message | accepted |
| R3 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlZoneCharsetTests.cs` and `…/StreamSourceUrlPunctuationHostTests.cs`: same | accepted |
| R4 | **new** `tests/Architecture.Tests/Persistence/CameraUrlZoneCharsetRefusalTests.cs`: FR-006 agreement over the union of spec §6 sets, where the two `From`s give the same outcome and message on every row and every FR-001 / FR-002 row is refused | the new rows are accepted |
| R5 | **new** `tests/Architecture.Tests/CameraUrlZoneCharsetEndpointTests.cs` (existing `CameraCatalogEndpointHost`, pattern `CameraUrlControlCharacterEndpointTests`): `POST /cameras` and `PATCH /cameras/{id}` with `rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]:8554/live`, `rtsp://[fe80::1%25eth0‎SECRET]/s` and `rtsp://..` answer 400 `CAMERA_INVALID_REQUEST` with the rendered detail, body free of the marker, handler not invoked; one PATCH with a stale If-Match still answers 400; a name-only PATCH of a camera seeded with an FR-001 address answers 204 | 201 / 204 |
| R6 | **new** `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyZoneCharsetReplayTests.cs` (harness of `LegacyControlCharacterReplayTests`): `CameraRegisteredV1` with `rtsp://[fe80::1%25eth0%0D%0AX-Inject:%201]/s` throws `InvalidOperationException` naming `STREAM_INVALID_RTSP_SOURCE`, saves nothing, makes no gateway call and logs one Warning free of `X-Inject`; `CameraAddressChangedV1` to `rtsp://..` onto a seeded stream throws and leaves the address unchanged | provisions / re-points |

Expected red: R1-R3 "did not throw"; R4 the agreement fails on the new rows; R5 201/204; R6 "did not throw" and a
gateway call recorded. The name-only PATCH row of R5 and the agreement-of-outcomes half of R4 pass on arrival. That
is fine and stated in the PR as such: they are guards that ride along, not the red evidence.

### 6.3 PR 1, structural guard (red before the code)

| ID | Test | Red today because |
|---|---|---|
| G1 | `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs`, **additive**: a theory over `RtspUrl.cs`, `StreamSourceUrl.cs` matching `\bUrlAuthority\s*\.\s*NonPrintableCharacterInsideTheZone\s*\(` and `\bUrlAuthority\s*\.\s*IsPunctuationOnlyHost\s*\(`; a fact that only `src/Shared.Kernel/UrlAuthority.cs` declares `\bbool\s+NonPrintableCharacterInsideTheZone\s*\(` and `\bbool\s+IsPunctuationOnlyHost\s*\(` (slashes normalised) | not declared or called |

No red test calls the new members directly (a compile error is not a red). G1 reaches them by source scan. T060 adds
direct rows after green.

### 6.4 PR 1 run commands

```sh
dotnet test tests/CameraCatalog.Domain.Tests --filter "FullyQualifiedName~RtspUrl"                     # C1 C2 C4 R1 R2
dotnet test tests/StreamDistribution.Domain.Tests --filter "FullyQualifiedName~StreamSourceUrl"        # C1 C3 C4 R3
dotnet test tests/Shared.Kernel.Tests --filter "FullyQualifiedName~Url"                                # C1 C6 T060
dotnet test tests/AuditObservability.Application.Tests --filter "FullyQualifiedName~AuditPayload"     # C1 C7
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~CameraUrl|FullyQualifiedName~UrlAuthoritySingleSource|FullyQualifiedName~FromStored|FullyQualifiedName~CameraRegisterEndpoint|FullyQualifiedName~CameraPatchEndpoint|FullyQualifiedName~PrimitiveBoundary"   # C1 C5 R4 R5 G1, incl. the A-8 fuzz floor
dotnet test tests/StreamDistribution.Application.Tests --filter "FullyQualifiedName~Legacy|FullyQualifiedName~CameraAddressChanged|FullyQualifiedName~CameraRegistered"   # C1 R6
```

Light builds, these projects only. Stop any running AppHost first.

### 6.5 PR 2, characterisation (observed green before the fold, unmodified after)

| ID | Test | Guards |
|---|---|---|
| C-F0 | every existing suite that reaches `CarriesZone` or `BracketedHostReadings`: `AuditObservability.Application.Tests` (full), `Shared.Kernel.Tests` `~UrlAuthority\|~UrlRedaction`, `Architecture.Tests` `~CameraUrl\|~UrlAuthoritySingleSource`, both domain suites `~RtspUrl` / `~StreamSourceUrl` | quote counts before and after; identical |
| C-F1 | **new** `tests/Architecture.Tests/AuditPayloadBracketFoldCharacterisationTests.cs`. It holds a private **oracle**: today's `CarriesZone`, `BracketContentIsAddressOnly` and `IsAddressText`, copied verbatim from `AuditPayloadRedaction.cs:150-202` at the PR 2 base, with `UrlRedaction.ToOrigin` and the placeholder check exactly as `DetailChanged` has them. For every address drawn from the spec 394 fuzz generators (the five `Corpora` seeds; `CameraUrlZoneDelimiterFuzzAdversarialTests.Generate` at `:311` is `private`, so make it `internal`. That is a one-word visibility change with no assertion touched, and it goes in commit 1. Do not copy the generator) **paired with** `rtsp://[fe80::1]/s` in both directions, plus the explicit rows `rtsp://[]@[FE80::1]`, `rtsp://[x]@[::1]:/s?x#y`, `rtsp://a@b@[fe80::1%25eth0]/x`, `rtsp://u:p@[fe80::1/64%25eth0]/x` and `  rtsp://[fe80::1]/x  `: `Serialize(CameraAddressChangedV1)`'s `DetailChanged` equals the oracle's. It also asserts the corpus reaches at least 700 inputs on which the bracket check is evaluated (measured 725) and at least 100 that hold a bracket in the userinfo (measured 110), so it cannot pass vacuously | the fold changes no audit outcome |

**Counterfactual for C-F1 (required in the PR body, per memory "prove a guard by counterfactual").** With the fold
applied *without* the userinfo-bracket arm, C-F1 must fail, and the failing count is quoted (expected about 110
pairs times 2 directions). Then restore the arm. A C-F1 that cannot fail is not characterisation.

### 6.6 PR 2, post-fold structure guard (green on arrival, stated as such)

| ID | Test | Guards |
|---|---|---|
| G2 | `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs`, additive: `AuditPayloadRedaction.cs` contains no `TextFromTheAuthority(` and no `IndexOfTheTerminator(` call, and calls `UrlAuthority.BracketContentIsAddressOnly(` | the duplication stays gone. Its counterfactual (reinstate the local scan, so G2 goes red) is quoted. It is a structure guard, not behaviour, so arriving green is not a phase-4a failure |

## 7. Commit plan

**PR 1** (`fix/3023-camera-url-authority-residuals`). Each commit builds on its own (ADR-0087). The red commit
leaves its suites failing until the fix commits land (CLAUDE.md "Builds is not passes", accepted).

1. `test(3023): characterise zone, host and audit rows before the zone and host rules` (C2-C7; counts quoted).
2. `test(3023): a non-printable zone and a punctuation-only host are refused (red)` (R1-R6, G1).
3. `fix(3023): refuse a camera URL whose IPv6 zone is not printable ASCII` (FR-001; R1, R3-zone, R4, R5, R6 first row and G1-zone green).
4. `fix(3023): refuse a camera URL whose host is only dots, hyphens or underscores` (FR-002; the rest green).
5. `test(3023): direct rows for the zone and host predicates` (T060, post-green).
6. Adversary findings (T070), as separate `test` / `fix` commits.

**PR 2** (`refactor/3023-bracket-scan-fold`, from `develop` after PR 1 merges):

1. `test(3023): characterise the audit's bracket check before the fold` (C-F1; C-F0 counts quoted).
2. `refactor(3023): the audit asks UrlAuthority whether a bracketed host is address-only` (§4; C-F0 and C-F1 unmodified and green; counterfactual quoted).
3. `test(3023): guard that the audit no longer locates the authority itself` (G2).

## 8. Existing tests whose assertions change

**None in either PR.** The evidence is a probe of all 920 distinct `rtsp://` string literals under `tests/`. Four
are refused by FR-001: `AuditPayloadDetailChangedZoneAdversarialTests.cs:41`,
`UrlRedactionHostCharsetAdversarialTests.cs:94-95` and `UrlRedactionHostCharsetTests.cs:35`. All four are `ToOrigin` /
audit rows that never call `From`, and A-1 leaves them unchanged. Interpolated and concatenated literals are not
covered by that probe, so **T001 runs the real counterfactual** (rules applied in the worktree, the §6.4 unit suites
run, `git checkout -- src`) and lists any hit in this section and in the PR. If a hit is a row that pins acceptance,
moving it is the owner's call (spec 408 A-2 precedent). The lane flags it and blocks.

## 9. Risks

- **R-1: accepted input narrows** for zones that are not printable ASCII and for hosts made only of punctuation
  (spec A-2, A-3, A-4). No real interface name or resolvable host is affected, as reasoned in spec §1.6 and §7.
- **R-2: `From` and `ToOrigin` disagree on the new rows** (spec A-1): refused but rendered. This is deliberate and
  pinned (C6).
- **R-3: the fuzz floor margin is 11** (spec A-8). Re-measure at T001. Below 400 is a block.
- **R-4: replay dead-letters** pre-deploy events with such addresses (spec FR-005).
- **R-5: PR 2's fold diverges where the corpus does not reach.** Mitigation: C-F1's reach floors, the
  userinfo-bracket rows, and the counterfactual.
- **R-6: wording drift between the value objects.** R1-R3 pin the exact literal in both, and R4 asserts agreement.
