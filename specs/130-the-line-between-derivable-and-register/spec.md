# Spec 130 — The line between derivable and register-only

**Issue:** #2142 — *A status produced away from the mapping line goes undeclared — five instances, no rule*
**Branch:** `test/2142-the-line-between-derivable-and-register`
**ADRs:** 0037 (phases), 0139 (red first), 0144 (autonomous lane), 0036 (smallest change), 0070 (minimal APIs)

## The class, restated

A status produced somewhere other than the line that maps the route does not get
declared. The endpoint author declares what they wrote; the request also meets
middleware, policies, shared exception handlers and helpers that are nowhere in
the `Map…` chain. The generated OpenAPI then asserts a status the route
certainly returns cannot happen, and a generated client has no branch for it.

Six instances have been fixed one at a time — #91, #2088/spec 072, #2101,
#2096/spec 075, #2113/spec 085, #2114/spec 091. This spec does not fix a
seventh by hand. It draws the line the six were found either side of, measures
the population behind it, and builds what the line says is derivable.

## What the tree already holds (measured, not inherited)

Two of the six shipped since #2142 was filed, and the issue's own instruction
("do not fold in the open instances; #2113 and #2114 are filed and can proceed")
is moot: both are **closed**, and both shipped as **derivations**, not registers.

| Guard | Spec | Rule | Kind |
|---|---|---|---|
| `EndpointScopeDeclarationTests` A13 | 085 | scoped mapping ⇒ declares 403 | derivation, chain-visible antecedent |
| `RouteValueRefusalDeclarationTests` G1 | 091 | handler body can answer 400 ⇒ chain declares 400 | derivation, handler-body antecedent |
| `PreconditionDeclarationTests` | 072 | reads `If-Match` ⇒ declares 428 and 400 | derivation, handler-body antecedent |
| `ConcurrencyConflictDeclarationTests` | 075 | which mutating routes can answer 409 | **pinned register** |

So the derivable half is not unbuilt. It is built in three places, each found
independently, and **nobody has said what makes those three derivable and the
fourth not** — which is why a seventh instance is still findable by accident.

## The mechanism census

Every mechanism in this tree that can put a status on a response the mapping
line does not name, and where its antecedent lives.

| # | Mechanism | Status | Produced in | Antecedent visible in… |
|---|---|---|---|---|
| M1 | JWT bearer challenge on an authorized route | **401** | `AuthenticationDefaults.AddBearerAuthentication` → JwtBearer middleware | **the mapping's own chain** — `.RequireAuthorization(…)`, or the `MapGroup` it was written on |
| M2 | `AddScopePolicies` claim assertion | **403** | `RequireScopeExtensions` policy | **the mapping's own chain** — same antecedent, plus the scope literal |
| M3 | `BadHttpRequestExceptionHandler` | the exception's own — 400, 413, 415 | ServiceDefaults; raised by minimal-API model binding | the handler **signature** (`[FromQuery] Guid?`, required parameters) |
| M4 | `FabAuthorizationExceptionHandler` | 403 | thrown by `IFabAuthorizationGuard` | nowhere — Application, 2+ hops |
| M5 | `UnattributableOperatorExceptionHandler` | 401 | thrown by `ClaimsPrincipalExtensions` | nowhere — 2+ hops |
| M6 | `ConcurrencyConflictExceptionHandler` | 409 | EF `DbUpdateConcurrencyException` | nowhere — spec 075's finding |
| M7 | `UniqueConstraintExceptionHandler` | 409 | SQLSTATE 23505 | nowhere |
| M8 | `ApiError.Status` rendering a `Result` failure | whatever the error carries | Application error types | nowhere — 3–4 hops, and `Add` vs mutate-then-save is invisible at Api |
| M9 | `IdempotentRequest` in-progress refusal | 409 | ServiceDefaults, **returned not thrown** | the **handler body** — the call site is there |
| M10 | `IdempotencyHeaders.TryRead` malformed key | 400 | ServiceDefaults | the **handler body** |
| M11 | `ConcurrencyHeaders` `If-Match` read | 428, 400 | ServiceDefaults | the **handler body** — spec 072 derives it |
| M12 | the handler's own route-value refusal | 400 | the endpoint's own file | the **handler body** — spec 091 derives it |
| M13 | route-constraint mismatch (`:guid`) | 404 | routing | the route template, in the chain — but see below |
| M14 | `ApiGateway` fixed-window rate limiter | 429 | `src/ApiGateway/Program.cs`, `UseRateLimiter` | nowhere in the nine documents — the gateway maps no routes and calls no `AddOpenApi` |

M13 is chain-visible and still not a rule: a constraint miss is a **routing**
404, so there is no operation for it to be a response of. Spec 091 already
recorded the related trap — `Guid.Empty` satisfies `:guid` and still earns the
handler's 400, so the constraint is not a defence.

## The line

**A mechanism is derivable when its antecedent is reachable from the mapping
without leaving the Api project.** That splits three ways, and the middle band
is the part nobody had named:

- **Chain-visible — M1, M2.** The antecedent is a call in the mapping's own
  fluent chain, or in the `MapGroup` the mapping was written on. One guard,
  all routes, fails on code nobody has written yet.
- **Handler-body visible — M3, M9, M10, M11, M12.** One hop: resolve the
  method group named in the chain, read its body or its signature. Demonstrated
  three times (072, 091, and this spec does not add a fourth).
- **Not visible — M4, M5, M6, M7, M8, M14.** The producer is in Application, in
  a shared handler, or in another process. **A register is the ceiling.**

## The gap this spec closes

M2 is derived (spec 085). **M1 is not derived and is not declared anywhere.**

Measured on this branch: 56 route-handler mappings in 12 `*Endpoints.cs` files;
**54 are authorized** (scoped or bare), 2 are `AllowAnonymous`. Exactly **two**
`Status401Unauthorized` declarations exist under `src/*/Api` — and both sit on
the two **anonymous** chains, where the handler validates a bearer itself.

**So 0 of the 54 authorized mappings declare the 401 their authorization
produces**, while 54 of 54 declare the 403 from the same antecedent. The
asymmetry is the proof that #2113 fixed an instance and not a class: the same
`.RequireAuthorization(…)` line produces both answers, `AddScopePolicies` builds
every policy as `RequireAuthenticatedUser()` plus a claim assertion, and a
caller with no token is **challenged**, not forbidden.

## Scope

**In:** the M1 derivation and its soundness companions; the census above made
load-bearing where it can be; the 401 declaration on the 54 authorized chains.

**Out, with reasons:**
- No change to spec 075's pinned register. It pins M6/M7/M8, all of which sit
  below the line. Nothing here can derive them, so nothing here replaces them.
- No new rule for M3's framework producer. Spec 091 already records it as its
  largest residual, and it needs the handler *signature*, not its body.
- **The shared-reader extraction is not done here.** Spec 091 assigned it to
  #2142 ("a fifth copy of `RepositoryRoot()`, the masker and the chain reader
  … owned by #2142"). That is a behaviour-preserving refactor of five test
  files; this spec is behaviour-changing. ADR-0144 forbids mixing them. It
  needs its own issue, and this guard is the sixth copy, said out loud.

## Success criteria

1. A guard fails when a mapping requires authorization and its chain does not
   declare 401 — demonstrated on a route that does not exist in this tree.
2. The 54 authorized mappings declare it; the 2 anonymous ones are untouched.
3. No route gains a status it cannot return. 401 is added only where the
   antecedent is the authorization line itself.
4. `dotnet build -c Release` clean; Architecture.Tests green.

## Honesty block — what a green run proves, and what it does not

Modelled on spec 075's.

- **The M1 rule is a derivation.** Its antecedent is resolved, not typed: delete
  a route's `RequireAuthorization` and the route leaves the population in the
  same edit. A route added tomorrow is checked on its first build.
- **It does not prove the 401 is declared *because* of the authorization.**
  OpenAPI has one slot per status. The two anonymous routes declare 401 for a
  different reason (M5's shape — a bearer their own handler rejects), and a
  third reason arriving on an authorized route would be indistinguishable.
- **It does not prove the document is right — only that it is not wrong in this
  one direction.** The rule runs one way. A mirror rule (*declares 401 ⇒ must
  require authorization*) would fail the two anonymous routes, which are
  correct, so there deliberately is none.
- **The census is a register where it is a classification.** The *population* of
  registered exception handlers is derived by reflection, so a sixth handler
  fails the build; **which side of the line each falls on is typed in**, and a
  green run proves only that the five known ones still exist and are still
  classified. It cannot notice a mechanism nobody wrote down — M14 was found by
  sweeping for `RateLimiter`, not by any test.
- **Completeness stops at `src/*/Api`.** Same limit spec 085 records: a
  declaration hoisted into a convention in `src/ServiceDefaults` leaves both the
  walk and the flat sweep equally, so they still agree and only the per-chain
  rule fires — truthfully about the text, misleadingly about the document.

## Latency

**§IV is not touched.** No change reaches a request path: the declarations are
OpenAPI metadata attached at startup, and the guard is a build-time source scan.
No leg of the event-to-overlay budget is affected.
