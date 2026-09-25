# Tasks 254 — The credential the fake never kept

**Spec:** `specs/254-the-credential-the-fake-never-kept/spec.md`
**Plan:** `specs/254-the-credential-the-fake-never-kept/plan.md`
**Issue:** #2450 is already on Project #13, according to the lane's pickup. The
`gh` GraphQL quota was exhausted at phase 3, so this was not re-verified by
`content.url`. **No `item-add` is needed**, and no task issues are created
(spec-028 convention).

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **RED, observed under a counterfactual**

This is behaviour-changing: the suite gains an observation it could not make
before, and the issue asks for red-then-green. `plan.md` §4 explains why the red
cannot be an ordinary-run red. Production is already correct, and a test that
used the new member before it existed would be a compile error, which is not an
acceptable red (spec 243, spec 179). Spec 179's T2 had the same shape. The
evidence is therefore:

- **Run A (T002):** the unchanged suite stays **green** under the reconstructed
  #2038 defect. This is the blindness, recorded.
- **Run B (T007):** the new publisher test is **red** under the same
  reconstruction. That is the red the gate needs, and it is quoted verbatim in
  the PR body.

The publisher test arriving green on correct code is expected. It is not a
phase-4 failure. **A compile error presented as the red is.**

### Engineer roles: `test-writer` → `backend-engineer` → `test-writer`

**The engineer this needs is `backend-engineer`.** The only file it changes is
the C# test double. No `src/` file changes (verified: the fake can already reach
the credential through `options.Credentials`). The ordering puts the fake before
the tests because of the compile barrier. Test independence is kept because the
test-writer writes against `plan.md` §3's contract, and CF3 and CF4 prove the
contract tests can fail.

### Files phase 4 may touch: exactly these

1. `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs` (4b only)
2. `tests/ScenarioSimulator.Tests/FakeMqttClientCredentialContractTests.cs` (4c, new)
3. `tests/ScenarioSimulator.Tests/MqttPublisherCredentialTests.cs` (4c, new)
4. `specs/254-the-credential-the-fake-never-kept/verification.md` (phase 5, new)

**Forbidden:**

- Everything under `src/`. The CF1 and CF2 patches to `MqttPublisher.cs` are
  transient, and T002 and T007 gate their revert.
- Every other file under `tests/ScenarioSimulator.Tests/`. That includes the
  three existing publisher test files and their constant-token stubs.
- Everything under `tests/EventIngestion.Infrastructure.Tests/`. It is the reference.
- Any project reference.

### No new ADR

`spec.md` §3 gives the reasoning: there is no `src/` change, no public or
configuration surface, and no new pattern. The mechanism, a `PresentedCredentials`
history recorded at CONNECT, already exists in the twin fake for this exact
purpose (#2038). The lane may not write an ADR, and this change does not need one.

### Stack

**Not needed.** This is plain xUnit with no `AspireFixture`, no Docker and no
broker. Do not stop any running AppHost. If MSB3027 appears, report it instead
of stopping a stack.

---

## Tasks

`[P]` means the task owns disjoint files and may run in parallel with its
siblings. Almost nothing here can run in parallel: there is one assembly and one
slice, and 4a → 4b → 4c is a gate sequence.

### Foundational

*(None: no Shared.Kernel, Shared.Contracts, AppHost or Aspire change.)*

### Phase 4a — `test-writer` (US1)

- **[T001] [US1]** Baseline: on the untouched branch, run
  `dotnet test tests/ScenarioSimulator.Tests` **twice**, because the first run
  after machine churn can read like a regression. Record the pass count and
  quote the summary verbatim.
  *Depends on: nothing.*

- **[T002] [US1]** **Run A (the blindness)**. Apply CF1 (`plan.md` §5) to
  `src/ScenarioSimulator/Mqtt/MqttPublisher.cs`, run the whole assembly, and
  record **all green** with the pass count. Revert with
  `git checkout -- src/ScenarioSimulator/Mqtt/MqttPublisher.cs`, then confirm
  `git diff --exit-code -- src/` is clean. Repeat the same steps for CF2. Quote
  both outputs. If either counterfactual turns anything red, **stop and report
  it**: the premise that the suite is blind would be false.
  *Depends on: T001.*

### Phase 4b — `backend-engineer` (US1)

- **[T003] [US1]** Make the `plan.md` §2 change in `Fakes/FakeMqttClient.cs`:
  - Add the `ConcurrentQueue<string>` field and the `PresentedCredentials`
    snapshot property.
  - Read the password after the liveness check and before the gate, and enqueue
    it immediately before `Interlocked.Increment(ref connectAttempts)`.
  - Update the doc comments: the property's summary, the `ConnectAsync`
    paragraph, and the corrected class-level "what differs" paragraph.

  Receives T001's and T002's verbatim output as the brief. Touches no other file.
  *Depends on: T002.*

- **[T004] [US1]** Run the whole `ScenarioSimulator.Tests` assembly twice. It
  should be green with the same pass count as T001, because no existing test
  reads the new member. Show that `git diff origin/develop --stat` lists only the
  fake and this spec's documents. Then run
  `dotnet test tests/EventIngestion.Infrastructure.Tests` (the control): it
  should be green and untouched. Run these one at a time, since concurrent
  builds are how MSB3027 appears. Quote the outputs.
  *Depends on: T003.*

### Phase 4c — `test-writer` (US1)

- **[T005] [P] [US1]** Write `FakeMqttClientCredentialContractTests.cs` with the
  three cases in `plan.md` §3.1. Case 3 waits on the credential provider's own
  read signal with a deadline, never on `Options` and never on a fixed delay.
  *Depends on: T004.*

- **[T006] [P] [US1]** Write `MqttPublisherCredentialTests.cs`: the harness plus
  `Each_attempt_presents_a_freshly_minted_credential` (`plan.md` §3.2). Carry
  `CountingKeycloak` over from EventIngestion **with** its doc comment about
  `expires_in: 0`, and inject a brisk `MqttBackoff`. This file is disjoint from T005's.
  *Depends on: T004.*

- **[T007] [US1]** Run the assembly: all tests green, the four new ones included.
  Quote the output. Then run **Run B**:
  - Apply CF1 through CF4 one at a time.
  - Quote each verbatim red. CF1 must show `["token-1","token-1","token-1"]`,
    and CF2 must show `["","",""]`.
  - Revert after each, and check that
    `git diff --exit-code -- src/ tests/ScenarioSimulator.Tests/Fakes/` is clean.

  Finish with a clean full run. Its pass count must show that the rebuild
  happened, because a restored file keeps its old timestamp. **This task's
  revert check gates the change.**
  *Depends on: T005, T006.*

### Phase 5 — verify

- **[T008] [US1]** Write `verification.md` with:
  - the premise table from `spec.md` §1, including "blind twice over";
  - the T001 baseline and the T004 same-count run;
  - Run A green and Run B red, side by side, per counterfactual;
  - the revert proof;
  - the ADR-0084 note on the fake's line count.

  Latency: **N/A. The simulator is dev-only (ADR-0111), and no `src/` code changes.** State this explicitly.
  *Depends on: T007.*

---

## Dependency graph

```
T001 (baseline) ─> T002 (Run A: blind) ─> T003 (fake) ─> T004 (same count + control) ─┬─> T005  ─┐
                                                                                      └─> T006  ─┴─> T007 (green + Run B + revert gate) ─> T008
```

## Definition of done

1. Run A shows the whole existing suite green under CF1 and CF2, and Run B shows
   the new publisher test red under both. Both are quoted in the PR body
   (ADR-0139).
2. `ScenarioSimulator.Tests` is green, with the pre-existing test files
   byte-identical to `origin/develop`. `EventIngestion.Infrastructure.Tests` is
   green and untouched.
3. The PR diff contains exactly the four files listed above, plus this spec's
   `spec.md`, `plan.md` and `tasks.md`. **It contains no file under `src/`**,
   and the PR body says how that was checked.
4. Commits follow Conventional Commits with **no `Co-Authored-By` footer**
   (ADR-0086), and each commit builds on its own. The PR uses `--base develop`
   (ADR-0028), and its body uses a closing keyword for #2450.
