# Verification — spec 130

Phase 4a colour: **RED, behaviour-changing.** Observed red before the change,
and observed red on a route that did not exist in this tree.

## 1. The population, measured before planning

| Figure | Value | How |
|---|---|---|
| route-handler mappings under `src/*/Api` | 56 | `Map(Get\|Post\|Put\|Patch\|Delete)(` sweep, agrees with the three existing guards' pinned count |
| `*Endpoints.cs` files | 12 | glob |
| authorized mappings (scoped or bare) | **54** | resolved from the chain, or the `MapGroup` bound by receiver |
| `AllowAnonymous` mappings | 2 | webhook ingest, WHEP auth hook |
| `Status401Unauthorized` declarations under `src/*/Api` before | **2** | both on the two **anonymous** chains |
| authorized mappings declaring 401 before | **0 of 54** | the guard's first run |
| authorized mappings declaring 403 before | 54 of 54 | spec 085 |

The issue's own instruction to leave #2113 and #2114 open was moot: both are
closed, and **both shipped derivations** — the 403 rule in
`EndpointScopeDeclarationTests` (spec 085) and the 400 rule in
`RouteValueRefusalDeclarationTests` (spec 091). Neither shipped a register.

## 2. Red — 12 of 12 files, 54 mappings

`dotnet test tests/Architecture.Tests -c Release --filter
"FullyQualifiedName~StatusProducerDeclarationTests"`, on commit `6ecaf491`
(the guard alone, before the fix):

```
Failed!  - Failed:    12, Passed:    15, Skipped:     0, Total:    27
```

One file, verbatim:

```
  Failed SmartSentinelEye.Architecture.Tests.StatusProducerDeclarationTests.Every_authorized_mapping_declares_the_challenge_its_authorization_produces(file: "src/Identity/Api/DevicesEndpoints.cs") [< 1 ms]
  Error Message:
   Shouldly.ShouldAssertException : undeclared
    should be empty but had
3
    items and was
["src/Identity/Api/DevicesEndpoints.cs:39 POST /devices/register declares 201, 400, 403, 409", "src/Identity/Api/DevicesEndpoints.cs:50 DELETE /devices/{clientId} declares 200, 400, 403, 404, 409", "src/Identity/Api/DevicesEndpoints.cs:63 GET /devices/ declares 200, 400, 403"]

Additional Info:
    these mappings require authorization and never declare the challenge it produces:
src/Identity/Api/DevicesEndpoints.cs:39 POST /devices/register declares 201, 400, 403, 409
src/Identity/Api/DevicesEndpoints.cs:50 DELETE /devices/{clientId} declares 200, 400, 403, 404, 409
src/Identity/Api/DevicesEndpoints.cs:63 GET /devices/ declares 200, 400, 403
AddBearerAuthentication registers JwtBearer and AddScopePolicies builds every sse.* policy as RequireAuthenticatedUser() plus a claim assertion, so a caller with no token, an expired one or one from the wrong issuer is challenged with 401 — not forbidden, which is the 403 these same chains already declare. The document tells a client author that answer cannot happen, and a generated client has no branch for it. Add .ProducesProblem(StatusCodes.Status401Unauthorized) to the mapping's own chain; change no route, scope, summary or handler.
```

The twelve failures name **54 distinct mappings** — the predicted population,
arrived at independently of the count typed into the spec.

## 3. Red on code nobody had written — the counterfactual

A guard that only passes against today's tree proves nothing about tomorrow's.
After the fix (suite green, 27/27), one route that does not exist in this
repository was added to `SystemVariableEndpoints.cs`:

```csharp
        group.MapGet("/counterfactual", Counterfactual)
            .RequireAuthorization(Scope.Sse.Variables.Read)
            .WithName("Counterfactual")
            .WithSummary("A route nobody has written. Required scope: sse.variables.read")
            .Produces<string>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);
```

```
  Error Message:
   Shouldly.ShouldAssertException : undeclared
    should be empty but had
1
    item and was
["src/SystemVariables/Api/SystemVariableEndpoints.cs:58 GET /system-variables/counterfactual declares 200, 400, 403"]
```

and, separately, the census assertion saw the new mapping:

```
   Shouldly.ShouldAssertException : swept
    should be
56
    but was
57
```

```
Failed!  - Failed:     2, Passed:    25, Skipped:     0, Total:    27
```

The route was then removed. The guard fails on a route written after it, from
its antecedent, not from a list.

## 4. Green

```
Passed!  - Failed:     0, Passed:    27, Skipped:     0, Total:    27   (this guard)
Passed!  - Failed:     0, Passed:   388, Skipped:     0, Total:   388   (Architecture.Tests)
Passed!  - Failed:     0, Passed:   181, Skipped:     0, Total:   181   (ServiceDefaults.Tests)
Passed!  - Failed:     0, Passed:    79, Skipped:     0, Total:    79   (Shared.Kernel.Tests)
Passed!  - Failed:     0, Passed:    77, Skipped:     0, Total:    77   (Shared.Contracts.Tests)
```

`dotnet build -c Release` — **0 warnings, 0 errors**. It caught six analyzer
errors a Debug run had hidden (CA1875 ×4, S2971, S3218); those are fixed in the
phase-4a commit, so **both commits build on their own** — verified by building
the guard commit with `src/` stashed.

No other guard's pinned count moved: the 403 sweep (spec 085 A15), the 400
sweeps (specs 072, 091) and the 409 register (spec 075) each count only their
own status.

## 5. What a green run does not prove

- **It does not prove the 401 is declared *because* of the authorization.**
  OpenAPI has one slot per status. The two anonymous routes declare 401 for a
  different reason, and a third reason arriving on an authorized route would be
  indistinguishable from this one.
- **It runs one way.** *Declares 401 ⇒ must require authorization* would fail
  the two anonymous routes, which are correct. There deliberately is no mirror.
- **The census is a register where it is a classification.** Its population of
  exception handlers is derived by reflection and checked against the
  registration site, so a sixth handler fails the build; which side of the line
  each falls on is typed in. It cannot notice a mechanism nobody wrote down —
  M14, the gateway's rate limiter, was found by grepping for `RateLimiter`.
- **Completeness stops at `src/*/Api`.** A declaration hoisted into a convention
  in `src/ServiceDefaults` leaves the walk and the flat sweep equally, so they
  still agree and only the per-chain rule fires — true of the text it can see,
  misleading about the document. Inherited limit, recorded by spec 085 first.
- **Nothing here was observed against a running service.** No Aspire stack was
  booted and none was needed: the change is OpenAPI metadata and a build-time
  source scan. A 401 is what `RequireAuthenticatedUser()` produces by
  construction; that it is *declared* is what this verifies, not that it is
  *returned*.

## 6. Latency

**§IV untouched, confirmed.** `.ProducesProblem(...)` attaches endpoint metadata
at startup and adds no work to any request. The guard runs at build time. No leg
of the event-to-overlay budget is affected, and none is cited.
