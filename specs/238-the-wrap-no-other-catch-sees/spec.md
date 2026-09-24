# Spec 238 — The wrap no other catch sees

**Issue**: #2435 (feature-level; no per-task issues) · **Branch**: `fix/2435-dbexception-wrap-audit`
**Status**: Phase 1 — ready for review · **Lane**: autonomous (ADR-0144)
**ADRs**: ADR-0154 (readiness is liveness by choice — its Consequences name this audit as an open
follow-up), ADR-0036 (smallest change), ADR-0139 (the two test obligations), ADR-0144 (lane).
Neighbour: spec 172 (fixed the one known site; its T007 follow-up was folded into #2435's comment).
**Latency budget**: N/A — health-check and error-classification paths; not on the event→overlay
path (constitution §IV).
**No new ADR.** This spec audits against a decision already made (ADR-0154) and changes no
production behaviour.

---

## 1. The mechanism, restated precisely

`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`Directory.Packages.props:54`) registers
`NpgsqlExecutionStrategy` even with no `EnableRetryOnFailure` (none exists anywhere in `src/`).
Its `ExecuteAsync` catches any exception for which `NpgsqlTransientExceptionDetector.ShouldRetryOn`
answers true — after `ExecutionStrategy.CallOnWrappedException` unwraps a `DbUpdateException` — and
rethrows it inside a plain `InvalidOperationException` ("…likely due to a transient failure").

"Transient" is wider than "unreachable". It covers:

- every **connection failure** (`NpgsqlException` with an `IOException` / `SocketException` /
  `TimeoutException` inner) — refused and timed-out alike (#2435 comment, item 1);
- **server-reported transient SQLSTATEs** on a `PostgresException` — per Npgsql's
  `PostgresException.IsTransient`: class 53 (`53300` too many connections, …), `57P01` admin
  shutdown, `57P03` cannot connect now, `40001` serialization failure, `40P01` deadlock, class 08, and
  a few more. **Assumption A1** (to be proven against the pinned DLL by §5's tests, not trusted from
  memory): `57P03` is transient and `42P01` is not.

It does **not** cover non-transient SQLSTATEs (`23505` unique violation, `23514` check violation,
`42P01` undefined table, …) or `DbUpdateConcurrencyException` (an EF row-count failure with no
provider exception). Those reach application code unwrapped.

So the question for each site is two-sided:

1. **Blind spot** — does it classify on `DbException`/`NpgsqlException`/SQLSTATE and so miss the
   wrapped shape?
2. **Inverse blind spot** — does it catch `InvalidOperationException` (to translate a domain rule)
   around code that can hit the database, and so misread a wrapped outage as a rule violation?

---

## 2. The audit (re-verified 2026-09-24 on this branch, cut from `origin/develop`)

Searched `src/**/*.cs` for: `DbException`, `NpgsqlException`, `PostgresException`, `SqlState`,
`PostgresErrorCodes`, `InnerException`, `DbUpdateException`, `IsTransient`, `EnableRetryOnFailure`,
`ExecutionStrategy`, `TimeoutException`, `SocketException`, `is … Exception` pattern tests,
`typeof(…Exception)`, **every** `catch (…)` clause, Wolverine `OnException` policies, and every
health-check registration. Re-measure with:

```sh
grep -rnE 'DbException|NpgsqlException|PostgresException|SqlState|IsTransient|EnableRetryOnFailure|ExecutionStrategy' --include=*.cs src | grep -v '/obj/'
grep -rnE 'catch \(InvalidOperationException|and not InvalidOperationException|or InvalidOperationException' --include=*.cs src
grep -rnE 'AddHealthChecks|\.AddCheck|AddTypeActivatedCheck|AddDbContextCheck' --include=*.cs src
```

### 2.1 Sites that classify a database exception (blind-spot candidates)

| # | Site | Classifies on | Wrapped shape reachable? | Decision |
|---|---|---|---|---|
| S1 | `src/ServiceDefaults/OutboxBacklogHealthCheck.cs:129,154,164` | `DbException` + empty SQLSTATE = unreachable | Yes | **Fix confirmed in place** (spec 172): `catch (InvalidOperationException ex) when (ex.InnerException is DbException inner && IsUnreachable(inner))` at `:129`, raw `DbException` arms kept at `:154,:164`. One residual shape — see §3. |
| S2 | `src/ServiceDefaults/Persistence/UniqueConstraintExceptionHandler.cs:135-141` | SQLSTATE `23505`, bare and `DbUpdateException`-wrapped | No — `23505` is not transient, so never wrapped | **Safe as-is.** Not outage detection. Premise pinned by §5 T2 and exercised end-to-end by the existing 409 integration tests. |
| S3 | `src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs:554-560` `IsMissingPartition` | SQLSTATE `23514` | No — not transient | **Safe as-is.** Selects a log line only; the enclosing `catch (Exception)` at `:445` returns `Ending.Failed` for both shapes. `FabPartitionProvisioningIntegrationTests` exercises it. |
| S4 | `src/ServiceDefaults/Persistence/ConcurrencyConflictExceptionHandler.cs:41` | `is DbUpdateConcurrencyException` | No — no provider exception, `ShouldRetryOn` false | **Safe as-is.** Not outage detection. |

**No site other than S1 detects a database outage by exception type.** The issue's narrow grep was
right by accident: S1 was the only one.

### 2.2 Broad catches around database work (type-agnostic — both shapes caught identically)

`catch (Exception …) when (… is not OperationCanceledException)` or unfiltered, so a wrapped
`InvalidOperationException` is caught exactly as a raw `DbException` would be. **Safe as-is**, all:

- `PersistenceLoopHostedService.cs:323, 358, 409, 445` (ack, dead-letter, batch, single store)
- `EventIngestion/Api/EventsEndpoints.Writes.cs:366` (503 `EVENT_NOT_STORED`)
- `EventIngestion/Infrastructure/Persistence/EventRepository.cs:115` (dispatch aggregation)
- `ServiceDefaults/Idempotency/IdempotentRequest.cs:173` (bare `catch` + rethrow), `:206`
- `ServiceDefaults/Idempotency/IdempotencyReservationSweep.cs:92`
- `AuditObservability/Application/Retention/AuditRetentionHostedService.cs:174`
- `MigrationRunner/MigrationRun.cs:69`
- `SystemVariables/Infrastructure/Resolution/ReverseIndexSeederHostedService.cs:112`
- `Automation/Infrastructure/Cache/RuleCacheSeederHostedService.cs:49`
- `StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs:45`,
  `…/Attribution/StreamFabAttributionService.cs:40`, `…/Reconciler/MediaMtxReconciler.cs:42`
- `Identity/Infrastructure/KeycloakAdmin/KioskPrivilegeSweepHostedService.cs:42`

### 2.3 `InvalidOperationException` catches (inverse-blind-spot candidates)

| Site | What the `try` covers | Decision |
|---|---|---|
| `CameraCatalog/…/RenameCameraCommandHandler.cs:73`, `…/ChangeCameraAddressCommandHandler.cs:53` | the in-memory aggregate call only; `SaveAsync` is outside | **Safe as-is** |
| `Automation/…/PublishRuleCommandHandler.cs:43` | `rule.Publish(clock)` only | **Safe as-is** |
| `StreamDistribution/…/ReportStreamHealthCommandHandler.cs:46` | aggregate transitions only | **Safe as-is** |
| `Automation/…/DryRunRuleQueryHandler.cs:133-137` | AEL evaluation only; the rule read is before the `try` | **Safe as-is** |
| `EventIngestion/…/MqttSubscriberHostedService.cs:220`, `StreamDistribution/…/WhepAuthValidator.cs:119` | parsing / JWKS; no database | **Safe as-is** |
| `Identity/…/RotateWebhookClientCommandHandler.cs:139` | `SaveAsync` **and** Keycloak calls; filter **excludes** `InvalidOperationException` | **Safe as-is for outages** — a wrapped outage escapes to the global 500 path rather than being reported as `KEYCLOAK_UNAVAILABLE`, which is the *correct* attribution. See §4 for an unrelated observation on this site. |

### 2.4 Nets and registrations

- No `EnableRetryOnFailure`, no `CreateExecutionStrategy`, no Wolverine `OnException` policy in `src/`.
- Health-check registrations: exactly two — `ServiceDefaults/Extensions.cs:127-129` (`self`, `live`,
  default `failureStatus`) and `ServiceDefaults/WolverineDefaults.cs:173-177` (`outbox-{module}`,
  `ready`, `failureStatus: Degraded`). `AddMinioClient` (`CommunityToolkit.Aspire.Minio.Client`
  13.5.0, `AuditObservabilityInfrastructureModule.cs:84`) registers none — its assembly contains no
  health-check type or string.

---

## 3. The residual shape in S1, accepted explicitly

A **transient `PostgresException` that carries a SQLSTATE** — `57P03` while Postgres starts or
recovers, `53300` at the connection limit, `57P01` on an administrative shutdown — is wrapped (§1),
and then:

- `:129` does not match: `IsUnreachable(inner)` is false because the SQLSTATE is non-empty;
- `:154` and `:164` do not match: the thrown type is `InvalidOperationException`.

It escapes the check. `DefaultHealthCheckService` then resolves it through the registration's
`failureStatus: HealthStatus.Degraded` (`WolverineDefaults.cs:175`) → **`Degraded`, HTTP 200** —
ADR-0154 table row 4 ("check throws"). The raw, unwrapped form of the same exception would have
reached `:164` and also returned `Degraded`. **The observable contract is identical; only the
description and the `data` dictionary differ.**

**Decision: accept, do not widen.** Reasons:

1. The safety net the issue asks about exists and is ADR-0154's own row 4.
2. Widening would force a classification — is "cannot connect now" an *unreachable* database
   (row 3, `Healthy`) or an *unreadable backlog* (`Degraded`)? Both are 200, so the choice is purely
   about what the check reports, and it is a reading of ADR-0154 the autonomous lane may not make
   (ADR-0144). Recorded here so it is met, not rediscovered.
3. The acceptance is **pinned by a test** (§5 T1), so it cannot change silently in either direction —
   neither by a provider upgrade nor by a well-meant widening.

---

## 4. Out of scope, recorded so it is not lost

**O1 — suspected misattribution in `RotateWebhookClientCommandHandler.cs:139` (not an outage issue,
unverified).** The `try` holds `clients.SaveAsync` and the filter catches everything except
`OperationCanceledException` and `InvalidOperationException`. So the *unwrapped* database failures —
the ADR-0113 Layer-2 loser's `DbUpdateConcurrencyException` (the comment at `:84-92` expects it to
"pick a winner" and the loser to see 409), or any non-transient `DbUpdateException` — would be
returned as `KEYCLOAK_UNAVAILABLE` instead of reaching `ConcurrencyConflictExceptionHandler`. No test
races two rotations at Layer 2 (`RegisteredClientConcurrencyIntegrationTests` covers Layer 1 only).
**Action: T006 files it as its own issue after checking the board.** Not fixed here: it is a
different defect with its own red test to write.

**O2 — the composed health-check registration guard (#2435 comment, item 2).** Deferred to its own
issue (T005). Why it is not a "write a test like `AppHostReplicaCountTests`" slice:

1. **The AppHost model cannot see it.** `AppHostReplicaCountTests` reads
   `DistributedApplicationTestingBuilder.Resources` — the AppHost's resource graph. Health-check
   registrations live in each *service's* DI container (`IOptions<HealthCheckServiceOptions>`), which
   the AppHost model does not contain. The requested mirror would observe nothing and pass vacuously.
2. **A booted Aspire stack cannot show it either.** The services run as separate processes; `/health`
   writes one word (spec 078, ADR-0154 clause 2) and nothing else exposes registrations.
3. **Reading the real composition in-process needs a harness decision.** Either (a) replicate each
   `Program.cs`'s `Add…` calls in a test — a copy that drifts, i.e. a guard over the design artefact;
   or (b) capture each of the ten real `Program` entry points at `Build()` without starting them — the
   `Microsoft.Extensions.HostFactoryResolver.Sources` mechanism EF tooling and `Mvc.Testing` use —
   which adds a test dependency not in `Directory.Packages.props`, plus per-host placeholder
   configuration for ten hosts.
4. **"Otherwise accounted for" needs a stated policy.** `self` carries the framework-default
   `failureStatus` (`Unhealthy`) today and is correct to (it cannot throw). The guard needs an
   explicit exemption rule, and ADR-0154 clause 2 ("no check may *name or reach* an external
   dependency") is not mechanically checkable from a registration at all.

(3) and (4) are choices, and the lane implements decisions rather than making them.

---

## 5. User stories

### US1 (P1) — The audit is recorded and its premises are executable

*As a maintainer*, I want every database-failure classification in `src/` audited against the
wrapped shape, with the one accepted gap pinned by a test, *so that* the next provider upgrade or
well-meant edit that changes the picture fails a build instead of being rediscovered at a site.

**Acceptance scenarios** (all against `OutboxBacklogHealthCheck<TDbContext>` driven directly, with a
connection interceptor raising a constructed `PostgresException` — no Postgres needed):

```gherkin
Scenario: T1 — a transient refusal carrying a SQLSTATE escapes the check (the accepted gap, pinned)
  Given a DbContext whose connection opening throws PostgresException with SQLSTATE 57P03
  When the outbox backlog check runs
  Then it throws InvalidOperationException
  And the InnerException is that PostgresException with SqlState "57P03"

Scenario: T2 — a non-transient refusal is not wrapped and is reported as an unreadable backlog
  Given a DbContext whose connection opening throws PostgresException with SQLSTATE 42P01
  When the outbox backlog check runs
  Then the result is Degraded
  And Data["error"] is "PostgresException"
```

The existing `A_database_that_cannot_be_reached_is_reported_healthy` remains the happy/unreachable
path and must stay green unmodified. Conflict / bad-request / auth scenarios: **N/A** — no endpoint,
no command, no authorisation surface changes.

**Why these two and no more.** T1 pins the only accepted gap (§3). T2 pins the premise S2 and S3
rest on — that a non-transient SQLSTATE is *not* wrapped — against the pinned provider, cheaply, in
the one place that already has the harness. Both also prove assumption A1.

### Independent end-to-end test procedure

`dotnet test tests/ServiceDefaults.Tests --filter FullyQualifiedName~OutboxBacklogHealthCheckTests`
— three tests green on unchanged production code. Plus the two counterfactuals in plan §4, each
observed red and reverted.

---

## 6. Requirements

- **FR-001** No production file changes. (If §5's tests contradict the audit, stop — see FR-004.)
- **FR-002** Add T1 and T2 to `tests/ServiceDefaults.Tests/OutboxBacklogHealthCheckTests.cs`; each
  assertion message states what a failure *means* (T1: the §3 acceptance was reversed or the provider
  changed; T2: non-transient errors are now wrapped, so S2 and S3 are blind).
- **FR-003** Each new test is proven by counterfactual (plan §4) before it counts as a guard.
- **FR-004** Colour: **characterisation, observed green**. A test that arrives red is a finding
  that an audit premise is false — report it verbatim and stop; do **not** change production code to
  make it green.
- **FR-005** File O1 and O2 as issues (board-checked first) and link them from the PR.

## 7. Assumptions

- **A1** — `57P03` is transient and `42P01` is not in the pinned Npgsql (§1). Proven or refuted by T1/T2.
- **A2** — an exception thrown from `DbConnectionInterceptor.ConnectionOpeningAsync` is raised inside
  `NpgsqlExecutionStrategy.ExecuteAsync`, as a real connection failure is. If T1 sees a raw
  `PostgresException`, the harness does not reproduce the wrap: stop and report rather than improvise.
