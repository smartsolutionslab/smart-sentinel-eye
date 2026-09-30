# T031 — counterfactual proof for T012 and T010

Per `tasks.md` T031. Both settings were flipped locally in
`src/ServiceDefaults/Persistence/PostgresTelemetry.cs`, the relevant test run,
the output quoted below, then reverted (confirmed by `git diff` showing no
changes and both suites green again at 18/18).

## `DisableHealthChecks = false` — T012 (`DependencyHealthCheckGuardTests`)

Command: `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~DependencyHealthCheckGuardTests" -c Release`

Result: **9 failed, 0 passed** — every one of the nine contexts fires both
rule (a) (name) and rule (b) (type):

```
Failed!  - Failed:     9, Passed:     0, Skipped:     0, Total:     9, Duration: 1 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

Representative failure (`automation`):

```
Shouldly.ShouldAssertException : offenders
    should be empty but had
1
    item and was
["automation:"AutomationDbContext" (Microsoft.Extensions.Diagnostics.HealthChecks.DbContextHealthCheck`1[[SmartSentinelEye.Automation.Infrastructure.Persistence.AutomationDbContext, SmartSentinelEye.Automation.Infrastructure, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]], name rule=True, type rule=True)"]

Additional Info:
    automation registers a health check that names or reaches an external dependency (ADR-0154 clause 2): automation:"AutomationDbContext" (Microsoft.Extensions.Diagnostics.HealthChecks.DbContextHealthCheck`1[[SmartSentinelEye.Automation.Infrastructure.Persistence.AutomationDbContext, SmartSentinelEye.Automation.Infrastructure, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]], name rule=True, type rule=True). A /health check is a liveness signal, not a readiness probe for shared infrastructure; a dependency outage must route to telemetry instead, never to this endpoint.
```

All nine contexts (`automation`, `system-variables`, `event-ingestion`,
`audit-observability`, `layout-composition`, `camera-catalog`,
`stream-distribution`, `overlay-designer`, `identity`) failed the same way —
`registration.Name` equals the `DbContext` type name (rule a) and
`instance.GetType().Assembly` is `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`
(rule b). This is Aspire's own `AddDbContextCheck`, registered by
`EnrichNpgsqlDbContext` the moment `DisableHealthChecks` is not `true`.

## `DisableRetry = false` — T010 (`ExecutionStrategyCharacterisationTests`)

Command: `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~ExecutionStrategyCharacterisationTests" -c Release`

Result: **9 failed, 0 passed** — every context now resolves the retrying
strategy instead of the non-retrying one:

```
Failed!  - Failed:     9, Passed:     0, Skipped:     0, Total:     9, Duration: 2 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

Representative failure (`audit-observability`):

```
Shouldly.ShouldAssertException : dbContext.Database.CreateExecutionStrategy().GetType().FullName
    should be
"Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy"
    but was
"Npgsql.EntityFrameworkCore.PostgreSQL.NpgsqlRetryingExecutionStrategy"
...
Additional Info:
    audit-observability's DbContext must resolve the non-retrying NpgsqlExecutionStrategy. The retrying strategy (NpgsqlRetryingExecutionStrategy) refuses Wolverine's user-initiated EF transactions (ADR-0088, UseEntityFrameworkCoreTransactions) and changes the exception shape OutboxBacklogHealthCheck handles (ADR-0154 §Context). Aspire's EnrichNpgsqlDbContext enables it by default via EnableRetryOnFailure unless DisableRetry is set, which is why this must stay true.
```

## Revert

Both settings were restored to `true` immediately after capturing the output
above. `git diff src/ServiceDefaults/Persistence/PostgresTelemetry.cs` showed
no changes, and re-running both filters together came back
`Passed! - Failed: 0, Passed: 18, Skipped: 0, Total: 18`.
