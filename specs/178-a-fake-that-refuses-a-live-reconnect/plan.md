# Plan 178 — A fake that refuses a live reconnect

**Spec:** `specs/178-a-fake-that-refuses-a-live-reconnect/spec.md`
**Issue:** #2233

---

## 1. Where this lives

No bounded context, no layer, no entity, no value object, no messaging, no
migration. Every file is under `tests/`, in **one** test assembly
(`SmartSentinelEye.ScenarioSimulator.Tests`), plus the spec directory.

That is worth saying plainly rather than leaving the usual headings empty: the
subject of this change is a test double, so the layer rules (§II value objects,
§III no cross-context references, `Shared.Contracts`-only messaging) are not
relaxed here — they are simply not engaged. `NetArchTest`'s boundary rules scan
`src/`; `PrimitiveBoundaryTests` scans domain models; `HandlerDeconstructionTests`
scans handlers. None of them see this diff, and none of them is being worked
around.

**The one boundary that does apply** is the assembly split that makes the
duplication deliberate: the two test projects share no project reference, and
`SmartSentinelEye.ScenarioSimulator.Tests` must not gain one to
`SmartSentinelEye.EventIngestion.Infrastructure.Tests`. Copying the behaviour
across is the design (spec 079, ADR-0036); referencing it is not.

## 2. The change to the fake

`tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs`, `ConnectAsync` only.

The reference shape is EventIngestion's:

```csharp
if (IsConnected)
{
    throw new InvalidOperationException(
        "It is not allowed to connect with a server after the connection is established.");
}
```

**Three properties of the reference shape are load-bearing and must be carried
over, not approximated:**

1. **The type is `InvalidOperationException`**, because that is what
   `MqttClient.ThrowIfConnected` raises. It matters here specifically: the
   publisher's `ConnectAsync` catch is `catch (Exception exception) when
   (!IsShutdown(...))`, so it swallows the refusal into one error line either
   way — but a fake throwing something narrower would silently model a different
   client, and the message is what lands in the log the counterfactual reads.
2. **The message is verbatim the real client's**, because the counterfactual's
   evidence is the logged line `MQTT publisher could not connect to
   'mosquitto.test:1883': It is not allowed to connect with a server after the
   connection is established.` A paraphrase makes that evidence unreadable as
   the real symptom.
3. **Nothing is recorded before the throw.** `ConnectAttempts` must not move: no
   CONNECT leaves a real client in this case, so the failure is visible in the
   log and not in the count — the exact reasoning EventIngestion's doc comment
   gives, and the reason its `LoopUnderTest.FailedConnects` counts log lines
   rather than attempts.

**Placement, which differs from EventIngestion's and must:** after the
`staleDuringNextConnect` raise, **before** `await connectGate.Task`. The
simulator's fake has a connect gate that EventIngestion's has not; a liveness
check placed after the gate would park a CONNECT that the real client refuses
outright, which is a third behaviour belonging to neither client. Assigning
`Options` before the check (as the file does today) is harmless and left alone —
it is not state a test reads to distinguish a refusal.

The `<summary>` gains the third paragraph EventIngestion's carries, naming
`ThrowIfConnected` and saying that `ConnectAttempts` does not move. The class
`<summary>`'s last paragraph currently says the two fakes differ because "this
one gates connects and forces publish failures where that one records credentials
and topics" — that sentence stays true and stays.

## 3. The two tests

### T1 — `tests/ScenarioSimulator.Tests/FakeMqttClientContractTests.cs` (new)

A test of the double itself. That is unusual and deliberate: the deliverable of
#2233 *is* the double's behaviour, and §Testing requires that behaviour be
observed failing first. There is precedent for pinning a fake's fidelity in this
very pair — `MqttClientWasConnectedContractTests` measures the **real** client to
justify what both fakes encode. T1 is the other half of that: it pins what the
fake encodes.

A new file rather than an addition to `MqttPublisherDropAccountingTests.cs`, for
two reasons: that file is already 368 lines against ADR-0084's 300-line limit,
and its subject is drop accounting, not the double.

Cases, all named sentence-style (ADR-0053):

| Case | Arrange | Assert |
|---|---|---|
| `A_connect_against_a_live_connection_is_refused_as_the_real_client_refuses_it` | one successful CONNECT | second CONNECT throws `InvalidOperationException` with the real client's message |
| `A_refused_reconnect_does_not_end_and_does_not_close_the_connection` | live connection, then four CONNECTs | all four throw; `IsConnected` still `true`; `ConnectAttempts` still `1` |
| `A_stale_disconnect_reused_against_a_live_connection_is_refused` | live connection, then `RaiseStaleDisconnectAsync()`, then CONNECT | throws — #2130's shape, driven directly |
| `A_broker_refusal_is_still_a_result_rather_than_a_throw` | `RefuseEveryConnect()`, disconnected client | returns `NotAuthorized`, throws nothing |
| `A_gated_connect_against_a_live_connection_is_refused_rather_than_parked` | live connection, `GateConnect()`, no `AllowConnect()` | throws inside a bounded wait rather than blocking |

The last case is the one that proves the placement decision in §2 and is the
reason it is written down: a check placed after the gate passes all four other
cases.

### T2 — `tests/ScenarioSimulator.Tests/MqttPublisherDropAccountingTests.cs` (edit)

Three edits to the existing `A_stale_disconnect_landing_mid_connect_...` test and
its harness:

1. **`PublisherUnderTest.FailedConnects`** — a new property, mirroring
   `LoopUnderTest.FailedConnects` including its doc comment's reasoning: counted
   from the log rather than from `ConnectAttempts`, because a CONNECT refused by
   `ThrowIfConnected` never leaves the client and so shows up only there. The
   marker substring is `could not connect`, which is what
   `MqttPublisherConnectFailed`'s template emits (`"MQTT publisher could not
   connect to '{Host}': {Error}."`) and matches the subscriber's marker exactly.
2. **The new assertion**, placed alongside the existing two:
   `publisher.FailedConnects.ShouldBe(0, ...)`, with a `because` that says what a
   non-zero count would mean — `ThrowIfConnected` refusing to reconnect a live
   client, nothing closing that client, so the cycle does not end.
3. **The doc comment's "What this fake can and cannot show" paragraph is now
   false and must go**, replaced by what the fake now shows. Same for the tail of
   the `ConnectAttempts` assertion's `because` ("*against the real client*, where
   `ThrowIfConnected` refuses…") — the fake no longer needs the hedge. Leaving
   either in place would be the defect this repository keeps correcting: a record
   that describes what used to be true.

**And a rename.** `A_stale_disconnect_landing_mid_connect_does_not_restart_the_connect_cycle`
→ `A_stale_disconnect_landing_mid_connect_does_not_end_the_connection_it_landed_in`,
which is the subscriber's name for the same case. The old name was chosen because
a restarted cycle was the most the fake could show; it now understates what the
test asserts. This is not a drive-by — it is the same sentence the fix
invalidates.

## 4. What must not change

- `tests/EventIngestion.Infrastructure.Tests/**` — the control. Its suite is run
  unchanged, and a diff there means the change was made in the wrong direction.
- `src/**` — permanently. The counterfactual patches one file transiently and
  reverts it; see §5.
- The other five test methods in `MqttPublisherDropAccountingTests`, and
  `MqttPublisherProtocolPinTests`. The fake's new throw is reachable only from a
  second CONNECT on a live connection, which none of them performs — but they are
  the regression check for that claim and must be run.

## 5. The counterfactual — procedure

**What it proves:** that the fixed fake makes the simulator's suite report
#2130's defect as a *permanent refusal* rather than as *one extra connect*. T1
proves the fake refuses; only this proves the suite's symptom changed.

**The injection is the exact pre-#2130 code**, taken from commit `e27c1c47`
("fix(ingress): a drop is discriminated by the args, not by the clock"), three
lines in `src/ScenarioSimulator/Mqtt/MqttPublisher.cs`:

| Now | Injected (pre-#2130) |
|---|---|
| `DropSignal drop = new(logger, $"{host}:{port}");` | `DropSignal drop = new(client, logger, $"{host}:{port}");` |
| `private sealed class DropSignal(ILogger logger, string broker)` | `private sealed class DropSignal(IMqttClient client, ILogger logger, string broker)` |
| `if (args.ClientWasConnected)` | `if (!client.IsConnected)` |

Reconstructing it rather than inventing a defect is the point: a counterfactual
that constructs a *different* fault proves a guard against something nobody
filed.

**Run A — injected defect, fake NOT yet fixed** (phase 4a, the test-writer):

expected, and this is the blindness being closed —

- `FailedConnects` = **0**
- two `MQTT publisher connected` lines, `ConnectAttempts` = 2
- the test fails on the connect-count assertions, with nothing in the output
  suggesting the failure is permanent

**Run B — injected defect, fake fixed** (phase 4b, the engineer, immediately
after the fake change):

expected —

- `FailedConnects` ≥ **2** within the window, each carrying `It is not allowed to
  connect with a server after the connection is established.`
- `IsConnected` still `true` — nothing closed the connection
- the test fails on the new assertion as well as the old ones

**Timing, because the window is not arbitrary.** `MqttPublisher`'s backoff is a
hardcoded `new()` — 1 s floor, 30 s cap, jitter in [0.8, 1.2] — and is not
injectable. Under the injected defect the cycle is: connect (no delay) → false
drop → `ResetIfHeld(≈0)` against a 2 s yardstick, so no reset → ~1 s → refused →
~2 s → refused → ~4 s → refused. The shipped test's 3 s `StaleWindow` sees the
first refusal comfortably, which is all the shipped assertion needs. For **Run B
only**, widen the window locally to 6 s so at least two refusals are observed:
one refusal is a cycle, two at a growing interval is the stream. Revert the window
with the injection.

**Revert gate, non-negotiable.** After Run B:

```sh
git checkout -- src/ScenarioSimulator/Mqtt/MqttPublisher.cs
git diff --exit-code -- src/ && git status --porcelain -- src/
```

Both must be silent. Then re-run the ordinary suite green. **No file under
`src/` may appear in the PR diff**, and the PR body states that the counterfactual
patch was reverted and how that was verified. A restored file keeps its old
timestamp, so the re-run must be preceded by a build that actually rebuilds —
check the test output reports the expected pass count rather than trusting a
skipped compile.

## 6. Verbatim output is the artefact

Four quoted outputs go in the PR body and `verification.md`:

1. T1 red, against the unfixed fake (phase 4a).
2. Run A — the weaker symptom (phase 4a).
3. T1 + T2 green, against the fixed fake (phase 4b).
4. Run B — the permanent refusal (phase 4b), plus the revert verification.

Anything reported only to the orchestrator is invisible to every later reader, so
all four are written down.

## 7. Risks

| Risk | Handling |
|---|---|
| The new throw breaks an unrelated simulator test | Run the whole `ScenarioSimulator.Tests` assembly, not a filter. The throw is reachable only from a second CONNECT on a live connection; that claim is checked by running everything, not by reading. |
| The engineer "fixes" a red T1 by weakening T1 | The 4a/4b split holds even though every file is under `tests/`. The boundary is by file: T1 and T2 belong to the test-writer, `Fakes/FakeMqttClient.cs` to the engineer. Neither crosses. |
| The counterfactual patch is left in `src/` | The revert gate in §5, plus a Phase-3 task whose only content is verifying it, plus the PR-diff check. |
| Run B's two refusals do not both land inside the window | The window is a *failure bound* on an observation, not a settle (ADR-0150). If only one lands, widen and re-run; do not report one refusal as a stream. |
| The simulator suite is flaky under contention | The existing `StartAndSettleAsync` comment documents a two-in-three race that its gate closed. Neither new test uses `StartAndSettleAsync`; T1 touches no loop at all. Run the assembly twice before calling it green — the first run after machine churn reads like a regression. |
