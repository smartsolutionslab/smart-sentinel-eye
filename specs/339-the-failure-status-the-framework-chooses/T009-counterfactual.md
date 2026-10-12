# T009 — counterfactual proof for the ten-host theory and the completeness test

Per `tasks.md` T009 and plan §7 step 4 (SC-003, SC-004) plus plan §9's completeness
counterfactual. All three mutations were made locally, the relevant filter run, the
output quoted below, then reverted. After each revert `git diff --stat src/` (mutations
a, b) or `git diff tests/Architecture.Tests/HealthChecks/ServedHealthHosts.cs` (mutation
c) was confirmed empty, the touched file's mtime was bumped (`touch`) to force a
rebuild — a restored file keeps its old timestamp and MSBuild can otherwise skip
recompiling it — and the full `HealthChecks` filter was re-run green (17/17) before
moving to the next mutation.

## (a) SC-003 — `WolverineDefaults.cs:211`, `failureStatus: HealthStatus.Degraded` → `HealthStatus.Unhealthy`

Rebuild (after editing `WolverineDefaults.cs`): `dotnet build tests/Architecture.Tests -c Release`

Command: `dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~Every_health_check_on_a_host_serving_health_declares_a_failure_status_other_than_unhealthy" --no-build`

Result: **exactly the nine context-host cases failed; ApiGateway stayed green** —
ApiGateway composes no outbox registration.

```
Failed!  - Failed:     9, Passed:     1, Skipped:     0, Total:    10, Duration: 5 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

Representative failure (`OverlayDesigner.Api`):

```
Shouldly.ShouldAssertException : offenders
    should be empty but had
1
    item and was
[""outbox-overlay-designer" (failureStatus=Unhealthy, tags=[ready], type=SmartSentinelEye.ServiceDefaults.OutboxBacklogHealthCheck`1[[SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence.OverlayDesignerDbContext, SmartSentinelEye.OverlayDesigner.Infrastructure, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]): failure status is the framework default Unhealthy (ADR-0154 clause 1)"]

Additional Info:
    SmartSentinelEye.OverlayDesigner.Api registers a health check whose failure status is the framework default (Unhealthy), other than the exempt "self" liveness check (ADR-0154 clause 1): "outbox-overlay-designer" (failureStatus=Unhealthy, tags=[ready], type=SmartSentinelEye.ServiceDefaults.OutboxBacklogHealthCheck`1[[SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence.OverlayDesignerDbContext, SmartSentinelEye.OverlayDesigner.Infrastructure, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]): failure status is the framework default Unhealthy (ADR-0154 clause 1)
```

All nine failed the same way — `AuditObservability.Api`, `Automation.Api`,
`CameraCatalog.Api`, `Identity.Api`, `LayoutComposition.Api`, `OverlayDesigner.Api`,
`StreamDistribution.Api`, `SystemVariables.Api`, and the ninth
(`EventIngestion.Api`, not shown above but part of the same 9/10 failure count) —
each naming its own `outbox-{module}` registration. `SmartSentinelEye.ApiGateway`
is the one passing case, confirming it composes no Wolverine outbox check.

## (b) SC-004 — `Extensions.cs:129`, `AddCheck("self", …, ["live"])` → `["ready"]`

Rebuild (after editing `Extensions.cs`): `dotnet build tests/Architecture.Tests -c Release`

Command: `dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~Every_health_check_on_a_host_serving_health_declares_a_failure_status_other_than_unhealthy" --no-build`

Result: **all ten cases failed, every one naming `self`**.

```
Failed!  - Failed:    10, Passed:     0, Skipped:     0, Total:    10, Duration: 830 ms - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

Representative failure (`ApiGateway`, the smallest host — one registration):

```
Shouldly.ShouldAssertException : offenders
    should be empty but had
1
    item and was
[""self" (failureStatus=Unhealthy, tags=[ready], type=Microsoft.Extensions.Diagnostics.HealthChecks.DelegateHealthCheck): tags are not exactly {"live"}"]

Additional Info:
    SmartSentinelEye.ApiGateway registers a health check whose failure status is the framework default (Unhealthy), other than the exempt "self" liveness check (ADR-0154 clause 1): "self" (failureStatus=Unhealthy, tags=[ready], type=Microsoft.Extensions.Diagnostics.HealthChecks.DelegateHealthCheck): tags are not exactly {"live"}
```

Every one of the ten hosts failed identically, naming `"self"` and reporting
`tags are not exactly {"live"}` — confirming the exemption is pinned to `self`'s
shape, and a tag change alone (name and factory type both still match) is enough
to lose it.

## (c) Completeness — drop `EventIngestion.Api` from `ServedHealthHosts`

Rebuild (after editing `ServedHealthHosts.cs`): `dotnet build tests/Architecture.Tests -c Release`

Command: `dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~ServedHealthHostCompletenessTests" --no-build`

Result: **the completeness test failed, naming the dropped host's `Program.cs`**.

```
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 222 ms - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

```
Shouldly.ShouldAssertException : unguarded
    should be empty but had
1
    item and was
["src/EventIngestion/Api/Program.cs (SmartSentinelEye.EventIngestion.Api)"]

Additional Info:
    these Program.cs files call MapDefaultEndpoints and so serve /health, but their assembly is not in ServedHealthHosts: 
src/EventIngestion/Api/Program.cs (SmartSentinelEye.EventIngestion.Api)
HealthCheckFailureStatusGuardTests never composes this host, so ADR-0154 clause 1 is unguarded for it. Add it to ServedHealthHosts.
```

## Revert

All three mutations were reverted immediately after capturing their output.
`git diff --stat src/` was empty after (a) and (b); `git diff
tests/Architecture.Tests/HealthChecks/ServedHealthHosts.cs` was empty after (c).
Each touched file was `touch`-ed before its re-run to force a rebuild (memory:
a restored file keeps its old timestamp). The full `HealthChecks` filter came
back green after every revert, ending at `Passed! - Failed: 0, Passed: 17,
Skipped: 0, Total: 17`.
