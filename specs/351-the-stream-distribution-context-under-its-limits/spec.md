# Spec 351: The stream distribution context under its limits

**Feature Branch**: `refactor/2893-stream-distribution-metrics` (cut from `origin/develop` at `8ded100e`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked explicitly (§6).

**Input**: Issue [#2893](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2893),
*Clear the ADR-0084 metric warnings in Stream Distribution's Domain, Application and Infrastructure (12)*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green").

**Security-sensitive context.** Stream Distribution owns the WHEP authorization decision
(`AuthorizeWhepCommandHandler`, `WhepAuthValidator`: action-before-token refusal, token validation,
revocation, scope, fab scoping per ADR-0161), MediaMTX path provisioning through a gateway that opts
into `RetryEveryMethod()` (ADR-0143) and the shared control-API secret (ADR-0171). Every change here
is shape-only; the characterisation pins in plan §4 exist to prove that no refusal moved, no
precedence flipped and no retry opt-in lost its reason. Phase 6 includes a **security-reviewer**
pass (plan §7).

**Spec number.** 351. Checked on 2026-10-14: `origin/develop` highest 348; every remote branch via
`git ls-tree` highest 349 (`refactor/2891-system-variables-metrics`, PR #2906); worktree `sse-2892`
holds unpushed 350 (#2892); open PRs #2811 and #2837 carry no spec. Highest visible 350, so 351.
Re-check before the PR is opened.

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17: S104 <= 300 lines/file, S138 <= 30
lines/method, S107 <= 4 parameters, S1541 <= 10, S134 depth <= 3; advisory `warning`), ADR-0144
(autonomous lane; phase 4a colours; the lane may not weaken a gate), ADR-0036 (smallest change),
ADR-0050 (`[LoggerMessage]` fields come from the template), ADR-0143 (retry safety;
`RetryEveryMethod` opt-ins keep their reason at the call site), ADR-0161 (fab scoping of the WHEP
decision), ADR-0160 (revoked clients), ADR-0171 (MediaMTX control-API secret), ADR-0141 (`Option<T>`
for absences in Application), ADR-0051 (per-context DI module), ADR-0113 (concurrency; the version
bump race of #2783 is pinned, not touched), ADR-0103 (Aspire fixture, no Testcontainers).

---

## 1. What was measured (premise verified, not assumed)

The issue's command does not run as written (`src/StreamDistribution` holds no project file). The
equivalent, which compiles Domain and Application transitively:

```sh
dotnet build src/StreamDistribution/Infrastructure/SmartSentinelEye.StreamDistribution.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep StreamDistribution
```

Result on `8ded100e`: 0 errors, **12 hits, exactly the 12 the issue lists, at the same lines. None
is stale.** No hit sits in `Migrations/`. No S104 or S134 hit exists in this context's non-Api layers.

Baseline test counts on `8ded100e` (`dotnet test -c Release`): StreamDistribution.Domain.Tests 141,
StreamDistribution.Application.Tests 86, StreamDistribution.Infrastructure.Tests 77, all green.

## 2. Decision per hit

| # | File:line | Member | Rule (measured) | Decision |
|---|---|---|---|---|
| 1 | `Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs:27` | `HandleAsync` | S138 (39) | **Clear** by extraction |
| 2 | same `:27` | `HandleAsync` | S1541 (12) | **Clear** by the same extraction |
| 3 | `Application/Commands/Handlers/RepointStreamCommandHandler.cs:15` | `HandleAsync` | S138 (36) | **Clear** by extraction |
| 4 | `Application/Commands/Handlers/ReportStreamHealthCommandHandler.cs:14` | `HandleAsync` | S138 (34) | **Clear** by extraction |
| 5 | `Application/Log.cs:57` | `StreamProvisioned` `[LoggerMessage]` | S107 (5) | **Leave**: parameters are the structured template's fields (ADR-0050) |
| 6 | `Domain/Stream/Stream.cs:68` | `Stream.Provision` (public) | S107 (5) | **Leave**: public Domain factory with 10 call sites (1 production in `ProvisionStreamCommandHandler`, 9 in tests incl. Integration.Tests); a parameter object changes every caller and breaks "tests unmodified" (J1) |
| 7 | `Infrastructure/Auth/WhepAuthValidator.cs:123` | `ValidateAsync` | S138 (48) | **Clear** by extraction (synchronous helpers only, J2) |
| 8 | `Infrastructure/Log.cs:29` | `ReconcilerStartupPassComplete` `[LoggerMessage]` | S107 (5) | **Leave**: ADR-0050, as #5 |
| 9 | `Infrastructure/Persistence/Configurations/StreamConfiguration.cs:17` | `Configure` | S138 (71) | **Clear** by extraction |
| 10 | `Infrastructure/Reconciler/MediaMtxReconciler.cs:132` | `ReconcileOnceAsync` (public) | S138 (54) | **Clear** by extraction (public signature unchanged, J3) |
| 11 | `Infrastructure/StreamDistributionInfrastructureModule.cs:38` | `AddStreamDistributionInfrastructure` | S138 (55) | **Clear** by extraction (public signature unchanged) |
| 12 | same `:109` | `AddMediaMtxGateway` (internal, called by tests) | S138 (33) | **Clear** by extraction (signature unchanged; `RetryEveryMethod()` and its reason stay in it) |

**Cleared: 9** (hits 1-4, 7, 9-12: 8 S138 and the S1541 that rides on hit 1).
**Left: 3** (2 `[LoggerMessage]`, 1 public Domain factory). Expected after-count: **3**, every one
named in the PR body with its reason.

## 3. User story

### US1 (P1): Stream Distribution's non-Api layers meet ADR-0084 wherever that is mechanical, with every WHEP refusal and provisioning order pinned first

As a maintainer, I want the 9 clearable hits gone, each by an extraction, **without** any change a
MediaMTX hook call, a WHEP viewer, the health watcher, another context, a log reader or the database
could observe.

**Why P1 / independently shippable.** It is the whole issue: one context, no contract, no
migration, no runtime resource, no Api file.

#### Acceptance scenarios

Characterisation (all must hold **before** the refactor and pass **unmodified** after):

```gherkin
Scenario: Every existing Stream Distribution test passes unmodified (happy path)
  Given StreamDistribution.Domain.Tests (141), .Application.Tests (86), .Infrastructure.Tests (77)
  When the refactor commits land
  Then all pass and `git diff <last 4a commit>..HEAD -- tests/` is empty

Scenario: The WHEP decision refuses in today's order (auth, security)
  Given a request failing more than one check
  Then the first check today wins: action absent/unrecognised -> publish/api -> blank token
       -> token refused (realm down vs token rejected) -> missing read scope -> wrong or absent fab
       -> offline stream -> admitted
  And the validator is not called for a blank token, and the stream is not looked up for a
      token without the read scope
  And an unregistered path with a valid read-scoped token is admitted

Scenario: The fab refusal names the caller, the path and the stream's fab (conflict, security)
  Given a read-scoped subject "subject-a" holding fab "munich" and a stream in fab "dresden"
  Then the answer is WHEP_FAB_NOT_AUTHORIZED and the warning text names all three distinct values

Scenario: A forwarded token is judged in today's order (bad request, security)
  Given a validly signed token with no "sub" whose "azp" the revocation registry refuses
  Then the answer is TokenRejected and the registry is never consulted and nothing is logged as revoked
  Given a revoked azp
  Then the revocation warning names that azp exactly
  Given a valid token with scope "  a  b c" and two fab groups
  Then the subject carries "a","b","c" in that order and both fabs in claim order

Scenario: Repointing saves before it touches MediaMTX (conflict)
  Then the order is: lookup -> save -> gateway repoint; an unparsable address refuses before the lookup
  And a missing stream or a retired stream answers success with nothing repointed, each with its own log text

Scenario: A health report applies today's precedence (conflict)
  Given DeclareOffline=true and an observation that is ready
  Then the stream goes Offline
  And absent LastError text falls back to today's two literal defaults
  And a rejected transition answers STREAM_INVALID_STATE_TRANSITION with today's from/to/reason and does not save
  And an exception from SaveAsync propagates unchanged (the #2783 race is pinned, not handled)

Scenario: The startup reconcile pass removes, then re-adds, and reports its counts (dependency failure)
  Given configured paths with one orphan whose removal fails and one that succeeds,
    and two missing streams whose re-add fails for one
  Then every removal is attempted before any re-add, failures do not stop the pass,
    and the summary log names distinct configured/expected/removed/readded counts

Scenario: The DI composition is unchanged
  Then AddStreamDistributionInfrastructure registers the same Stream Distribution services, in the same order,
    with the same lifetimes, and the MediaMTX client and the camera-catalog token client each still opt into
    RetryEveryMethod exactly once

Scenario: The EF model is unchanged
  Then StreamDistributionDbContext's logical model, read through the public metadata API, equals the baseline captured on 8ded100e
```

**Auth scenario.** No authorisation boundary moves: no Api file, no scope, no `RequireScope`, no
rate limiter, no `WhepAuthorizeActionPartition` is touched. The decisions inside the handler and the
validator (action, token, revocation, scope, fab) are pinned above, not changed.

#### Independent test procedure (end to end)

1. The `dotnet test` runs of plan §5, green before and after.
2. The grep of §1 prints exactly the 3 "Leave" rows after the change.
3. CI's Integration.Tests `StreamDistribution/*` green, notably `WhepAuthIntegrationTests`,
   `StreamFabScopingIntegrationTests`, `MediaMtxReconcilerIntegrationTests`,
   `RepointStreamIntegrationTests`, `StreamHealthTransitionTests`, `MediaMtxControlApiAuthIntegrationTests`,
   `WhepHandshakeLatencyTests` and the new reconciler characterisation (plan C7).

## 4. Functional requirements

- **FR-001** The 9 "Clear" hits of §2 report nothing after the change, and no new S104/S107/S138/
  S1541/S134 hit appears in a touched file. If an extraction leaves a hit, the PR says so with the
  measured value; it is never suppressed.
- **FR-002** No public, `internal` or `protected` signature changes: every `ICommandHandler`
  implementation and constructor, `IWhepAuthValidator`, both `WhepAuthValidator` constructors,
  `MetadataSourceFor`, `CreateParameters`, `MediaMtxReconciler`'s public members,
  `AddStreamDistributionInfrastructure`, `AddMediaMtxGateway`, `BindWhepAuthOptions`,
  `Stream.Provision`, everything in `Shared.Contracts` and every `Log.cs` partial.
- **FR-003** For every input: the order of checks, gateway calls, repository calls and saves; each
  `try` block's extent and each `catch` filter; error values, codes and messages; log calls (method,
  arguments, order) are identical.
- **FR-004** No helper extracted from a per-viewer-connect member (`AuthorizeWhepCommandHandler.HandleAsync`,
  `WhepAuthValidator.ValidateAsync`) introduces an `await`, an allocation, a lock or I/O that the
  member did not already perform: those helpers are synchronous and return `Option<T>`/`Result<T,E>`
  (both `readonly struct`).
- **FR-005** `RetryEveryMethod()` on the MediaMTX typed client and on the camera-catalog token client
  stays at its call site with its stated reason; the MediaMTX client's Basic-auth header (ADR-0171)
  is built exactly as today; no new log call, field or exception message carries the control-API
  secret or a bearer token.
- **FR-006** DI registrations keep their order and lifetimes; the EF configuration produces the same model.
- **FR-007** No Domain file changes (constitution §II).
- **FR-008** No suppression, `#pragma`, threshold, `.editorconfig` or `SonarLint.xml` change; the
  existing `#pragma warning disable/restore S1075, S5332` in `BindStreamFabAttribution` is not
  edited; no guard edited; no file under any `Api/` or `Migrations/` directory touched; no existing
  test file edited. The only non-test, non-production file touched is
  `tests/Integration.Tests/ci-shards/shard-2.filter` (one appended entry for C7).

## 5. Success criteria

- **SC-001** Before/after hit lists quoted in the PR body; after = the 3 "Leave" rows.
- **SC-002** Characterisation observed green on unrefactored code, green and unmodified after; both
  outputs quoted.
- **SC-003** `dotnet build -c Release` 0 errors; `dotnet format --verify-no-changes` clean on the
  Application and Infrastructure projects; coverage gates hold (Domain >= 90 %, Application >= 80 %).
- **SC-004** Phase 6 security-reviewer pass with no unresolved finding.
- **SC-005** `WhepHandshakeLatencyTests` p95 quoted from the PR's CI run (trx artifact), next to the
  last green `develop` figure; no regression beyond run-to-run noise.

## 6. Judgement calls (marked)

- **J1. Leaving the public Domain factory.** `Stream.Provision` could take a `ProvisioningRequest`
  value object, but every caller, tests included, would change. "Tests unmodified" decides it.
- **J2. Synchronous extraction on the WHEP path.** `WhepAuthValidator.ValidateAsync` keeps its only
  `await` (the metadata fetch) and the realm-transition logging in place; the token validation and
  claim reading move into synchronous private methods called from where the code sits today, inside
  the same `try`. `AuthorizeWhepCommandHandler.HandleAsync` keeps both of its `await`s (validator,
  repository); the action refusals and the stream refusals move into synchronous helpers returning
  `Option<AuthorizeWhepError>`. Nothing on a viewer-connect gains a state machine.
- **J3. The reconciler's anonymous projection stays.** `ReconcileOnceAsync`'s query keeps its
  anonymous-type projection (its comment records why `ValueTuple.Create` does not translate). The
  re-add loop receives the rows as `streams.Select(entry => (entry.Path, entry.SourceUrl))`, a lazy
  projection over the materialised list, iterated once in the same order; one iterator allocation per
  startup pass, on no §IV leg.
- **J4. The reconciler is characterised at integration level.** `ReconcileOnceAsync` reads Postgres
  through `IDbContextFactory<StreamDistributionDbContext>`, and no test project references an
  in-memory EF provider; adding one would be a new package for a refactor. C7 lives in
  Integration.Tests (Aspire fixture, Postgres only, a recording gateway) and is observed green before
  the refactor as plan §4 describes.

**No bug fix is folded in.** Four oddities were found and are **pinned or left as-is, not changed**:

1. `BindWhepAuthOptions`'s doc says "Same shape as `BindMediaMtxApiOptions` directly above", but
   `BindStreamFabAttribution` already sits between them on `8ded100e`. Comment drift; the refactor
   must not move either method, and the fix is a separate change.
2. `WhepAuthValidatorResolutionTests`' doc cites `StreamDistributionInfrastructureModule.cs:57` for
   the validator registration, which sits at line 62 on `8ded100e` and will move again. Test text;
   tests are not edited here. Follow-up candidate.
3. `ReportStreamHealthCommandHandler` lets a `DbUpdateConcurrencyException` from `SaveAsync` escape,
   which is the health-watcher side of board issue #2783 (a retire can lose a version race to the
   ~2 s `ReportHealthy` sweep). Pinned as-is; deciding it is #2783's job.
4. `HandlerDeconstructionTests`' regex matches only `var (a, b) = x;`, so the `(var a, var b) = x;`
   lines in `AuthorizeWhepCommandHandler` and `ReportStreamHealthCommandHandler` are not checked by
   it. Not a defect in this context (the bindings are correct today); both lines stay verbatim.

## 7. Latency budget impact (constitution §IV)

**No §IV leg is touched.** Camera -> SFU (<= 80 ms) and SFU -> kiosk decode (<= 120 ms) are media
legs that MediaMTX and the browser carry; nothing this spec edits sits in the media path.
`AuthorizeWhepCommandHandler.HandleAsync` and `WhepAuthValidator.ValidateAsync` run once per
viewer-connect (click-to-first-frame, spec 002's p95 <= 3 s bound measured by
`WhepHandshakeLatencyTests`), before any frame flows; FR-004 still forbids adding `await`,
allocation, lock or I/O there. `ReportStreamHealthCommandHandler` runs on the periodic ~2 s health
sweep, the reconciler once at startup, the DI module and EF configuration at boot. Before/after
evidence: `WhepHandshakeLatencyTests` p95 from CI (SC-005), compared against the last green
`develop` run, read from the trx artifact.

## 8. Out of scope

Every `Api/` file (already split by merged #2896), migrations, tests' own metrics, `ServiceDefaults`
hits shown by the same build, the 3 left hits, the four oddities of §6, and #2783.

## 9. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052, ADR-0054), sentence-style names (ADR-0053),
`Ensure.That` guards (ADR-0105), `Option<T>` for absences in Application (ADR-0141), Aspire fixture
for integration (ADR-0103), `var` wherever it compiles, collection expressions, fluent DI chains, one
parameter per line when wrapping, no leading underscores (CLAUDE.md house rules).
