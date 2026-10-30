# Verification note — spec 467 / #3186

Phase 5 (ADR-0037). Branch `fix/3186-postgres-localhost-to-loopback` at the implementation commit, Release build, Windows 11 / Docker Desktop, Aspire 13.6.3. 2026-10-30.

## What was observed

With `Host=localhost` rewritten to `127.0.0.1` in `PostgresConnectionBudget.Bounded`, the first connection to each database opens in 0.08–0.87 s instead of 2.1–2.4 s, and every connection the migration runner opened went to `127.0.0.1`. The `migrations` resource, from launch to the services launching, took 17.9 s and 15.7 s on two timed warm boots, against 37 s in the pre-change baseline.

## How

1. Priming boot (not timed): `dotnet run --project src/AppHost -c Release` with the anonymous-dashboard variables, to start the stopped persistent containers on the new build. Then the AppHost was stopped and its containers left running, which is the baseline's warm state.
2. Two timed warm boots (A, B), the same command. Timings from the Aspire resource `creation_timestamp` / `start_timestamp` values and from DCP's captured stdout for `migrations` (timestamped lines).
3. Direct observation of the address: `src/MigrationRunner/appsettings.Development.json` temporarily set to log `Microsoft.EntityFrameworkCore` at Debug (reverted, not committed), `migrations` restarted through Aspire, and the "Opening connection / Opened connection ... on server" lines read from the captured stdout.

## Output (quoted)

Warm boot, pre-change baseline (earlier the same day, same machine, one run):

| Phase | Baseline | Boot A | Boot B |
|---|---|---|---|
| Command → AppHost up (dashboard resource starts) | 43 s | 29.8 s | 30.5 s |
| AppHost up → `migrations` launch | 31 s | 18.5 s | 17.0 s |
| `migrations` launch → services launch | 37 s | 17.9 s | 15.7 s |
| Command → last service (scenario-simulator) | 114 s | 70 s | 66.8 s |

`migrations` process, application start → "MigrationRunner exiting": baseline 12:45:28.8 → 12:45:59.2 (30.4 s); boot A 13:38:03.17 → 13:38:15.46 (12.3 s); boot B 13:41:33.84 → 13:41:44.81 (11.0 s). The nine migrators together: baseline about 25 s; boot A 13:38:08.4 → 13:38:15.3 (6.9 s).

Direct observation, EF Debug log of a restarted `migrations`:

```
--- distinct servers opened:
    241 on server 'tcp://127.0.0.1:29564'
--- first-connection spans ("Opening connection" -> "Opened connection"):
CameraCatalog.      13:43:48.655 -> 13:43:49.040
StreamDistribution. 13:43:50.430 -> 13:43:50.516
LayoutComposition.  13:43:51.602 -> 13:43:52.469
OverlayDesigner.    13:43:53.327 -> 13:43:53.598
SystemVariables.    13:43:54.788 -> 13:43:54.871
EventIngestion.     13:43:55.446 -> 13:43:55.561
Automation.         13:43:55.857 -> 13:43:55.974
Identity.           13:43:56.153 -> 13:43:56.224
AuditObservability. 13:43:56.465 -> 13:43:56.548
```

Before the change the same nine spans were 2.11–2.40 s each (about 19.4 s in total), with the server shown as `tcp://localhost:29564`.

## Service observation (SC-004)

On the final code (HEAD `4e2734c9`), `src/SystemVariables/Api/appsettings.Development.json` was temporarily set to log `Microsoft.EntityFrameworkCore` at Debug (reverted, not committed) and the `system-variables` resource restarted through Aspire. Its captured stdout:

```
--- distinct servers:
     14 on server 'tcp://127.0.0.1:29564'
1731 2026-10-30T13:59:22.620Z   Opening connection to database 'system-variables-db' on server 'tcp://127.0.0.1:29564'.
1733 2026-10-30T13:59:22.621Z   Opened connection to database 'system-variables-db' on server 'tcp://127.0.0.1:29564'.
```

A running service resolves its connection string to `127.0.0.1`, none of its 14 opens named `localhost`. The first open here returned in 1 ms, which is the pool handing out a connection, so this observation says nothing about first-connection latency in a service.

## Latency (constitution §IV)

N/A on the event-to-overlay path: no leg's budget changes in production, because k3s connection strings use DNS names, never `localhost`. On dev and CI it shortens any request that makes a pool open a new physical connection (plan §Latency); pooled connections are unchanged. Not measured on a service's request path.

## What was not covered

- **The two phases this change does not touch also got faster** (command → AppHost up, and AppHost up → `migrations` launch, each by roughly 12–14 s against the baseline). There was one baseline run and it was taken just after a cold boot, so those two differences are not attributed to this change. Only the `migrations` phase is, and it is supported by the per-connection spans above, not by the totals. A control boot of `origin/develop` right now was not run.
- **Only one service was observed (SC-004).** `system-variables` was read on the final code (after the phase 6 fixes), see "Service observation" below. The other services share the changed code path (all nine `Add<Context>Persistence` modules plus the Wolverine store go through `Bounded`) but were not read individually. No service-side connection latency was measured, only the address.
- **`PostgresConnectionBudgetIntegrationTests` (needs the Aspire fixture) was not run here**; it is left to CI.
- **One machine, one OS.** Whether CI or other machines show the 2.1 s cost, and so gain from this, is unknown.
- **The cause of the `localhost` slowness is still not established** (IPv6 failover is a hypothesis); the change avoids it and does not explain it.
- Assumption A: every Postgres that dev and CI point at must answer on IPv4 loopback. It did for Docker Desktop's published ports here.
