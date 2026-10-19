# Plan 416: the audit's bracket scan folds onto `UrlAuthority`

**Spec**: [spec.md](./spec.md) · **Tasks**: [tasks.md](./tasks.md) · **Issue**: #3023 item 6 (owner scope: PR 2 only).
Supersedes spec 411 plan §4 and §6.5-§6.6 on the points spec §1.3-§1.4 correct.

## 1. Constitution and ADR check

| Gate | Result |
|---|---|
| §II primitives on domain models | Pass. No domain model touched. `UrlAuthority` is a stateless helper returning `bool`. |
| §III no cross-context references | Pass. AuditObservability.Application already references Shared.Kernel. Code moves *into* Shared.Kernel. |
| §IV latency | N/A (audit consumer). |
| ADR-0105 | The new public member opens with `Ensure.That(url).IsNotNull()`, as its siblings do. Unobservable: `CarriesZone` never passes `null`. |
| ADR-0139 / ADR-0144 colour | **Characterisation, observed green.** C-F0 and C-F1 green on `d24ee84e`, unmodified after. One pre-declared structure retarget (spec §1.4); no behaviour assertion edited. |
| ADR-0144 limits | No ADR, no constitution change, no suppression, no deleted test, no lowered threshold. |
| ADR-0087 | Each commit builds **and passes** on its own (the retarget travels in the fold commit for that reason). |
| ADR-0084 | Advisory. Keep the new member at complexity <= 10 by extracting the userinfo-bracket arm into a private helper. |
| ADR-0036 | `TextFromTheAuthority` / `IndexOfTheTerminator` stay public (direct tests use them). `CarriesZone`'s `Uri` arm untouched. |
| New ADR needed? | **No.** |

## 2. Layers and files

| Project | Layer | File | Change |
|---|---|---|---|
| Shared.Kernel | n/a | `src/Shared.Kernel/UrlAuthority.cs` | + `public static bool BracketContentIsAddressOnly(string url)`, + private helper(s) for the userinfo arm and address-text check |
| AuditObservability | Application | `src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs` | `CarriesZone` calls the new member; delete private `BracketContentIsAddressOnly` and `IsAddressText`; update the two comments above them |
| tests | Architecture | `tests/Architecture.Tests/AuditPayloadBracketScanOracle.cs` | **new**, commit 1, frozen thereafter |
| tests | Architecture | `tests/Architecture.Tests/AuditPayloadBracketFoldCharacterisationTests.cs` | **new**, commit 1 (C-F1), frozen thereafter |
| tests | Architecture | `tests/Architecture.Tests/CameraUrlZoneDelimiterFuzzAdversarialTests.cs` | `Generate`: `private` → `internal`, one word, commit 1 |
| tests | Architecture | `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs` | retarget `The_audit_redaction_asks_the_shared_locator` (G2), commit 2; class summary sentence updated |
| tests | Architecture | `tests/Architecture.Tests/UrlAuthorityBracketContentOracleEquivalenceTests.cs` | **new**, commit 3 (G3) |
| tests | Shared.Kernel | `tests/Shared.Kernel.Tests/UrlAuthorityBracketContentDirectTests.cs` | **new**, commit 3 (direct rows, coverage) |

Not touched: `UrlRedaction.cs`, `RtspUrl.cs`, `StreamSourceUrl.cs`, every other caller of `BracketedHostReadings`,
contracts, Api, Infrastructure, migrations, AppHost, frontend.

No foundational task, no fan-out: two production files in a strict sequence after the characterisation.

## 3. Shape of the fold

```csharp
// Shared.Kernel.UrlAuthority
public static bool BracketContentIsAddressOnly(string url);
```

Algorithm (the engineer chooses the code; the obligation is C-F1 and G3 unmodified and green):

1. `Ensure.That(url).IsNotNull()`.
2. **Userinfo-bracket arm, verbatim semantics of A lines 159-172**: on `TextFromTheAuthority(url.AsSpan().Trim())`;
   empty authority → false; cut at `IndexOfTheTerminator`; if a `[` or `]` occurs before that text's **last** `@` →
   false. Note A uses `Trim()` (both ends), not `TrimStart()`; keep it here (the locator characterisation suite pins
   the leading trim, `AuditPayloadDetailChangedLocatorCharacterisationTests` counterfactual).
3. `BracketedHostReadings(url)`: true iff at least one reading has its first `]` at index > 0, and every such reading's
   content (`[1..close]`) is non-empty and only ASCII hex, `:` or `.`. After step 2 there is at most one such reading
   (spec §1.2), so "every" and "the" coincide; "every" is the fail-closed wording should a later edit to the readings
   add one.
4. The address-text check (A's `IsAddressText`) moves into `UrlAuthority` as a private helper.

Comment on the new member says *why* (the parser drops a zone written behind a prefix length from `DnsSafeHost`, so
the audit reads the written text; a bracket in the userinfo or no closing `]` must read as unknown, never as the same
host). `CarriesZone`'s comment keeps its reasoning and names the new member.

If the engineer finds a cleaner shape, the obligation is unchanged. A C-F1 row that would have to change is a
**block**, filed as its own issue, never adjusted.

## 4. Characterisation design

### 4.1 C-F0: existing suites, counts before and after (no file change)

```sh
dotnet test tests/AuditObservability.Application.Tests
dotnet test tests/Shared.Kernel.Tests --filter "FullyQualifiedName~UrlAuthority|FullyQualifiedName~UrlRedaction"
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~CameraUrl|FullyQualifiedName~UrlAuthoritySingleSource|FullyQualifiedName~AuditPayload"
dotnet test tests/CameraCatalog.Domain.Tests --filter "FullyQualifiedName~RtspUrl"
dotnet test tests/StreamDistribution.Domain.Tests --filter "FullyQualifiedName~StreamSourceUrl"
```

Passed/failed/skipped quoted verbatim on `d24ee84e` and after commit 2. Identical, except the Architecture count,
which differs by exactly the C-F1/G3 tests added (stated). In particular every `AuditPayloadDetailChanged*` suite
(`...BracketContentTests`, `...UserinfoBracketTests`, `...ZoneTests`, `...ZoneAdversarialTests`,
`...LocatorCharacterisationTests`, `...BracketTail*`, `...LeadingWhitespaceBracketTailTests`, `...UnknownTests`,
`...AdversarialTests`) passes with `git diff d24ee84e -- tests/AuditObservability.Application.Tests` empty.
Stop any running AppHost before building (memory: stop the stack before building).

### 4.2 The oracle (`AuditPayloadBracketScanOracle.cs`, `internal static class`)

A copy of today's behaviour, kept in the test project:

- `CarriesZone`, `BracketContentIsAddressOnly`, `IsAddressText`: **copied verbatim** from
  `AuditPayloadRedaction.cs:150-202` at `d24ee84e` (bodies byte-for-byte; only `private` → `internal` and the
  containing class differ). The file's summary names the source commit and lines.
- `DetailChanged(string? previousUrl, string? url)`: verbatim from `:121-143`, calling the live
  `UrlRedaction.ToOrigin` and comparing against the live `UrlRedaction.Unparseable`. These are not touched by the
  fold, and the oracle must not fork them (a forked `ToOrigin` would hide nothing and test nothing).
- The oracle calls `UrlAuthority.TextFromTheAuthority` / `IndexOfTheTerminator` as A does. Both are public and
  untouched by the fold, so the oracle stays today's code.
- Exposes, for the reach floors, `IsBracketCheckReached(url)`: both origins render, `Uri.TryCreate` succeeds,
  `HostNameType == IPv6`, `DnsSafeHost` has no `%` (the exact conditions under which A runs).

**Frozen.** After commit 1 the oracle file never changes (`git diff <commit1> -- tests/Architecture.Tests/AuditPayloadBracketScanOracle.cs`
empty at the tip; Phase 6 checks it).

### 4.3 C-F1 (`AuditPayloadBracketFoldCharacterisationTests.cs`), green before the fold, unmodified after

Corpus: every input of `CameraUrlZoneDelimiterFuzzAdversarialTests.Corpora` (six seeds, `Generate` made `internal`;
the generator is **not** copied), plus the explicit rows:

| Row | Why |
|---|---|
| `rtsp://[]@[FE80::1]`, `rtsp://[x]@[::1]:/s?x#y` | the userinfo-bracket arm (spec 411 §1.7) |
| `rtsp://a@b@[fe80::1%25eth0]/x`, `rtsp://a@b@[fe80::1]/x` | two `@`, first-`@` reading vs last |
| `rtsp://u:p@[fe80::1/64%25eth0]/x`, `rtsp://[fe80::1/64]/x` | zone behind a prefix length; prefix only |
| `  rtsp://[fe80::1]/x  `, `\trtsp://[fe80::1]/x`, `rtsp://[fe80::1] ` | `Trim` vs `TrimStart`, trailing text without terminator |
| `rtsp://@[fe80::1]/x`, `rtsp://[::1]@  `, `rtsp://a@\u0001/x` | empty userinfo; `@` followed only by parser-trimmed text |
| `rtsp://[fe80::1`, `rtsp://[]/x`, `rtsp://   ` | no `]`; empty content; blank authority |
| `rtsp://[FE80::1]/x`, `rtsp://[::ffff:10.0.0.1]/x`, `rtsp://[fe80::1]:554/x` | address-only, true path (hex case, dotted, port) |

Assertion, per input `u`: for both `(u, P)` and `(P, u)` with partner `P = "rtsp://[fe80::1]/s"`,
`AuditPayloadRedaction.Serialize(new CameraAddressChangedV1(...))`'s `DetailChanged` JSON value kind equals the
oracle's `DetailChanged` (`true` / `false` / `null`). All mismatches are collected and the first 20 printed
(`ShouldBeEmpty` with the list), as the fuzz test does.

Reach floors (so it cannot pass vacuously), counted with the oracle, measured at T111 and written into the test with
the measured figure in a comment:

- inputs where the bracket check is reached: >= ~90% of measured (spec 411 measured 725 on five seeds);
- reached inputs with a `[`/`]` before the last `@`: >= ~90% of measured (spec 411: 110);
- reached inputs where the oracle's `BracketContentIsAddressOnly` is **true**, and where it is **false** without a
  userinfo bracket: each >= ~90% of measured. Without these a fold that always answers one way could pass.

Floors below spec A-1's block line are a block, not a lowered floor.

**Counterfactuals (run once, reverted, quoted verbatim in the PR body):**

- CF-1: the fold **without** step 2 (the userinfo arm). C-F1 must fail; quote the mismatch count (expected about
  2 x the userinfo-bracket reach).
- CF-2: the new member returning `true` unconditionally. C-F1 must fail (the false-reach floor's inputs flip from
  `null` to `true`/`false`).
- CF-3 (before the fold, against today's code): change A's `IsAddressText` to also accept `%`. C-F1 must fail. This
  proves C-F1 can see a change in A itself, not only in a new member.

### 4.4 Post-fold guards, green on arrival (stated as such; not phase-4a evidence)

- **G2** (retarget, spec FR-004), `UrlAuthoritySingleSourceTests`: `The_audit_redaction_asks_the_shared_locator`
  becomes `The_audit_redaction_asks_the_shared_bracket_scan`: `AuditPayloadRedaction.cs` executable lines contain
  `UrlAuthority.BracketContentIsAddressOnly(` and contain no `TextFromTheAuthority(` and no `IndexOfTheTerminator(`.
  Plus `Only_UrlAuthority_declares_the_bracket_content_predicate` (`bool BracketContentIsAddressOnly(` declared only
  in `src/Shared.Kernel/UrlAuthority.cs`), mirroring the sibling pairs. Its regexes follow the file's existing
  pattern. Counterfactual: reinstate a local `UrlAuthority.TextFromTheAuthority(` call in the audit, G2 red, quoted.
- **G3**, `UrlAuthorityBracketContentOracleEquivalenceTests`: over the **whole** corpus plus the explicit rows (not
  only reached inputs), `UrlAuthority.BracketContentIsAddressOnly(u) == AuditPayloadBracketScanOracle.BracketContentIsAddressOnly(u)`.
  This is the predicate-level, total equivalence of spec §1.2 for future callers of the now-public member; C-F1 can
  only see the reached domain. Floors on oracle-true and oracle-false counts. It cannot exist before the fold (the
  member does not), which is why it is not characterisation.
- **Direct rows**, `UrlAuthorityBracketContentDirectTests` (Shared.Kernel.Tests): a handful of `true`/`false` rows
  from §4.3's table plus the `null` guard, so Shared.Kernel coverage (ADR-0065) does not depend on Architecture.Tests.

## 5. Messaging

None. No domain event, integration event or contract changes. Replay is unaffected: the audit answer per stored
message is identical (C-F1).

## 6. Commit plan (ADR-0030/0087; each commit builds and passes on its own)

1. `test(3023): characterise the audit's zone answer against an oracle copy of today's bracket scan`: oracle, C-F1,
   `Generate` → `internal`. C-F0 + C-F1 green on unchanged code, quoted; CF-3 run and reverted.
2. `refactor(3023): the audit asks UrlAuthority whether a bracketed host holds only an address`: `UrlAuthority.cs`,
   `AuditPayloadRedaction.cs`, and the G2 retarget in `UrlAuthoritySingleSourceTests.cs` (it must travel here, or
   commit 2 is red). C-F0 + C-F1 unmodified, identical counts; CF-1, CF-2 run and reverted.
3. `test(3023): the shared bracket scan answers as the oracle on every input`: G3 and the direct rows.

PR body: `Phase 4a: characterisation`, C-F0 before/after counts, C-F1 green before/after, CF-1..CF-3 verbatim,
G2's counterfactual, and spec §1.4's retarget named explicitly. `Refs #3023` (items 4 and Q1-Q3 stay open; not a
closing keyword).
