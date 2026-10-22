# Spec 437 — The decision point every scope asks

**Issue:** #2225 · **ADRs:** ADR-0183 (governing, Accepted), ADR-023 (row 023), ADR-0130, ADR-0144, ADR-0178, ADR-0141, ADR-0105
**Lane:** autonomous (ADR-0144) · **Security-sensitive:** yes — `security-reviewer` mandatory at phase 6 (ADR-0183 §6)

## Premise, re-verified at `2e00b2ff`

| Claim | Observed |
|---|---|
| `IFabAuthorizationGuard` exists, one impl, singleton | `src/ServiceDefaults/Authorization/IFabAuthorizationGuard.cs:37`; registered `AuthenticationDefaults.cs:48`; throws `FabAuthorizationException` |
| `IAuthorizationDecisionPoint` absent in `src/` | `git grep` finds it only in constitution / ADRs / `FoundingDecisionRecordTests.cs:37` |
| `.RequireAuthorization(Scope.Sse.*)` | **52 sites in 16 files** (matches ADR-0183) |
| One policy lambda | `RequireScopeExtensions.cs:38` `RequireAssertion`; the only `RequireAssertion` in `src/` |
| `RequireScope` dead | zero call sites; only doc comments (`RequireScopeExtensions.cs:58`, `Scope.cs:7`) |
| §IX row | constitution line 380: ``**`IAuthorizationDecisionPoint` — absent** (issue 1970)`` |
| Constitution version | **1.11.0** (line 677) → this PR bumps to **1.12.0** |
| Record guard | `FoundingDecisionRecordTests.Section_nine_agrees_with_the_code_about_its_strategy_interfaces` — `recordedAbsent.ShouldBe(!existsInCode, …)` where `recordedAbsent = ReadConstitution().Contains("`IAuthorizationDecisionPoint` — absent")` and `existsInCode = DeclaredInSource("IAuthorizationDecisionPoint")` (comment-insensitive scan of `src/**/*.cs`) |

## User story 1 (P1) — every scope policy is decided by one replaceable seam

As the platform owner, I want every `sse.*` scope policy answered by `IAuthorizationDecisionPoint`, which also
wraps the fab guard, so a v2 policy engine (OPA / Cedar) replaces one implementation instead of 52 call sites (§IX).

### Acceptance scenarios

```gherkin
Scenario: scope held, no fab — allow
  Given an authenticated principal whose scope claim contains "sse.cameras.read"
  When the decision point is asked for scope "sse.cameras.read" with no fab
  Then the decision is Allow and the fab guard is not called

Scenario: scope missing — deny, no throw
  Given an authenticated principal whose scope claim lacks "sse.rules.write"
  When the decision point is asked for "sse.rules.write"
  Then the decision is Deny(MissingScope) and no exception escapes

Scenario: scope held, fab held — allow
  Given the principal holds the scope and group "/fabs/munich"
  When asked for that scope within fab "munich"
  Then the decision is Allow and IFabAuthorizationGuard.EnsureAccessAsync was called once with "munich"

Scenario: scope held, fab not held — deny with the fab reason, no throw
  Given the guard throws FabAuthorizationException for "berlin"
  When asked within fab "berlin"
  Then the decision is Deny(FabNotAuthorized) and no exception escapes

Scenario: scope missing and fab given — scope decides first
  Then the decision is Deny(MissingScope) and the guard is not called

Scenario: bad request — null principal / blank scope
  Then Ensure.That guards throw (ADR-0105) — programming error, not a decision

Scenario (characterisation): HTTP answers unchanged
  Given any of the 52 scoped endpoints
  Then unauthenticated → 401, authenticated without scope → 403, with scope → handler runs — identical to before
```

### Independent end-to-end test

Boot the stack, call one scoped endpoint (e.g. `GET /cameras`) three times — no token (401), token without
`sse.cameras.read` (403), token with it (200) — and observe in the Aspire dashboard / a debugger that the 403 is
produced through the new `ScopeRequirement` handler. Existing scope integration tests pass unmodified.

## Out of scope (ADR-0183 §3–§5, later slices)

Migrating `FabResolution` and the 7 direct `EnsureAccessAsync` calls; the WHEP hook
(`AuthorizeWhepCommandHandler`); `WebhookDeliveryAuthentication.cs:179`; the audit post-query check
(`AuditEndpoints.cs:159`); removing `RequireScope`; §Security bullet and decision row 023 prose (edits 2–3,
human).

## Tech + latency

ServiceDefaults, ASP.NET Core authorization (`IAuthorizationRequirement` / `AuthorizationHandler<T>`),
`Option<T>` from Shared.Kernel. Latency: **N/A in effect** (ADR-0183) — the scope check fronts the event-ingestion
routes on the *event → overlay state* leg but adds one in-memory interface call, no I/O; the leg's existing tests
must pass unmodified.
