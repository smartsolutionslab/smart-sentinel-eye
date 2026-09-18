# Tasks 179 — A fake that refuses a live reconnect

**Spec:** `specs/179-a-fake-that-refuses-a-live-reconnect/spec.md`
**Plan:** `specs/179-a-fake-that-refuses-a-live-reconnect/plan.md`
**Issue:** #2233 — already on Project #13, status **Todo** (verified by
`content.url`, not by the number filter). **No `item-add` needed.**

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **RED then GREEN**

The fake gains real behaviour it did not have — it could not express this failure
mode before — so this is behaviour-changing, not behaviour-preserving. Ambiguity
resolves to red, and there is none here.

**Two reds, of two different kinds, and the difference is stated so neither is
confused for the other:**

- **T1 is observed red in the ordinary run**, against the unfixed fake, and that
  is the phase-4 gate's red. `Should.ThrowAsync<InvalidOperationException>`
  against a fake that answers `Success` fails for exactly the right reason.
- **T2's new assertion (`FailedConnects.ShouldBe(0)`) is green on arrival**, and
  that is correct rather than a shortcut. The production loop is already fixed
  (#2130 landed), so nothing in an ordinary run can make it fire. Its red is
  observed under the **counterfactual** — the injected pre-#2130 defect — and
  **only after** the fake is fixed. That is the whole finding of #2233 expressed
  as a test: before the fake change the assertion cannot fail *at all*, which is
  an assertion that checks nothing; after it, it fails under the defect it names.
  Run A and Run B in `plan.md` §5 are that proof, and both outputs are quoted.

An assertion that cannot fail is the defect this repo keeps finding. T2 is shipped
only because Run B demonstrates it can now fail.

### Engineer roles: `test-writer` (4a) then `backend-engineer` (4b) — the split holds

Every file in this change is under `tests/`, which makes this the hybrid case:
the "production code" being changed *is* a test double. The split is kept anyway,
and the reason is the risk it exists for. A single agent writing T1 and then
changing the fake can make T1 pass by weakening T1 — it is one `Should.ThrowAsync`
away. So:

- **4a — `test-writer`** owns `FakeMqttClientContractTests.cs` and
  `MqttPublisherDropAccountingTests.cs`. Writes them, runs them, observes T1 red,
  returns the **verbatim** output. Runs counterfactual Run A. **Must not touch
  `Fakes/FakeMqttClient.cs`.**
- **4b — `backend-engineer`** owns `Fakes/FakeMqttClient.cs` and nothing else.
  Receives 4a's verbatim output as its brief. **May not edit T1 or T2.** Runs
  counterfactual Run B and the revert gate.

The boundary is **by file, not by directory**. `backend-engineer` rather than a
second test role because the work is a behaviour change in C# with a real-client
contract to match, which is what that brief is for.

### Audit-found gaps beyond the named one

| Gap | Fixed in this PR? |
|---|---|
| **G1** — `ConnectAsync` does not emulate `ThrowIfConnected` | **Yes.** The issue's named gap and this spec's whole slice. |
| **G2** — no `DropEveryConnectionImmediately` / `HoldEveryConnectionFor`, so the die-on-arrival spin and the takeover flap are invisible in the simulator's suite although `MqttPublisher` runs the same `ResetIfHeld` | **No — own follow-up issue (T010).** Needs an injectable `MqttBackoff` on `MqttPublisher`; today it is a hardcoded `new()` at 1 s/30 s. That is a production signature change and a multi-second test window. |
| **G3** — no `PresentedCredentials`, so the simulator is blind to a loop that mints a token and presents a stale one (#2038's property, and `MqttPublisher` does mint per attempt) | **No — own follow-up issue (T011).** The fix is a new assertion about production credential freshness, green on arrival, in #2038's territory. |
| **G4** — `ConnectAttempts` is an unsynchronised `List<T>` read in **EventIngestion's** fake, and `MqttConnectionLoopTests:148` enumerates that list from the test thread while the loop thread adds to it; the simulator uses `Interlocked`/`Volatile` and is the stronger one | **No — own follow-up issue (T012).** Opposite direction, and a flake risk rather than a blindness. |
| `SubscribeAsync` liveness refusal (EI only); `PublishAsync` recording and failure switches (SS only); `FirstSubscribe` (EI only) | **No, and deliberately not filed.** Unreachable — the publisher never subscribes and the subscriber never publishes, and both loops' doc comments list that as an intended difference. A fake member no production path reaches cannot make a suite blind. |
| Neither fake's `PublishAsync` throws on its own when `IsConnected` is false, though the real client does | **No, and deliberately not filed.** Symmetric, and `Published.ShouldBeEmpty()` already catches the only defect it would hide. Recorded in `spec.md` §2. |

Nothing found is silently dropped: each row is either fixed, filed, or refused
with a reason.

### Files phase 4 may touch

**Exactly these, and no others:**

1. `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs` — 4b only
2. `tests/ScenarioSimulator.Tests/FakeMqttClientContractTests.cs` — 4a only, new
3. `tests/ScenarioSimulator.Tests/MqttPublisherDropAccountingTests.cs` — 4a only
4. `specs/179-a-fake-that-refuses-a-live-reconnect/verification.md` — new, phase 5

**Forbidden:** everything under `src/` (the counterfactual's patch to
`src/ScenarioSimulator/Mqtt/MqttPublisher.cs` is transient and must be reverted —
T008 is the gate), everything under
`tests/EventIngestion.Infrastructure.Tests/` (the control), every other file in
`tests/ScenarioSimulator.Tests/`, and any new project reference.

### No new ADR

Closing a test-infrastructure asymmetry against a sibling that already behaves
the wanted way is not a design decision. The decisions in play are already made:
ADR-0052 (hand-written fakes), ADR-0053 (naming), ADR-0139 + constitution
§Testing (observed red), ADR-0036 (smallest change, no speculative generality —
the reason G2/G3/G4 are filed rather than bundled), ADR-0150 (wait on a
condition), ADR-0084 (why T1 is a new file). The lane may not write an ADR anyway.

### Stack

**Not needed.** Both suites are plain xUnit against hand-written fakes — no
`AspireFixture`, no Docker, no broker. The running Aspire stack (**pid 3312**)
must be left alone. `dotnet build` can fail with MSB3027 while a stack holds the
service binaries, but the two test assemblies here are not among them; if a build
lock appears, report it rather than stopping the stack.

---

## Tasks

`[P]` marks tasks that own disjoint files and may run in parallel. There is
little parallelism here by design: one assembly, one slice, and 4a→4b is a
sequential gate rather than a dependency that could be relaxed.

### Foundational

*(None. No `Shared.Kernel`/`Shared.Contracts`/AppHost/Aspire change; nothing
blocks anything outside this spec.)*

### Phase 4a — `test-writer` (US1)

- **[T001] [US1]** Write `tests/ScenarioSimulator.Tests/FakeMqttClientContractTests.cs`
  with the five cases in `plan.md` §3. Sentence-style names (ADR-0053), Shouldly,
  every assertion carrying a `because` that says what a failure would mean. The
  gated case must be bounded by a wall-clock deadline, not by a bare `await` that
  would hang the run (ADR-0150).
  *Depends on: nothing.*

- **[T002] [P] [US1]** Edit `MqttPublisherDropAccountingTests.cs`: add
  `PublisherUnderTest.FailedConnects` (log-line count on `could not connect`, with
  the doc comment's reasoning carried over from `LoopUnderTest.FailedConnects`);
  add the `ShouldBe(0, …)` assertion; delete the now-false "What this fake can and
  cannot show" paragraph and the `ConnectAttempts` hedge; rename the test to
  `A_stale_disconnect_landing_mid_connect_does_not_end_the_connection_it_landed_in`.
  Disjoint file from T001.
  *Depends on: nothing. Parallel with T001.*

- **[T003] [US1]** Run `dotnet test tests/ScenarioSimulator.Tests`. **Observe T1
  red.** Capture the verbatim failure — the `Should.ThrowAsync` message included —
  and confirm the red is the missing throw and not a compile error, a wrong
  namespace, or a hang. T2 is expected green here; say so explicitly rather than
  leaving it unmentioned.
  *Depends on: T001, T002.*

- **[T004] [US1]** Counterfactual **Run A**: apply the three-line pre-#2130
  injection from `plan.md` §5 to `src/ScenarioSimulator/Mqtt/MqttPublisher.cs`, run
  the renamed T2, record `FailedConnects` = 0 with two announced connections —
  the blindness — then `git checkout -- src/ScenarioSimulator/Mqtt/MqttPublisher.cs`
  and verify `git diff --exit-code -- src/` is silent. Quote the output verbatim.
  *Depends on: T002, T003.*

### Phase 4b — `backend-engineer` (US1)

- **[T005] [US1]** Add the liveness refusal to
  `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs` `ConnectAsync`: the
  exact type and message of `MqttClient.ThrowIfConnected`, placed after the
  stale-disconnect raise and **before** the connect gate, recording nothing before
  it throws. Extend the method's `<summary>` with EventIngestion's third
  paragraph, adapted: name `ThrowIfConnected`, say `ConnectAttempts` does not
  move, and say why the check precedes the gate.
  *Depends on: T003's red. May not edit T001/T002's files.*

- **[T006] [US1]** Run the **whole** `ScenarioSimulator.Tests` assembly (not a
  filter) twice — the first run after machine churn reads like a regression. All
  green, T1 included. Quote the output.
  *Depends on: T005.*

- **[T007] [US1]** Run `dotnet test tests/EventIngestion.Infrastructure.Tests` —
  the control. Green, and `git diff --stat` shows no file under
  `tests/EventIngestion.Infrastructure.Tests/`.
  *Depends on: T005. Parallel-safe with T006 in principle; run serially — one
  machine, and a concurrent build is how MSB3027 appears.*

- **[T008] [US1]** Counterfactual **Run B**: re-apply the injection, widen the
  observation window to 6 s locally, run T2, record `FailedConnects` ≥ 2 each
  carrying *It is not allowed to connect with a server after the connection is
  established.* and `IsConnected` still true — the permanent refusal. Then revert
  **both** the injection and the widened window, and verify with
  `git checkout -- src/ScenarioSimulator/Mqtt/MqttPublisher.cs`,
  `git diff --exit-code -- src/`, `git status --porcelain -- src/`. Re-run T2
  green afterwards, checking the reported pass count rather than trusting a
  skipped rebuild (a restored file keeps its old timestamp). Quote both outputs.
  **This task's revert verification is the gate on the whole change.**
  *Depends on: T005, T006.*

### Phase 5 — verify

- **[T009] [US1]** Write `verification.md`: the premise check, the four verbatim
  outputs (T003 red, T004 Run A, T006 green, T008 Run B), the before/after symptom
  table from `spec.md` §4, the revert proof, and a §"Findings not fixed here"
  naming G2/G3/G4 with their issue numbers. Latency: **N/A, no production code
  changes** — stated, not omitted.
  *Depends on: T003, T004, T006, T007, T008.*

### Follow-ups — file as issues, do not implement (Phase 3 output)

- **[T010] [P]** File G2: *the simulator's MQTT fake cannot end a connection it
  answered, so the flap `ResetIfHeld` guards is invisible in one of the two
  suites*. Body: names `DropEveryConnectionImmediately` and
  `HoldEveryConnectionFor`, the two EventIngestion tests that use them
  (`A_connection_that_dies_on_arrival_is_retried_with_a_delay_rather_than_a_spin`,
  `A_connection_held_just_past_the_floor_still_backs_off`), and the blocker: an
  injectable `MqttBackoff` on `MqttPublisher`. Labels `tech-debt`; **no
  `agent:ready`** — it needs a production signature decision first. Add to
  Project #13.

- **[T011] [P]** File G3: *the simulator's MQTT fake records no presented
  credential, so a stale-token regression would pass*. Body: names
  `PresentedCredentials`, `MqttConnectionLoopTests:148`'s use of it, #2038's
  property, and that `MqttPublisher` mints per attempt through `TokenCredentials`
  with nothing asserting the sequence. Labels `tech-debt`; **no `agent:ready`**.
  Add to Project #13.

- **[T012] [P]** File G4: *`FakeMqttClient.ConnectAttempts` is an unsynchronised
  `List<T>` read, and one test enumerates the list across threads*. Body: names
  `tests/EventIngestion.Infrastructure.Tests/Fakes/FakeMqttClient.cs`
  (`ConnectAttempts => PresentedCredentials.Count`, `PresentedCredentials.Add` on
  the loop thread), `MqttConnectionLoopTests:148`'s
  `[.. loop.Client.PresentedCredentials.Take(3)]`, and the simulator's
  `Interlocked`/`Volatile` shape as the fix. Labels `tech-debt`; **no
  `agent:ready`**. Add to Project #13.

T010–T012 own disjoint artefacts (three separate issues) and are the only
genuinely parallel work in this spec.

---

## Dependency graph

```
T001 ─┐
T002 ─┴─> T003 (RED, the gate) ─> T004 (Run A) ─┐
                                                 ├─> T005 (fix) ─> T006 ─> T007 ─> T008 (Run B + revert gate) ─> T009
T010 [P] ─ T011 [P] ─ T012 [P]  (independent of everything above)
```

## Definition of done

1. T1 was observed red for the missing throw, and the failure is quoted in the PR
   body.
2. The whole `ScenarioSimulator.Tests` assembly is green, twice.
3. `EventIngestion.Infrastructure.Tests` is green and untouched.
4. Run A and Run B are both recorded, and the contrast — 0 refusals against ≥ 2 —
   is the evidence the fix does what #2233 asked for.
5. **No file under `src/` appears in the PR diff**, and the PR body says how that
   was verified.
6. G2, G3, G4 are filed, on Project #13, and named in `verification.md`.
7. Commits are Conventional Commits with **no `Co-Authored-By` footer**
   (ADR-0086). PR base `develop` (`--base develop`, ADR-0028).
