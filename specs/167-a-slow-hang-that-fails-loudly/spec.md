# Spec 167 — a slow hang that fails loudly

**Phase:** 1 (Specify) — ADR-0037
**Issue:** [#2410](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2410) · **Branch:** `fix/2410-an-integration-hang-that-fails-loudly`
**Lane:** autonomous (ADR-0144) — `#2410` currently carries `agent:ready` on
the board. **Flagged below (§0): the issue's own body recommends the
opposite** — read that before this enters phase 4.
**Engineer:** `infra-engineer` · **Reviewer:** `infra-reviewer`
**Base:** `origin/develop`, cut fresh — **not stacked**.
**Precedent:** spec 166 / PR #2411 (merged), the identical instrument for the
`backend` job. This spec follows its shape where the evidence agrees and
departs from it, explicitly, where measurement says it must.
**ADRs:** ADR-0037 (phases), ADR-0144 (the lane; phase 4a's two colours),
ADR-0139 (checked, not applicable — §4), ADR-0103 (AspireFixture, no
Testcontainers — this *is* that fixture, read in full — §2), ADR-0109
(contention file: `ci.yml`).
**Constitution:** §IV — N/A, no production runtime file touched. §Testing —
engaged, see §5.
**New ADR required: no.** §4.

---

## 0. A flag before anything else

`gh issue view 2410` shows the `agent:ready` label present today. The
issue's own body, written when #2409/#2411 filed it, says the opposite in
its last line: *"Not `agent:ready` — needs a human to size the budget and
confirm scope before it enters the autonomous lane."* Someone (or some
process) added the label after that was written; nothing in the issue or
its history retracts the sentence.

This spec was commissioned anyway, as phases 1–3 only, by the session
orchestrating this work — which is within scope regardless of the label
(architecture work is not gated the same way phase 4 is). But §3 below
concludes the budget question is genuinely harder than the sentence
implies, not easier, which makes the original caution look *more*
justified in hindsight, not less. **Recorded here so the human the issue
asked for sees this before phase 4 starts, not after.**

---

## 1. The defect, inherited from #2409/#2411's own words

`ci.yml`'s `integration` job (`needs: [backend]`, `timeout-minutes: 30`) runs
one `dotnet test` invocation with no watchdog. The three-layer invisibility
PR #2411 fixed for `backend` is structurally identical here:

1. A `timeout-minutes` expiry reports `cancelled`, not `failure`
   (`ci.yml`'s own comment at the e2e job, citing #2137, already names this
   equivalence).
2. `e2e` declares `needs: [backend, frontend]` — not `integration` — so
   `integration` hanging does **not** cascade a `skipped` onto `e2e` the way
   a `backend` hang cascades onto both `integration` and `e2e`. This is one
   real difference from #2409's framing, checked rather than assumed: a
   hung `integration` job reports `cancelled` **on its own account**, with
   `e2e` running independently in parallel (both are `needs: [backend]` /
   `needs: [backend, frontend]` siblings, not a chain). The visibility gap
   is one layer shallower here than #2409 described — still real, because
   `cancelled` alone already defeats `gh pr checks --watch` (memory: *a
   finished watcher is not a green run*), just not compounded by a second
   job going `skipped` underneath it.
3. `gh pr checks --watch` exits 0 on a `cancelled` bucket regardless.

`ci.yml` already carries a pointer comment at this exact step (added by
#2411, present today): *"No blame-hang watchdog here (unlike `backend`,
#2409) … Tracked as a follow-up: #2410."* This spec is that follow-up.

---

## 2. What is structurally different here — measured, not inferred

#2409/#2411's 3-minute number was sized against 29 fakes-only unit-test
projects with near-zero inter-test gaps (3–14s observed, whole loop 2m03s).
The brief for this issue warned explicitly against copying that number
across. It was right to warn: the real numbers are not a scaled-up version
of `backend`'s, they come from a different mechanism entirely.

### 2.1 Measurement method

Four independent green `develop` runs, chosen as the four most recent at
the time of writing (no cherry-picking — first four `gh run list
--status success` results with a downloaded `integration-test-results`
artifact):

| Run | Job wall time | Artifact |
|---|---|---|
| [35090574187](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35090574187) | 8m57s | 10444024628 |
| [35077532786](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35077532786) | 9m06s | 10439002421 |
| [35058230826](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35058230826) | 9m03s | 10431767815 |
| [35042726833](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35042726833) | 8m42s | 10426451498 |

Each run's `integration-test-results` artifact contains `integration.trx`,
which records a `startTime`/`endTime`/`duration` for every one of the 539
`UnitTestResult` entries. `--blame-hang-timeout` (per `coverage-check.ps1`'s
own now-verified comment, and vstest's docs) is a window that resets on
**every test-case completion across the whole host**, xUnit's own
parallel-by-collection execution included — so the quantity that matters is
not any single test's duration, it is **the largest gap between two
consecutive test-case completions, host-wide**, sorted by `endTime`. That
was computed for all four runs.

### 2.2 Finding 1: the fixture boot is a ~132-second dead zone, every run, by construction

`AspireFixture` (`tests/Integration.Tests/Fixtures/AspireFixture.cs`) is
registered `ICollectionFixture<AspireFixture>` via `AspireCollection`
(`[CollectionDefinition("Aspire")]`). 117 of the 142 test classes in the
assembly carry `[Collection(AspireCollection.Name)]`; 25 do not (utility/CLI
tests like `AppHostE2ESwitchTests`, `AuditObservability.RunModeDriverTests`
— the latter has a test literally named
`The_run_mode_test_takes_no_fixture_as_a_dependency`).

xUnit does not dispatch **any** test in a collection until that collection's
fixture's `InitializeAsync` returns. `InitializeAsync` here brings up the
entire AppHost via `DistributedApplicationTestingBuilder`: Postgres,
RabbitMQ, Keycloak (+ realm import), MediaMTX, and all nine context
services, gated behind eleven sequential `WaitForResourceAsync` calls plus a
migration-exit-code check, a Keycloak realm-readiness wait, a MediaMTX
wait, and a Kestrel-listener health probe. None of this shows up as any
`UnitTestResult`'s duration — fixture setup is invisible to the trx.

Measured directly, across all four runs, by finding the earliest `startTime`
among tests in classes confirmed (by source, not by name-guessing) to carry
`[Collection(AspireCollection.Name)]` (e.g. `ApiGateway.
GatewayRoutingIntegrationTests`), relative to the run's own first recorded
`startTime` (the non-gated tests, which start within ~2s of the step
beginning):

| Run | First gated-collection test completes at (offset) |
|---|---|
| 35090574187 | 132.0s |
| 35077532786 | 132.0s |
| 35058230826 | 133.7s* |
| 35042726833 | 120.2s* |

(*35058230826/35042726833 rows use the host-wide max-gap figure, §2.3 below,
which coincides with this same boundary in all four runs — the gap that
immediately precedes the first `ApiGateway.GatewayRoutingIntegrationTests`
completion, every time.)

**This is not noise.** It is the same two test names bracketing the same
gap in all four independently-sampled runs:
`Fixtures.OverlaySnapshotReadinessTests.…` (last non-gated-adjacent
completion) → **120–134 second silence** → `ApiGateway.
GatewayRoutingIntegrationTests.Gateway_forwards_the_health_route_to_each_
context_service(context: "automation")` (first arrival after the fixture
unblocks). A structural consequence of how the fixture is scoped, not a
flaky tail.

**This directly answers the brief's question 1: yes, the fixture's own
structure produces exactly the failure shape a naive per-test-case
watchdog handles badly** — a multi-minute legitimate silence sitting at the
very front of nearly every run, before any gated test has even been
dispatched. It does not, however, attach to "the first test's duration" the
way the brief's hypothesis framed it (no single `UnitTestResult` shows an
inflated duration) — it shows up as **inter-test dead air**, which is the
exact quantity `--blame-hang-timeout` watches.

### 2.3 Finding 2: one individual test legitimately runs ~97 seconds, every run

`EventIngestion.PoisonDeliveryEscapeIntegrationTests.
One_unstorable_delivery_is_recorded_and_released_without_delaying_the_rest`
has its own `duration` of 97.2–97.3s in all four runs (97.26, 97.26, 97.20,
97.26) — a retry/backoff-bound wait, by its own name ("without delaying the
rest" implies the test asserts other deliveries proceed while this one
retries out). This is the **second** largest host-wide gap in every run
(97s, vs. the ~132s fixture-boot gap as the largest), and it recurs with
the same identity and near-identical duration across all four samples —
deterministic, not flaky.

### 2.4 The measured ceiling, stated plainly

**Across four independent green runs, the legitimate host-wide
inactivity ceiling is a stable 120–134 seconds**, produced by two
identified, understood, and named structural causes (fixture bring-up;
one retry-bound test) — not by unexplained variance. No other gap in any
of the four runs exceeds 16 seconds outside these two.

### 2.5 A caution this repo has already paid for once: #2382

The brief asks what #2382's 15.5-minute Playwright teardown hang implies
here. Checked directly rather than assumed: **#2382 is not this
population.** It is `e2e`'s Playwright teardown
(`retire-e2e-cameras.teardown.ts`), a browser-driven re-sign-in stall
against Keycloak — a different test runner, different job, different
failure mechanism (an unbounded `await` after a deadline check that stopped
being consulted) than anything in `Integration.Tests`. `ci.yml`'s own
comment already scopes it to the `e2e` job, not `integration`. #2410's issue
body draws the analogy loosely ("the `integration` job boots the same kind
of Docker-bound Aspire fixture") without claiming #2382 occurred here, and
that reading is correct: it did not.

What #2382 **does** transfer is a method lesson, and it is the one worth
taking seriously: *"a sample of successful runs was used to size a CI
timeout, and the upper mode was read as 'slow but healthy' rather than as
[a] defect. #2376 was corrupted by it."* Four green runs here show a *very*
tight, *repeatable* ~120–134s band with two named causes — which is a much
stronger footing than #2376 had (that analysis did not know the cause of
its own upper mode). But it is still four runs, still all green, and still
incapable by construction of showing what this job's tail looks like on a
loaded runner, a slow image pull, or a Keycloak realm import that takes
longer than usual. The budget in §3 is sized with this asymmetry in mind:
**the cost of a too-tight budget (a false failure on a legitimately slow
but healthy run) is treated as worse than the cost of a looser one**, per
the issue's own framing.

### 2.6 Finding 3: the fixture already has its own boot-phase watchdog — and it out-diagnoses vstest's

`InitializeAsync` wraps the entire bring-up in `using CancellationTokenSource
cts = new(StartupTimeout)` where `StartupTimeout = TimeSpan.FromMinutes(8)`
(`AspireFixture.cs:60`). On expiry it does not fail silently: the `catch
(Exception ex) when (ex is OperationCanceledException or
TaskCanceledException)` clause captures resource states, exit codes, failed
resource logs, and a recent log tail, then throws a `TimeoutException`
carrying all of it (`FormatTimeoutMessage`). **A boot-phase hang under 8
minutes is already diagnosed today, richly, with no CI change at all** —
this is a solved problem for the ordinary case (a container slow to pull, a
migration that stalls, a resource that never reaches `Running`).

This matters directly for sizing `--blame-hang-timeout` (§3): if that
budget is set **below** 8 minutes, it would pre-empt the fixture's own
better-diagnosed failure with vstest's generic hang dump — replacing a
message that names the resource, its exit code, and its log tail with one
that, worse, may have **no test to attribute the hang to at all**, because
no test in the gated collections has been dispatched yet during boot.
Checked directly: vstest's blame collector attributes a hang to whichever
test was "running" when the inactivity timer fired; during
`InitializeAsync`, xUnit has not started a test case, so the attribution
this mechanism's whole value proposition rests on would likely come back
empty or misleading for exactly the failure class this job is most exposed
to. **The budget must sit above `StartupTimeout`, not near it or below
it, or the new instrument actively degrades an existing one.**

What `--blame-hang` still buys, positioned above `StartupTimeout`: a
backstop for the case the fixture's own timeout **cannot** cover — its
cancellation not actually propagating through some awaited call (the same
class of risk spec 166 §2.1 found and named in
`WhepValidatorUnreachableRealmTests` — third-party code whose cancellation
forwarding an xUnit test, or this fixture, cannot fully verify). If `cts`
fires but an awaited `WaitForResourceAsync`/Docker/Aspire call does not
honour it, `InitializeAsync` never throws, and the process is genuinely
stuck below `--blame-hang`'s own layer. That is precisely the scenario
this instrument exists for, and precisely why it must be sized as an outer
layer rather than a tight scalpel.

---

## 3. The instrument — three parts, sized against §2

### 3.1 `--blame-hang` on `ci.yml`'s "Run integration tests" step

`--blame-hang --blame-hang-dump-type mini --blame-hang-timeout 12min`.

**Why 12 minutes, not 3:**

- **≥5.3× the measured, repeated, two-cause-explained legitimate ceiling**
  (134s). Smaller than `backend`'s ~12× margin over its own ceiling, and
  that gap between the two ratios is not an oversight — `backend`'s ceiling
  was a few seconds of in-process fake-driven work with abundant room
  above it before anything else mattered; this job's ceiling is a real
  Docker/network/migration bring-up that is structurally close in order of
  magnitude to a "few minutes," so the same multiplier would demand a
  budget (~24–27 min) that eats nearly the entire 30-minute job ceiling —
  which itself would make the instrument nearly as slow as doing nothing
  (§3.3's "worse than the hang it catches" test).
- **Above `StartupTimeout` (8 min) with real margin** (§2.6) — an ordinary
  boot hang is caught and richly diagnosed by the fixture's own mechanism
  first; `--blame-hang` never gets to pre-empt it with a worse message.
  +50% over 8 minutes lands at 12.
- **Comfortably inside the 30-minute job ceiling**, leaving the job's own
  timeout as the final backstop unchanged, and firing at roughly the
  40% mark rather than the 100% mark — the same qualitative improvement
  #2409 delivered for `backend` (silence-until-ceiling → named failure
  well before it), sized honestly for a population where "well before"
  cannot mean "in under 3 minutes."

**The margin, stated as a figure, and what it has to absorb:** 12min −
8min = **240s**. That 240s is not slack against a known delay — it is
the budget available to absorb the one failure class `--blame-hang`
exists to backstop *above* `StartupTimeout` (§2.6): `StartupTimeout`'s
own `cts` firing but the cancellation **not propagating instantly**
through some awaited call (`WaitForResourceAsync`/Docker/Aspire).
Measured directly rather than assumed — §5 counterfactual 2's own
procedure, run for this purpose: `StartupTimeout` shrunk to 10s
(temporarily, locally, reverted before this PR) and one
`WaitForResourceAsync` pointed at a resource name that can never
resolve (`"keycloak-typo"`) — the fixture's own `TimeoutException` fired
**~30ms after the nominal 10s** in that construction. Propagation was
essentially instant for the one code path this measurement could reach
(`WaitForResourceAsync` awaiting `ResourceNotificationService`). **240s
is stated as a floor for that reason, not a midpoint**: the one case
measurable locally showed ~0 delay, so the 240s is sized against the
*unbounded* version of this risk that a single local measurement cannot
rule out — third-party code that does not honour the token at all (the
class spec 166 §2.1 found in `WhepValidatorUnreachableRealmTests`), not
a known few-second figure to "absorb."

**Known fragility, recorded at the call site rather than discovered later:**
this number is coupled to **three** things that can move it without anyone
touching `ci.yml`: (1) `AspireFixture.InitializeAsync` gaining an
additional gated resource (grows the ~132s boot figure), (2) any
change to `EventIngestion`'s retry/backoff policy that
`PoisonDeliveryEscapeIntegrationTests` exercises (currently ~97s), and (3)
`StartupTimeout` itself (`AspireFixture.cs:60`, currently 8min) — the
only one of the three that **inverts the 12min/8min ordering** rather
than merely consuming margin. A later slice raising it toward or above
12min would, from that commit on, cause every boot-phase hang to be
pre-empted by `--blame-hang`'s generic dump — with no dispatched test to
attribute it to — instead of `StartupTimeout`'s own resource-state/
exit-code/log-tail table (§2.6), and nothing on the `AspireFixture` side
today flags that coupling (tracked as a follow-up: #2412, filed rather
than fixed here — `AspireFixture.cs` is out of scope for this spec's
diff, §6). **Checked, not assumed: `StartupTimeout`'s own 8-minute
figure has never moved in this file's history** — but the fixture's
boot-wait *logic* has been retuned before as ordinary feature work
(#2064, the migration-exit-code gate), which is the actual precedent for
"this class of change happens here," not evidence the timeout itself has
drifted. None of the three items is hypothetical — all are ordinary
feature work in this repo.
The call-site comment must say so, so the next person who sees this
test's duration creep, or retunes `StartupTimeout`, does not mistake
policy drift for a CI regression or ship an inverted ordering unknowingly.

### 3.2 Upload the dump — extend the existing step, don't add one

Unlike `coverage-check.ps1` (which overrides `--results-directory` per
project), the `integration` job's `dotnet test` call takes no
`--results-directory` override — only `--logger "trx;LogFileName=
integration.trx"`. vstest's default applies: everything, including a
`--blame-hang` dump, lands under `TestResults/` in the working directory.
The existing "Upload integration test results" step already targets
`**/TestResults/*.trx` with `if: always()`. **Extend its `path:` list**
rather than adding a new step or reaching for `backend`'s wider
`**/*.dmp` glob — that wider glob exists specifically to cover
`coverage-check.ps1`'s per-project `--results-directory` override, which
does not apply here.

**Corrected after a real induced hang (counterfactual 1, §5): this
section's first draft called a single-segment glob
(`**/TestResults/*.dmp`) "correct and sufficient at this call site,
verified rather than assumed." That verification was wrong.** Checked
against a real `--blame-hang` dump, the single-segment form matched
neither of the two nesting depths vstest actually produced —
`.dmp`/`Sequence*.xml` land one level deeper than the `.trx`
(`TestResults/<guid>/...`), with a second copy three levels deep
(`TestResults/<host>_<timestamp>/In/<host>/...`). The single-segment form
never shipped in `ci.yml`; the glob below is what shipped.

Add `**/TestResults/**/*.dmp` and `**/TestResults/**/*Sequence*.xml`
(note the second `**` between `TestResults/` and the filename — required
to reach either real nesting depth above) to the existing `path:` block.
Same `*Sequence*` looseness as `backend`'s step, same reason (the
collector's actual filename and `dotnet test --help`'s documented
filename disagree — verified once, applies to both call sites since both
use the same pinned SDK's collector).

### 3.3 A cancellation-only container-log dump — new, answering the brief's question 3 directly

`--blame-hang` (§3.1) and the fixture's own `StartupTimeout` (§2.6) both
depend on *something inside the test host* noticing the hang and unwinding
cleanly enough to write a dump or throw. If neither does — the outer
30-minute `timeout-minutes` is what finally kills the job — everything
either mechanism might have captured is lost with the runner, the same gap
`709d56c8`/#2137 fixed for `e2e`'s `apphost.log`. That job's AppHost runs as
a separate `dotnet run` process with a redirected log file to `tail`;
`integration`'s AppHost runs in-process via `DistributedApplicationTestingBuilder`
inside the test host, so there is no equivalent single log file — but the
Docker containers it starts are still visible to the runner's own Docker
daemon regardless of what the test host is doing.

Add one new step, mirroring `709d56c8`'s exact condition (`if: failure() ||
cancelled()` — not `always()`, "that prints [N] lines on every green run"):

```yaml
- name: Container state and logs (on failure or cancellation)
  if: failure() || cancelled()
  run: |
    docker ps -a
    for c in $(docker ps -aq); do
      echo "::group::$(docker inspect -f '{{.Name}}' "$c")"
      docker logs --tail 200 "$c" || true
      echo "::endgroup::"
    done
```

This is the outermost of the three layers: it survives a case where
`--blame-hang` itself fails to fire cleanly (a known risk category — the
same "cancellation might not propagate" class this whole spec is built
around) and the job runs all the way to its 30-minute wall.

---

## 4. Does this need an ADR? No — same test as spec 166 §4, re-applied

ADR-0139 governs rules about code and tests; this changes none. It chooses
among no competing infrastructure topologies and trades against no NFR —
the AppHost's shape, the fixture's resource list, and every production
runtime file are untouched. Direct unADR'd precedent stands, now including
spec 166/PR #2411 itself (the identical shape, one job over) alongside
`37011568` (#2376), `709d56c8` (#2137, and directly reused in §3.3), and
`12211126` (#2077).

**What would have made this a BLOCK, and why it doesn't:** if §2 had
concluded no safe budget exists at all, or that the fixture's structure
requires reshaping (e.g., splitting boot-time waiting so a hang during
bring-up can be attributed to a specific resource inside a single test
case), that would be a design decision with real trade-offs, not an
application of an existing mechanism. §2.6 came close — it found the
fixture already *has* the better mechanism for the boot phase — but using
that finding only changes how `--blame-hang` is **sized and scoped**, not
whether the fixture itself changes. No fixture file is touched by this
spec's plan (§ engineer scope, `plan.md` §1).

---

## 5. Testing (§Testing / ADR-0144 phase 4a) — behaviour-changing, two counterfactuals needed

**BEHAVIOUR-CHANGING**, same reasoning as spec 166 §5: the job's behaviour
on a hang changes from silent-`cancelled`-at-30-minutes to a named failure
with diagnostics inside a bounded budget. New harness behaviour, red-first
required, real hangs cannot be summoned on demand (heisenbug class).

**This slice needs *two* constructed counterfactuals, not one — because §2
found two distinct hang shapes this instrument has to cover, and they
exercise different code paths:**

1. **A hung test case, once dispatched** (mirrors spec 166 exactly). Add
   one throwaway `[Fact]` — in a class carrying
   `[Collection(AspireCollection.Name)]`, so it runs through the real call
   site (`ci.yml`'s "Run integration tests" step, real filter) rather than
   the Docker-free path spec 166 used — with
   `await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);`
   as its body. Run unflagged (kill manually after ~30s, do not wait out a
   real 30-minute job), quote the silence. Run flagged, quote the failure,
   the named test, and the dump path. Delete the test; confirm `git status`
   clean before opening the PR.

2. **A boot-phase hang, cheaply reproduced.** §2.6's finding — that
   `--blame-hang-timeout` must sit above `StartupTimeout` or it degrades an
   existing, better mechanism — is a claim about *ordering*, not about
   duration, and it can be verified in a fast loop rather than a real
   8-to-12-minute wait: temporarily (local-only, never committed) shrink
   both `StartupTimeout` (e.g. to 10s) and, in a scratch `dotnet test`
   invocation, `--blame-hang-timeout` (e.g. to 20s and separately to 5s),
   and point one `WaitForResourceAsync` call at a resource name that can
   never satisfy (e.g. `"keycloak-typo"`). Observe and quote **both
   orderings**:
   - `StartupTimeout` (10s) < `--blame-hang-timeout` (20s): the fixture's
     own `TimeoutException` fires first — quote the rich, named message
     (`FormatTimeoutMessage`'s resource-state table).
   - `--blame-hang-timeout` (5s) < `StartupTimeout` (10s): `--blame-hang`
     fires first — quote whatever vstest actually reports as "the test
     running" (this is the open question §2.6 raises: confirm directly
     whether it comes back empty, `null`, or misleading, rather than
     assuming). This is the demonstration that the ordering constraint in
     §2.6/§3.1 is not theoretical.
   Revert every temporary edit (`StartupTimeout`, the mistyped resource
   name, the scratch invocation) before opening the PR — none of it may
   appear in the shipped diff, same discipline as counterfactual 1.

3. **Quote all captures verbatim in the PR body** — four transcripts total
   (before/after × two counterfactuals) — per CLAUDE.md's phase-4 quoting
   rule and this repo's own *prove a guard by counterfactual* standing
   practice.

**What ships:** `ci.yml`'s three changes (§3.1–§3.3). No application code,
no fixture file, no existing test's assertions, count, or shape changes.

---

## 6. Non-goals

- **Not a retune of `backend` or `e2e`.** Untouched.
- **Not a fix for `PoisonDeliveryEscapeIntegrationTests`'s ~97s duration**
  or the fixture's ~132s boot — both are recorded as legitimate, understood
  costs this budget must clear, not defects this slice resolves.
- **Not a change to `AspireFixture.cs`, `StartupTimeout`, or any
  `WaitForResourceAsync` call.** §2.6 uses the fixture's existing behaviour
  as a sizing constraint; it does not propose changing it. If a future
  slice wants to shrink the ~132s boot (parallelise the resource waits,
  for instance), that is separate work with its own trade-offs.
- **Not `xUnit`'s `[Fact(Timeout = …)]`.** Same rejection as spec 166 §6 —
  does not abort the underlying operation, leaves the same orphaned-process
  signature, produces no dump naming the stuck frame.
- **Not a fold-in of #2382.** Confirmed in §2.5 to be a different job, a
  different runner, a different failure mechanism. Left as its own issue.

---

## 7. Acceptance

- `ci.yml`'s diff reviewed by `infra-reviewer` against §3 (right call site,
  the 12-minute figure justified against §2's measured numbers and
  explicitly checked against `StartupTimeout`, the glob extension verified
  against this call site's actual results-directory behaviour, the new
  container-log step's condition matching `709d56c8`'s precedent).
- The PR body quotes all four counterfactual transcripts from §5.
- A normal `integration` job run (no injected hang) still passes end to
  end with the new flags and step present.
- No ADR added; no existing test edited, skipped, renamed, or relaxed; no
  fixture file changed; `backend` and `e2e` jobs untouched.
- §0's flag is either resolved (a human confirms the lane proceeds,
  informed by §2's findings) or the issue is returned to `agent:blocked`
  pending that confirmation — this spec does not resolve that tension on
  its own authority.
