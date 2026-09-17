# Spec 175 — Plan

**Phase**: 2 (Plan) · **Date**: 2026-09-17 · **Issue**: #2212
**Engineer**: `backend-engineer` · **Reviewer**: `backend-reviewer`
**Scope**: US1 only. The integration test is **#2441**, split out at the phase-3
gate on the instruction of #2212's own scope note. No AppHost file is touched.

---

## 1. Bounded context and layers

One context, `EventIngestion`. Three of its four layers, plus the composition
root.

| Layer | Change |
|---|---|
| **Domain** | **None.** No aggregate, entity, value object or domain event is touched. |
| **Application** | New `IngestWriteOptions` (configuration shape). Guard added to `IngestWriteLimiter`'s `int` constructor. |
| **Infrastructure** | `EventIngestionInfrastructureModule` binds the options and registers the limiter from them. |
| **Api** | **None.** `EventsEndpoints.Writes.cs` is read-only here — it is the code under test, and editing it would be editing the subject to fit the test. |
| **AppHost** | **None.** The `isE2ETests` override line belongs to #2441. |

**Why the options class lives in Application, not Infrastructure.** It is the
shape `IngestWriteLimiter` is configured by, and the limiter is in
`Application/Ingress/`. Its nearest sibling, `IngestRetryOptions`, is in that
exact folder for the same reason, while `MosquittoOptions` — which describes a
broker connection — is in `Infrastructure/Ingress/`. The split already in the
repo is *what is configured*, not *who binds it*; Infrastructure binds both.

## 2. Entities, value objects, invariants

**No new domain types**, and that is a deliberate finding rather than an
omission — see spec §2.3. `IngestWriteOptions.Concurrency` is an `int` because
every options class in this repo binds primitives and constitution §II does not
reach the Application layer.

The one invariant this spec adds is not on a domain model; it is an **argument
precondition**:

> `IngestWriteLimiter`'s concurrency is at least 1.

Expressed with `Ensure.That(concurrency).AtLeast(1)` (ADR-0105). The `int`
overload of `Ensure.That` (`src/Shared.Kernel/Ensure.cs:50-53`) carries
`[CallerArgumentExpression]`, so the parameter name comes for free and the
message is `concurrency must be >= 1; got 0.` — naming the input and the value,
which is what makes the failure diagnosable in a service log.

`SmartSentinelEye.Shared.Kernel` is already a `ProjectReference` of
`SmartSentinelEye.EventIngestion.Application`
(`src/EventIngestion/Application/SmartSentinelEye.EventIngestion.Application.csproj:5`),
and `Ensure.That` is used across that project's handlers. **No `.csproj` edit.**

## 3. Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` change,
no Wolverine handler, no queue. Backpressure is answered synchronously on the
HTTP response and always has been.

## 4. Boundary rules

- **No cross-context project reference** is added or needed. Everything is
  inside `EventIngestion`. No composition-root file is touched either — the
  AppHost override belongs to #2441.
- **`Shared.Contracts` untouched.** The 429 is a `ProblemDetails` produced by
  `Results.Problem`, not a contract type.
- **Api does not reference Infrastructure.** The limiter is resolved through DI
  as it is today; only the *registration* changes.
- **NetArchTest**: no rule is affected. `Architecture.Tests` should pass
  unchanged, and if it does not, that is a finding to report rather than a rule
  to adjust.

## 5. Files

Exhaustive. **Phase 4 may touch these and no others.** A change outside this
list is a stop-and-report, not a judgement call.

### Production

| File | Change |
|---|---|
| `src/EventIngestion/Application/Ingress/IngestWriteOptions.cs` | **New.** `SectionName = "EventIngestion:IngestWrite"`, `int Concurrency` defaulting to `IngestWriteLimiter.DefaultConcurrency`. |
| `src/EventIngestion/Application/Ingress/IngestWriteLimiter.cs` | `:29` only — expression body becomes a block with `Ensure.That(concurrency).AtLeast(1);` before the assignment. Add the `using` for `SmartSentinelEye.Shared.Kernel`. **`DefaultConcurrency = 64` at `:23` does not move.** |
| `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs` | `:117` becomes a factory registration; an `AddOptions<IngestWriteOptions>().Bind(...)` beside the `IngestRetryOptions` binding at `:134-138`. |

### Tests

| File | Change |
|---|---|
| `tests/EventIngestion.Application.Tests/Ingress/IngestWriteLimiterTests.cs` | **Append only.** Two cases for AS-3. The four existing tests are not edited (spec AS-4). |
| `tests/EventIngestion.Infrastructure.Tests/IngestWriteConcurrencyRegistrationTests.cs` | **New.** AS-1 and AS-2 through the real module. |

### Explicitly read-only

`src/EventIngestion/Api/EventsEndpoints.Writes.cs`,
`src/EventIngestion/Api/EventsEndpoints.cs`,
`src/EventIngestion/Api/appsettings.json` (the default is the type default; a
key in `appsettings.json` would restate 64 in a second place and is the
duplication §5.1 avoids), and every existing test file other than
`IngestWriteLimiterTests.cs`.

## 6. Test design

### 6.1 AS-1 / AS-2 — mirrors `IngestVolumeRegistrationTests`

`tests/EventIngestion.Infrastructure.Tests/IngestVolumeRegistrationTests.cs` is
the precedent and it is an exact fit: it composes the **real** module in-process
and resolves one service, without starting the host, opening a connection or
touching a container.

```csharp
HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
builder.Configuration.AddInMemoryCollection(Configuration);
builder.AddEventIngestionInfrastructure();
using ServiceProvider provider = builder.Services.BuildServiceProvider();
```

The new file reuses that `Configuration` dictionary shape — the five keys the
module resolves (two Postgres connections, RabbitMQ, Keycloak, the Mosquitto
endpoint), syntactically valid and never dialled — adding
`["EventIngestion:IngestWrite:Concurrency"] = "2"` for AS-1 and omitting it for
AS-2.

That precedent also records the cost, and it applies here unchanged: this test
couples to those five configuration keys, so a change to any of them breaks it
in a way that reads as unrelated to backpressure. The trade is the same one
`IngestVolumeRegistrationTests` made — a loud, named failure instead of the
silent one the test exists to prevent.

**AS-2 takes 64 leases and asserts the 65th is refused.** Slower than asserting
`IngestWriteOptions.Concurrency == 64`, and that is the point: the property
default cannot tell a factory registration that reads it from one that ignores
it (spec §6.3 CF-C).

### 6.2 AS-3 — appended to the existing unit file

Two `[Fact]`s, Shouldly's `Should.Throw<ArgumentException>`, asserting
`ParamName` is `"concurrency"` so the test pins the diagnosis and not merely the
throw. Sentence-style names (ADR-0053). No builder needed (ADR-0054) — the input
is an `int`.

### 6.3 What the integration test would have added, and where it went

`tests/Integration.Tests/EventIngestion/IngestBackpressureIntegrationTests.cs`
is **#2441**, not this spec. Its design — the eight-POST `Task.WhenAll`, the
`if (isE2ETests)` override on `AppHost.cs:501`, the two-step red, the
problem-title injection and the honest non-determinism framing — is carried in
full on that issue so it is not redone from scratch.

**What this slice therefore leaves uncovered, stated rather than discovered**:
the two lines at `EventsEndpoints.Writes.cs:344-351` that turn a refused lease
into a `429`. Spec 104 recorded that gap; this spec does not close it, it makes
closing it possible. AS-1 proves the limiter is bounded by the configured value;
nothing here proves that bound becomes an HTTP answer.

### 6.4 What is deliberately not tested

- **Anything over HTTP.** #2441. This slice tests how the limiter is
  constructed, not its effect on a response.
- **Concurrency under load.** Spec §0.1.
- **A non-numeric configuration value.** Options binding throws before the code
  under test is reached; a test of it tests the BCL.

## 7. CI placement

- `IngestWriteLimiterTests` and `IngestWriteConcurrencyRegistrationTests` run in
  the ordinary unit buckets. No new project, no new package, no `.csproj` edit.
- **No integration bucket change.** This slice adds no test to
  `tests/Integration.Tests/`, boots no stack and starts no container, so the
  Aspire bucket's runtime is unaffected.
- **No `[Trait("Category", "Measurement")]`.** Neither new test is a
  measurement; tagging one would exclude it from CI and defeat the issue.

## 8. Coverage gate (ADR-0065)

Application ≥ 80%. The added Application code is one options class of a single
auto-property plus one guard line, and both are covered by AS-1/AS-2/AS-3.
Infrastructure carries no gate. **Coverage should move up, not down**; a drop is
a finding.

## 9. Constitution and ADR alignment

| Rule | How this plan meets it |
|---|---|
| **§II** value objects | Checked; does not bind (spec §2.3). No primitive reaches a domain model. |
| **§Testing** two obligations | Behaviour-changing → **red**, both stories. Three named reds, three counterfactuals (spec §6). |
| **§IV** latency | N/A, argued from the instruction stream rather than asserted (spec §7). |
| **§III / ADR-0051** boundaries | One context; registration stays in `AddEventIngestionInfrastructure`. |
| **ADR-0105** guards | `Ensure.That(concurrency).AtLeast(1)`, not `throw new ArgumentException`. |
| **ADR-0036** smallest change | Three production lines plus one new 8-line file. No abstraction, no interface, no startup-validation mechanism the repo does not have. |
| **ADR-0084** metrics | Every touched file stays far below 300 LOC; no method grows past 30. |
| **ADR-0141** `Option<T>` | Not applicable — no absence is modelled. A missing key is an absent *configuration*, resolved by the property default, not by the domain. |
| **ADR-0109** `[P]` | One task qualifies (T002); see tasks.md. |
| **ADR-0144** no weakened gates | `IngestWriteLimiterTests`'s four existing tests pass **unmodified**. No suppression, no threshold change, no deletion. |

## 10. Risks, and what each costs

| Risk | Mitigation |
|---|---|
| The binding half is mistaken for characterisation and only AS-2 gets written | **The live risk in this slice.** AS-2 passes against the current broken registration, so skipping AS-1 yields a test wired to nothing. Spec §6.2 and tasks.md's per-piece colour table both say so; CF-A (T010) demonstrates it rather than asserting it. |
| The engineer drifts into #2441's work | Any edit under `src/AppHost/` or `tests/Integration.Tests/` is out of scope. Stop and report. |
| `IngestWriteConcurrencyRegistrationTests` breaks on an unrelated config change | Known and accepted, same as `IngestVolumeRegistrationTests` (§6.1). |
| The engineer is tempted to change 64 | Out of scope, in bold, three times. Stop and report. |
