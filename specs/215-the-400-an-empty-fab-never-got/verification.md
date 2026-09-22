# Verification — Spec 215 (#2507)

## Phase 4a — RED, premise re-derived from source (not just observed) before any code changed

Before any test was written, two premise checks were done statically rather than assumed:

- **T001 (the empty-fab-produces-500 premise):** confirmed by reading `DefaultFabAuthorizationGuard.EnsureAccessAsync` (`Ensure.That(fabId).IsNotNullOrWhiteSpace()`, unconditional, on the raw string) against `GetTimeline`'s actual code (the guard runs first, before any parse) and the set of five registered `IExceptionHandler`s in `AuthenticationDefaults.cs` (none covers `ArgumentException`), so the throw genuinely falls through to the generic `app.UseExceptionHandler()` catch-all → 500. Three independent points of evidence, all agreeing.
- **T002 (`SearchAuditQueryHandler`'s fab predicate):** confirmed the handler treats a non-null `Fab` as an equality filter via `FabIdentifier.From(fab)` then `.Where(e => e.Fab == fabId)` — so passing an empty string through unwidened would itself throw a second, different `ArgumentException` inside the handler layer. T006's normalisation (`Fab: string.IsNullOrWhiteSpace(fabId) ? null : fabId`) is load-bearing, not optional.

**A third correction, found by the test-writer questioning rather than trusting the plan, then independently re-verified by the orchestrator from source:** spec.md/tasks.md originally classified SC-6 (`?fabId=NOT_A_FAB`) as "characterisation — already answers 400 today." This was wrong. `GetTimeline`'s guard call runs on the raw string before `FabIdentifier.From`'s grammar check is ever reached, and the guard's own groups-membership check rejects `NOT_A_FAB` on its own terms — the actual answer today is **403 `RESOURCE_FAB_NOT_AUTHORIZED`**, not 400. Verified three ways (the guard's implementation, `FabIdentifier.IsValid`'s lowercase-only grammar, and the seeded test operator's actual `/fabs/munich`-only group membership) before `spec.md`, `tasks.md`, and the test itself (name and doc comment; its assertion already correctly targeted the post-fix 400) were corrected. SC-6 is now a fourth red case, not a fourth characterisation case.

Eight new test cases written in `CrossFabReadGuardIntegrationTests.cs` (four red: SC-1, SC-2, SC-8, SC-6; four characterisation: SC-5, SC-7, SC-9, SC-10), asserting both status and problem `title` on every non-200. Build clean, 0 errors; the two pre-existing assertions in the file are untouched (`git diff` confirms purely additive).

## Phase 4a — RED, genuinely captured, via a throwaway CI-only branch

**Two local Aspire boot attempts were made first, both deliberately aborted mid-boot on a worsening RAM trend, not a hard failure:**

1. First attempt (via a dispatched test-writer, before T004's code was even finalized): free RAM measured 6.2GB, judged too risky for a 9-service-plus-Postgres-plus-RabbitMQ-plus-Keycloak stack given this session's history; not attempted.
2. Second attempt (this orchestrator, after T004's tests and the SC-6 correction were committed): free RAM measured 4.3GB. `dotnet run --project src/AppHost` was started and monitored continuously. The AppHost dashboard came up cleanly (build succeeded, 0 errors) at RAM 3.2GB; by the next check it was 2.8GB, then 2.5GB, on the same steep downward trajectory that caused a crisis earlier today (6.7GB → 1.9GB in two minutes, delivering #2432). Aborted at 2.5GB, before any service beyond the dashboard had reported ready. Teardown: AppHost process and two orphaned child `dotnet` processes killed, nine leftover Docker containers stopped explicitly (they outlive the AppHost), `dotnet build-server shutdown` run. RAM recovered to 4.2GB afterward.

**Phase-6 review correctly identified that CI runs in the cloud, not on this resource-constrained machine, and that the red observation was therefore still recoverable** — `origin/develop` plus only the new test file is a valid tree that CI can run for real. Executed:

```sh
git worktree add -b 215-red-capture ../sse-215-red-capture origin/develop
git checkout 8fc0305a -- tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs
# committed, pushed, opened PR #2531 (title: "[throwaway, do not merge] capture
# spec 215's genuine red output"), closed without merging once the log was read
```

**Result: exactly the four predicted cases failed, no more and no fewer** (`Failed: 4, Passed: 624, Total: 628`), each with the exact status mismatch predicted by the static derivation above — including SC-6, whose corrected 403-not-400 premise is now confirmed by a captured live response, not only by source reading:

```
A_empty_fab_on_a_resource_timeline_is_a_client_error [FAIL]
  Shouldly.ShouldAssertException : response.StatusCode
    should be
  HttpStatusCode.BadRequest
    but was
  HttpStatusCode.InternalServerError

A_whitespace_fab_on_a_resource_timeline_is_a_client_error [FAIL]
  Shouldly.ShouldAssertException : response.StatusCode
    should be
  HttpStatusCode.BadRequest
    but was
  HttpStatusCode.InternalServerError

An_empty_fab_on_the_audit_search_spans_the_callers_fabs [FAIL]
  Shouldly.ShouldAssertException : response.StatusCode
    should be
  HttpStatusCode.OK
    but was
  HttpStatusCode.InternalServerError

A_malformed_fab_grammar_now_gets_a_client_error_not_an_authorization_refusal [FAIL]
  Shouldly.ShouldAssertException : response.StatusCode
    should be
  HttpStatusCode.BadRequest
    but was
  HttpStatusCode.Forbidden
```

This is the genuine ADR-0139 evidence this delivery was missing — captured against real, unmodified `origin/develop` code, on real CI infrastructure, not reasoned about. The static derivation earlier in this file predicted every one of these four outcomes exactly (including which specific wrong status each produces), which is itself a second form of confirmation: the reasoning and the observation agree.

**Before merging, the orchestrator will also read the real fix branch's own CI output** and confirm all eight cases pass by name:
- Four red-turned-green: `An_empty_fab_on_a_resource_timeline_is_a_client_error`, `A_whitespace_fab_on_a_resource_timeline_is_a_client_error`, `An_empty_fab_on_the_audit_search_spans_the_callers_fabs`, `A_malformed_fab_grammar_now_gets_a_client_error_not_an_authorization_refusal`.
- Four characterisation, unmodified: `A_cross_fab_timeline_is_refused_before_a_malformed_resource_is_parsed`, `An_omitted_fab_keeps_the_frameworks_own_refusal`, `A_cross_fab_audit_search_is_still_refused`, `An_unauthenticated_empty_fab_request_is_challenged`.

Phase 5's live end-to-end procedure (five probes, run by hand against a booted stack) still owes this delivery a genuine hand-driven observation of the fix and its problem-detail bodies — the captured red output above proves the *test suite's* red-then-green, not a person's own curl against the running endpoint. Retry when the delivery machine has more headroom, or accept the real fix branch's own CI test run (which exercises the identical HTTP paths phase 5's probe table would) as sufficient live confirmation of the green state — a human decision to make explicitly rather than one the lane assumes.

Latency: **N/A** — not on any constitution §IV leg.
