# Plan 467 — A Postgres connection to `localhost` goes to the loopback address

Spec: [spec.md](spec.md) · Issue #3186 · ADR-0125, ADR-0067, ADR-0105, ADR-0139, ADR-0144.

## 1. Where it lands

**Not a bounded context.** `src/ServiceDefaults/Persistence/PostgresConnectionBudget.cs`, the
cross-cutting type ADR-0125 made the single source of every context's Postgres connection string. One
file of production code. No Domain/Application/Api layer is involved; no `Shared.Contracts` change; no
AppHost change; no migration; no frontend.

§II (value objects) does not apply: `ServiceDefaults` is infrastructure plumbing that translates a
framework-native string, not a domain model. The method keeps its `string → string` shape.

No messaging: no domain or integration event is raised or consumed.

## 2. Who goes through `Bounded` today (verified by grep, 2026-10-30)

`GetBoundedPostgresConnectionString` is called by:

| Caller | File |
|---|---|
| CameraCatalog | `src/CameraCatalog/Infrastructure/CameraCatalogInfrastructureModule.cs` |
| StreamDistribution | `src/StreamDistribution/Infrastructure/StreamDistributionPersistenceModule.cs` |
| LayoutComposition | `src/LayoutComposition/Infrastructure/LayoutCompositionPersistenceModule.cs` |
| OverlayDesigner | `src/OverlayDesigner/Infrastructure/OverlayDesignerPersistenceModule.cs` |
| SystemVariables | `src/SystemVariables/Infrastructure/SystemVariablesPersistenceModule.cs` |
| EventIngestion | `src/EventIngestion/Infrastructure/EventIngestionPersistenceModule.cs` |
| Automation | `src/Automation/Infrastructure/AutomationPersistenceModule.cs` |
| Identity | `src/Identity/Infrastructure/IdentityPersistenceModule.cs` |
| AuditObservability | `src/AuditObservability/Infrastructure/AuditObservabilityPersistenceModule.cs` |
| Wolverine message store (every service) | `src/ServiceDefaults/WolverineDefaults.cs:69` → `PersistMessagesWithPostgresql` |

`MigrationRunner` registers the same nine `Add<Context>Persistence` modules (`src/MigrationRunner/Program.cs`),
so its migrators are covered — that is where SC-003 is measured.

`EnrichPostgresDbContext` (`PostgresTelemetry.cs`) uses Aspire's `EnrichNpgsqlDbContext`, which enriches
the existing registration and does **not** read `ConnectionStrings:{name}`;
`BoundedConnectionStringCharacterisationTests` pins that (spec 282). No `AddNpgsqlDbContext`,
`AddNpgsqlDataSource`, Marten (`AddMarten` — unused, ADR-0130), or Npgsql health check (`AddNpgSql`)
exists in `src/`.

## 3. Bypasses of `Bounded` (reported, not fixed — out of scope)

1. **The integration-test harness.** `tests/Integration.Tests/Fixtures/AspireFixture.Db.cs` (and ~20
   integration test classes) open their own Npgsql connections from
   `GetConnectionStringAsync(...)` — Aspire's `Host=localhost` string, unbounded, un-rewritten. On the
   measured machine each new physical connection there still costs ~2.1 s. Not product code; a candidate
   follow-up issue if the integration suite's wall time matters.
2. **Design-time factories** (`src/*/Infrastructure/Persistence/DesignTimeDbContextFactory.cs`, nine)
   hard-code `Host=localhost` for `dotnet ef` tooling. Never used at runtime; irrelevant to the issue.
3. **Non-.NET consumers** AppHost wires to Postgres (e.g. pgAdmin in dev) receive Aspire's string
   directly. Not ours to rewrite.

No runtime bypass in `src/` was found.

## 4. Design

Inside `Bounded`, after the existing guard:

```
written  = DbConnectionStringBuilder(connectionString)          // existing: names only what is written
npgsql   = NpgsqlConnectionStringBuilder(connectionString)
explicitCap   = written.ContainsKey("Maximum Pool Size") || written.ContainsKey("MaxPoolSize")   // existing
namesLocalhost = string.Equals(npgsql.Host, Localhost, StringComparison.OrdinalIgnoreCase)

if explicitCap && !namesLocalhost  → return connectionString   // verbatim, as today
if !explicitCap                    → npgsql.MaxPoolSize = MaxPoolSize
if namesLocalhost                  → npgsql.Host = Loopback
return npgsql.ConnectionString
```

- `Localhost = "localhost"`, `Loopback = "127.0.0.1"`: `private const string`. Not public — FR-005, no
  new surface. A literal const rather than `IPAddress.Loopback.ToString()`: it reads as what it is in a
  connection string, and it is what the tests assert against.
- The `Host` comparison goes through `NpgsqlConnectionStringBuilder.Host`, which normalises the
  `Server=` synonym — so the rewrite is keyed on the keyword, not on a spelling.
- Exact match only: `localhost,db2` (multi-host) and `localhost:5432` read as a different value and are
  untouched — FR-002.
- `var` for locals; `Ensure.That` guard already present (ADR-0105); no new parameter, no `Option<T>` question.
- Update the class summary and `Bounded`'s summary: one paragraph naming the measured ~2.1 s / ~80 ms,
  that the cause is unestablished, and that only the literal `localhost` is touched. The type is still
  named for the budget; renaming it is out of scope (smallest change).

**Invariants**
- I1 Output host ∈ {input host, `127.0.0.1`}; it is `127.0.0.1` iff the input host is `localhost` (OIC).
- I2 Output pool size = explicit value if one was written, else `MaxPoolSize` (unchanged from today).
- I3 With an explicit pool size and a non-`localhost` host, output == input byte for byte (unchanged).

## 5. Tests

All in **`tests/ServiceDefaults.Tests/Persistence/PostgresConnectionBudgetTests.cs`**, class
**`PostgresConnectionBudgetTests`** (existing). Unit project — not sharded, so no
`Integration.Tests/ci-shards` filter entry is needed.

Expected **red** on unmodified develop (new behaviour):
- `Localhost_is_rewritten_to_the_ipv4_loopback_address` (also asserts port/database/username/password kept,
  and pool size = `MaxPoolSize`)
- `The_localhost_match_ignores_case` (`LocalHost`, `LOCALHOST`)
- `The_server_synonym_for_host_is_rewritten_too`
- `An_explicit_pool_size_keeps_its_value_and_localhost_is_still_rewritten`
- `The_configured_connection_string_is_rewritten_too` — through `GetBoundedPostgresConnectionString` on a
  `HostApplicationBuilder` (`Host.CreateApplicationBuilder()` + in-memory `ConnectionStrings:audit-db`).

Expected **green already** (they pin the non-match side; legitimately green-on-arrival because today's
code already leaves every host alone — the test-writer should say so in its output, not count them as a
4a failure):
- `A_host_other_than_localhost_is_left_as_written` (Theory: `db.internal`, `127.0.0.1`, `::1`,
  `localhost.localdomain`, `localhost,db2`, `mylocalhost`)
- `An_explicit_pool_size_with_another_host_returns_the_string_verbatim`

Assertions read the host back through `NpgsqlConnectionStringBuilder` and compare to the literal
`"127.0.0.1"` — never to a value computed by `Bounded` (an assertion must not check its own input).

Existing tests that must stay green **unmodified**: every fact in `PostgresConnectionBudgetTests`,
`tests/Architecture.Tests/Persistence/BoundedConnectionStringCharacterisationTests.cs` (its harness already
uses `127.0.0.1`), `tests/Architecture.Tests/PostgresPoolBoundTests.cs`,
`tests/Integration.Tests/ServiceDefaults/PostgresConnectionBudgetIntegrationTests.cs`.

## 6. Verification (phase 5)

- Warm AppHost boot timed **twice before** (on develop) and **twice after**: `migrations` start → exit,
  plus one migrator's "Opening connection" → "Opened connection" span (EF logging at Debug, temporarily).
  Measurement runs need repeating — the first after churn reads like a regression.
- Read one running service's resolved EF connection string host (e.g. via a debug log line or the
  `db.connection_string`-free span attributes — `server.address` on an EF/Npgsql span in the Aspire
  dashboard) and record `127.0.0.1`.
- Record the latency-leg statement from spec §Latency in the PR.

## 7. Constitution / ADR check

- §II: N/A (no domain model). §III: no cross-context reference added. §IV: cited (spec §Latency).
  §VI: Aspire remains the composition root — the injected string is post-processed, not hand-wired.
  §VII: no new leg. §Testing: red first (ADR-0139).
- ADR-0125: extended in its own single place; the budget arithmetic is untouched.
- No new ADR needed.
