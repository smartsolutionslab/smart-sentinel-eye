# Plan — Spec 073, a catalogued scope is enforced

**Issue:** #2070 · **Spec:** `specs/073-a-catalogued-scope-is-enforced/spec.md`

## Shape of the change

Six lines of `src/`, three deleted lines of `tests/`, and two tests. Nothing is
designed here that does not already exist; this plan is mostly about *where* the
three reds live and why the register deletion is safe.

```
src/SystemVariables/Api/SystemVariableEndpoints.cs
  :23        comment — "reads require any authenticated user" → the new rule
  :38-42     comment above the reads — same
  :43-51     GET /            + .RequireAuthorization(Scope.Sse.Variables.Read)
                              + "Required scope: sse.variables.read" in the summary
  :53-58     GET /snapshot    + .RequireAuthorization(Scope.Sse.Variables.Read)
                              + .WithSummary(...)   ← first one this endpoint has had
  :60-66     GET /{name}      + .RequireAuthorization(Scope.Sse.Variables.Read)
                              + "Required scope: sse.variables.read" in the summary

tests/Architecture.Tests/EndpointScopeDeclarationTests.cs
  :240-245   UnenforcedByDesign — the three #2070 rows deleted; the array stays,
             empty, with its doc-comment intact

tests/Architecture.Tests/SystemVariableReadScopeTests.cs        (new, Docker-free)
tests/Integration.Tests/SystemVariables/VariableReadScopeIntegrationTests.cs  (new, [CI])
```

No file is added to or removed from `src/*/Api`, so
`EndpointScopeDeclarationTests.EndpointFileCount` (12) and
`RouteHandlerMappingCount` (56) are untouched. If either has to change, the
change is wrong.

## Bounded context and layers

**SystemVariables**, **Api layer only.** Domain, Application and Infrastructure
are not opened. No entity, no value object, no invariant, no aggregate, no
repository — the authorization decision is made in ASP.NET middleware before any
handler runs, which is exactly why it belongs at the Api layer and nowhere else.

**Boundary rules:** no cross-context reference is added. `SystemVariables.Api`
already references `ServiceDefaults` for `Scope` and `IFabAuthorizationGuard`;
that is the only dependency the change uses. Nothing goes near
`Shared.Contracts` because nothing crosses a context.

## Messaging

**None.** No domain event, no integration event, no `V<N>` contract, no
Wolverine handler, no outbox row. A 403 is not an event.

## Persistence and migrations

**None.** No table, no column, no EF model change, no `MigrationRunner` work.

## What enforces the rule after this lands

The policy is registered once, at startup, by
`RequireScopeExtensions.AddScopePolicies` over `Scope.All` — `sse.variables.read`
is already in that list (`Scope.cs:105`), so its policy already exists and has
simply never been demanded. `.RequireAuthorization(Scope.Sse.Variables.Read)`
names it by string, which is what every other scoped endpoint does.

Policies compose by AND, so the group-level `.RequireAuthorization()` at `:35`
and the per-route policy both apply: no header → 401 from the group; header
without the scope → 403 from the route. Keeping the group call is FR-002 and is
not an oversight to be tidied away.

`sse.management` continues to satisfy the new requirement, by
`AddScopePolicies`' `acceptLegacyBundle` branch (`RequireScopeExtensions.cs:59`).
That is pre-existing behaviour, it is deliberate, and this plan does not touch
it — but it is the reason the fixture's default principal is useless as a
negative case, so it drives the test design below.

## The three reds, and where each lives

| # | Asserts | Project | Docker |
|---|---|---|---|
| 1 | Each of the three routes resolves to policy `sse.variables.read` | `Architecture.Tests` (new file) | No |
| 2 | The register's completeness half names the three routes once its rows are deleted | `Architecture.Tests` (existing guard) | No |
| 3 | A fab-member token without the scope gets 403 on all three | `Integration.Tests` (new file) | **Yes — [CI]** |

### Red 1 — `SystemVariableReadScopeTests`

`Architecture.Tests` already project-references
`src/SystemVariables/Api/SmartSentinelEye.SystemVariables.Api.csproj` (line 42
of its `.csproj`), so the endpoints can be mapped in-process:

```
WebApplication app = WebApplication.CreateBuilder([]).Build();
app.MapSystemVariableEndpoints();
Endpoint[] endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints;
// for each of the three GET routes: metadata.GetOrderedMetadata<IAuthorizeData>()
//   must contain one whose Policy == Scope.Sse.Variables.Read
```

This reads the endpoint the framework built, not the text of the file, which is
the point: a summary-only edit cannot make it pass, and neither can a comment.

**Risk, and the fallback ladder.** Minimal-API endpoint *building* may want
services for the `[AsParameters]` / `[FromServices]` parameters. In order:
(a) add `builder.Services.AddAuthorization()`; (b) add
`AddSystemVariablesApi()` — registration only, no connection opened;
(c) if the DI graph proves to want a live database, abandon the metadata form
and assert through the source-scan reader that already exists in
`EndpointScopeDeclarationTests`, which parses the same three chains today. All
three rungs are Docker-free and all three are red before the fix. **The engineer
states in the PR which rung it stood on** — a guard that quietly fell back is
the failure mode ADR-0139 is about.

### Red 2 — the existing register, read the other way

`EndpointScopeDeclarationTests` holds the three routes in `UnenforcedByDesign`
and checks it in both directions:

- `Every_endpoint_that_enforces_no_scope_is_registered_against_an_open_issue`
  (`:594`) — a bare route **not** in the register fails.
- `Every_registered_route_still_enforces_no_scope` (`:621`) — a row that
  **no longer matches** a bare route fails.

**The order of operations is the design.** Delete the rows *first*, before
touching `src/`: the completeness half then fails naming all three routes, which
is the defect stated in the guard's own words. Add the scope second: that half
goes green and `Every_scoped_endpoint_names_the_scope_it_enforces_in_its_summary`
(`:537`) goes red until each summary carries the literal — which is what forces
the three summaries and `/snapshot`'s first `.WithSummary`. Two reds, both
specific, neither of them a second copy of a guard that already exists.

**This is the register retiring, not a gate being weakened.** Its doc-comment
says so at `:236` — "Fixing 2070 deletes these rows; the completeness half fails
if it does not." ADR-0144 forbids the lane from deleting a test, lowering a
threshold, adding a suppression or narrowing an analyzer; deleting a row from a
both-ways register *strengthens* what is asserted, because the route it named
moves from "excused, against an issue" to "checked like every other endpoint".
The array itself stays, empty, with its doc-comment — the next endpoint that
needs a deferral needs the mechanism, and an empty register is the honest record
that nothing is currently deferred.

### Red 3 — the refusal, on the real stack

**The problem this test exists to solve:** `AspireFixture.ClientId` is
`smart-sentinel-eye-web` and `FetchAccessTokenAsync` always asks for
`openid sse.management` (`AspireFixture.Auth.cs:12`, `:111`). That bundle
satisfies the new policy. A test written with `CreateAdminClientAsync` returns
200 before and after and proves nothing — and that is not hypothetical, it is the
mechanism by which this gap survived long enough for a phase-6 review of an
unrelated PR to find it.

**The way round it:** a principal that is in a fab group and does not hold the
scope. The realm ships two, both service accounts, both reachable by
`client_credentials`:

| Client | Secret | Groups | Has `sse-audience`? | Has `sse.variables.read`? |
|---|---|---|---|---|
| `scenario-simulator` | `dev-only-scenario-simulator-secret` | `/fabs/munich` | Yes | No |
| `stream-distribution-attribution` | `dev-only-stream-distribution-secret` | `/fabs/munich`, `/fabs/dresden` | Yes | No |

`scenario-simulator` first; the other is the alternate if anything about the
simulator's account changes. `sse-audience` matters because spec 069 made the
API check `aud`; both have it as a default client scope, so the 403 will be the
scope refusal and not an audience rejection — **the test asserts 403 and not
merely "not 200"**, and it also asserts the admin client still gets 200 on the
same route in the same run, so a broken fixture cannot masquerade as a pass.

Reuse the existing minting shape from
`tests/Integration.Tests/StreamDistribution/StreamFabAttributionIntegrationTests.cs:97`
or `tests/Integration.Tests/Identity/FabGroupClaimIntegrationTests.cs:176`. **Do
not add a helper to `AspireFixture`** — two call sites already do this by hand,
and a third is not yet a pattern (ADR-0036, no speculative generality).

Mint from **Aspire's proxied Keycloak endpoint**, not the container's mapped
port, or the issuer will not match and everything 401s regardless of scope.

## Boundary and convention compliance

- **ADR-0070** — minimal APIs, fluent chain, unchanged in shape.
- **ADR-0036** — smallest change. The endpoints are not restructured, the
  handlers are not touched, `RequireScope` is neither adopted nor deleted, no
  fixture helper is introduced, no realm edit.
- **ADR-0105** — no new argument guard; nothing new takes an argument.
- **ADR-0084** — `SystemVariableEndpoints.cs` is 448 lines before the change and
  gains ~6. It is already past the 300-line limit, so the limit is evidently
  already satisfied some other way for this file; the engineer reports it if the
  Release build says otherwise rather than splitting the file to make room.
- **Constitution §II** — no domain type is touched, so no primitive-boundary
  question arises.
- **Constitution §Testing** — behaviour-changing, so red first, and the verbatim
  failure output goes in the PR body (ADR-0139).
- **ADR-0086** — Conventional Commits, **no `Co-Authored-By` footer**.

## Commit sequence

Each commit must build and be individually meaningful — rebase-merge lands them
one at a time on `develop` (ADR-0087), so a commit that only compiles with its
successor breaks `git bisect`.

1. `test(architecture): the variable reads are asked which scope they require`
   — Red 1, new file. Red. **This commit does not build green and that is the
   point**; it is the phase-4a observation, and its output is quoted in the PR.
   (If the repo's convention is that no commit may be red, squash 1 into 3 and
   quote the observed failure from the run before committing — the engineer
   picks, and says which in the PR.)
2. `test(architecture): the register stops excusing the three variable reads`
   — Red 2, the three rows deleted.
3. `fix(system-variables): the three reads require sse.variables.read`
   — FR-001, FR-003, FR-004, FR-008. Reds 1 and 2 go green together.
4. `test(integration): a fab member without the scope cannot read variables`
   — Red 3, `[CI]`.

Commits 1 and 2 may be a single commit if the engineer prefers one red
observation; they may not be reordered after 3.

## Risks

1. **The metadata test needs more DI than expected.** Mitigated by the fallback
   ladder above. Cost if it bites: the guard becomes a source scan, which is
   weaker but still red-then-green and still Docker-free.
2. **Docker stays unresponsive through phase 4.** Then Red 3 is observed on CI
   from the PR run, and the failure is taken from the **downloaded job log
   before** any re-run — a passing re-run flips the whole run to success and
   erases the evidence.
3. **A caller outside this repository breaks.** Unfalsifiable from here; recorded
   as assumption 3 in the spec. It is also the population the issue says is the
   reason to do this.
4. **Someone re-adds the register rows to quiet a failure.** The completeness
   half would then be green and the staleness half red, so the repo cannot rest
   in that state. Called out here because it is the one wrong move this change
   makes easy.

## Gate — Phase 2

Plan aligns with the constitution (§VIII, §Testing) and with ADR-007/008,
ADR-0036, ADR-0070, ADR-0139, ADR-0144. **No new ADR is required**; nothing here
is a new architectural decision.
