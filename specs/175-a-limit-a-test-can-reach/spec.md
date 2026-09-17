# Spec 175 — A limit a test can reach

**Issue**: #2212 · **Branch**: `fix/2212-a-limit-a-test-can-reach` · **Phase**: 1 (Specify)
**Date**: 2026-09-17 · **Context**: `EventIngestion`
**Engineer**: `backend-engineer` · **Reviewer**: `backend-reviewer`
**Feature bucket**: spec `specs/020-durable-ingest-ack/` (FR-013), extended by
`specs/104-a-limiter-that-sheds-and-recovers/` (which filed this as **F2**)
**ADRs**: ADR-0105 (`Ensure.That` argument guards), ADR-0051 (per-context
`Add<Context>Infrastructure` registration), ADR-0103 (integration tests are
Aspire-only), ADR-0036 (smallest change, no speculative generality),
ADR-0052 / ADR-0053 / ADR-0054 (xUnit + Shouldly, sentence-style names,
hand-written data), ADR-0084 (code metrics), ADR-0109 (disjoint files),
ADR-0139 / ADR-0144 (two testing obligations; phase 4a has two colours and no
exemption), ADR-0143 (`POST` is not retried — load-bearing here, see §5.4),
ADR-0037 (the phased workflow).
**Constitution**: §II (checked and **does not bind** — see §2.3), §Testing, §IV
(latency budget — **N/A**, see §7).
**New ADR needed**: **No.** This wires an existing constructor parameter to the
existing options-binding pattern. No design decision is made. See §8.

---

## 0. Scope, and the one thing this spec had to settle before writing

Issue #2212 grew twice after it was filed, and its own comments disagree with
the brief this spec was written from. **That disagreement is recorded here
rather than resolved silently**, because it decides whether US2 exists.

| Source | Says about the integration test |
|---|---|
| Issue **body** | *"Do not add the integration test in the same change **without a red**."* — permits it, conditional on a red. |
| Issue **comment 2** (scope note) | *"Still explicitly out of scope … the integration test the config knob enables. That needs its own red … and it should not ride along on a change that cannot demonstrate it."* |

The two are reconcilable and this spec reads them as one condition: **the test
may land here if and only if it is observed red against the unwired code
first.** A change that *can* demonstrate the test is not the change the comment
excludes; the comment excludes a test that arrives green beside its own wiring.

**So US2 is specified, sequenced behind its own red, and kept independently
droppable.** US1 ships alone and is useful alone. If the phase-3 reviewer reads
comment 2 as an absolute bar, **drop US2 and file it** — US1 needs no rework,
and the only orphan is one `AppHost.cs` line that goes with it. This is flagged
at the gate (tasks.md §Gate) rather than decided by an agent.

### 0.1 What is explicitly **not** in this spec

| Item | Why not |
|---|---|
| **Changing the production default from 64.** | The issue names this out of scope in bold and this spec does not answer it. 64 stays 64 in the type default and in production. Whether 64 is right for a 250-camera fab is a separate issue with a separate measurement. |
| **A `WriteConcurrency` value object.** | Constitution §II binds domain models; this is Application-layer configuration. See §2.3. ADR-0105's `Ensure.That` is the guard the rule actually asks for. |
| **The MQTT path (`BoundedIngestChannel`, `FullMode = Wait`).** | #2211, and a design question with no ADR. It does not touch `IngestWriteLimiter` at all (§2.2). |
| **A non-zero acquisition timeout.** | Spec 104's review noted `slots.Wait(50)` passes all four existing unit tests. Real, but it is spec 104's uncovered clause and changing the gate is a behaviour change. |
| **A load or throughput assertion.** | The whole point of the knob is that no load generator is needed. |

---

## 1. The premise, checked by content before specifying

Every claim in the issue was re-read against the working tree at `ccc4262e`
(origin/develop, fetched 2026-09-17). **All hold; two line numbers have
drifted.**

| Claim | Verdict | Evidence |
|---|---|---|
| The limiter answers `429 EVENT_INGEST_BACKPRESSURE` when saturated | **Holds** | `src/EventIngestion/Api/EventsEndpoints.Writes.cs` — `TryAcquire()` at `:344`, the `Results.Problem` block at `:347-350`, `title: "EVENT_INGEST_BACKPRESSURE"` at `:348`, `Status429TooManyRequests` at `:350`. The issue cites `:338-350`; the refusal block is `:344-351`. |
| Registered as a singleton through the **parameterless** constructor | **Holds** | `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs:117` — `builder.Services.AddSingleton<IngestWriteLimiter>();`. The issue cites `:101`; it is **`:117`** today. Spec 104 also said `:101`, so the drift predates the issue. |
| The type already accepts `concurrency` | **Holds** | `src/EventIngestion/Application/Ingress/IngestWriteLimiter.cs:27-29` — `IngestWriteLimiter() : this(DefaultConcurrency)` and `IngestWriteLimiter(int concurrency)`. `DefaultConcurrency = 64` at `:23`. |
| Spec 104's unit tests use `new IngestWriteLimiter(concurrency: 2)` | **Holds** | `tests/EventIngestion.Application.Tests/Ingress/IngestWriteLimiterTests.cs:48`, `:69` (`concurrency: 2`), `:93` (`concurrency: 1`), `:115` pins `DefaultConcurrency.ShouldBe(64)`. |
| The `int` constructor has no guard | **Holds** (issue comment 1) | `IngestWriteLimiter.cs:29` is an expression body straight into `new SemaphoreSlim(concurrency, concurrency)`. `SemaphoreSlim` rejects negatives; **`0` passes**. |
| #625 / spec 104 is in the state described | **Holds** | #625 **CLOSED / COMPLETED** 2026-09-08. `specs/104-a-limiter-that-sheds-and-recovers/` exists with spec/plan/tasks. Its `tasks.md` files this work as **F2**, and its `spec.md` §5.2 states the untestability in the same terms. |
| #2211 is the other backpressure mechanism | **Holds**, context only | #2211 OPEN — *"The MQTT ingest channel blocks rather than sheds, and that choice lives only in a class comment."* Not touched here. |

### 1.1 One premise the issue states that is **not quite right**, corrected here

Issue comment 1 says *"a missing key binding to `0` is exactly the input that
gets constructed"*. With the options pattern this spec uses, **a missing key
does not produce `0`** — `.Bind()` only overwrites keys that are present, so an
absent section leaves the C# property initialiser (`64`) intact.

The reachable bad inputs are narrower, and the guard is still required:

1. the key present and set to `0` or a negative (`"EventIngestion:IngestWrite:Concurrency": "0"`);
2. a typo'd section that binds nothing — *safe*, falls back to 64;
3. a non-numeric value — options binding throws on first `.Value` read, before the guard.

So the guard's job is (1), and (1) alone justifies it: it is the input that
produces a service which looks healthy and refuses every write. **Stated here so
the engineer does not write a test for a failure mode that cannot occur** and
then conclude the guard is unreachable.

---

## 2. The gap, stated precisely

### 2.1 What is untestable, and why

`StoreOrRefuseAsync` is the only consumer. Two lines connect the limiter to an
HTTP answer — take a lease, and if refused return the 429 — and **no test above
unit level executes them.** Reaching 64 genuinely-concurrent in-flight writes
over HTTP needs hundreds of simultaneous POSTs racing a write that commits in
single-digit milliseconds: the flaky load test spec 104 declined to write.

And the escape hatch does not exist either. The Aspire fixture
(`tests/Integration.Tests/Fixtures/AspireFixture.cs:273-302`) boots the AppHost,
which launches `event-ingestion` as a **separate process**. A test holds an
`HttpClient`, not the service's `IServiceProvider`; it cannot reach in and
saturate the singleton.

### 2.2 Which endpoints this actually covers

The limiter guards exactly **two** routes, both in the `/events` group
(`src/EventIngestion/Api/EventsEndpoints.cs:24`):

- `POST /events/manual` (`EventsEndpoints.cs:30`) — `StoreOrRefuseAsync` at `EventsEndpoints.Writes.cs:105`
- `POST /events/webhook/{integrationName}` (`EventsEndpoints.cs:59`) — at `:171`

**Not** the event-type registry, and **not** the MQTT path — MQTT publishes go
through `BoundedIngestChannel` + `PersistenceLoopHostedService` and never touch
`IngestWriteLimiter`. Verified: the type is referenced only in
`EventsEndpoints.Writes.cs` and the registration line.

### 2.3 Constitution §II was checked and does **not** bind here

§II bans primitive-typed state on **a domain model**. `IngestWriteLimiter` and
any options class are Application-layer. `PrimitiveBoundaryTests`
(`tests/Architecture.Tests/PrimitiveBoundaryTests.cs`) loads only
`SmartSentinelEye.*.Domain.dll` and walks outward from aggregate roots, so an
Application options class is out of scope on **both** counts — different
assembly, unreachable from any root.

Confirmed by consistent practice rather than by exemption: **every** options
class in the repo carries raw primitives, including `IngestRetryOptions`
(`TimeSpan`) and `AuditMeasurementOptions` (`bool`) — types that *are* in §II's
banned set and would fail the rule if it reached them.

So issue comment 1's parenthetical — *"or a `WriteConcurrency` value object,
which is the shape the rest of the codebase would reach for"* — is **not what
the rest of the codebase does for configuration**, and this spec takes the
`Ensure.That` branch the same sentence offers first. That is also ADR-0105's own
wording: the `int` overload of `Ensure.That` exists specifically "in place of a
numeric-range `throw new ArgumentException` precondition".

---

## 3. User stories

### US1 (P1) — The write limit can be set, and a bad setting fails loudly

*As an operator of a fab whose write throughput differs from the shipped
assumption, I can set the direct-write concurrency from configuration, and a
value that would silently refuse every event is refused at construction
instead.*

**Independently shippable.** It is a complete, observable change on its own: a
configured value takes effect, an invalid one throws. It does not depend on US2,
and US2 can be dropped without touching it.

**The default does not move.** `EventIngestion:IngestWrite:Concurrency` unset →
64, exactly as today.

### US2 (P2) — The 429 is proved off the real endpoint

*As a reviewer, I can see that a refused lease actually becomes a
`429 EVENT_INGEST_BACKPRESSURE` over HTTP, rather than trusting two lines that
no test executes.*

Depends on US1. **Must be observed red against the unwired code first** (§6).

---

## 4. Acceptance scenarios (Gherkin)

### US1

#### AS-1 — A configured value takes effect (the "binding does nothing" direction)

```gherkin
Given EventIngestion infrastructure is registered
  And configuration sets "EventIngestion:IngestWrite:Concurrency" to 2
When the IngestWriteLimiter singleton is resolved
  And two leases are taken and held
Then the third TryAcquire is refused
```

This is the **red**: today the singleton ignores configuration and grants 64, so
the third lease succeeds.

#### AS-2 — The shipped default is unchanged (the "smuggled behaviour change" direction)

```gherkin
Given EventIngestion infrastructure is registered
  And configuration sets no IngestWrite section
When the IngestWriteLimiter singleton is resolved
Then 64 leases are granted and the 65th is refused
```

AS-2 guards this spec's own worst failure. It must be asserted **through the
registration**, not only on the type default, because the registration is what
changes.

#### AS-3 — Zero is refused at construction (bad input, at the configuration boundary)

```gherkin
Given a concurrency of 0
When an IngestWriteLimiter is constructed
Then an ArgumentException is thrown naming "concurrency"
```

And the same for a negative value — which `SemaphoreSlim` already rejects, but
with its own message; the guard makes the two inputs answer alike.

#### AS-4 — The existing unit tests still hold

```gherkin
Given the four tests in IngestWriteLimiterTests
When the guard and the options binding are added
Then all four still pass, unmodified
```

Explicit because `The_default_limiter_bounds_writes_at_sixty_four`
(`IngestWriteLimiterTests.cs:115`) asserts `DefaultConcurrency.ShouldBe(64)` in
both directions. Spec 104's review noted a configurable default "will need that
assertion revisited rather than deleted". **This spec keeps the default at 64,
so it needs neither** — it must pass untouched. Editing or deleting it is a
weakened gate (ADR-0144) and a review blocker.

### US2

#### AS-5 — Overlapping writes are refused off the real endpoint (the wiring, end to end)

```gherkin
Given the Aspire fixture is running with write concurrency set to 1
  And an authenticated operator for a provisioned fab
When eight POSTs to /events/manual are issued concurrently
Then at least one answers 429 with problem title "EVENT_INGEST_BACKPRESSURE"
  And at least one answers 201 Created
```

**Both clauses are load-bearing.** The 201 clause is what distinguishes a
working limiter from one stuck refusing everything — precisely the
concurrency-zero failure AS-3 guards, observed from the outside.

#### AS-6 — Auth

```gherkin
Given no bearer token
When a POST to /events/manual is issued
Then the answer is 401, not 429
```

Already covered by `AnonymousIngestIsRefusedTests`. Restated so the phase-4
engineer does not duplicate it, and to record that the limiter sits **behind**
`RequireAuthorization(Scope.Sse.Events.Write)` (`EventsEndpoints.cs:31`) —
backpressure is never the answer to an unauthenticated caller.

#### AS-7 — Bad request

```gherkin
Given an authenticated operator and a malformed body
When a POST to /events/manual is issued
Then the answer is 400, not 429
```

Covered by `MissingPayloadIsRefusedIntegrationTests`. Restated for the same
reason: validation precedes `StoreOrRefuseAsync`, so a lowered limit must not
turn a 400 into a 429. **Not a new test** — but if either of these existing
tests reddens under concurrency 1, US2's AppHost line is wrong and must be
reported, not worked around.

---

## 5. The mechanism, and which existing pattern each half mirrors

Nothing here is invented. Every piece has a named precedent in this repo.

### 5.1 The options class — mirrors `IngestRetryOptions`, same folder

`src/EventIngestion/Application/Ingress/IngestWriteOptions.cs`, sibling of
`IngestWriteLimiter.cs` and of `IngestRetryOptions.cs`:

```csharp
public sealed class IngestWriteOptions
{
    public const string SectionName = "EventIngestion:IngestWrite";

    public int Concurrency { get; set; } = IngestWriteLimiter.DefaultConcurrency;
}
```

The initialiser references `DefaultConcurrency` rather than repeating `64`, so
the constant stays the single source and `IngestWriteLimiterTests:115` keeps
pinning it.

### 5.2 The binding — mirrors `EventIngestionInfrastructureModule.cs:134-138`

`AddOptions<T>().Bind(config.GetSection(T.SectionName))`, added beside the
`IngestRetryOptions` binding; `AddSingleton<IngestWriteLimiter>()` at `:117`
becomes a factory that reads `IOptions<IngestWriteOptions>.Value.Concurrency`.

**`ValidateOnStart` / `ValidateDataAnnotations` are deliberately not used**:
there is not one call site in `src/`, and inventing one here would be a new
mechanism (ADR-0036). The consequence is stated rather than hidden — a
configured `0` throws when the singleton is first resolved (on the first write
request), not at startup. That is strictly better than today's silent
refuse-everything, and it matches every other options class in the repo. If
startup-time validation is wanted, it is a repo-wide question and a different
issue.

### 5.3 The test-lane override — mirrors `AppHost.cs:501-514`

**There is no per-test configuration hook and this spec does not add one.** The
fixture is an `ICollectionFixture`: one AppHost boot per assembly, from a
hard-coded `string[] parameters` containing `"E2ETests=true"`
(`AspireFixture.cs:275-281`). The **only** channel by which the integration lane
configures a service differently is a `WithEnvironment` line inside
`if (isE2ETests)` at `AppHost.cs:501`, which already carries two:

```csharp
auditObservability.WithEnvironment("AuditObservability__Retention__TickInterval", "00:00:03");
auditObservability.WithEnvironment("AuditObservability__Measurement__RecordIngestBreakdown", "true");
```

Spec 109 cites the first as *the* precedent for exactly this move. US2 adds a
third, on the `eventIngestion` local captured at `AppHost.cs:405`:

```csharp
eventIngestion.WithEnvironment("EventIngestion__IngestWrite__Concurrency", "1");
```

### 5.4 Why a stack-wide concurrency of 1 is safe — checked, not assumed

This is the one genuinely risky consequence, so it was verified rather than
argued.

| Risk | Finding |
|---|---|
| Other integration tests contend for the single slot | Every integration test is `[Collection(AspireCollection.Name)]` — **one** collection, so xUnit runs them **sequentially**. One in-flight HTTP write at a time; one slot suffices. |
| A test that itself issues concurrent writes | Only two exist. `EventTypeRegistryConcurrencyIntegrationTests` (three `Task.WhenAll` blocks) hits the **event-type** endpoints, which do not call `StoreOrRefuseAsync`. `IngestThroughputMeasurementTests` uses `Parallel.ForAsync` but publishes over **MQTT** (`MqttClientFactory`, `PublishAsync`), bypassing the limiter entirely — and is `[Trait("Category", "Measurement")]` besides. |
| A latency test serialised by the lowered limit | `AcceptToDecideLatencyTests` is a **run-mode** test keyed on `SSE_RUNMODE_*` and skips unless pointed at an externally-booted stack. `E2ETests=true` is never set there. |
| Dev and production affected | No. The line is inside `if (isE2ETests)`; `dotnet run` on the AppHost sets nothing and the options default stands at 64. |
| The fixture's `HttpClient` retries the 429 away | **No — and this is what makes AS-5 observable at all.** `FixtureHttpClients.Configure` applies `IdempotentRetry.RetryIdempotentMethodsOnly` (ADR-0143, #2129), so a `POST` gets one attempt. Had the fixture kept the library's outcome-based predicate, the retry handler would have swallowed the 429 and AS-5 would have failed for a reason with nothing to do with the limiter. |

### 5.5 Determinism of AS-5, stated honestly

AS-5 is **not** formally deterministic and this spec does not claim it is. With
concurrency 1, a 429 requires two requests inside `StoreOrRefuseAsync` at the
same instant. Eight `PostAsJsonAsync` calls awaited through one `Task.WhenAll`
arrive within microseconds of each other and each holds its lease across a real
Postgres round-trip of single-digit milliseconds, so overlap is
overwhelming — but it is probabilistic, and saying otherwise would be the
overclaim this repository keeps correcting.

**It is a different risk class from the test spec 104 declined**, and that
difference is the whole justification for this issue:

| | `[T091]`'s load test | AS-5 |
|---|---|---|
| Concurrent in-flight writes needed | **64** | **2** |
| Load generator | 5 000 ev/s for 30 s | 8 POSTs, once |
| Runtime | ~30 s | well under a second |
| Fails when | the runner is slow, the DB is fast, the burst disperses | only if the server serialises eight simultaneous requests end to end |

**No retry loop, no wall-clock bound, no `while`.** If AS-5 ever flakes, the
answer is to raise the request count, never to loop until a 429 appears — a loop
would turn a real regression into a slow pass.

---

## 6. Phase-4a colour: **RED**, explicit, with the counterfactual plan

Both user stories change behaviour, so ADR-0139/ADR-0144's first obligation
applies and there is no ambiguity to resolve toward red — it *is* red. **A test
in this spec that arrives green is a phase-4 failure.**

### 6.1 The reds, and what each must say when it fails

| # | Test | Red against | Expected failure |
|---|---|---|---|
| **R1** | AS-3 — `A_concurrency_of_zero_is_refused` | `IngestWriteLimiter.cs:29` unguarded | `Should throw ArgumentException but did not` — today `new IngestWriteLimiter(0)` constructs cleanly. |
| **R2** | AS-1 — `A_configured_concurrency_bounds_the_registered_limiter` | `EventIngestionInfrastructureModule.cs:117` parameterless | third `TryAcquire().Acquired` is `True`, expected `False` — the singleton grants 64 regardless of configuration. |
| **R3** | AS-5 — `Overlapping_direct_writes_are_refused_with_backpressure` | the whole chain unwired | no response has status 429; eight 201s. |

**R2 is the load-bearing red.** R1 would pass with the guard and no binding at
all; R3 is expensive and boots the stack. R2 is the cheap test that fails for
exactly the reason this issue exists.

### 6.2 R3's red must be observed against the **unwired** state

This is the anti-pattern the issue names, so the sequence is mandatory and is
its own task, not folded into the wiring:

1. Write R3. Run it on the branch with **no** production change — no options
   class, no factory registration, no `AppHost.cs` line. **Capture the verbatim
   failure.**
2. Land US1 (options class, guard, registration). Run R3 again. It is **still
   red** — the AppHost still passes no value, so concurrency is still 64. This
   second observation is worth capturing too: it proves R3 is testing the
   *delivered* value, not merely the existence of a config class.
3. Land the `AppHost.cs:501` line. R3 goes **green**.

A test that goes green at step 2 is wired to nothing and must be reported.

### 6.3 Defect-injection counterfactuals (phase 5, after green)

A red from absent wiring proves the test *notices the feature*. It does not
prove the test asserts what it claims. Three injections, each applied to `src/`,
observed, then reverted with `git checkout -- src/`:

| # | Injection | Predicted result | What it proves |
|---|---|---|---|
| **CF-A** | `EventsEndpoints.Writes.cs:348` — change the title to `"EVENT_INGEST_OVERLOAD"` | **R3 red** on the title assertion; the 429 status still appears | R3 asserts the problem title, not merely a status code. Without this, R3 would pass against any 429 from anywhere — including the gateway's own rate limiter. |
| **CF-B** | `IngestWriteLimiter.cs` — drop the `slots.Wait(0)` gate in `TryAcquire`, always grant. Currently `:36-37`; **locate it by content**, since T004's guard shifts every line below `:29`. | **R3 red** (no 429), **R2 red**, R1 green | The 429 comes from the limiter, not from the server shedding load some other way. |
| **CF-C** | `EventIngestionInfrastructureModule` — revert the factory to `AddSingleton<IngestWriteLimiter>()` | **R2 red**, **R3 red**, **R1 green**, AS-2 green | The asymmetry is the point: the *binding* is what R2 and R3 test, and AS-2 (the default) cannot tell the two registrations apart — which is why AS-2 alone is not sufficient coverage. |

**Predictions are written before the runs.** A mismatch is reported, not edited
into agreement.

### 6.4 The counterfactual this spec deliberately does **not** run

Injecting `Concurrency = 0` to watch the guard fire at runtime. R1 covers it at
unit level, and a stack booted with a limiter that refuses every write would
redden a dozen unrelated integration tests for no added information.

---

## 7. Latency budget

**N/A.** No leg of constitution §IV is touched.

The event-to-overlay path's first leg is *camera → SFU*; direct HTTP ingest is
not on any of the six legs, which is the same reading spec 104 recorded (§6) for
the same component.

More to the point, **the production instruction stream is unchanged**. The
concurrency reaches the limiter once, at singleton construction; the per-request
path (`TryAcquire`, the lease, the release) is byte-for-byte what it is today,
and with the default still 64 the semaphore is initialised identically. The only
production-observable difference is that a configured `0` now throws instead of
refusing every write in silence.

The E2E-only value of 1 lives inside `if (isE2ETests)` and reaches no
measurement: §5.4 records that the only latency test in the suite
(`AcceptToDecideLatencyTests`) is run-mode and never sees `E2ETests=true`.

---

## 8. Why no ADR is needed

ADR-0144 forbids the autonomous lane writing an ADR, so this was checked rather
than assumed. Each half already has a decision on the books:

- **The options binding** — ADR-0051 fixes per-context registration in
  `Add<Context>Infrastructure`; the `AddOptions<T>().Bind(GetSection(...))`
  shape is the established form inside that very method.
- **The guard** — ADR-0105 mandates `Ensure.That`, and the `int` overload exists
  for this exact case.
- **The E2E override** — an established mechanism with two existing users at
  `AppHost.cs:501-514`.
- **The test level** — ADR-0103 settles that integration means Aspire.

**What would need an ADR, and is therefore excluded**: changing the default from
64 (a sizing decision on a 250-camera target), and adding startup-time options
validation (a repo-wide convention that does not exist). Both are §0.1
exclusions. If the phase-4 engineer finds itself wanting either, that is a stop
and a report, not a judgement call.

---

## 9. Independent end-to-end test procedure

Run by a human against a stack this spec's tests did not boot.

1. Stop any running AppHost. Start the dev stack: `dotnet run --project src/AppHost`.
   **No `E2ETests` flag** — this is the production-shaped lane.
2. Mint an operator token and `POST /events/manual` once. **Expect `201`.** This
   is the "default unchanged" observation: the shipped configuration names no
   concurrency and the write succeeds exactly as before.
3. Stop the stack. Add `"EventIngestion": { "IngestWrite": { "Concurrency": 1 } }`
   to `src/EventIngestion/Api/appsettings.Development.json`. Restart.
4. Issue eight concurrent `POST /events/manual`. **Expect at least one `429`**
   whose body has `"title": "EVENT_INGEST_BACKPRESSURE"`, **and at least one
   `201`**. This is the observation that the value reaches production code
   through ordinary configuration, not only through the AppHost's test-lane line.
5. Set `Concurrency` to `0`. Restart and issue one write. **Expect the request to
   fail with an `ArgumentException` naming `concurrency`** in the
   `event-ingestion` log — *not* a silent 429. This is the guard, observed.
6. Remove the configuration. Restart. Repeat step 2. **Expect `201`** — the
   default is back at 64 and nothing was left behind.

**Record the figures observed, in the verification note, as observed** — not
only to the orchestrator. A measurement reported only in conversation is
invisible to every later grep.
