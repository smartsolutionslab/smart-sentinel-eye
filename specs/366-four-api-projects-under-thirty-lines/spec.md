# Spec 366: Four Api projects' endpoints under thirty lines

**Feature Branch**: `refactor/2931-misc-api-s138` (cut from `origin/develop` at `39b0ef64`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Five judgement calls
are marked explicitly (§6).

**Input**: Issue [#2931](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2931),
*ADR-0084 S138: split the long methods in OverlayDesigner, SystemVariables, StreamDistribution and
CameraCatalog Api*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green"). Engineer: **backend-engineer**.

**Spec number.** 366. Checked 2026-10-15 after `git fetch`: `origin/develop` highest 364; no remote
head carries a `specs/365*`-or-later directory; 365 is claimed by #2930 in the unpushed worktree
`D:\Github\sse-2930` (`specs/365-the-event-ingestion-endpoints-under-thirty-lines`). Re-check after
any parked-PR merge before this PR opens.

**Prior art reused.** Spec 363 (#2929) in full: the in-process host, the route-table baseline and
its `IsContract` filter (async-state-machine and Debug-only `DebuggerStepThroughAttribute`
excluded, so the baseline passes in Debug **and** Release), `BoundaryParse.TryParse` with tuple
parsers, `CreateExecution`, and `void Map<Name>(RouteGroupBuilder group)` helpers. Spec 341's
`KioskLatencyRecordingCharacterisationTests` already characterises `RecordKioskLatency` in full.

**ADRs.** ADR-0084 (S138 <= 30 lines, S107 <= 4 parameters, advisory), ADR-0144 (lane; phase 4a
colours; no gate weakened), ADR-0070 (Minimal APIs), ADR-0036 (smallest change), ADR-0105
(`Ensure.That`), ADR-0113 (`If-Match` order), ADR-0142 (idempotency scope = route, caller, fab,
fingerprint), ADR-0114 (fab resolution), ADR-0089 (`ApiError` → problem), ADR-0122 (kiosk latency
reports). No new ADR: shape only.

---

## 1. What was measured (premise verified, not assumed)

```sh
for p in OverlayDesigner SystemVariables StreamDistribution CameraCatalog; do
  dotnet build src/$p/Api -c Release --no-incremental -nologo -v q 2>&1 | grep "$p.Api" \
    | grep -oE "[A-Za-z.]+\([0-9]+,[0-9]+\): warning S(138|107)[^\[]*" | sort -u
done
```

Result on `39b0ef64`: **exactly the issue's 10 S138 hits, same methods, same line counts.** Nothing
is stale.

| # | File:line | Method | Lines |
|---|---|---|---|
| 1 | `OverlayDesigner/Api/OverlayEndpoints.Commands.cs:16` | `CreateDraft` | 39 |
| 2 | `OverlayDesigner/Api/OverlayEndpoints.Commands.cs:195` | `EditDraft` | 36 |
| 3 | `OverlayDesigner/Api/OverlayEndpoints.cs:19` | `MapOverlayEndpoints` | 96 |
| 4 | `SystemVariables/Api/SystemVariableEndpoints.cs:144` | `Define` | 58 |
| 5 | `SystemVariables/Api/SystemVariableEndpoints.cs:36` | `MapSystemVariableEndpoints` | 88 |
| 6 | `StreamDistribution/Api/StreamEndpoints.cs:149` | `RecordKioskLatency` | 35 |
| 7 | `StreamDistribution/Api/StreamEndpoints.cs:22` | `MapStreamEndpoints` | 61 |
| 8 | `CameraCatalog/Api/CameraEndpoints.cs:133` | `Register` | 45 |
| 9 | `CameraCatalog/Api/CameraEndpoints.cs:235` | `Patch` | 66 |
| 10 | `CameraCatalog/Api/CameraEndpoints.cs:23` | `MapCameraCatalogEndpoints` | 65 |

The same build prints **15 S107 hits** (OverlayDesigner 6: `OverlayElementParsing.cs:72`, Commands
`CreateDraft`, `Publish`, `Archive`, `EditDraft`, `Revert`; SystemVariables 5: Reads `:15`, `:56`,
`Define`, `SetValue`, `Archive`; StreamDistribution 0; CameraCatalog 4: Reads `Get`, `List`,
`Register`, `Patch`). Out of scope (issue) and **must not grow**.

## 2. User story

### US1 (P1): the four Api projects' endpoint methods fit ADR-0084's 30-line limit with no observable change

An operator, a kiosk, MediaMTX, the gateway and the generated OpenAPI client see exactly the same
API before and after: same routes, verbs, names, summaries, tags, authorization policies,
rate-limit policy, declared statuses, the same status and problem body for every request, in the
same refusal order, and the same latency instruments fed by kiosk reports.

**Why one slice, one PR.** 10 methods across 4 contexts, 7 production files. At the split threshold
(~15 methods / 4 contexts of risk), not over it. It ships as **one PR in four commit groups, one per
context, lowest risk first** (StreamDistribution, SystemVariables, OverlayDesigner, CameraCatalog);
each group builds and passes on its own, so the PR can be cut at a group boundary if review demands.

**Independent test.** Characterisation tests (§4) observed green on `39b0ef64` before any
production edit, green **byte-unmodified** after each refactor commit; the §1 build prints zero S138
hits in the four projects and the same 15 S107 hits (by file + method).

#### Acceptance scenarios

Route table (the "conflict" case for a refactor is that the shape moved):

```gherkin
Scenario Outline: The route table of each context is identical after the refactor
  Given <map> is called on an in-process WebApplication on develop 39b0ef64
  And every RouteEndpoint is rendered canonically and captured as a literal baseline
  When the same rendering is taken after the refactor, in Debug and in Release
  Then it equals the baseline line for line
    # pattern, verbs, name, summary, tags, authorize (policy/roles/schemes), allow-anonymous,
    # rate-limit policy, produces in order, accepts, handler method name + parameter list
  Examples:
    | map                         |
    | MapOverlayEndpoints         |
    | MapSystemVariableEndpoints  |
    | MapStreamEndpoints          |
    | MapCameraCatalogEndpoints   |
```

Happy path:

```gherkin
Scenario: An unkeyed valid create reaches the handler and answers 201 with its location
  Given an operator "op-1" holding exactly fab "dresden"
  When they POST /cameras with a valid name and rtsp address and no Idempotency-Key
  Then the response is 201, Location "/cameras/{new id}", body the new id
  And the repository was asked to add a camera in "dresden" with that name and address
  # likewise POST /overlays (no fab) and POST /system-variables (Location by variable name)

Scenario: A valid camera rename reaches the rename handler with the route's identity and the caller's fab
  Given an operator holding fab "dresden" and a camera repository with no such camera
  When they PATCH /cameras/{id} with If-Match "3" and only a name
  Then the response is the rename handler's 404 problem, exactly
  And the repository was asked for exactly {id} in exactly "dresden"
```

Bad request and refusal order:

```gherkin
Scenario: A malformed Idempotency-Key is refused before the body is parsed
  When a caller POSTs /overlays (or /system-variables, /cameras) with a malformed key and an invalid body
  Then the response is the key refusal, not the INVALID_INPUT problem

Scenario: The body is parsed before fab resolution on the creates that resolve a fab
  When a caller holding no fab POSTs /system-variables (or /cameras) with an invalid name
  Then the response is 400 VARIABLE_INVALID_INPUT (CAMERA_INVALID_REQUEST) with the value object's message

Scenario: Overlay elements are checked before the name, and before the revision number
  When a caller POSTs /overlays with no elements and an invalid name
  Then the 400 OVERLAY_INVALID_INPUT detail is the missing-elements message
  When a caller PATCHes /overlays/{id}/revisions/0 with no elements
  Then the 400 detail is the missing-elements message, not the revision message

Scenario: An overlay draft edit with an invalid body is refused before the precondition
  When a caller PATCHes /overlays/{id}/revisions/1 with no elements and no If-Match
  Then the response is 400 OVERLAY_INVALID_INPUT, not 428

Scenario: A camera patch carrying both attributes, or neither, is refused
  Given a caller in their fab sending If-Match "3"
  When the body carries both rtspUrl and name     Then 400 "Send either rtspUrl or name, not both: ..."
  When the body carries neither (or whitespace)   Then 400 "Send either rtspUrl or name."

Scenario: A kiosk latency report is validated in the same order
  # already pinned by KioskLatencyRecordingCharacterisationTests (spec 341): non-kiosk 202 first,
  # then measurement, then camera, then finiteness; nothing recorded on refusal
```

Conflict / precondition:

```gherkin
Scenario: A camera patch without If-Match is refused with 428 only after the fab check
  Given a caller holding fab "dresden"
  When they PATCH /cameras/{id} with both attributes and no If-Match
  Then the response is 428, not 400

Scenario: An overlay draft edit without If-Match is refused with 428
  When a valid PATCH /overlays/{id}/revisions/1 carries no If-Match
  Then the response is 428 with the existing problem body
```

Auth / scope (security-sensitive):

```gherkin
Scenario: A camera patch from a caller with no usable fab is refused before anything else (FR-007, non-enumeration)
  Given a caller in two fabs who names neither, or a caller in no fab
  When they PATCH /cameras/{id} with no If-Match and both attributes
  Then the response is the existing fab refusal, not 428 and not 400
  And no camera is looked up

Scenario: An idempotent create is scoped to route, caller, fab and body (ADR-0142)
  Given operator "op-1" in "dresden" sending POST /cameras with Idempotency-Key "k1"
  Then the store is consulted with scope ("POST /cameras", op-1, Some("dresden"), fingerprint(body))
  # POST /system-variables: ("POST /system-variables", op-1, Some(fab), fingerprint)
  # POST /overlays:         ("POST /overlays", op-1, None, fingerprint) — OverlayDesigner has no fab

Scenario: A latency report from a non-kiosk principal is accepted and dropped
  # spec 341's test; the IsBrowserKiosk discriminator stays the first statement after the guard
```

Scope policies (401/403), `AllowAnonymous` on `POST /streams/authorize` and its `whep-authorize`
rate-limit policy are endpoint metadata, not handler code: pinned by the route-table rendering
(§4.1) and by the existing CI integration tests (§4.3), not driven in process.

## 3. Functional requirements

- **FR-001** Every method in §1 is at most 30 S138-lines; the §1 build prints no S138 hit in the
  four projects and exactly the same 15 S107 hits (by file and method; line numbers may move). No
  new warning. Every new helper has **at most 4 parameters**.
- **FR-002** No route, verb, name, summary, tag, authorization policy, `AllowAnonymous`,
  rate-limit policy, `Produces*`/`Accepts` metadata, handler method name or handler parameter list
  changes (§4.1 baselines equal).
- **FR-003** Every request gets the same status, problem body and side effect as before, in the
  same refusal order:
  - creates: idempotency key → body parse → fab resolution (SystemVariables, CameraCatalog) →
    operator resolution → idempotent dispatch;
  - `EditDraft`: route `Guid.Empty` → body/revision parse (elements null-check, then revision, then
    elements) → `If-Match` → handler;
  - `Patch`: fab → `If-Match` → exactly-one-attribute → `CameraIdentifier.From(camera)` →
    `ResolveOperator` → attribute parse → handler;
  - `RecordKioskLatency`: kiosk discriminator → measurement → camera → finiteness → record.
- **FR-004** Tokens the source guards read stay where the guards read them (plan §3.6 lists them
  per file). In particular: `ConcurrencyHeaders.TryReadExpectedVersion` in `EditDraft` and `Patch`;
  `IdempotencyHeaders.TryRead` and `IdempotentRequest.ExecuteCreateAsync` in the three creates; the
  `Guid.Empty` refusal spelling `StatusCodes.Status400BadRequest` in `EditDraft`; a
  `StatusCodes.Status400BadRequest` spelling in `Patch` (its exactly-one refusal); a
  `.ValidationProblem(` call in `RecordKioskLatency`; `report.Measurement == "…"` and
  `"…" => LatencySegment.` in `StreamEndpoints.cs`. Each mapping file keeps its existing
  `var <name> = app.MapGroup("…")` statement(s) unchanged; mapping helpers take the group under
  **the same name as that local** (`group`; in CameraCatalog `writes` / `reads`) and are declared
  below the Map method in the same file.
- **FR-005** No guard, test assertion, suppression, threshold, `.editorconfig` or `SonarLint.xml`
  is edited. No existing test file is modified. No new partial file of an endpoint class
  (`PreconditionDeclarationTests` pins `OverlayEndpoints` at 3 files).
- **FR-006** No migration, no `Shared.Contracts`, no Application/Domain/Infrastructure file, no
  AppHost change, no csproj change (Moq is already referenced by `Architecture.Tests`).

## 4. Characterisation (phase 4a, observed green before any production edit)

### 4.1 Route tables (new, in process) — prove FR-002 and cover methods 3, 5, 7, 10

One test file per context, each mapping only its own context into a bare
`WebApplication.CreateBuilder([]).Build()` (no services needed: every service-typed handler
parameter in these four projects is `[FromServices]`-attributed, checked), rendering every
`RouteEndpoint` in data-source order through one shared renderer, asserted equal to a raw-string
literal captured on `39b0ef64`. The renderer is spec 363's (same `IsContract` filter, so the
baseline holds in Debug and Release) plus one line kind: `EnableRateLimitingAttribute` → its policy
name, because `whep-authorize` is a security control (spec 208) that 363's renderer prints only as a
type name.

### 4.2 Endpoint behaviour (new, in process) — prove FR-003

The mapped `RequestDelegate` is driven directly (no Docker), with the **real** command handlers
over Moq'd repositories / fab guard / idempotency store, so the extracted code paths run end to
end. Problem bodies asserted **exactly**; repository calls verified with exact identifier, fab and
values (Moq `Verify`); keyed creates capture the `IdempotencyScope` and assert it field by field.

| Method | Cases |
|---|---|
| Overlay `CreateDraft` | malformed key → key refusal; malformed key + null elements → key refusal; null elements → 400 elements message; null elements + invalid name → elements message; invalid name → 400 name message; one invalid element → 400 element-parser message; keyed → scope (`POST /overlays`, sub, **None**, fingerprint); unkeyed valid → 201 + `Location: /overlays/{id}`, repo add verified with name + elements; name taken (repo returns an overlay by name) → exact 409 problem |
| Overlay `EditDraft` | empty id → 400; empty id + null elements → empty-id message; revision 0 + null elements → elements message; revision 0 + valid elements → revision message; null elements + no If-Match → 400; valid + no If-Match → 428; valid + If-Match → 404 from repo `None`, repo asked for the route id |
| Variables `Define` | malformed key → key refusal; malformed key + invalid name → key refusal; invalid name → 400; invalid type → 400; invalid name + invalid type → name message; initial value not of the type → 400 value message; only a truthy label → whatever `BooleanLabels.From(x, "")` answers (pin it); invalid name + caller with no fab → 400 (parse before fab); two-fab caller without `fabId` → exact `VARIABLE_FAB_REQUIRED` problem; keyed → scope (`POST /system-variables`, sub, Some(fab), fingerprint); unkeyed valid → 201 + `Location: /system-variables/{name}`, repo add verified with fab, name, type, value, labels; name taken → exact 409 |
| Camera `Register` | malformed key → key refusal; malformed key + invalid name → key refusal; invalid name → 400; invalid url → 400; invalid name + invalid url → name message (deconstruct order); invalid name + no fab → 400; two-fab caller without `fabId` → `CAMERA_FAB_REQUIRED`; keyed → scope (`POST /cameras`, sub, Some(fab), fingerprint of request); unkeyed valid → 201 + `Location: /cameras/{id}`, repo add verified; principal with no `sub` → the `InvalidOperationException` propagates (pin type + message) |
| Camera `Patch` | no usable fab + no If-Match + both → fab refusal, repo untouched; fab ok + no If-Match + both → 428; If-Match + both → 400 "not both" message; neither → 400 "Send either rtspUrl or name."; whitespace name + whitespace url → neither message; invalid name → 400 VO message; invalid url → 400 VO message; valid name → rename handler's 404, repo asked for exact (fab, id); valid url → address handler's 404, same verification; empty-Guid route + If-Match + valid name → pin what `CameraIdentifier.From` does (exception type, propagated); principal with no `sub` + invalid name → the `InvalidOperationException` (operator resolved before the attribute parse) |
| Stream `RecordKioskLatency` | **already covered** by `KioskLatencyRecordingCharacterisationTests` (16 tests, spec 341: kiosk discriminator, each measurement's instrument and tags, refusal order measurement → camera → finiteness, nothing recorded on refusal) and by `KioskMeasurementContractTests` (source spellings). Re-run unmodified; no new test. |

### 4.3 Existing coverage (CI integration, Docker; the second observer)

Not runnable in the lane's local loop; CI's integration shards observe them on the PR.

- **OverlayDesigner**: `OverlayLifecycleIntegrationTests`, `OverlayRevisionLifecycleIntegrationTests`,
  `OverlayETagIntegrationTests` (428 / stale), `OverlayElementValidationIntegrationTests` and
  `OverlayGeometryValidationIntegrationTests` (400 details), `OverlayNameUniquenessIntegrationTests`
  (409, keyed create), `OverlayElementCheckConstraintIntegrationTests`.
- **SystemVariables**: `SystemVariableLifecycleIntegrationTests`, `VariableFabResolutionIntegrationTests`,
  `VariableReadScopeIntegrationTests`, `ShadowedVariableNameIntegrationTests`; in-process
  `SystemVariableReadScopeTests`, `SystemVariableRouteShadowingTests` (Architecture.Tests).
- **StreamDistribution**: `WhepAuthIntegrationTests`, `WhepAuthorizeRateLimitTests`,
  `StreamFabScopingIntegrationTests`, `ListStreams*IntegrationTests`.
- **CameraCatalog**: `RegisterCameraIntegrationTests`, `IdempotentCameraRegistrationIntegrationTests`,
  `IdempotencyKeyFabBindingIntegrationTests` (incl. fab guard before key reuse),
  `CameraFabResolutionIntegrationTests`, `RenameCameraIntegrationTests` (both/neither 400, 428),
  `ChangeCameraAddressIntegrationTests` (428, 400), `CameraNonEnumerationIntegrationTests`
  (cross-fab 428 parity), `CameraAddressAuditIntegrationTests`.
- **Cross-context**: `IdempotencyKeyReuseAcrossContextsIntegrationTests`,
  `UniquenessRaceIntegrationTests`, `GatewayRoutingIntegrationTests`.

**Gaps they leave, which 4.1/4.2 close:** the whole route-table metadata (incl. the rate-limit policy
name and handler signatures), the in-body refusal orders (key before body, body before fab,
elements before name/revision, fab before If-Match before exactly-one), the exact problem bodies,
the idempotency scope's argument order per create, the operator-before-parse order in `Patch`, and
`EditDraft`'s revision message. None is asserted exactly by an existing test.

### 4.4 Source guards that must stay green unmodified

`PreconditionDeclarationTests`, `RouteValueRefusalDeclarationTests`,
`IdempotentKeyReuseDeclarationTests` (12 endpoints in 12 files), `EndpointScopeDeclarationTests`,
`StatusProducerDeclarationTests`, `ConcurrencyConflictDeclarationTests`,
`NameMutabilityConventionTests`, `KioskMeasurementContractTests`, `HandlerDeconstructionTests`,
`SystemVariableReadScopeTests`, `SystemVariableRouteShadowingTests`,
`KioskLatencyRecordingCharacterisationTests`, `LayoutCompositionRouteTableCharacterisationTests`.

## 5. Success criteria

- **SC-001** §1 build: 0 S138 in the four projects; the same 15 S107 by file + method; no new
  warning anywhere in those four builds.
- **SC-002** §4.1 and §4.2 observed green on `39b0ef64` in Release and Debug (output quoted in the
  PR), green and byte-unmodified after every refactor commit.
- **SC-003** All of `tests/Architecture.Tests` green; CI integration shards green on the PR.
- **SC-004** `git diff --stat origin/develop` touches only the 7 production files of §1, the new
  test files, and this spec directory.

## 6. Judgement calls (marked)

- **J1 — `BoundaryParse.TryParse` for the three create parses and the two `Patch` attribute
  parses** (spec 363's J1). Byte-identical refusal (title = code, detail = message, 400). Cost:
  `CreateDraft`, `Define` and `Register` bodies stop spelling `StatusCodes.Status400BadRequest`, so
  `RouteValueRefusalDeclarationTests` stops *demanding* the 400 declaration on `POST /overlays`,
  `POST /system-variables` and `POST /cameras`. None of the three has a route value (the guard's
  subject), and §4.1 pins their `ProducesValidationProblem(400)` exactly — strictly stronger.
  `EditDraft` and `Patch`, which do have a route value, keep a spelled 400 in their bodies (FR-004).
- **J2 — `RecordKioskLatency` extracts only the unknown-measurement refusal** (the 12-line
  dictionary) into an expression-bodied helper. The camera and finiteness refusals stay inline, so
  the body keeps a `.ValidationProblem(` antecedent, and every comment explaining the routing stays
  where it is. Smallest cut that reaches 30 with margin.
- **J3 — `Patch` dispatches through one generic private helper** (parse thunk + apply delegate,
  2 parameters) rather than a parameter record: no new type, no S107, and the command is built
  where it was, from locals computed in the original order. The exactly-one refusal stays inline;
  only its two-way detail string moves to an expression-bodied helper (plan §3.4).
- **J4 — Characterisation lives in `Architecture.Tests`**, as specs 341/363/365 do. To avoid a
  fourth and fifth copy of 363's renderer and request driver, this spec adds **two new shared
  internal helpers** (route-table renderer; in-process request driver) used only by the new files.
  363's and 365's files are **not** touched (FR-005); converging them is a later, separate cleanup.
- **J5 — One host per context, not one parameterised host.** OverlayDesigner, SystemVariables and
  CameraCatalog each get a small host owning only their DI + mocks; StreamDistribution needs none
  (its route table maps on a bare app; its behaviour is spec 341's).

## 7. Latency budget impact (constitution §IV)

**N/A to every leg's runtime.** Nothing here is on the event-to-overlay path. `RecordKioskLatency`
is the **recorder** for three kiosk-side legs (decode fragment, presentation buffer, composite +
render) plus wall skew and label delay: a behaviour change there would corrupt §IV's evidence rather
than its latency, which is why spec 341's exact instrument/tag assertions must pass unmodified. One
extra delegate allocation per write request is the only runtime difference.

## 8. Out of scope

S107; migrations; every method not in §1 (e.g. `AuthorizeWhep`, `Retire`, overlay
`Publish`/`Archive`/`Revert`/`BranchDraft`, `SetValue`, variable `Archive`) even where a new helper
would fit; converging 363/365's test helpers onto §6 J4's shared ones; any behaviour fix found along
the way (a separate issue).

## 9. Locked tech choices

.NET 10 Minimal APIs (ADR-0070), `Result<T,E>` + `ApiError.ToProblem` (ADR-0047/0089),
`Ensure.That` (ADR-0105), xUnit + Shouldly + Moq (ADR-0052), no Testcontainers (ADR-0103).
