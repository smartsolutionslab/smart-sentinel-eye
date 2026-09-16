# Spec 170 — a cancellation that cannot be honoured

**Phase:** 1 (Specify) — ADR-0037
**Issue:** [#2418](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2418) · **Branch:** `fix/2418-a-cancellation-that-cannot-be-honoured`
**Lane:** autonomous (ADR-0144) — #2418 carries `agent:ready`, Project #13 status **Todo**, no `agent:blocked`.
**Engineer:** `backend-engineer` · **Reviewer:** `backend-reviewer`
**Base:** `origin/develop`, cut fresh — **not stacked**.
**ADRs:** ADR-0037 (phases), ADR-0144 (the lane; phase 4a's two colours),
ADR-0052 (xUnit + Shouldly + hand-written fakes — the fakes in question),
ADR-0053 (sentence-style test naming — three test names change),
ADR-0049 (`CancellationToken` last parameter — the contract this spec
re-reads), ADR-0103 (AspireFixture, no Testcontainers — checked; this
assembly uses neither), ADR-0084 (code metrics — checked, §6),
ADR-0109 (`[P]` markers / contention files),
ADR-0139 (rules that fail the build — checked and found **not to apply**, §6).
**Constitution:** §IV — **N/A**. No production runtime behaviour changes;
nothing on the event-to-overlay path. §Testing — engaged, see §7.
**New ADR required: no.** §6 argues it.

---

## 1. What is broken

`tests/StreamDistribution.Infrastructure.Tests/Auth/WhepValidatorUnreachableRealmTests.cs:105-112`,
`A_cancelled_request_stays_cancelled`, hangs the backend test host. Five
occurrences in ten observed backend-bucket runs on 2026-09-16 (issue #2418's
own tally) — 32 cases pass in six seconds, then the host sits inactive for
three minutes and `--blame-hang` kills it, naming this test. It is now failing
pull requests, not only `develop`, and each re-run destroys the failing log.

```csharp
[Fact]
public async Task A_cancelled_request_stays_cancelled()
{
    WhepAuthValidator validator = ValidatorOver(new CancellingRealm());
    using CancellationTokenSource cancelled = new(TimeSpan.FromMilliseconds(50));

    await Should.ThrowAsync<OperationCanceledException>(
        () => validator.ValidateAsync(AToken(), cancelled.Token));
}
```

```csharp
private sealed class CancellingRealm : IDocumentRetriever
{
    public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancel); }
        catch (Exception exception) { throw new IOException($"IDX20804: …", exception); }
        return DiscoveryDocument;
    }
}
```

## 2. The mechanism — read from the library's own source, at the installed version

`Microsoft.IdentityModel.Protocols` **8.19.2** (confirmed from the built
output: `ProductVersion 8.19.2+25d90ed3f48854036d444541a049089ccd198707`).
Source read at tag `8.19.2`,
`src/Microsoft.IdentityModel.Protocols/Configuration/ConfigurationManager.cs`
and `ConfigurationManager_Blocking.cs`. Every line below was read, not
recalled.

`ConfigurationManager<T>.GetConfigurationAsync(CancellationToken cancel)`
consults the caller's token in **exactly one place**, and it is not the fetch:

| # | Site | Uses `cancel`? |
|---|---|---|
| 1 | Cache hit (`_currentConfiguration != null && _syncAfter > now`) — returns immediately | **no**, returns before reading it |
| 2 | `await _configurationNullLock.WaitAsync(cancel)` (non-blocking path, `:227`) | **yes** — the only one |
| 2′ | `await _refreshLock.WaitAsync(cancel)` (blocking path, `_Blocking.cs:27`) | **yes** — same shape |
| 3 | `_configRetriever.GetConfigurationAsync(MetadataAddress, _docRetriever, CancellationToken.None)` (`:262`, `_Blocking.cs:61`) | **no**, hard-coded `None` |
| 4 | Background refresh `Task.Run(…, CancellationToken.None)` (`:319`, `:500`) | **no** |

Site 3 carries the library's own comment:

> `// Don't use the individual CT here, this is a shared operation that shouldn't be affected by an individual's cancellation.`
> `// The transport should have its own timeouts, etc.`

So `CancellingRealm` receives `CancellationToken.None`. Its
`Task.Delay(Timeout.InfiniteTimeSpan, cancel)` therefore has **no escape at
all** — not a slow one, not a cancellable one. The retriever never returns.

**Why the test nevertheless "passes" about half the time.** `SemaphoreSlim.WaitAsync(cancellationToken)`
checks `IsCancellationRequested` *before* it tries to take the count, so with
a free semaphore it still throws `OperationCanceledException` if the token is
**already** cancelled. The 50 ms timer starts at the `using` line; `AToken()`
— RSA-2048 signing plus `JwtSecurityTokenHandler.WriteToken` — is evaluated
*inside* the `Should.ThrowAsync` lambda, i.e. after it. So the outcome is a
footrace between that timer and token minting:

- token minting takes **> 50 ms** → the token is already cancelled at site 2 →
  `OperationCanceledException` → the test passes;
- token minting takes **< 50 ms** → site 2 admits it → site 3 → the
  unescapable delay → **the host never exits**.

Named hang: testhost's `Main` blocks on `WaitOne()` inside the VSTest runner
waiting for xUnit's assembly-runner completion signal, which is suspended
forever on the abandoned delay. Nothing times it out, because the only timeout
in play was the caller's token and site 3 discarded it.

## 3. Premise check — performed, and it did not go as the issue predicts

Ordered by what it changes for the plan.

**3a. The code is unchanged.** `A_cancelled_request_stays_cancelled` reads
exactly as `:105-112` above; `CancellingRealm` still does
`Task.Delay(Timeout.InfiniteTimeSpan, cancel)` at `:266`. Confirmed on
`origin/develop` at branch point.

**3b. The prescribed local repro does not hang on this machine — 12 clean runs.**

| Shape | Runs | Hangs |
|---|---|---|
| `--filter …A_cancelled_request_stays_cancelled`, `--blame-hang-timeout 1min` | 1 | 0 (passed, 566 ms) |
| whole assembly, `--blame-hang-timeout 1min` | 1 | 0 (34 passed, 6 s) |
| whole assembly, `--blame-hang-timeout 30sec`, tight loop | 10 | 0 (34 passed, 6 s each) |

This is **not** evidence the defect is gone. It is evidence the race resolves
one way here and the other way on CI, which §2 predicts and which is worth
stating plainly because it inverts the usual intuition: **the faster, less
loaded the machine, the more likely the hang**, because token minting finishes
inside the 50 ms window. This workstation is running the Aspire stack (pid
3312) and mints the first token in well over 50 ms every time.

**3c. The mechanism was therefore proved by counterfactual instead, and it is
deterministic.** One character-level edit — `TimeSpan.FromMilliseconds(50)` →
`TimeSpan.FromSeconds(30)`, i.e. widening the window so the timer can never
win — and the filtered single-test run hangs on the first attempt:

```
Data collector 'Blame' message: The specified inactivity time of 45 seconds has elapsed.
Collecting hang dumps from testhost and its child processes.
Test Run Aborted.
The active Test Run was aborted because the host process exited unexpectedly.
The test running when the crash occurred:
SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Auth.WhepValidatorUnreachableRealmTests.A_cancelled_request_stays_cancelled
```

Byte-identical signature to CI. The edit was reverted (`git checkout --`,
followed by `touch` — restoring a file keeps its old timestamp and MSBuild
skips the rebuild) and the tree confirmed clean.

**This is the repro the fix must be measured against**, not the one in the
issue: the timed form is a coin flip whose bias is the machine, so a green run
proves nothing and ten green runs prove nothing. The widened window removes
the coin and leaves the defect.

## 4. The second finding — the test asserts a guarantee that does not exist

`A_cancelled_request_stays_cancelled` claims that a caller can cancel out of a
realm's **first** metadata fetch. Per §2 site 3, `ConfigurationManager<T>`
does not offer that, by explicit design, on either of its two code paths. The
test has never demonstrated it; it demonstrated the *already-cancelled*
short-circuit at site 2 whenever it passed at all, and hung otherwise.

Three claims in the current source are falsified by the 8.19.2 source and must
not be carried forward:

1. **`CancellingRealm`'s doc comment** — "an `OperationCanceledException`
   thrown *straight* out of a retriever propagates unwrapped and needs no
   guard at all. It is the wrapping that creates the confusion." False at
   8.19.2: both paths wrap the retriever call in `catch (Exception ex)` and
   re-throw `InvalidOperationException` IDX20803, so a bare
   `OperationCanceledException` from a retriever is *also* converted. The
   wrapping the fake so carefully reproduces changes nothing.
2. **The same fake's reason to exist** — it is never reached in its documented
   role. Its `catch` block cannot run, because its delay cannot complete.
   Dead code that hangs the host.
3. **`WhepAuthValidator.ValidateAsync`'s catch comment** (`src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs`)
   — "a cancelled request surfaces here as `OperationCanceledException` —
   `ConfigurationManager` raises it even when the retriever wrapped the
   cancellation". Literally defensible, materially misleading: the
   `OperationCanceledException` comes from site 2 and only from site 2, and
   when the retriever wraps a cancellation the caller gets
   `IdentityProviderUnavailable`, not a cancellation. See US3.

**The production code is correct and does not change.** A metadata fetch that
cannot complete *is* an unavailable realm, and reporting it as
`IdentityProviderUnavailable` is the right answer — including for an
`HttpClient` timeout, the one way site 3 can be cancelled in a fab. The narrow
`catch (InvalidOperationException)` is still load-bearing, and still needs a
test aimed at the edit that would widen it.

**What is actually guaranteed**, and what the tests should say:

| Cancellation arrives… | Honoured? | Why |
|---|---|---|
| before the call | **yes** — `OperationCanceledException` | site 2's already-cancelled check |
| while queued behind *another* caller's first fetch | **yes** — `OperationCanceledException` | site 2's semaphore wait |
| while the first fetch is in flight, same caller | **no** — outcome is whatever the realm eventually said | site 3 holds `CancellationToken.None` |
| after a configuration is cached | **no** — returns instantly, token never read | site 1 |

## 5. User stories

### US1 — the backend bucket stops hanging (P1, independently shippable)

Closes #2418 on its own. Test-file-only.

**As** anyone whose PR draws the short straw, **I want** the backend bucket to
finish, **so that** a red bucket means a real failure and not a coin flip.

- Rewrite `A_cancelled_request_stays_cancelled` onto the guarantee that holds:
  a token cancelled **before** the call, against `UnreachableRealm`. No timer,
  no delay, no window to widen. The assertion discriminates properly —
  `UnreachableRealm` would otherwise produce `IdentityProviderUnavailable`, so
  the test still fails on the `catch (Exception)` edit it was written to catch.
- Replace `CancellingRealm`'s `Timeout.InfiniteTimeSpan` with a **bounded**
  delay that cannot hang the host whatever token it is handed, and rename it
  for what it now is.
- Add the boundary characterisation: a cancellation arriving *mid-fetch* does
  not abandon the first metadata read. This keeps the bounded fake in use and
  turns red if a future library version starts honouring the token — the early
  warning whose absence is why #2418 cost five CI runs.
- Correct the two false doc claims in the test file (§4.1, §4.2).

### US2 — the in-flight cancellation that is real (P2)

**As** the validator serving 250 kiosks through one singleton, **I want** the
one in-flight cancellation `ConfigurationManager` *does* honour covered, **so
that** a viewer queued behind another viewer's first metadata fetch leaves as
a cancellation and not as a refused viewer.

Site 2's semaphore wait. Two concurrent callers, the first gated inside the
retriever on a bounded gate, the second cancelled while it queues.

### US3 — the comment that describes the wrong mechanism (P3)

**As** the next person to read why this catch is narrow, **I want** the comment
to name site 2, **so that** the next edit is reasoned about the mechanism that
exists. Comment-only; the comment-stripped file must hash identically.

## 6. Gates checked

**New ADR: no.** Nothing architectural moves. The test stack (ADR-0052), test
naming (ADR-0053), `CancellationToken` contract (ADR-0049) and the narrow-catch
design (spec 119) are all unchanged; this is a corrected reading of a
third-party library's documented behaviour and a test that asserted the wrong
thing. **If phase 2 or 4 finds it cannot be done without changing
`ValidateAsync`'s control flow, stop and report — that would be ADR-shaped and
the lane may not write one (ADR-0144).**

**ADR-0084 / file length:** `Directory.Build.props:108` suppresses `S104`,
`S138`, `S107`, `S1541`, `S134` for test projects. The file is already 314
lines; growth is not a build break.

**ADR-0139:** checked — no build-failing rule is being weakened, no test
deleted, no analyzer narrowed, no threshold lowered. The `--blame-hang` gate
(#2409, spec 166) stays exactly as it is; this spec fixes what it caught.

**ADR-0103:** this assembly boots no Aspire fixture and no container. Nothing
here touches the fixture.

**Constitution §IV:** N/A. No production runtime file changes behaviour (US3
is comment-only), no leg moves, no latency claim made or discharged.

## 7. Acceptance scenarios (Gherkin)

```gherkin
Feature: a WHEP cancellation is reported as a cancellation, and never hangs the host

  # US1 — happy path, the guarantee that holds
  Scenario: a request cancelled before it reaches the realm stays cancelled
    Given a WhepAuthValidator over a realm whose metadata cannot be retrieved
    And a cancellation token that is already cancelled
    When ValidateAsync is called with that token
    Then an OperationCanceledException is thrown
    And no IdentityProviderUnavailable failure is returned
    And no warning is logged for a caller that never asked

  # US1 — the counterfactual this test exists to catch
  Scenario: widening the catch turns the cancellation into a refused viewer
    Given catch (InvalidOperationException) in ValidateAsync is widened to catch (Exception)
    When the scenario above is run
    Then it fails, naming the cancellation that was reported as a refusal

  # US1 — the boundary, characterised
  Scenario: a cancellation arriving mid-fetch does not abandon the first metadata read
    Given a WhepAuthValidator over a realm that answers after a bounded delay
    And a token cancelled while that first fetch is already in flight
    When ValidateAsync is called
    Then the call completes on the realm's answer
    And it does not hang
    # ConfigurationManager fetches with CancellationToken.None by design (8.19.2).
    # Red here means the library changed its mind — which is the warning we want.

  # US1 — the host-safety invariant, independent of every token
  Scenario: no fake in this file can outlive its bound
    Given any fake realm in WhepValidatorUnreachableRealmTests
    When it is handed CancellationToken.None
    Then it still completes within its own fixed bound

  # US2 — the in-flight cancellation that is real
  Scenario: a viewer queued behind another viewer's first fetch can still cancel
    Given one caller is inside the metadata retriever holding the configuration lock
    And a second caller enters with its own token
    When that second token is cancelled
    Then the second caller throws OperationCanceledException
    And the first caller is unaffected
    And the gate is released so neither call outlives the test

  # bad-request / auth: N/A — no HTTP surface, no scope, no trust boundary is
  # touched. /streams/authorize's AllowAnonymous posture is unchanged.
```

## 8. Independent end-to-end test procedure

Runnable by a reviewer who read none of the above. `-c Release` throughout —
the Aspire stack (pid 3312) holds the `Debug` binaries.

1. **Reproduce the defect deterministically (before the fix).** On
   `origin/develop`, change `TimeSpan.FromMilliseconds(50)` to
   `TimeSpan.FromSeconds(30)` at `WhepValidatorUnreachableRealmTests.cs:108`.
   ```sh
   dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release \
     --filter "FullyQualifiedName~WhepValidatorUnreachableRealmTests.A_cancelled_request_stays_cancelled" \
     --blame-hang --blame-hang-dump-type none --blame-hang-timeout 45sec
   ```
   Expect `Test Run Aborted`, naming this test. Revert, then `touch` the file.
2. **After the fix, confirm there is no window left to widen.** Grep the file
   for a `CancellationTokenSource` constructed with a delay — the rewritten
   US1 test must not have one. A defect that depends on a timing window is
   fixed by deleting the window, not by choosing a luckier number.
3. **Determinism, statistically.** 50 consecutive runs of the whole assembly
   with `--blame-hang --blame-hang-timeout 30sec`; zero hangs, zero failures.
4. **The assertion counterfactual.** Widen `catch (InvalidOperationException)`
   to `catch (Exception)` in `WhepAuthValidator.ValidateAsync`; the US1 test
   must fail with its own message. Revert; it must pass again.
5. **CI.** The backend bucket green on the PR — *necessary, not sufficient*
   (it was green in five of ten runs with the defect present). Steps 1-4 are
   the evidence; this is the confirmation.

## 9. Locked tech choices

xUnit + Shouldly, hand-written fakes, no Moq needed (ADR-0052, ADR-0054); no
Testcontainers, no Aspire fixture (ADR-0103); sentence-style test names
(ADR-0053); `Microsoft.IdentityModel.Protocols` stays at 8.19.2 — **no package
bump is proposed**, the behaviour is by design and a bump would be a different
slice with its own evidence.

## 10. Latency budget impact

**N/A.** No leg of the event-to-overlay path is touched. No production runtime
behaviour changes. `/streams/authorize` is a WHEP admission hook, not on the
event-to-overlay path; no figure is claimed or discharged here.

## 11. Out of scope

- Bumping or patching `Microsoft.IdentityModel.*`.
- Changing `ValidateAsync`'s control flow, its catches, or the
  `JwtSecurityTokenHandler` → `JsonWebTokenHandler` migration (spec 089 D4).
- `AppContextSwitches.UpdateConfigAsBlocking`. Both paths behave identically
  for everything this spec asserts (§2, sites 2/2′ and 3), so nothing here
  depends on the switch — but nothing here sets or tests it either.
- Any other `--blame-hang` finding in any other assembly.
