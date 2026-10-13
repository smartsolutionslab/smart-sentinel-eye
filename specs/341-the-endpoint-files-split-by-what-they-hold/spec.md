# Spec 341: The endpoint files, split by what they hold

**Feature Branch**: `refactor/2896-api-endpoint-shape`, cut from `origin/develop` at `5ecc3007`

**Created**: 2026-10-13

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Each judgement call
is recorded in §6 with the evidence behind it.

**Input**: Issue [#2896](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2896),
*Refactor the Api layers' oversize files and complex endpoint methods: 5x S104, 3x S1541
(ADR-0084)*. The owner asked for it on 2026-10-13. The only comment on the issue is the lane's
pick-up note, so the body is the whole scope.

**Lane.** Autonomous (ADR-0144). **Phase 4a colour: characterisation, observed green.** The
change is behaviour-preserving. It is not also a bug fix (§6.4).

**Spec number.** 341. Checked on 2026-10-13 against `origin/develop` (highest is 340), every
remote branch, every local branch, open PRs #2837 and #2811, and the sibling worktrees
`sse-1545` (highest is 338) and `sse-2897` (highest is 340, with no uncommitted `specs/34x`).
`sse-2897` (issue #2897) was cut at the same moment and could also claim 341. **Check again
before opening the PR.**

**ADRs and constitution sections referenced:**

- **ADR-0084**, code metrics (amended 2026-09-17): S104 allows at most 300 lines per file and
  S1541 allows a cyclomatic complexity of at most 10. These limits are advisory warnings. This
  spec brings eight Api-layer hits under them and changes neither limit.
- **ADR-0144**, the autonomous lane. It covers characterisation, the ban on weakening a gate, and
  the rule that a refactor plus a bug fix is two issues.
- **ADR-0139** and constitution §Testing: refactors stay green.
- **ADR-0036**: the smallest change, no speculative generality.
- **ADR-0070**, Minimal APIs: mappings stay on `*Endpoints.cs` and handlers are method groups.
- **ADR-0122**, ADR-0118, ADR-0128 and ADR-0129: the kiosk-latency endpoint is the entry to the
  `SmartSentinelEye.Latency` measurement chain, and `wall_skew` and `label_delay` are routed to
  their own instruments.
- **ADR-0052**, ADR-0053 and ADR-0103: xUnit with Shouldly, sentence-style names, no
  Testcontainers.
- **ADR-0109**: `[P]` marks file-disjoint work.

**No new ADR is needed.** ADR-0084 already fixes the limits. This spec only meets them. It adds
no mechanism, abstraction or dependency. The two new helper classes follow existing repository
precedent (§5).

---

## 1. Premise, re-measured

The build was run on `5ecc3007` in this worktree with no Aspire stack running:
`dotnet build SmartSentinelEye.slnx -c Release --no-incremental -p:TreatWarningsAsErrors=false`.
It reported 289 warnings and 0 errors. The issue's grep, filtered to `Api[\\/]`, gives:

| # | Location | Rule | Measure | Unit |
|---|---|---|---|---|
| 1 | `src/Automation/Api/RulesEndpoints.cs(1,1)` | S104 | 353 lines | file |
| 2 | `src/Automation/Api/RulesEndpoints.cs(432,31)` | S1541 | 11 | `BuildAction` |
| 3 | `src/CameraCatalog/Api/CameraEndpoints.cs(1,1)` | S104 | 372 lines | file |
| 4 | `src/LayoutComposition/Api/LayoutEndpoints.Commands.cs(1,1)` | S104 | 375 lines | file |
| 5 | `src/OverlayDesigner/Api/OverlayEndpoints.Commands.cs(1,1)` | S104 | 320 lines | file |
| 6 | `src/StreamDistribution/Api/Program.cs(1,1)` | S1541 | 12 | top-level file |
| 7 | `src/StreamDistribution/Api/StreamEndpoints.cs(149,28)` | S1541 | 14 | `RecordKioskLatency` |
| 8 | `src/SystemVariables/Api/SystemVariableEndpoints.cs(1,1)` | S104 | 424 lines | file |

**All eight still exist, and none is stale.** The counts match the issue exactly. S104 counts
*lines of code*, which excludes blank and comment-only lines, not physical lines. For example,
`RulesEndpoints.cs` has 455 physical lines and 353 lines of code.

## 2. User stories

### US1 (P1): Api endpoint files stay under ADR-0084's limits, and nothing a caller sees moves

*As the maintainer who asked for ADR-0084's shape limits to hold*, I want the eight Api-layer
hits gone, without changing any route, verb, status, scope, header, OpenAPI declaration, DTO or
recorded measurement. I also want every architecture guard that reads these files to keep
passing **unmodified**.

**Why P1.** The owner asked for these to be taken before the S138/S107 items in #2887–#2895.
This is the smallest set.

**Independent test.** Run the issue's warning grep before and after. *After* is empty. The
characterisation suite (§4) and `Architecture.Tests` pass before and after with no assertion
edited.

#### Acceptance scenarios

1. **Happy path, the gate clears.** *Given* `origin/develop` at `5ecc3007`, *when* the eight
   tasks land, *then* the issue's grep prints nothing for `Api[\\/]`.
2. **The behaviour holds (characterisation).** *Given* the characterisation tests observed green
   before the change, *when* the change lands, *then* the same tests pass and their files are
   byte-identical to the before state.
3. **Conflict: a guard would object.** *Given* a split that adds a fourth partial file to
   `LayoutEndpoints` or `OverlayEndpoints`, or moves an `IdempotentRequest`/`ConcurrencyHeaders`
   call out of a mapped handler body, *when* `Architecture.Tests` runs, *then* it fails. The
   split is then wrong and is redone. The guard is **not** edited.
4. **Bad request, refusals unchanged.** *Given* a kiosk principal posting
   `{"measurement":"bogus",…}` to `POST /streams/kiosk-latency`, *when* it runs after the
   change, *then* the answer is the same `400` validation problem, with the same key and message
   text, and nothing is recorded. The same applies to an empty camera, a non-finite elapsed, and
   an unknown or under-specified `ActionType` on `POST /rules`. The `400` `detail` there is the
   `ArgumentException` message from `BuildAction`, which is caller-visible.
5. **Auth, the kiosk gate unchanged.** *Given* a non-kiosk principal (`azp` ≠ `kiosk-web`),
   *when* it posts any report, *then* the answer is `202` and nothing is recorded, exactly as
   before (#1893). Route-level authorization (`sse.streams.read`, `sse.rules.*`, and so on) is
   untouched, because no mapping chain changes.

## 3. Requirements

- **FR-001.** S104 is cleared by **splitting by responsibility**: either a whole handler moves to
  a partial file of the same class, or a non-handler helper moves to a named helper class. The
  split is never made by line count alone.
- **FR-002.** S1541 is cleared by **extracting named private methods**. Branches are never moved
  into a lookup table or a new abstraction.
- **FR-003.** No `Map*` call or fluent chain moves or changes. No route, verb, status, scope,
  header, OpenAPI, DTO or `Produces*` change.
- **FR-004.** The guard constraints in §5 hold. No guard test is edited.
- **FR-005.** No suppression, threshold edit, `#pragma`, `.editorconfig` or `SonarLint.xml`
  change. S107/S138 hits are not touched.
- **FR-006.** What `RecordKioskLatency` records stays byte-for-byte unchanged: the instrument
  name, the tags, the values and the refusals. A characterisation test pins this (§4), because
  no existing test does.
- **FR-007.** Each commit builds on its own. Rebase-merge lands them individually (ADR-0087).
- **FR-008.** House style applies to the code that moves or is written: `var`, no
  underscore-prefixed fields, one parameter per line when wrapping, and `Ensure.That` guards.

**Latency-budget impact: N/A.** `RecordKioskLatency` is measurement plumbing at the entry of
`POST /streams/kiosk-latency` → `LatencyBudget.Record` → `sse.latency.segment.duration`. It is
not a leg of constitution §IV. FR-006 keeps what it records unchanged.

## 4. Characterisation, the safety net

| Touched unit | Existing coverage | Gap |
|---|---|---|
| `RecordKioskLatency` | `e2e/kiosk-latency-contract.spec.ts` (statuses only, full stack). `KioskMeasurementContractTests` (source text). `WhepAuthorizeRateLimitTests` (the 401 only). `ServiceDefaults.Tests/KioskLatencyTests` (tests `LatencyBudget` directly and never goes through the endpoint). | **Nothing pins what the endpoint records.** New: `KioskLatencyRecordingCharacterisationTests`. |
| `RulesEndpoints.BuildAction` | `RuleLifecycleIntegrationTests` and `RuleSwitchesAWallIntegrationTests` (success shapes). | **No test pins the four refusal messages**, which reach the caller as the `400` `detail`. New: `RuleActionBuildCharacterisationTests`. |
| Handlers moved whole (Rules, Camera, SystemVariables reads) | Integration tests per context (plan §4) plus the `Architecture.Tests` guards. | None. The bodies move byte-identical. |
| Helpers moved to a helper class (Layout fab resolution and grid/tile parsing, Overlay element parsing) | `LayoutFabScopingIntegrationTests`, `WallEndpointsTests`, `AbsentLayoutBodyMembersAreRefusedIntegrationTests`, `TileSpanIntegrationTests`, `OverlayElementValidationIntegrationTests`, `OverlayGeometryValidationIntegrationTests`, `OverlayLifecycleIntegrationTests`, and the others in plan §4. | None. |
| `StreamDistribution/Api/Program.cs` classification helpers | `WhepAuthorizeRateLimitTests`, `MediaMtxControlApiAuthIntegrationTests`, `WhepAuthIntegrationTests`, `WhepAuthorizePartitionKeyTests`, `WhepAuthorizeCeilingTests`. | None. |

## 5. Guard constraints (decided by reading the guards, not by assumption)

These are the shapes the guards tolerate. Each one was derived from the guard source on
`5ecc3007`.

- **G-a. New partial files must not end in `Endpoints.cs`.** `EndpointFileCount = 16` is pinned
  in `EndpointScopeDeclarationTests`, `PreconditionDeclarationTests`,
  `StatusProducerDeclarationTests` and `RouteValueRefusalDeclarationTests`. Each matches with
  `EndsWith("Endpoints.cs")`. Use the existing `.Reads.cs` / `.Commands.cs` naming
  (`EventsEndpoints.Reads.cs` is the precedent).
- **G-b. `LayoutEndpoints` and `OverlayEndpoints` stay at exactly three partial files.**
  `PreconditionDeclarationTests.A_partial_endpoint_class_is_indexed_across_every_file_that_declares_it`
  pins `("LayoutEndpoints", 3)`, `("OverlayEndpoints", 3)` and `("EventsEndpoints", 3)`. Their
  oversize `.Commands.cs` files therefore shed **non-handler helpers into a separate,
  non-partial `internal static class`**. Precedent: `EventIngestionFabResolution`,
  `IdentityFabResolution`.
- **G-c. The five LayoutComposition If-Match handlers stay in `LayoutEndpoints.Commands.cs`.**
  `The_layout_write_mappings_bind_to_the_handlers_in_their_partial_file` asserts this by file
  name.
- **G-d. Nothing leaves a mapped handler body that a guard reads there.** Two guards cross-check
  a mapping walk against a flat sweep. `IdempotentRequest.ExecuteAsync`/`ExecuteCreateAsync`
  (`IdempotentKeyReuseDeclarationTests`) and `ConcurrencyHeaders.TryRead*`
  (`PreconditionDeclarationTests`) must stay in the mapped method's own body. In
  `RouteValueRefusalDeclarationTests`, the 400 antecedent is `StatusCodes.Status400BadRequest`,
  `Results.BadRequest(` or `.ValidationProblem(` in the handler body. That guard says it
  "catches no hoisted producer", so moving one out would *pass* while quietly shrinking the
  guard's population. That is weakening a gate, and it is forbidden here. Handlers therefore move
  **whole**, and extractions take only code without those tokens.
- **G-e. Handlers resolve by method-group name within the declaring class, across its partial
  files** (`RouteChainReader`). A handler moved to `RulesEndpoints.Reads.cs` keeps its name and
  stays a member of `RulesEndpoints`. That requires `static class` → `static partial class`.
- **G-f. `StreamEndpoints.cs` stays in place and keeps, as text**, the switch arms
  `"overlay_draw" => LatencySegment.…` (and the other two), the comparisons
  `report.Measurement == "wall_skew"` / `== "label_delay"`, the refusal literal
  `"must be '…', " + "'wall_skew' or 'label_delay'"`, and the calls `WallSkew.Record` and
  `LabelDelay.Record` (`KioskMeasurementContractTests`). A helper method inside the same file is
  fine. The variable must stay named `report` where the comparisons are made.
- **G-g. `StreamDistribution/Api/Program.cs` keeps** exactly one
  `"WhepAuthorizeRateLimiting:PermitLimit") ?? 2000` and one
  `"WhepAuthorizeRateLimiting:Window") ?? TimeSpan.FromMinutes(1)`
  (`WhepAuthorizeCeilingTests`). It also keeps `AddPolicy("whep-authorize"` →
  `RateLimitPartition.GetFixedWindowLimiter(` with a partition-key argument that spells
  `context.Connection.RemoteIpAddress` inline and ends at `_ => new FixedWindowRateLimiterOptions`
  (`WhepAuthorizePartitionKeyTests`). It must keep calling `MapDefaultEndpoints`
  (`ServedHealthHostCompletenessTests`).

## 6. Decisions and assumptions

1. **Characterisation tests live in `tests/Architecture.Tests`.** That project already
   references every Api project and already maps endpoints in-process
   (`SystemVariableReadScopeTests`, `SystemVariableRouteShadowingTests`). The
   `endpoint.RequestDelegate` invocation pattern exists in
   `ServiceDefaults.Tests/DefaultEndpointsTests`. A new test project would add CI wiring for two
   classes, and `Architecture.Tests` has no shard filter.
2. **The NaN/Infinity refusal cannot be reached over HTTP with default JSON options.**
   `JsonSerializerDefaults.Web` does not allow named floating-point literals, so `"NaN"` fails
   in model binding before the handler runs. The characterisation test reaches the branch by
   configuring the *test host's* `JsonOptions` with `AllowNamedFloatingPointLiterals`. If that
   proves impossible, it falls back to invoking the method group by reflection. Either way the
   test says why. The branch is defence-in-depth, not a defect, so it is left as it is.
3. **`BuildAction` keeps its name and signature**, so the reflection-based characterisation
   test can bind to it before and after. The three arms become three named private methods.
4. **Not a bug fix.** Nothing found during the investigation changes behaviour. One doc comment
   is stale: `LayoutEndpoints.ResolveWriteFabAsync` says "Only `POST /layouts` uses this", but
   `WallEndpoints.Commands.cs:53` uses it too. It moves verbatim, and the PR reports it as an
   out-of-scope finding rather than fixing it here (ADR-0036).
5. **S104 margins are estimated with the same line-of-code count Sonar uses.** That count
   reproduced all five baseline figures exactly. Projected results: Rules about 236, Camera about
   278, SystemVariables about 266, Overlay about 251, and Layout about 294. **Layout is the thin
   one.** It needs both of its moves (T009 and T010). The engineer re-measures after each task.

## 7. Out of scope

- The S107/S138 hits in `Api` (the `[AsParameters]` question) and every Domain, Application or
  Infrastructure hit (#2887–#2895).
- Any other `Program.cs`, and `src/AppHost/AppHost.cs`'s S104, which is not in an Api layer.
- The stale comment in §6.4.
