# Spec 254 — The credential the fake never kept

**Issue:** #2450 (`agent:ready`, Project #13)
**Branch:** `test/2450-mqtt-credential-history`
**Lane:** autonomous (ADR-0144)
**Found by:** spec 179 (#2233), audit row **G3** — `specs/179-a-fake-that-refuses-a-live-reconnect/spec.md` §2, `tasks.md` T011
**ADRs:** ADR-0111 (the Scenario Simulator, dev-only), ADR-0036 (smallest change,
no speculative generality), ADR-0052 / ADR-0054 (hand-written fakes and test
data), ADR-0053 (test naming), ADR-0084 (code metrics, advisory), ADR-0139 +
constitution §Testing (new behaviour starts red), ADR-0150 (wait for a
condition, not a count), ADR-0037 / ADR-0144 (phases, lane).
**No new ADR.** §3 gives the reasoning.

---

## 1. Premise check (done before planning)

Each claim in the issue was checked against the files instead of being taken on trust.

| Claim | Verified | Where |
|---|---|---|
| `MqttPublisher` mints per attempt, and `TokenCredentials` reads a live `TokenHolder` at CONNECT time | yes | `src/ScenarioSimulator/Mqtt/MqttPublisher.cs` — `ConnectAsync` sets `token.Value = await tokens.GetAccessTokenAsync(…)` before every `client.ConnectAsync`; `TokenCredentials.GetPassword` returns `token.Value` |
| The simulator's fake keeps no credential history | yes | `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs` — it keeps only `Options` (the **latest** options object). Calling `Options.Credentials.GetPassword(Options)` returns the token that is live *now*, not the token that was presented at the time |
| EventIngestion's fake does have one | yes | `tests/EventIngestion.Infrastructure.Tests/Fakes/FakeMqttClient.cs` — `PresentedCredentials` (a `ConcurrentQueue<string>` snapshot), enqueued in `ConnectAsync` just before `ConnectAttempts` is incremented |
| That is what makes #2038 testable there | yes | `MqttConnectionLoopTests.Each_attempt_presents_a_freshly_minted_credential` asserts `["token-1","token-2","token-3"]` over two refusals and a success |
| No production signature change needed | **yes** | The fake receives `MqttClientOptions` on every `ConnectAsync`, and `options.Credentials` is the publisher's own `TokenCredentials`. The fake can read the password at the same moment the real client reads it to build the CONNECT packet. No new seam is needed. `MqttPublisher`'s internal constructor already takes `IMqttClient` and (since spec 250) `MqttBackoff?` |

**Something the issue does not say: the suite is blind twice over.** Recording
alone would not be enough. Every stub Keycloak in the simulator's suite
(`MqttPublisherDropAccountingTests`, `MqttPublisherBackoffTests`,
`MqttPublisherProtocolPinTests`) answers a constant `"a-token"` with
`expires_in: 300`, and `ClientCredentialsTokenProvider` caches it until 80 % of
that lifetime has passed. So every attempt presents `"a-token"` whether the
publisher mints fresh tokens or reuses one. A recording fake would record
`["a-token","a-token","a-token"]` either way. The new publisher test therefore
needs its own **numbered, uncached** token source, which is EventIngestion's
`CountingKeycloak` pattern (`token-{n}`, `expires_in: 0`), in addition to the
fake's history.

**No existing simulator test reads the credential at all.** Nothing in
`tests/ScenarioSimulator.Tests/` calls `GetPassword` or `Credentials`. The only
`Options` read is `MqttPublisherProtocolPinTests`, and it asserts the protocol
version only.

**No overlap with parallel work.** Spec 250 (#2449, G2) has merged and gave the
publisher its `MqttBackoff?` seam. This spec relies on that seam and does not
change it. EventIngestion's fake is the reference here: it is read, not edited.

---

## 2. What "done" means

The simulator's suite can observe #2038's property: **each CONNECT presents a
freshly minted credential.** Each new assertion is shown able to fail, and the
evidence runs in both directions:

1. **Before:** with #2038's shape reconstructed in `MqttPublisher`, the whole
   existing `ScenarioSimulator.Tests` assembly stays green. This is the blindness
   the issue describes, recorded as an observed run rather than argued.
2. **After:** under the same reconstructed defect, the new publisher test is red,
   and its message names the reused credential.
3. **Also after:** under correct production code, the new test is green.

---

## 3. Scope decision: why this needs no ADR

An ADR records a decision with architectural reach, such as a public surface, a
configuration or deployment surface, a cross-context contract, or a pattern the
codebase must follow from now on. This change has none of those:

- **No file under `src/` changes.** Every edit is to a test double and to new
  test files. The one `src/` edit in the whole procedure is the transient
  counterfactual patch, and it is reverted behind a gate.
- **No new pattern.** `PresentedCredentials` already exists, for exactly this
  purpose, in the twin fake. `MqttPublisher`'s doc comment says *"A change to one
  of these loops belongs in the other unless it is on that list"*. Credential
  freshness is not on that list, so this change closes a drift between the twins
  rather than opening one.
- **Neither fake gets merged into the other, and no MQTT type moves into
  `Shared.Kernel`.** Both are forbidden by spec 079 and ADR-0036.
- **The simulator is dev-only** (ADR-0111): it is outside every production path
  and the latency budget.

If any of the following had been found, this spec would have stopped and handed
the decision back: a production seam needed to observe the credential, a change
to `TokenCredentials` or `TokenHolder`, or a change to how
`ClientCredentialsTokenProvider` caches. None was found.

---

## 4. User stories

### US1 (P1) — The simulator's suite can see a stale credential reused across reconnects

**As** whoever next changes either MQTT connection loop,
**I want** the simulator's suite to fail when `MqttPublisher` presents a credential it did not just mint,
**so that** #2038's stale-credential shape is guarded in both twins, not just in EventIngestion's.

This is a single slice. It ships alone, it can be observed in a unit run, and nothing depends on it.

#### Acceptance scenarios

```gherkin
Scenario: each attempt presents a freshly minted credential (happy path)
  Given the publisher runs against a Keycloak that mints token-1, token-2, … with no caching
    And a brisk injected backoff
    And the fake refuses the first two CONNECTs
  When the third CONNECT is answered
  Then the fake recorded the presented credentials ["token-1", "token-2", "token-3"], in order
```

```gherkin
Scenario: a reused credential is visible, not merely uncounted (conflict case: #2038's shape)
  Given MqttPublisher mints only when its token slot is empty (reconstructed #2038)
  When the same three attempts run
  Then the fake recorded ["token-1", "token-1", "token-1"]
    And the happy-path assertion fails, naming the reused credential
```

```gherkin
Scenario: the fake records what was presented at CONNECT time, not what is live when read (fake contract)
  Given client options whose credential provider reads a mutable slot holding "first"
  When one CONNECT is refused, the slot is set to "second", and a second CONNECT is sent
  Then PresentedCredentials is ["first", "second"]
```

```gherkin
Scenario: a CONNECT the client refuses before sending records nothing (bad-request case)
  Given the fake is already connected
  When ConnectAsync is called again and throws "It is not allowed to connect with a server after the connection is established."
  Then PresentedCredentials is unchanged
    And ConnectAttempts is unchanged
```

```gherkin
Scenario: a held CONNECT records the credential read when the packet was built (fake contract, gate)
  Given the fake gates connects, and the credential slot holds "before"
  When a CONNECT starts and its credential has been read, the slot is set to "after", and the gate is released
  Then PresentedCredentials is ["before"]
```

**Auth:** this is not an HTTP or scope change. The credential in question *is* the
MQTT auth token (ADR-0100 go-auth: username `scenario-simulator`, password = JWT),
but nothing about how it is minted, scoped or validated changes. Only what the
test double records changes.

---

## 5. Independent end-to-end test procedure

No Aspire stack, Docker or broker is involved: this is plain xUnit against hand-written fakes.

**A. The blindness, observed (before the fake change).**
Apply counterfactual CF1 (`plan.md` §5) to `MqttPublisher.cs`, then run
`dotnet test tests/ScenarioSimulator.Tests`. Every test is green, which is the
finding. Revert, then check `git diff --exit-code -- src/`. Repeat the same steps for CF2.

**B. The capability (after).**
Run `dotnet test tests/ScenarioSimulator.Tests`. All tests are green, including
the new ones.

**C. The new assertions can fail.**
Apply CF1 to CF4 one at a time. Each turns its named test red, and the verbatim
failure is quoted. Revert after each and check that
`git diff --exit-code -- src/ tests/ScenarioSimulator.Tests/Fakes/` is clean.

**D. The control.**
Run `dotnet test tests/EventIngestion.Infrastructure.Tests`. It is green, and
none of its files appear in the diff.

The pass condition is the contrast between A and C, with both outputs written in
`verification.md`.

---

## 6. Locked tech choices and latency

Nothing new is introduced. The tools are xUnit, Shouldly and hand-written fakes
(ADR-0052, ADR-0054), with sentence-style test names (ADR-0053). Waits poll a
condition against a deadline (ADR-0150). The history uses a
`ConcurrentQueue<string>` with a snapshot property, which is EventIngestion's
shape after its thread-safety fix.

**Latency-budget impact: N/A.** The Scenario Simulator is dev-only (ADR-0111) and
sits on no leg of constitution §IV. No production code changes.

---

## 7. Out of scope (stated, not dropped)

- **Any production change to `MqttPublisher`**, including its mint-failure path.
  When Keycloak is unreachable, that path deliberately presents the credential
  already in the slot. That reuse is intended behaviour, not #2038, and the new
  test uses a Keycloak that always answers.
- **Changing the existing stub Keycloaks** in the three existing publisher test
  files. Their constant, cached token is correct for what those tests assert.
  The new test brings its own counting stub.
- **Reordering the simulator fake's `Options = options` assignment** to match
  EventIngestion's. That difference is harmless and unrelated to this issue.
- **Merging the two fakes**: forbidden (spec 079, ADR-0036).
