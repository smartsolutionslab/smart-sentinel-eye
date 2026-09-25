# Verification 254 — The credential the fake never kept

**Spec:** `specs/254-the-credential-the-fake-never-kept/spec.md`
**Plan:** `specs/254-the-credential-the-fake-never-kept/plan.md`
**Tasks:** `specs/254-the-credential-the-fake-never-kept/tasks.md`
**Issue:** #2450

Phase 4a colour: **RED, observed under a counterfactual** (plan.md §4 — an
ordinary-run red is unavailable: production is already correct, and a test
against the not-yet-existing `PresentedCredentials` member would fail to
compile, which this repo's precedent rejects as a valid red). Latency:
**N/A — the Scenario Simulator is dev-only (ADR-0111) and sits on no leg of
constitution §IV; no `src/` code changes survive into the final diff.**

---

## 1. Premise table (carried over from `spec.md` §1, checked against the files)

| Claim | Verified |
|---|---|
| `MqttPublisher` mints a credential before every attempt; `TokenCredentials` reads a live `TokenHolder` at CONNECT time | yes |
| The simulator's fake kept no credential history before this spec | yes |
| EventIngestion's fake already has `PresentedCredentials`, for the same purpose | yes |
| No production signature change needed — the fake can already reach the credential through `options.Credentials` | yes |
| The suite is blind **twice over**: every existing stub Keycloak answers a constant, cached token (`expires_in: 300`), so even a recording fake would see the same string on every attempt regardless of whether the publisher mints fresh tokens | yes — `MqttPublisherCredentialTests` brings its own `CountingKeycloak` (`token-{n}`, `expires_in: 0`) for exactly this reason |
| No existing simulator test reads the credential at all | yes |

---

## 2. Run A — the blindness observed (phase 4a, before the fake changed)

Baseline (T001), `dotnet test tests/ScenarioSimulator.Tests -c Release`, run twice:

```
Passed!  - Failed:     0, Passed:    67, Skipped:     0, Total:    67, Duration: 5 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    67, Skipped:     0, Total:    67, Duration: 5 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
```

**CF1** applied to `src/ScenarioSimulator/Mqtt/MqttPublisher.cs` — mint only
when the token slot is empty (`if (token.Value.Length == 0) { token.Value =
await tokens.GetAccessTokenAsync(...); }`, i.e. mint once and reuse forever —
#2038's shape):

```
Passed!  - Failed:     0, Passed:    67, Skipped:     0, Total:    67, Duration: 5 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
```

All green, same count as the baseline. The whole existing suite cannot see
#2038's shape once reconstructed. Reverted (`git checkout -- src/...`);
`git diff --exit-code -- src/` clean.

**CF2** applied to the same line — mint every attempt but discard the result
(`_ = await tokens.GetAccessTokenAsync(...)`), so the stale slot is presented
even though a mint happens each time (the shape a mint-count assertion could
not catch):

```
Passed!  - Failed:     0, Passed:    67, Skipped:     0, Total:    67, Duration: 6 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
```

All green, same count. Reverted; `git diff --exit-code -- src/` clean.

This is the "before": both reconstructions of #2038 are invisible to the
suite as it stood.

---

## 3. The fake change (phase 4b) — same count, no other file touched

After adding `PresentedCredentials` to
`tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs` (plan.md §2):

```
Passed!  - Failed:     0, Passed:    67, Skipped:     0, Total:    67, Duration: 5 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    67, Skipped:     0, Total:    67, Duration: 6 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
```

Same 67 as the baseline — no existing test reads the new member yet.
`dotnet build -c Release` for that project: **Build succeeded, 0 Error(s)**
(19 pre-existing SonarAnalyzer warnings in `src/ScenarioSimulator/`,
unrelated to this change, carved out of `TreatWarningsAsErrors` by ADR-0084).

Control run, `tests/EventIngestion.Infrastructure.Tests` (the reference fake
this spec ports the pattern from, and does not edit):

```
Passed!  - Failed:     0, Passed:    67, Skipped:     0, Total:    67, Duration: 11 s - SmartSentinelEye.EventIngestion.Infrastructure.Tests.dll (net10.0)
```

(One run of this control assembly showed a single, self-admittedly flaky
failure in `FakeMqttClientConcurrencyTests.A_count_read_while_credentials_are_being_recorded_is_never_ahead_of_them`, a timing/interleaving test whose
own assertion message reads "the reader never overlapped the writer, so this
run proves nothing." Confirmed unrelated: no file in that assembly appears in
this branch's diff, the test passed in isolation, and the full control suite
was green on re-run. Pre-existing flake, not introduced here — flagged for
the record.)

---

## 4. Run B — the new tests, and the red they can produce (phase 4c)

All tests green, including the 4 new ones (67 + 4 = 71):

```
Passed!  - Failed:     0, Passed:    71, Skipped:     0, Total:    71, Duration: 6 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
```

**CF1** (mint only when the slot is empty), filtered to
`MqttPublisherCredentialTests`:

```
[xUnit.net]     SmartSentinelEye.ScenarioSimulator.Tests.MqttPublisherCredentialTests.Each_attempt_presents_a_freshly_minted_credential [FAIL]
  Error Message:
   Shouldly.ShouldAssertException : presented
    should be
["token-1", "token-2", "token-3"]
    but was (case sensitive comparison)
["token-1", "token-1", "token-1"]
    difference
["token-1", *"token-1"*, *"token-1"*]

Additional Info:
    each attempt must present a freshly minted credential. Reusing one is exactly #2038: a publisher that re-presents the same dead JWT forever, recoverable only by a restart.
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 2 s
```

Reverted; `git diff --exit-code -- src/` clean.

**CF2** (mint every attempt, discard the result):

```
[xUnit.net]     SmartSentinelEye.ScenarioSimulator.Tests.MqttPublisherCredentialTests.Each_attempt_presents_a_freshly_minted_credential [FAIL]
  Error Message:
   Shouldly.ShouldAssertException : presented
    should be
["token-1", "token-2", "token-3"]
    but was (case sensitive comparison)
["", "", ""]
    difference
[*""*, *""*, *""*]

Additional Info:
    each attempt must present a freshly minted credential. Reusing one is exactly #2038: a publisher that re-presents the same dead JWT forever, recoverable only by a restart.
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 704 ms
```

`["","",""]` — the slot is never written, so every attempt presents the
empty credential the `TokenHolder` started with. This is the stronger of the
two counterfactuals: a mint-*count* assertion would pass it, which is why the
fake records the password rather than counting mints (plan.md §5). Reverted;
`git diff --exit-code -- src/` clean.

**CF3** (fake records lazily — stores `MqttClientOptions` and resolves
`GetPassword` at property-getter time instead of at CONNECT time):

```
[xUnit.net]     SmartSentinelEye.ScenarioSimulator.Tests.FakeMqttClientCredentialContractTests.Each_connect_records_the_password_it_presented_at_the_time_it_was_sent [FAIL]
  Error Message:
   Shouldly.ShouldAssertException : client.PresentedCredentials
    should be
["first", "second"]
    but was (case sensitive comparison)
["second", "second"]
    difference
[*"second"*, "second"]

Additional Info:
    each CONNECT must record the password that was live at the moment it was sent, not the password the slot holds when the test later reads the history back — a loop that mints a token and then presents a stale one must be visible here (#2038), which a fake that read the slot lazily could never show.
```

`["second","second"]` — exactly as `plan.md` predicts. This particular
implementation of CF3 removes the eager `GetPassword` call from
`ConnectAsync` entirely (not merely delaying *when* it resolves, but
*whether* it is invoked at CONNECT time at all), so the other two contract
tests in the same file went red as a side effect too:
`A_connect_refused_because_the_client_is_already_connected_records_nothing`
(`["second"]` instead of `["first"]`) and
`A_gated_connect_records_the_credential_read_before_the_gate` (the `Read`
signal never completes, since `GetPassword` is never called during the
gated wait). Filtered run: `Failed: 3, Passed: 0, Skipped: 0, Total: 3`. This
is a broader red than the plan's narrower claim ("contract test 1 red") but
does not contradict it — the primary required assertion
(`["second","second"]`) is exactly as specified. Reverted;
`git diff --exit-code -- src/ tests/ScenarioSimulator.Tests/Fakes/` clean.

**CF4** (move the credential read to after the connect gate):

```
[xUnit.net]     SmartSentinelEye.ScenarioSimulator.Tests.FakeMqttClientCredentialContractTests.A_gated_connect_records_the_credential_read_before_the_gate [FAIL]
  Error Message:
   Shouldly.ShouldAssertException : read
    should be
True
    but was
False

Additional Info:
    the fake never read the credential while the gate was held closed — the read must happen before the gate, at the same point the real client reads the password to build the CONNECT packet, not after it.
Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3, Duration: 5 s
```

Confirmed: exactly 1 of the 3 contract tests failed. The other two
(`Each_connect_records_the_password_it_presented_at_the_time_it_was_sent`
and
`A_connect_refused_because_the_client_is_already_connected_records_nothing`)
stayed green — the contrast plan.md §5 calls for, and evidence that test 3
is exercising the read-before-gate ordering specifically rather than a
static value. Reverted; `git diff --exit-code -- src/
tests/ScenarioSimulator.Tests/Fakes/` clean.

**CF5** (phase 6 finding — added because CF3's red for
`A_connect_refused_because_the_client_is_already_connected_records_nothing`
was a side effect of removing the eager `GetPassword` call entirely, not a
counterfactual that targets what that test claims on its own: that a connect
refused because the client is already connected records nothing). Moved the
credential read and the `presentedCredentials` enqueue in `ConnectAsync` to
run *before* the `IsConnected` liveness check, instead of after it — the
narrowest change that makes "already connected" CONNECTs still get recorded
while leaving the gate and the refusal/success paths otherwise untouched.
Filtered to the contract test file:

```
[xUnit.net]     SmartSentinelEye.ScenarioSimulator.Tests.FakeMqttClientCredentialContractTests.A_connect_refused_because_the_client_is_already_connected_records_nothing [FAIL]
  Error Message:
   Shouldly.ShouldAssertException : client.PresentedCredentials
    should be
["first"]
    but was (case sensitive comparison)
["first", "second"]
    difference
["first", *"second"*]

Additional Info:
    a CONNECT ThrowIfConnected refuses never reaches the point where a real client reads the password to build the packet, so nothing about "second" belongs in the history — only what the first, successful CONNECT actually presented.
Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3, Duration: 281 ms - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
```

Full-assembly run under the same patch, to confirm this is the *only*
failure in the whole suite (contrast, the way CF4's isolation was shown):

```
Failed!  - Failed:     1, Passed:    70, Skipped:     0, Total:    71, Duration: 6 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
```

Exactly 1 of 71 failed, and it is the target test — `ConnectAttempts` stayed
at 1 (the increment was not moved), so the sibling assertion in the same
test kept passing and no other test anywhere in the assembly was touched.
Reverted (`git checkout -- tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs`);
`git diff --exit-code -- src/ tests/ScenarioSimulator.Tests/Fakes/` clean.

---

## 5. Final state

Full clean run after every counterfactual reverted (rebuilt via `dotnet
clean` + `dotnet test`, so this is not reusing a cached binary and a
restored file's unchanged mtime cannot mask a missed revert):

```
Passed!  - Failed:     0, Passed:    71, Skipped:     0, Total:    71, Duration: 9 s - SmartSentinelEye.ScenarioSimulator.Tests.dll (net10.0)
```

Same 71 as §4's first run.

`git status --porcelain` at the end of phase 4:

```
?? tests/ScenarioSimulator.Tests/FakeMqttClientCredentialContractTests.cs
?? tests/ScenarioSimulator.Tests/MqttPublisherCredentialTests.cs
```

Both are now committed (`8b9d8116` fake change, `f1074d28` new tests). Final
diff against `origin/develop`:

```
 specs/254-the-credential-the-fake-never-kept/plan.md                       | 219 +++++++++++++++++++++
 specs/254-the-credential-the-fake-never-kept/spec.md                       | 201 +++++++++++++++++++
 specs/254-the-credential-the-fake-never-kept/tasks.md                      | 181 +++++++++++++++++
 tests/ScenarioSimulator.Tests/FakeMqttClientCredentialContractTests.cs     | 178 +++++++++++++++++
 tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs                      |  37 +++-
 tests/ScenarioSimulator.Tests/MqttPublisherCredentialTests.cs              | 141 +++++++++++++
 6 files changed, 954 insertions(+), 3 deletions(-)
```

**Nothing under `src/` appears in the final diff.** Every `src/` edit made
during verification (CF1, CF2 in §2 and §4) was a transient counterfactual
patch, reverted and confirmed clean before the next step. No pre-existing
test file changed; both new test files are additions.

---

## 6. Note carried from `plan.md` §2

The fake, `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs`, is now
368 lines, further past ADR-0084's advisory 300-line limit. That limit is a
`warning`, carved out of `TreatWarningsAsErrors`, and the plan explicitly
directs against splitting a test double over it — recorded here rather than
acted on.

---

## 7. Definition of done — checked against `tasks.md`

1. Run A shows the whole existing suite green under CF1 and CF2 (§2); Run B
   shows the new publisher test red under both (§4). Both quoted verbatim
   above.
2. `ScenarioSimulator.Tests` is green (71/71); pre-existing files are
   byte-identical to `origin/develop` (only `Fakes/FakeMqttClient.cs` among
   pre-existing files appears in the diff, and that is the intended change).
   `EventIngestion.Infrastructure.Tests` is green and untouched (§3).
3. The diff contains exactly the fake, the two new test files, and this
   spec's `spec.md`/`plan.md`/`tasks.md`/`verification.md`. No file under
   `src/` survives into it (§5).
4. Commits follow Conventional Commits with no `Co-Authored-By` footer
   (ADR-0086); each commit builds on its own.
