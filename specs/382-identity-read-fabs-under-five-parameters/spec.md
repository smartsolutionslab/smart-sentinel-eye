# Spec 382: Identity's read-fab resolution under five parameters

**Feature Branch**: `refactor/2975-camera-identity-api-s107` (cut from `origin/develop` at `38eb836f`)

**Created**: 2026-10-16

**Status**: Implemented (T010 done; CI pending). No `[NEEDS CLARIFICATION]` remains. The issue's scope is **shrunk**
from 8 hits to 1 (§5, D-1): the other 7 are route handlers in two contexts whose route-table
characterisation baselines pin every handler's parameter list, so clearing them would edit a
characterisation assertion, which is a block (ADR-0144). They stay open for the owner decision
already recorded on #2971 (spec 380 §6, options a-c).

**Input**: Issue [#2975](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2975), as
narrowed by its two comments after the #2971 review. Template: spec 380 (#2971), which cleared the
reachable helpers of EventIngestion and handed the handler hits back. Refs spec 183 (Identity's
fab-scoped lists), spec 367 (`IdentityEndpointHost` and the Identity route-table baseline),
ADR-0114 (the shared fab decision table).

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation,
observed green**. Engineer: **backend-engineer**. **Security-sensitive**:
`IdentityFabResolution.ResolveReadFabsAsync` decides which fabs a caller of `GET /devices/`,
`GET /kiosks/` and `GET /webhook-integrations/` may read; Phase 6 runs `/security-review`
(security-reviewer) as well as `/code-review`.

**Spec number.** 382. Checked 2026-10-16 after `git fetch`: highest spec on `origin/develop`
(`38eb836f`) is 380; 381 is claimed by the unmerged `fix/2993-url-redaction-userinfo-as-host`
(worktree `D:\Github\sse-2993`); no other local, remote or open-PR head (including
`docs/adr-0084-s107-log-methods` and `claude/beautiful-mendel-jz9d5x`) carries 381+. Re-check before
the PR is opened.

**ADRs.** **ADR-0084** (S107, at most 4 parameters; advisory `warning`, carved out of
`TreatWarningsAsErrors`), ADR-0144 (lane; characterisation colour; no gate weakened), ADR-0114 (the
fab decision table this helper binds), ADR-0070 (Minimal APIs), ADR-0036 (smallest change),
ADR-0087 (each commit builds on its own), ADR-0091 (no shortcuts in names), ADR-0105
(`Ensure.That`, not added here: not a trust boundary).

**No new ADR.** ADR-0084 states the limit; this spec changes the shape of one internal helper. The
seven blocked handlers wait on the decision already posed on #2971; this lane does not take it.

## 1. Problem: the re-measured hit list

Release, `--no-incremental`, measured 2026-10-16 on `38eb836f`
(`dotnet build SmartSentinelEye.slnx -c Release --no-incremental -flp:logfile=…metrics-2975.log;verbosity=normal;NoSummary`,
unique `file(line,col): warning S107`): **110 S107 hits solution-wide, 8 under
`src/(CameraCatalog|Identity)/Api`**, the same 8 files and counts the issue filed. No other Sonar
rule fires in either Api project.

"Counted" is the parameters S107 counts: everything except `[FromServices]` (spec 380 §1, proven
by counterfactual). A helper's injected collaborators are plain parameters and **are** counted.

| # | Hit | Method | Kind | Counted parameters | n |
|---|---|---|---|---|---|
| H1 | `CameraCatalog/Api/CameraEndpoints.Reads.cs(14,43)` | `Get` | route handler | `camera`, `user`, `response`, `cancellationToken`, `fabId` (+2 `[FromServices]`) | 5 |
| H2 | `CameraCatalog/Api/CameraEndpoints.Reads.cs(51,44)` | `List` | route handler | `user`, `cancellationToken`, `fabId`, `sort`, `order`, `offset`, `limit`, `includeRetired`, `name` (+2) | 9 |
| H3 | `CameraCatalog/Api/CameraEndpoints.cs(155,48)` | `Register` | route handler | `request`, `services` (`[AsParameters]`), `httpContext`, `cancellationToken`, `fabId` | 5 |
| H4 | `CameraCatalog/Api/CameraEndpoints.cs(259,45)` | `Patch` | route handler | `camera`, `request`, `httpContext`, `cancellationToken`, `fabId` (+3) | 5 |
| H5 | `Identity/Api/DevicesEndpoints.cs(115,48)` | `Register` | route handler | `body`, `fabId`, `services` (`[AsParameters]`), `http`, `user`, `cancellationToken` | 6 |
| H6 | `Identity/Api/KiosksEndpoints.cs(115,46)` | `Enroll` | route handler | `body`, `fabId`, `services` (`[AsParameters]`), `http`, `user`, `cancellationToken` | 6 |
| H7 | `Identity/Api/WebhookRotationEndpoints.cs(91,46)` | `Rotate` | route handler | `name`, `body`, `request`, `services` (`[AsParameters]`), `user`, `cancellationToken` | 6 |
| H8 | `Identity/Api/IdentityFabResolution.cs(36,97)` | `ResolveReadFabsAsync` | internal helper (public member of an `internal static` class) | `user`, `fabId`, `fabGuard`, `unusableFabErrorCode`, `cancellationToken` | 5 |

Not hits, recorded so nobody goes looking: CameraCatalog's own private `ResolveReadFabsAsync`,
`ResolveWriteFabAsync`, `CreateExecution` and `Retire` sit at exactly 4; Identity's three `List`
handlers count 3 (`user`, `cancellationToken`, `fabId`).

## 2. User story

### US1 (P1): Identity's read-fab helper takes four parameters and every list answers as before

**Why P1.** It is the only hit of #2975 reachable without editing a characterisation assertion
(§5, D-1). It is independent of the #2971 decision and forecloses none of its options.

**Independent test.** The plan §4 commands, including the characterisation tests T000a adds
(observed green on unchanged code at the characterisation commit `C0`), pass on `C0` and pass
again, **unmodified**, after the change (`git diff C0 -- tests/` empty); the §1 measurement no
longer lists H8, lists H1-H7 exactly as before (same methods; line numbers may shift), and no other
new warning appears under `src/Identity/Api`.

#### Acceptance scenarios

Each applies to all three list routes, `GET /devices/`, `GET /kiosks/` and
`GET /webhook-integrations/`, with that route's code (`DEVICE_INVALID_INPUT`,
`KIOSK_INVALID_INPUT`, `WEBHOOK_INVALID_INPUT`).

```gherkin
Scenario: happy path - an omitted fabId spans exactly the caller's fabs
  Given a caller holding /fabs/munich and /fabs/dresden, and clients of that route's kind in
        munich, dresden and bremen
  When the caller lists without a fabId
  Then the answer is 200 with the munich and dresden clients and not the bremen one
  And the fab guard is never asked

Scenario: happy path - a named, held fabId narrows to it
  Given the same caller and clients
  When the caller lists with fabId=munich
  Then the guard is asked once, for that caller and "munich"
  And the answer is 200 with the munich clients only

Scenario: conflict - an unusable group is skipped, not fatal
  Given a caller holding /fabs/Not A Fab and /fabs/munich
  When the caller lists without a fabId
  Then the answer is 200 with the munich clients only, and no error mentions the unusable group

Scenario: bad request - no usable fab is refused with the route's own code
  Given a caller whose only fab group is not a usable fab name, or who names an unusable fab
        that the guard admits
  When the caller lists
  Then the answer is 400 with the exact problem body
       {title: "<ROUTE>_INVALID_INPUT", status: 400, detail: "None of your fab groups is a usable fab name."}
       as observed on unchanged code
  And the registered-client source is never read

Scenario: auth - a fab the caller does not hold is refused before anything is read
  Given the fab guard refuses "bremen"
  When the caller lists with fabId=bremen
  Then FabAuthorizationException for "bremen" escapes the delegate (mapped to 403 by the running
       service's exception handler; spec 367's in-process convention)
  And the registered-client source is never read

Scenario: auth - a caller with no fab membership is refused
  Given a caller holding no /fabs/ group
  When the caller lists without a fabId
  Then FabAuthorizationException (no fab membership) escapes, and the source is never read
```

## 3. Requirements

- **FR-001** H8 `IdentityFabResolution.ResolveReadFabsAsync` takes at most 4 counted parameters.
  Shape: plan §2.1.
- **FR-002** Its outcomes are unchanged: the call into `FabResolution.ResolveForReadAsync` with the
  same `user`, `fabId`, `fabGuard` and token; per-entry `FabIdentifier.From` with an
  `ArgumentException` skipped; the 400 problem's title (the caller's code), detail and status; the
  success list in the same order.
- **FR-003** Each of the three callers passes the **same** error code it passes today
  (`KIOSK_INVALID_INPUT` in `KiosksEndpoints.List`, `DEVICE_INVALID_INPUT` in
  `DevicesEndpoints.List`, `WEBHOOK_INVALID_INPUT` in `WebhookRotationEndpoints.List`), as a string
  literal in the handler body, where it is today.
- **FR-004** No route handler's parameter list changes. In particular H1-H7 and the three `List`
  handlers keep their exact signatures (`CameraCatalogRouteTableCharacterisationTests` and
  `IdentityRouteTableCharacterisationTests` pin them).
- **FR-005** Route templates, verbs, names, summaries, tags, policies, status codes, response
  bodies, the OpenAPI shape and log text are unchanged. The new record is `internal`, not a handler
  parameter, so it is invisible to the route table and to OpenAPI.
- **FR-006** No file under `tests/` changes after the characterisation commit `C0`. No suppression,
  threshold, `.editorconfig`, `SonarLint.xml`, `Directory.Build.props` or csproj change. No new or
  renamed file under `src/*/Api` (the guards pin `EndpointFileCount = 16` on `*Endpoints.cs`; the
  record lives in `IdentityFabResolution.cs`).
- **FR-007** Each commit builds on its own and leaves the §4 suite green (ADR-0087).

## 4. Characterisation, the safety net

**The helper's outcomes are not pinned in-process today, so tests are added first (D-3).**
`IdentityEndpointHost` maps all three list routes but registers no list query handler, and no
in-process test drives a `GET` list. The only coverage is Aspire:
`tests/Integration.Tests/Identity/CrossFabListIntegrationTests` (shard 2) pins span-without-fabId
for all three routes, multi-fab spanning for all three, and the 403 and narrowing for kiosks only.
**Not covered anywhere:** the per-entry skip of an unusable group, the all-unusable 400 and its
per-route title (the one argument that differs between the three callers), the named-but-unusable
path, the no-membership refusal, and narrowing/403 on devices and webhook integrations.

| Changed | Covered by (in-process, `tests/Architecture.Tests`) | Covered by (Aspire, CI shard authoritative) |
|---|---|---|
| `ResolveReadFabsAsync` + its call in `DevicesEndpoints.List` and `KiosksEndpoints.List` | **to add (T000a):** `DeviceAndKioskEndpointCharacterisationTests`, the §2 scenarios for `/devices/` and `/kiosks/` | `CrossFabListIntegrationTests` |
| its call in `WebhookRotationEndpoints.List` | **to add (T000a):** `WebhookRotationEndpointCharacterisationTests`, the §2 scenarios for `/webhook-integrations/` | `CrossFabListIntegrationTests` |
| every handler signature (must not move) | `IdentityRouteTableCharacterisationTests`, `CameraCatalogRouteTableCharacterisationTests` | - |
| the rest of the three endpoint files (unchanged; regression net) | the existing 19 + 15 tests of the two classes above | `CrossFabRegistrationConflictIntegrationTests`, `CrossFabWebhookRotationIntegrationTests`, `IdempotentRegistrationIntegrationTests` |

Commands are in plan §4.

## 5. Guard constraints and decisions

**D-1. Seven hits are out of reach; the scope shrinks to H8.**

1. *Both contexts have a route-table baseline.* `CameraCatalogRouteTableCharacterisationTests`
   (lines 19, 51, 70, 84) and `IdentityRouteTableCharacterisationTests` (lines 32 and the three
   `List` lines 64, 109, 142, plus `Enroll` and `Rotate`) render each handler as
   `handler Name(param: Type [Attributes], …)` via `RouteTableRendering`. Every counted parameter of
   H1-H7 is a request-bound value (route, `[FromQuery]`, `[FromBody]`, `HttpContext`/`HttpRequest`/
   `HttpResponse`, `ClaimsPrincipal`, `CancellationToken`) or an existing `[AsParameters]` bundle.
   Moving any of them changes that line, and the baseline would have to be edited: a **block**
   (ADR-0144). So the "neutral to OpenAPI" route the issue comment allows for contexts *without* a
   baseline does not arise here; it is not attempted, and no OpenAPI diff is owed.
2. *The issue's own pattern would not help anyway.* Bundling `[FromServices]` parameters removes
   nothing S107 counts (spec 380 §1); H3, H5, H6, H7 are already bundled and still hit.
3. *H8 is reachable without any test edit.* It is not a route handler, the route table does not see
   it, and no test names it (`grep -rn ResolveReadFabsAsync tests/` finds only prose in two
   EventIngestion/SystemVariables integration comments about other contexts' copies).

**D-2. The guards, checked one by one (all must pass unmodified; any red is a block).**

| Guard (`tests/Architecture.Tests`) | Reads | Reacts to this spec? |
|---|---|---|
| `IdentityRouteTableCharacterisationTests` | each handler's parameter list, literal baseline, 8 routes | **Would react to any handler-signature change** (D-1.1). Not to this spec (FR-004). The T000a host change adds DI registrations only, which the rendering does not read. |
| `CameraCatalogRouteTableCharacterisationTests` | same, CameraCatalog | No: nothing in CameraCatalog changes. |
| `PreconditionDeclarationTests` | `*Endpoints.cs` (count 16), partial-class indexing, `If-Match` helper calls in handler bodies | No: no file added/renamed; the `List` bodies have no `If-Match` call. |
| `EndpointScopeDeclarationTests` | `*Endpoints.cs` count, mapping count, scope on chains | No: chains untouched. |
| `IdempotentKeyReuseDeclarationTests` | 422 declaration ⇔ `IdempotentRequest.Execute*` in body | No: `List` has neither. |
| `RouteValueRefusalDeclarationTests` | 400 producers (`Status400BadRequest`, `.BadRequest(`, `.ValidationProblem(`) in handler bodies ⇔ declared 400 | No: the 400 producer stays inside the helper; the call-site change adds `new FabReadRequest(…)`, which matches none of the three producer shapes. |
| `StatusProducerDeclarationTests` | status producers in handler bodies ⇔ declared statuses | No: no producer moves. |
| `ConcurrencyConflictDeclarationTests` | 409 producers / mutating mappings | No: `GET` routes. |
| `HandlerDeconstructionTests` | `public … Handle`/`HandleAsync` signatures | No: not a `Handle` method. |
| `BoundaryTests` | assembly references | No: `ClaimsPrincipal` is BCL; no new reference. |

**D-3. Characterisation is written first, by the test-writer, and observed green on unchanged
code.** The helper decides tenancy for three reads and the in-process suite does not exercise it
(§4). A refactor with no covering test is a rewrite (CLAUDE.md), so T000a adds the §2 scenarios for
all three routes to the two existing classes (no new class, so no shard-filter entry), extending
`IdentityEndpointHost` with the three list query handlers over an async-capable in-memory
`IRegisteredClientQuerySource`. That host change touches no assertion and must leave the existing
34 tests and the route-table baseline green. The tests are committed as `C0` before any production
change, their green output quoted in the PR.

**D-4. Bundle the caller's request, not the collaborator or the code.** Of the three ways to drop
one parameter, two were rejected: folding the error code into the guard pairs unrelated things; and
returning a code-less failure for the caller to translate would move a `Status400BadRequest`
producer into three handler bodies (a `RouteValueRefusalDeclarationTests`/`StatusProducerDeclarationTests`
reaction risk and a behaviour-shaped change). Pairing `user` and `fabId` names what ADR-0114's
table judges, "this caller asks to read this fab"; the grouping is what keeps the method within the
four-parameter limit. It does not make a swap a compile error: the pair that could be transposed is
`fabId` and the error-code string, which stay plain strings.

## 6. Out of scope, and the decision handed back

- **H1-H7 (seven route handlers): blocked, owner decision needed**, exactly as spec 380 §6 posed it
  for EventIngestion: (a) an ADR-0084 scope note that a minimal-API handler's request-bound
  parameters are structural; (b) an approved re-capture of the two route-table baselines with
  request-value `[AsParameters]` records, proven OpenAPI-neutral by a generated-document diff (a
  contract-surface change, not a refactor); (c) leave them advisory. The PR uses `Refs #2975`, not
  a closing keyword.
- The other contexts' private copies of `ResolveReadFabsAsync` (CameraCatalog, Automation,
  SystemVariables, StreamDistribution, LayoutComposition, EventIngestion): none is a hit at 4, and
  unifying them is not asked for.
- Every other ADR-0084 metric in these files; hits outside `src/(CameraCatalog|Identity)/Api`.
