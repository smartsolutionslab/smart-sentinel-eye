# Plan: Spec 282, the reference nobody calls

**Spec:** [spec.md](./spec.md) · **Issue:** #1140 · **Lane:** autonomous (ADR-0144)
**ADRs applied:** 0154 (health), 0125 (connection budget), 0067 (persistence split), 0051 (DI
modules), 0050/0118 (telemetry), 0088 (Wolverine EF transactions). **New ADR: none** (spec §0).

## 1. Shape of the change

This is not a bounded-context feature, so there are no domain entities, value objects,
integration events or migrations. It changes composition only, in three places:

| Layer | Files | Change |
|---|---|---|
| Central packages | `Directory.Packages.props` (contention file, ADR-0109) | Change `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` from `9.5.2` to `13.5.4`, and update its comment. |
| ServiceDefaults | `SmartSentinelEye.ServiceDefaults.csproj`, new `Persistence/PostgresTelemetry.cs` | Add the package reference. Add one helper that is the only caller of `EnrichNpgsqlDbContext` (§3). |
| Nine Infrastructure modules | `src/{AuditObservability,Automation,CameraCatalog,EventIngestion,Identity,LayoutComposition,OverlayDesigner,StreamDistribution,SystemVariables}/Infrastructure/*InfrastructureModule.cs` | One line each in `AddXInfrastructure`, after `AddXPersistence()`. |
| Four Infrastructure csproj | `src/{CameraCatalog,Identity,Automation,EventIngestion}/Infrastructure/*.csproj` | Remove the now-redundant direct `PackageReference`. The package arrives through ServiceDefaults, which all nine already reference. |

**Boundary rules.** The change adds no cross-context reference. ServiceDefaults is already
shared infrastructure that every context references. NetArchTest (`BoundaryTests`) is unaffected.

## 2. Mechanism choice, reconciled with ADR-0125 and ADR-0154

| Option | Verdict | Reason |
|---|---|---|
| `AddNpgsqlDbContext<T>(connectionName)` | **Rejected** | It reads `ConnectionStrings:{name}` itself, which bypasses `GetBoundedPostgresConnectionString` and the pool cap (ADR-0125). `PostgresPoolBoundTests` scans only *our* Infrastructure IL, so it would not see the bypass. It also switches to `AddDbContextPool`, which changes the lifetime model every repository relies on (`IDbContextFactory<T>`). Both are behaviour changes. |
| `EnrichNpgsqlDbContext<T>(settings)` after our own `AddDbContextFactory` | **Chosen** | It leaves registration and the connection string exactly as they are, and it adds only what the settings enable. |
| Skip the component and call `tracing.AddNpgsql()` in `ServiceDefaults.ConfigureOpenTelemetry` | **Not chosen** | Technically the smallest way to get spans. But the owner's decision on #1140 is *"wire it in properly rather than bump or delete"*, restated in the 2026-09-28 re-scope, and the lane does not revisit that. Recorded so a reviewer who prefers it can raise it as a finding. |

**The settings, all fixed in code inside the helper's delegate.** The delegate runs after
configuration binding, so configuration cannot override it:

| Setting | US1 | US2 | Why |
|---|---|---|---|
| `DisableHealthChecks` | `true` | `true` | ADR-0154 clause 2. **Never flips.** |
| `DisableRetry` | `true` | `true` | Preserves `NpgsqlExecutionStrategy`. The retrying strategy refuses Wolverine's user-initiated EF transactions (ADR-0088) and changes the exception shape `OutboxBacklogHealthCheck` handles (ADR-0154 §Context). **Never flips.** |
| `DisableTracing` | `true` | `false` | This is US2's new behaviour. |
| `DisableMetrics` | `true` | `false` | This is US2's new behaviour. Pool metrics make ADR-0125's *"a cap set too low presents as slow"* visible. |
| `CommandTimeout` | unset | unset | Unset preserves Npgsql's default. |

**Facts T001 must observe against the restored 13.5.4 assembly before any wiring.** They come
from the XML docs and IL strings, and nothing has exercised them yet (spec A-1):

1. `EnrichNpgsqlDbContext<T>` accepts a DbContext registered by `AddDbContextFactory<T>` under EF
   Core 10.0.12 and patches its options. The factory must hand out contexts carrying the enriched
   options. **If it throws or silently leaves the options alone, stop: spec S-1.**
2. With `DisableHealthChecks = true`, no `HealthCheckRegistration` is added.
3. With `DisableRetry = true`, the resolved context's `Database.CreateExecutionStrategy()` is
   `NpgsqlExecutionStrategy`.
4. The tracing and metrics toggles are independent of the other two (**spec S-3**).
5. A restore and a Release build move no pin (**spec S-2**). Quote `dotnet list package
   --include-transitive` for Npgsql*, Microsoft.EntityFrameworkCore* and Microsoft.Extensions.*,
   before and after.

## 3. The helper

`src/ServiceDefaults/Persistence/PostgresTelemetry.cs` holds an extension on
`IHostApplicationBuilder`. The name is the engineer's call within ADR-0091, with no
abbreviations. A candidate:

```csharp
public static IHostApplicationBuilder EnrichPostgresDbContext<TDbContext>(this IHostApplicationBuilder builder)
    where TDbContext : DbContext
```

- It guards with `Ensure.That(builder).IsNotNull()` (ADR-0105).
- It calls `builder.EnrichNpgsqlDbContext<TDbContext>(settings => { ... })` with the table
  above.
- Its XML doc carries the *why* for each setting, citing ADR-0154, ADR-0088 and ADR-0125. This is
  the one place the reasoning lives, as `PostgresConnectionBudget` is for the pool.
- **It is not called from `AddWolverineForContext`.** That would hide a persistence concern
  inside messaging wiring. Each module calls it explicitly, one line after `AddXPersistence()`.

The per-context call is `builder.EnrichPostgresDbContext<CameraCatalogDbContext>();` inside
`AddCameraCatalogInfrastructure`, and likewise for the other eight contexts.

## 4. MigrationRunner (ADR-0067)

MigrationRunner calls the nine `AddXPersistence` methods only. The helper is not reachable from
there, so MigrationRunner gains no tracing, metrics, health check or retry strategy. A
characterisation test (T013) proves this rather than leaving it to this sentence.

## 5. Tests

### 5.1 Where they live

| Test | Project | Why there |
|---|---|---|
| Nine-context composition tests (T010, T011, T012, T020, T021) | `tests/Architecture.Tests` | It already references all nine Infrastructure projects, and `KioskPrivilegeSweepRegistrationTests` already composes a module there. |
| MigrationRunner characterisation (T013) | `tests/MigrationRunner.Tests` | It covers the composition root under test. |
| Real span over real Postgres (T022) | `tests/Integration.Tests/ServiceDefaults/` | It needs a live database (ADR-0103). It follows the in-process composition pattern of `KioskPrivilegeSweepStartupIntegrationTests` and reads the fixture's connection string. **A new class needs an entry in a `ci-shards/shard-N.filter`** (memory: a missing entry fails deterministically). |

### 5.2 Composition harness

This follows the precedent in `tests/EventIngestion.Infrastructure.Tests/IngestVolumeRegistrationTests.cs`:

- `Host.CreateEmptyApplicationBuilder(null)`;
- an in-memory configuration holding the keys each module resolves, with syntactically valid,
  never-dialled values;
- `AddXInfrastructure()`;
- `BuildServiceProvider(validateScopes: true)`;
- the host is **never started**, so no hosted service runs and no connection opens.

**The cost, stated.** A config-shape change in any module breaks these tests loudly, so the
harness owns one configuration dictionary per context. Write it once as a shared `[Theory]` data
source keyed by context, not nine copies.

`AddServiceDefaults()` is **not** composed. It lives in each `Program.cs`, and the checks it
registers (`self`, `revocation-snapshot`) are unchanged by this spec. The guard therefore covers
what a module registers, which is where the component's check would land. That limit is stated
in the test's XML doc. Composing the real `Program.cs` is #2571's open question 1, and it is not
decided here.

### 5.3 Characterisation (US1), captured green on unmodified `develop` before T030

- **T010: the execution strategy.** For each context, resolve `IDbContextFactory<T>`, create a
  context, and assert `Database.CreateExecutionStrategy()` is `NpgsqlExecutionStrategy`, exactly
  and not a subclass. It is red under `DisableRetry = false`.
- **T011: the bounded connection string.** For each context, the created context's
  `Database.GetConnectionString()` parses to `MaxPoolSize == PostgresConnectionBudget.MaxPoolSize`
  when the configured string names no pool size. Build the expected value from the constant, not
  from `Bounded(...)`, which is the code under test (memory: *an assertion must not check its own
  input*).
- **T012: the health-check guard (FR-010).** For each context, read
  `IOptions<HealthCheckServiceOptions>.Value.Registrations`. It fails when either of two things
  holds, and the message names the context, the registration and ADR-0154 clause 2:
  - **(a)** the registration's name contains, case-insensitively, a term from a dependency
    vocabulary: `postgres`, `npgsql`, `dbcontext`, `database`, `sql`, `rabbit`, `amqp`,
    `keycloak`, `mqtt`, `mosquitto`, `mediamtx`, `blob`, `azurite`, `redis`, or the name of any
    `DbContext` type registered in the container;
  - **(b)** the instance produced by `registration.Factory(scope.ServiceProvider)` comes from an
    assembly whose name starts with `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`,
    `HealthChecks.` (the AspNetCore.HealthChecks family), `Aspire.` or `Npgsql`.

  The vocabulary is a list, so the test's XML doc says it is a list and why: clause 2 has no
  other checkable form. **Counterfactual (SC-006):** set `DisableHealthChecks = false` in the
  helper, observe it red with both (a) and (b) firing on Aspire's `{TContext}` check (its name is
  the DbContext type name), quote the output, and revert.
- **T013: MigrationRunner.** Compose the nine `AddXPersistence` calls the way
  `src/MigrationRunner/Program.cs:32` onward does. Assert that no
  `HealthCheckRegistration` has been added, and that no `TracerProvider` or `MeterProvider` is
  registered by the persistence path.
- **The existing suites** are part of the characterisation set: `PostgresPoolBoundTests`,
  `PostgresConnectionBudgetTests`, `OutboxBacklogHealthCheckTests`, `DefaultEndpointsTests`, and
  the full integration suite. Phase 4a records their pass counts on `develop` before T030.

Every characterisation test must pass **unmodified** after T030 and after T040. An edited
assertion is a block, not an adjustment (constitution §Testing).

### 5.4 Red (US2), observed failing on the US1 tree before T040

- **T020: tracing registered.** For each context, compose the module and resolve
  `TracerProvider`. Assert that Npgsql's activity source has a listener. Take the source name from
  the restored `Npgsql` assembly (T001 records it), not from a guess. Red while `DisableTracing =
  true`.
- **T021: metrics registered.** The same for the Npgsql meter via `MeterProvider`, using the
  `IngestVolumeRegistrationTests` capturing-exporter shape. Red while `DisableMetrics = true`.
- **T022: a real span (integration).** Compose `AddCameraCatalogInfrastructure` in-process with
  the fixture's real `camera-catalog-db` connection string and a hand-written capturing
  `BaseExporter<Activity>`, in the same shape as `IngestVolumeRegistrationTests`' metric
  `CapturingExporter`. `OpenTelemetry.Exporter.InMemory` is **not** pinned, and adding it is not
  needed. Execute one query through
  `IDbContextFactory<CameraCatalogDbContext>`, then assert that an exported activity carries
  `db.system` (or `db.system.name`) = `postgresql`. Red on the US1 tree.

## 6. Verification (phase 5)

These steps are required, not optional. The issue's history (#1133) is the reason.

1. Release build: `dotnet build -c Release`, with 0 warnings and 0 errors.
2. All unit test projects, with counts quoted.
3. **The full integration suite, all four shards**, quoted per shard. One stack per machine, and
   stop it before building (memory).
4. Boot the stack and run `GET /{context}/health` through the gateway on all nine contexts. Quote
   the word before (on `develop`) and after. Then stop the Postgres container and repeat: the
   answer must still be `Healthy` (SC-004). Restart Postgres afterwards; the volume is shared.
5. On the dashboard, request the camera list through the gateway and find its trace. Record that
   the Postgres span sits under the camera-catalog server span, and add a screenshot or the
   span's attributes. Also record the Npgsql pool metric for one context, and how much Wolverine
   polling noise there is (spec edge case). Do not hunt history with `list_traces search`, which
   is ignored (memory); create the request and look at it.
6. Latency (spec §7): run `IngestThroughputMeasurementTests` on the US1 tree and on the US2 tree,
   twice each, and quote the figures.

## 7. Risks

| Risk | Handling |
|---|---|
| Enrich does not support factory registration under EF 10 | T001, then stop (S-1). |
| Transitive pin drift | T001, then stop (S-2). |
| Fixture breaks as in #1133 | Phase 5 step 3. Retry once, then park (S-4). |
| Wolverine polling floods traces | Observe and record. File an issue if unusable. No filter in this PR. |
| The guard's vocabulary list goes stale | Stated in its doc. The type rule (b) catches library checks whatever their name. |

## 8. Engineers

- **`test-writer`** writes T010 to T013 and T020 to T022, and runs them for the colour evidence.
- **`backend-engineer`** does T001, T030 and T040. The work is a package pin, one helper in
  ServiceDefaults/Persistence, and nine one-line module edits: persistence composition, not AppHost
  or deploy work.
- **`infra-engineer` is not needed.** Nothing touches AppHost, CI workflows, Docker, realm or
  Helm. The shard-filter entry for T022 is a one-line test-selection edit that `test-writer` owns.
- **Reviewers**: `backend-reviewer`. `infra-reviewer` is advisable for the telemetry and package
  major, and optional. `security-reviewer` is not needed, because there is no auth surface. SQL
  text in spans goes to the dev sink only (spec edge case); the reviewer may still note it.
