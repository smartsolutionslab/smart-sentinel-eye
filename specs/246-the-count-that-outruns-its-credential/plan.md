# Plan 246 — The count that outruns its credential

**Spec**: [spec.md](spec.md) · **Issue**: #2451 · **Phase**: 2 (Plan)
**Scope**: `tests/EventIngestion.Infrastructure.Tests/` — one fake edited, one test file added.

## 1. Where this sits

There is no bounded context, no layer, no entity, no message, and no boundary rule in play, because the
subject is a hand-written test double (ADR-0054) in one test assembly. The domain, messaging and
boundary sections of a normal plan are **N/A**, and this is stated here so their absence is not read
as an omission. NetArchTest and `PrimitiveBoundaryTests` do not scan `tests/`.

| File | Change |
|---|---|
| `tests/EventIngestion.Infrastructure.Tests/Fakes/FakeMqttClient.cs` | Two lists become ordered concurrent recorders exposed as snapshots. `ConnectAttempts` becomes its own counter. |
| `tests/EventIngestion.Infrastructure.Tests/FakeMqttClientConcurrencyTests.cs` | **New.** Three facts (§3). |
| `tests/EventIngestion.Infrastructure.Tests/MqttConnectionLoopTests.cs` | **Untouched.** The characterisation net (spec §7). |

## 2. The fix shape, and why this one

The fix mirrors two patterns the repo already has. Nothing new is introduced.

- **The counter mirrors the simulator's fake exactly**: `private int connectAttempts;`, then
  `Interlocked.Increment(ref connectAttempts)` in `ConnectAsync`, and
  `ConnectAttempts => Volatile.Read(ref connectAttempts)`
  (`ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs:39,72,209`).
- **The ordered record mirrors `FakeEventBus`**, which is a `private readonly ConcurrentQueue<T>` exposed as
  `=> queue.ToArray()` (`tests/EventIngestion.Application.Tests/Fakes/FakeEventBus.cs:8,10`, and the same
  file in Automation and Identity). The simulator has no ordered recorder to mirror: its only list,
  `Published`, is a plain `List<T>` with the same flaw (spec §1 row 5).

Alternatives considered and rejected:

| Option | Why not |
|---|---|
| `ConcurrentBag<T>` (the issue's first suggestion) | **Unordered.** Enumeration order is not insertion order, so `:148`'s `["token-1","token-2","token-3"]` would become nondeterministic. That swaps one flake for a worse one. |
| `lock` around the `List<T>` plus a copying getter | Correct, but it is a third pattern for the same job in this repo, and it gives the count no ordering guarantee unless the lock also covers `ConnectAttempts`. |
| Keep `ConnectAttempts => credentials.Count` on the queue | `ConcurrentQueue.Count` can count a slot whose element is still being written, which is the same "count ahead of value" shape in another form. A separate counter incremented **after** the enqueue is what makes the invariant hold. |

Target shape (member names are fixed; the engineer writes the XML docs):

```csharp
private readonly ConcurrentQueue<string> presentedCredentials = new();
private readonly ConcurrentQueue<string> subscribedTopics = new();
private int connectAttempts;

public IReadOnlyList<string> PresentedCredentials => presentedCredentials.ToArray();
public IReadOnlyList<string> SubscribedTopics => subscribedTopics.ToArray();
public int ConnectAttempts => Volatile.Read(ref connectAttempts);

// ConnectAsync, at the spot where :210 is today (after the IsConnected check, so ThrowIfConnected
// still records nothing), in this order:
presentedCredentials.Enqueue(/* the same password decode as today */);
Interlocked.Increment(ref connectAttempts);   // after the enqueue — that ordering is the fix

// SubscribeAsync, replacing :245's AddRange:
foreach (MqttTopicFilter filter in options.TopicFilters) subscribedTopics.Enqueue(filter.Topic);
```

**The whole fix is the invariant:** the counter is incremented *after* the element is stored, so any
count a test has observed is already backed by a stored element. `ToArray()` hands back a point-in-time
snapshot, and it waits for any slot that is mid-write, so a read never throws and never sees `null`.
`ConcurrentQueue<T>` has no `Add` method, so it can't be a collection expression, and `new()` is not
flagged (`FakeEventBus` compiles under the same Release rules). The engineer confirms this with the
Release build and does not assume it.

**Why-comment, one paragraph, on the fake**: the loop writes on its own thread while a test polls, and
the counter follows the enqueue so an observed count never runs ahead of the snapshot. That is the
non-obvious *why*. Don't add issue or task references to the comment (Karpathy guidelines, CLAUDE.md).
Leave the existing `ConnectAsync` remark about `ThrowIfConnected` not moving the count as it is,
because it stays true.

**Consumer compatibility.** `MqttConnectionLoopTests.cs` uses `ConnectAttempts` (int), `PresentedCredentials.Take(3)`
spread into a `List<string>`, `SubscribedTopics.Count`, and `SubscribedTopics.ShouldAllBe(...)`. All of
these compile against `IReadOnlyList<string>` unchanged. No other file references this fake.

## 3. The new tests — `FakeMqttClientConcurrencyTests`

Sentence-style names (ADR-0053). The class summary cites the two precedent contract-test classes and
says *why* a stress test: a single-threaded test cannot interleave, and the green is deterministic
while the red is probabilistic (spec §6).

**Shared shape of F1 and F2**

- `const int Writes = 20_000;` bounded by count, not by time.
- The writer runs on `Task.Run`. It performs the `Writes` operations in order and signals a
  `CancellationTokenSource` in `finally`, so the reader always stops.
- The reader runs on `Task.Run` as a synchronous loop while the writer is not done. Each iteration
  enumerates the property **with an explicit `foreach`**, not a spread and not `ToList()`, because
  `List<T>`'s `ICollection.CopyTo` path skips the version check that makes the red near-certain.
  **It checks with plain comparisons in the hot loop.** It records the first violation (the message
  includes the index, the expected and actual values, and the snapshot length) plus a count of
  *overlapping reads*, meaning reads taken with `0 < count < Writes`. It catches any exception from the
  enumeration and records it as the violation, so the test fails on an assertion rather than an
  unobserved task fault.
- `await Task.WhenAll(writer, reader).WaitAsync(TimeSpan.FromSeconds(30))`, a bound that turns a hang
  into a failure (ADR-0150's spirit: never an unbounded wait).
- Assert (Shouldly, after the tasks finish): **(a)** no violation was recorded, with the violation as
  the message. **(b)** An arrange check: overlapping reads `> 0`, because a reader that never overlapped
  the writer proves nothing. **(c)** Final state: `ConnectAttempts == Writes` for F1 and the final
  snapshot length is `Writes` for F2.

**F1** `A_count_read_while_credentials_are_being_recorded_is_never_ahead_of_them`
Arrange `RefuseEveryConnect()`, so every CONNECT is recorded and the client stays disconnected. The
writer calls `ConnectAsync(OptionsPresenting($"token-{i}"))` for `i = 1..Writes`. For each iteration
the reader reads `int counted = client.ConnectAttempts;` **first**, then enumerates
`PresentedCredentials`. Violations are: snapshot length `< counted`, any element `!= $"token-{j+1}"`
(which also catches `null`), or an exception. `OptionsPresenting` builds
`new MqttClientOptions { Credentials = new MqttClientCredentials("user", Encoding.UTF8.GetBytes(password)) }`.
**Verify that constructor against the pinned MQTTnet 5 package before relying on it.** If it
differs, a five-line private `IMqttClientCredentialsProvider` is the fallback, the same shape as the
loop's `TokenCredentials`.

**F2** `Topics_read_while_subscriptions_are_being_recorded_arrive_whole_and_in_order`
No arrange. For each `i` the writer does `ConnectAsync(new MqttClientOptions())`, then `SubscribeAsync`
with the single filter `$"topic-{i}"` (built with `MqttClientSubscribeOptionsBuilder().WithTopicFilter(...)`),
then `DisconnectAsync(new MqttClientDisconnectOptions())`. The reader enumerates `SubscribedTopics`.
Violations are an element `!= $"topic-{j+1}"` or an exception. For the overlap check, use the snapshot
length in place of `counted`.

**F3** `A_connect_refused_because_the_client_is_already_connected_is_not_recorded`
This is a single-threaded **characterisation** fact, green before and after. Connect once, then
`Should.ThrowAsync<InvalidOperationException>` on a second connect. `ConnectAttempts` stays `1` and
`PresentedCredentials.Count` stays `1`. It pins the one placement the fix could plausibly move: the
enqueue and increment must stay after the `IsConnected` check.

**Expected 4a result against the unmodified fake:** F1 red, F2 red, F3 green. The acceptable reds are
listed in spec §6.

## 4. Verification (phase 5, and what the PR quotes)

1. Before any change: `dotnet test tests/EventIngestion.Infrastructure.Tests -c Release`, whole
   assembly green. Record the count.
2. 4a: add the tests. F1 and F2 go red for an acceptable reason and F3 is green, captured verbatim.
3. 4b: the fix. Whole assembly green, with count = baseline + 3.
   `git diff --exit-code a54b11d0 -- tests/EventIngestion.Infrastructure.Tests/MqttConnectionLoopTests.cs`
   is empty. `FakeMqttClientConcurrencyTests.cs` is unchanged since its 4a commit.
4. Repeat the new class 5 times back to back, all green. Record each run's wall time, because a test
   that takes seconds in the fast lane needs saying.
5. Counterfactual: temporarily restore the fake's `List<string>` fields and `.Count`-based
   `ConnectAttempts`. F1 and F2 go red and F3 stays green. Restore, then check `git diff` is empty.
   (Memory: prove a guard by counterfactual. Memory: a restored file keeps its old timestamp, so
   rebuild with `--no-incremental` or touch the file before re-running.)

## 5. Risks

- **The red may not appear.** On a single-core or throttled runner the reader and writer may barely
  overlap. The overlap arrange check makes a non-overlapping *green* fail loudly instead of passing
  quietly. If the old fake stays green on three runs, stop and report (spec §6), and do not raise
  `Writes` without limit.
- **Runtime.** 20k `Task.Yield`s plus O(k) snapshot checks should finish in well under a second
  locally. If the class takes more than about 3 s, lower `Writes` in 4a before the fix is written, with
  the red re-observed, and never after.
- **Stack contention.** None. This is a unit test assembly with no Docker and no Aspire stack.
