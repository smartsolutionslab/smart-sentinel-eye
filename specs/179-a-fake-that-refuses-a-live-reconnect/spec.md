# Spec 179 — A fake that refuses a live reconnect

**Issue:** #2233 (`agent:ready`, `tech-debt`, Project #13 → Todo)
**Branch:** `test/2233-a-fake-that-refuses-a-live-reconnect`
**Lane:** autonomous (ADR-0144)
**ADRs:** ADR-0052 (hand-written fakes), ADR-0053 (test naming), ADR-0139
(rules that fail the build, not the review — and §Testing's observed red),
ADR-0036 (smallest change, no speculative generality), ADR-0084 (300 LOC/file),
ADR-0150 (wait for a condition, not a count), ADR-0037 (phases), ADR-0144 (lane).
**No new ADR.** This closes a test-infrastructure asymmetry against a sibling
that already behaves the wanted way. No design decision is being made.

---

## 1. Premise check (done before planning)

Every claim in the issue was read off the files rather than taken forward.

| Claim | Verified | Where |
|---|---|---|
| The two loops are near-twins | yes | `src/EventIngestion/Infrastructure/Ingress/MqttConnectionLoop.cs` and `src/ScenarioSimulator/Mqtt/MqttPublisher.cs` — same `AttemptAsync` / `HoldConnectionAsync` / `ConnectAsync` shape, same per-attempt `DropSignal`, same `backoff.ResetIfHeld(...)`; both doc comments name the other and say a change to one belongs in the other |
| Each has its own fake of the same name | yes | `tests/EventIngestion.Infrastructure.Tests/Fakes/FakeMqttClient.cs`, `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs` — two files, two assemblies, no shared project |
| EventIngestion's fake emulates `ThrowIfConnected` | yes | its `ConnectAsync` throws `InvalidOperationException("It is not allowed to connect with a server after the connection is established.")` when `IsConnected`, before recording anything, and the doc comment says so |
| ScenarioSimulator's fake does not | yes | its `ConnectAsync` reads `Options`, may raise a stale disconnect, awaits the gate, increments the counter, and answers `Success` — there is no `IsConnected` check anywhere in it |
| Spec 118 / #2130 records the finding | yes | `specs/118-the-guard-discriminates-by-construction/verification.md` §6 "Findings not fixed here", naming the file and the reason it was deferred |
| The stack is not needed | yes | both suites are plain xUnit against hand-written fakes; no `AspireFixture`, no Docker. The running Aspire stack (pid 3312) is untouched |

**One correction to the issue's framing, and it changes what "done" means.** The
issue says the simulator showed "only a restarted cycle". That is not the same as
*undetected*: `A_stale_disconnect_landing_mid_connect_does_not_restart_the_connect_cycle`
asserts one announced connection and one `ConnectAttempts`, so #2130's defect
does fail that test today. What the simulator's suite cannot show is the
**severity** — the permanent stream of "could not connect" errors that the
subscriber's `FailedConnects` counts. So this spec delivers **symptom parity**,
not detection parity, and the difference is worth stating because a spec that
claimed to make an invisible defect visible would be overselling a real but
smaller gain.

---

## 2. The audit (issue's second ask, done member by member)

Both fakes were compared exhaustively, not only on the named method. `—` means
the member does not exist on that side.

| Member | EventIngestion | ScenarioSimulator | Reachable by that loop? | Verdict |
|---|---|---|---|---|
| `ConnectAsync` refuses a CONNECT on a live connection | throws `InvalidOperationException` | **absent** | yes — both loops can reconnect without closing | **G1. Fixed here.** |
| `DropEveryConnectionImmediately` | yes (drop raised after SUBSCRIBE) | — | yes — `MqttPublisher` calls the same `ResetIfHeld` | **G2. Follow-up issue.** |
| `HoldEveryConnectionFor(TimeSpan)` | yes | — | yes — same `ResetIfHeld` yardstick | **G2. Follow-up issue.** |
| `PresentedCredentials` (the password each CONNECT presented, in order) | yes | — | yes — `MqttPublisher` mints per attempt through `TokenCredentials` | **G3. Follow-up issue.** |
| `ConnectAttempts` | `PresentedCredentials.Count` — an unsynchronised `List<T>` read, and line 148 of `MqttConnectionLoopTests` *enumerates* the list from the test thread while the loop thread may be adding | `Interlocked.Increment` / `Volatile.Read` | yes | **G4. Follow-up issue — and it is EventIngestion that is weaker.** |
| `SubscribeAsync` refuses on a disconnected client | throws `MqttClientNotConnectedException`, records topics, drives the drop/hold hooks | returns `Success`, records nothing | **no** — the publisher has no subscription (both loops' comments list this as an intended difference) | Not fixed, not filed. Unreachable. |
| `PublishAsync` | returns `Success` unconditionally | records payloads, `FailNextPublishAsNotConnected`, `FailNextPublishAsTimedOut` | **no** — the subscriber never publishes | Not fixed, not filed. Unreachable. |
| `FirstSubscribe` | yes | — | no (see above) | Unreachable. |
| `GateConnect` / `AllowConnect` | — | yes | n/a — a test affordance, not a broker behaviour | Not a blindness. |
| `RefuseNextConnects`, `RefuseEveryConnect`, `RaiseStaleDisconnectAsync`, `RaiseStaleDisconnectDuringNextConnect`, `DropAsync`, `DisconnectAsync`, `UnsubscribeAsync`, `PingAsync`, `SendEnhancedAuthenticationExchangeDataAsync`, `Dispose`, `SilenceUnusedEvents` | yes | yes | — | Equivalent. |

**One ordering difference inside `ConnectAsync`, recorded because it is easy to
mistake for a fifth gap and is not one.** EventIngestion raises the stale
disconnect, then checks liveness, then records. ScenarioSimulator assigns
`Options`, raises the stale disconnect, then awaits the connect gate. The gate is
the simulator's own affordance and the stale event must precede it or a gated
test could never deliver one, so the order is correct on both sides. G1's fix
must slot in **after** the stale-disconnect raise and **before** the gate, so a
gated CONNECT against a live connection is refused rather than parked.

**One weakness shared by both fakes, filed nowhere because nothing is blind to
it.** Neither `PublishAsync` throws `MqttClientNotConnectedException` on its own
when `IsConnected` is false — the real client does. In the simulator the
`Published.ShouldBeEmpty()` assertion in
`A_sample_published_while_disconnected_is_counted_and_nothing_is_thrown` already
fails if the publisher's own `IsConnected` guard is removed, so the modelling gap
costs nothing today. Recorded, not filed.

**Scope call on G2–G4: filed, not fixed here.** Not because they are small — G2
and G3 are the same class of defect as G1 — but because each needs its own
red-then-green against a symptom the fake cannot currently express, and two of
them need more than a fake change:

- **G2** needs an injectable `MqttBackoff` on `MqttPublisher` (today it is a
  hardcoded `new()` at 1 s/30 s, so a flap-window assertion would run for tens of
  seconds). That is a production signature change.
- **G3**'s fix is a new assertion about `MqttPublisher`'s credential freshness —
  #2038's property, in #2038's territory, and green on arrival.
- **G4** is EventIngestion's fake, the opposite direction, and a flakiness risk
  rather than a blindness.

Bundling any of them would put a production signature change, a new production
assertion, and a flake fix in a diff whose stated purpose is one fake's liveness
check — the same reason #2130 deferred G1 in the first place.

---

## 3. User stories

### US1 (P1) — The simulator's fake refuses a reconnect against its own live connection

**As** whoever next breaks a connection loop,
**I want** the simulator's suite to show the same symptom the subscriber's does,
**so that** the severity of a stale-disconnect defect does not depend on which
suite happened to catch it.

This is the whole slice. It ships independently, it is observable on its own, and
nothing in the repo depends on it landing with anything else.

#### Acceptance scenarios

```gherkin
Scenario: a CONNECT against a live connection is refused, the way the real client refuses it
  Given the simulator's FakeMqttClient has answered one CONNECT successfully
    And nothing has disconnected it
  When a second CONNECT is issued
  Then it throws InvalidOperationException
    And the message is the one MqttClient.ThrowIfConnected raises
```

```gherkin
Scenario: the refusal is permanent, not one cycle
  Given the fake holds a live connection
  When three further CONNECTs are issued
  Then every one of them throws
    And IsConnected is still true, because nothing closed the connection
    And ConnectAttempts has not moved past 1, because no CONNECT left the client
```

```gherkin
Scenario: #2130's shape — a stale disconnect reused against a live connection
  Given the fake holds a live connection
    And a stale disconnect is delivered for a connection that never existed
  When the caller takes that event for a drop and reconnects
  Then the CONNECT is refused rather than answered
```

```gherkin
Scenario: a refusal is still a refusal, not a liveness error (conflict case)
  Given the fake is set to refuse every CONNECT
  When a CONNECT is issued against a disconnected client
  Then it returns NotAuthorized and throws nothing
    And the new liveness check has not turned a refusal into an exception
```

```gherkin
Scenario: a gated CONNECT against a live connection is refused rather than parked (bad-request case)
  Given the fake holds a live connection
    And GateConnect has been called and AllowConnect has not
  When a CONNECT is issued
  Then it throws rather than blocking on the gate
```

```gherkin
Scenario: the publisher's stale-disconnect test sees the stronger symptom
  Given the publisher's loop is running against the fixed fake
    And a stale disconnect lands inside the CONNECT that is about to succeed
  Then within the observation window no "could not connect" error is written
    And exactly one connection is announced
    And exactly one CONNECT was answered
```

**Auth:** N/A. No HTTP surface, no scope, no token path is touched. The fake's
`ConnectAsync` reads `options.Credentials` on the EventIngestion side only, and
this change does not add that.

---

## 4. Independent end-to-end test procedure

Two runs, neither needing the Aspire stack, Docker, or a broker.

**A. The ordinary run**

```sh
dotnet test tests/ScenarioSimulator.Tests --filter "FullyQualifiedName~FakeMqttClientContract|FullyQualifiedName~MqttPublisherDropAccounting"
dotnet test tests/EventIngestion.Infrastructure.Tests
```

Both green. The subscriber's suite is run unchanged, as the control: nothing in
this change may move it.

**B. The counterfactual (the part that proves the fix does what it claims)**

Construct #2130's defect in the simulator's loop, run the publisher's
stale-disconnect test against it, and compare what the suite reports in the two
fake states. Full procedure in `plan.md` §5; the pass condition is the contrast:

| Fake state | `FailedConnects` in the window | What the suite reports |
|---|---|---|
| before the fix | 0 | a restarted cycle — two announced connections |
| after the fix | ≥ 2 | a permanent refusal stream — plus the restarted cycle |

The patch to `src/` exists only for the duration of that run. `git diff
--exit-code -- src/` must be clean before anything is committed, and **no file
under `src/` may appear in the PR diff.**

---

## 5. Locked tech choices

Nothing new. xUnit + Shouldly, hand-written fakes, no mocking framework
(ADR-0052, ADR-0054). Sentence-style test names with underscores (ADR-0053).
Waits poll a condition against a wall-clock deadline; the one fixed `Task.Delay`
is an *observation window*, not a settle — what is in doubt is a rate, and the
existing tests on both sides already use it that way with that reasoning in the
comment (ADR-0150).

---

## 6. Latency-budget impact

**N/A — no production code changes.** For the record on the leg this touches
indirectly: the loop the fake models keeps EventIngestion's MQTT subscription
alive, and an ingestion outage stops the **event → overlay state** leg (≤ 200 ms)
at the source rather than slowing it. This change adds no code to that path; it
makes one failure mode of the path's sibling observable in a unit suite. No
figure is claimed and none is discharged.

---

## 7. Out of scope (stated, not dropped)

- Merging the two fakes or extracting a shared one — the issue forbids it, and
  both loops' doc comments forbid it for a stronger reason: sharing would put an
  MQTT client into `Shared.Kernel`, which all nine bounded contexts reference
  (spec 079, ADR-0036).
- G2, G3, G4 from §2 — filed as follow-up issues by this spec's Phase 3, each
  with its reason above.
- The two unreachable asymmetries and the shared `PublishAsync` weakness from §2
  — recorded, deliberately not filed.
