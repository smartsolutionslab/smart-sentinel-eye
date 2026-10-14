# Spec 363: The layout endpoints under thirty lines

**Feature Branch**: `refactor/2929-layout-s138` (cut from `origin/develop` at `0296275e`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Three judgement calls
are marked explicitly (§6).

**Input**: Issue [#2929](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2929),
*ADR-0084 S138: split the long methods in LayoutComposition.Api*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green"). Engineer: **backend-engineer**.

**Spec number.** 363. Checked 2026-10-15 after `git fetch`: `origin/develop` highest 361; every
remote head via `git ls-tree` highest 362 (`origin/refactor/2941-option-internal-params`). 363 is
taken by agreement for this issue; #2933's architect, running in parallel, takes 364. Re-check
before the PR is opened.

**ADRs.** ADR-0084 (code metrics: S138 <= 30 lines, S107 <= 4 parameters, advisory `warning`),
ADR-0144 (autonomous lane; phase 4a colours; the lane may not weaken a gate), ADR-0070 (Minimal
APIs), ADR-0036 (smallest change), ADR-0105 (`Ensure.That` guards), ADR-0113 (`If-Match` refusal
order is contract), ADR-0142 (idempotency scope = route, caller, fab, fingerprint), ADR-0089
(`ApiError` → problem). No new ADR: this changes shape, not a decision.

---

## 1. What was measured (premise verified, not assumed)

```sh
dotnet build src/LayoutComposition/Api -c Release --no-incremental -nologo -v q 2>&1 \
  | grep "LayoutComposition.Api" | grep -oE "warning S[0-9]+[^\[]*" | sort -u
```

Result on `0296275e`: **10 S138 hits in `src/LayoutComposition/Api`, exactly the issue's 10, same
methods, same line counts.** #2943 touched only an Application handler; nothing is stale.

| # | File:line | Method | Lines |
|---|---|---|---|
| 1 | `LayoutEndpoints.Commands.cs:17` | `CreateDraft` | 48 |
| 2 | `LayoutEndpoints.Commands.cs:92` | `Publish` | 34 |
| 3 | `LayoutEndpoints.Commands.cs:142` | `Archive` | 34 |
| 4 | `LayoutEndpoints.Commands.cs:234` | `EditDraft` | 42 |
| 5 | `LayoutEndpoints.Commands.cs:293` | `Revert` | 34 |
| 6 | `LayoutEndpoints.cs:19` | `MapLayoutEndpoints` | 98 |
| 7 | `WallEndpoints.Commands.cs:18` | `CreateWall` | 46 |
| 8 | `WallEndpoints.Commands.cs:87` | `EditScenes` | 38 |
| 9 | `WallEndpoints.Commands.cs:142` | `Switch` | 38 |
| 10 | `WallEndpoints.cs:17` | `MapWallEndpoints` | 61 |

S138 counts non-blank, non-comment lines between the braces (Publish: 39 physical lines, 5 blank =
34). The same build prints **10 S107 hits** in this project (7 at 5 parameters, 3 at 6); they are out
of scope (issue: the `[AsParameters]` question) and **must not grow**. The 5 migration S138 hits in
`Infrastructure/Persistence/Migrations` are generated and excluded by the issue.

## 2. User story

### US1 (P1): LayoutComposition.Api's endpoint methods fit ADR-0084's 30-line limit with no observable change

An operator, a kiosk, the gateway and the generated OpenAPI client see exactly the same API before
and after: same routes, verbs, names, summaries, tags, authorization policies, declared statuses,
the same status and problem body for every request, in the same refusal order.

**Why P1 / why one slice.** It is the whole issue: 10 methods across 4 files of one project. It is
under the ~10-methods-across-4-files split threshold only just, so it ships as **one PR in two
commit groups** (layouts, then walls), each group building and passing on its own.

**Independent test.** Characterisation tests (§4) observed green on `0296275e` before any production
edit, then green **unmodified** after each refactor commit; the §1 build prints zero S138 hits in
`LayoutComposition.Api` and still exactly the 10 S107 hits.

#### Acceptance scenarios

Route table (the conflict case for a refactor is "the shape moved"):

```gherkin
Scenario: The route table is identical after the refactor
  Given the LayoutComposition endpoints are mapped in process on develop 0296275e
  And their canonical route-table rendering is captured as a literal baseline
  When the same rendering is taken after the refactor
  Then it equals the baseline line for line
    # pattern, verb, name, summary, tags, authorization policies, produces metadata in order,
    # accepts metadata, handler method name and parameter list
```

Happy path (handler outcome reaches the wire unchanged):

```gherkin
Scenario: A valid publish reaches the handler with the route's identifiers and the caller's fabs
  Given an operator holding fab "dresden" with sse.layouts.write
  And the layout repository holds no layout with the route's identifier in "dresden"
  When they POST /layouts/{id}/revisions/1/publish with If-Match "3"
  Then the response is 404 with the handler's problem body
  And the repository was asked for exactly that identifier within exactly ["dresden"]
```

(One such scenario per command endpoint; §4 lists them.)

Bad request (refusal and refusal order):

```gherkin
Scenario: An empty layout identifier is refused before the precondition
  When a caller POSTs /layouts/00000000-0000-0000-0000-000000000000/revisions/1/publish without If-Match
  Then the response is 400 LAYOUT_INVALID_INPUT "layoutIdentifier must be a non-empty Guid."
  And not 428

Scenario: An invalid body is refused before the precondition (EditDraft, EditScenes, Switch)
  When a caller PATCHes /layouts/{id}/revisions/1 with no grid and no If-Match
  Then the response is 400 LAYOUT_INVALID_INPUT with the parser's message

Scenario: A malformed Idempotency-Key is refused before the body is parsed (CreateDraft, CreateWall)
  When a caller POSTs /layouts with a malformed Idempotency-Key and an invalid body
  Then the response is the key refusal, not LAYOUT_INVALID_INPUT
```

Conflict / precondition:

```gherkin
Scenario: A command without If-Match is refused with 428
  Given a valid route and a valid body
  When the request carries no If-Match
  Then the response is 428 with the existing problem body
```

Auth / scope:

```gherkin
Scenario: A caller who holds no fab is refused before the handler runs
  Given a caller whose token carries no /fabs/ group
  When they send any write command with a valid route, body and If-Match
  Then the response is the existing fab-resolution refusal and the repository is never called

Scenario: An idempotent create is scoped to route, caller, fab and body
  Given an operator in "dresden" sending POST /layouts with Idempotency-Key "k1"
  Then the idempotency store is consulted with scope ("POST /layouts", that operator, "dresden", fingerprint of the body)
  # same for POST /walls; pins the argument order of the extracted helper (ADR-0142)
```

Scope policies (401/403 by `RequireAuthorization`) are middleware, not handler code: they are pinned
by the route-table rendering (policy names per endpoint) and by the existing CI integration tests
(§4.3), not driven in process.

## 3. Functional requirements

- **FR-001** Every method in §1 is at most 30 S138-lines; the §1 build prints no S138 hit in
  `src/LayoutComposition/Api` and exactly the same 10 S107 hits (by file and method; line numbers
  may move). No other new warning anywhere in the build.
- **FR-002** No route, verb, name, summary, tag, authorization policy, `Produces*`/`Accepts`
  metadata, handler method name or handler parameter list changes (§4.1 baseline equal).
- **FR-003** Every request gets the same status and problem body as before, in the same refusal
  order: idempotency key → body/route parse → `If-Match` → fab resolution → handler.
- **FR-004** The tokens the endpoint guards read in a handler body stay in that body:
  `ConcurrencyHeaders.TryReadExpectedVersion` (7 handlers), `IdempotentRequest.ExecuteCreateAsync`
  and `IdempotencyHeaders.TryRead` (2 creates), and the `Guid.Empty` refusal with
  `StatusCodes.Status400BadRequest` (6 route-value handlers). Mappings stay in `LayoutEndpoints.cs`
  / `WallEndpoints.cs` on a parameter or local named `group`, and each file keeps exactly one
  `var group = app.MapGroup("…")` (plan §3.4).
- **FR-005** No guard, test assertion, suppression, threshold, `.editorconfig` or `SonarLint.xml`
  is edited. Existing test files are not modified.
- **FR-006** No migration, no `Shared.Contracts`, no Application/Domain/Infrastructure file, no
  AppHost change.

## 4. Characterisation (phase 4a, observed green before any production edit)

### 4.1 Route table (new, in process) — proves FR-002

`tests/Architecture.Tests/LayoutCompositionRouteTableCharacterisationTests.cs`. Builds a
`WebApplication` the way `KioskLatencyRecordingCharacterisationTests` (spec 341) does, calls
`MapLayoutEndpoints()` and `MapWallEndpoints()`, renders every `RouteEndpoint` in data-source order
as one canonical line, and asserts the list equals a raw-string literal captured on `0296275e`.
Covers methods 6 and 10 completely and the handler signatures of 1-5 and 7-9.

### 4.2 Endpoint behaviour (new, in process) — proves FR-003

Drives the mapped `RequestDelegate` directly (no Docker), handlers constructed with Moq'd
repositories / guards / idempotency store, so the **extracted code paths** are exercised
including the dispatch tail. Per method:

| Method | Cases (exact status + exact problem JSON) |
|---|---|
| CreateDraft | malformed key → key refusal; malformed key + invalid body → key refusal; invalid name → 400; null grid → 400; keyed create → store consulted with scope (route, caller, fab, fingerprint); unkeyed create → 201 + `Location: /layouts/{id}`; caller with no fab → fab refusal |
| Publish, Archive, Revert | empty id → 400; empty id + no If-Match → 400; revision 0 → 400 (VO message); no If-Match → 428; no fab → refusal, repo untouched; valid → 404 from repo `None`, repo called with route id + caller fabs |
| EditDraft | empty id → 400; revision 0 + null grid → revision message (tuple order); null grid + no If-Match → 400; no If-Match → 428; valid → 404, repo called with route id + fabs |
| CreateWall | as CreateDraft with `WALL_INVALID_INPUT`, `POST /walls`, `Location: /walls/{id}`; null scenes → 400 |
| EditScenes | empty id → 400; null scenes + no If-Match → 400; no If-Match → 428; valid → 404 from wall repo `None`, called with route id + fabs |
| Switch | empty id → 400; target "bogus" → 400 "Unknown switch target 'bogus'."; target "layout" without layout → 400; no If-Match → 428; valid → 404, repo called with route id + fabs |

### 4.3 Existing coverage (CI integration, Docker; the second observer)

Not runnable in the lane's local loop; CI's integration shards observe them on the PR.
`LayoutLifecycleIntegrationTests`, `EditRevisionIntegrationTests` (publish, revert, branch, edit),
`LayoutETagIntegrationTests` (428 / stale 409 / re-read), `LayoutFabScopingIntegrationTests`
(403/404 fab isolation, create fab inference), `LayoutNameUniquenessIntegrationTests` (409, keyed
create), `AbsentLayoutBodyMembersAreRefusedIntegrationTests` (create/edit 400, anonymous 401),
`TileSpanIntegrationTests` (400 details), `WallEndpointsTests` (US1-1..15: create, keyed replay,
switch, edit scenes, 428, 409, kiosk 403, fab 404), `WallNameCaseInsensitivityIntegrationTests`,
`IdempotencyKeyReuseAcrossContextsIntegrationTests` (422 reuse), `AggregateVersionConflictIntegrationTests`.

**Gaps they leave, which 4.2 closes:** the `Guid.Empty` 400 on all six route-value handlers, the
revision-number 400, parse-before-`If-Match` order, key-before-body order, the idempotency scope's
argument order, and `Switch`'s two target refusals. None is reachable by any existing test.

### 4.4 Source guards that must stay green unmodified

`PreconditionDeclarationTests` (counts: 5 Layout If-Match handlers resolve into
`LayoutEndpoints.Commands.cs`), `IdempotentKeyReuseDeclarationTests` (count of `IdempotentRequest`
endpoints), `RouteValueRefusalDeclarationTests` (`MapGroup` sites == bound groups; mapping count),
`EndpointScopeDeclarationTests`, `StatusProducerDeclarationTests`,
`ConcurrencyConflictDeclarationTests`, `HandlerDeconstructionTests`.

## 5. Success criteria

- **SC-001** §1 build: 0 S138 in `LayoutComposition.Api`; S107 set unchanged; no new warning.
- **SC-002** §4.1 and §4.2 observed green on `0296275e` (output quoted in the PR), green and
  byte-unmodified after each refactor commit.
- **SC-003** All of `tests/Architecture.Tests` green; CI integration shards green on the PR.
- **SC-004** `git diff --stat origin/develop` touches only the 4 Api files, at most one
  `src/LayoutComposition/Api/LayoutCompositionFabResolution.cs`, the new test files, the
  `Architecture.Tests` csproj (Moq reference), and this spec directory.

## 6. Judgement calls (marked)

- **J1 — Reuse `BoundaryParse.TryParse` for the body parses.** Its refusal is byte-identical to the
  inline `catch (ArgumentException)` blocks (title = error code, detail = message, 400). Consequence:
  `CreateDraft`/`CreateWall` bodies no longer spell `StatusCodes.Status400BadRequest`, so
  `RouteValueRefusalDeclarationTests` stops *demanding* the 400 declaration on `POST /layouts` and
  `POST /walls`. Those routes have no route value (the guard's subject), and §4.1 pins their 400
  declaration exactly, which is strictly stronger. The six route-value handlers keep their inline
  `Guid.Empty` refusal, so the guard's coverage of them is unchanged.
- **J2 — Characterisation lives in `Architecture.Tests`**, following spec 341's in-process
  precedent, rather than a new `LayoutComposition.Api.Tests` project (new project = sln, coverage
  and CI wiring for a refactor). It needs a `Moq` package reference added to that csproj (Moq is
  the repo's mocking library, ADR-0052); that is a test dependency, not a gate change.
- **J3 — Per-route mapping helpers** (`MapCreateLayoutDraft(group)` …) rather than grouped ones.
  Any cohesive grouping of the layout routes exceeds 30 lines in at least one group (create + get +
  list = 31); one helper per mapping is uniform, named after the existing `WithName`, and leaves
  every chain byte-identical.

## 7. Latency budget impact (constitution §IV)

N/A. Management write endpoints; nothing on the event-to-overlay path. One extra delegate
allocation per write request is the only runtime difference.

## 8. Out of scope

S107 (the `[AsParameters]` / DI-parameter question), migrations, `BranchDraft` (same shape as
`Publish` but under the limit today and not listed; it is left as written even where the helpers of
plan §3 would fit it), every other context's Api (#2930-#2932), any behaviour fix found along the
way (a separate issue).

The "existing fab-resolution refusal" in §2 and §4.2 is whatever `0296275e` does for a caller with
no usable `/fabs/` group: a problem result from `LayoutCompositionFabResolution`, or a
`FabAuthorizationException` thrown out of the delegate (mapped to 403 by the global handler, which
an in-process drive does not include). The test-writer pins whichever it observes, exactly.

## 9. Locked tech choices

.NET 10 Minimal APIs (ADR-0070), `Result<T,E>` + `ApiError.ToProblem` (ADR-0047/0089),
`Ensure.That` (ADR-0105), xUnit + Shouldly + Moq (ADR-0052), no Testcontainers (ADR-0103).
