# Spec 282: The reference nobody calls

**Feature Branch**: `chore/1140-aspire-npgsql-wiring` (cut from `origin/develop` at `f484b51b`)

**Created**: 2026-09-28 (first draft, on `chore/2642-aspire-npgsql-major`, commit `daa8a267`)
**Revised**: 2026-09-29, for #1140's re-scope

**Status**: Draft. Phase 1 gate. No `[NEEDS CLARIFICATION]` remains: the first draft's Q1 was
answered by the repository owner's comment on #1140 (2026-09-28), which chose option A.

**Input**: Issue [#1140](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/1140),
*Wire in Aspire.Npgsql properly (health checks + OTel), rather than bumping a dead reference*.
The body is scoped as of 2026-08 and **its scope is superseded** by the comment of 2026-09-28.
That comment re-scopes the issue, closes #2642 as a duplicate, and makes #1140 this spec's owner.
Where the body and the comment disagree, this spec follows the comment. §2 lists each point where
they differ.

**Lane.** Autonomous (ADR-0144). #1140 carries `agent:ready`, and its card is on Project #13 in
*In Progress*. Its `agent:blocked` label was removed because its gate, #2125, closed on
2026-09-17 (ADR-0154).

**Spec number.** 282 was reserved on 2026-09-28 for the first draft. That draft lives only on
the unmerged branch `chore/2642-aspire-npgsql-major`, and no PR was ever opened from it. This
directory replaces it. **Re-check the number before opening the PR.** `origin/develop` has
281 and 283 but no 282 (memory: *spec number: origin/develop isn't enough*).

**ADRs and constitution sections referenced:**

- **ADR-0154**, readiness is liveness. Clause 2 bars any `/health` check that names or reaches an
  external dependency. Clause 3 routes dependency outages to telemetry instead.
- **ADR-0125**, the Postgres connection budget. It uses `GetBoundedPostgresConnectionString` and
  counts two pools per service.
- **ADR-0067**, MigrationRunner. It is the reason for the `AddXPersistence` / `AddXInfrastructure`
  split.
- **ADR-0051**, per-context DI modules.
- **ADR-0050** and ADR-0026, OpenTelemetry through MEL and OTLP.
- **ADR-0118**, one sink per environment. The Aspire dashboard is the only sink.
- **ADR-0103**, the Aspire fixture, no Testcontainers.
- **ADR-0088**, Wolverine EF transactions and the outbox.
- **ADR-0109**, contention files. `Directory.Packages.props` is one.
- **ADR-0087**, each commit builds on its own.
- **ADR-0034**, warnings are errors in Release.
- **ADR-0139** and constitution §Testing, characterisation versus red.
- **ADR-0144**, the lane implements decisions and does not make them.
- Constitution §IV (see §7) and §Availability, as amended by ADR-0154.

**No new ADR is needed. This spec applies two decisions that already exist:**

- **ADR-0154** decides the health-check question. The component's check is switched off.
- **ADR-0125** decides the connection question. The bounded connection string stays the only
  source. That is why `EnrichNpgsqlDbContext` is used and `AddNpgsqlDbContext` is not (plan §2).

Nothing here changes a decision. §6 lists what would turn this spec into one that needs a
human, and the implementation must stop if any of those appear.

---

## 1. What was measured

The first-draft figures were taken on `edf41b97`. They were re-verified on `f484b51b` on
2026-09-29, and every figure below comes from a command that was run.

| Claim | Checked with | Result |
|---|---|---|
| Package pinned at 9.5.2 | `Directory.Packages.props:37` | **Holds.** |
| Rest of the Aspire family on 13.5.4 | `Directory.Packages.props:25,28,32,33,81,82` | **Holds.** `Aspire.Hosting.NodeJs` is gone because spec 283 replaced it with `Aspire.Hosting.JavaScript` 13.5.4, so this package is now the **only** Aspire package off the 13.x line. The body's "13.4.6" is stale. |
| Newest 13.x | nuget flat-container index | **13.5.4.** |
| Referenced by 4 Infrastructure projects | `grep Aspire.Npgsql src tests --include=*.csproj` | **Holds**: CameraCatalog, Identity, Automation, EventIngestion. |
| Its API is called nowhere | grep for `AddNpgsqlDbContext`, `EnrichNpgsqlDbContext`, `AddNpgsqlDataSource` | **Holds.** There are zero call sites in `src/` and `tests/`. |
| All contexts hand-roll the registration | grep for `AddDbContextFactory<` | **All nine** use `AddDbContextFactory<T>(o => o.UseNpgsql(builder.GetBoundedPostgresConnectionString(...)))` in their `*PersistenceModule` (or, for CameraCatalog, its `*InfrastructureModule`). The five contexts without the package register exactly like the four with it. |
| Registered `/health` checks | `ServiceDefaults/Extensions.cs:129`, `WolverineDefaults.cs:187`, `AuthenticationDefaults.cs:216` | There are three names: `self` (tag `live`), `outbox-{module}` (`ready`, `Degraded`) and `revocation-snapshot` (`ready`, `Degraded`). **None names a dependency.** |
| DB time in traces | `ServiceDefaults/Extensions.cs:87-108` | Tracing subscribes to the application source, Wolverine, ASP.NET Core and HttpClient. **Nothing subscribes to Npgsql**, so a request's database time is a gap in its trace. |

**What 13.5.4 is.** These facts come from the published `.nuspec` and the net10.0 assembly's XML
docs and IL string table. The nupkg was downloaded and unpacked on 2026-09-29. Nothing was built
or restored (A-2).

| | 9.5.2 | 13.5.4 |
|---|---|---|
| Target frameworks | net8.0, net9.0. **No net10.0 asset.** | net8.0, net9.0, **net10.0** |
| `Npgsql.EntityFrameworkCore.PostgreSQL` floor | 9.0.4 | 10.0.0. We pin 10.0.3. |
| `Npgsql.OpenTelemetry` / `Npgsql.DependencyInjection` | 9.0.3 | 10.0.2. Transitive pinning lifts them to our `Npgsql` 10.0.3. |
| `Microsoft.Extensions.*` floor | 9.0.9 | 10.0.11. We pin 10.0.12 where we pin at all. |
| `OpenTelemetry.Extensions.Hosting` floor | 1.9.0 | 1.15.3. We pin 1.19.1. |

**`EnrichNpgsqlDbContext<TContext>` in 13.5.4** has the signature
`(IHostApplicationBuilder, Action<NpgsqlEntityFrameworkCorePostgreSQLSettings>?)`. Its documented
behaviour is *"Configures retries, health check, logging and telemetry"*. It throws if the
DbContext is not already registered. It binds settings from configuration section
`Aspire:Npgsql:EntityFrameworkCore:PostgreSQL[:{TContext}]` **before** the delegate runs, so a
value set in code wins over configuration. The settings are `DisableHealthChecks`,
`DisableTracing`, `DisableMetrics` and `DisableRetry`, all defaulting to `false`, plus
`CommandTimeout` and `ConnectionString`. The IL references `AddDbContextCheck`, `AddNpgsql`
(tracing), `AddMeter`, `EnableRetryOnFailure` and `NpgsqlRetryingExecutionStrategy`.

**So the component's defaults would change behaviour in two places this system depends on:**

1. **Health.** `AddDbContextCheck` registers a check that reaches Postgres. That violates
   ADR-0154 clause 2.
2. **Retries.** `EnableRetryOnFailure` swaps EF's `NpgsqlExecutionStrategy` for the retrying
   strategy. That strategy refuses user-initiated transactions, and Wolverine's EF transactions
   (ADR-0088, `UseEntityFrameworkCoreTransactions`) are user-initiated. It also changes which
   exception shape `OutboxBacklogHealthCheck` sees (ADR-0154 §Context).

Both are switched off in code (FR-003, FR-004).

## 2. How this differs from #1140's body

| #1140 body (2026-08) | Now | Why |
|---|---|---|
| Wire it in for its **Npgsql health checks**, so *"a degraded DB connection should surface on `/health`"* | **Health checks explicitly off.** | ADR-0154 clause 1 says `/health` is not a readiness signal for shared infrastructure. Clause 2 bars a check that names or reaches one. |
| Acceptance: *"Health endpoints actually report Postgres state, verified by observation"* | **Inverted.** It is verified by observation that `/health` does **not** change: same answer before and after, and still `200 Healthy` with Postgres stopped (ADR-0154 row 3). | Same. Kept as an observed criterion (SC-004) rather than dropped, so the reversal is on the record. |
| *"Aspire-native connection-string handling via `EnrichNpgsqlDbContext`, instead of re-reading configuration by hand"* | The connection string is **still** read through `GetBoundedPostgresConnectionString`. `EnrichNpgsqlDbContext` does not read one, so nothing changes there. | ADR-0125. `AddNpgsqlDbContext` would read `ConnectionStrings:{name}` itself and bypass the pool cap. `PostgresPoolBoundTests` scans only our Infrastructure IL, so it would not notice. Plan §2. |
| Bump `9.5.2 → 13.4.6` | Bump to **13.5.4.** | That is the current family line (§1). |
| Four referencing modules, and "decide" about the other five | **All nine** contexts (FR-006). | See §5. |
| Keep the `AddXPersistence` / `AddXInfrastructure` split | **Kept.** The enrichment goes in `AddXInfrastructure` only. | ADR-0067. MigrationRunner calls only `AddXPersistence`. |

## 3. User Scenarios & Testing

The "user" is the **maintainer or operator** of a 24/7 system. That is the person reading
`Directory.Packages.props`, a Release build, `/health`, or a trace on the Aspire dashboard.

### User Story 1: The component is on the family line and wired, and nothing observable changes (Priority: P1, characterisation)

A maintainer finds `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` on 13.5.4, referenced once
from `ServiceDefaults`, and called by every context's infrastructure module. The call is
configured so that every behaviour a running service had before is unchanged:

- the same registered health checks and the same `/health` answer;
- the same EF execution strategy;
- the same bounded connection string;
- the same MigrationRunner registration;
- no new telemetry yet.

**Why this priority**: it is the whole of #2642. It removes the net9.0 assembly from net10.0
projects, and it is the mechanism US2 switches on. Keeping it behaviour-preserving means that a
regression here cannot be mistaken for a US2 effect.

**Independent Test**: capture the characterisation tests (plan §5, T010 to T013) green on
unmodified `develop`. Make the change. Then check four things:

- the same tests pass **unmodified**;
- the Release build is clean;
- the full integration suite is green;
- `GET /{context}/health` returns the same word on all nine contexts before and after.

**Acceptance Scenarios**:

```gherkin
Feature: Aspire.Npgsql wired without changing behaviour

  Scenario: The bump and the wiring change nothing observable  (happy path)
    Given the package pinned at 13.5.4, referenced only by ServiceDefaults
    And every context's AddXInfrastructure enriching its DbContext through the shared helper
    When the solution is built in Release with warnings as errors
    Then the build has 0 warnings and 0 errors
    And every characterisation test captured green on develop passes unmodified
    And the full integration suite passes
    And GET /health on each of the nine context services returns the same answer as before

  Scenario: The bump would drag a pinned EF Core, Npgsql or Microsoft.Extensions version  (conflict)
    Given a restore that resolves a pinned package to a version other than its pin
    Then the change is held back with the verbatim restore output
    And no other pin is moved to accommodate it

  Scenario: The component is wired with its default settings  (bad request)
    Given EnrichNpgsqlDbContext called with health checks or retries left at their defaults
    When a context's composed health-check registrations are read
    Then a check that reaches the database is present, and the guard fails naming it
    When the context's DbContext execution strategy is resolved
    Then it is the retrying strategy, and the characterisation test fails naming it

  Scenario: Configuration tries to switch the health check back on  (bad request)
    Given Aspire:Npgsql:EntityFrameworkCore:PostgreSQL:DisableHealthChecks = false in configuration
    When the context is composed
    Then no database health check is registered, because the setting is fixed in code

  Scenario: The MigrationRunner path is untouched
    Given only AddXPersistence is called, as MigrationRunner does
    Then the DbContext and IMigrator are registered
    And no Npgsql tracing, metrics or health registration is added

  Scenario: Auth surface
    Given this change touches no endpoint, scope, token or realm
    Then no authorization test is added or altered
```

The auth scenario is stated rather than left out. This feature has no auth surface, and saying
so is the check.

---

### User Story 2: Database time is visible in traces, and a dependency health check cannot sneak back (Priority: P2, red)

An operator opens the trace for a request that reads the database, on the Aspire dashboard. The
database span sits under the request span with its duration, where before there was a gap. The
Npgsql pool metrics are also on the dashboard, which makes the ADR-0125 budget visible: a pool
running at its cap shows up there instead of only as latency. And if a later change registers a
health check that names or reaches a dependency on any context, a test fails and names the
check.

**Why this priority**: it is the value #1140 still has after ADR-0154. It is P2 because it is an
observability gain rather than a correctness fix, and because ADR-0118 means it reaches only the
dev/CI sink today.

**Independent Test**:

- The unit-level registration tests are **red** on the US1 tree, where tracing and metrics are
  still disabled, and green after.
- The integration trace test issues one real query through a composed CameraCatalog module
  against the fixture's Postgres. It captures a `db.system` = `postgresql` span that is **absent**
  on the US1 tree.
- Phase 5 observes the span on the dashboard for a camera-list request through the gateway.

**Acceptance Scenarios**:

```gherkin
Feature: Database spans and pool metrics, with no dependency health check

  Scenario: A database read is traced  (happy path)
    Given the stack running with tracing enabled through the component
    When a camera list is requested through the gateway
    Then the request's trace contains a Postgres span as a descendant of the camera-catalog request span

  Scenario: Every context exports database telemetry
    Given each of the nine contexts composed through its AddXInfrastructure
    Then its tracer provider listens to Npgsql's activity source
    And its meter provider listens to Npgsql's meter

  Scenario: A health check naming or reaching a dependency is registered  (conflict)
    Given any context's composed health-check registrations
    When one registration's name matches the dependency vocabulary, or its check type reaches a dependency
    Then the guard fails, naming the context, the registration and ADR-0154 clause 2

  Scenario: The guard would catch the component's own check  (counterfactual)
    Given DisableHealthChecks temporarily set to false in the shared helper
    Then the guard is red, and the failure is quoted in the PR
    And the helper is restored before commit

  Scenario: Auth surface
    Given the new telemetry is exported only to the configured OTLP sink
    Then no endpoint, scope or token path is added, and no authorization test changes
```

### Edge Cases

- **Transitive pinning.** `CentralPackageTransitivePinningEnabled` is on. 13.5.4's floors are all
  at or below the pins, but only a restore proves that (A-2).
- **The fixture.** An adjacent Aspire bump broke the fixture once while the build, the unit
  suites and e2e all stayed green: #1133, where 54 of 75 tests failed on `UntrustedRoot`. **The
  integration suite is the acceptance signal, and the build is not.** Phase 5 must run all four
  shards, not only the new tests.
- **Enrich against a factory registration.** Every context uses `AddDbContextFactory`. Enrich's
  documented precondition is a registered DbContext, and whether it patches a *factory*
  registration's options under EF Core 10 has not been verified. T001 checks it. If it does not,
  the fix is either `AddDbContext` / `AddDbContextPool` (a behaviour change) or forgoing the
  component entirely. Either one is a decision, so the run **blocks** (§6).
- **Wolverine's own pool is traced too.** `AddNpgsql()` subscribes to Npgsql's process-wide
  activity source, so Wolverine's durability polling on its separate `NpgsqlDataSource` will
  emit spans as well. That is expected, and it is noise on the dashboard (memory: *a 10 s
  Wolverine poll floods the window*). Phase 5 records how much noise. Filtering it is **out of
  scope**. If it makes traces unreadable, file an issue rather than building a filter here.
- **Configuration override.** Settings bound from `Aspire:Npgsql:...` run before the code
  delegate, so the code wins. The bad-request scenario in US1 checks that rather than assuming it.
- **SQL text in spans.** Npgsql's spans carry the statement text (`db.statement` /
  `db.query.text`) with parameters as placeholders, not values. The sink is the dev/CI dashboard
  only (ADR-0118). This is recorded here so a future production sink meets it deliberately.

## 4. Requirements

### Functional Requirements

- **FR-001** `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` is pinned at **13.5.4** in
  `Directory.Packages.props`. No `.csproj` gains a `Version=`.
- **FR-002** No other package pin moves as a consequence. If one would have to, the change is
  held back with the verbatim restore failure (spec 272's held-back precedent).
- **FR-003** No health check that names or reaches Postgres, or any other external dependency,
  is registered on any context's host (ADR-0154 clause 2). The component's check is disabled
  **in code**, where configuration cannot re-enable it.
- **FR-004** The EF execution strategy stays `NpgsqlExecutionStrategy`, the non-retrying one. The
  component's retry is disabled in code, and `CommandTimeout` is left unset.
- **FR-005** Every DbContext's connection string still comes from
  `GetBoundedPostgresConnectionString` and still carries `Maximum Pool Size=20` (ADR-0125). The
  component's own connection-string resolution (`AddNpgsqlDbContext`) is **not** used.
- **FR-006** All **nine** contexts are enriched, through **one** helper in
  `ServiceDefaults/Persistence`. That helper is the only place the four settings are written. The
  package reference moves from the four Infrastructure `.csproj` files to
  `SmartSentinelEye.ServiceDefaults.csproj`.
- **FR-007** The enrichment is called from each context's `AddXInfrastructure`, never from
  `AddXPersistence`. The MigrationRunner's registration is unchanged (ADR-0067).
- **FR-008** *(US2)* Npgsql tracing is enabled on all nine contexts, so a database command
  produces a span in the request's trace.
- **FR-009** *(US2)* Npgsql metrics are enabled on all nine contexts.
- **FR-010** *(US2)* An automated guard composes each of the nine contexts' infrastructure and
  fails if any registered health check's name matches a dependency vocabulary, or if its check
  type comes from a dependency-probing assembly (plan §5.4). It is proven by counterfactual.
- **FR-011** Each commit builds on its own (ADR-0087). The US1 bump and the reference move land
  together, because a commit that removes the reference from the Infrastructure projects before
  ServiceDefaults gains it does not build.

### Key Entities

Not applicable. There are no data model changes and no migrations.

## 5. The five contexts without the package

AuditObservability, LayoutComposition, OverlayDesigner, StreamDistribution and SystemVariables
are **brought in**. They are not left out and justified. There are four reasons:

1. **Their registration is identical.** §1 found no difference between the four contexts that
   reference the package and the five that don't. The difference is an accident of which
   projects were scaffolded when, not a design, so there is no "why they differ" to record.
2. **A trace crosses contexts.** A request that shows database time in CameraCatalog and a gap in
   LayoutComposition is harder to read than one with no database spans at all. A partial
   rollout would make the dashboard look broken.
3. **The guard is only worth having if it covers every context.** A guard that composes four of
   nine hosts leaves the other five exactly as unguarded as ADR-0154 found them.
4. **One helper makes nine call sites cheap.** This mirrors how ADR-0125 made
   `GetBoundedPostgresConnectionString` the one place the pool rule lives. The package then has
   one reference, in `ServiceDefaults`, which every Infrastructure project already references.

## 6. Assumptions and stop conditions

- **A-1. No release notes were read.** The API facts in §1 come from the 13.5.4 assembly's XML
  docs and IL strings, not from a changelog. Plan §2 names the facts T001 must re-observe
  against the restored assembly (memory: *a plan's SDK claim needs checking against the pinned
  version*).
- **A-2. No build or restore probe exists yet.** The claim that no pin moves is a prediction from
  metadata. T001 replaces it with an observed restore and Release build.
- **A-3. The health-check guard is not red on `develop`.** No violating check exists there, so a
  new guard arrives green. The owner's comment lists it under US2 ("new behaviour, red"). This
  spec classifies it as **characterisation plus counterfactual**, and puts it before the US1
  wiring, because it is US1's safety net for FR-003. It is observed green on `develop`, observed
  **red** with the component's health check switched on, and both outputs are quoted. The DB span
  and metrics tests are genuinely red. See the colour table in `tasks.md`.
- **A-4. The guard does not discharge #2571.** #2571 asks for a guard on `failureStatus` (no
  check may default to `Unhealthy`), and it is held for a human decision on what "otherwise
  accounted for" means. This spec's guard covers ADR-0154 **clause 2** only, which is the part
  #1140's re-scope explicitly asks for. It composes each module in-process, following the
  `IngestVolumeRegistrationTests` precedent, so it needs no `HostFactoryResolver`. The PR links
  #2571 and states that its questions 1 and 2 remain open.
- **A-5. The existing `outbox-{module}` check queries Postgres.** Read literally, that sits
  awkwardly with clause 2's word "reach". ADR-0154 itself describes that check and keeps it, and
  its row 3 makes the check return `Healthy` when the database is unreachable. This spec does not
  re-litigate it. The guard's type rule targets dependency-probing *library* checks (EF's
  `DbContextHealthCheck`, `AspNetCore.HealthChecks.*`), not ServiceDefaults' own
  ADR-0154-reviewed checks. That wording choice is surfaced here for the reviewer.

**Stop conditions. The run blocks with `agent:blocked` rather than deciding:**

- **S-1.** `EnrichNpgsqlDbContext` rejects or ignores a factory registration (§3 edge case).
- **S-2.** A pin would have to move (FR-002).
- **S-3.** Tracing cannot be enabled without also enabling the health check or the retry. For
  example, a 13.5.4 behaviour that couples the settings.
- **S-4.** The integration suite goes red and the cause is the fixture rather than the change,
  the #1133 shape. That is retried once, then parked.

## 7. Latency budget (constitution §IV)

**US1: N/A.** The component is wired with every feature disabled. No leg changes.

**US2: the *event → overlay state* leg (≤ 200 ms).** EventIngestion's write and its outbox
commit share one transaction (`OutboxSharesTheWritesFateTests`), so database commands sit on this
leg. Npgsql tracing adds one `Activity` per command while a listener is attached. The predicted
cost is microseconds against a 200 ms leg, but that is a prediction, not a figure. §IV records
this leg as *recorded, not yet readable*, so there is no baseline figure to protect. Phase 5
therefore runs `IngestThroughputMeasurementTests` before and after US2, **twice each**, and
quotes the figures (memory: *measurement runs need repeating*). This spec changes no cell of the
§IV table.

## 8. Success Criteria

- **SC-001** Every Aspire package in `Directory.Packages.props` is on the 13.x line.
- **SC-002** The Release build has 0 warnings and 0 errors. Every unit test project passes. **The
  full integration suite (all four shards plus the unsharded selections CI runs) passes on the
  final tree.**
- **SC-003** Every characterisation test captured green on `develop` passes **unmodified** after
  US1 and after US2.
- **SC-004** *(Observed on the running stack.)* `GET /{context}/health` through the gateway
  returns the same word on all nine contexts before and after. With the Postgres container
  stopped, it still returns `200 Healthy` (ADR-0154 row 3). This replaces the body's *"health
  endpoints report Postgres state"*.
- **SC-005** *(Observed on the Aspire dashboard.)* A camera-list request's trace shows a Postgres
  span under the camera-catalog request span. Npgsql connection-pool metrics appear for at least
  one context. Both are observed, not inferred from registration.
- **SC-006** The health-check guard is observed green on the final tree, and observed red with
  `DisableHealthChecks = false`. Both outputs are quoted in the PR.
- **SC-007** #1140 closes via a closing keyword in the PR, and its state is checked after the
  merge. #2642 is already closed as its duplicate.
