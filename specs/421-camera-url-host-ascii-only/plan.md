# Plan 421: A camera URL's plain DNS host is written in ASCII

**Spec**: [spec.md](./spec.md) · **Tasks**: [tasks.md](./tasks.md) · **Issue**: #3023 (item 3, host half; owner Q1 of
2026-10-19).

## 1. Constitution and ADR check

| Gate | Result |
|---|---|
| §II primitives on domain models | Pass. The value objects keep their single `Value`. The new `UrlAuthority` member is stateless and returns `bool`. |
| §III no cross-context references | Pass. CameraCatalog and StreamDistribution already reference `Shared.Kernel`. No project reference is added. |
| §IV latency | N/A (spec header). |
| ADR-0105 guards | The new public member starts with `Ensure.That(url).IsNotNull()`. The message carries no value. |
| ADR-0047/0089 errors | No new code. The refusal surfaces as 400 `CAMERA_INVALID_REQUEST` (through `BoundaryParse`) and as `STREAM_INVALID_RTSP_SOURCE`. |
| ADR-0042/0088 Wolverine | No policy change. Replay dead-letters as in spec 389 (spec FR-004). |
| ADR-0139/0144 colours | **Red**, plus characterisation for every unchanged row. The five existing-test edits of §8 are **authorised by the owner (2026-10-20)**. No other existing assertion may change. No ADR, no constitution change, no gate weakened: no threshold, floor or suppression moves. |
| ADR-0036 | No drive-by changes. Only the two comments FR-007 names are reworded. |
| ADR-0065 coverage | The new member is covered by the red rows plus direct post-green rows (T060). |
| ADR-0084 metrics | Advisory. The predicate is about six lines, with complexity well under 10. |
| New ADR needed? | **No** (owner, 2026-10-20). |

## 2. Layers touched

| Context / project | Layer | Files |
|---|---|---|
| Shared.Kernel | n/a | `src/Shared.Kernel/UrlAuthority.cs` (one public member, two comments reworded) |
| CameraCatalog | Domain | `src/CameraCatalog/Domain/Camera/RtspUrl.cs` |
| StreamDistribution | Domain | `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` |

Not touched: `UrlRedaction.cs`, `AuditPayloadRedaction.cs`, `FromStored` / `EnsureStoredInvariants`, the EF
converters, Api, Contracts, Infrastructure, migrations, AppHost, frontend, Helm and the simulator.

## 3. Shapes

### 3.1 `Shared.Kernel.UrlAuthority`

```csharp
// The parser drops some invisible characters from Host (U+200E, U+202E) and folds others, so the written text is read:
// a plain host shown as written must be the host every reader dials. Bracketed hosts are refused by the zone rule.
public static bool NonAsciiCharacterInsideThePlainHost(string url)
{
    Ensure.That(url).IsNotNull();

    var authority = TextFromTheAuthority(url.AsSpan().TrimStart());
    var end = IndexOfTheTerminator(authority);
    var written = end < 0 ? authority : authority[..end];
    var host = written[(written.LastIndexOf('@') + 1)..];

    return !host.IsEmpty && host[0] != '[' && !Ascii.IsValid(host);
}
```

The name is fixed here because the structural guard (G1) is written before the code and scans for it. The body is
the one measured in spec §1.4. It needs `using System.Text;`. The comment wording is the engineer's, kept to one or two
lines.

FR-007 rewords two comments:
- `IsPunctuationOnlyHost`: drop the "would refuse internationalised hosts" clause, or say that spec 421's ASCII rule
  covers that set.
- `ContainsControlOrWhiteSpace`: say that a format character is left to the ASCII rule (in a host) or accepted (in a
  path).

### 3.2 Value objects (identical shape in both)

```csharp
    .Satisfies(url => !UrlAuthority.WrittenPortIsEmpty(url), EmptyPortMessage)
    .Satisfies(url => !UrlAuthority.NonAsciiCharacterInsideThePlainHost(url), NonAsciiHostMessage);   // new, last
```

```csharp
private const string NonAsciiHostMessage =
    "must write the host in ASCII: a non-ASCII or invisible character there is shown as written but read differently, or dropped, by the parser. "
    + "Write an internationalised name in its xn-- form";
```

The rendered text must equal spec FR-001 exactly (`value: <text> (Parameter 'value')`). Each XML `<summary>` gains:
"A plain host holding any non-ASCII character is refused on write (spec 421)."

## 4. Messaging

No domain event or integration event changes. `CameraRegisteredV1` and `CameraAddressChangedV1` keep carrying the
address as a string. StreamDistribution re-validates it through `StreamSourceUrl.From`, which now refuses a non-ASCII
host. That gives FR-004's dead-letter, and the newly failing (no longer ignored) older change of §8 E4.

## 5. Boundary rules

- `Shared.Kernel` declares the predicate. Both domains call it (FR-005, G1).
- No `CameraCatalog` ↔ `StreamDistribution` reference. NetArchTest, `PrimitiveBoundaryTests` and
  `UrlAuthoritySingleSourceTests` (existing rows) pass unmodified.

## 6. Testing strategy

### 6.1 Characterisation (captured green before any production edit, unmodified after)

| ID | Test | Guards |
|---|---|---|
| C1 | existing suites of §6.4 | quote the pass counts |
| C2 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlNonAsciiHostCharacterisationTests.cs` | spec §6 accept set accepted and verbatim; spec §1.2 older-message set refused with today's exact message |
| C3 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlNonAsciiHostCharacterisationTests.cs` | same for `StreamSourceUrl` |
| C4 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredNonAsciiHostTests.cs`, `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredNonAsciiHostTests.cs` | `FromStored` reads back every FR-001 row verbatim |
| C5 | **new** `tests/Architecture.Tests/Persistence/CameraUrlNonAsciiHostHydrationCharacterisationTests.cs` | both EF converters' `ConvertFromProvider` return the stored value for the C4 rows (pattern `CameraUrlEmptyPortHydrationCharacterisationTests`) |
| C6 | **new** `tests/Shared.Kernel.Tests/UrlRedactionNonAsciiHostCharacterisationTests.cs` | `ToOrigin` is `UrlRedaction.Unparseable` for every FR-001 row and unchanged for the accept set |

C2-C6 cannot be red. Inputs are built with C# `\uXXXX` escapes (the surrogate pair as `😀`), and each row's
display name names the code point. No Aspire integration test is added (hydration goes through the converter, as in
specs 394, 408, 411 and 414; phase 5 asks the running system once), so no `ci-shards/shard-N.filter` entry is needed.
**Check the shard filters anyway** if any new class lands in a project that has them (memory: a missing entry fails
deterministically).

### 6.2 Red (observed failing before production code)

| ID | Test | Red today because |
|---|---|---|
| R1 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlNonAsciiHostTests.cs`: every spec §6 FR-001 row throws `ArgumentException` with message exactly `value: <FR-001> (Parameter 'value')` and `ParamName` `value`; the message contains no part of the host (`münchen`, `SECRET`) | accepted ("did not throw") |
| R2 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlNonAsciiHostTests.cs`: same | accepted |
| R3 | **new** `tests/Architecture.Tests/Persistence/CameraUrlNonAsciiHostRefusalTests.cs`: FR-005 agreement over the union of spec §6 sets (same outcome and same message from both `From`s on every row), and every FR-001 row refused | the FR-001 rows are accepted |
| R4 | **new** `tests/Architecture.Tests/CameraUrlNonAsciiHostEndpointTests.cs` (existing `CameraCatalogEndpointHost`, pattern `CameraUrlZoneCharsetEndpointTests`). `POST /cameras` and `PATCH /cameras/{id}` with `rtsp://kamera-münchen.local/live`, `rtsp://cam‎SECRET/p`, `rtsp://SECRETㅤ/p` and `rtsp://SECRET⠀:554/p` answer 400 `CAMERA_INVALID_REQUEST`, with the rendered detail, no secret in the body and no handler reached. A PATCH with a stale If-Match still answers 400. A name-only PATCH of a camera seeded through `RtspUrl.FromStored("rtsp://kamera-münchen.local/live")` answers 204 | 201 / 204 |
| R5 | **new** `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyNonAsciiHostReplayTests.cs` (harness of `LegacyEmptyPortReplayTests`). `CameraRegisteredV1` with `rtsp://kamera-münchen.local/live` throws `InvalidOperationException` naming `STREAM_INVALID_RTSP_SOURCE`, saves nothing, makes no gateway call and logs one Warning free of `münchen`. `CameraAddressChangedV1` to `rtsp://cam⠀/p` onto a seeded stream throws and leaves the address and the save count unchanged | provisions / re-points |
| R6 | the five **authorised edits** of §8 | E4 is red. E1, E2, E3 and E5 are green on arrival and stay green (see §8) |
| G1 | `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs`, **additive**: `RtspUrl.cs` and `StreamSourceUrl.cs` each match `\bUrlAuthority\s*\.\s*NonAsciiCharacterInsideThePlainHost\s*\(`; only `src/Shared.Kernel/UrlAuthority.cs` declares `\bbool\s+NonAsciiCharacterInsideThePlainHost\s*\(` | not declared or called |

**Expected red:**
- R1 and R2: "did not throw".
- R3: the refusal assertion on the FR-001 rows. The outcome-agreement half passes on arrival.
- R4: 201 / 204. The name-only PATCH row passes on arrival.
- R5: "did not throw" and a gateway call recorded.
- E4: "did not throw".
- G1: "not declared or called".

Rows that pass on arrival ride along and are named as such in the PR. No red test calls the new predicate directly,
because a compile error is not a red. G1 reaches it by source scan.

### 6.3 Post-green

- **T060**: a new `tests/Shared.Kernel.Tests/UrlAuthorityNonAsciiHostTests.cs` with direct rows:
  - null is refused;
  - every spec §6 row through the predicate;
  - bracketed `rtsp://[fe80::1%25ü]/s` answers false (left to the zone rule);
  - `rtsp://cam/x?q=ü` answers false (path);
  - `rtsp://@cam‎/p` answers true;
  - `" rtsp://cam‎/p"` answers true (`TrimStart`);
  - `rtsp://` answers false (empty).
- **T070** (`test-adversary`): a backslash terminator (`rtsp://cam‎\x`), several `@`, `rtsp:cam‎/x` and
  `rtsp:\\cam‎/x` scheme spellings, a lone high surrogate, U+FFFD, and the spec 394 fuzz corpora under the rule
  (no `Inspect` defect).

### 6.4 Run commands

```sh
dotnet test tests/CameraCatalog.Domain.Tests --filter "FullyQualifiedName~RtspUrl"                      # C2 C4 R1 E1
dotnet test tests/StreamDistribution.Domain.Tests --filter "FullyQualifiedName~StreamSourceUrl"         # C3 C4 R2 E2
dotnet test tests/CameraCatalog.Application.Tests                                                       # E3
dotnet test tests/StreamDistribution.Application.Tests                                                  # R5 E4
dotnet test tests/StreamDistribution.Infrastructure.Tests                                               # E5
dotnet test tests/Shared.Kernel.Tests --filter "FullyQualifiedName~Url"                                 # C6 T060
dotnet test tests/AuditObservability.Application.Tests --filter "FullyQualifiedName~AuditPayload"      # C1 (unchanged)
dotnet test tests/Architecture.Tests                                                                    # C5 R3 R4 G1, fuzz floors, PrimitiveBoundary
```

Stop any running AppHost first.

## 7. Commit plan

Each commit builds on its own (ADR-0087). Commit 2 leaves its suites red until commit 3 lands (CLAUDE.md "Builds is
not passes", accepted).

1. `test(3023): characterise ASCII hosts, older refusals and stored rows before the host rule` (C2-C6; C1 counts quoted).
2. `test(3023): a plain host holding a non-ASCII character is refused (red)` (R1-R5, G1, and the authorised edits
   E1-E5). E4 is red here. E1, E2, E3 and E5 compile and pass, because they only stop asserting acceptance or switch a
   fixture to `FromStored`.
3. `fix(3023): refuse a camera URL whose plain host is not ASCII` (§3 and FR-007; everything green, including E1-E5
   and C2-C6, unmodified).
4. `test(3023): direct rows for the non-ASCII host predicate` (T060).
5. Adversary findings (T070), as separate `test` / `fix` commits.

## 8. Existing tests whose assertions change (authorised by the owner, 2026-10-20)

These are the only existing-test edits allowed. They are exactly the 12 assertions the counterfactual of spec §1.4
turned red. **No other existing assertion may change.** If T001 finds a thirteenth, that is a block, not an edit.

| # | File | Before | After | Colour |
|---|---|---|---|---|
| E1 | `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlZoneCharsetCharacterisationTests.cs` | `AcceptedPendingTheOwnersQuestions` holds 6 rows, all asserted accepted by `From` (and `FromStored`); `The_sets_hold_exactly_the_rows_the_spec_lists` asserts `.ShouldBe(6)` | the 4 host rows (`rtsp://kamera-münchen.local/live`, `rtsp://cam`+U+200E+`/p`, `rtsp://cam`+U+3164+`/p`, `rtsp://cam`+U+2800+`/p`) are removed; the 2 path rows stay (Q2); the count is `.ShouldBe(2)`. The removed rows are asserted refused in R1 and read back in C4 | green on arrival (removal), green after |
| E2 | `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlZoneCharsetCharacterisationTests.cs` | same as E1 | same as E1 | green / green |
| E3 | `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressHostCharsetLogRedactionTests.cs` (2 tests) | `CommandFor` builds the address with `RtspUrl.From(url)` for `rtsp://cam／token＝SECRET` | `RtspUrl.FromStored(url)`: defence in depth: `From` no longer admits such a host; the handler still logs the placeholder if one is constructed. Both assertions (placeholder logged, full URL stored) are unchanged. The class summary gains one sentence saying so | green / green |
| E4 | `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedAdversarialOrderingTests.cs` | `[InlineData("rtsp://SECRET-MARKER.cаmera-sim:8554/x")]` (Cyrillic а) on `An_ignored_hostile_address_leaves_no_trace_…`: an older change is ignored | the `InlineData` moves to `An_older_change_whose_address_this_context_rejects_fails_rather_than_being_ignored`: it throws, the command logger is silent, a Warning is logged, no message holds the marker, and the address stays `AddressB`. Neither theory's body changes | **red** (did not throw) / green |
| E5 | `tests/StreamDistribution.Infrastructure.Tests/Fakes/SourceRepairFakes.cs:110` (reached by `MediaMtxSourceRepairOverRealGatewayTests` row `rtsp://bücher.example/kamera`) | `Commit` builds the committed source with `StreamSourceUrl.From(source)` | `StreamSourceUrl.FromStored(source)`: `Commit` models a persisted row, so it is read the way persistence reads it. Every other `Commit` caller passes an address both read alike. The `InlineData` row and every assertion are unchanged | green / green |

Count check: E1 has 4 failing cases and E2 has 4 (the `From` theory). E3 has 2, E4 has 1 and E5 has 1, for **12**.
The `FromStored` theory rows of E1 and E2 were not failing; they are removed along with their rows, and C4 takes them
over.
