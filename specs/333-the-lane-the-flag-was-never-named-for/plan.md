# Implementation Plan: The lane the flag was never named for

**Spec**: [spec.md](./spec.md) · **Issue**: #2297 · **Status**: **DRAFT for route B — void unless
spec §4 Q1 is answered "B".**

**Phase-4a colour**: **behaviour-changing → red** for the e2e lane's composition (spec scenarios 1,
4, 5-e2e-half), plus **characterisation, observed green** for the lanes that must not move (scenarios
2, 3, 5-dev-half, and the integration fixture shape). Both halves are captured in 4a, before any
edit to `AppHost.cs` or `ci.yml`.

**Engineer**: `infra-engineer` (Aspire AppHost + CI workflow). **Test writer**: `test-writer`
(model-composition tests via `DistributedApplicationTestingBuilder.CreateAsync`, no containers).
**Reviewer**: `infra-reviewer`. No security surface (dev/CI-only containers; no credential moves).

## 1. Constitution / ADR check

| Rule | Status |
|---|---|
| ADR-0024 Aspire is the composition root | Change lives entirely in `AppHost.cs` + the boot line that feeds it. |
| ADR-0068 / ADR-0103 integration fixture boots "in E2ETests mode" | Untouched. The new switch is evaluated only inside `isRunMode && !isE2ETests`, so the fixture's composition cannot change (scenario: fixture shape characterised). |
| ADR-0108 Playwright runs against a run-mode stack | Preserved — this is precisely why route A is rejected (spec §2 B1). |
| ADR-0111 / #2013 switch precedent | `PersistentStack` copies `ScenarioSimulator`'s parse shape and fail-open default, and its workflow-text guard. No new mechanism. |
| ADR-0153 one instance per service | Unaffected; `AppHostReplicaCountTests` unchanged. |
| ADR-0144 lane may not write an ADR | None needed for B: a dev/CI composition switch alongside an existing one, implementing the issue's own first *Done looks like* route. (Route A would arguably need one — spec §4.) |
| ADR-0036 smallest change | `AppHost.cs` (one bool + gate edits + comments), one `ci.yml` line, one new test class, one shard-filter line. |
| §IV latency | N/A — the mediamtx ICE map is deliberately left in the e2e lane. |
| Bounded contexts / Shared.Contracts | N/A — no service code. |

## 2. Design

### 2.1 `src/AppHost/AppHost.cs`

1. Beside `isScenarioSimulatorEnabled` (`:25-26`):

   ```csharp
   bool isPersistentStackEnabled =
       !bool.TryParse(builder.Configuration["PersistentStack"], out bool persistent) || persistent;
   bool isPersistentDevStack = isRunMode && !isE2ETests && isPersistentStackEnabled;
   ```

   With a comment naming the three lanes once: developer `aspire run` (persistent), CI e2e boot
   (`PersistentStack=false`: run-mode shape, nothing persists), integration fixture (`E2ETests=true`).

2. Re-gate on `isPersistentDevStack`, each block's comment saying it is the developer lane's:
   - `:107` postgres — `WithLifetime(Persistent)`, `WithDataVolume()`, `WithPgAdmin()`.
   - `:130` rabbitmq — lifetime + data volume.
   - `:170` keycloak — lifetime + data volume (its realm-edit comment stays; it is about the dev lane).
   - `:297` mosquitto — lifetime + `mosquitto-data` volume.
   - `:332` azurite, inside the `RunAsEmulator` callback — lifetime + data volume.
3. **Split `:253` mediamtx**:
   - `if (isRunMode && !isE2ETests)` keeps `WithContainerRuntimeArgs("--publish", "8189:8189/udp", …)`
     and `MTX_WEBRTCADDITIONALHOSTS` — comment updated: both run-mode lanes, the browser's ICE path.
   - `if (isPersistentDevStack)` gets `WithLifetime(ContainerLifetime.Persistent)`.
4. `:850` camera-sim — move `.WithLifetime(ContainerLifetime.Persistent)` out of the fluent chain into
   `if (isPersistentDevStack) cameraSim.WithLifetime(...)`.
5. `:12-13` header comment — replace with the three-lane statement (no spec/issue numbers).
6. **Untouched**: `:243` fixture-video, `:435`, `:450`, `:600`, `:696` (6000/min), `:706` (Vite apps),
   `:838` simulator gate.

Order note: Aspire applies a persistent lifetime only if `WithLifetime` is called; a container with
no lifetime annotation is session-scoped. So "not calling" is the whole mechanism — no
`ContainerLifetime.Session` call is added.

### 2.2 `.github/workflows/ci.yml` (`:793`)

Append `PersistentStack=false` after `ScenarioSimulator=false`, before `StackStatusFile=` and the
`>` redirection. Optionally one comment line above the step saying the e2e lane is the run-mode shape
minus persistence. No other job changes (the `integration-shards` job boots via the fixture).

### 2.3 Tests — new class `tests/Integration.Tests/AppHostStackPersistenceTests.cs`

`[Trait("Category", "FixtureLogic")]`, model-only (`DistributedApplicationTestingBuilder.CreateAsync`,
no `StartAsync`), the same footing and argument-array convention as `AppHostE2ESwitchTests` /
`AppHostReplicaCountTests`. Argument sets:

- `DevArguments` — the four `Parameters:` entries only (developer lane).
- `E2EArguments` — `DevArguments` + `ScenarioSimulator=false` + `PersistentStack=false` (CI e2e lane).
- `UnparseableArguments` — `DevArguments` + `PersistentStack=nope`.
- `FixtureArguments` — `DevArguments` + `E2ETests=true`.

Facts (names sentence-style, ADR-0053):

| Fact | Lane | Today | After |
|---|---|---|---|
| `The_e2e_lane_composes_no_persistent_container` — no `ContainerLifetimeAnnotation { Lifetime: Persistent }` on any resource; failure message lists the offenders by name | e2e | **red** (six offenders) | green |
| `The_e2e_lane_mounts_no_data_volume` — no `ContainerMountAnnotation { Type: Volume }` on any resource | e2e | **red** (five) | green |
| `The_e2e_lane_composes_no_pgadmin` | e2e | **red** | green |
| `The_e2e_lane_keeps_the_web_apps_the_gateway_budget_and_the_ice_port_map` — four Vite apps + fixture-video present; `api-gateway` env `RateLimiting__PermitLimit=6000`; `mediamtx` runtime args contain `8189:8189/udp` and `8189:8189/tcp`; env `MTX_WEBRTCADDITIONALHOSTS=127.0.0.1` | e2e | green | green (characterisation) |
| `The_developer_lane_keeps_its_persistent_containers_volumes_and_pgadmin` — exactly postgres, rabbitmq, keycloak, mosquitto, storage, mediamtx, camera-sim Persistent; postgres/rabbitmq/keycloak/mosquitto/storage each carry a volume; pgadmin present | dev | green | green (characterisation) |
| `An_unparseable_switch_keeps_the_stack_persistent` | dev (bad value) | green | green (characterisation) |
| `The_integration_fixture_composes_no_persistent_container_and_no_volume` | fixture | green | green (characterisation) |
| `The_e2e_boot_line_disables_persistence` — reads `ci.yml`, single `dotnet run --project src/AppHost` line, `PersistentStack=false` before the first `>` | workflow | **red** | green |

**Witness against vacuous passes** (repo pattern, spec 169 §5.4): the dev-lane fact names its
expected resources explicitly, which proves the annotation reader can see a Persistent lifetime and a
volume at all — so the e2e-lane "none" assertions cannot pass because the reader is broken.

Reading helpers: env via the same `EnvironmentCallbackAnnotation` evaluation `AppHostGatewayRateBudgetTests`
already uses; runtime args via `ContainerRuntimeArgsCallbackAnnotation`. **Reuse those helpers'
shape, do not invent a new reader** — the test writer should read both existing classes first. The
workflow reader copies `AppHostE2ESwitchTests.BootLine()` / `WorkflowPath()` (slash-normalised via
`Path.Combine`, memory *source-scanning tests need slash normalising*). Duplicating ~25 lines of
private helpers matches how the existing AppHost test classes are written today; extracting a shared
helper is out of scope.

Shard filter: add `AppHostStackPersistenceTests` to the shard file that carries
`AppHostE2ESwitchTests` (`tests/Integration.Tests/ci-shards/shard-4.filter`) — memory *new test classes
need a shard-filter entry*.

### 2.4 Commits (ADR-0030, each builds on its own; ADR-0087)

1. `test(2297): pin the e2e lane's composition to no persistence` — the new class + shard entry.
   Red by design (four facts); the PR quotes the failure. Build compiles.
2. `fix(2297): stop the e2e lane composing persistent containers and volumes` — `AppHost.cs` +
   `ci.yml`. All eight facts green.

## 3. Proof that the new topology works (before merging)

The model tests prove the composition; they do not prove a stack boots and the suite passes on it.
Three further observations, in order:

1. **Local boot (phase 5, SC-004).** With no other Aspire stack running (memory *one machine, one
   Aspire stack*), boot exactly the CI command with `PersistentStack=false`, wait for
   `scripts/wait-for-e2e-stack.sh` to pass (STACK_STATUS_FILE set), record `docker ps --format
   '{{.Names}}'` (no `pgadmin`), stop the AppHost, then record `docker ps -a` and `docker volume ls`
   filtered to the stack's names — expected empty. Note: the developer's own persistent containers
   from earlier `aspire run`s share names; **do not remove them** — compare against a listing taken
   before the boot, and check the new boot created no new volume.
2. **The PR's own CI run (SC-003).** The four `e2e-shards` jobs are the live test of the new topology
   — there is no other way to run the full Playwright suite on a Linux runner. Read each shard's
   "Wait for the stack to be ready" and suite verdict; compare against the latest green `develop` run
   for the same base. A shard that is red for a reason present on `develop` too is not attributed to
   this change (memory *kiosk label-span test flake*); one red only here blocks.
3. **What does not change and why it matters:** the render-leg summary (spec 225) is produced by the
   same shards on the same media topology, so its figure remains comparable.

## 4. Risks

| Risk | Mitigation |
|---|---|
| A Playwright spec silently relied on Postgres/Keycloak state surviving inside a run across a container restart | None found (grep of `e2e/` for docker/restart/kill — the only "process death" is the browser's). The live CI run is the backstop. |
| DCP cleanup on SIGTERM races runner teardown | Irrelevant to correctness on ephemeral runners; the guarantee is "nothing created persistent". |
| Developer surprised by a new switch | Default on; bare `aspire run` unchanged (characterised). |
| Fixed port 8189 collision remains | Accepted residual pending spec §4 Q2. |
