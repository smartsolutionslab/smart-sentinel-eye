# Plan 250 — The backoff no test could reach

**Spec:** `specs/250-the-backoff-no-test-could-reach/spec.md`
**Issue:** #2449

---

## 1. Where this lives

Not a bounded context. `src/ScenarioSimulator/` is the dev-only simulated PLC
(ADR-0111) — a single project with no Domain/Application/Infrastructure split,
and no `Shared.Contracts` message or integration event is involved. Boundary
rules are trivially met: no project reference is added, and nothing crosses into
EventIngestion (its files are the **reference**, read not edited).

No entities, value objects or invariants change. The only invariant in play
belongs to `MqttBackoff` and is unchanged: `first ≥ 1 ms` (its constructor's
`Ensure.That(...).AtLeast(1)`, which is what stops a zero floor turning the loop
into a hard spin). The injected test backoffs (100 ms / 400 ms) satisfy it.

Messaging: none. The loop is MQTT publish only.

---

## 2. Production change — `src/ScenarioSimulator/Mqtt/MqttPublisher.cs`

Behaviour-preserving. Exactly three edits:

1. `private readonly MqttBackoff backoff = new();` → `private readonly MqttBackoff backoff;`
2. The **internal** constructor gains a trailing `MqttBackoff? backoff = null`,
   assigned `this.backoff = backoff ?? new MqttBackoff();`.
3. That constructor's `<summary>` gains one paragraph: the backoff is taken for the
   same reason the client is — a test must be able to observe the loop's waits
   without the production 1 s / 30 s, and EventIngestion's `MqttConnectionLoop`
   already takes it the same way.

**Not changed:** the public constructor, `BilletTimelineExtensions`
(`AddSingleton<MqttPublisher>()`), `MqttBackoff`, and every existing test file.

Why optional rather than required: a required parameter would force edits to the
two existing call sites in `MqttPublisherDropAccountingTests.cs` and
`MqttPublisherProtocolPinTests.cs`, and those are the characterisation tests —
§Testing requires them to pass **unmodified**. Nullable rather than `Option<T>`:
ADR-0141's preference is scoped to Domain and Application; this is neither, and
EventIngestion's test harness already spells the same default
`MqttBackoff? backoff = null`.

S107 (5 > 4 parameters) will report on the internal constructor — advisory,
accepted in `spec.md` §3.3.

---

## 3. Fake change — `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs`

Two members, named as EventIngestion's so a reader of both suites meets one
vocabulary, with one deliberate difference in **where** the drop is raised.

| Member | EventIngestion raises the drop from | This fake raises it from | Why |
|---|---|---|---|
| `bool DropEveryConnectionImmediately { get; set; }` | inside `SubscribeAsync`, after the topics are recorded | inside `ConnectAsync`, **after** `IsConnected = true` and the attempt counter move, **before** it returns `Success` | The publisher never subscribes — both loops' doc comments list that as an intended difference — so the answered CONNECT is the last packet of a "connection that completed". Raising it inside the call, awaited, makes the ordering a fact of the test rather than a race, which is the principle both fakes already follow for the stale disconnect |
| `void HoldEveryConnectionFor(TimeSpan duration)` | scheduled (`_ = HoldThenDropAsync(hold)`) from `SubscribeAsync` | scheduled from `ConnectAsync` at the same point | Same reason. Scheduled, not awaited, so the CONNECT is not the thing that takes the hold |

Both raise through the existing `DropAsync()`, so the event carries
`ClientWasConnected = true` and `IsConnected` becomes `false` — a drop the
publisher's `DropSignal` accepts. Neither applies to a **refused** CONNECT (the
`refusals > 0` branch returns before the success path), which matches the real
client: a refusal has no connection to lose, and its disconnect is the stale kind
the fake already models.

Doc comments: carry EventIngestion's `<summary>` text over, adapted to name
`ConnectAsync` rather than `SubscribeAsync` and to say why. Keep the class-level
paragraph that lists what differs between the two fakes accurate — it currently
says this one "gates connects and forces publish failures where that one records
credentials and topics"; the drop/hold controls are now shared, not a difference.

**Placement inside `ConnectAsync`:** after the stale-disconnect raise, the
liveness check (spec 179) and the connect gate; after `Interlocked.Increment`;
only on the success path. A gated test must still be able to hold the CONNECT
before any drop fires.

File size: the fake is 283 lines; the additions keep it near but should not take
it past ADR-0084's advisory 300. If they do, that is an advisory warning, not a
reason to split the double.

---

## 4. New tests

Two new files, so the characterisation files stay unmodified and ADR-0084's
300-line advisory is respected (`MqttPublisherDropAccountingTests.cs` is already
375).

### 4.1 `tests/ScenarioSimulator.Tests/FakeMqttClientDropHoldContractTests.cs`

Pins the double's fidelity, as `FakeMqttClientContractTests` does for the liveness
check (spec 179). A separate file rather than an addition to that one, because
that file is a characterisation guard for spec 179's work and is left alone.

1. `A_connection_dropped_on_arrival_raises_one_disconnect_for_a_connection_that_was_up`
   — handler attached, `DropEveryConnectionImmediately = true`, one `ConnectAsync`:
   result `Success`; exactly one disconnect observed, `ClientWasConnected == true`;
   `IsConnected == false`.
2. `A_refused_connect_is_not_dropped_as_if_it_had_connected` — refusal +
   immediate-drop both set: result `NotAuthorized`; **no** disconnect raised.
3. `A_held_connection_is_up_until_the_hold_ends_and_then_drops` — hold 150 ms:
   `IsConnected == true` straight after `ConnectAsync`; a `ClientWasConnected ==
   true` disconnect arrives within a deadline (poll, ADR-0150); elapsed since the
   connect ≥ hold minus a stated timer-granularity tolerance (≈15 ms on Windows).

### 4.2 `tests/ScenarioSimulator.Tests/MqttPublisherBackoffTests.cs`

The three EventIngestion `MqttConnectionLoopTests` cases that exercise
`ResetIfHeld`, ported to the publisher. Its own small harness (the
`PublisherUnderTest` in the drop-accounting file is private and that file is not
edited): builds the publisher through the **internal** constructor with a fake
client, a stub Keycloak handler (the pattern both existing publisher test files
already duplicate), a `RecordingLogger<MqttPublisher>`, and `Patient()` =
`new MqttBackoff(100 ms, 400 ms)`. `DisposeAsync` disposes publisher and token
provider.

| Test | Arrange | Window / bound | EventIngestion source |
|---|---|---|---|
| `A_connection_that_dies_on_arrival_is_retried_with_a_delay_rather_than_a_spin` | `DropEveryConnectionImmediately = true` | `ConnectAttempts < 20` over 1 s | same name |
| `A_connection_held_just_past_the_floor_still_backs_off` | `HoldEveryConnectionFor(110 ms)` | `ConnectAttempts < 16` over 3 s | same name |
| `A_reconnect_after_a_held_connection_does_not_inherit_the_previous_backoff` | `RefuseNextConnects(2)`; wait connected; hold 900 ms; `DropAsync()` | next CONNECT within 150 ms | `A_reconnect_after_a_success_does_not_inherit_the_previous_backoff` |

**The constants are carried over with their derivations, and then measured, not
trusted.** EventIngestion's ceilings were derived for its loop's cycle; the
publisher's cycle is the same shape (mint → CONNECT → wait) but not the same code.
Phase 4 records the **observed** `ConnectAttempts` in both the correct and the
counterfactual state for each window and confirms each ceiling sits clear of both
populations. If one does not, the constant is re-derived from the measurement and
the derivation written in its doc comment — the constant is never moved merely to
turn a run green.

Every assertion carries a `because` saying what a failure means, as the sibling
file does. `Start` uses `StartAsync` then polls; `ConnectAttempts` is already
`Volatile.Read`, so it is safe to poll across threads.

---

## 5. Counterfactuals (the evidence the new tests can fail)

All patches transient; each followed by `git checkout -- <file>` and
`git diff --exit-code -- src/ tests/ScenarioSimulator.Tests/Fakes/` before the next.
A restored file keeps its old timestamp, so re-run with `--no-incremental` or
confirm the pass count moved.

| # | File | Patch | Expected red |
|---|---|---|---|
| CF1 | `src/ScenarioSimulator/Mqtt/MqttPublisher.cs` | `backoff.ResetIfHeld(Stopwatch.GetElapsedTime(connectedAt));` → `backoff.Reset();` | dies-on-arrival (hundreds+ attempts); held-just-past-the-floor |
| CF2 | `src/ScenarioSimulator/Mqtt/MqttBackoff.cs` | yardstick → `first` (the pre-fix constant) | held-just-past-the-floor only; dies-on-arrival stays green (held ≈ 0 < first) |
| CF3 | `src/ScenarioSimulator/Mqtt/MqttPublisher.cs` | delete the `ResetIfHeld` line | reconnect-after-a-held-connection (inherits 320–480 ms) |
| CF4 | `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs` | both new controls become no-ops | the fake contract tests 1 and 3 |

CF2 is the one that matters most: it is the defect the doubled yardstick fixed,
and a held-just-past-the-floor test that stays green under it checks nothing.

---

## 6. Phase-4 roles and file ownership

Colour: **behaviour-preserving → characterisation**. See `tasks.md` for the
declaration and the reasoning against red.

| Step | Agent | Owns |
|---|---|---|
| 4a | `test-writer` | nothing new — captures the existing `ScenarioSimulator.Tests` run green (baseline) |
| 4b | `backend-engineer` | `src/ScenarioSimulator/Mqtt/MqttPublisher.cs`, `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs` |
| 4c | `test-writer` | the two new test files; runs CF1–CF4 |

The engineer does not write the tests that judge the seam and the fake; the
test-writer does not keep any `src/` or fake patch.

---

## 7. Constitution / ADR alignment

- §II primitives: no domain model touched. `TimeSpan` in `MqttBackoff` is outside
  any Domain layer.
- §IV latency: N/A (dev-only simulator).
- §Testing / ADR-0139: characterisation green before and after, unmodified; new
  coverage proven by counterfactual.
- ADR-0105 guards: no new argument guard; `MqttBackoff`'s existing one stays.
- ADR-0036: one optional parameter, no options type, no interface, no factory.
- ADR-0144: the lane writes no ADR, and none is needed (`spec.md` §3.3).
