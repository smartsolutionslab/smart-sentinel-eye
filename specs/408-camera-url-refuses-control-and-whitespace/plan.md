# Plan 408: A camera URL holding a control character or whitespace is refused, and the simulator drops an address the parser cannot read

**Spec**: [spec.md](./spec.md) · **Tasks**: [tasks.md](./tasks.md) · **Issue**: #3031 (items 1, 2)

## 1. Constitution and ADR check

| Gate | Result |
|---|---|
| §II primitives on domain models | Pass. The value objects keep their single `Value`. The two new `UrlAuthority` members are stateless and return `bool`; `PrimitiveBoundaryTests` unaffected. |
| §III no cross-context references | Pass. CameraCatalog, StreamDistribution, AuditObservability and ScenarioSimulator already reference `Shared.Kernel`. No project reference added. The two value objects stay separate copies by design (`StreamSourceUrl`'s doc comment). |
| §IV latency | N/A (spec header). |
| ADR-0105 guards | Each new public `UrlAuthority` member guards `url` with `Ensure.That(url).IsNotNull()`. The message carries no value. |
| ADR-0047/0089 errors | No new code. 400 `CAMERA_INVALID_REQUEST` (via `BoundaryParse`); `STREAM_INVALID_RTSP_SOURCE`. |
| ADR-0042/0088 Wolverine | No policy change. The simulator stops throwing for the item 2 shapes; it gains no policy. |
| ADR-0139/0144 | Behaviour-changing: red for FR-001, FR-002, FR-005, FR-006; characterisation for every unchanged row of spec §5. Six existing rows move (spec §1.4, A-2), each in the red commit by the test-writer, never by the engineer. No ADR, no constitution change, no gate weakened (each moved row asserts a *stricter* outcome). |
| ADR-0036 / no drive-by error handling | `RtspPath`'s `catch (IndexOutOfRangeException)` is at a trust boundary (a broker message into a BCL parser), mirrors `UrlRedaction.TryParse`, and is reached by observed inputs (spec §1.3) that a pre-spec-392 event can carry. |
| ADR-0065 coverage | New `UrlAuthority` members covered by red rows plus direct post-green rows (T060). |
| ADR-0084 metrics | Advisory. Each new member is a handful of lines. |
| New ADR needed? | **No** (spec header). |

## 2. Layers touched

| Context / project | Layer | Files |
|---|---|---|
| Shared.Kernel | — | `src/Shared.Kernel/UrlAuthority.cs` (two public members), `src/Shared.Kernel/UrlRedaction.cs` (one `||` in `ToOrigin`) |
| CameraCatalog | Domain | `src/CameraCatalog/Domain/Camera/RtspUrl.cs` |
| StreamDistribution | Domain | `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` |
| ScenarioSimulator | CameraSim (dev tooling) | `src/ScenarioSimulator/CameraSim/RtspPath.cs` |

Not touched: `AuditPayloadRedaction.cs` (its outcome changes only through `ToOrigin`), `CameraRegisteredSimHandler.cs`
and `Log.cs` (existing skip branch and message), the EF converters, any Api, Application, Contracts,
Infrastructure, migration, AppHost, frontend or Helm file.

## 3. Shapes

### 3.1 `Shared.Kernel.UrlAuthority` — two new public members

```csharp
// FR-001. True when any code unit is a control character (Cc) or white space. The set is char.IsControl ∪
// char.IsWhiteSpace — 84 code units, all in the BMP — and deliberately not IsBlankHost's wider set (no Cf).
public static bool ContainsControlOrWhiteSpace(string url);

// FR-002. True when such a code unit has a non-member on both sides: leading and trailing runs are ignored,
// because the parser trims them and they never reach the host ToOrigin renders.
public static bool ContainsControlOrWhiteSpaceWithinTheText(string url);
```

Names are fixed here because the structural guard (§5.3 G1) is written before the code and scans for them; an
engineer who wants other names changes this plan first, not the test.

- Both: `Ensure.That(url).IsNotNull();`, spans only, no `Uri`, cannot throw (`ToOrigin` calls the second on
  untrusted text). One private `IsControlOrWhiteSpace(char)` serves both; it is the same set as the existing private
  `IsTrimmedByTheParser`, so **reuse that member** (rename it only if the engineer judges the name now misleads —
  then in the same commit, no behaviour change, and `BracketedHostReadings` keeps calling it).
- The second is the first applied to the text between the first and last non-member
  (`span.TrimStart(...)`/`TrimEnd(...)` over the set, or two index scans). It must be exactly that: the counterfactual
  of spec §1.4 was run with this definition, and a span `.Trim()` (white space only, not controls) is **not** the
  same function.
- Declared next to the other public predicates. The comments state *why* (request-line injection for the first; the
  parser trims the edges and pinned decisions keep them rendering, spec A-1, for the second), not *what*.

### 3.2 `UrlRedaction.ToOrigin`

```csharp
if (UrlAuthority.AtSignFollowsTheAuthority(url)
    || UrlAuthority.TextFollowsTheClosingBracket(url)
    || UrlAuthority.SlashInsideTheBracketsFollowsNoAddress(url)
    || UrlAuthority.DelimiterInsideTheBrackets(url)
    || UrlAuthority.ContainsControlOrWhiteSpaceWithinTheText(url)   // new, FR-002
    || !TryParse(url, out var uri)
    ...
```

Position in the `||` chain does not change the result (every arm yields the placeholder) and the new arm cannot
throw; it sits with the other span predicates, before the `Uri` reads. Nothing else in `UrlRedaction` changes.

### 3.3 Value objects (identical shape in both)

```csharp
public static RtspUrl From(string value)
{
    EnsureStoredInvariants(value)
        .Satisfies(url => !UrlAuthority.AtSignFollowsTheAuthority(url), AtSignAfterAuthorityMessage)
        .Satisfies(url => UrlAuthority.TryReadHost(url, out _), UnreadableHostMessage)
        .Satisfies(HasHost, EmptyHostMessage)
        .Satisfies(url => !UrlAuthority.TextFollowsTheClosingBracket(url), TextAfterBracketMessage)
        .Satisfies(url => !UrlAuthority.SlashInsideTheBracketsFollowsNoAddress(url), SlashInBracketsMessage)
        .Satisfies(url => !UrlAuthority.DelimiterInsideTheBrackets(url), DelimiterInsideBracketsMessage)
        .Satisfies(url => !UrlAuthority.ContainsControlOrWhiteSpace(url), ControlOrWhiteSpaceMessage);   // new, last
    return new RtspUrl(value);
}
```

`ControlOrWhiteSpaceMessage` is a `private const string` holding the spec FR-001 text, declared once per value
object. `FromStored` and `EnsureStoredInvariants` are untouched. The doc comment gains one sentence. The order is
fixed (spec FR-001); the syntax is the engineer's.

**Invariants after the change**

| Factory | length / `rtsp://` / userinfo | raw `@` | host readable | host not blank | no text after `]` | `/` in brackets | no `@?#` in brackets | **no control / white space** |
|---|---|---|---|---|---|---|---|---|
| `From` (all writes) | yes | yes | yes | yes | yes | yes | yes | **yes** |
| `FromStored` (EF read only) | yes | no | no | no | no | no | no | no |

### 3.4 `RtspPath.TryExtract` (ScenarioSimulator)

```csharp
public static bool TryExtract(string rtspUrl, out string path)
{
    path = string.Empty;

    if (string.IsNullOrWhiteSpace(rtspUrl) || !TryReadPath(rtspUrl, out var absolutePath))
    {
        return false;
    }

    var candidate = absolutePath.Trim('/');
    ...
}

// Uri throws IndexOutOfRangeException from TryCreate for some inputs and from AbsolutePath / Host for a ':' left
// before a '/' prefix length ([1:2:3:4:5:6:7:8:/64]); a throw here would retry the handler instead of dropping.
private static bool TryReadPath(string rtspUrl, [NotNullWhen(true)] out string? absolutePath)
{
    try
    {
        // every Uri call TryExtract makes goes inside this try
    }
    catch (IndexOutOfRangeException)
    {
        absolutePath = null;
        return false;
    }
}
```

Illustrative, not prescriptive: the obligation is that **every** `Uri` call `TryExtract` makes — `TryCreate`,
`AbsolutePath`, and spec 406's `uri.Host.Length == 0` if it has landed — sits inside the one `try`, catching
`IndexOutOfRangeException` only (spec A-5). If the helper must return more than the path once 406's host check is in
(host emptiness), the engineer folds the check into the guarded block; it does not get a second `try`.

**Ordering with spec 406 (#3025, `sse-3025`, committed, unmerged).** Both edit the same `if` in `TryExtract`.
Whichever merges second rebases and puts the other's `Uri` read inside the guard. If 406 is first, its `uri.Host`
read is the one that throws on `rtsp://[1:2:3:4:5:6:7:8:/64]/live` and `rtsp://[::1:2:3:4:5:6:7:/64]/s`, so those two
rows move from characterisation (`live` / `s`) to red (false). T050 checks which tip it is on and records it. New
test files here are named so they cannot collide with 406's `tests/ScenarioSimulator.Tests/RtspPathTests.cs`.

## 4. Messaging

No domain event, integration event or contract changes. `CameraRegisteredV1` / `CameraAddressChangedV1` keep
carrying the full URL; the audit copy changes only through `ToOrigin` (FR-002). Replay: spec 389 §4 applies
unchanged — a pre-deploy event carrying an FR-001 address is moved to the error queue on its first StreamDistribution
attempt with one Warning naming `STREAM_INVALID_RTSP_SOURCE` and no part of the address. The simulator's queue
(`scenario-simulator.*`) receives the same events; it does not call `From`, so item 1 does not change what it does
with them, and item 2 turns its one throwing class into an acknowledged skip.

## 5. Testing strategy

Phase 4a is **red** for the new behaviour, with **characterisation** for every unchanged row, in separate files and
separate commits. Ambiguity resolves to red (ADR-0144). Red tests reach behaviour only through members that exist at
`c8c3773c` (`From`, `FromStored`, `ToOrigin`, `AuditPayloadRedaction.Serialize` / the audit handler, the endpoint
host, the stream handlers, `RtspPath.TryExtract`, the simulator handler rig) or through source scanning; the two new
`UrlAuthority` members are called directly only in post-green rows (T060). A compile error is not a red.

Raw characters: tests build inputs with C# escapes (`"\r\n"`, `"\u0000"`) or `(char)codePoint`, never by reading a
file; theory data that holds a NUL or a lone control is named by code point in the display name
(`$"U+{cp:X4} in {position}"`) so a failure is readable.

### 5.1 Characterisation — captured green on `c8c3773c` before any production edit, unmodified after

| ID | Test | Guards |
|---|---|---|
| C1 | existing suites of §5.4, **minus the six rows spec §1.4 moves** (run once in full first: they are green today and are quoted as such) | nothing spec 389/392/394 pinned moves; quote the pass counts |
| C2 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlControlCharacterCharacterisationTests.cs` | spec §5 accept set (incl. percent-encoded and the six format characters) accepted by `From` and round-trips through `FromStored`; spec §5 "older message" set refused by `From` with today's exact message |
| C3 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlControlCharacterCharacterisationTests.cs` | same for `StreamSourceUrl` |
| C4 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredControlCharacterTests.cs` and `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredControlCharacterTests.cs` | `FromStored` reads back every FR-001 named row and one row per template |
| C5 | **new** `tests/Architecture.Tests/Persistence/CameraUrlControlCharacterHydrationCharacterisationTests.cs` | both EF converters' `ConvertFromProvider` return the stored value for the C4 rows (pattern `CameraUrlZoneDelimiterHydrationCharacterisationTests`) |
| C6 | **new** `tests/Shared.Kernel.Tests/UrlRedactionControlCharacterCharacterisationTests.cs` | leading/trailing-only rows keep their origins (spec §5); percent-encoded and format-character rows keep theirs; rows already rendering the placeholder (in-host, port, after `]`) stay so; `ToOrigin` does not throw on any 84 x template input |
| C7 | **new** `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadControlCharacterCharacterisationTests.cs` | through `IntegrationEventAuditHandler`: `DetailChanged` is `JsonValueKind.Null` for the zoned CR/LF pair of spec §3 in both directions; `rtsp://cam/x` -> `rtsp://cam/x ` stays `true` (the edge row of `AuditPayloadRedactionPostFixAdversarialTests:98`, re-asserted here so A-1 has its own pin) |
| C8 | **new** `tests/ScenarioSimulator.Tests/RtspPathUnreadableAddressCharacterisationTests.cs` | `TryExtract` outcomes of spec §5 characterisation rows; through `SimHandlerRig`, `rtsp://cam` (no path) logs the skip line and records nothing, as today |

No Aspire integration test is added (hydration through the converter, C5, as spec 394; phase 5 asks the running
system once). No `tests/Integration.Tests/ci-shards/shard-N.filter` entry is therefore needed.

### 5.2 Red — new behaviour, observed failing before production code

| ID | Test | Red today because |
|---|---|---|
| R1 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlControlCharacterTests.cs`: every spec §5 FR-001 row throws `ArgumentException`, message exactly `value: <FR-001> (Parameter 'value')`, `ParamName` `value`, `ToString()` free of the row's marker (`X-Inject`, `CSeq`, `SECRET`). Includes the 84 x template theory. **Receives retired row 1** (`rtsp://[fe80::1]/x `), deleted from `RtspUrlAuthorityHostAdversarialTests.cs:151` in the same commit. | accepted |
| R2 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlControlCharacterTests.cs`: same; **receives retired row 2** (deleted from `StreamSourceUrlAuthorityHostAdversarialTests.cs:151`) | accepted |
| R3 | **new** `tests/Architecture.Tests/Persistence/CameraUrlControlCharacterRefusalTests.cs`: FR-006 agreement over the union of spec §5 sets — a row `From` refuses with the FR-001 message and that has a non-member at both ends renders the placeholder; the two `From`s agree row by row (same outcome, same message) | interior rows accepted with a rendered origin |
| R4 | **new** `tests/Shared.Kernel.Tests/UrlRedactionControlCharacterTests.cs`: every interior FR-001 row -> `UrlRedaction.Unparseable`. **Receives retired rows 4-6** (`rtsp://admin:12345 @cam/x`, `rtsp://admin:12345\t@cam/x`, `rtsp://admin:12345 @cam/s` -> placeholder), deleted from `UrlRedactionUserInfoAdversarialTests.cs:162-163` and `UrlRedactionUserInfoAsHostTests.cs:147` | rendered (`rtsp://10.0.0.66`, `rtsp://[fe80::1]:8554`, `rtsp://cam`) |
| R5 | **new** `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadControlCharacterTests.cs`, through `IntegrationEventAuditHandler`: `CameraRegisteredV1.Url` stored as `<unparseable url>` for the interior named rows; `CameraAddressChangedV1` `rtsp://10.0.0.66/live` <-> `rtsp://10.0.0.66/live\r\nX-Inject: 1` stores the placeholder for that side and `DetailChanged` `null` in both directions; payload free of `X-Inject`. **Receives retired row 3** (the `CSeq` zone row -> `Url` placeholder, `CSeq` absent), deleted from `AuditPayloadZoneDelimiterAdversarialTests.cs:59-69` | origin rendered; `DetailChanged` `true` |
| R6 | **new** `tests/Architecture.Tests/CameraUrlControlCharacterEndpointTests.cs` (existing `CameraCatalogEndpointHost`, pattern `CameraUrlZoneDelimiterEndpointTests`): `POST /cameras` and `PATCH /cameras/{id}` with `rtsp://10.0.0.66/live\r\nX-Inject: 1`, `rtsp://10.0.0.66/live ` and `rtsp://cam /live` -> 400 `CAMERA_INVALID_REQUEST`, detail the rendered FR-001 text, body free of the marker; the handler not invoked; one PATCH with a stale If-Match -> still 400; `rtsp://a b/x` -> 400 with the userinfo text | 201 / 204 |
| R7 | **new** `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyControlCharacterReplayTests.cs` (harness of `LegacyZoneDelimiterReplayTests`): `CameraRegisteredV1` with `rtsp://10.0.0.66/live\r\nX-Inject: 1` -> `InvalidOperationException` naming `STREAM_INVALID_RTSP_SOURCE`, nothing saved, no gateway call, one Warning free of `X-Inject`; newer `CameraAddressChangedV1` with `rtsp://[fe80::1%25eth0\r\nX-Inject: 1]:8554/live` onto a seeded stream -> throws, address unchanged, no gateway call | provisions / re-points |
| R8 | **new** `tests/ScenarioSimulator.Tests/RtspPathUnreadableAddressTests.cs`: the four throwing rows of spec §1.3 and `FILE://‎/x` -> `TryExtract` returns false, `path` empty, `Should.NotThrow`; through `SimHandlerRig` the four rows -> `Handle` completes, one skip entry whose text contains `<unparseable url>` and none of the address, no camera-sim request (`rig.CameraSim` recorded nothing), no correlation entry | `IndexOutOfRangeException` escapes `TryExtract` and `Handle` |

Expected red: R1/R2 "did not throw"; R3 the agreement fails on interior rows; R4 `should be "<unparseable url>" but
was "rtsp://10.0.0.66"`; R5 the rendered origin and `DetailChanged` `true`; R6 201/204; R7 "did not throw" and a
gateway call recorded; R8 "expected no exception but IndexOutOfRangeException was thrown".

### 5.3 Structural guard — red before the code

| ID | Test | Red today because |
|---|---|---|
| G1 | `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs`, **new** theory `Each_value_object_asks_the_control_character_predicate` over `RtspUrl.cs`, `StreamSourceUrl.cs` matching `\bUrlAuthority\s*\.\s*ContainsControlOrWhiteSpace\s*\(`; **new** fact `ToOrigin_asks_the_interior_control_character_predicate` over `UrlRedaction.cs` matching `\bUrlAuthority\s*\.\s*ContainsControlOrWhiteSpaceWithinTheText\s*\(`; **new** fact `Only_UrlAuthority_declares_the_control_character_predicates` (patterns `\bbool\s+ContainsControlOrWhiteSpace\s*\(` and `\bbool\s+ContainsControlOrWhiteSpaceWithinTheText\s*\(`, expected `[src/Shared.Kernel/UrlAuthority.cs]`, slashes normalised). Additive: no existing line of the file changes. | not declared or called |

### 5.4 Run commands

```sh
dotnet test tests/Shared.Kernel.Tests                                                                  # C1, C6, R4
dotnet test tests/AuditObservability.Application.Tests --filter "FullyQualifiedName~AuditPayload"     # C1, C7, R5
dotnet test tests/CameraCatalog.Domain.Tests --filter "FullyQualifiedName~RtspUrl"                     # C1, C2, C4, R1
dotnet test tests/StreamDistribution.Domain.Tests --filter "FullyQualifiedName~StreamSourceUrl"        # C1, C3, C4, R2
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~CameraUrl|FullyQualifiedName~UrlAuthoritySingleSource|FullyQualifiedName~FromStored|FullyQualifiedName~CameraRegisterEndpoint|FullyQualifiedName~CameraPatchEndpoint|FullyQualifiedName~PrimitiveBoundary"   # C1, C5, R3, R6, G1
dotnet test tests/StreamDistribution.Application.Tests --filter "FullyQualifiedName~Legacy|FullyQualifiedName~CameraAddressChanged|FullyQualifiedName~CameraRegistered"   # C1, R7
dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~StreamSourceUrlHydration|FullyQualifiedName~CameraEventFailurePolicy"   # C1
dotnet test tests/ScenarioSimulator.Tests --filter "FullyQualifiedName~RtspPath|FullyQualifiedName~CameraRegisteredSimHandler"   # C1, C8, R8
```

Light builds: these projects only; no Aspire boot before phase 5; stop any running AppHost first.

## 6. Commit plan — one PR

Each commit builds on its own (ADR-0087); the red commit leaves its suites failing until the next commit (CLAUDE.md
"Builds is not passes", accepted).

1. `test(3031): characterise control-character, edge-whitespace and simulator rows before the change` — C2-C8 (green; counts quoted).
2. `test(3031): a control character or whitespace in a camera URL is refused and withheld; the simulator drops an unreadable address (red)` — R1-R8, G1, and the six row moves of spec §1.4 (deleted from their old tables, re-asserted with the new outcome in R1/R2/R4/R5).
3. `fix(3031): refuse control characters and whitespace anywhere in a camera URL; ToOrigin withholds them` — §3.1-§3.3.
4. `fix(3031): the simulator drops an address whose path the parser cannot read` — §3.4. All of §5 green.
5. `test(3031): direct rows for the control-character predicates` — T060 (post-green).
6. Adversary findings (T070), as separate `test` / `fix` commits.

Commits 3 and 4 are separate so a bisect over the simulator does not land in the URL rule and vice versa; each is
green on its own suites.

## 7. Existing tests whose assertions change

**Six rows in five files** — spec §1.4, found by counterfactual (rule applied, unit suites run, reverted), each read.
They move in commit 2, by the test-writer: the row is deleted from its old table and the opposite outcome asserted in
the new red file. No other existing line changes. The literal whole-string reading of FR-002 would have moved ~100
rows across 11 methods and is rejected (spec A-1). If a branch merged after `c8c3773c` adds a row of this kind, T001
finds it and it is listed in the PR.

## 8. Risks

- **R-1 — accepted input narrows** (spec A-6) for API clients and the UI, including a pasted trailing newline.
  Decided by #3031's lane scope.
- **R-2 — `From` and `ToOrigin` disagree on edge-only rows** (spec A-1): `rtsp://cam/x ` is refused but rendered as
  `rtsp://cam`. The rendered origin is correct for every such row (the parser trims SP/TAB/CR/LF; other edge members
  never reach the host), and pinned. Recorded for the owner.
- **R-3 — stored rows with a member stay readable and visible in clear** through `CameraDto` until #3016, and the
  reconciler keeps re-provisioning them in MediaMTX. Not made worse.
- **R-4 — a pre-deploy event with such an address dead-letters** in StreamDistribution (spec FR-004; specs
  389/392/394 apply unchanged).
- **R-5 — merge order with spec 406** on `RtspPath.cs` (§3.4). Mitigation: T050 checks the tip; the guard covers
  every `Uri` read whichever lands first.
- **R-6 — the predicate disagrees with `char`'s tables on a future runtime.** Pinned by the 84 x template theory and
  T060's exact-set row (the 84 code units enumerated, nothing else).
- **R-7 — wording drift between the two value objects.** R1/R2 pin the exact literal in both; R3 asserts the two
  `From`s agree.
