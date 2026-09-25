# Spec 250 — The backoff no test could reach

**Issue:** #2449 (`agent:ready`, `tech-debt`, Project #13 → In Progress)
**Branch:** `fix/2449-mqtt-simulator-drop-hold`
**Lane:** autonomous (ADR-0144)
**Found by:** spec 179 (#2233), audit row **G2** — `specs/179-a-fake-that-refuses-a-live-reconnect/spec.md` §2
**ADRs:** ADR-0111 (the Scenario Simulator, dev-only), ADR-0036 (smallest change,
no speculative generality), ADR-0052 / ADR-0054 (hand-written fakes and test
data), ADR-0053 (test naming), ADR-0084 (code metrics, advisory), ADR-0139 +
constitution §Testing (refactors stay green; new behaviour starts red), ADR-0150
(wait for a condition, not a count), ADR-0037 / ADR-0144 (phases, lane).
**No new ADR** — the reasoning is §3, because the issue asked for exactly that
call.

---

## 1. Premise check (done before planning)

Every claim in the issue was read off the files, not carried forward.

| Claim | Verified | Where |
|---|---|---|
| The simulator's fake has no drop/hold controls | yes | `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs` — no `DropEveryConnectionImmediately`, no `HoldEveryConnectionFor`; its only drop is the test-driven `DropAsync()` |
| EventIngestion's fake has both | yes | `tests/EventIngestion.Infrastructure.Tests/Fakes/FakeMqttClient.cs` — `DropEveryConnectionImmediately` (drop raised from inside `SubscribeAsync`) and `HoldEveryConnectionFor(TimeSpan)` (drop scheduled from `SubscribeAsync`) |
| `MqttPublisher` runs the same `ResetIfHeld` logic | yes, **byte-identical** | `src/ScenarioSimulator/Mqtt/MqttBackoff.cs` vs the `MqttBackoff` class at the foot of `src/EventIngestion/Infrastructure/Ingress/MqttConnectionLoop.cs` — diffed with doc comments stripped: no difference. Called at the same point: `HoldConnectionAsync`, after `drop.WaitAsync` |
| The backoff is a hardcoded `new()` at 1 s / 30 s | yes | `MqttPublisher.cs:67` — `private readonly MqttBackoff backoff = new();`; `MqttBackoff()` chains to `(1 s, 30 s)` |
| Nothing in the simulator's suite reaches `ResetIfHeld` | yes | no test in `tests/ScenarioSimulator.Tests/` names `MqttBackoff` or `ResetIfHeld`; the one test that observes the loop's timing (`A_stale_disconnect_landing_mid_connect…`) runs against the production 1 s floor and asserts on connection counts, not on the backoff |
| #2451 overlaps this file | **no** | PR #2585 changes **EventIngestion's** fake only (`git diff --name-only origin/develop origin/fix/2451-mqtt-fake-thread-safety`). No merge interaction with this spec |

**One thing the issue does not say, recorded because it sizes the gap.** The
simulator's `MqttBackoff` has **no direct tests at all** — EventIngestion has
`MqttBackoffTests`, the simulator's copy has nothing. The code is identical today,
so EventIngestion's tests describe it by proxy; this spec's loop tests will cover
`ResetIfHeld` indirectly. A direct `MqttBackoffTests` copy is **recorded, not in
scope** (§7).

---

## 2. What "done" means

The simulator's suite can construct, and **observe**, both failure shapes the
issue names — and each new assertion is shown able to fail:

1. **A connection that dies on arrival** (a session takeover's CONNACK-then-close)
   is retried with a delay, not a spin.
2. **A connection held just past the backoff floor** still backs off — the
   takeover-flap case the doubled yardstick exists for.
3. **A connection that genuinely held** clears the debt, so the reconnect after it
   does not inherit the outage's backoff. This is the other side of the same
   boundary; without it a `ResetIfHeld` that never reset would pass 1 and 2.

Observed deterministically means against an **injected** fast backoff
(100 ms / 400 ms), not the production 1 s / 30 s, which would put a flap window at
tens of seconds.

---

## 3. The scope decision — inject, and no ADR

### 3.1 Every `MqttPublisher` constructor call site

| Call site | Constructor | Effect of this change |
|---|---|---|
| `src/ScenarioSimulator/Timeline/BilletTimelineExtensions.cs:23` — `builder.Services.AddSingleton<MqttPublisher>()` | **public** 3-arg | **None.** The public constructor's signature does not change. Microsoft DI selects among *public* constructors only, so a parameter added to the `internal` one is invisible to it |
| `src/ScenarioSimulator/Timeline/BilletTimelineHostedService.cs:19` | — (consumes the singleton) | None |
| `src/ScenarioSimulator/Mqtt/MqttPublisher.cs:77` — public ctor chains `: this(options, tokens, logger, new MqttClientFactory().CreateMqttClient())` | internal 4-arg | Source unchanged; the optional parameter takes its default, which is the backoff it constructs today |
| `tests/ScenarioSimulator.Tests/MqttPublisherDropAccountingTests.cs:259` | internal 4-arg | Source unchanged (optional parameter) |
| `tests/ScenarioSimulator.Tests/MqttPublisherProtocolPinTests.cs:41` | internal 4-arg | Source unchanged (optional parameter) |

No other assembly references `MqttPublisher`: the class is in the simulator's own
project, and `InternalsVisibleTo` names only its test assembly.

### 3.2 The change

The **internal** constructor gains one optional parameter, `MqttBackoff? backoff =
null`, resolved to `new MqttBackoff()` — the same 1 s / 30 s it uses today. The
field becomes constructor-assigned instead of field-initialised. That is the whole
production diff.

### 3.3 Why this is lane-safe and not an ADR

An ADR records a decision with architectural reach — a public surface others
depend on, a configuration or deployment surface, a cross-context contract, a
pattern the codebase must now follow. The change here has none of those:

- **No public surface moves.** `MqttPublisher`'s public constructor is untouched,
  and both `MqttPublisher(… IMqttClient)` and `MqttBackoff` are `internal`. The only
  callers that can pass the new argument are the simulator's own tests.
- **No configuration surface is created.** The backoff is **not** made
  `IOptions<T>`-driven, is not bound from `SimulatorOptions`, is not registered in
  DI. That would be a new operational knob for a need that does not exist
  (ADR-0036's "no speculative generality") — *and it would be the version that
  needs a decision*, which is why it is explicitly out of scope (§7).
- **No new pattern.** It is the pattern already in production on the other side of
  this pair: `MqttConnectionLoop(MqttConnection, MqttBackoff, MosquittoOptions,
  ILogger)` takes the backoff by constructor, and `MqttSubscriberHostedService`
  passes `new MqttBackoff()`. `MqttPublisher`'s own doc comment says *"A change to
  one of these loops belongs in the other unless it is on that list"*; the backoff
  seam is not on that list, so this **closes** a drift between the twins rather
  than opening one. It is also the same kind of seam as the `IMqttClient`
  parameter the internal constructor already has, whose doc comment gives the
  identical reason (behaviour to control, not a network).
- **No observable production behaviour changes.** Same type, same arguments, same
  construction time (once per publisher instance — it is a singleton either way).
- **The simulator is dev-only** (ADR-0111), outside every production path and the
  latency budget.

Had the investigation found any of: a public or cross-assembly caller of the
internal constructor, a need to vary the backoff at runtime, a DI registration
that would have to change, or backoff values relied upon elsewhere (e.g. a test or
dashboard asserting on the 1 s / 30 s figures through the public path) — this spec
would have stopped and handed the call back. None exists.

**The one cost, accepted in writing:** the internal constructor goes from four
parameters to five, which ADR-0084's advisory S107 (limit 4) reports as a warning.
It is carved out of `TreatWarningsAsErrors` (`Directory.Build.props`), so the
Release build stays green. The alternatives are worse: a parameter object for a
test seam is speculative structure, and a settable property would make the
backoff mutable after the loop starts.

---

## 4. User stories

### US1 (P1) — The simulator's suite can construct and observe both takeover shapes

**As** whoever next changes either MQTT connection loop,
**I want** the simulator's suite to fail when `MqttPublisher`'s backoff stops
distinguishing a connection that held from one that merely arrived,
**so that** the twin loops are guarded equally and a regression in the simulator's
copy is not caught only by luck in EventIngestion's.

One slice. It ships alone, it is observable in a unit run, nothing depends on it.

#### Acceptance scenarios

```gherkin
Scenario: a connection that dies on arrival is retried with a delay, not a spin (happy path)
  Given the publisher runs with a 100 ms / 400 ms backoff
    And the fake drops every connection the moment it is answered
  When the publisher's loop runs for one second
  Then fewer than 20 CONNECTs were answered
```

```gherkin
Scenario: a connection held just past the floor still backs off (conflict case)
  Given the publisher runs with a 100 ms / 400 ms backoff
    And the fake holds every connection for 110 ms and then drops it
  When the publisher's loop runs for three seconds
  Then fewer than 16 CONNECTs were answered
```

```gherkin
Scenario: a connection that held clears the debt
  Given the publisher runs with a 100 ms / 400 ms backoff
    And the first two CONNECTs are refused
  When the third connects, is held for 900 ms, and is then dropped
  Then the next CONNECT is answered within 150 ms
```

```gherkin
Scenario: existing callers see the backoff they always had (bad-request / no-argument case)
  Given MqttPublisher is constructed without a backoff — through DI, or by the existing tests
  Then it backs off at 1 s / 30 s exactly as before
    And every existing ScenarioSimulator test passes unmodified
```

```gherkin
Scenario: the fake's new controls model a connection that was up (fake contract)
  Given the fake is set to drop every connection immediately
  When a CONNECT is answered Success
  Then exactly one disconnect is raised, with ClientWasConnected = true
    And IsConnected is false afterwards
  Given instead the fake is set to hold every connection for a duration
  When a CONNECT is answered Success
  Then IsConnected is true immediately afterwards
    And a disconnect with ClientWasConnected = true arrives no earlier than the hold
```

**Auth:** N/A. No HTTP surface, scope or token path changes. The publisher still
mints a token per attempt; the tests use the existing stub Keycloak handler.

---

## 5. Independent end-to-end test procedure

No Aspire stack, Docker or broker — plain xUnit against hand-written fakes.

**A. Characterisation (the refactor stayed green)**

```sh
dotnet test tests/ScenarioSimulator.Tests          # before the change: record pass count
# … apply the seam …
dotnet test tests/ScenarioSimulator.Tests          # after: same tests, unmodified, green
git diff origin/develop -- tests/ScenarioSimulator.Tests/MqttPublisherDropAccountingTests.cs \
                           tests/ScenarioSimulator.Tests/MqttPublisherProtocolPinTests.cs   # empty
```

**B. The new tests can fail (counterfactuals)** — each is a transient patch,
observed red, reverted, and `git diff --exit-code -- src/` verified clean:

| Injected defect | Must turn red |
|---|---|
| `backoff.ResetIfHeld(…)` → `backoff.Reset()` in `MqttPublisher.HoldConnectionAsync` (reset on every connect) | dies-on-arrival; held-just-past-the-floor |
| `ResetIfHeld`'s yardstick → constant `first` in the simulator's `MqttBackoff` (the pre-fix guard) | held-just-past-the-floor only |
| `ResetIfHeld` call deleted (never reset) | a-connection-that-held-clears-the-debt |
| fake: `DropEveryConnectionImmediately` / `HoldEveryConnectionFor` no-ops | the fake contract tests |

The pass condition is the contrast, with the observed counts written down in
`verification.md` — not the ceilings restated.

---

## 6. Locked tech choices and latency

Nothing new. xUnit + Shouldly + hand-written fakes (ADR-0052, ADR-0054);
sentence-style names (ADR-0053); waits poll a condition against a deadline, and a
fixed `Task.Delay` appears only as an **observation window** where a rate is what
is in doubt — the same reasoning EventIngestion's `MqttConnectionLoopTests`
already records (ADR-0150).

**Latency-budget impact: N/A.** The Scenario Simulator is dev-only (ADR-0111) and
sits on no leg of constitution §IV. Its production-path twin,
EventIngestion's subscriber, is not touched.

---

## 7. Out of scope (stated, not dropped)

- **Making the backoff configurable** (`IOptions<T>`, `SimulatorOptions`, DI).
  That would be the decision-shaped version of this change; nothing needs it.
- **Merging the two fakes or the two `MqttBackoff` copies** — forbidden by both
  loops' doc comments (spec 079, ADR-0036): sharing would put MQTT into
  `Shared.Kernel`.
- **A direct `MqttBackoffTests` for the simulator's copy** (§1) — recorded; the
  loop tests here exercise `ResetIfHeld` through the publisher.
- **G3 (`PresentedCredentials`) and G4** from spec 179 — their own issues.
- **Thread-safety of the simulator fake's `IsConnected`** — a plain auto-property
  written by the hold timer's thread. The tests in this spec never read it for a
  decision while a hold is pending; recorded rather than fixed, and #2451 is the
  EventIngestion-side precedent if it ever bites.
