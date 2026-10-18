# Plan 414: A camera URL with a ':' and no port is refused, and the three guarded `Uri` reads fold into one helper

**Spec**: [spec.md](./spec.md) · **Tasks**: [tasks.md](./tasks.md) · **Issue**: #3031 (items 3 and 7; item 6 is
owner question Q1, spec §9).

## 1. Constitution and ADR check

| Gate | Result |
|---|---|
| §II primitives on domain models | Pass. The value objects keep their single `Value`. The new `UrlAuthority` member is stateless and returns `bool`; the PR 2 helper is `internal` and stateless. |
| §III no cross-context references | Pass. CameraCatalog and StreamDistribution already reference `Shared.Kernel`. No project reference is added. |
| §IV latency | N/A (spec header). |
| ADR-0105 guards | `WrittenPortIsEmpty` starts with `Ensure.That(url).IsNotNull()`. The message carries no value. PR 2 keeps the existing guards on `TryReadHost` / `TryReadUserInfo` / `ToOrigin`; the internal helper is reached only through them. |
| ADR-0047/0089 errors | No new code: 400 `CAMERA_INVALID_REQUEST` (through `BoundaryParse`) and `STREAM_INVALID_RTSP_SOURCE`. |
| ADR-0042/0088 Wolverine | No policy change. Replay dead-letters as in spec 389 (spec FR-004). |
| ADR-0139/0144 colours | PR 1 is **red** for FR-001 and FR-005, with characterisation for every unchanged row of spec §6. **Existing rows move** (spec A-2, plan §8), retired by the issue's own decision as spec 408 A-2 did; the fuzz floor is held by a sixth corpus row with every threshold unchanged (spec A-3). PR 2 is **characterisation** only. The two never share a PR. No ADR, no constitution change, no gate lowered. |
| ADR-0036 | No drive-by changes. #3031 items 4, 5, 6, 8 are not touched; PR 2 does not move `ToOrigin`'s later `uri.Host` reads (item 4). |
| ADR-0065 coverage | The predicate is covered by red rows plus direct post-green rows (T060). PR 2's helper is covered by the existing suites and C-F1. |
| ADR-0084 metrics | Advisory. `UrlAuthority.cs` is already 334 lines (over the advisory 300). PR 1 adds about 25, PR 2 removes about 10. Keep `WrittenPortIsEmpty` under complexity 10 by reusing `BracketedHostReadings` and one small private span helper. |
| New ADR needed? | **No** for either PR. Q1 is wording, not architecture. |

## 2. Layers touched

| PR | Context / project | Layer | Files |
|---|---|---|---|
| 1 | Shared.Kernel | n/a | `src/Shared.Kernel/UrlAuthority.cs` (one public member, at most one private helper) |
| 1 | CameraCatalog | Domain | `src/CameraCatalog/Domain/Camera/RtspUrl.cs` |
| 1 | StreamDistribution | Domain | `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` |
| 2 | Shared.Kernel | n/a | `src/Shared.Kernel/UrlAuthority.cs` (one `internal` helper; two members call it), `src/Shared.Kernel/UrlRedaction.cs` (private `TryParse` deleted; `ToOrigin` calls the helper) |

Not touched by either PR: `FromStored` / `EnsureStoredInvariants`, the EF converters, Api, Contracts, Infrastructure,
migrations, AppHost, frontend, Helm, the simulator, `AuditPayloadRedaction.cs`.

**`UrlAuthority.cs` is touched by both PRs, so they run in sequence and never in parallel.** Order: PR 1, then PR 2,
cut from `develop` after PR 1 merges (not stacked). PR 2 does not depend on PR 1's content; **if the owner holds PR 1
on Q2, PR 2 may go first** from `develop` on its own. What matters is that the two are never open at once against the
same file.

## 3. Shapes (PR 1)

### 3.1 `Shared.Kernel.UrlAuthority`: one new public member

```csharp
// Uri reads an empty port and no port alike (Port -1, the ':' dropped from Authority), while ffmpeg reads port 0,
// so only the written text shows that the ':' names no port.
public static bool WrittenPortIsEmpty(string url);
```

The name is fixed here because the structural guard (§5.3 G1) is written before the code and scans for it. An engineer
who wants another name changes this plan first, not the test.

- Bracketed host: for each `BracketedHostReadings(url)` reading with a `]`, take the text after the first `]`, cut at
  `IndexOfTheTerminator`; true when it is exactly `":"`. (Same reading set as `TextFollowsTheClosingBracket`, which
  already accepts `:` plus zero digits. That predicate is **not** changed: it stays false for `rtsp://[fe80::1]:/s`,
  pinned in `UrlAuthorityDirectTests.cs:83`.)
- Unbracketed host: `TextFromTheAuthority(url.AsSpan().TrimStart())`, cut at `IndexOfTheTerminator`, then the text after
  its last `@`; true when that text is non-empty, does not start with `[`, and ends with `:`.
- Spans only, no `Uri`, so it cannot throw. The behaviour is fixed by spec FR-001 and the §6 rows; the syntax is the
  engineer's. The architect's probe implementation of exactly this agrees with spec §6 on every row and finds 57
  empty-port inputs among the fuzz corpus's 411 accepted.

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
    .Satisfies(url => !UrlAuthority.NonPrintableCharacterInsideTheZone(url), NonPrintableZoneMessage)
    .Satisfies(HostIsNotPunctuationOnly, PunctuationOnlyHostMessage)
    .Satisfies(url => !UrlAuthority.WrittenPortIsEmpty(url), EmptyPortMessage);                          // new
```

`EmptyPortMessage` is a `private const string` per value object holding the exact spec FR-001 text. The doc comment
gains one sentence ("… and a ':' after the host with no port (spec 414)").

**Invariants after PR 1**

| Factory | stored invariants | rules of specs 389-411 | **written port not empty** |
|---|---|---|---|
| `From` (all writes) | yes | yes | **yes** |
| `FromStored` (EF read only) | yes | no | no |

## 4. Shapes (PR 2): the fold

```csharp
// Shared.Kernel.UrlAuthority
// Trust-boundary guard around a BCL parser fed untrusted text: Uri throws IndexOutOfRangeException from TryCreate
// (a file: authority holding U+200E), from Host ([1:2:3:4:5:6:7:8:/64]) and from UserInfo for some inputs, and an
// exception here would fail a write with a 500 or retry-loop an audit message.
internal static bool TryReadParsed<T>(string url, Func<Uri, T> read, [NotNullWhen(true)] out T? value)
    where T : class;
```

- Body: `try { value = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? read(uri) : null; } catch
  (IndexOutOfRangeException) { value = null; } return value is not null;`, which is today's `TryReadHost` body with
  `uri.Host` replaced by `read(uri)`.
- `TryReadHost` becomes `Ensure.That(url).IsNotNull(); return TryReadParsed(url, uri => uri.Host, out host);`, and
  `TryReadUserInfo` likewise with `uri.UserInfo`. Public signatures and doc comments stay.
- `UrlRedaction.ToOrigin` calls `UrlAuthority.TryReadParsed(url, uri => uri, out var uri)` where it called
  `TryParse(url, out var uri)`, and the private `TryParse` with its comment is deleted (the comment's reason moves to
  the helper, above). `TryParse` returned `Uri.TryCreate`'s bool; the helper returns `uri is not null`, which is the
  same bool, because `TryCreate` yields a non-null `Uri` exactly when it returns true.
- **Not moved:** `ToOrigin`'s `uri.Host`, `HostIsPlainAscii(uri)` and `uri.Port` reads after the parse. Wrapping them
  would be a behaviour change on inputs where they throw (#3031 item 4, not enrolled).
- The name and visibility are the engineer's within these constraints: one helper, `internal` (it has callers in two
  classes), in `UrlAuthority`, and after the fold `src/Shared.Kernel/**/*.cs` holds exactly one
  `catch (IndexOutOfRangeException)` (G2). `CameraUrlGuardedUriReadTests` must pass unmodified (the value objects still
  call `TryReadHost` / `TryReadUserInfo`).

## 5. Messaging

No domain event, integration event or contract changes in either PR. Replay (PR 1): spec 389 §4 applies unchanged. A
pre-deploy `CameraRegisteredV1` / `CameraAddressChangedV1` carrying an empty-port address goes to the error queue on
its first StreamDistribution attempt, with one Warning that names `STREAM_INVALID_RTSP_SOURCE` and no part of the
address (`ProvisionStreamCommandHandler.cs`, `RepointStreamCommandHandler.cs`, the two `StreamSourceUrl.From` callers).
The audit consumer does not call `From` and is unaffected (A-1). The simulator does not call `From`.

## 6. Testing strategy

### 6.1 PR 1, characterisation (captured green on the branch tip before any production edit, unmodified after)

| ID | Test | Guards |
|---|---|---|
| C1 | existing suites of §6.4 | quote the pass counts |
| C2 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlEmptyPortCharacterisationTests.cs` | spec §6 accept set accepted by `From` and round-tripping through `FromStored`; spec §6 older-message set refused with today's exact message |
| C3 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlEmptyPortCharacterisationTests.cs` | same for `StreamSourceUrl` |
| C4 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredEmptyPortTests.cs`, `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredEmptyPortTests.cs` | `FromStored` reads back every FR-001 row |
| C5 | **new** `tests/Architecture.Tests/Persistence/CameraUrlEmptyPortHydrationCharacterisationTests.cs` | both EF converters' `ConvertFromProvider` return the stored value for the C4 rows (pattern `CameraUrlZoneCharsetHydrationCharacterisationTests`) |
| C6 | **new** `tests/Shared.Kernel.Tests/UrlRedactionEmptyPortCharacterisationTests.cs` | `ToOrigin` for every FR-001 row exactly as spec §6 "`ToOrigin` (characterisation)" (A-1) |
| C7 | **edit, additive** `tests/Architecture.Tests/CameraUrlZoneDelimiterFuzzAdversarialTests.cs`: one `Corpora` row `{ "tokens", 3031 }` and the "Measured on the five seeds" comment updated to the six-seed figures quoted at T001. **No threshold, assertion or generator line changes** (spec A-3) | the floor stays reachable after FR-001; green today (411 + 118 = 529 accepted, 0 `Inspect` defects on the new row) |

C2-C6 cannot be red. C7 is green on arrival by design and is stated as such in the PR. No Aspire integration test is
added (hydration goes through the converter, as in specs 394, 408 and 411; phase 5 asks the running system once), so no
`ci-shards/shard-N.filter` entry is needed.

### 6.2 PR 1, red (observed failing before production code)

| ID | Test | Red today because |
|---|---|---|
| R1 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlEmptyPortTests.cs`: every spec §6 FR-001 row throws `ArgumentException`, message exactly `value: <FR-001> (Parameter 'value')`, `ParamName` `value` | accepted |
| R2 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlEmptyPortTests.cs`: same | accepted |
| R3 | **new** `tests/Architecture.Tests/Persistence/CameraUrlEmptyPortRefusalTests.cs`: FR-005 agreement over the union of spec §6 sets, where the two `From`s give the same outcome and message on every row and every FR-001 row is refused | the new rows are accepted |
| R4 | **new** `tests/Architecture.Tests/CameraUrlEmptyPortEndpointTests.cs` (existing `CameraCatalogEndpointHost`, pattern `CameraUrlZoneCharsetEndpointTests`): `POST /cameras` and `PATCH /cameras/{id}` with `rtsp://10.0.0.66:/live` and `rtsp://[::1]:/live` answer 400 `CAMERA_INVALID_REQUEST` with the rendered detail, handler not invoked; one PATCH with a stale If-Match still answers 400; a name-only PATCH of a camera seeded with `rtsp://10.0.0.66:/live` answers 204 | 201 / 204 |
| R5 | **new** `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyEmptyPortReplayTests.cs` (harness of `LegacyZoneCharsetReplayTests`): `CameraRegisteredV1` with `rtsp://10.0.0.66:/live` throws `InvalidOperationException` naming `STREAM_INVALID_RTSP_SOURCE`, saves nothing, makes no gateway call and logs one Warning free of `10.0.0.66`; `CameraAddressChangedV1` to `rtsp://[::1]:/live` onto a seeded stream throws and leaves the address unchanged | provisions / re-points |
| R6 | **edits** retiring the rows of plan §8 (spec A-2): each row is removed from its "accepted" set and the persistence corpus row is reclassified, so the edited suites fail until FR-001 lands | the rows are accepted today |

Expected red: R1-R2 "did not throw"; R3 the agreement fails on the new rows; R4 201/204; R5 "did not throw" and a
gateway call recorded; R6 the reclassified persistence row and its counts. The name-only PATCH row of R4 and the
outcome-agreement half of R3 pass on arrival; they ride along and are named as such in the PR. The rows R6 removes from
"accepted" lists are not lost: each appears in R1 / R2 (refused) and in C4 (hydrates).

### 6.3 PR 1, structural guard (red before the code)

| ID | Test | Red today because |
|---|---|---|
| G1 | `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs`, **additive**: `RtspUrl.cs` and `StreamSourceUrl.cs` each match `\bUrlAuthority\s*\.\s*WrittenPortIsEmpty\s*\(`; only `src/Shared.Kernel/UrlAuthority.cs` declares `\bbool\s+WrittenPortIsEmpty\s*\(` (slashes normalised) | not declared or called |

No red test calls `WrittenPortIsEmpty` directly (a compile error is not a red). T060 adds direct rows after green.

### 6.4 PR 1 run commands

```sh
dotnet test tests/CameraCatalog.Domain.Tests --filter "FullyQualifiedName~RtspUrl"                     # C1 C2 C4 R1 R6
dotnet test tests/StreamDistribution.Domain.Tests --filter "FullyQualifiedName~StreamSourceUrl"        # C1 C3 C4 R2 R6
dotnet test tests/Shared.Kernel.Tests --filter "FullyQualifiedName~Url"                                # C1 C6 T060
dotnet test tests/AuditObservability.Application.Tests --filter "FullyQualifiedName~AuditPayload"     # C1 (A-1 rows unchanged)
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~CameraUrl|FullyQualifiedName~UrlAuthoritySingleSource|FullyQualifiedName~FromStored|FullyQualifiedName~CameraRegisterEndpoint|FullyQualifiedName~CameraPatchEndpoint|FullyQualifiedName~PrimitiveBoundary"   # C1 C5 C7 R3 R4 R6 G1, incl. the fuzz floor
dotnet test tests/StreamDistribution.Application.Tests --filter "FullyQualifiedName~Legacy|FullyQualifiedName~CameraAddressChanged|FullyQualifiedName~CameraRegistered"   # C1 R5
```

Light builds, these projects only. Stop any running AppHost first.

### 6.5 PR 2, characterisation (observed green before the fold, unmodified after)

| ID | Test | Guards |
|---|---|---|
| C-F0 | every suite that reaches the three members: `Shared.Kernel.Tests` (full), `Architecture.Tests` `~CameraUrl\|~UrlAuthoritySingleSource\|~FromStored`, both domain suites `~RtspUrl` / `~StreamSourceUrl`, `AuditObservability.Application.Tests` `~AuditPayload`, `StreamDistribution.Application.Tests` `~Legacy` | quote counts before and after; identical |
| C-F1 | **new** `tests/Shared.Kernel.Tests/UrlAuthorityGuardedReadCharacterisationTests.cs`. Each row states its precondition on `Uri` first, so the row cannot pass vacuously if a runtime update stops throwing: (a) `"FILE://" + U+200E + "/x"` and `"file:/\\" + U+200E`: `Uri.TryCreate` throws; `TryReadHost` and `TryReadUserInfo` answer false with a null `out`; `ToOrigin` answers `UrlRedaction.Unparseable` and does not throw. (b) `rtsp://[1:2:3:4:5:6:7:8:/64]`: `new Uri(url).Host` throws; `TryReadHost` false / null; `TryReadUserInfo` true / `""`. (c) `rtsp://[]@[1:2:3:4:5:6:7:8:/64%25eth0]/s`: `UserInfo` throws; `TryReadUserInfo` false / null; `TryReadHost` false / null. (d) `not a url`: `TryCreate` false; all three answer false / placeholder. (e) `rtsp://cam/s` -> host `cam`; `rtsp://u%40x@cam/s` -> userinfo `u%40x`; `ToOrigin("rtsp://cam:554/x")` -> `rtsp://cam:554` | the fold changes no answer |

**Counterfactuals (required in the PR body, memory "prove a guard by counterfactual").** After the fold, delete the
helper's `catch` (let the exception escape): C-F1 rows (a), (b) and (c) must fail, and the existing
`UrlRedactionAuthorityLocationTests.A_parser_that_throws_on_the_text_yields_the_placeholder` and
`UrlAuthorityZoneDelimiterAdversarialTests` rows must fail too; quote the failing counts, then restore. Before the
fold, do the same per arm on today's code (remove `UrlRedaction.TryParse`'s catch only; then `TryReadHost`'s only; then
`TryReadUserInfo`'s only) and quote that each removal turns at least one C-F0/C-F1 test red. An arm whose removal
turns nothing red means the characterisation does not cover it: add a row before the fold, do not proceed.

### 6.6 PR 2, post-fold structure guard (green on arrival, stated as such)

| ID | Test | Guards |
|---|---|---|
| G2 | `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs`, additive: over `src/Shared.Kernel/**/*.cs` executable lines (`RepositorySource.ExecutableLines`, slashes normalised), `catch (IndexOutOfRangeException)` occurs exactly once, in `UrlAuthority.cs`, and `UrlRedaction.cs` contains no `Uri.TryCreate(` | the duplication stays gone. Counterfactual (reinstate `UrlRedaction.TryParse`, so G2 goes red) quoted. A structure guard, not behaviour, so arriving green is not a phase-4a failure |

## 7. Commit plan

**PR 1** (`fix/3031-camera-url-residuals`). Each commit builds on its own (ADR-0087). The red commit leaves its suites
failing until the fix commit lands (CLAUDE.md "Builds is not passes", accepted).

1. `test(3031): characterise empty-port, port and audit rows before the empty-port rule` (C2-C7; counts quoted).
2. `test(3031): an address with a ':' and no port is refused (red)` (R1-R6, G1).
3. `fix(3031): refuse a camera URL whose ':' after the host names no port` (FR-001; everything green).
4. `test(3031): direct rows for the empty-port predicate` (T060, post-green).
5. Adversary findings (T070), as separate `test` / `fix` commits.

**PR 2** (`refactor/3031-guarded-uri-read-fold`, from `develop` after PR 1 merges, or first if PR 1 is held):

1. `test(3031): characterise the guarded Uri reads before the fold` (C-F1; C-F0 counts and the per-arm counterfactuals quoted).
2. `refactor(3031): one helper guards every Uri read in Shared.Kernel` (§4; C-F0 and C-F1 unmodified and green; post-fold counterfactual quoted).
3. `test(3031): guard that Shared.Kernel catches the parser's exception once` (G2).

## 8. Existing tests whose assertions change (PR 1 only; spec A-2)

| File | Lines | Change |
|---|---|---|
| `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAuthorityHostAdversarialTests.cs` | 141-144, 151 | remove `rtsp://[fe80::1]:`, `…]:/`, `…]:?q`, `…]:#f`, `rtsp://cam:/x` from `AddressesThatWorkedBeforeAndStillDo` |
| `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAuthorityHostAdversarialTests.cs` | 141-144, 151 | same |
| `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAuthorityHostCharacterisationTests.cs` | 33 | remove `rtsp://[fe80::1]:/s` from `Accepted` |
| `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAuthorityHostCharacterisationTests.cs` | 33 | same |
| `tests/Architecture.Tests/Persistence/CameraUrlAuthorityHostRefusalTests.cs` | 61, the `OlderRuleRows` set and its comment, the counts in `The_corpus_exercises_every_outcome_class_row_by_row` | add `rtsp://[fe80::1]:/s` to `OlderRuleRows` (comment: "rules other than spec 392's two, including spec 414's empty port"); counts `older-rules` 8 -> 9, `accepted` 15 -> 14; `rows.Count` stays 42 |
| `tests/Architecture.Tests/CameraUrlZoneDelimiterFuzzAdversarialTests.cs` | `Corpora`, the "Measured" comment | C7 (additive, commit 1) |

**Not changed** (they test `ToOrigin`, the audit, or a predicate that stays as it is): `UrlAuthorityDirectTests.cs:83`,
`UrlRedactionAuthorityHostAdversarialTests.cs:30, 73`, `UrlRedactionAuthorityHostCharacterisationTests.cs:32`,
`AuditPayloadDetailChangedLocatorCharacterisationTests.cs:43`.

This list comes from a literal search; interpolated and generated rows are not covered by it, so **T001 runs the real
counterfactual** (FR-001 applied in the worktree, every §6.4 command run, `git checkout -- src`) and the PR lists every
failing assertion. A hit not in the table above that pins acceptance of an empty port is added here and in the PR (it
is the same decision, A-2). A hit that is anything else is a block.

## 9. Risks

- **R-1: accepted input narrows** for an empty port (spec A-2). No resolvable address is lost: writing the port or
  dropping the `:` gives the same camera.
- **R-2: `From` and `ToOrigin` disagree on the new rows** (spec A-1): refused, but rendered. Deliberate and pinned (C6).
- **R-3: the fuzz floor.** Without C7 it falls to 354 (< 400). With it, 458. Re-measure at T001; below 400 with C7 is a
  block, never a threshold edit.
- **R-4: replay dead-letters** pre-deploy events with an empty port (spec FR-004).
- **R-5: PR 2 widens a catch by accident.** The helper wraps exactly what each caller wrapped before; `ToOrigin`'s later
  reads stay outside (§4). The per-arm counterfactuals prove each arm is characterised.
- **R-6: wording drift between the value objects.** R1-R2 pin the exact literal in both, and R3 asserts agreement.
