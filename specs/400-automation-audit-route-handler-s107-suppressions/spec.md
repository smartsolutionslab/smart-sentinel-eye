# Spec 400: the Automation and AuditObservability route handlers carry ADR-0084's S107 suppression

**Feature Branch**: `chore/2974-automation-audit-s107-suppressions` (cut from `origin/develop` at `f950cb7c`)

**Created**: 2026-10-18

**Status**: Draft (phases 1-3). **Two decisions are open (D-1, D-2, §6); the lane must not start phase 4
until both are confirmed.** Each has a recommended option; the tasks are written for the recommended
options and name the tasks that change if the other is chosen.

**Input**: Issue [#2974](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2974), as narrowed
by its comments. The body (bundle injected services like spec 341) is **superseded**: comment 1 proved S107
does not count `[FromServices]` parameters; comment 4 (spec 383 analysis) found all 8 hits are route
handlers and parked the issue; comment 5 (owner decision, 2026-10-17, #2970 merged as `c7a6e586`)
re-enrolled it with the per-handler `[SuppressMessage]` this spec applies, and forbids changing handler
signatures, OpenAPI, route-table baselines or any existing assertion.

**Prior art, copied exactly**: spec 398 (#2972, merged as PR #3034, all checks green) and spec 397 (#2971,
open PR #3037). The attribute text is theirs, byte for byte.

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation, observed
green**. Engineer: **backend-engineer**. Test-writer: one new characterisation file (D-1a). Not
security-sensitive (§3): no signature, route, policy, fab-resolution or audit-read line changes; Phase 6 is
`/code-review`.

**Spec number.** 400, assigned by the orchestrator (396-398 taken; 399 is #2973's, in parallel). Checked
2026-10-18 across every `origin/*` branch: no `specs/400-*` exists.

**ADRs.** **ADR-0084** (S107; 2026-10-16 amendment, accepted 2026-10-17: "S107 does not apply to the
route-handler methods themselves ... Helpers in the same files stay subject to S107"; mechanism "a
`[SuppressMessage]` for S107 with a justification on each of the 45 handlers"; Consequences: "It closes
#2971, #2972 and #2974"), ADR-0144 (lane; the one authorised suppression; characterisation colour),
ADR-0070 (Minimal APIs), ADR-0036 (smallest change), ADR-0087 (each commit builds on its own).

**No new ADR.** The decision is made and accepted. One stale *evidence* sentence results from D-1a (§6) and
is reported, not edited (the lane may not edit ADRs).

## 1. Premise check — the re-measured hit list

Measured 2026-10-18 on `f950cb7c`, one Api project closure at a time (the machine is shared):

```sh
dotnet build src/<Context>/Api/SmartSentinelEye.<Context>.Api.csproj -c Release --no-incremental \
  "-flp:logfile=<scratch>\<ctx>.log;verbosity=normal;NoSummary"
grep -oE '[^ ]+\([0-9]+,[0-9]+\): (warning|error) [A-Za-z]+[0-9]+' <scratch>/<ctx>.log | tr '\\\\' '/' \
  | sed -E 's#^.*/src/#src/#' | grep -v Migrations | sort -u
```

(The logfile path must be a Windows path: a `/c/...` path under Git Bash is written to `D:\c\...`.)

**8 S107 hits under the two Api projects, all 8 mapped route handlers, 0 helpers.** Identical to issue
comment 4 and to ADR-0084's per-context table (Automation 6, AuditObservability 2). The premise holds.

| # | File (line) | Method | Counted | Mapped at |
|---|---|---|---|---|
| A1 | `src/Automation/Api/RulesEndpoints.cs` (164) | `Create` | 6 | `RulesEndpoints.cs:57` `MapPost("/")` |
| A2 | `src/Automation/Api/RulesEndpoints.cs` (250) | `Publish` | 5 | `RulesEndpoints.cs:70` |
| A3 | `src/Automation/Api/RulesEndpoints.cs` (288) | `Archive` | 5 | `RulesEndpoints.cs:84` |
| A4 | `src/Automation/Api/RulesEndpoints.Reads.cs` (23) | `List` | 6 | `RulesEndpoints.cs:98` |
| A5 | `src/Automation/Api/RulesEndpoints.Reads.cs` (50) | `GetOne` | 5 | `RulesEndpoints.cs:109` |
| A6 | `src/Automation/Api/RulesEndpoints.Reads.cs` (82) | `DryRun` | 5 | `RulesEndpoints.cs:124` |
| B1 | `src/AuditObservability/Api/AuditEndpoints.cs` (60) | `Search` | 12 | `AuditEndpoints.cs:32` |
| B2 | `src/AuditObservability/Api/AuditEndpoints.cs` (116) | `GetTimeline` | 9 | `AuditEndpoints.cs:40` |

All are `private static async Task<IResult>`, none has an XML doc comment. **Not hits, so no attribute**:
`AuditEndpoints.GetSingle` (3 counted), and the helpers `ResolveWriteFabAsync`, `ParseRule`, `CreateScope`,
`CreateAsync`, `BuildAction` and its three `Build*` siblings, `ResolveReadFabsAsync`. No helper hit exists,
so the "helpers out of scope unless trivially behaviour-preserving" question does not arise.

**The rest of each closure, which must be unchanged after (SC-2):**

- Automation (6 lines): S107 `Automation/Domain/Rule/Rule.cs:58`,
  `ServiceDefaults/Authentication/ClientCredentialsTokenProvider.cs:88`,
  `ServiceDefaults/Authorization/FabResolution.cs:49`, `ServiceDefaults/Idempotency/IdempotencyScope.cs:46`,
  `ServiceDefaults/WolverineDefaults.cs:40`; S138 `ServiceDefaults/WolverineDefaults.cs:40`.
- AuditObservability (6 lines): S107 `AuditObservability/Application/EventHandlers/AuditingMessageHandler.cs:33`
  and the same four ServiceDefaults S107 plus the S138.

No S104 in either closure today.

## 2. User story

### US1 (P1) — the S107 count for these two contexts' handlers measures nothing nobody can act on

As the maintainer reading ADR-0084's S107 count, I want the eight Automation and AuditObservability route
handlers to carry the sanctioned suppression, with a justification that is true for each context, so the
count stops including HTTP contracts the amendment declared out of scope while every contract stays
exactly as it is.

**Independent test**: the two Release builds report 0 S107 under `src/Automation/Api` and
`src/AuditObservability/Api` and the §1 remainder unchanged with no new S104; both route-table baselines,
`RuleActionBuildCharacterisationTests`, `RuleCreateEndpointCharacterisationTests` and the six
source-scanning guards pass; no pre-existing file under `tests/` is modified.

**Acceptance scenarios**

```gherkin
Scenario: the eight handlers are no longer counted
  Given each Api closure is built in Release with --no-incremental
  When the S107 warnings under src/Automation/Api and src/AuditObservability/Api are listed
  Then there are none

Scenario: nothing else moved (conflict: a leaking suppression, or a file pushed over S104's 300)
  Given the same builds
  When every distinct warning in each closure is listed
  Then each list equals its §1 remainder exactly
  And no S104 appears on RulesEndpoints.cs, RulesEndpoints.Reads.cs, RulesEndpoints.Actions.cs or AuditEndpoints.cs

Scenario: the HTTP contract is unchanged (the attribute reaches no metadata)
  Given the Automation and AuditObservability route tables rendered from the mapped endpoints
  When each is compared with its baseline captured on unchanged code
  Then AutomationRouteTableCharacterisationTests and AuditObservabilityRouteTableCharacterisationTests pass unmodified

Scenario: the justification states something true for every context (bad request: a false text)
  Given a handler carrying the prescribed attribute
  Then its context has a *RouteTableCharacterisationTests baseline that prints that handler's full parameter list

Scenario: the source-scanning guards still read the handlers
  When ConcurrencyConflict-, EndpointScope-, IdempotentKeyReuse-, Precondition-, RouteValueRefusal-
       and StatusProducerDeclarationTests run
  Then every one passes unmodified

Scenario: a helper is not exempted (auth/scope of the exemption)
  Given a method in these files that is not passed to a Map call
  Then it carries no S107 suppression
```

## 3. Why this is behaviour-preserving, and where it could leak

- **Not emitted.** `SuppressMessageAttribute` is `[Conditional("CODE_ANALYSIS")]`. Both Release compile
  lines define `TRACE;RELEASE;NET;NET10_0;...` and the logs contain `CODE_ANALYSIS` zero times; no `.props`,
  `.targets`, `.csproj` or workflow defines it. **Observed** on a throwaway probe (reverted, worktree clean):
  with the six Automation attributes in place the built `SmartSentinelEye.Automation.Api.dll` contains
  `SuppressMessageAttribute` 0 times and the justification text 0 times. Minimal APIs copy a handler's
  method attributes into endpoint metadata, which the route-table renderers print as `other <Type>`; were
  the attribute emitted, each baseline would fail, which is why they are the covering tests.
- **Source readers.** `RouteChainReader.MethodBodies` matches a declaration on one line
  (`(?:private|...)[^;{}()\n]*?\bName\s*\(`); an attribute on preceding lines does not intervene.
  `HandlerBodyFor` resolves across every partial span of the class in the same Api project, so a method in
  another partial file is found exactly as `List`/`GetOne`/`DryRun` in `RulesEndpoints.Reads.cs` are today.
  The identical attribute form is already on `develop` in ten LayoutComposition handlers (spec 398, PR #3034,
  all checks green), and the guards scan every `src/*/Api` file, so the form is proven against all six.
- **Literal forms.** The justification is two plain `"..."` literals, no `@"`, `"""`, `\"` or `'"'`, which
  `ConcurrencyConflictDeclarationTests`' corpus ban requires.
- **Line-number pins.** No test pins `RulesEndpoints*.cs` or `AuditEndpoints.cs` by line (searched
  `tests/`); one Integration test names `AuditEndpoints.cs` in a doc comment only.

## 4. Success criteria

- **SC-1**: S107 under `src/Automation/Api` 6 -> 0 and under `src/AuditObservability/Api` 2 -> 0 (§1).
- **SC-2**: each closure's remaining distinct warnings equal §1's six lines, and no S104 appears. Measured
  by probe: the attributes alone take `RulesEndpoints.cs` from 289 to **305** code lines and Sonar reports
  `S104: This file has 305 lines, which is greater than 300 authorized`. Hence D-2. `RulesEndpoints.Reads.cs`
  goes 110 -> 126 and `AuditEndpoints.cs` 166 -> 177: no S104.
- **SC-3**: no pre-existing file under `tests/` changes (`git diff f950cb7c --diff-filter=M -- tests/` empty);
  the only `tests/` addition is the D-1a baseline, committed before any `src/` edit.
- **SC-4**: exactly 8 occurrences of the prescribed attribute across the two Api projects, one per §1 row;
  `grep -rc "S107:Methods should not have too many parameters" src` totals 18 with spec 398's ten (more if
  #2971/#2973 have merged meanwhile; count only these two projects for the gate).

## 5. Locked choices and the prescribed form

Byte-identical to specs 397 and 398:

```csharp
    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> Create(
```

- Directly above the declaration line, never on the same line.
- `using System.Diagnostics.CodeAnalysis;` in sorted position: first line of `RulesEndpoints.cs`,
  `RulesEndpoints.Reads.cs` and `AuditEndpoints.cs` (each currently starts `using System.Security.Claims;`).
- Not `.editorconfig`, not `#pragma`, not a glob section, not a shared `const`, not
  `UnconditionalSuppressMessage` (emitted; it would move the baselines).

**Latency budget**: N/A — no runtime change, nothing on the event-to-overlay path.

## 6. Decisions for the orchestrator / owner

### D-1 — AuditObservability has no route-table baseline, so the fixed text would be false there

Verified: `tests/Architecture.Tests` has `AutomationRouteTableCharacterisationTests` (prints each of A1-A6's
full parameter list, e.g. `handler Publish(name: System.String [], request: ...HttpRequest [], fabGuard: ...
[FromServicesAttribute], ...)`), and **no** `AuditObservability*RouteTableCharacterisationTests`, no
`AuditObservabilityEndpointHost`, and no test anywhere that renders `MapAuditEndpoints`' metadata.
ADR-0084's evidence paragraph says the same ("AuditObservability has none"). The justification "pinned by
the route-table characterisation baseline" is **true for Automation and false for AuditObservability**.

- **D-1a (recommended)**: write `tests/Architecture.Tests/AuditObservabilityRouteTableCharacterisationTests.cs`
  first, in the SystemVariables shape (no host class: `WebApplication.CreateBuilder([]).Build()`,
  `app.MapAuditEndpoints()`, `RouteTableRendering.RenderAll(InProcessEndpointDriver.Endpoints(app))`
  against a literal baseline), captured green on unchanged code (`f950cb7c`). The text then becomes true and
  stays byte-identical, and AuditObservability gains the contract pin the other contexts have. Cost: one
  test file (~90 lines, mostly the literal). Architecture.Tests already references the Audit Api project
  and is not sharded (shard filters exist only for Integration.Tests). Side effect: ADR-0084's "AuditObservability
  has none" becomes a stale dated observation; reported in the PR, not edited.
- **D-1b**: a context-specific justification for B1/B2, e.g. `"ADR-0084 (2026-10-16 amendment): a route
  handler's parameters are its HTTP contract; S107 does not apply to them."` The ADR fixes no text, so this
  is still prescribed-compliant, and `grep "S107:Methods should not have too many parameters"` still counts
  all of them; but the byte-identity across contexts is lost and the contract stays unpinned.

### D-2 — the attributes push `RulesEndpoints.cs` over S104

Measured (§4 SC-2): 289 -> 305 code lines, a new S104. The owner's criterion is "every other warning
unchanged", so the attributes alone fail it.

- **D-2a (recommended)**: before the attributes, move the four rule-action builders (`BuildAction`,
  `BuildSetVariableValue`, `BuildHighlightOverlay`, `BuildSwitchWallScene`, lines 326-357, ~30 code lines,
  pure static, not handlers, not S107 hits) verbatim into a new partial file
  `src/Automation/Api/RulesEndpoints.Actions.cs`. Same type, same names, same bodies: route table
  unchanged, `RuleActionBuildCharacterisationTests` (finds `BuildAction` by reflection on `RulesEndpoints`)
  unchanged, `RouteChainReader` unaffected (helpers are not handlers, and it resolves across partial spans
  anyway). Result ~275 code lines. The `SetVariableValue`/`HighlightOverlay`/`SwitchWallScene` constants
  stay where they are. A behaviour-preserving move in its own commit, outside the issue's literal wording
  but forbidden by none of it.
- **D-2b**: accept one new advisory S104 on `RulesEndpoints.cs` (S104 is carved out of
  `TreatWarningsAsErrors`; nothing fails). Contradicts the owner's stated criterion, so only on the owner's
  word.
- Rejected: a compact one-line attribute for this file (breaks byte-identity); a fully-qualified attribute
  to skip the `using` (saves one line, 304 still over); moving a handler (touches a guarded method for no gain).

## 7. Overlap with open PR #3036 (#2927, `fix/2927-audit-unresolved-fab-rows`)

#3036 edits `AuditEndpoints.cs` only inside `GetSingle`'s body (develop lines 194-206: passes `callerFabs`
to `GetAuditEventQuery`, adds a comment). This spec touches that file at line 1 (`using`) and above lines
60 and 116 (attributes). The hunks are 75+ lines apart, `GetSingle` is not a hit and gets nothing, and #3036
changes no handler signature or endpoint metadata, so the D-1a baseline (which renders `GetSingle` too) is
the same before and after #3036. Its new test files (`AuditReadEndpointAdversarialTests.cs`, a
`Persistence/` file, Application tests) are disjoint from this spec's one new file. **No need to wait for
#3036**: whichever merges second rebases without conflict. After any rebase, re-run T001/T021 (the baseline
must still pass unmodified; if #3036 had changed metadata it would fail loudly, not silently).

## 8. Assumptions

- **A-1**: Sonar's S104 line count matches "non-blank, non-comment lines" closely enough to plan with; the
  gate is the build's own report (SC-2), not this estimate.
- **A-2**: `"Major Code Smell"` is cosmetic; Roslyn matches on `S107` before the colon. Proven by the probe
  (6 Automation hits vanished) and by spec 398.

## 9. Out of scope (what the lane must not take)

- Any handler signature, `*Services` record, `Map*` chain, route, OpenAPI metadata, summary or policy.
- Any **existing** file under `tests/`; an assertion that would need editing is a **block**.
- `GetSingle`, every helper, and any S107 outside the two Api projects (Rule.cs, AuditingMessageHandler,
  ServiceDefaults): advisory per the amendment.
- `AuditEndpoints.cs`' `GetSingle` body (owned by #3036), `.editorconfig`, `SonarLint.xml`,
  `Directory.Build.props`, csproj files, ADRs (including the stale ADR-0084 sentence), the constitution,
  CLAUDE.md.
- The PR closes #2974 (`Closes #2974`): ADR-0084's Consequences name it, and all eight hits are covered.
