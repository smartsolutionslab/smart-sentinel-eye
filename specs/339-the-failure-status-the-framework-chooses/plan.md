# Plan 339: The failure status the framework chooses

**Spec**: [spec.md](./spec.md). **Issue**: #2571. **Branch**: `feat/2571-health-check-registration-guard`

**Status**: Phase 2 gate.

## 1. Constitution and ADR alignment

| Check | Result |
|---|---|
| Spec references ≥ 1 ADR | ADR-0154 (the rule), plus 0103, 0109, 0139, 0144, 0052, 0053, 0105 |
| New ADR needed | **No.** The rule is ADR-0154 clause 1. The `self` exemption follows from that clause (spec §5). The capture mechanism is a test-harness choice, and the owner made it (spec §8, option A). |
| Bounded-context boundaries | Untouched. All code lives in `tests/Architecture.Tests`, which already references every host. `src/` gets no new code and no new cross-context reference. |
| §II primitives | N/A. Test code, no domain model. |
| §IV latency | N/A. Build-time test, no leg touched. |
| ADR-0103 (no Testcontainers) | Holds. The test needs no Docker and no Aspire boot. |
| ADR-0109 contention files | **None touched.** No `Directory.Packages.props` change, no `.csproj` change, and no `ci.yml` change. Architecture.Tests runs whole in CI and has no shard filter. One existing file in this project changes by visibility only (§4). |
| ADR-0144 lane limits | No ADR, no constitution change, no suppression, no lowered gate. |

## 2. Shape

This is a test-only change in one project, `tests/Architecture.Tests`. All new files go under a
new folder, `tests/Architecture.Tests/HealthChecks/`.

| File | Kind | Responsibility |
|---|---|---|
| `HealthChecks/ComposedHost.cs` | harness | Runs a host's real entry point and captures the built `IHost` at `Build()` (§3). |
| `HealthChecks/ServedHealthHosts.cs` | data | The ten hosts that map `/health`: assembly name and project directory relative to the repo root. Exposes `TheoryData<string>`. |
| `HealthChecks/HostCaptureCollection.cs` | xUnit | `[CollectionDefinition(Name, DisableParallelization = true)]` (§3.4). |
| `HealthChecks/HealthCheckFailureStatusRule.cs` | evaluator | A pure function from registrations to offenders, encoding FR-002 and FR-003 (§5). |
| `HealthChecks/HealthCheckFailureStatusGuardTests.cs` | tests | The ten host cases, plus the planted-violation self-tests and the capture-failure tests (§6). |
| `HealthChecks/ServedHealthHostCompletenessTests.cs` | tests | FR-007. Compares the source-scanned set of `MapDefaultEndpoints` callers with `ServedHealthHosts`. |
| `Persistence/PostgresComposition.cs` | **edit** | `ConfigurationFor` changes from `private` to `internal`. No other change (§4). |

## 3. The capture mechanism (spec §8, option A)

### 3.1 Contract

```text
ComposedHost.Capture(
    string entryAssemblyName,
    string contentRoot,
    IReadOnlyDictionary<string, string?> configuration,
    Action<IHostBuilder>? configureServices,   // null for the guard; the planted hook for self-tests
    TimeSpan timeout) → IHost                  // caller disposes
```

The return value is the `IHost`, not just its `Services`, so the caller can dispose it. The host
is built but never started. Disposing an unstarted host is safe.

### 3.2 Mechanism

The §1.2 probe ran this mechanism on all ten hosts on 2026-10-12. The reference copy is the
scratch probe at
`scratchpad/probe/Program.cs`. It is not in the repository, and the engineer rewrites it to repo
conventions.

1. Resolve the entry point: `Assembly.Load(entryAssemblyName).EntryPoint`. It must not be null.
   Each host's `Program.cs` uses top-level statements, so the compiler-synthesised `Main(string[])`
   is the entry point.
2. Build `args`. Each configuration key becomes `--{key}={value}`. Then append
   `--contentRoot={contentRoot}`.
   - **Why args and not the hook:** `AddBearerAuthentication` reads `ConnectionStrings:keycloak`
     *before* `Build()` and throws if it is missing (spec §1.3). `HostBuilding` fires inside
     `Build()`, which is too late. Arguments reach `CreateBuilder(args)` first.
   - **Why not environment variables:** they are process-wide and would leak into other tests.
3. Subscribe to `DiagnosticListener.AllListeners`. For a listener named
   `Microsoft.Extensions.Hosting`, subscribe to its events. **Every callback first checks
   `Thread.CurrentThread == captureThread` and ignores events from any other thread.** Other tests
   in this project build hosts (`SystemVariableReadScopeTests.cs:109`,
   `SystemVariableRouteShadowingTests.cs:120`, `DatabaseCommandLogLevelTests.cs:129`,
   `WhepAuthorizeCeilingTests.cs:199`).
   - `HostBuilding`: if `configureServices` is non-null, invoke it on the payload cast to
     `IHostBuilder`. *Assumption, verify in T004:* the payload is an `IHostBuilder` adapter for
     both `WebApplicationBuilder` and `HostApplicationBuilder`, and a `ConfigureServices` callback
     registered on it still applies to the host being built. The probe exercised only
     `HostBuilt`. If the assumption fails, the fallback is a `HostBuilt`-time
     `Configure<HealthCheckServiceOptions>` mutation. That would be unsound, because options are
     already materialised, so **stop and report** instead of improvising.
   - `HostBuilt`: complete the `TaskCompletionSource<IHost>` with the payload, then throw a private
     sentinel exception. This unwinds `Program.cs` out of `Build()`, so it never maps endpoints
     and never calls `RunAsync`, and no hosted service starts.
4. Run the entry point on a dedicated `Thread` (not the thread pool, so the thread filter is
   exact).
   - Unwrap `TargetInvocationException` and `AggregateException`. If the result is the sentinel,
     it is the expected outcome.
   - If the entry point **returns** without the sentinel, fail with "entry point returned
     without building a host".
   - Any other exception faults the task with the **host's own** exception (FR-004).
5. Wait on the task with `timeout`. On timeout, throw a `TimeoutException` naming the assembly.
   Use 60 s (the probe built all ten in seconds).
6. Dispose the listener subscription in all paths.

Everything about the hook is undocumented: the event names `HostBuilding` and `HostBuilt`, the
listener name, and the payload types. A change to any of them fails every case loudly through
FR-004. Record this in the file's doc comment. It is the cost of option A, and the spec accepted
it.

### 3.3 Conventions that apply

- `Ensure.That(...)` for argument guards (ADR-0105).
- No leading-underscore fields.
- Collection expressions.
- Sentence-style test names (ADR-0053).
- Shouldly.
- No `ConfigureAwait`.
- The advisory S138 metric (30 LOC per method, ADR-0084) applies, so split `Capture` into its
  listener and runner halves.
- No new `#pragma` or suppression (ADR-0144).

### 3.4 Non-parallel collection

The hosting hook is a **process-global** `DiagnosticListener`. Two concurrent captures, or a
capture concurrent with any other test that builds a host, would receive each other's events.
The thread filter (§3.2.3) prevents misattribution. `DisableParallelization = true` additionally
makes xUnit 2.9.3 run this collection **after** all parallel collections, alone. Belt and braces:
the filter makes the result correct, and the collection makes it deterministic.
`HealthCheckFailureStatusGuardTests` carries `[Collection(HostCaptureCollection.Name)]`. The
completeness test captures nothing and stays parallel.

### 3.5 Captured state across repeated captures

Each host is captured once by the theory. The self-tests recapture **ApiGateway** only. It is the
smallest host: one registration, no Wolverine, no static Wolverine or JasperFx state. If a
second capture of the same assembly misbehaves, that is a finding to report, not something to
work around.

## 4. Placeholder configuration (FR-008)

Reuse rather than duplicate. `PostgresComposition.ConfigurationFor(string contextName)` changes
from `private` to `internal`. This is a visibility-only change with no behaviour change, and its
existing callers are untouched. `ServedHealthHosts` builds **one** union dictionary across
`PostgresComposition.ContextNames` and gives it to every host, including ApiGateway. The probe
showed the union is sufficient and harmless for all ten: an unread key is inert. The keys are the
nine `*-db` connection strings, `rabbitmq`, `keycloak`, `mediamtx-api`, `mediamtx-whep`,
`Mosquitto:Endpoint` and `blobs`. Every value is syntactically valid and never dialled.

Content root: `RepositorySource.RepositoryRoot()` + the host's project directory. Each host then
reads its own `appsettings.json`. This matters for ApiGateway, whose `ReverseProxy` section is
read at registration time.

## 5. The rule (spec §5, FR-002, FR-003)

`HealthCheckFailureStatusRule.Offenders(IReadOnlyCollection<HealthCheckRegistration> registrations,
IServiceProvider services) → IReadOnlyList<string>`

- **Exempt shape.** All of these must hold:
  - `Name == "self"`, compared ordinally.
  - `Tags` equals the set `{ "live" }` exactly.
  - `Factory(services)` returns `Microsoft.Extensions.Diagnostics.HealthChecks.DelegateHealthCheck`.
    The type is internal to the framework, so compare its `FullName` string. Instantiate inside a
    scope, as the existing guards do.
- **Exactly one exempt registration per host.**
  - Zero `self` registrations is an offender: `"self" missing`. This also catches an empty
    registration set (FR-004).
  - More than one registration named `self` makes every one of them an offender.
- **Every other registration:** `FailureStatus == HealthStatus.Unhealthy` makes it an offender.
  `Degraded` and `Healthy` both pass, since both are non-default and both answer `200`
  (ADR-0154).
- **A registration named `self` that does not match the exempt shape** is an offender under the
  exemption clause, whatever its `FailureStatus`. Its message says which part of the shape
  failed.
- **Offender format:** `"{name}" (failureStatus={status}, tags=[{tags}], type={type}): {reason}`.
  The test prefixes the host. The assertion message cites ADR-0154 clause 1 and the exemption's
  rationale in one sentence.

Domain events, integration events and messaging: **none.** This is a test-only change.

## 6. Tests

**Expected today: all green, except the red step in T004 that phase 4a observes.**

| Test | Collection | Arrives | Bite proven by |
|---|---|---|---|
| `Every_health_check_on_a_host_serving_health_declares_a_failure_status_other_than_unhealthy(host)`, theory × 10 | HostCapture | green (the property holds, spec §1.2) | SC-003 and SC-004 counterfactuals |
| `The_guard_reports_a_check_registered_without_a_failure_status`: ApiGateway + planted `AddCheck("planted", () => Healthy())` → offenders exactly `["planted"…]` | HostCapture | **red first** (phase 4a) | itself |
| `The_guard_reports_a_check_registered_with_an_explicit_unhealthy_failure_status` | HostCapture | green after the evaluator | — |
| `A_check_named_self_with_a_different_tag_does_not_inherit_the_exemption`: the hook mutates `self`'s `Tags` through `Configure<HealthCheckServiceOptions>` | HostCapture | green after the evaluator | — |
| `A_second_check_named_self_does_not_inherit_the_exemption` | HostCapture | green after the evaluator | — |
| `A_host_missing_required_configuration_fails_capture_with_its_own_exception`: Automation.Api with an empty dictionary → throws, and the message contains "Keycloak base URL not found" | HostCapture | green after the harness | — |
| `An_entry_point_that_returns_without_building_a_host_fails_capture`: `Architecture.Tests`' own assembly, whose generated entry point returns at once | HostCapture | green after the harness | — |
| `Every_host_that_maps_health_is_guarded_and_every_guarded_host_maps_health` (FR-007) | none | green | counterfactual: drop one entry from `ServedHealthHosts` → red naming that `Program.cs` |

**Untested path, stated:** the capture's **timeout** branch. Exercising it needs an entry point
that hangs before `Build()`, and no such assembly exists to load. It is covered by review only.
The spec's timeout scenario is satisfied by the branch existing and naming the host, not by a
test.

**FR-007 mechanics.**
- Enumerate `src/**/Program.cs`, excluding `obj/` and `bin/`.
- Keep files containing `MapDefaultEndpoints(`.
- Map each to the single `*.csproj` in its directory. The file name without extension is the
  assembly name; no `src` project overrides `<AssemblyName>`, checked 2026-10-12.
- Set-compare with `ServedHealthHosts`.
- Use `RepositorySource` for the root, and normalise slashes on relative paths (memory:
  *source-scanning tests need slash normalising*).
- Expected: ten files. MigrationRunner and ScenarioSimulator do not call it.

## 7. Phase 4a: red (behaviour-changing)

1. **Harness first, characterised by the theory.** T002–T003 land `ComposedHost`,
   `ServedHealthHosts` and the collection. They also add a temporary evaluator stub that returns
   `[]`. With the stub, the ten-host theory is green. That is meaningless as evidence, and the PR
   must say so.
2. **Red.** T004: the test-writer writes
   `The_guard_reports_a_check_registered_without_a_failure_status` against the stub. Shouldly
   must fail **on the assertion** (`should be [...] but was []`), not on compile. The output is
   quoted verbatim.
3. **Green.** T005: the engineer implements the rule. The red test passes **unmodified**. The ten
   host cases stay green.
4. **Bite of the ten host cases.** Both changes below are local edits to production code and are
   reverted before commit. Quote the output in `specs/339-…/T009-counterfactual.md`, following
   spec 282's precedent.
   - SC-003: set `WolverineDefaults.cs:211` to `HealthStatus.Unhealthy`. **Exactly nine** context
     cases go red; ApiGateway stays green.
   - SC-004: change `Extensions.cs:129`'s tag to `"ready"`. **All ten** go red, naming `self`.

   Precedent for "the guard arrives green, its bite is proven by counterfactual":
   `tests/Architecture.Tests/Persistence/DependencyHealthCheckGuardTests.cs`'s own doc comment.

## 8. Risks

| Risk | Likelihood | Effect | Mitigation |
|---|---|---|---|
| The `HostBuilding` payload is not a usable `IHostBuilder` | low | No planted hook, so no red test | T004 verifies it first. Stop and report, do not switch to a `HostBuilt` mutation. |
| A future runtime renames the hosting events | low | All cases fail | Loud by FR-004. The doc comment names the dependency. |
| Repeated capture of one assembly misbehaves | low | Self-tests flaky | Only ApiGateway is recaptured (§3.5). |
| A new host config key added later | medium | That host's case fails with its own exception | Loud by design. Fix by adding the key to `PostgresComposition.ConfigurationFor`. |
| Adding the theory slows Architecture.Tests | low | Seconds | Measure in phase 5 and record the figure. |

## 9. Follow-up for whoever picks up ADR-0154 clause 2 (not part of this spec, not filed)

`DependencyHealthCheckGuardTests` composes `Add{Context}Infrastructure` only. Its doc comment
attributes that to #2571's open question 1. Once `ComposedHost` lands, that guard can run over
`ServedHealthHosts` instead. That would bring every check registered in `Program.cs` and
`ServiceDefaults` into clause 2's name and assembly rules, with no new policy. `outbox-{module}`
needs its existing carve-out carried over. This is a separate issue for the orchestrator to
decide on.
