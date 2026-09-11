# Plan — spec 130

## Shape

One new Architecture test file plus a one-line addition to 54 mapping chains.
Nothing in `src` changes behaviour: `.ProducesProblem(...)` is OpenAPI metadata.

## The guard: `tests/Architecture.Tests/StatusProducerDeclarationTests.cs`

Named for the class, not for the status, so the next person looking for "what
covers statuses produced away from the mapping line" finds it by filename —
the failure mode the class has already had six times.

### Antecedent, stated exactly

Within the mapping's own chain span (from `.MapX(` to the terminating
semicolon, comments blanked and literal *content* masked) **or** within the
chain of the `MapGroup` statement whose bound variable is the mapping's
receiver: a call matching `\.RequireAuthorization\s*\(`, and no
`\.AllowAnonymous\s*\(` in the mapping's own chain.

`AllowAnonymous` on the mapping wins over `RequireAuthorization` on the group —
that is ASP.NET's own precedence, and it is how the two anonymous routes are
written today (both sit under an authorized group).

### Consequent

Within the mapping's own chain span:
`\.Produces(?:Validation)?Problem\(\s*StatusCodes\.Status401Unauthorized\s*\)`.
Both spellings, because `PreconditionDeclarationTests` learned in spec 072 that
matching only `ProducesProblem` reports correct code as broken.

### Assertions

| # | Assertion | Kind |
|---|---|---|
| L1 | per endpoint file: every authorized mapping declares 401 on its own chain; the file yields ≥1 mapping | **derivation** |
| L2 | no route group declares 401 — OpenAPI inherits group metadata, so a group-level declaration would make L1 unsound and fail correct code | derivation |
| L3 | the walk's 401 count equals a flat sweep of `src/*/Api/**/*.cs` — a declaration the walk cannot see is one L1 does not credit | derivation |
| L4 | the mapping census agrees: 12 files, 56 mappings, and every `Map*` site the flat sweep finds is enumerated | pinned counts + agreement |
| L5 | the authorized population is non-empty **and** the anonymous population is non-empty — L1's antecedent is a filter, not a tautology, in both directions | derivation |
| L6 | every `IExceptionHandler` in the `ServiceDefaults` assembly (found by reflection) is classified by the census; every classified row still resolves to a type | **register of classifications over a derived population** |
| L7 | every mechanism classified chain-visible names a guard file and a test method that exists | derivation |
| L8 | the guard offers no way to excuse an endpoint (`allowlist`, `exempt`, `suppress`, `#pragma warning disable`, …) | derivation over the guard's own source |

L6 is the one place the line itself is written down in code. Its population is
derived — add a sixth `IExceptionHandler` and it fails until someone says which
side of the line it falls on — but the classification is typed, and the spec's
honesty block says so rather than presenting it as a rule.

L7 is what stops the line from being prose: if A13 is deleted or renamed, the
row claiming M2 is derived fails.

### Why a new file rather than an addition to `EndpointScopeDeclarationTests`

That file is the nearest neighbour by antecedent — A13 resolves the very same
authorization kind — and it is 2181 lines. Its subject is *the scope an
endpoint names*; M1's subject is authentication, which applies to bare
`RequireAuthorization()` too. The census (L6/L7) belongs to neither existing
file, because it is about all four guards at once. Cost, named: a sixth copy of
`RepositoryRoot()`, the masker and the chain reader. The extraction spec 091
assigned to #2142 is a behaviour-preserving refactor and cannot ride along with
a behaviour-changing change (ADR-0144); it needs its own issue.

## The fix

Add `.ProducesProblem(StatusCodes.Status401Unauthorized)` to each of the 54
authorized mapping chains, positioned as the first refusal in the chain — the
order a request meets them: challenge, then forbid, then the rest.

Touched: all 12 `*Endpoints.cs` files plus the three partial files that carry
mappings (`EventsEndpoints.Reads/.Writes`, `LayoutEndpoints.Commands/.Queries`,
`OverlayEndpoints.Commands/.Queries`). No handler, route, scope or summary
changes. No group gains a declaration (L2).

## Risks

- **A false declaration.** Guarded by construction: the antecedent *is* the
  authorization line, and a route without one is not touched. The two anonymous
  routes keep the 401 they already declare for their own reason.
- **Interaction with existing pinned counts.** The 403 sweep (A15), the 400
  sweep (spec 072/091) and the 409 register count only their own status; adding
  401 lines moves no number they read. Verified by running the whole
  Architecture suite, not by reasoning.
- **File length.** The endpoint files already run to 513 lines, so the 300-line
  metric is not enforced on them today; `dotnet build -c Release` is the check.

## Verification

1. Phase 4a: write the guard, run it, quote the red — 54 offenders.
2. Counterfactual: add a route that does not exist in this tree, with a scope
   and no 401, see it named; remove it.
3. Phase 4b: add the declarations, run the guard green.
4. `dotnet build -c Release` on the solution; full Architecture.Tests run.
