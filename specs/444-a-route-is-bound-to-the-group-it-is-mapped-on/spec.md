# Spec 444: A route is bound to the group it is mapped on

**Issue:** #3101 (feature-level, on Project #13) · **Branch:** `test/3101-scope-binding`
**ADRs:** ADR-0139 (rules that fail the build, not the review — this guard is one), ADR-0179 (the credential-bearing
`GET /cameras/{camera}/address` must sit under the write scope; the route that exposed the gap), ADR-0054 (hand-written
in-file fixtures), ADR-0053 (test naming), ADR-0144 (lane). Constitution §Testing (red for new behaviour,
characterisation for what must not move).
**Specs built on:** 070 (the guard, `EndpointScopeDeclarationTests`), 085 (its refusal rule), 190 (the shared
`SourceMask` / `RouteChainReader` it reads with), 438 (`MapGetCameraAddress`, slice A of #3016).
**Lane:** autonomous (ADR-0144) · **Phase-4a colour:** **red** for the new binding; **characterisation** for the 106
existing cases of the class, which must pass unmodified (plan §"Phase 4a").
**Security-sensitive:** no runtime change; the guard is a scope-declaration check, so `security-reviewer` is
optional. **Latency budget:** N/A — test code only, no leg touched.
**New ADR needed:** no (plan §"Why no ADR").
**Written 2026-10-24 against `develop` at `99516703`.**

---

## Premise, checked at `99516703`

1. **The binding is by name, and parameters are bound by it.** `Read` (`tests/Architecture.Tests/
   EndpointScopeDeclarationTests.cs:1438-1518`) builds a file-wide `name → group` dictionary from
   `var|RouteGroupBuilder <name> = …MapGroup(…)` declarations; `ReadMapping` (`:1535`) looks the mapping's receiver up
   in it by name. A helper parameter `RouteGroupBuilder writes` is never a declaration (`LocalDeclaration` requires
   `=`), so `writes.MapGet(…)` inside a helper resolves to whichever local is *named* `writes`, regardless of what the
   caller passed. **Holds.**
2. **Demonstrated, not reasoned.** In `src/CameraCatalog/Api/CameraEndpoints.cs` the call `MapGetCameraAddress(writes)`
   (`:41`) was moved below the `reads` group as `MapGetCameraAddress(reads)` — the credential read now really enforces
   `sse.cameras.read` while its summary says `sse.cameras.write`. `dotnet test tests/Architecture.Tests --filter
   FullyQualifiedName~EndpointScopeDeclarationTests` reported **`Failed: 0, Passed: 106`**. Reverted.
3. **The shape is the norm, not an edge.** 13 of the 16 endpoint files map through private helpers taking a single
   `RouteGroupBuilder` parameter (`writes`, `reads` or `group`) — 65 helpers. A shell sweep found **every helper is
   called exactly once in its file, with an argument spelled the same as its parameter** — so today's name binding
   happens to agree with the call sites everywhere, and a call-site binding is green on the current corpus.
4. **The class doc is wrong in two places.** `:218-222` says "An endpoint mapped from a helper … No such indirection
   exists today" (in-file helper indirection is universal); `:282-301` describes name binding as the reader's
   contract without saying a parameter is bound by it.
5. **Correction to the issue's "no backstop" clause.** All nine contexts now have a route-table baseline
   (`tests/Architecture.Tests/*RouteTableCharacterisationTests.cs`) and every scoped mapping appears there with its
   `authorize policy=`. A misbinding is therefore caught at runtime level in every context today. This spec still
   fixes the source guard: a guard that credits a route with a scope it does not enforce is false evidence, and the
   baselines are re-recorded in the same diff as the change they pin, which is a weaker review signal.
6. **Same defect class elsewhere, out of scope.** `StatusProducerDeclarationTests.cs:866-868` binds a group-level
   declaration to a mapping by receiver name too; `RouteValueRefusalDeclarationTests.cs:745` does so for the prefix
   in a message only. Follow-up issue to be filed (tasks T009), not fixed here.

## User stories

### US1 (P1) — A helper's mappings inherit the group its caller actually passed

As the maintainer relying on `EndpointScopeDeclarationTests`, I want a mapping written on a helper's parameter to be
credited with the authorization of the group passed at the helper's call site, and to be refused when that cannot be
established, so that the guard can never report a route as enforcing a scope it does not.

**Why P1 / only story:** it is the whole issue. Independently testable through fixtures fed to the reader.

#### Acceptance scenarios

```gherkin
Scenario: happy — a helper called with the group its parameter is named after
  Given a file declaring "var writes = app.MapGroup(\"/c\").RequireAuthorization(Scope.Sse.Cameras.Write)"
  And a helper "MapX(RouteGroupBuilder writes)" mapping "writes.MapGet(\"/a\", H)"
  And the only call "MapX(writes)"
  When the reader reads the file
  Then the mapping GET /c/a is Scoped with constant "Scope.Sse.Cameras.Write"

Scenario: conflict — the parameter's name and the caller's argument disagree (the #3101 shape)
  Given groups "writes" (Cameras.Write) and "reads" (Cameras.Read)
  And a helper "MapX(RouteGroupBuilder writes)" mapping "writes.MapGet(\"/a\", H)"
  And the only call "MapX(reads)"
  When the reader reads the file
  Then the mapping is Scoped with constant "Scope.Sse.Cameras.Read"
  And a summary claiming "Required scope: sse.cameras.write" on it is reported as a mismatch

Scenario: conflict — one helper called with two different groups
  Given the calls "MapX(writes)" and "MapX(reads)"
  When the reader reads the file
  Then the mapping is Unreadable, naming the helper and both groups

Scenario: bad request — the argument is not a group declared in the file
  Given the only call is "MapX(app.MapGroup(\"/c\"))" or "MapX(other)" where "other" is not a group local
  When the reader reads the file
  Then the mapping is Unreadable, naming the argument

Scenario: bad request — the helper is never called in the file
  Given a helper "MapX(RouteGroupBuilder writes)" with no call site in the file
  When the reader reads the file
  Then the mapping is Unreadable rather than bound to the local named "writes"

Scenario: auth — a parameter is never bound by name
  Given a method whose parameter list declares the receiver in a shape the reader does not resolve
    (more than one parameter, or a non-RouteGroupBuilder route-builder type)
  And a group local elsewhere in the file with the same name
  When the reader reads the file
  Then the mapping inherits no group authorization (its own chain's, or Unreadable)
```

#### Edge cases

- A mapping on a group local in the method that declares it binds exactly as today (no call site involved).
- A chain's own `.RequireAuthorization` still wins over any group (unchanged precedence, `:1541`).
- The `this IEndpointRouteBuilder app` entry point is a parameter but not a group: mappings on it keep today's
  behaviour (own chain, or "not a route group … unreadable").
- A helper calling another helper with its own parameter (`MapY(writes)` inside `MapX`) is Unreadable — no
  transitive resolution (none exists today).

## Functional requirements

- **FR-001** A mapping whose receiver is a parameter of its enclosing method MUST NOT be bound to a group by name.
- **FR-002** For a method with exactly one parameter, typed `RouteGroupBuilder`, a mapping on that parameter MUST be
  bound to the group named by the argument of the method's in-file call sites, when there is ≥1 call site, every
  call passes a single bare identifier, and every such identifier is the same group local declared in the file.
- **FR-003** Every other case in FR-002's population MUST resolve to `AuthorizationKind.Unreadable` with a detail
  naming the helper and the reason (no call, differing groups, non-group argument). Under-recognition stays red,
  never silent — the class's existing contract (`:203-211`).
- **FR-004** The 106 existing test cases MUST pass unmodified, and the corpus pins (`EndpointFileCount = 16`,
  `RouteHandlerMappingCount = 71`) MUST not move.
- **FR-005** The class doc MUST describe call-site binding and drop the false "no such indirection" sentence.

## Success criteria

- **SC-001** The planted shape of premise 2 turns `EndpointScopeDeclarationTests` red (quoted in the PR).
- **SC-002** The new fixture facts are observed red on `99516703`'s reader and green after (quoted).
- **SC-003** Full `Architecture.Tests` green; no `src/` file changed.

## Independent end-to-end test procedure

1. `dotnet test tests/Architecture.Tests --filter FullyQualifiedName~EndpointScopeDeclarationTests` — green.
2. In a scratch edit, move `MapGetCameraAddress(writes);` below the `reads` group as `MapGetCameraAddress(reads);`
   in `src/CameraCatalog/Api/CameraEndpoints.cs`; re-run: red, naming `GET /cameras/{camera:guid}/address` and the
   `sse.cameras.write` vs `Scope.Sse.Cameras.Read` mismatch. Revert.
3. Repeat with a second call `MapGetCameraAddress(reads);` added beside the first: red, Unreadable (two groups).
4. Repeat by deleting the call entirely: red, Unreadable (helper never called) — A2 only (`Every_mapping_in_an_endpoint_file_is_read_rather_than_skipped`) fails; the count pin does not, so quote that one.

## Locked choices

Source-scan guard in `tests/Architecture.Tests` (ADR-0139); shared masker/reader from spec 190, unchanged; xUnit +
Shouldly; in-file raw-string fixtures (ADR-0054, `SourceScanFixtures` precedent); no new test class (so no
`ci-shards` filter entry).

## Out of scope

`StatusProducerDeclarationTests` / `RouteValueRefusalDeclarationTests` name binding (follow-up); cross-file helpers;
transitive helper chains; any `src/` change.
