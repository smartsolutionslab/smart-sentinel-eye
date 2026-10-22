# Plan 437 — The decision point every scope asks

Spec: [spec.md](spec.md) · Governing ADR: ADR-0183 (Implementation Notes, "What the lane builds")

## Placement

All new types in `src/ServiceDefaults/Authorization/` beside `IFabAuthorizationGuard` (ADR-0183 §1). No bounded
context, no `Shared.Contracts` change, no cross-context reference. ServiceDefaults already references
Shared.Kernel (`Ensure`, `Option<T>` — see `ConcurrencyHeaders.cs:75`). ServiceDefaults is outside §II's domain
primitive rule, so `string scope` / `string fab` follow the ADR's signature verbatim.

## Types

| Type | Shape / invariant |
|---|---|
| `IAuthorizationDecisionPoint` | `Task<AuthorizationDecision> DecideAsync(ClaimsPrincipal principal, string scope, Option<string> fab, CancellationToken cancellationToken)` — exactly ADR-0183's signature; never throws for a deny |
| `AuthorizationDecision` | Allow, or Deny with a reason: `MissingScope` / `FabNotAuthorized` (+ the fab, so the existing `RESOURCE_FAB_NOT_AUTHORIZED` 403 can be reproduced by later migration slices) |
| `DefaultAuthorizationDecisionPoint(IFabAuthorizationGuard)` | Scope split identical to today's lambda (`scope` claims, split on `' '`, `RemoveEmptyEntries`, `StringComparer.Ordinal`). Scope missing → Deny(MissingScope), guard not called. Fab `Some` → `await guard.EnsureAccessAsync`; `catch (FabAuthorizationException)` → Deny(FabNotAuthorized); nothing else caught. `Ensure.That` on principal and scope (ADR-0105) |
| `ScopeRequirement(string Scope)` : `IAuthorizationRequirement` | one per policy |
| `ScopeRequirementHandler(IAuthorizationDecisionPoint)` : `AuthorizationHandler<ScopeRequirement>` | asks with `Option<string>.None`; `Succeed` on Allow; on Deny does **not** call `Fail()` (mirrors `RequireAssertion`'s semantics: unsatisfied requirement → 403) |

## `AddScopePolicies` change (the seam at the lambda, ADR-0183 §2)

Per scope: `policy.RequireAuthenticatedUser().AddRequirements(new ScopeRequirement(scope))`. Policy names
unchanged. **The method must register its own dependencies via `builder.Services`** —
`TryAddSingleton<IFabAuthorizationGuard, DefaultFabAuthorizationGuard>()`,
`TryAddSingleton<IAuthorizationDecisionPoint, DefaultAuthorizationDecisionPoint>()`,
`TryAddEnumerable(Singleton<IAuthorizationHandler, ScopeRequirementHandler>)` — because
`tests/ServiceDefaults.Tests/Authorization/RequireScopePolicyTests.cs:14` builds `AddAuthorizationBuilder().AddScopePolicies(Scope.All)`
**alone**, with no guard registered, and must pass unmodified. `AuthenticationDefaults.cs:48`'s `AddSingleton` of the
guard stays (TryAdd keeps it the winner); test hosts that register a `Mock<IFabAuthorizationGuard>` keep theirs.

## Record edits in this PR (ADR-0183 edits 1 + 4 only)

1. Constitution line 380 → exactly the ADR's "row after" text.
2. Version 1.11.0 → **1.12.0**, `Last Amended` date, one amendment-history entry above 1.11.0 citing ADR-0183 / issue 2225.
Edits 2 (§Security line 367) and 3 (row 023) are **not** applied — human-owned per ADR-0183; flag them in the PR body.

## Boundary / guard interplay

- `FoundingDecisionRecordTests` (authorization case) is green today, **red** after the interface alone, **green** again
  with the row edit — so interface + row edit go in the same commit-pair window, and the PR is never split.
- `EndpointScopeDeclarationTests`, `StatusProducerDeclarationTests` (M2 "AddScopePolicies claim assertion"),
  `ScopeGrantTests`, `LegacyBundleGrantTests`, `RegisteredPolicyTests`, `RequireScopePolicyTests`, `KioskScopeParityTests`:
  read policy names / endpoint text, not the lambda body — expected unaffected; must pass unmodified.
- Commit ordering (ADR-0087, every commit builds): the red commit must compile, so it carries the **declarations**
  (interface, `AuthorizationDecision`, a `DefaultAuthorizationDecisionPoint` whose body throws `NotImplementedException`)
  — and declaring the interface flips the record guard. Therefore the **§IX row edit + 1.12.0 bump travel in that same
  red commit**, keeping `FoundingDecisionRecordTests` green at every commit; only the new decision-point facts are red.
  The green commit fills the implementation and switches `AddScopePolicies`.
