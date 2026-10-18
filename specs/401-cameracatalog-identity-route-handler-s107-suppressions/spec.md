# Spec 401: the CameraCatalog and Identity route handlers carry ADR-0084's S107 suppression

**Feature Branch**: `chore/2975-cameracatalog-identity-s107-suppressions` (cut from `origin/develop` at `7f05b12b`)

**Created**: 2026-10-18

**Status**: Draft (phases 1-3). **No decision is open** (§6): both contexts already have a route-table
baseline and no file approaches S104, so neither of spec 400's two decisions recurs here.

**Input**: Issue [#2975](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2975), as narrowed
by its comments. The body (bundle injected services like spec 341) is **superseded**: comment 1 proved S107
does not count `[FromServices]` parameters; comments 2-5 cleared the one helper hit
(`IdentityFabResolution.ResolveReadFabsAsync`, PR #3002) and parked the seven handler hits; comment 6
(owner decision, 2026-10-17, #2970 merged as `c7a6e586`) re-enrolled them with the per-handler
`[SuppressMessage]` this spec applies, and forbids changing handler signatures, OpenAPI, route-table
baselines or any existing assertion.

**Prior art, copied exactly**: spec 397 (EventIngestion), spec 398 (LayoutComposition), spec 399
(OverlayDesigner + SystemVariables), all merged on `develop` (30 attributes of this exact form), and spec
400 (Automation + AuditObservability, open PR #3039). The attribute text is theirs, byte for byte.

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation, observed
green**. Engineer: **backend-engineer**. Test-writer: runs and quotes the existing covering tests only; **no
new test file** (both baselines exist, §6). Phase 6: `/code-review` **and** `security-reviewer` (§3, last
bullet).

**Spec number.** 401, assigned by the orchestrator (396-400 taken). Checked 2026-10-18 after `git fetch`
across every `origin/*` branch: no `specs/401-*` exists. No open PR touches `src/CameraCatalog/Api` or
`src/Identity/Api`.

**ADRs.** **ADR-0084** (S107; 2026-10-16 amendment, accepted 2026-10-17: "S107 does not apply to the
route-handler methods themselves ... Helpers in the same files stay subject to S107"; per-context table
"CameraCatalog 4, Identity 3"; Consequences: it closes "the handler hits of #2973 and #2975"), ADR-0144
(lane; the one authorised suppression; characterisation colour), ADR-0070 (Minimal APIs), ADR-0036
(smallest change), ADR-0087 (each commit builds on its own).

**No new ADR.** The decision is made and accepted, and ADR-0084's evidence paragraph is already true for
both contexts ("Identity, CameraCatalog" are listed among those with a baseline), so nothing in it goes
stale.

## 1. Premise check — the re-measured hit list

Measured 2026-10-18 on `7f05b12b`, one Api project closure at a time (the machine is shared):

```sh
export MSYS_NO_PATHCONV=1
dotnet build src/<Context>/Api/SmartSentinelEye.<Context>.Api.csproj -c Release --no-incremental \
  "-flp:logfile=C:/Users/.../scratchpad/<ctx>.log;verbosity=normal;NoSummary"
grep -oE '[^ ]+\([0-9]+,[0-9]+\): (warning|error) [A-Za-z]+[0-9]+' <scratch>/<ctx>.log | tr '\\\\' '/' \
  | sed -E 's#^.*/src/#src/#' | grep -v Migrations | sort -u
```

(The logfile path must be a native `C:/` forward-slash path with `MSYS_NO_PATHCONV=1`: a `/c/...` path under
Git Bash is written to `D:\c\...`.)

**7 S107 hits under the two Api projects, all 7 mapped route handlers, 0 helpers.** The issue body's eighth
hit, `IdentityFabResolution.ResolveReadFabsAsync`, is gone (cleared by PR #3002, as comment 5 says) and no
other helper is a hit. Identical to ADR-0084's per-context table (CameraCatalog 4, Identity 3). The premise
holds.

| # | File (line) | Method | Counted (S107 excludes `[FromServices]`) | Mapped at |
|---|---|---|---|---|
| C1 | `src/CameraCatalog/Api/CameraEndpoints.cs` (155) | `Register` | 5 | `CameraEndpoints.cs:53` `MapPost("/")` |
| C2 | `src/CameraCatalog/Api/CameraEndpoints.cs` (259) | `Patch` | 5 | `CameraEndpoints.cs:105` |
| C3 | `src/CameraCatalog/Api/CameraEndpoints.Reads.cs` (14) | `Get` | 5 | `CameraEndpoints.cs:128` |
| C4 | `src/CameraCatalog/Api/CameraEndpoints.Reads.cs` (51) | `List` | 9 | `CameraEndpoints.cs:142` |
| I1 | `src/Identity/Api/DevicesEndpoints.cs` (118) | `Register` | 6 | `DevicesEndpoints.cs:52` |
| I2 | `src/Identity/Api/KiosksEndpoints.cs` (118) | `Enroll` | 6 | `KiosksEndpoints.cs:52` |
| I3 | `src/Identity/Api/WebhookRotationEndpoints.cs` (94) | `Rotate` | 6 | `WebhookRotationEndpoints.cs:37` |

All are `private static async Task<IResult>`. **Not hits, so no attribute**: `CameraEndpoints.Retire`, the
Identity `Disable` and `List` handlers, and every helper (`ParseRegistration`, `CreateExecution`,
`ExactlyOneAttribute`, `ResolveOperator`, both `ResolveWriteFabAsync`/`ResolveReadFabsAsync` in
CameraCatalog, `RegisterScope`, `EnrollScope`, `RotationOutcome`, `IdentityFabResolution.*`, the `Map*`
methods). The "helpers out of scope unless trivially behaviour-preserving" question does not arise: there
is no helper hit left to take.

**The rest of each closure, which must be unchanged after (SC-2):**

- CameraCatalog (6 lines): S107 `CameraCatalog/Domain/Camera/Camera.cs:37`,
  `ServiceDefaults/Authentication/ClientCredentialsTokenProvider.cs:88`,
  `ServiceDefaults/Authorization/FabResolution.cs:49`, `ServiceDefaults/Idempotency/IdempotencyScope.cs:46`,
  `ServiceDefaults/WolverineDefaults.cs:40`; S138 `ServiceDefaults/WolverineDefaults.cs:40`.
- Identity (7 lines): S107 `Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:37`,
  `Identity/Domain/RegisteredClient/RegisteredClient.cs:39`, the same four ServiceDefaults S107, and the S138.

  The CameraCatalog closure also reports two migration S138s (`20260810164633_FabScopeCameras.cs:10`,
  `20260823194632_CaseInsensitiveCameraNames.cs:11`), 8 lines in a full build; the `grep -v Migrations` filter
  above leaves them out, and they are unchanged.

Those Domain/Application/ServiceDefaults hits are advisory per the amendment and stay. No S104 in either
closure today.

## 2. User story

### US1 (P1) — the S107 count for these two contexts' handlers measures nothing anybody can act on

As the maintainer reading ADR-0084's S107 count, I want the seven CameraCatalog and Identity route handlers
to carry the sanctioned suppression, with a justification that is true for each context, so the count stops
including HTTP contracts the amendment declared out of scope while every contract stays exactly as it is.

**Independent test**: the two Release builds report 0 S107 under `src/CameraCatalog/Api` and
`src/Identity/Api` and the §1 remainders unchanged with no S104; both route-table baselines, the four
endpoint characterisations and the seven source-scanning guards pass; no file under `tests/` is modified.

**Acceptance scenarios**

```gherkin
Scenario: the seven handlers are no longer counted
  Given each Api closure is built in Release with --no-incremental
  When the S107 warnings under src/CameraCatalog/Api and src/Identity/Api are listed
  Then there are none

Scenario: nothing else moved (conflict: a leaking suppression, or a file pushed over S104's 300)
  Given the same builds
  When every distinct warning in each closure is listed
  Then each list equals its §1 remainder exactly
  And no S104 appears on any of the five touched files

Scenario: the HTTP contract is unchanged (the attribute reaches no metadata)
  Given the CameraCatalog and Identity route tables rendered from the mapped endpoints
  When each is compared with its literal baseline
  Then CameraCatalogRouteTableCharacterisationTests and IdentityRouteTableCharacterisationTests pass unmodified
  And so do CameraRegister-, CameraPatch-, DeviceAndKiosk- and WebhookRotationEndpointCharacterisationTests

Scenario: the justification states something true for every context (bad request: a false text)
  Given a handler carrying the prescribed attribute
  Then its context's *RouteTableCharacterisationTests baseline prints that handler's full parameter list

Scenario: the source-scanning guards still read the handlers
  When ConcurrencyConflict-, EndpointScope-, EventMetadataFab-, IdempotentKeyReuse-, Precondition-,
       RouteValueRefusal- and StatusProducerDeclarationTests run
  Then every one passes unmodified

Scenario: the exemption reaches no further than the amendment (auth/scope of the exemption)
  Given a method in these files that is not passed to a Map call, or a handler that is not an S107 hit
  Then it carries no S107 suppression
```

## 3. Why this is behaviour-preserving, and where it could leak

- **Not emitted.** `SuppressMessageAttribute` is `[Conditional("CODE_ANALYSIS")]`. Both Release logs contain
  `CODE_ANALYSIS` zero times, and no `.props`, `.targets`, `.csproj` or workflow in the repo defines it.
  **Observed** on a throwaway probe (all seven attributes plus the five usings, reverted, worktree clean):
  the built `SmartSentinelEye.CameraCatalog.Api.dll` and `SmartSentinelEye.Identity.Api.dll` contain
  `SuppressMessageAttribute` 0 times and the justification text 0 times. Minimal APIs copy a handler's
  method attributes into endpoint metadata, which both renderers print as `other <Type>` (the shared
  `RouteTableRendering` and Identity's own `Render`, whose `IsContract` filter drops only binder and
  async/debugger markers); were the attribute emitted, each baseline would fail. That is why they are the
  covering tests.
- **The DLLs are not byte-identical, and that is not evidence either way.** Probe vs clean Release hashes
  differ for both assemblies. The likely cause is the debug-directory PDB identity, which follows the line
  numbers the attributes shift; **unverified**, and not used as evidence. The covering evidence is the
  rendered metadata plus the zero-occurrence scan above.
- **Source readers.** `RouteChainReader` matches a declaration on one line
  (`(?<!\w)(?:private|...)[^;{}()\n]*?\bName\s*\(`); an attribute on the preceding lines does not intervene,
  and partial spans are resolved within the project. 30 attributes of this exact form are already on
  `develop` (specs 397-399) and every guard scans every `src/*/Api` file, so the form is proven against all
  seven guards. `EventMetadataFabDeclarationTests` scans all of `src/` and is added to the filter for that
  reason.
- **Literal forms.** The justification is two plain `"..."` literals (no `@"`, `"""`, `\"` or `'"'`), which
  `ConcurrencyConflictDeclarationTests`' corpus ban requires.
- **Line-number pins.** No test names any of the five files outside a comment (searched `tests/`).
- **Security.** No executable line, signature, route, policy, scope, fab-resolution call, idempotency scope or
  audit line changes. A `security-reviewer` pass is **still expected** for the Identity half: the issue body
  names it for Identity and for "anything with auth or fab resolution" (which CameraCatalog's write handlers
  also are), ADR-0144's phase 6 runs it for security-sensitive contexts, and the lane has no mechanism for
  deciding a mandated review is unnecessary. Its job here is narrow and cheap: confirm the diff is
  attributes and usings only, no attribute lands on a helper, and the `authorize policy=` lines of both
  baselines are unchanged. Zero findings expected.

## 4. Success criteria

- **SC-1**: S107 under `src/CameraCatalog/Api` 4 -> 0 and under `src/Identity/Api` 3 -> 0. **Observed by
  probe** (the diff of before/probe lists removes exactly the seven §1 lines and adds nothing).
- **SC-2**: each closure's remaining distinct warnings equal §1's lines, and no S104 appears. Probe-measured
  code lines (non-blank, non-`//` lines, S104's basis; exact against the build for spec 400):

  | File | Before | After | Headroom to 300 |
  |---|---|---|---|
  | `CameraEndpoints.cs` | 282 | 293 | 7 |
  | `CameraEndpoints.Reads.cs` | 100 | 111 | 189 |
  | `DevicesEndpoints.cs` | 202 | 208 | 92 |
  | `KiosksEndpoints.cs` | 199 | 205 | 95 |
  | `WebhookRotationEndpoints.cs` | 156 | 162 | 138 |

  No file crosses 300, and the probe build reported no `warning S104`. **No relocation into a partial is
  needed.** `CameraEndpoints.cs` keeps only 7 lines of headroom: the next handler with more than four request
  values added to it will need the `36abc23a` move; reported, not acted on.
- **SC-3**: no file under `tests/` changes (`git diff 7f05b12b -- tests/` empty).
- **SC-4**: exactly 7 occurrences of the prescribed attribute across the two Api projects, one per §1 row
  (4 in CameraCatalog, 3 in Identity); `git grep -c "S107:Methods should not have too many parameters" -- src`
  totals 37 with the 30 on `develop` (more if #3039 merged meanwhile; count only these two projects for the
  gate).

## 5. Locked choices and the prescribed form

Byte-identical to specs 397-400:

```csharp
    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> Register(
```

- Directly above the declaration line, never on the same line. None of the seven has an XML doc comment
  or another attribute, so there is no ordering question.
- `using System.Diagnostics.CodeAnalysis;` in sorted position (`dotnet_sort_system_directives_first = true`):
  **line 1 of all five files** — before `Microsoft.AspNetCore.Mvc` in the two CameraCatalog files, before
  `System.Security.Claims` in Devices/Kiosks, before `System.Globalization` in WebhookRotation (D < G).
- Not `.editorconfig`, not `#pragma`, not a glob section, not a shared `const`, not
  `UnconditionalSuppressMessage` (emitted; it would move the baselines).
- Line endings follow each file's existing ones.

**Latency budget**: N/A — no runtime change, nothing on the event-to-overlay path.

## 6. Decisions

**None blocking.** The two that spec 400 had to raise were checked and do not recur:

- **Baseline per context (spec 400's D-1).** Both exist and pin every suppressed handler's full parameter
  list as a literal: `tests/Architecture.Tests/CameraCatalogRouteTableCharacterisationTests.cs` (captured on
  `39b0ef64`, spec 366; `handler Register(...)`, `Patch(...)`, `Get(...)`, `List(...)` with all
  parameters, `[FromServices]` ones included) and `tests/Architecture.Tests/IdentityRouteTableCharacterisationTests.cs`
  (captured on `491d5027`, spec 367, via `IdentityEndpointHost`; `handler Register(...)`, `Enroll(...)`,
  `Rotate(...)`, plus an eight-routes count fact). The fixed justification is therefore true in both
  contexts. No characterisation test needs writing first.
- **S104 (spec 400's D-2).** No file crosses 300 (§4 SC-2). No partial-file move.

One small call is the orchestrator's, with a recommendation:

- **Closing keyword.** Recommended `Closes #2975`: ADR-0084's Consequences name "the handler hits of ...
  #2975" as closed by this mechanism, the helper hit was cleared by #3002, and the Domain/Application hits
  in these closures were never in scope ("hits in Domain/Application/other are out of scope", issue body).
  The alternative, `Refs`, leaves an issue open with nothing left in it.

## 7. Assumptions

- **A-1**: Sonar's S104 count matches "non-blank, non-`//` lines" (exact for spec 400's 305; here
  corroborated by the probe build reporting no S104 at 293). The gate is the build's own report (SC-2).
- **A-2**: `"Major Code Smell"` is cosmetic; Roslyn matches on `S107` before the colon. Proven by the probe
  and by the 30 attributes on `develop`.
- **A-3**: no other PR touches these five files before this one merges (none open on 2026-10-18). If one
  does, rebase and re-run T020/T021.

## 8. Out of scope (what the lane must not take)

- Any handler signature, `*Services` record, `Map*` chain, route, OpenAPI metadata, summary, policy or scope.
- Any file under `tests/`; an assertion that would need editing is a **block**.
- `Retire`, the Identity `Disable`/`List` handlers, every helper, `RevocationEndpoints.cs`,
  `IdentityFabResolution.cs`, and every S107 outside the two Api projects (`Camera.cs`, `RegisteredClient.cs`,
  `RotateWebhookClientCommandHandler.cs`, ServiceDefaults): advisory per the amendment.
- Moving code between partials (not needed; SC-2), `.editorconfig`, `SonarLint.xml`,
  `Directory.Build.props`, csproj files, ADRs, the constitution, CLAUDE.md.
