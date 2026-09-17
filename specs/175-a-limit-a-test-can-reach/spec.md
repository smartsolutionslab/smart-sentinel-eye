# Spec 175 — A limit a test can reach

**Issue**: #2212 · **Branch**: `fix/2212-a-limit-a-test-can-reach` · **Phase**: 1 (Specify)
**Date**: 2026-09-17 · **Context**: `EventIngestion`
**Engineer**: `backend-engineer` · **Reviewer**: `backend-reviewer`
**Feature bucket**: spec `specs/020-durable-ingest-ack/` (FR-013), extended by
`specs/104-a-limiter-that-sheds-and-recovers/` (which filed this as **F2**)
**Follow-up filed**: **#2441** — the integration test this knob enables, split
out at the phase-3 gate. See §0.
**ADRs**: ADR-0105 (`Ensure.That` argument guards), ADR-0051 (per-context
`Add<Context>Infrastructure` registration), ADR-0036 (smallest change, no
speculative generality), ADR-0052 / ADR-0053 / ADR-0054 (xUnit + Shouldly,
sentence-style names, hand-written data), ADR-0084 (code metrics),
ADR-0109 (disjoint files), ADR-0139 / ADR-0144 (two testing obligations; phase
4a has two colours and no exemption), ADR-0037 (the phased workflow).
**Constitution**: §II (checked and **does not bind** — see §2.3), §Testing, §IV
(latency budget — **N/A**, see §7).
**New ADR needed**: **No.** This wires an existing constructor parameter to the
existing options-binding pattern. No design decision is made. See §8.

---

## 0. Scope: one story, and the one that was split out

Issue #2212 grew twice after it was filed. Its **most recent comment**
(2026-09-08) settles the scope authoritatively, and this spec follows it:

> The scope is deliberate and it is two things, not one: (1) Make
> `IngestWriteLimiter`'s concurrency configurable … **default unchanged at 64**.
> (2) Guard it. … They are one piece of work because (1) creates the
> reachability that makes (2) matter. Splitting them would land a config knob
> whose worst input fails silently.
>
> **Still explicitly out of scope** … the integration test the config knob
> enables. That needs its own red … and it should not ride along on a change
> that cannot demonstrate it.

So this spec is **one user story with two halves that ship together**, and the
integration test is **#2441**, filed before phase 4 began with the full
red-then-green design carried across — the two-step red sequence, the
problem-title defect injection, and the honest non-determinism framing. It is
blocked by this spec: the knob has to exist before the test can be written,
which is exactly why #2212 declined to include it.

**An earlier draft of this spec carried that test as a P2 story.** It was
removed, not deferred. Recorded because a spec that silently drops half its
scope is indistinguishable from one that forgot.

### 0.1 What is explicitly **not** in this spec

| Item | Why not |
|---|---|
| **The integration test.** | **#2441.** Out of scope by the issue's own scope note. |
| **Changing the production default from 64.** | The issue names this out of scope in bold. 64 stays 64 in the type default and in production. Whether 64 is right for a 250-camera fab is a separate issue wanting a measurement. |
| **A `WriteConcurrency` value object.** | Constitution §II binds domain models; this is Application-layer configuration. See §2.3. ADR-0105's `Ensure.That` is the guard the rule actually asks for. |
| **The MQTT path (`BoundedIngestChannel`, `FullMode = Wait`).** | #2211, and a design question with no ADR. It does not touch `IngestWriteLimiter` at all (§2.2). |
| **A non-zero acquisition timeout.** | Spec 104's review noted `slots.Wait(50)` passes all four existing unit tests. Real, but it is spec 104's uncovered clause and changing the gate is a behaviour change. |
| **`src/AppHost/AppHost.cs`.** | The `if (isE2ETests)` override line belongs to #2441. **This spec touches no AppHost file.** |

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

### 2.1 Why the two halves are one change

The issue's scope note puts it exactly right and the reasoning is worth keeping
in front of the engineer: *"(1) creates the reachability that makes (2) matter.
Splitting them would land a config knob whose worst input fails silently."*

Today `new IngestWriteLimiter(0)` is unreachable — the parameterless constructor
is the only caller and it passes 64. The moment concurrency comes from
configuration, `0` becomes an input an operator can supply, and the failure mode
is a service that reports healthy and answers `429` to **every** write, with
nothing in the log to say why. The singleton lives for the process, so it stays
that way.

### 2.2 Which endpoints this affects

The limiter guards exactly **two** routes, both in the `/events` group
(`src/EventIngestion/Api/EventsEndpoints.cs:24`):

- `POST /events/manual` (`EventsEndpoints.cs:30`) — `StoreOrRefuseAsync` at `EventsEndpoints.Writes.cs:105`
- `POST /events/webhook/{integrationName}` (`EventsEndpoints.cs:59`) — at `:171`

**Not** the event-type registry, and **not** the MQTT path — MQTT publishes go
through `BoundedIngestChannel` + `PersistenceLoopHostedService` and never touch
`IngestWriteLimiter`. Verified: the type is referenced only in
`EventsEndpoints.Writes.cs` and the registration line.

`src/EventIngestion/Api/` is **read-only in this spec**. Nothing on the request
path changes; only how the singleton is constructed.

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

## 3. User story

### US1 (P1) — The write limit can be set, and a bad setting fails loudly

*As an operator of a fab whose write throughput differs from the shipped
assumption, I can set the direct-write concurrency from configuration, and a
value that would silently refuse every event is refused at construction
instead.*

The only story. Its two halves ship together for the reason §2.1 gives.

**The default does not move.** `EventIngestion:IngestWrite:Concurrency` unset →
64, exactly as today.

---

## 4. Acceptance scenarios (Gherkin)

#### AS-1 — A configured value takes effect (the "binding does nothing" direction)

```gherkin
Given EventIngestion infrastructure is registered
  And configuration sets "EventIngestion:IngestWrite:Concurrency" to 2
When the IngestWriteLimiter singleton is resolved
  And two leases are taken and held
Then the third TryAcquire is refused
```

**New behaviour, and therefore RED** (§6). Today the singleton ignores
configuration and grants 64, so the third lease succeeds.

#### AS-2 — The shipped default is unchanged (the "smuggled behaviour change" direction)

```gherkin
Given EventIngestion infrastructure is registered
  And configuration sets no IngestWrite section
When the IngestWriteLimiter singleton is resolved
Then 64 leases are granted and the 65th is refused
```

**Characterisation, observed green before and after** (§6). It guards this
spec's own worst failure. It must be asserted **through the registration**, not
only on the type default, because the registration is what changes — and note
that AS-2 alone cannot tell the old registration from the new one, which is
precisely why AS-1 exists.

#### AS-3 — Zero is refused at construction (bad input, at the configuration boundary)

```gherkin
Given a concurrency of 0
When an IngestWriteLimiter is constructed
Then an ArgumentException is thrown naming "concurrency"
```

**New behaviour, and therefore RED.** And the same for a negative value — which
`SemaphoreSlim` already rejects, but with its own message; the guard makes the
two inputs answer alike.

#### AS-4 — The existing unit tests still hold

```gherkin
Given the four tests in IngestWriteLimiterTests
When the guard and the options binding are added
Then all four still pass, unmodified
```

**Characterisation, green.** Explicit because
`The_default_limiter_bounds_writes_at_sixty_four`
(`IngestWriteLimiterTests.cs:115`) asserts `DefaultConcurrency.ShouldBe(64)` in
both directions. Spec 104's review noted a configurable default "will need that
assertion revisited rather than deleted". **This spec keeps the default at 64,
so it needs neither** — it must pass untouched. Editing or deleting it is a
weakened gate (ADR-0144) and a review blocker.

#### Auth and bad-request — unchanged, and that is the assertion

The limiter sits **behind** `RequireAuthorization(Scope.Sse.Events.Write)`
(`EventsEndpoints.cs:31`) and behind body validation, so an unauthenticated
caller gets 401 and a malformed body gets 400 — never 429. Already covered by
`AnonymousIngestIsRefusedTests` and `MissingPayloadIsRefusedIntegrationTests`.
**No new test**; recorded so the engineer does not duplicate them, and so that a
redden in either reads as a finding rather than an inconvenience. Nothing in
this spec changes the request path, so neither should move.

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

### 5.3 No new configuration key is shipped

`src/EventIngestion/Api/appsettings.json` gains **nothing**. The default is the
C# property initialiser; writing `64` into `appsettings.json` too would put the
number in two places, and the next person to change one would not find the
other. Confirmed: the file contains no `IngestWrite` key today.

---

## 6. Phase-4a colour: **RED**, per piece

ADR-0144 requires the colour declared per obligation, and this slice has both.
**The two must not be collapsed** — see §6.2 for why that particular collapse is
dangerous here.

| Piece | Colour | Why |
|---|---|---|
| **AS-1** — a configured value bounds the registered limiter | **RED** | Behaviour-changing. The registration ignores configuration today; the test fails today. **The load-bearing red** — it is the defect #2212 names. |
| **AS-3** — zero and negative are refused at construction | **RED** | Behaviour-changing. `new IngestWriteLimiter(0)` constructs cleanly today and produces a limiter that refuses every write. |
| **AS-2** — the default is still 64, through the registration | **Characterisation, GREEN** | Behaviour-preserving. Captured passing **before** the change and must pass **unmodified** after. |
| **AS-4** — the four existing unit tests | **Characterisation, GREEN** | Same. Unmodified. An assertion that has to be edited is evidence the behaviour moved: block, don't adjust. |

**The slice as a whole is RED.** A test for AS-1 or AS-3 that arrives green is a
phase-4 failure, not a shortcut.

### 6.1 The reds, and what each must say when it fails

| # | Test | Red against | Expected failure |
|---|---|---|---|
| **R1** | AS-3 — `A_concurrency_of_zero_is_refused` | `IngestWriteLimiter.cs:29` unguarded | `Should throw ArgumentException but did not` — today `new IngestWriteLimiter(0)` constructs cleanly. |
| **R2** | AS-1 — `A_configured_concurrency_bounds_the_registered_limiter` | `EventIngestionInfrastructureModule.cs:117` parameterless | third `TryAcquire().Acquired` is `True`, expected `False` — the singleton grants 64 regardless of configuration. |

**Both verbatim failures are quoted in the PR body** (ADR-0139). R2's is the one
that matters: it is the exact defect the issue describes, in the test's own
words.

### 6.2 Why the binding half is **not** characterisation

Worth stating plainly, because it is an easy and expensive mistake. "The default
is unchanged" is true and is AS-2's job — but it describes the *preserved* half.
The *new* half is that a configured value is now honoured, and that is new
behaviour with a test that fails today.

Reading the binding as characterisation leads to writing AS-2 and skipping
AS-1 — and **AS-2 passes against the current, broken registration**, because a
parameterless `AddSingleton<IngestWriteLimiter>()` also yields 64. That is
precisely the "test wired to nothing" shape this whole issue exists to prevent.
**AS-1 is not optional.**

### 6.3 Defect-injection counterfactuals (phase 5, after green)

A red from absent wiring proves a test *notices* the feature. It does not prove
the test asserts what it claims. Two injections, each applied to `src/`,
observed, then reverted with `git checkout -- src/`:

| # | Injection | Predicted result | What it proves |
|---|---|---|---|
| **CF-A** | `EventIngestionInfrastructureModule` — revert the factory to `AddSingleton<IngestWriteLimiter>()` | **R2 red**, **R1 green**, **AS-2 green** | The **asymmetry is the finding**: AS-2 cannot tell the two registrations apart, so AS-2 alone would never have caught this. This is §6.2's argument, demonstrated rather than asserted. |
| **CF-B** | `IngestWriteOptions.Concurrency` initialiser — change `IngestWriteLimiter.DefaultConcurrency` to a literal `32` | **AS-2 red**, R1 green, R2 green | AS-2 genuinely pins the shipped default through the registration, so a silent change to it cannot pass. This is the counterfactual for the "do not change 64" constraint, and it is the one that makes that constraint enforced rather than merely instructed. |

**Predictions are written before the runs.** A mismatch is reported, not edited
into agreement.

### 6.4 The counterfactual this spec deliberately does **not** run

Injecting `Concurrency = 0` into a booted stack to watch the guard fire. R1
covers it at unit level, and a stack whose limiter refuses every write would
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

---

## 8. Why no ADR is needed

ADR-0144 forbids the autonomous lane writing an ADR, so this was checked rather
than assumed. Each half already has a decision on the books:

- **The options binding** — ADR-0051 fixes per-context registration in
  `Add<Context>Infrastructure`; the `AddOptions<T>().Bind(GetSection(...))`
  shape is the established form inside that very method.
- **The guard** — ADR-0105 mandates `Ensure.That`, and the `int` overload exists
  for this exact case.

**What would need an ADR, and is therefore excluded**: changing the default from
64 (a sizing decision on a 250-camera target), and adding startup-time options
validation (a repo-wide convention that does not exist). Both are §0.1
exclusions. If the phase-4 engineer finds itself wanting either, that is a stop
and a report, not a judgement call.

---

## 9. Independent end-to-end test procedure

Run by a human against a stack this spec's tests did not boot. Six steps, all
against the ordinary dev stack — this spec adds nothing to the test lane, so
there is nothing test-only to observe.

1. Stop any running AppHost. Start the dev stack: `dotnet run --project src/AppHost`.
2. Mint an operator token and `POST /events/manual` once. **Expect `201`.** This
   is the "default unchanged" observation: the shipped configuration names no
   concurrency and the write succeeds exactly as before.
3. Stop the stack. Add `"EventIngestion": { "IngestWrite": { "Concurrency": 1 } }`
   to `src/EventIngestion/Api/appsettings.Development.json`. Restart.
4. Issue eight concurrent `POST /events/manual`. **Expect at least one `429`**
   whose body has `"title": "EVENT_INGEST_BACKPRESSURE"`, **and at least one
   `201`**. This is the observation that a configured value reaches production
   code through ordinary configuration. *(Automating this assertion is #2441;
   here it is observed by hand, which is what phase 5 is for.)*
5. Set `Concurrency` to `0`. Restart and issue one write. **Expect the request to
   fail with an `ArgumentException` naming `concurrency`** in the
   `event-ingestion` log — *not* a silent 429. This is the guard, observed, and
   it is the step that distinguishes this change from the one the issue warned
   would "land a config knob whose worst input fails silently".
6. Remove the configuration. Restart. Repeat step 2. **Expect `201`** — the
   default is back at 64 and nothing was left behind.

**Record the figures observed, in the verification note, as observed** — not
only to the orchestrator. A measurement reported only in conversation is
invisible to every later grep.
