# Tasks 246 — The count that outruns its credential

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2451 · **Phase**: 3 (Tasks)
**Colour**: **red** for F1/F2 (new behaviour: the fake becomes safe to read concurrently).
**Characterisation** (green before and after, unmodified) for F3 and all of `MqttConnectionLoopTests.cs`.
**Engineer**: `test-writer` (4a) then `backend-engineer` (4b) · **Reviewer**: `backend-reviewer`
**Tracking**: feature-level issue #2451 (already `agent:ready` on Project #13). No per-task issues.

Format: `[ID] [P?] [Story] description`. No foundational (Shared.Kernel / Contracts / AppHost) work.
No `[P]` markers: two files in one test assembly, one sequence, one agent per phase.

## Phase 4a — tests first (test-writer)

- [ ] **T001 [US1]** On the unmodified branch, run
  `dotnet test tests/EventIngestion.Infrastructure.Tests -c Release` and capture the pass count verbatim.
  This is the characterisation baseline.
- [ ] **T002 [US1]** Create `tests/EventIngestion.Infrastructure.Tests/FakeMqttClientConcurrencyTests.cs`
  with F1, F2 and F3 exactly as plan §3. Use an explicit `foreach` in the reader, plain comparisons in
  the hot loop, the first violation recorded, the overlap arrange check, and the 30 s `WaitAsync` bound.
  Verify the `MqttClientCredentials` constructor against the pinned MQTTnet version first.
- [ ] **T003 [US1]** Run the new class 3 times against the **unmodified** fake and capture the output
  verbatim. **Required**: F1 and F2 red for an acceptable reason (spec §6: `InvalidOperationException`
  from enumeration, a `null` or out-of-order element, or a snapshot shorter than its count), and F3
  green. Any other pattern, including F1 or F2 green on all 3 runs, means stop and report. The Release
  build must be warning-free. Commit
  `test(eventingestion): the MQTT fake cannot be read while the loop is writing to it`.

Depends: T001 → T002 → T003.

## Phase 4b — the fix (backend-engineer; may not edit T002's file or `MqttConnectionLoopTests.cs`)

- [ ] **T004 [US1]** `Fakes/FakeMqttClient.cs`, per plan §2: `ConcurrentQueue<string>` for both
  recorders, exposed as `IReadOnlyList<string> => queue.ToArray()`, plus an `int connectAttempts` field
  read with `Volatile.Read` and incremented with `Interlocked.Increment` **after** the enqueue, at the
  spot that is `:210` today. Replace `SubscribedTopics.AddRange` with a `foreach` enqueue. Add one
  *why* paragraph and update the two properties' `<summary>` to say "snapshot". Touch nothing else in
  the file.
- [ ] **T005 [US1]** Verify per plan §4 steps 3–5. Run the whole assembly green with count = T001 + 3.
  `git diff --exit-code a54b11d0 -- tests/EventIngestion.Infrastructure.Tests/MqttConnectionLoopTests.cs`
  must be empty, and T002's file must be unchanged since T003's commit. Run the new class 5× green and
  record the wall times. Run the counterfactual (restore `List<string>` and `.Count`, see F1/F2 red and
  F3 green, capture verbatim, restore, `git diff` empty). Run `dotnet format --verify-no-changes` on the
  test project. Commit
  `fix(eventingestion): record the MQTT fake's credentials and topics so a count never outruns them`.

Depends: T003 → T004 → T005.

## Bookkeeping (orchestrator)

- [ ] **T006** PR body. `Closes #2451`. Quote T003's red and T005's counterfactual verbatim. Record the
  premise correction (spec §1: `:148` never enumerates a growing list, and the real hazard is count
  publication ahead of the element), and that `SubscribedTopics` was fixed alongside for the same
  defect. `Phase 5`: this is a unit-test double with no running system, so the 5× repeat plus the
  counterfactual is the observation. Latency: N/A. Re-check spec number 246 against unmerged branches
  before merge (243–245 exist on open branches today). After merge, confirm #2451 closed.
- [ ] **T007 (optional)** File a `tech-debt` issue for the simulator fake's `Published` `List<string>`
  (spec §4). Do not fix it here.

## Out of scope — do not do

- `tests/ScenarioSimulator.Tests/**` · `MqttConnectionLoopTests.cs` · `IsConnected` / `refusals` /
  `holdFor` / `staleDuringNextConnect` in either fake · any `src/` file · any backoff or window
  constant.
