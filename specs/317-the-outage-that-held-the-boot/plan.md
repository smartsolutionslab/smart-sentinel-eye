# Plan 317: The outage that held the boot

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2170

## 1. Where it lives

| Concern | Placement |
|---|---|
| Bounded context | **Identity** only. No other context, no `Shared.Contracts`, no `Shared.Kernel`. |
| Layer | **Infrastructure** — `src/Identity/Infrastructure/KeycloakAdmin/KioskPrivilegeSweepHostedService.cs` and `src/Identity/Infrastructure/Log.cs`. |
| Domain / Application | **Untouched.** `KioskPrivilegeSweep` (Application) stays as it is (FR-007). No entities, value objects or invariants change; the bound is an infrastructure timing policy on a hosted service, which §II does not govern. |
| Messaging | None. No domain or integration event is added or changed. |
| Composition | `IdentityInfrastructureModule.cs` **unchanged** — `TimeProvider.System` is already registered at line 50 and `AddHostedService<KioskPrivilegeSweepHostedService>()` resolves the new constructor parameter from it. |
| Boundaries | No new project references. `BoundaryTests` unaffected. |

## 2. The change

### 2.1 `KioskPrivilegeSweepHostedService`

- Constructor gains `TimeProvider timeProvider` (between the scope factory and the logger).
- A private field `private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);` — private,
  because the test pins the value by behaviour (§3) rather than by reading it. Its comment carries spec §4's
  reasoning in short: below the 10 s attempt timeout so the bound, not Polly, ends an outage;
  ~10× the measured 519 ms healthy pass; the kiosk-count assumption.
- `StartAsync` shape (the essential part; the engineer writes the real code):

  ```csharp
  using CancellationTokenSource bound = new(Bound, timeProvider);
  using CancellationTokenSource linked =
      CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bound.Token);
  try
  {
      await SweepOnceAsync(linked.Token);
  }
  catch (OperationCanceledException) when (bound.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
  {
      logger.KioskPrivilegeSweepTimedOut(Bound);
  }
  catch (Exception exception) when (exception is not OperationCanceledException)
  {
      logger.KioskPrivilegeSweepFailed(exception);
  }
  ```

**Why the first catch is load-bearing.** The existing catch excludes `OperationCanceledException`
on purpose (host shutdown must propagate). The bound surfaces *as* an `OperationCanceledException`
— `HttpClient` throws `TaskCanceledException`, and Polly rethrows an outer token's cancellation as
OCE rather than `TimeoutRejectedException`. Without the filtered catch, hitting the bound would
escape `StartAsync` and **stop the host** — the exact failure spec 092's Red C exists to prevent.
The filter checks *which* token fired, so FR-004 (shutdown still propagates) holds.

**Why `new CancellationTokenSource(TimeSpan, TimeProvider)`** and not `CancelAfter`: the
`TimeProvider` overload creates its timer through the provider, so a hand-written clock can drive
it (FR-006). `CancelAfter` always uses the system timer.

**Why cooperative cancellation and not `Task.WaitAsync(Bound)`**: `WaitAsync` would return while
the pass kept running against a disposed scope. Every call on the path honours the token —
`HttpKeycloakAdminClient`'s calls, `KeycloakAdminAuthorizationHandler` →
`ClientCredentialsTokenProvider.GetAccessTokenAsync` (`gate.WaitAsync(cancellationToken)`, then
`PostAsync(…, cancellationToken)`) — and the two catches in `HttpKeycloakAdminClient`
(lines 128, 493) both exclude OCE, so nothing on the path converts the cancellation into a
different exception.

### 2.2 `Log.cs`

One new `[LoggerMessage]` beside `KioskPrivilegeSweepFailed` (line 64), Warning level, no
exception argument (the cancellation is not a fault worth a stack), the bound as a field:

> `"The kiosk privilege startup sweep did not finish within {Bound} and was abandoned so Identity's start is not held; enrolled kiosks may still hold inherited realm privileges until the next start."`

Method name `KioskPrivilegeSweepTimedOut(this ILogger logger, TimeSpan bound)`. The existing
comment above `KioskPrivilegeSweepFailed` explains Warning-not-Error ("the API is serving, and the
next start tries again"); the new entry shares that reasoning and adds only what differs — that
this line is what an operator reading a slow Identity boot looks for (the issue's "recorded
somewhere an operator would find it").

`EventId`: generator-assigned, like every other entry in `Log.cs`; tests select by `EventId.Name`.

## 3. Tests (phase 4a — RED)

New file `tests/Identity.Infrastructure.Tests/KeycloakAdmin/KioskPrivilegeSweepBoundTests.cs`,
plus two fakes in `tests/Identity.Infrastructure.Tests/Fakes/`:

- `SilentKeycloakAdminClient` — `GetEnrolledKioskClientIdsAsync` awaits
  `Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)` (a black-holed socket that honours
  cancellation, as `HttpClient` does); other members throw like `UnreachableKeycloakAdminClient`.
  Counts enumeration attempts.
- `ManualTimeProvider` — hand-written (ADR-0054; `FakeTimeProvider` is deliberately not referenced
  anywhere in the repo). Copy the minimum of `tests/ScenarioSimulator.Tests/Fakes/ManualTimeProvider.cs`
  that `CancellationTokenSource(TimeSpan, TimeProvider)` needs: `CreateTimer`, `Advance`, and an
  event-driven `WaitForPendingTimersAsync` (no real-time sleeps). Not shared across test projects —
  test projects don't reference each other here.

Compose exactly as `KioskPrivilegeSweepStartupTests.Compose` does — `AddIdentityInfrastructure`,
then last-wins overrides of `IKeycloakAdminClient` **and `TimeProvider`** — and build the hosted
service with `ActivatorUtilities.CreateInstance`, so the test compiles against today's constructor
and fails at runtime, not at compile time.

The test owns the value it pins: a local `TimeSpan bound = TimeSpan.FromSeconds(5)` with a comment
citing spec 317 §4, **not** a reference to the production field. That keeps the file compiling
against today's code — so the red is a runtime failure that can be quoted, not a compile error —
and a change to the production value fails the test instead of silently moving the assertion with it.

| Test | Today | After |
|---|---|---|
| `A_keycloak_that_never_answers_is_abandoned_at_the_bound_and_the_boot_continues` — start; wait (event-driven) for the pending timer; `Advance(bound - 1 tick)` → `StartAsync` not completed; `Advance(1 tick)` → completes (real-time guard `WaitAsync(10 s)` so a regression fails instead of hanging), no exception, exactly one `KioskPrivilegeSweepTimedOut` entry whose `Bound` field equals `bound`, no `KioskPrivilegeSweepFailed`; enumeration attempted once | **RED at runtime**: today's service never asks the `TimeProvider` for a timer, so the pending-timer wait fails with a message naming the missing bound | green |
| `Host_shutdown_during_the_sweep_is_not_mistaken_for_the_bound` — cancel the start token before the bound; `StartAsync` throws `OperationCanceledException`; no `KioskPrivilegeSweepTimedOut` | green — characterisation guard for FR-004, observed green **before** the change | must stay green, unmodified |

Test 1 pins both edges of the value (not done at 5 s − 1 tick, done at 5 s), so no separate
"the constant is 5" test is needed. Quote test 1's red and test 2's pre-change green verbatim in
the PR.

`CapturingLogger<T>` (existing fake) supplies the log assertions by `EventId.Name`; register it as
the `ILogger<KioskPrivilegeSweepHostedService>` override.

Existing tests that must pass **unmodified**: `KioskPrivilegeSweepStartupTests` (Red C),
`KioskPrivilegeSweepSteadyStateTests`, `Identity.Application.Tests/KeycloakAdmin/KioskPrivilegeSweepTests`,
`Architecture.Tests/KioskPrivilegeSweepRegistrationTests`,
`Integration.Tests/Identity/KioskPrivilegeSweepStartupIntegrationTests`.

No Integration.Tests class is added, so no `ci-shards/shard-N.filter` entry is needed.

## 4. Constitution / ADR alignment

- §II (primitives on the domain model): not engaged — no domain model changes.
- §IV latency: N/A (startup, not the event→overlay path).
- ADR-0134 Decision 1: the sweep stays a *startup* sweep, awaited at start.
- ADR-0143: no resilience option changes; the bound sits *outside* the pipeline.
- ADR-0105: no new argument guards needed (DI-supplied constructor parameters; follow the file's
  existing primary-constructor pattern, which has none).
- ADR-0049: `CancellationToken` stays the last parameter; no `ConfigureAwait`.
- ADR-0036: one constant, no option class, no config key.

## 5. Risks

| Risk | Mitigation |
|---|---|
| Bound hit escapes as OCE and stops the host | The filtered catch (§2.1); test 1 asserts no exception. |
| Shutdown swallowed as a timeout | Filter requires the host token *not* cancelled; test 3. |
| A large healthy realm truncated every boot | Visible via FR-003 on every boot; spec §4 records the ~80-kiosk assumption; phase 5 step 2 re-measures the healthy pass. |
| Flaky timing in the unit test | Virtual clock; the only real-time wait is a generous failure guard. |
