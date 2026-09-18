# Verification 181 — A gate that knows its resource set (#2268)

**Latency: N/A.** CI infrastructure, not on the event→overlay path.

## Phase 4a — red (ADR-0139), quoted verbatim from commit `980ecdbf`

```
✖ a stack whose status report names a resource that never started is not reported ready (#2268) (2242.0526ms)
  AssertionError [ERR_ASSERTION]: the gate opened over a stack missing minio (#2268):
  ... every existing probe (migrations, three ports, gateway 401) reports healthy ...
  (actual: 0, expected: 0, operator: 'notStrictEqual')

✖ a stack with no status report for the whole wait window is not reported ready (#2268) (1994.9977ms)
  AssertionError [ERR_ASSERTION]: the gate opened with no status report at all (#2268)

✖ a one-shot resource that ended non-zero fails the gate immediately (#2268) (2157.1871ms)
  AssertionError [ERR_ASSERTION]: the gate opened over a stack whose migrations resource exited 1 (#2268)

tests 6, pass 3, fail 3
```

All three fail for the right reason: the script had no notion of `STACK_STATUS_FILE` at all, so it always proceeded through the existing probes and exited 0. The `AppHostStackStatusTests.cs` facts were observed failing the same way — `NotImplementedException` from a placeholder, and a missing `StackStatusFile=` argument in `ci.yml`.

## T001 — live-boot spike, observed

Wired `AfterResourcesCreatedEvent` and `IHostApplicationLifetime.ApplicationStarted` side by side (throwaway, not committed) against a real `dotnet run --project src/AppHost -- ScenarioSimulator=false StackStatusFile=...` boot:

- Non-terminal states appear live: the first notification batch showed `NotStarted` for several `-rebuilder` companions and an unset state for `system-variables` before it flipped to `Running` — the watch is live, not a final snapshot.
- Hook timing: both fired 4.3ms apart (`10:08:42.2577342Z` vs `10:08:42.2620575Z`) — picked `AfterResourcesCreatedEvent` as Aspire's purpose-built hook, not the incidental coincidence.
- Full distinct state set across 145 notifications: `(empty/unset)`, `NotStarted`, `Running`, `Finished` (exit 0 for `migrations`).
- **Every one of the 145 notifications read `withoutLifetime=False`**, including all 10 secret parameters (`PostgresPassword`, `KeycloakPassword`, `IdentityAdminClientSecret`, etc.). Decompiling the pinned `Aspire.Hosting.dll` (13.5.3) confirmed nothing in that assembly implements `IResourceWithoutLifetime` — plan.md §2.2's stated filter would have excluded zero resources. **Corrected to `resource is not ParameterResource`**, verified against all 10 `builder.AddParameter(...)` calls in `AppHost.cs` and confirmed there is no `AddConnectionString`/`ConnectionStringResource` anywhere in this AppHost or this Aspire version.

## T007 — live boot against the corrected implementation

Seed (written immediately, before any resource started) — 31 lines, all `(not reported)`, zero parameter names, zero `-rebuilder` companions. Final report after "Distributed application started" — 31 lines, every one `Running` or `Finished 0`, matching the live composition exactly (11 `AddProject` services + `migrations` + `api-gateway`, minus 11 `-rebuilder` companions and 10 `ParameterResource`s).

Gate run against the real stack: resource-gate section's own wall-clock **~5s** (stack already fully up). Whole script ~501s in this ad-hoc manual boot, entirely the pre-existing gateway probe's own best-effort buffer (the gateway wasn't answering 401 in this manual run) — unrelated to and unaffected by this change.

**The counterfactual** — edited the live file's `minio` line from `Running` to `FailedToStart`, re-ran:

```
$ STACK_STATUS_FILE="$PWD/scratch-status.tsv" bash scripts/wait-for-e2e-stack.sh
Waiting for the AppHost's composed resource set to start ...
::error::a composed resource ended without starting — minio(FailedToStart)
EXIT CODE: 1
```

Non-zero, immediate (no wait budget spent), names minio and nothing else. Reverted; confirmed `minio Running` restored.

## Second finding, also caught live: the `-rebuilder` companions

Every `AddProject<T>` resource (11 of them) gets an Aspire-built-in `<name>-rebuilder` companion, seeded at `NotStarted` and never started unless someone invokes the dashboard's Rebuild command — confirmed by decompilation (`ProjectResourceBuilderExtensions.AddRebuilderResource`, `ExplicitStartupAnnotation` + `WithHidden()`). Without excluding these, the gate would time out on every single boot, including a fully healthy one. Fixed by filtering on `ExplicitStartupAnnotation` — the same signal `ResourceNotificationService` itself reads internally — rather than pattern-matching the `-rebuilder` name suffix.

## Phase 6 — review, findings closed

`infra-reviewer` found one blocker and four should-fix correctness bugs, all verified independently by the orchestrator before and after the fix:

1. **Blocker — an empty status report (header-only, or unreadable) vacuously passed the gate.** `not_started_resources`/`dead_resources` both print nothing over empty input, and the loop read silence as success. Fixed: the loop now explicitly checks `status_lines` is non-empty before proceeding, retrying rather than failing immediately (a header-only file mid-write is a normal transient state), with a distinct timeout message. Verified: a header-only file now polls and times out with `::error::the status report ... names no resources`, where before it exited 0 immediately.

2. **`Finished` with an unconfirmed/empty exit code burned the full window and then failed a healthy stack.** This repo's own `AspireFixture.cs:637-648` (#2064) documents that a `Finished` snapshot can arrive before its exit code is populated, and deliberately does not treat that as failure. The gate's `migrations` handling now mirrors that exact rule, scoped to the one one-shot resource this composition has. Verified: `migrations\tFinished\t` (empty) now returns "started" instead of hanging the full window; a non-migrations resource in the same state (negative control, `api-gateway\tFinished\t`) correctly keeps polling rather than being instantly passed or failed.

3. **`api-gateway`'s two replicas (`WithReplicas(2)`, ADR-0153 clause 2) collapsed into one line, last-write-wins.** `StackStatusReport` now tracks per-instance state keyed by `ResourceId` and folds worst-state-wins into one line per name on every write, so a bad sibling instance can never be hidden behind a healthy one. Verified live: booted with real replicas, confirmed two distinct instances via the Aspire resource list, both `Running`, folded correctly to one `api-gateway Running` line. The divergent-fold path (one replica bad) was verified by construction (`OrderByDescending(Severity)` over `{Running=0, FailedToStart=3}` deterministically picks the worse entry) rather than live, to avoid destabilizing the shared boot.

4. **The watch task failed silently on any exception.** Now wrapped in try/catch; genuine faults log via `ILogger` and append a `# watch ended: <reason>` line to the frozen file. `OperationCanceledException` from normal shutdown is not logged as a fault.

5. **The "no secrets" reasoning recorded the wrong mechanism.** The actual guarantee is the fixed 3-field line shape (`name\tstate\texitCode` — no properties, no URLs, no values), not the `ParameterResource` filter, which only decides which resources get a line at all. Corrected in `StackStatusReport.cs`'s doc comments.

Two should-fix items landed in the test files (test-writer, commit `61e35071`): stale `IResourceWithoutLifetime` doc references corrected to `ParameterResource`; the US2 message-content assertion strengthened (`assert.match(output, /minio/)` + `assert.doesNotMatch(output, /postgres/)`) rather than only asserting a non-zero exit code.

**Re-verified after all fixes, independently by the orchestrator** (not merely trusted from the agents' reports): `node --test scripts/wait-for-e2e-stack.test.mjs` — 6/6 green; `dotnet test --filter "Category=FixtureLogic"` — 145/145 green; `dotnet test tests/Architecture.Tests` — 401/401 green; `dotnet build -c Release` — 0 errors; code read directly (`StackStatusReport.cs`, `wait-for-e2e-stack.sh`'s new section) and confirmed to match every claim above.

**One residual, accepted in writing rather than fixed**: there is a narrow transient window, before every replica of a multi-replica resource has reported at least once, where the fold reflects only the replica(s) that have reported so far rather than holding the seed placeholder for the ones that haven't. This is strictly better than the pre-fix behavior (which silently discarded one replica's state permanently, not just transiently) and self-corrects on the gate's next 5-second poll; not worth the added complexity of tracking "has every expected instance reported at least once" given the gate already polls to convergence.

## Accepted nits (not fixed, recorded)

- Worst-case probe budget (~45 min) is close to the job's `timeout-minutes: 45`; a pathological run is cancelled rather than printing the gate's own error, but the `cancelled()` diagnostic step still dumps the report. T010 asks for the measured figure from the first real CI run rather than this estimate.
- `Running` is not `Healthy` — a service that is up and failing health checks satisfies the gate. Deliberate, per spec.md §5.
- `Attach` writes synchronously before `RunAsync`; a `StackStatusFile=` whose directory doesn't exist fails the boot itself rather than the gate. Fail-loud, at the composition root — acceptable.
- ADR-0109: this PR touches both `AppHost.cs` (a few lines) and `ci.yml` (the e2e job's boot step) — legitimate ownership for a CI-infra slice; a parallel branch touching either file rebases behind this one.

## Close-out measurements (T010)

- NetArchTest / `PrimitiveBoundaryTests` / `HandlerDeconstructionTests`: 401/401 green (`tests/Architecture.Tests`), confirmed independently.
- Resource-gate section's own wall-clock: **~5s** observed in a healthy manual boot (T007).
- E2E job's total CI duration against the 45-minute timeout: not measurable locally — first green CI run on the PR is the actual measurement, per tasks.md's own expectation.
