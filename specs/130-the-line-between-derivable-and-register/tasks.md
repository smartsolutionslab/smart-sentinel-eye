# Tasks — spec 130

Phase 4a colour: **RED, behaviour-changing.** The guard must be seen failing,
and must be seen failing on a route that does not exist in this tree.

| # | Task | Done when |
|---|---|---|
| T1 | Measure the population: mappings, authorized, anonymous, existing 401 declarations | figures in spec.md, obtained from the tree, not from the issue |
| T2 | Enumerate the mechanisms M1–M14 and classify each | census table in spec.md |
| T3 | Write `StatusProducerDeclarationTests` — L1 through L8 | file compiles, suite runs |
| T4 | Observe L1 red and quote it verbatim | 54 offenders named, per file |
| T5 | Counterfactual: add a scoped route that does not exist in this tree, with no 401; observe L1 name it; remove it | the new route appears in the failure output |
| T6 | Add `.ProducesProblem(StatusCodes.Status401Unauthorized)` to the 54 authorized chains | L1 green |
| T7 | Full Architecture.Tests run — no other guard's pinned count moved | all green |
| T8 | `dotnet build -c Release` | clean, TreatWarningsAsErrors |
| T9 | `verification.md` with the before/after counts and the honesty block's limits | written |

## Not in this spec, recorded so it is not lost

- **The shared-reader extraction** (#2142 inherited it from spec 091). Six
  copies of `RepositoryRoot()` + masker + chain reader now exist across
  `EndpointScopeDeclarationTests`, `PreconditionDeclarationTests`,
  `RouteValueRefusalDeclarationTests`, `ConcurrencyConflictDeclarationTests`,
  `PaginatedConsumerTests` and this file. It is a behaviour-preserving refactor
  and must not ride with a behaviour-changing change. **Needs its own issue.**
- **M3's framework producer.** A route whose only 400 comes from model binding
  passes every guard here unnoticed. Spec 091 records it; this spec does not
  close it, because the antecedent is the handler signature and nothing reads
  signatures yet.
- **M14's 429.** The gateway's rate limiter can answer a caller of any route,
  and no per-service OpenAPI document can express it — the gateway maps no
  routes and calls no `AddOpenApi`. Out of every document's reach, and recorded
  rather than fixed.
