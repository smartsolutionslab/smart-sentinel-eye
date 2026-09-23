# Spec 223 — Tasks

**Phase**: 3 (Tasks) · **Date**: 2026-09-23 · **Issue**: #2441
**Branch**: `2441-a-429-a-test-can-provoke` (cut from `origin/develop` @ `a40d7ad1`)
**Spec**: `specs/223-a-429-a-test-can-provoke/spec.md` ·
**Plan**: `specs/223-a-429-a-test-can-provoke/plan.md`

---

## Declarations

| | |
|---|---|
| **Engineer** | `backend-engineer` (phase 4b). The AppHost line is one `WithEnvironment` inside an existing block — `infra-engineer` is not warranted, and splitting one line across two agents would cost more than it buys. |
| **Reviewer** | `backend-reviewer` (phase 6). **Not security-sensitive**: no auth, scope, secret or trust-boundary change. The endpoint's `RequireAuthorization(Scope.Sse.Events.Write)` is untouched and the test authenticates as the ordinary fixture operator. |
| **Phase-4a colour** | **RED.** AS-1 fails today. See the table below. |
| **Counterfactuals** | Two defect injections at phase 5 (T010, T011), predictions written before the runs. |
| **New ADR** | **No.** Spec §9 states why for each piece. |
| **Latency budget** | **N/A**, no leg of constitution §IV. Spec §8 — no production code changes at all. |
| **Live Aspire stack** | **Required**, at three points: T003 (step-1 red), T007 (step-2 green) and T008 (whole bucket), plus phase 5's T010–T013. This slice cannot be completed without booting the stack. See the note at the end. |
| **Files phase 4 may touch** | The three below. **Anything else is a stop-and-report.** |

### Phase-4a colour, per acceptance scenario (ADR-0144)

| Scenario | Colour | Obligation |
|---|---|---|
| **AS-1** — the burst is shed with the documented title, and not everything is shed | **RED** | New behaviour at integration level. Must be observed failing first, **before** the AppHost line exists. **The load-bearing red.** |
| **AS-5** — the rest of the suite still holds under a stack-wide concurrency of 1 | **Characterisation, GREEN** | Behaviour-preserving. Every test that passed before must pass **unmodified** after. A test that has to be edited is evidence the behaviour moved: **block, don't adjust.** |
| **AS-2 / AS-3 / AS-4** — 401, 400 and fab scoping never become 429 | **Characterisation, GREEN** | Already covered by `AnonymousIngestIsRefusedTests`, `MissingPayloadIsRefusedIntegrationTests`, `ManualIngestFabScopingIntegrationTests`. **No new test.** Observed through AS-5. |

**The slice as a whole is RED.** A green at T003 is a phase-4 failure, not a
shortcut — see T004.

### Files phase 4 may touch

```
src/AppHost/AppHost.cs                                                        (one line, inside the existing if (isE2ETests) at :575)
tests/Integration.Tests/EventIngestion/IngestBackpressureIntegrationTests.cs  (new)
tests/Integration.Tests/ci-shards/shard-3.filter                              (one clause appended)
```

**Read-only, and named so a diff there reads as a stop:**
`src/EventIngestion/Api/EventsEndpoints.Writes.cs`,
`src/EventIngestion/Application/Ingress/IngestWriteLimiter.cs`,
`src/EventIngestion/Application/Ingress/IngestWriteOptions.cs`,
`src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs`,
`tests/Integration.Tests/Fixtures/*`, the other three `shard-*.filter` files.
The only edits the first two may receive are CF-A's and CF-B's transient
injections at T010/T011, each reverted and verified reverted.

---

## Phase 4a — the red (`test-writer`)

**Tests only. The AppHost line does not exist yet, and adding it here voids the
evidence.**

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T001** | | US1 | Write `tests/Integration.Tests/EventIngestion/IngestBackpressureIntegrationTests.cs`. One `[Fact]`, `A_burst_past_the_write_limit_is_shed_with_the_documented_problem_title`. `[Collection(AspireCollection.Name)]`; **no `[Trait]`** (spec §7 — a Measurement trait excludes it from CI and defeats the issue). Mirror `DirectWriteHonestyIntegrationTests` in the same folder: one client from `aspire.CreateAuthenticatedClientAsync("event-ingestion", "op-hamburg@hamburg.test", "Operator1234")`, body `{deviceId, kind, occurredAt, payload}`, run-unique `kind` = `$"Backpressure{Guid.CreateVersion7():N}"[..20]`. Build eight `PostAsJsonAsync("/events/manual", …)` **tasks first**, then one `await Task.WhenAll(...)` — awaiting inside a loop serialises them and the test asserts nothing. **No `Idempotency-Key`, no `Task.Delay`, no retry loop, no wall-clock bound.** Assert: ≥1 response is `429`; that response's problem body `title` is `"EVENT_INGEST_BACKPRESSURE"`; ≥1 response is `201`. | — |
| **T002** | `[P]` | US1 | Append one clause to `tests/Integration.Tests/ci-shards/shard-3.filter`: `\|FullyQualifiedName~SmartSentinelEye.Integration.Tests.EventIngestion.IngestBackpressureIntegrationTests.` — **keep the trailing `.`**, every clause has one. **Mandatory, and it goes in before the first run** (spec §1.1): a class in no shard runs nowhere, and "nowhere" looks like neither red nor green. **Do not rebalance the other three files** and **do not fold a category clause into this one** (the README says an earlier attempt broke `IntegrationTestSelectionTests` silently). | — |
| **T003** | | US1 | Build the test's failure messages so the red *says something* (spec §6.1, plan §6.2). Both assertions carry a Shouldly `customMessage` containing **the observed status distribution built from the actual responses** (e.g. `201×8, 429×0`) and `aspire.RecentLogs("event-ingestion")`. `output.WriteLine` the distribution unconditionally, pass or fail. **Build the string from the responses, never from a constant** — an assertion whose message cannot change when the subject changes is an assertion checking its own input. | T001 |
| **T004** | | US1 | **Step 1 of the three-step sequence. Run the new test against this branch with the `AppHost.cs` line still absent.** Stop any running AppHost first (one machine, one Aspire stack; a second boot gives `FailedToStart` that reads exactly like a code defect). `dotnet test tests/Integration.Tests/ --filter "FullyQualifiedName~IngestBackpressureIntegrationTests"`. **Expect RED**, and specifically `201×8, 429×0` — the limiter still grants 64. **Capture the output verbatim.** | T002, T003 |
| **T005** | | US1 | **A green at T004 is a stop-and-report, not a pass.** It means the 429 came from something other than this limiter, or the assertion was already true — either way the phase-4 gate is unsatisfied and the test is wired to nothing. Report the verbatim green and halt. Likewise a red whose distribution is *not* `201×8, 429×0` (a 401, a 400, a 500) is a different failure than the one predicted: report it, do not proceed by adjusting the test. | T004 |

**T001–T003 produce no production change. The verbatim output of T004 is
`test-writer`'s deliverable and is the brief `backend-engineer` receives.**

---

## Phase 4b — the reach (`backend-engineer`)

**Briefed with T004's verbatim output. The tests may not be edited to pass**
(ADR-0144).

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T006** | | US1 | Add **one line** to `src/AppHost/AppHost.cs`, inside the existing `if (isE2ETests)` block — today at `:575-588`, alongside the audit retention tick and the ingest-breakdown switch. **Locate it by content, not by line number** (spec §1 records both, and the numbers have already drifted twice since the issue was filed). `eventIngestion.WithEnvironment("EventIngestion__IngestWrite__Concurrency", "1");`. Note `__` is the section separator, matching the two lines already in the block and `EventIngestion__IngestRetry__MaximumRetryWindow` at `:492`. Add a comment saying **why** — the 429 seam is unreachable at 64 and this is the only channel the collection fixture has (house rule: comments say why). **No other AppHost edit.** | T005 |
| **T007** | | US1 | **Step 2. Re-run the same filter.** **Expect GREEN.** Capture the output **verbatim**, including the observed distribution — the figure is the evidence that the burst genuinely overlapped, not just that an assertion passed. | T006 |
| **T008** | | US1 | **Step 2b — AS-5. Run the *whole* Aspire integration bucket, not just the new test**, across all four shards: `dotnet test tests/Integration.Tests/ --filter "Category!=Measurement&Category!=Disruptive&Category!=Maintenance"`. E2E write concurrency is now **1 stack-wide** and spec §5.4's seven-row sweep is an argument that wants checking. Read these four first — they are write-path or auth-path tests on this endpoint and they sit in **different shards** (2, 3, 4, 2), so a single-shard run does not cover them: `EventTypeRegistryConcurrencyIntegrationTests`, `ManualIngestFabScopingIntegrationTests`, `MissingPayloadIsRefusedIntegrationTests`, `AnonymousIngestIsRefusedTests`. **A redden is a finding to report, not a reason to add a retry** to the reddened test — that would be weakening a gate to reach green. | T007 |
| **T009** | | US1 | Run `dotnet test tests/Architecture.Tests/`. Expect green and unchanged — in particular `LogTailCoverageTests` (the `RecentLogs("event-ingestion")` call site, which is legal because `event-ingestion` is in `TailedResources`), `IntegrationTestSelectionTests` (satisfied by the `[Collection]` declaration), and `AppHostE2ESwitchTests`. A failure here is a **finding to report**, not a rule to adjust. | T006 |
| **T010** | | US1 | Build Release (`dotnet build -c Release`) so the analyzer set that only fires there is exercised — `dotnet_style_prefer_collection_expression` at `warning` and the SonarAnalyzer metrics (ADR-0084). **Stop the Aspire stack first**; a running AppHost holds the service binaries and MSB3027 reads exactly like a broken build. | T007 |
| **T011** | | US1 | `git diff tests/Integration.Tests/` shows **only** the new file and the one appended shard clause — **no edit to any existing test**. An edited assertion elsewhere is a weakened gate (ADR-0144) and a **stop**, not a fix. Also confirm `git diff src/` is the single AppHost line plus its comment. | T008, T009, T010 |

---

## Phase 5 — verification and the defect injections

**Predictions are written before the runs. A mismatch is reported, never edited
into agreement.**

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T012** | | US1 | **CF-A — the contract, not the status code.** In `src/EventIngestion/Api/EventsEndpoints.Writes.cs:348`, change `title:` from `"EVENT_INGEST_BACKPRESSURE"` to `"EVENT_INGEST_OVERLOAD"`. Re-run the new test. **Predicted: RED on the title assertion, with the 429 status clause still satisfied.** The *shape* is the finding: if the status clause reddens too, the two clauses are coupled and the assertion needs splitting — report it. This proves the test names *this* seam rather than any 429 in the stack, the API gateway's own rate limiter included. Capture verbatim, then `git checkout -- src/` and confirm `git diff --stat src/` is empty. | T011 |
| **T013** | | US1 | **CF-B — the 429 comes from this limiter.** In `src/EventIngestion/Application/Ingress/IngestWriteLimiter.cs:43-44`, drop the `slots.Wait(0)` gate so `TryAcquire` always grants a lease. Re-run. **Predicted: RED with no 429 at all** — the same shape as T004's red. If a 429 survives, something other than the limiter is producing it: that is a **larger finding and a stop**. Capture verbatim, revert with `git checkout -- src/`, confirm `git diff --stat src/` is empty. | T012 |
| **T014** | | | Compare every result against the predictions in spec §6.3 and restate the comparison explicitly. Note that CF-A and CF-B **must fail differently** — that difference is what separates "the test noticed something" from "the test asserts what it claims". | T013 |
| **T015** | | | Run spec §10's six-step procedure against a **dev-mode** stack (`dotnet run --project src/AppHost`, `isE2ETests` false). Step 1's observation — that the new line does *not* apply in dev — is the one that shows production is untouched; step 4's is AS-1 seen from outside the test that asserts it. Mint the token from **Aspire's proxied Keycloak endpoint**, not the container's mapped port, or everything 401s. **Check the AppHost process's start time against the commit** before reading a manual `curl` as evidence — a persistent stack serves whatever binaries were loaded at boot. **Write every observed figure into the verification note as observed**; a figure reported only to the orchestrator is invisible to every later grep and reviewer. | T014 |
| **T016** | | | `git status --short` clean of anything outside the three files above. | T015 |

**This slice needs a live Aspire stack** — at T004, T007, T008 and throughout
phase 5. It cannot be completed from a build alone. **One machine, one Aspire
stack**: check for a running AppHost before every boot; two concurrent boots
give `FailedToStart` that reads exactly like a code defect, and an Aspire run
has zero-container gaps, so a container count is not a liveness test — look for
a `testhost` process naming this worktree.

---

## Parallelism (ADR-0109)

Almost none within this slice, and that is honest rather than a failure to look.

- **`[P]` T002** — the shard filter file is disjoint from the test file, and it
  is the one task that can be done in any order relative to T001 so long as both
  precede T004.
- Everything else is a chain, and the chain **is the evidence**: the red must be
  observed before the AppHost line exists, so T006 cannot move earlier and
  nothing after it can move before it.

**Nothing here is foundational.** No `Shared.Kernel`, `Shared.Contracts`,
Aspire-resource or migration change blocks other work. The one production file,
`src/AppHost/AppHost.cs`, **is a contention file** — it is a single file every
infrastructure slice touches — so the orchestrator should avoid running this
concurrently with another issue that edits the AppHost. It touches no
`src/EventIngestion/` file at all, so an EventIngestion feature can run
alongside it freely.

---

## Out of scope — already filed, or to file

| # | Item | Where |
|---|---|---|
| **W1** | **The webhook path's identical 429** (`EventsEndpoints.Writes.cs:171`). Same two lines, different auth shape — a registered integration's bearer rather than an operator's OIDC token, so it needs its own fixture setup. | **To file.** Recorded in spec §11 so its absence reads as a decision rather than an oversight. |
| **F1** | **Is 64 right for production on a 250-camera target?** | Filed by spec 175 as F1, still open. A sizing decision wanting a measurement; excluded in bold by #2212 and by this spec. |
| **F3** | **The limiter's acquisition timeout is untested** — `slots.Wait(50)` passes all four existing unit tests. | Filed by spec 175 as F3. Changing the gate is a behaviour change. |
| **#2211** | **The MQTT path's `Wait`** — the other backpressure mechanism. It does not touch `IngestWriteLimiter` at all. | Already filed. A design question with no ADR, and the lane may not write one. |
| — | **Rebalancing the four shard files.** | Not warranted: one added test case moves the partition to 157/157/158/157 against a hand-maintained balance its own README regenerates only on genuine drift. |
| — | **A per-test configuration hook on `AspireFixture`.** | ADR-0036. The `isE2ETests` block is the established channel and it suffices. |

---

## Gate (phase 3)

**Scope settled.** One user story, one `[Fact]`, three files, **no production
code**. The design is #2441's own, carried from `specs/175-a-limit-a-test-can-reach/`
per that spec's out-of-scope table rather than reinvented; every deviation is
named in spec §1.1 and §1.2 with its reason.

**Premise re-checked by content** against `origin/develop` @ `a40d7ad1`
(2026-09-23). All substantive claims hold. Two line numbers drifted
(`AppHost.cs` `:501`→`:575` and `:405`→`:479`; `AspireFixture.cs` `:275`→`:317`)
and **one new requirement appeared that the issue's 2026-09-17 design could not
have known**: spec 218's CI shard partition landed 2026-09-22, so the file list
is **three files, not two** — without the shard clause the class runs nowhere
and `integration-shard-coverage` goes red.

**Board:** #2441 is on **Project #13**, status **Todo**, carrying `agent:ready`
— **verified** by `gh project item-list 13 --owner smartsolutionslab --limit 2000`
(the default limit of 30 makes a filled board look empty). **No per-task
issues** — the repo stopped creating those after spec 028, and this `tasks.md`
is the artifact the work is tracked against.

**Phase 4 dispatch:** 4a is `test-writer` (T001–T005, tests and the shard file
only, verbatim output returned); 4b is `backend-engineer` (T006–T011), briefed
with that output. **A green at T004 ends the slice with a report, not a pass.**
