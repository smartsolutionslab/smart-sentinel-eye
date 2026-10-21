# ADR-0183: The authorization decision point wraps the fab guard now, shaped to absorb it later

**Status:** **Proposed** (the owner ruled on 2026-10-21 on #2225: "wrap now, shaped to absorb later; ADR first"; this records the ruling)
**Date:** 2026-10-21
**Amends:** constitution §IX (the *Authorization* row) and §Security (the *Authorization* bullet), and `docs/adr/0000-initial-decisions.md` row 023, by the text in *Implementation Notes*, applied by a human with the version bump
**Relates to:** ADR-023 (row 023), ADR-0114, ADR-0130, ADR-0144, ADR-0161, ADR-0178, spec 047, spec 110, issues 1970, 2225

## Context

Constitution §IX mandates, "in v1", a strategy interface for authorization so a policy engine (OPA,
Cedar) can plug in for v2 without touching call sites: `IAuthorizationDecisionPoint` (decision 023:
*"All authz checks go through `IAuthorizationDecisionPoint`"*). ADR-0130 recorded it absent, and §IX's
row says so: **`IAuthorizationDecisionPoint` — absent** (issue 1970). ADR-0178 separated it from #1970's
rule-engine half and left the design to #2225.

#2225's two corrections, re-verified on `develop` at `6206ecf9`:

**Half of the seam already exists, spelled `Guard`.** `IFabAuthorizationGuard`
(`src/ServiceDefaults/Authorization/IFabAuthorizationGuard.cs`) is DI-injected (singleton,
`AuthenticationDefaults.cs:47`) with one implementation, and answers the fab half of a decision: it
throws `FabAuthorizationException` (mapped to 403) unless the caller's `groups` claim holds
`/fabs/<fab>`. It is reached through `FabResolution` (38 call sites) and directly at 7 more
(`AuditEndpoints`, `DevicesEndpoints`, `KiosksEndpoints`, `WebhookRotationEndpoints`); the issue's figure of
29 has grown.

**The scope half has one mechanism and no seam.** `.RequireAuthorization(Scope.Sse.*)` appears at **52
sites in 16 files** (the issue's 39/12 has grown too). Every one names a policy that
`AddScopePolicies(Scope.All)` (`AuthenticationDefaults.cs:52`) builds as `RequireAuthenticatedUser()` plus
**one** `RequireAssertion` lambda splitting the `scope` claim (`RequireScopeExtensions.cs:38-48`). The
issue's second lambda (`AuthenticationDefaults.cs:98`) no longer exists. `LayoutLifecycleHub`'s
`[Authorize(Policy = Scope.Sse.Layouts.Read)]` names the same policy, so it already goes through that
lambda. **`RequireScope` has zero call sites** in `src/` or `tests/`; it appears only in two doc comments.

**Scope checks that bypass the policy**, verified:

| Site | Shape |
|---|---|
| `AuthorizeWhepCommandHandler.cs:25,62` with `WhepAuthValidator.cs:239` | Application-layer check of a hand-validated token for MediaMTX's auth hook (ADR-0161); no ASP.NET principal |
| `WebhookDeliveryAuthentication.cs:179` | hand-rolled `sse.events.write` check (not in the issue) |
| `AuditEndpoints.cs:159` | a fab check made after the query, a different shape from the pre-dispatch ones |

**The record guard.** `FoundingDecisionRecordTests.Section_nine_agrees_with_the_code_about_its_strategy_interfaces`
passes only while *"`IAuthorizationDecisionPoint` — absent"* appears in the constitution **if and only if**
no such type is declared in `src/`. It fails in **both** directions: declaring the interface while the row
says absent, and editing the row while the interface does not exist.

## Decision

**1. Wrap, not absorb, not beside.** `IAuthorizationDecisionPoint` is introduced in
`ServiceDefaults/Authorization` as the one question every authorization check can ask:

> may this **principal** act under this **scope**, optionally within this **fab**?

Its default implementation **composes** the existing scope check (the same claim split
`RequireAssertion` does today) and, when a fab is given, **calls `IFabAuthorizationGuard`**. The guard is not
changed, not moved and not deleted. A v2 policy engine replaces the implementation, not the call sites.

Proposed shape (owner to confirm the return type):

```csharp
public interface IAuthorizationDecisionPoint
{
    Task<AuthorizationDecision> DecideAsync(
        ClaimsPrincipal principal,
        string scope,
        Option<string> fab,
        CancellationToken cancellationToken);
}
```

`AuthorizationDecision` is allow, or deny with a reason that preserves today's distinction between a missing
scope (403 from the policy) and a missing fab (403 `RESOURCE_FAB_NOT_AUTHORIZED`). Returning a decision
rather than throwing is what lets an external engine answer without exceptions on its hot path; the guard's
throw is translated inside the default implementation.

**2. The scope policy routes through it now.** `AddScopePolicies` stops building a `RequireAssertion`
lambda and registers, per scope, a `ScopeRequirement` handled by a DI-resolved `IAuthorizationHandler` that
asks the decision point with **no fab**. Policy **names do not change**, so none of the 52 call sites, the
SignalR hub's attribute, or anything `EndpointScopeDeclarationTests` reads changes, and the observable
answers (401 unauthenticated, 403 without the scope) are identical. This is the "seam at the lambda, not at
the endpoints" the issue argued for.

**3. Guard call sites migrate later, one slice each, characterisation only.** Moving `FabResolution` and
the 7 direct `EnsureAccessAsync` calls onto the decision point is behaviour-preserving work: each slice
captures its covering tests green first and passes them unmodified (constitution §Testing, ADR-0144). Not in
the first slice. Until then the guard is called both directly and through the decision point, which is
the *wrap* the owner chose.

**4. The stragglers are named, not built here.** The WHEP hook, webhook delivery authentication and the
audit post-query check stay as they are in the first slice; each becomes its own later slice. §IX's "all
authz checks go through it" is therefore **not yet true** after the first slice, and the record must say so
(Implementation Notes, row text).

**5. `RequireScope` is removed in its own issue**, not here, so the seam change and the dead-code removal do
not hide each other.

**6. Security-sensitive.** Every slice under this ADR runs `security-reviewer` at phase 6. The first slice
changes how every scoped endpoint in nine services is authorized; a mistake there is a fleet-wide
authorization defect.

Latency budget: **N/A in effect.** The scope check sits in front of every HTTP route, including the
event-ingestion endpoints on the *event → overlay state* leg; it becomes one DI resolution and one interface
call instead of an inline lambda, with no I/O. The first slice should show the leg's existing latency tests
pass unmodified.

## Consequences

**Positive: the §IX obligation exists in code**, at the single point every scoped route already passes
through, without touching a call site.

**Positive: absorption stays open.** Because the interface takes the fab, the guard's call sites can move
onto it one at a time, and the guard can later become an implementation detail of the decision point.

**Negative: two ways to ask the fab question, for a while.** Until the migration slices land, a reader sees
both `IFabAuthorizationGuard` and the decision point in endpoint code. That is the price of wrap-now.

**Negative: §IX stays partly undischarged** until the stragglers move. The row text below says so rather
than claiming completion.

## Alternatives Considered

**Absorb now** (retire `IFabAuthorizationGuard`, move all its call sites in one change). One security change
across nine contexts and ~45 call sites at once, with the record guard tripping mid-way. Rejected by the
owner for "wrap now, absorb later".

**Beside** (a decision point for scopes only, the guard left independent). Satisfies the letter of §IX for
scopes and leaves a v2 engine unable to decide on fab membership, which is half of every decision. Rejected.

**Seam at each endpoint** (`.RequireDecision(...)` replacing 52 `RequireAuthorization` calls). Costs the change
at the wrong place, as the issue showed, and churns `EndpointScopeDeclarationTests` for no behavioural gain.
Rejected.

## Implementation Notes

**Before the lane starts (human, owner's ruling):** this ADR accepted, and the record edits below prepared.
**Sequencing problem, for the owner:** the record guard fails if the §IX row changes before the interface
exists, and also if the interface lands before the row changes. So the row edit and the interface must be
**in the same PR**. Either a human commits the row edit on the lane's branch, or this ADR authorizes the
lane to apply exactly the text below in that PR, as ADR-0178 does for the rule-engine row.

**Record edits (proposed text):**

1. **§IX, Authorization row, interface column:** *"**`IAuthorizationDecisionPoint`** wraps the fab guard; the
   scope policy routes through it; the fab guard's call sites, the WHEP hook, webhook delivery and the audit
   post-query check do not yet (ADR-0183, issue 2225)"*. The literal *"`IAuthorizationDecisionPoint` — absent"*
   disappears, which is what the guard needs.
2. **§Security, Authorization bullet:** replace *"**`IAuthorizationDecisionPoint` does not exist**, so v2
   cannot plug in a policy engine without touching call sites — the §IX obligation this bullet assumed is
   unmet"* with *"**`IAuthorizationDecisionPoint`** exists and every scope policy routes through it; fab checks
   reach it only through the guard it wraps, and three hand-rolled checks bypass it, so the §IX obligation is
   **partly** met (ADR-0183, issue 2225)"*.
3. **Row 023 of `0000-initial-decisions.md`:** append to the existing ADR-0130 annotation *"**Amended by
   ADR-0183**: `IAuthorizationDecisionPoint` exists and wraps the fab guard."*, keeping *"Originally:"* intact
   (`Every_amended_row_still_shows_what_was_originally_decided`).
4. **Version:** the next minor (1.11.0, or 1.12.0 if ADR-0178's record edits land first), with an
   amendment-history line.

**What the lane builds (#2225 re-enrolled; first slice):**

1. `IAuthorizationDecisionPoint`, `AuthorizationDecision`, `DefaultAuthorizationDecisionPoint` (scope split +
   `IFabAuthorizationGuard`), registered in `AuthenticationDefaults` beside the guard.
2. `ScopeRequirement` + its `IAuthorizationHandler`; `AddScopePolicies` builds policies from it. Policy names
   unchanged.
3. **Phase 4a colour: characterisation for the endpoints, red for the new type.** The existing 401/403 behaviour
   of scoped endpoints is captured green before the change and passes unmodified after
   (`EndpointScopeDeclarationTests`, `LegacyBundleGrantTests`, the scope integration tests). New unit facts for
   the decision point (scope present/absent; fab held/not held/not given) are red first. If the lane cannot
   tell which applies to a given test, it resolves to red.
4. `FoundingDecisionRecordTests` goes green only with the record edit in the same PR (above).

**Later issues, each its own slice, not `agent:ready` until filed:** migrate `FabResolution`; migrate each
direct `EnsureAccessAsync` call; the WHEP hook; webhook delivery authentication; the audit post-query check;
remove `RequireScope`.
