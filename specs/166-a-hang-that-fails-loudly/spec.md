# Spec 166 — a hang that fails loudly

**Phase:** 1 (Specify) — ADR-0037
**Issue:** [#2409](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2409) · **Branch:** `fix/2409-a-hang-that-fails-loudly`
**Lane:** autonomous (ADR-0144) — `#2409` carries `agent:ready`. **Engineer:** `infra-engineer` · **Reviewer:** `infra-reviewer`
**Base:** `origin/develop`, cut fresh — **not stacked**.
**ADRs:** ADR-0037 (phases), ADR-0144 (the lane; phase 4a's two colours),
ADR-0139 (rules that fail the build — checked and found **not to apply**, §4),
ADR-0065 (coverage gates — `scripts/coverage-check.ps1` is touched), ADR-0103
(AspireFixture, no Testcontainers — checked; this assembly uses neither),
ADR-0109 (contention files / `[P]` marking).
**Constitution:** §IV — **N/A**, no production runtime file is touched, nothing
on the event-to-overlay path. §Testing — engaged; see §5.
**New ADR required: no.** §4 argues it.

---

## 1. The defect, as measured from both logs

Confirmed directly from the two downloaded job logs (not taken from the issue on faith):

| Run | Branch | Hang begins | Job killed | Silent span |
|---|---|---|---|---|
| [35067663094](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35067663094) | `develop` | 07:20:11.617 | 07:36:22.317 | 16m 11s |
| [35087013185](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35087013185) | PR #2408 | 10:53:50.892 | 11:09:41.195 | 15m 50s |

Both logs show the identical shape. `scripts/coverage-check.ps1` runs one test
project per `dotnet test` invocation, in a loop, each writing
`  -> <ProjectName>` followed by that project's `A total of 1 test files
matched the specified pattern.` line. In both runs this line appears ~26
times — once per test project — the last one for
`SmartSentinelEye.StreamDistribution.Infrastructure.Tests`, and then **nothing
else is ever written**: no `Passed!`, no `Failed!`, no exception, until
`##[error]The operation was canceled.` at the job's `timeout-minutes: 20`
wall. Both teardowns report four orphaned `dotnet` processes
(`Terminate orphan process: pid (…) (dotnet)` × 4) — the host was alive and
stuck, not crashed.

Normal duration for this whole job (build + Docker-free fixture step +
26-project coverage loop) is 4–5 minutes; per-project timing in the
successful portion of both logs ranges roughly 3–14 seconds. A 15+ minute
silence from one project is not slow, it is stuck.

**Invisible at three layers**, exactly as the issue states, each confirmed
against this repo's own files rather than asserted:

1. `timeout-minutes` expiry is `##[error]The operation was canceled.` →
   GitHub reports the job as **`cancelled`**, not `failure`. `ci.yml:283`'s
   comment already names this equivalence from the e2e job's own prior
   incident (#2137).
2. `integration` and `e2e` both declare `needs: [backend]` (`ci.yml:127`,
   `ci.yml:172`) — a cancelled `backend` makes both report **`skipped`**.
3. `gh pr checks --watch` exits `0` on a run containing only `cancelled` and
   `skipped` buckets (memory: *a finished watcher is not a green run*) — an
   automated merge-on-green would sail through.

The coverage gate compounds it: `ci.yml`'s "Upload coverage report" step
targets `artifacts/coverage/report`, which `coverage-check.ps1` only
populates after every project in the loop has returned — a hung run leaves
it empty, and the upload step (`if: always()`) prints
`##[warning]No files were found …` with no job-level failure attached to that
warning.

---

## 2. What narrows it — read, not guessed

The issue asks whether the cause is findable by reading before reaching for
an instrument. It is partially findable: the assembly's own tests were read
in full (12 classes, 18 files under `tests/StreamDistribution.Infrastructure.Tests/`,
excluding `obj/`), and compared against every other `*.Infrastructure.Tests`
assembly that ran earlier in the same loop without incident.

### 2.1 Every wait in this assembly but one is bounded

```
$ grep -rn "Task\.Delay\|SemaphoreSlim\|\.Result\b\|Task\.Wait(\|ManualResetEvent\|CancellationTokenSource(" \
    tests/StreamDistribution.Infrastructure.Tests --include=*.cs
```

Two files use `Task.Delay` in a wait loop, and both are self-bounded by a
wall-clock deadline, not by anything asynchronous completing:

- `WhepValidatorRotationTests.cs:205,211` — `deadline =
  DateTime.UtcNow.Add(RefreshLandingWindow)`, polled every 25ms
  (`PollInterval`), loop condition `!subject.IsSuccess && DateTime.UtcNow <
  deadline`.
- `WhepValidatorRefreshRestraintTests.cs:189,193` — the identical shape
  against `RefetchWindow`.

Neither can hang past its window. **One wait in the entire assembly has no
such bound:**

`WhepValidatorUnreachableRealmTests.cs:260–275`, `CancellingRealm.GetDocumentAsync`:

```csharp
private sealed class CancellingRealm : IDocumentRetriever
{
    public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancel);
        }
        catch (Exception exception)
        {
            throw new IOException($"IDX20804: Unable to retrieve document from: '{address}'.", exception);
        }

        return DiscoveryDocument;
    }
}
```

exercised by exactly one test, `A_cancelled_request_stays_cancelled`
(line 104–112):

```csharp
WhepAuthValidator validator = ValidatorOver(new CancellingRealm());
using CancellationTokenSource cancelled = new(TimeSpan.FromMilliseconds(50));

await Should.ThrowAsync<OperationCanceledException>(
    () => validator.ValidateAsync(AToken(), cancelled.Token));
```

`Timeout.InfiniteTimeSpan` means this `Task.Delay` **only** ever completes
by `cancel` firing. `cancel` is the token handed in by
`Microsoft.IdentityModel.Protocols.ConfigurationManager<OpenIdConnectConfiguration>.GetConfigurationAsync`,
via its `IConfigurationRetriever` and down into this `IDocumentRetriever` —
third-party code this repo does not control and whose exact cancellation
forwarding through the retry/lock path an xUnit test cannot see into. If, on
some interleaving, that token does not reach this frame — a stale linked
token, a lock-acquisition path that swallows it, a version-specific
forwarding gap — this `await` never returns, this test never completes, and
`dotnet test` for the whole assembly never emits its next line. That is
exactly the observed symptom: not a crash, not a slow pass, a permanent
silence after "test files matched."

### 2.2 This is a lead, not a finding

This is **the only unbounded, externally-cancellation-dependent wait** in an
assembly of 12 classes and ~60 tests — everything else is fakes and stubs
returning completed or pre-faulted `Task`s synchronously, with no timers, no
semaphores, no real I/O. That singularity is worth recording. It is *not*
being reported as the confirmed cause, for a reason stated plainly: **both
occurrences produced zero output between "test files matched" and the job
kill** — no stack, no exception, nothing identifying which of the ~60 tests
in the assembly was even running. `xUnit` does not report a test's *start*,
only its *completion* (`Passed`/`Failed`/`Skipped`) by default, so a hang on
the very first test looks identical in the log to a hang on the last.
Asserting this line is the cause without that evidence would be exactly the
kind of unchecked record this repository has already corrected twice (§IV's
leg table, the Phase 3 board gate).

**Secondary, weaker factor, noted for completeness and explicitly not acted
on:** no test class in this assembly (or anywhere in the repo — checked
repo-wide) opts out of xUnit's default parallel-by-collection execution
(`grep -r "DisableParallelization\|CollectionBehavior" tests` — zero hits).
12 classes could run concurrently on a 2-core GitHub-hosted runner. This
could sharpen a hang (thread-pool contention delaying whatever the true
blocking condition is) without being *the* blocking condition itself, and
is not a lead worth a design decision on the strength of two occurrences.

### 2.3 Conclusion: diagnostic first, and I agree with the issue's own reading

**A fix would be a guess dressed as a fix.** §2.1's lead is real but
unconfirmed; changing it (e.g., bounding the delay, or switching the test to
assert on the unwrapped-cancellation path directly) is not justified by
evidence strong enough to call it "the" defect, and ADR-0144 forbids
weakening a gate to reach green — a plausible-looking edit to the one test
that happens to be the only candidate, made without ever having seen the
hang's own stack, is exactly the kind of change that would look like
progress and prove nothing. **The honest first slice is the instrument**:
convert the invisible cancellation into a build failure that names the hung
test and dumps its threads, so the *next* occurrence (there have been two in
one day; a third is not a remote possibility) answers root cause with
evidence instead of requiring a fourth.

---

## 3. The instrument

### 3.1 What it is

`dotnet test`'s built-in vstest "blame" hang detector: `--blame-hang`
(enable), `--blame-hang-dump-type <type>` (dump format on hang — `mini` is
sufficient to name the hung thread's stack and is fast to write; `full` is
unnecessary weight for this), `--blame-hang-timeout <duration>` (inactivity
window before vstest kills the test host and writes the dump). This ships in
the SDK already pinned by `global.json` (10.0.401) — **no new package, no new
dependency**, only new CLI flags on an invocation this repo already makes.

### 3.2 Where it goes

Exactly the two `dotnet test` call sites in the `backend` job that run this
assembly (and every other assembly) without any watchdog today:

- `scripts/coverage-check.ps1`, the `foreach ($proj in $testProjects)` loop
  (currently building `$testArgs` around line 84) — the loop the hang
  actually occurred in, covering all ~26 non-Integration test projects.
- `.github/workflows/ci.yml`'s **"Docker-free fixture logic tests"** step —
  the other unguarded `dotnet test` invocation in the same job, against
  `Integration.Tests` filtered to `Category=FixtureLogic`. Same job, same
  risk shape (silent hang → `cancelled` → `skipped` downstream), same
  one-line fix; leaving it out would leave the identical hole open one step
  earlier in the same job.

**Explicitly out of scope**, and why: the `integration` job's `dotnet test`
(Docker-bound fixtures against real containers, 30-minute budget) and the
`e2e` job (Playwright, which already has its own per-test `timeout: 60_000`
in `playwright.config.ts` — the frontend's existing equivalent of this
instrument, confirmed present — plus its own tracked hang-hardening work,
#2376/#2382, sizing `globalTimeout` against a distribution that still
contains a *different*, already-known 15.5-minute teardown hang). Both have
a materially different "how slow is legitimately slow" question — a
Docker-bound fixture waiting on a real container start is not the same
population as this assembly's fakes-only unit tests — and folding them in
here would widen a diagnostic slice into a job-wide retuning exercise with
no reproduction to size it against. Recorded as follow-up, not silently
dropped.

### 3.3 Cost, honestly

**What happens to a legitimately slow test:** if a single project's *entire*
`dotnet test` run produces no vstest activity for the timeout window, it is
killed and reported as hung — indistinguishably from a real hang, from the
gate's point of view. The budget must sit comfortably above the slowest
normal per-project run. From both logs' successful portions, per-project gaps
run 3–14 seconds; the whole 26-project loop completes in under two minutes.
A budget of **3 minutes** (`--blame-hang-timeout 3min`) is proposed as the
default — over 12× the slowest observed normal gap, while still failing
**well** inside the 20-minute job ceiling even in the worst realistic case
(`coverage-check.ps1` throws on the first non-zero exit code, §`if
($exitCode -ne 0) { throw … }` — one hung project stops the loop, it does not
let 26 of them each burn 3 minutes). This number is a proposal for phase 4 to
verify against a fresh measurement (memory: *measurement runs need
repeating* — run the loop's timing twice) and to record, at the call site,
why it was chosen — the same convention this repo already applies to retry
opt-ins (`RetryEveryMethod()` "and says why at the call site").

**What this cannot do:** confirm or refute §2.1's lead on its own. It only
guarantees that *whichever* test hangs next, CI says so within minutes
instead of twenty, and names it.

---

## 4. Does this need an ADR? No.

The brief raises this explicitly, naming ADR-0139 ("rules that fail the
build, not the review") as the candidate authority, and naming #2409's own
sibling #2404 as a live precedent for the block gate. Both are checked
directly rather than assumed.

**ADR-0139 governs a different thing.** Its three subjects — `Ensure.That`
guard enforcement, the primitive-boundary ban, the red-first/green-throughout
testing split — are each a **rule about what application code and tests must
look like**, previously advisory, made mechanical. This change creates no
such rule. It does not ban an idiom, does not constrain how a test is
written, does not change what "passing" means for any test that currently
passes (§3.3's budget is sized to guarantee that). It changes **only** what
happens when a test assembly stops responding entirely — an operational
question about the CI harness's own reliability, not a governance question
about the code it runs.

**#2404 is not the same shape.** #2404 was blocked because answering it
required *choosing among infrastructure topologies* with a real trade-off
against a stated NFR (constitution §Availability's zero-downtime rolling
update vs. a single-replica assumption) and against §IV's latency budget (a
broker-hop candidate). This change trades off nothing against any NFR or
budget — it has no runtime-file footprint at all (§ latency line above) —
and chooses among no competing designs; there is exactly one built-in
mechanism for this (§3.1), not several candidates to adjudicate.

**Direct precedent, unADR'd:** every prior CI-reliability change of this
shape shipped as a plain commit, not an ADR —
`37011568` ("ci(e2e): raise the e2e job timeout so a hanging tail can
finish", #2376), `709d56c8` ("ci(128): the AppHost log survives a cancelled
e2e job", #2137), `12211126` ("feat(ci): a retried e2e pass says so", #2077).
Each of those changed how a CI job behaves under a failure condition,
uniformly, with no ADR. Spec 162 §5.3 independently drew this same line for
a frontend guard, against the same candidate authority (ADR-0139), using the
same test — *does this assert an existing gate actually covers what it
claims, or does it create a new cross-cutting prohibition* — and concluded
no ADR there for the same reason it holds here.

---

## 5. Testing (§Testing / ADR-0144 phase 4a) — behaviour-changing, and the evidence problem

**BEHAVIOUR-CHANGING.** The CI job's own behaviour on a hang changes: today,
silent → `cancelled` at 20 minutes; after this change, a named `dotnet test`
failure with a thread dump inside the configured budget. That is new
behaviour of the harness, and CLAUDE.md's rule is that ambiguity resolves to
red rather than being waved through as "just config." Declaring it
behaviour-preserving (and therefore characterisation-only) would let the
mechanism ship unobserved, which is the one thing this issue exists to stop
happening again.

**The standard red-first recipe does not fit directly, and the issue already
says why: this is a heisenbug.** It reproduced twice in one day and cannot be
summoned on demand, so "observe the real hang go red, then watch the fix turn
it green" is unavailable — there is no fix in this slice to turn it green,
only an instrument.

**What phase 4a must produce instead, and I agree this is the right
evidence (the issue's own proposal, adopted rather than restated):**

1. **Before:** add one deliberately-hanging `[Fact]` to a disposable location
   (a scratch test file, or a temporary addition to
   `StreamDistribution.Infrastructure.Tests` itself — engineer's call,
   documented either way) — e.g. `await Task.Delay(Timeout.InfiniteTimeSpan,
   CancellationToken.None);` — and run the **current**, unflagged `dotnet
   test` against it. Observe and quote that it does not return in a
   reasonable window (kill it manually after, say, 30 seconds — do not wait
   out a real timeout). This is the red: today's actual, current behaviour on
   a hang is silence, demonstrated on demand rather than argued from the two
   incident logs alone.
2. **After:** add the `--blame-hang*` flags, re-run the same deliberately-
   hanging test. Observe and quote: the run fails inside the configured
   budget, the console names the hung test, and a hang dump file is written.
   This is the new behaviour, proven.
3. **Remove the deliberately-hanging test.** It cannot be committed — a
   permanently-hanging `[Fact]` would fail every CI run forever, which is a
   new defect, not evidence of a fixed one. `git status` must be clean of it
   in the merged diff; only the `ci.yml` / `coverage-check.ps1` flag changes
   land.
4. **Quote both outputs verbatim in the PR body** — the "before" silence
   (or the manual-kill transcript) and the "after" failure-with-dump-and-name
   — since this is "the only form of the evidence a later reader can check"
   (CLAUDE.md, phase 4 quoting rule), and no CI run in this repo's history
   can retroactively prove a test was observed red before the code existed.

**What must NOT happen:** treating a green `coverage-check.ps1` run (i.e.,
no hang occurring during phase 4/5 verification) as evidence the instrument
works. Absence of a hang during verification proves nothing about whether
the watchdog would catch one — the counterfactual has to be constructed, per
this repo's own standing rule (memory: *prove a guard by counterfactual*).

**What ships:** the two flag changes (§3.2). No application code changes.
No test in the shipped suite changes shape, assertion, or count —
`A_cancelled_request_stays_cancelled` and every other existing test are
untouched; §2.1's lead is not acted on in this slice.

---

## 6. Non-goals

- **Not a fix for the hang.** §2.3.
- **Not a retune of `integration` or `e2e`.** §3.2.
- **Not a change to any test's assertions or the coverage thresholds.**
  `coverage-check.ps1`'s `$thresholds` table and every gated percentage are
  untouched.
- **Not xUnit's `[Fact(Timeout = …)]`.** Considered and rejected: xUnit v2's
  per-test `Timeout` does not abort the underlying async operation — it
  marks the test failed on a *separate* watchdog thread while the original
  thread (and whatever it is blocked on) keeps running, which would leave
  the same orphaned-process signature this issue already observed and would
  not produce a dump naming the stuck frame. `--blame-hang` operates at the
  test **host process** level and kills it, which is the shape this
  failure needs.

---

## 7. Acceptance

- `ci.yml` and `coverage-check.ps1` diffs are reviewed by `infra-reviewer`
  against §3.2/§3.3 (right call sites, a documented and justified timeout
  value).
- The PR body quotes the before/after counterfactual from §5.
- A normal `coverage-check.ps1 -NoBuild` run (no injected hang) still passes
  end to end with the new flags present — this is verification that the
  flags do not themselves break anything, not the red/green evidence itself.
- No ADR is added; no existing test is edited, skipped, renamed, or has its
  assertions relaxed (ADR-0144's three forbidden outcomes, checked against
  this slice: none apply).
