# Plan 254 — The credential the fake never kept

**Spec:** `specs/254-the-credential-the-fake-never-kept/spec.md`
**Issue:** #2450

---

## 1. Where this lives

This is not a bounded context. `src/ScenarioSimulator/` is the dev-only simulated
PLC (ADR-0111), a single project with no Domain/Application/Infrastructure
split. It involves no `Shared.Contracts` message, no integration event, no
entities, no value objects and no invariants. The boundary rules are trivially
met: no project reference is added, and EventIngestion's files are the
**reference**, which is read and not edited.

**Nothing under `src/` changes.**

---

## 2. Fake change — `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs`

The member is named as it is in EventIngestion's fake, so a reader of both suites meets one vocabulary.

| Element | Shape | Mirrors |
|---|---|---|
| field | `private readonly ConcurrentQueue<string> presentedCredentials = new();` | EI fake, line 31 |
| property | `public IReadOnlyList<string> PresentedCredentials => presentedCredentials.ToArray();` | EI fake, line 56, with its `<summary>` carried over: the password, in order, as a point-in-time snapshot, never short of a count a reader has already seen |
| capture | `string presented = System.Text.Encoding.UTF8.GetString(options.Credentials?.GetPassword(options) ?? []);` | EI fake, lines 226–227 |

### 2.1 Placement inside `ConnectAsync`, the one deliberate difference from EI

The capture is split into a **read** and an **enqueue**:

1. **Read** the password into a local **after** the liveness check
   (`ThrowIfConnected`) and **before** the connect gate. The real client calls
   `GetPassword` while it builds the CONNECT packet, which happens before it
   waits for a CONNACK. The gate models the broker answering slowly, so a
   password read after the gate would record whatever the slot held when the
   test released the gate, not what was sent. EventIngestion's fake has no gate,
   so there the two moments coincide.
2. **Enqueue** it immediately **before** the existing
   `Interlocked.Increment(ref connectAttempts)`, on both the refusal path and
   the success path. This keeps EventIngestion's ordering invariant: a
   `ConnectAttempts` value a reader has observed always has its element already
   stored. `PresentedCredentials.Count` then equals `ConnectAttempts` for every
   CONNECT that was answered.

The consequences follow from that placement. A CONNECT that `ThrowIfConnected`
refuses records nothing, because no packet was sent. A gated CONNECT that is
cancelled before release records nothing, and `ConnectAttempts` does not move
either. A **refused** CONNECT (`refusals > 0`) records its credential: it was
presented, and the broker said no. That is #2038's shape exactly.

### 2.2 Doc comments

- Add a `<summary>` to the property, adapted from EI's. Add one sentence saying
  why it records the **password** rather than counting mints: a loop that mints
  and then presents a stale token passes a count and fails this (#2038).
- Extend `ConnectAsync`'s `<summary>` with one short paragraph on §2.1: why the
  read comes before the gate and the enqueue after it.
- **Correct the class-level "what differs" paragraph.** It currently says the
  simulator's fake *"gates connects and forces publish failures where that one
  records credentials and topics"*. Credentials are now recorded by both.
  Rewrite it so the listed difference is that the simulator's fake gates
  connects and forces publish failures, while EventIngestion's records
  subscribed topics. The paragraph should say that credential recording and the
  drop/hold controls are shared.

File size: the fake is already 337 lines, past ADR-0084's advisory 300-line
limit. That is a warning carved out of `TreatWarningsAsErrors`, not a reason to
split a test double. Record it in the PR.

---

## 3. New tests (phase 4c, two new files)

Existing test files are not edited. `MqttPublisherDropAccountingTests.cs` (375
lines) and `MqttPublisherBackoffTests.cs` (277 lines) own private harnesses, and
adding to them would breach the advisory and mix concerns.

### 3.1 `tests/ScenarioSimulator.Tests/FakeMqttClientCredentialContractTests.cs`

These tests pin the double's fidelity, as `FakeMqttClientContractTests` and
`FakeMqttClientDropHoldContractTests` already do. They drive the fake directly
with `MqttClientOptions` built from a test-local `IMqttClientCredentialsProvider`
that reads a mutable string slot. That is the same shape as the publisher's
`TokenCredentials`, reproduced locally because that class is private.

1. `Each_connect_records_the_password_it_presented_at_the_time_it_was_sent`:
   `RefuseNextConnects(1)`; slot = `"first"`; connect (answered `NotAuthorized`);
   slot = `"second"`; connect (answered `Success`). Assert
   `PresentedCredentials` is `["first","second"]` and `ConnectAttempts` is `2`.
2. `A_connect_refused_because_the_client_is_already_connected_records_nothing`:
   connect once successfully, then connect again and expect
   `InvalidOperationException`. Assert `PresentedCredentials` is `["first"]` and
   `ConnectAttempts` is `1`.
3. `A_gated_connect_records_the_credential_read_before_the_gate`:
   `GateConnect()`; slot = `"before"`; start `ConnectAsync` without awaiting it;
   poll until the call has reached the gate; slot = `"after"`; `AllowConnect()`;
   await it. Assert `PresentedCredentials` is `["before"]`.

   **Wait for the read, not for a proxy of it.** The test-local credential
   provider completes a `TaskCompletionSource` inside `GetPassword`. The test
   awaits that signal with a deadline (`WaitAsync`, ADR-0150), and only then
   changes the slot and releases the gate. Do **not** poll `Options`: the fake
   assigns it before the liveness check, so a test that changed the slot on
   seeing it could race the read and produce a false red. Under CF4, where the
   read moves after the gate, the signal never arrives while the gate is closed.
   The deadline then expires, and the test fails with a message saying the fake
   did not read the credential before the gate. That is deterministic, and it
   cannot hang.

Every assertion carries a `because` that says what a failure means.

### 3.2 `tests/ScenarioSimulator.Tests/MqttPublisherCredentialTests.cs`

This is a port of EI's `Each_attempt_presents_a_freshly_minted_credential`, with
its own small harness in the pattern of `MqttPublisherBackoffTests.PublisherUnderTest`:

- the publisher built through the **internal** constructor with a
  `FakeMqttClient`, a `RecordingLogger<MqttPublisher>`, and a brisk
  `new MqttBackoff(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(4))`
  (EI's `Brisk()`);
- a **`CountingKeycloak`** handler answering
  `{"access_token":"token-{n}","expires_in":0,…}`, carried over from EI with its
  doc comment. `expires_in: 0` sets `refreshAfter = now`, so
  `ClientCredentialsTokenProvider`'s cache never answers. Without it, the test
  cannot tell minting from remembering (`spec.md` §1, "blind twice over");
- `DisposeAsync` disposes the publisher and the token provider.

Test: `Each_attempt_presents_a_freshly_minted_credential`.
`RefuseNextConnects(2)`; `StartAsync`; poll until `ConnectAttempts >= 3`
(ADR-0150, 10 s deadline); take the first three `PresentedCredentials` and assert
they are `["token-1","token-2","token-3"]`. The `because` names #2038: a
publisher that re-presents a dead JWT forever, which only a restart recovers.

---

## 4. Phase-4a colour, and why the red is a counterfactual red

**This is behaviour-changing, so the colour is RED.** The suite gains an
observation it could not make before, the issue asks for red-then-green, and
ambiguity resolves to red anyway.

**No ordinary-run red is available, and that is structural, not a shortcut.**
There are two reasons, the same two that spec 179's T2 had:

1. **Production is already correct.** `MqttPublisher` mints before every
   attempt, so under correct code the publisher test is green on arrival. Spec
   179 predicted exactly this for G3 ("green on arrival").
2. **The observation point is a new member.** A test written before the fake
   change does not compile (CS1061). Repo precedent rejects a compile failure as
   a red (spec 243 T002, spec 179 T003: "not a compile error"). ADR-0144 says
   not to manufacture a runtime test to satisfy the ritual.

So the red evidence is the **counterfactual pair**, as in spec 179's Run A and
Run B:

- **Run A (4a):** reconstruct #2038 in production, run the unchanged suite, and
  observe everything green. The suite cannot see the defect, and this is the
  "before".
- **Run B (4c):** apply the same reconstruction with the fake extended and the
  new test present, and observe red with a message naming the reused
  credential. This is the red the gate asks for, and it is quoted in the PR body.

The orchestrator must not read the publisher test's green-on-arrival as a phase-4
failure. It must read Run A green / Run B red as the evidence, and it must reject
a compile error offered as the red.

---

## 5. Counterfactuals

All patches are transient. After each one, run `git checkout -- <file>` and then
`git diff --exit-code -- src/ tests/ScenarioSimulator.Tests/Fakes/`. A restored
file keeps its old timestamp, so re-run with `--no-incremental` or confirm that
the pass count moved.

| # | File | Patch | Run A (unchanged fake, before 4b) | Run B (after 4b + 4c) |
|---|---|---|---|---|
| CF1 | `src/ScenarioSimulator/Mqtt/MqttPublisher.cs` `ConnectAsync` | `token.Value = await tokens.GetAccessTokenAsync(…)` → `if (token.Value.Length == 0) { token.Value = await tokens.GetAccessTokenAsync(…); }`: mint once, reuse forever, which is #2038 | whole assembly **green** | publisher test **red**: `["token-1","token-1","token-1"]` |
| CF2 | same | `_ = await tokens.GetAccessTokenAsync(…);`: mints every attempt but never stores the result, so the stale slot is presented. This is the shape EI's fake doc names ("mints a token and then presents a stale one"), and a mint count would pass it | whole assembly **green** | publisher test **red**: `["","",""]` |
| CF3 | `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs` | record lazily: enqueue the options and resolve `GetPassword` in the property getter | n/a | contract test 1 **red** (`["second","second"]`); publisher test **red** (latest token repeated) |
| CF4 | same | move the read to after the gate | n/a | contract test 3 **red** (the read-signal deadline expires with the gate closed); tests 1 and 2 stay green, and that contrast is what shows test 3 is needed |

CF1 and CF2 are the ones that matter. CF2 is the stronger of the two: it is the
defect a call-count assertion cannot see, which is the reason both fakes record
the password and not the number of mints.

---

## 6. Phase-4 roles and file ownership

| Step | Agent | Owns | Does |
|---|---|---|---|
| 4a | `test-writer` | nothing kept | baseline run of the whole `ScenarioSimulator.Tests`, twice; **Run A** (CF1, CF2) against the unchanged fake, observed green, reverted |
| 4b | `backend-engineer` | `Fakes/FakeMqttClient.cs` only | §2; runs the assembly (green, same pass count as the baseline, because no existing test reads the new member) |
| 4c | `test-writer` | the two new test files | §3; observes green; **Run B** (CF1–CF4), observed red, reverted; revert gate |

The ordering (fake before tests) is forced by the compile barrier in §4 (reason 2). The
engineer does not write the tests that judge the fake. The test-writer writes
them from this plan's contract, not from the engineer's code, and CF3 and CF4
prove they can fail. The boundary is set **by file**. `backend-engineer`,
because the change is a C# test double with a real-client contract to match.

---

## 7. Constitution / ADR alignment

- §II primitives: no domain model is touched.
- §IV latency: N/A (dev-only simulator, and no `src/` change).
- §Testing / ADR-0139: new behaviour, observed red. The red is a counterfactual
  red, and §4 says why.
- ADR-0105: no new argument guard. The test-local credential provider is a test
  type.
- ADR-0036: one field, one property, two lines in `ConnectAsync`, and no helper
  shared across assemblies.
- ADR-0144: the lane writes no ADR, and none is needed (`spec.md` §3).
