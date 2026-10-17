# Plan 392: A camera URL whose written authority is longer, or shorter, than the host the parser read is refused on write and redacted on read

**Spec**: [spec.md](./spec.md) · **Tasks**: [tasks.md](./tasks.md) · **Issues**: #3012, #3017

## 1. Constitution and ADR check

| Gate | Result |
|---|---|
| §II primitives on domain models | Pass. `UrlAuthority` stays a stateless static class; the value objects keep their single `Value`. New members return `bool`, `int` or `ReadOnlySpan<char>` and hold nothing. `PrimitiveBoundaryTests` unaffected. |
| §III no cross-context references | Pass. CameraCatalog, StreamDistribution and AuditObservability already reference `Shared.Kernel`. No project reference added. The two value objects remain separate copies by design (`StreamSourceUrl`'s doc comment). |
| §IV latency | N/A (spec header). |
| ADR-0105 guards | Every new public `UrlAuthority` member that takes a `string` guards it with `Ensure.That(url).IsNotNull()`; span overloads need none. Value-object messages carry no value. |
| ADR-0047/0089 errors | No new code. 400 `CAMERA_INVALID_REQUEST` (via `BoundaryParse`); `STREAM_INVALID_RTSP_SOURCE`. |
| ADR-0042/0088 Wolverine | No policy change; spec 389's characterisation (`CameraEventFailurePolicyCharacterisationTests`) stays the pin. |
| ADR-0139/0144 | Characterisation for US3 and for everything §5 lists as unchanged; red for US1/US2. No ADR, no constitution change, no gate weakened. The single edited assertion (§7) **tightens** a guard; its own summary prescribes that edit. |
| ADR-0065 coverage | New `UrlAuthority` members covered by direct post-green rows (T040) plus every `UrlRedaction*` and `AuditPayload*` suite. |
| ADR-0084 metrics | Advisory. `TextFollowsTheClosingBracket` should stay <= 30 LOC and complexity <= 10; split a private helper (`IsPortThenEnd`) if needed. |
| New ADR needed? | **No** (spec header). |

## 2. Layers touched

| Context / project | Layer | Files |
|---|---|---|
| Shared.Kernel | — | `src/Shared.Kernel/UrlAuthority.cs` (public locator + terminator search; new predicate; comment fix), `src/Shared.Kernel/UrlRedaction.cs` (one `||` in `ToOrigin`) |
| AuditObservability | Application | `src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs` (`BracketContentIsAddressOnly` calls the shared locator and terminator search; its comment updated) |
| CameraCatalog | Domain | `src/CameraCatalog/Domain/Camera/RtspUrl.cs` |
| StreamDistribution | Domain | `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` |

No Api, Contracts, Infrastructure, migration, AppHost, frontend or Helm change. The EF converters already call
`FromStored` (spec 389) and are not touched.

## 3. Shapes

### 3.1 `Shared.Kernel.UrlAuthority`

```csharp
public static class UrlAuthority
{
    internal const string AuthorityTerminators = "/?#\\";                    // unchanged, still the only literal

    // The text from the authority's first character to the end of the input: past a scheme (letters, digits,
    // + . -, starting with a letter) and its ':' if present, then past any run of '/' and '\'. Empty when nothing
    // follows. Models file: and UNC parsing; over-reaches for rtsp:/// (see FR-007 comment).
    public static ReadOnlySpan<char> TextFromTheAuthority(ReadOnlySpan<char> url);

    // Index of the first '/', '?', '#' or '\' in the authority text, or -1.
    public static int IndexOfTheTerminator(ReadOnlySpan<char> authority);

    public static bool AtSignFollowsTheAuthority(string url);          // behaviour unchanged, rebuilt on the two above
    public static bool TextFollowsTheClosingBracket(string url);       // new (FR-003, FR-004)
}
```

Names are fixed here because the US3 guard is written before the code and scans for them (§5.1 G-US3); an
engineer who wants different names changes this plan first, not the test.

**`TextFromTheAuthority`** is the body of today's `AtSignFollowsTheAuthority` lines 17-32, moved verbatim
(including `AllSchemeCharacters`), returning `ReadOnlySpan<char>.Empty` where the old code returned `false` at
`authorityStart < 0`. `AtSignFollowsTheAuthority` becomes:

```csharp
var authority = TextFromTheAuthority(url);
var end = IndexOfTheTerminator(authority);
return end >= 0 && authority[end..].Contains('@');
```

(for empty `authority`, `IndexOfTheTerminator` is -1, so the result is `false`, as today).

**`TextFollowsTheClosingBracket(url)`**, on the written text, no parse:

1. `authority = TextFromTheAuthority(url)`; empty -> `false`.
2. `terminator = IndexOfTheTerminator(authority)`; `userinfoEnd` = last `@` in `authority[..terminator]` (whole
   authority when -1). This mirrors `BracketContentIsAddressOnly` lines 176-177, which spec 387 already reasoned
   through (a `/` inside a bracket bounds the userinfo search, not the host).
3. `host = authority[(userinfoEnd + 1)..]`; empty or `host[0] != '['` -> `false` (A-2: only bracketed hosts).
4. `close = host.IndexOf(']')`; `< 0` -> `false` (the parser refuses it; another rule answers).
5. `rest = host[(close + 1)..]`. `false` when `rest` is empty, or `rest[0]` is a terminator, or `rest[0] == ':'`
   and every character of `rest[1..]` up to the first terminator (or the end) is an ASCII digit (zero digits
   allowed, A-1). Otherwise `true`.

It must not throw for any string (it is called by `ToOrigin` on untrusted text): spans only, no `Uri`.

**FR-007 comment.** Replace lines 9-10 ("The authority is located the way the parser does …") with: the skip of a
`/` and `\` run models `file:` and UNC parsing, where the host follows the run; for a hierarchical scheme such as
`rtsp:` a third `/` ends an empty authority, so this over-reaches there, and the shape is refused by the
value objects' empty-host rule and rendered as the placeholder by `ToOrigin`'s empty-host check. Keep lines 11-12
(the "do not unify with `AuthorityWasWrittenAsAscii`" note) as they are.

### 3.2 `UrlRedaction.ToOrigin`

```csharp
if (UrlAuthority.AtSignFollowsTheAuthority(url)
    || UrlAuthority.TextFollowsTheClosingBracket(url)
    || !TryParse(url, out var uri)
    ...
```

Nothing else changes in `UrlRedaction`. (`AuthorityWasWrittenAsAscii` keeps using the internal
`AuthorityTerminators`; switching it to `IndexOfTheTerminator` is optional and behaviour-identical — engineer's
call, in the refactor commit only.)

### 3.3 `AuditPayloadRedaction.BracketContentIsAddressOnly`

```csharp
var authority = UrlAuthority.TextFromTheAuthority(url.AsSpan().Trim());

if (authority.IsEmpty)
{
    return false;
}

var terminator = UrlAuthority.IndexOfTheTerminator(authority);
var userinfoEnd = (terminator < 0 ? authority : authority[..terminator]).LastIndexOf('@');
... (lines 179-193 unchanged)
```

Lines 160-176 (own scheme skip, own locator, own literal) are removed. The `Trim()` stays (spec §1.3 item 1). The
scheme-character difference is unreachable (spec §1.3 item 2). The comment at lines 155-157 is updated to say it
uses `UrlAuthority`'s locator on the trimmed text. Do **not** route this method through
`TextFollowsTheClosingBracket` or share its userinfo/bracket step: it additionally refuses a bracket in the
userinfo (spec 387 B2) and answers a different question; sharing that is a second refactor nobody asked for.

### 3.4 Value objects (identical shape in both)

```csharp
public static RtspUrl From(string value)
{
    EnsureStoredInvariants(value)
        .Satisfies(url => !UrlAuthority.AtSignFollowsTheAuthority(url), AtSignAfterAuthorityMessage)
        .Satisfies(HasHost, EmptyHostMessage)
        .Satisfies(url => !UrlAuthority.TextFollowsTheClosingBracket(url), TextAfterBracketMessage);
    return new RtspUrl(value);
}

private static bool HasHost(string url) =>
    Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host);
```

`FromStored` and `EnsureStoredInvariants` are not touched. Messages are the FR-002 / FR-003 literals, declared
once per value object as `private const string`. The doc comment gains one sentence each for the two rules. The
order is fixed (spec FR-001); the syntax is the engineer's.

**Invariants after the change**

| Factory | Not blank | <= 2048 | `rtsp://` | no userinfo | no raw `@` after authority | host not empty | no text after `]` |
|---|---|---|---|---|---|---|---|
| `From` (all writes) | yes | yes | yes | yes | yes | **yes** | **yes** |
| `FromStored` (EF read only) | yes | yes | yes | yes | no | no | no |

## 4. Messaging

No domain event, integration event or contract changes. `CameraRegisteredV1` / `CameraAddressChangedV1` keep
carrying the full URL. The audit copy changes only through `ToOrigin` (FR-004). Replay: spec 389 §4 applies
unchanged — a pre-deploy event carrying a now-refused address is moved to the error queue on its first attempt
with one Warning naming `STREAM_INVALID_RTSP_SOURCE` and no part of the address.

## 5. Testing strategy

Phase 4a has **both colours**, in separate files and separate commits:

- US3 (FR-008): **characterisation** of behaviour + a **tightened structural guard** that is red first.
- US1/US2 (FR-001..FR-004, FR-009): **red**.
- US2a (FR-005) and every unchanged row of spec §5: **characterisation**.

Ambiguity resolves to red (ADR-0144). Every red test references only types and members that exist at
`de9802b1` (value objects, `ToOrigin`, `AuditPayloadRedaction.Serialize`, the endpoint host, the handlers); the
new `UrlAuthority` members are reached directly only in post-green rows (T040). A compile error is not a red.

### 5.1 Characterisation — captured green on `de9802b1` before any production edit, unmodified after

| ID | Test | Guards |
|---|---|---|
| C1 | existing `tests/Shared.Kernel.Tests` (all `UrlAuthority*` and `UrlRedaction*` suites, incl. the 200,000-input fuzz) | `AtSignFollowsTheAuthority` and `ToOrigin` unchanged by the move; pins the predicate rows FR-007 keeps (`rtsp:///admin:1@cam/s` false, `rtsp://`/`rtsp:///` false) |
| C2 | existing `tests/AuditObservability.Application.Tests` `AuditPayload*` suites (all), notably `AuditPayloadDetailChangedBracketContentTests`, `…UserinfoBracketTests`, `…ZoneTests`, `…ZoneAdversarialTests`, `…AdversarialTests` (`rtsp:///s` -> placeholder) | `BracketContentIsAddressOnly` unchanged by US3; empty host already unknown |
| C3 | **new** `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadDetailChangedLocatorCharacterisationTests.cs` | rows whose answer depends on the locator, both directions: ` rtsp://[fe80::1]/x ` -> `rtsp://[fe80::1]/y` **true** (the trim); `\trtsp://[fe80::1]/x` -> `rtsp://[fe80::1]/y` true; `\\[fe80::1]\x` -> `\\[fe80::1]\y` true; `file:////[fe80::1]/x` -> `file:////[fe80::1]/y` true; `file://[fe80::1]/x` -> `file://[fe80::1]/y` true; `rtsp:\\[fe80::1]/x` -> `rtsp:\\[fe80::1]/y` true; `RTSP://[FE80::1]/x` -> `rtsp://[fe80::1]/y` true; `rtsp:[fe80::1]/x` -> `rtsp:[fe80::1]/y` null; `rtsp://[fe80::1/64]/x` -> `rtsp://[fe80::1/64]/y` null; `rtsp://user@[fe80::1]/x` -> `rtsp://user@[fe80::1]/y` true; `rtsp://[fe80::1]:554/x` -> `rtsp://[fe80::1]:554/y` true; `rtsp://[fe80::1]:/x` -> `rtsp://[fe80::1]/y` true; `rtsp://[fe80::1]?a` -> `rtsp://[fe80::1]#b` true. Values observed by the scratch probe on `de9802b1`; the test-writer re-observes them. Counterfactual (run once, recorded in the class summary): drop the `Trim()` locally -> the first row goes `null`. |
| C4 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAuthorityHostCharacterisationTests.cs` | spec §5 accept set accepted by `From` and `FromStored`; §5 "older message" set refused with today's exact message by both |
| C5 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAuthorityHostCharacterisationTests.cs` | same for `StreamSourceUrl` |
| C6 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredAuthorityHostTests.cs` and `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredAuthorityHostTests.cs` | `FromStored` reads back every FR-002 and FR-003 row (US2a) |
| C7 | **new** `tests/Architecture.Tests/Persistence/CameraUrlAuthorityHostHydrationCharacterisationTests.cs` | both EF converters' `ConvertFromProvider` return the stored value for every FR-002/FR-003 row (pattern: `CameraUrlHydrationCharacterisationTests`, `StreamSourceUrlHydrationCharacterisationTests`) |
| C8 | **new** `tests/Shared.Kernel.Tests/UrlRedactionAuthorityHostCharacterisationTests.cs` | spec §5 `ToOrigin` characterisation rows: every FR-002 row -> placeholder; bracket accept rows keep their origins; `rtsp://user:pass@[fd00::5]:8554/x` -> `rtsp://[fd00::5]:8554`; `rtsp://[]@[fe80::1/64%25eth0]/s` -> `rtsp://[fe80::1]`; `\\[fe80::1]\x`, `file:////[fe80::1]/x` -> `file://[fe80::1]` |
| C9 | existing `RtspUrl*Tests`, `StreamSourceUrl*Tests`, `CameraUrlRefusalAgreementTests`, `CameraUrlRefusalEndpointAdversarialTests`, `CameraUrlAtSignAfterAuthorityEndpointTests`, `LegacyMisreadSource*Tests`, `CameraEventFailurePolicyCharacterisationTests`, `FromStoredCallSiteTests`, `FromStoredCallerIlTests`, `CameraRegisterEndpointCharacterisationTests`, `CameraPatchEndpointCharacterisationTests`, NetArchTest boundary suite, `PrimitiveBoundaryTests`, `HandlerDeconstructionTests` | unchanged contracts; raw-`@` message wins over the new rules for `rtsp:///admin:4242#@cam` |

No Aspire integration test is added: hydration runs through the converter (C7), which spec 389's
`StoredMisreadCameraAddressIntegrationTests` already proved is what the running system uses; phase 5 asks the
running system once (spec §8 step 4). No shard-filter entry is therefore needed.

### 5.2 Red — new behaviour, observed failing before production code

| ID | Test | Red today because |
|---|---|---|
| R1 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAuthorityHostTests.cs`: each FR-002 row throws `ArgumentException`, message exactly `value: <FR-002> (Parameter 'value')`; each FR-003 row likewise with FR-003; `ToString()` free of the row's secret, `ParamName` `value` | `From` accepts them |
| R2 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAuthorityHostTests.cs`: same | same |
| R3 | **new** `tests/Architecture.Tests/Persistence/CameraUrlAuthorityHostRefusalTests.cs`: (a) **#3017's test verbatim**, `An_empty_authority_followed_by_userinfo_looking_text_is_refused` with rows `rtsp:///admin:4242@cam/s`, `rtsp:////admin:4242@cam/s`; (b) the FR-009 agreement over the union of spec §5 sets: `From` refusing on FR-002 or FR-003 implies `ToOrigin` is the placeholder, and an accepted row has a non-empty parsed host and a non-placeholder origin; the two `From`s agree row by row | `From` accepts; `ToOrigin` renders the FR-003 rows |
| R4 | **new** `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadDetailChangedBracketTailTests.cs`: **#3012's test**, `Text_written_after_the_closing_bracket_is_not_claimed_as_the_same_origin` with its three rows, through `IntegrationEventAuditHandler` as `AuditPayloadDetailChangedUserinfoBracketTests` does, asserting **`JsonValueKind.Null`** in both directions (stronger than the issue's "not True"; the decision is null), plus `PreviousUrl`/`Url` equal `<unparseable url>` for each side with a tail; add rows `rtsp://[fe80::1]x:554/s` -> `rtsp://[fe80::1]/s` and `rtsp://[fe80::1]]/x` -> `rtsp://[fe80::1]/x` | today `true` |
| R5 | **new** `tests/Shared.Kernel.Tests/UrlRedactionBracketTailTests.cs`: every FR-003 row plus `rtsp:\\[fe80::1]garbage/x`, `http://[fe80::1]garbage/x`, `rtsp://u@[fe80::1]x/s` -> `UrlRedaction.Unparseable` | today rendered as `…//[…]` |
| R6 | **new** `tests/Architecture.Tests/CameraUrlAuthorityHostEndpointTests.cs` (existing `CameraCatalogEndpointHost`, pattern `CameraUrlAtSignAfterAuthorityEndpointTests`): `POST /cameras` and `PATCH /cameras/{id}` with `rtsp:///admin:4242@cam/s`, `rtsp:///cam`, `rtsp://[fe80::1]%25eth0/x`, `rtsp://[fe80::1]garbage.example/x` -> 400 `CAMERA_INVALID_REQUEST`, detail the rendered FR-002/FR-003, body free of the secret, handler not invoked; one PATCH with a stale If-Match -> still 400 | today 201 / 204 |
| R7 | **new** `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyAuthorityHostReplayTests.cs` (harness of `LegacyMisreadSourceReplayTests`): `CameraRegisteredV1` with `rtsp:///admin:4242@cam/s` -> `InvalidOperationException`, nothing saved, no gateway call, one Warning with `STREAM_INVALID_RTSP_SOURCE` and no `4242`; newer `CameraAddressChangedV1` with `rtsp://[fe80::1]garbage.example/x` onto a seeded stream -> throws, address unchanged, no gateway call | today both apply the address |

Expected red: R1/R2/R3a "did not throw"; R3b the agreement fails on the FR-003 rows (`ToOrigin` renders); R4
`should be Null but was True`; R5 `should be "<unparseable url>" but was "rtsp://[fe80::1]"`; R6 201/204 for 400;
R7 "did not throw" and a gateway call recorded.

### 5.3 Structural guard for US3 — red before the move, by an authorised edit

| ID | Test | Red today because |
|---|---|---|
| G-US3a | **edit** `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs`: `The_terminator_set_is_spelled_out_only_where_it_is_declared_and_in_the_recorded_audit_copy` and `The_authority_locator_is_written_only_in_UrlAuthority_and_the_recorded_audit_copy` expect `[Declaration]` only; the `AuditCopy` constant and the summary's "recorded exception" paragraph are removed; the two methods are renamed `…only_where_it_is_declared` / `…only_in_UrlAuthority`. | `AuditPayloadRedaction.cs:168,176` still hold the copy |
| G-US3b | same file, **new** fact `The_audit_redaction_asks_the_shared_locator`: `AuditPayloadRedaction.cs` executable lines match `\bUrlAuthority\s*\.\s*TextFromTheAuthority\s*\(` | not called today |
| G-US3c | same file, **new** theory `Each_caller_asks_the_shared_bracket_predicate` over `RtspUrl.cs`, `StreamSourceUrl.cs`, `UrlRedaction.cs` matching `\bUrlAuthority\s*\.\s*TextFollowsTheClosingBracket\s*\(`; **new** fact `Only_UrlAuthority_declares_the_bracket_predicate` | not declared or called today (red with the US1/US2 reds, not the US3 one) |

The G-US3a edit is the only change to an existing assertion in this spec. It is authorised by the test's own
summary ("Listed by name so that a third copy fails here, and so that removing that one requires editing this list
rather than passing silently") and by #3017's proposal; it tightens the guard (two files allowed -> one). It is
listed in the PR body. G-US3c is a new test that goes red with R1-R7.

### 5.4 Run commands

```sh
dotnet test tests/Shared.Kernel.Tests                                                                  # C1, C8, R5
dotnet test tests/AuditObservability.Application.Tests --filter "FullyQualifiedName~AuditPayload"     # C2, C3, R4
dotnet test tests/CameraCatalog.Domain.Tests --filter "FullyQualifiedName~RtspUrl"                     # C4, C6, R1, C9
dotnet test tests/StreamDistribution.Domain.Tests --filter "FullyQualifiedName~StreamSourceUrl"        # C5, C6, R2, C9
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~CameraUrl|FullyQualifiedName~UrlAuthoritySingleSource|FullyQualifiedName~FromStored|FullyQualifiedName~CameraRegisterEndpoint|FullyQualifiedName~CameraPatchEndpoint"   # C7, C9, R3, R6, G-US3
dotnet test tests/StreamDistribution.Application.Tests --filter "FullyQualifiedName~LegacyMisreadSource|FullyQualifiedName~LegacyAuthorityHost|FullyQualifiedName~CameraAddressChanged|FullyQualifiedName~CameraRegistered"   # R7, C9
dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~StreamSourceUrlHydration|FullyQualifiedName~CameraEventFailurePolicy"   # C9
```

## 6. Commit plan — one PR, no commit mixes a refactor with a behaviour change

US3 first, so the new predicate is written against the shared locator and the refactor is proven before any
behaviour moves. Each commit builds on its own (ADR-0087); the two red commits leave their suite failing until the
next commit (CLAUDE.md "Builds is not passes", accepted).

1. `test(392): characterise the authority locator and the accept sets before the change` — C3-C8 (green; counts quoted).
2. `test(392): the authority locator and terminator set live only in UrlAuthority (red)` — G-US3a, G-US3b.
3. `refactor(392): AuditPayloadRedaction locates the authority through UrlAuthority` — §3.1 `TextFromTheAuthority` /
   `IndexOfTheTerminator`, `AtSignFollowsTheAuthority` rebuilt on them, §3.3, FR-007 comment. C1-C3, C9 unmodified
   green; G-US3a/b green. No value object or `ToOrigin` change in this commit.
4. `test(3017,3012): an empty host and text after the closing bracket are refused and redacted (red)` — R1-R7, G-US3c.
5. `fix(3017,3012): refuse an empty host and text after the closing bracket; ToOrigin withholds the tail` — §3.1
   `TextFollowsTheClosingBracket`, §3.2, §3.4. All of §5 green.
6. `test(392): direct rows for the UrlAuthority members` — T040 (post-green).
7. adversary findings (T050), as separate `test`/`fix` commits.

If the reviewer wants the refactor as its own PR, commits 1-3 stack cleanly on `develop` and 4-7 follow; one PR is
the default because the issues were delivered together.

## 7. Existing tests whose assertions change

Found by `grep -rnE '\]%25|\]%e|\]garbage|\][a-z]|rtsp:///|"rtsp://"|rtsp:////|\]\]'` and
`grep -rnE 'rtsp:[^"]*\][^:/"?#\\ ,)]'` over `tests/`, `src/`, `apps/`, each hit read for whether its URL reaches a
value object's `From` or `ToOrigin` with an outcome this spec changes:

| File | Change | Why it is not a silent edit |
|---|---|---|
| `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs` | G-US3a (two expected lists `[AuditCopy, Declaration]` -> `[Declaration]`; `AuditCopy` and the summary paragraph removed; two method names) | tightening the guard; prescribed by the test's own summary; listed in the PR |

No other existing assertion changes. Hits that keep their assertions and why:

- `UrlAuthorityTerminatorTableTests.cs:63-64,77,88-89`, `UrlAuthorityTests.cs:34` — predicate rows for `rtsp:///…`
  and `rtsp://`; the predicate is not changed (FR-007).
- `RtspUrlAtSignAdversarialTests.cs:47-48`, `StreamSourceUrlAtSignAdversarialTests.cs:47-48`,
  `CameraUrlRefusalEndpointAdversarialTests.cs:42`, `CameraUrlRefusalAgreementTests.cs:47` (`rtsp:///admin:4242#@cam`,
  `rtsp:////admin:4242#@cam`) — refused on the raw-`@` rule, which runs before the new rules (FR-001), so the
  message they pin is unchanged.
- `UrlRedactionAdversarialTests.cs:64` (`rtsp:///SECRET`), `UrlRedactionHostCharsetAdversarialTests.cs:86-87`,
  `AuditPayloadDetailChangedAdversarialTests.cs:62` (`rtsp:///s`) — already the placeholder.
- `AuditPayloadDetailChangedUserinfoBracketTests.cs:21-27` (`[]@[fe80::1/64%25eth0]` …) — the host after the
  userinfo has no tail, so `ToOrigin` is unchanged and `DetailChanged` stays `null`.
- `UrlAuthorityTerminatorTableTests.cs:79` (`rtsp://[::1]x@cam`) — predicate row; `ToOrigin` host is `cam`
  (userinfo `[::1]x`), the bracket rule sees host `cam` and answers false.
- The seeded fuzz in `UrlRedactionUserInfoAdversarialTests` only flags rendered outputs; more placeholders cannot
  create an offender.

If a branch merged after `de9802b1` adds a row of this kind, T001 finds it and it is handled the same way, listed
in the PR.

## 8. Risks

- **R-1 — the bracket scan disagrees with the parser on some input.** Over-flagging is fail-closed (placeholder;
  `From` refuses). Under-flagging leaves today's behaviour. Mitigation: R3b agreement over the corpus, the
  adversary pass (T050) with a seeded fuzz comparing `Uri.Host`/`Uri.Port` against the written authority for
  every accepted bracketed input.
- **R-2 — accepted input narrows** (spec A-5) for API clients, the simulator and the UI (which accepts most of
  these client-side). Decided on #3012.
- **R-3 — stored rows with these shapes stay readable and visible in clear** through `CameraDto` until #3016 lands.
  Not made worse.
- **R-4 — a pre-deploy event with such an address dead-letters** (spec 389 R-2/R-3 apply unchanged).
- **R-5 — the refactor changes behaviour after all.** Mitigation: C2/C3 unmodified, the fuzz in spec §1.3 re-run by
  the test-writer as a scratch check (not committed) and its count quoted, and the C3 trim counterfactual.
- **R-6 — wording drift between the two value objects.** R1/R2 pin the exact literal in both; R3b asserts the two
  `From`s agree.
- **R-7 — name drift between plan and code** (§3.1). G-US3b/c scan for the names; changing them means changing
  this plan and the guard together, visibly.
