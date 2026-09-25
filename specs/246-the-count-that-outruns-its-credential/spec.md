# Spec 246 — The count that outruns its credential

**Issue**: #2451 · **Branch**: `fix/2451-mqtt-fake-thread-safety` · **Phase**: 1 (Specify)
**Date**: 2026-09-25 · **Base**: `a54b11d0` (`origin/develop`, fetched 2026-09-25)
**Context**: test infrastructure only — `tests/EventIngestion.Infrastructure.Tests/`. No `src/`, `apps/`,
contract, AppHost, CI-workflow or migration change.
**Engineer**: `test-writer` (4a) then `backend-engineer` (4b) · **Reviewer**: `backend-reviewer`
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`
**Phase 4a colour**: **red** for the new concurrency guard (§6), with the existing loop tests as a
**characterisation** net that must pass unmodified (§7). Ambiguity resolves to red (ADR-0144).
**ADRs**: ADR-0037 (phases), ADR-0144 (the lane; 4a/4b split), ADR-0036 (smallest change), ADR-0054
(hand-written fakes), ADR-0052/0053 (xUnit + Shouldly, sentence-style names), ADR-0139 (§Testing — new
behaviour observed red first), ADR-0150 (a wait is bounded by a condition, not a sleep)
**Constitution**: §IV — **N/A**. No leg of the event→overlay path is touched; the subject is a test double.
§Testing — both obligations apply, to different files (§6, §7).
**New ADR needed**: **No.** Hand-written fakes are ADR-0054's; the counter pattern is the simulator
fake's; the ordered recorder is `FakeEventBus`'s. Nothing here is a new decision.

---

## 1. The premise, re-checked against `a54b11d0`

| # | Issue claim | Status |
|---|---|---|
| 1 | `PresentedCredentials` is a plain `List<T>` | **Holds.** `Fakes/FakeMqttClient.cs:46`. So is `SubscribedTopics` (`:49`) — the issue does not name it; it has the identical exposure. |
| 2 | `ConnectAttempts => PresentedCredentials.Count` reads it unsynchronized | **Holds.** `:62`. Written on the loop's thread (`:210`), polled from the test thread every 5 ms (`MqttConnectionLoopTests.cs:542-556`). |
| 3 | `:148` enumerates while the loop thread is still adding | **Does not hold as stated.** That test refuses two connects and the third succeeds with no drop, so the loop stops adding at exactly 3 — nothing is added while `Take(3)` runs. And `Take` over an `IList<T>` indexes rather than enumerates, so the `_version` check that throws `InvalidOperationException` never runs there. |
| 4 | …so it can throw `InvalidOperationException` nondeterministically | **Not reachable by any current test.** No test enumerates either list while it is growing. The `DropEveryConnectionImmediately`, `RefuseEveryConnect` and `HoldEveryConnectionFor` tests do run the loop continuously, but read only `ConnectAttempts`, an `int`. |
| 5 | The simulator's fake "uses `Interlocked`/`Volatile` throughout" | **Overstated.** It uses them for one field: `connectAttempts` (`ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs:39,72,209`). Its `Published` is a plain `List<string>` (`:53`), and `refusals`, `IsConnected` and `staleDuringNextConnect` are plain fields. It doesn't record credentials at all, so it has no ordered-sequence pattern to mirror. |

**The hazard that is real, and it is at `:148`.** `List<T>.Add` publishes the new count *before* it
stores the element (`_size = size + 1; array[size] = item;`). The test at `:145` waits for
`ConnectAttempts >= 3`, which is `Count`, and then reads element `[2]`. If the loop thread is
preempted between those two stores, the test sees a count of 3 and reads `null`, so
`presented.ShouldBe(["token-1","token-2","token-3"])` fails, naming a credential the loop did present.
`:125` → `:130` has the same shape for `SubscribedTopics` (`Count >= 2`, then `ShouldAllBe`). On x64
the window is a few instructions, so the chance is tiny but not zero. On a weakly ordered CPU it is a
store reordering and not only a preemption.

**So the defect is real, but it is not the one the issue names.** The fake publishes a count that can
run ahead of the value it counts. It also has a second, broader flaw: it is a trap for the next test
that reads either list while the loop is running (for example the credentials presented during a
`RefuseEveryConnect` run). That test would get the enumeration exception the issue describes.
Nothing has failed yet, and the fix removes both.

## 2. User story

**US1 (P1) — A test can read what the fake recorded at any moment, from any thread, and get a
consistent answer.** A test author reading `ConnectAttempts`, `PresentedCredentials` or
`SubscribedTopics` while the connection loop runs on another thread gets an in-order snapshot with no
missing or `null` element and no exception. A count already observed is never ahead of what the
snapshot contains.

## 3. Acceptance scenarios

```gherkin
Scenario: happy — the snapshot after an observed count contains every counted credential
  Given a FakeMqttClient that refuses every CONNECT
  And a writer presenting credentials "token-1" … "token-N" in order on one task
  When a reader on another task repeatedly reads ConnectAttempts, then PresentedCredentials
  Then every snapshot has at least as many elements as the count read before it
  And every snapshot is exactly "token-1" … "token-k" in order, with no null element
  And no read throws

Scenario: happy — subscribed topics are readable while subscriptions are recorded
  Given a writer that connects, subscribes to "topic-i" and disconnects, for i = 1 … N
  When a reader on another task repeatedly reads SubscribedTopics
  Then every snapshot is exactly "topic-1" … "topic-k" in order, with no null element
  And no read throws

Scenario: conflict — a CONNECT refused by ThrowIfConnected is still not counted
  Given a FakeMqttClient that is connected
  When ConnectAsync is called again
  Then it throws InvalidOperationException
  And neither ConnectAttempts nor PresentedCredentials moves
  # existing behaviour (FakeMqttClient.cs:185-190); must survive the change

Scenario: bad request — N/A. The fake has no input validation, and none is added.
Scenario: auth — N/A. A test double has no caller identity.
```

## 4. Out of scope

- **The simulator fake's `Published` list** (`ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs:53`).
  It has the same shape, but it is read once, after the publisher has stopped
  (`MqttPublisherDropAccountingTests.cs:49`). File a separate issue if wanted; it is not part of this change.
- **`IsConnected`, `refusals`, `holdFor`, `staleDuringNextConnect`** in either fake. These are plain
  fields that are written during arrange or only on the loop's thread, or are a `bool` polled with
  barriers from `Task.Delay`. The simulator fake doesn't guard them either, so changing them here
  would make this fake diverge from its twin.
- `MqttConnectionLoop`, `MqttConnection`, any `src/` file.
- `MqttConnectionLoopTests.cs`. **It must not change at all** (§7).

## 5. Independent end-to-end test procedure

This is a test double with no running system behind it, so "end to end" means the one test assembly
that consumes it:

1. `dotnet test tests/EventIngestion.Infrastructure.Tests -c Release`. All green, the same count as
   before plus the new cases.
2. Run the new concurrency cases 5 times back to back
   (`--filter "FullyQualifiedName~FakeMqttClientConcurrencyTests"`). All 5 green.
3. Counterfactual: revert only the fake's two lists and counter to `List<string>` / `.Count`. The new
   cases go red, and the red is quoted. Then restore the fake and confirm `git diff` is empty.

## 6. The red — a guard whose subject is the fake (new file)

The precedents for a test whose subject is the double: `ScenarioSimulator.Tests/FakeMqttClientContractTests.cs`
(spec 179) and `EventIngestion.Infrastructure.Tests/MqttClientWasConnectedContractTests.cs`.

A single-threaded test cannot reach this defect. The guard is a **bounded concurrent stress test**, and
it has two properties that together make it trustworthy:

- **Its green is deterministic.** Once fixed, no interleaving can fail it: one writer, FIFO storage,
  and the count incremented after the store. A flaky red on `develop` is therefore impossible by
  construction.
- **Its red is probabilistic but near-certain.** A version-checked `foreach` over a `List<T>` that
  another thread is appending to throws `InvalidOperationException` within a few thousand overlaps.
  The only way the test can be wrong is by *missing* a regression, never by failing a correct fake.

That asymmetry is why a stress test is the right kind of proof here. The test is **required red
against the unmodified fake** in phase 4a. The acceptable reds are an `InvalidOperationException` from
enumeration, a `null` or out-of-order element, or a snapshot shorter than the count read before it.
Anything else, such as a compile error, a timeout or a setup fault, is the wrong red. **Green on three
consecutive runs against the old fake is a phase-4a failure. Stop and report; do not keep raising N.**
The shape is fixed in plan.md §3.

## 7. The characterisation — the loop tests are the safety net

`MqttConnectionLoopTests.cs` (11 facts) is the fake's only consumer, and its observable contract must
not move: the order of credentials and topics, `ConnectAttempts`'s meaning (CONNECTs answered, with
refusals included and `ThrowIfConnected` excluded), `Take(3)`, `.Count` and `ShouldAllBe` all still
compile and pass. Capture the whole assembly **green before** the change, and again after, with
**`MqttConnectionLoopTests.cs` byte-identical** (`git diff --exit-code` on that path). An assertion
that has to be edited is evidence that the behaviour moved. If that happens, block; do not adjust the
assertion.

## 8. Latency

N/A. The change is a test double that runs in no deployed process.
