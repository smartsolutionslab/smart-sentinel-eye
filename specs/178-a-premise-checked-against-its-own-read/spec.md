# Spec 178 — A premise checked against its own read

**Issue**: #2223 · **Branch**: `fix/2223-a-premise-checked-against-its-own-read` · **Phase**: 1 (Specify)
**Date**: 2026-09-17 · **Context**: `AuditObservability` (test infrastructure only)
**Engineer**: `backend-engineer` · **Reviewer**: `backend-reviewer`
**Feature bucket**: spec `specs/109-*` (US3, the handover-leg reading) — this
consolidates the test infrastructure that spec left behind.
**ADRs**: ADR-0049 (`CancellationToken` mandatory and last), ADR-0084 (code
metric limits, **as amended 2026-09-17** — see §1.3), ADR-0036 (smallest
change; refactor changes shape, not behaviour), ADR-0052 / ADR-0053 (xUnit +
Shouldly, sentence-style names), ADR-0103 (Aspire fixture, no Testcontainers),
ADR-0109 (disjoint files → `[P]`), ADR-0126 (native acks hold a delivery
unacknowledged for the whole handler), ADR-0127 (evidence from the broker, not
a log search), ADR-0139 / ADR-0144 (two testing obligations; phase 4a has two
colours and no exemption), ADR-0037 (the phased workflow).
**Constitution**: §II (**does not bind** — no domain model is touched; this is
test infrastructure), §Testing (**binds, and decides the phase-4a colour of
every task here**), §IV (latency budget — **N/A**, see §7).
**New ADR needed**: **No.** Every rule this spec applies is already decided
(ADR-0049, ADR-0084, ADR-0036). Nothing here makes a design choice. See §8.

---

## 0. Scope, and what the premise check changed about it

Issue #2223 asks for three things. **The premise check (§1) refuted the stated
premise of two of them**, and the scope below is what survives that check —
not what the issue's text says.

| Issue asks for | Survives? | Becomes |
|---|---|---|
| Dedup the broker read; have `AuditHandoverPopulationTests` consume `AuditQueueProbe` | **Yes, and it is larger than stated** | **US1** — and the probe must *gain* the queue-name diagnostic, not the test lose it (§1.2) |
| Add the missing trailing `CancellationToken` to `AuditQueueProbe.ClientAsync` | **No — already done** | Folded into **US1**. The only real ADR-0049 gap left is in the three private helpers US1 deletes (§1.1). Not a separate task. |
| Split `NFR001_AuditIngestLatencyTests.cs`, 485 lines against ADR-0084's 300 | **Premise stale in both halves** | **US2**, restated honestly as **readability, not compliance** (§1.3) |

### 0.1 What is explicitly **not** in this spec

| Item | Why not |
|---|---|
| **What the probe measures.** `messages_unacknowledged`, the queue filter, the sampling interval, the drain window, the burst size. | The issue names this out of scope in bold: "Changing what the probe measures, or the Little's-law reading of deliver-ack to commit … were reviewed and found sound." Every number in §2 moves file-to-file **unchanged**. |
| **The Little's-law reading** in `AuditHandoverLegTests`. | Same clause. This spec changes where the read *lives*, never what it returns. |
| **Re-running the measurement to re-certify the premise.** | The premise check is a `Category=Measurement` fact excluded from CI. US1 is behaviour-preserving, so re-certification is not owed. Phase 5 observes it once (§6). |
| **Widening the ADR-0084 exemption, or narrowing it.** | §1.3 found the exemption is deliberate and freshly restated (spec 174 / #2209). Changing it is a decision, and this spec may not make one. |
| **`AuditHandoverLegTests.cs:90`'s `CancellationToken cancellationToken = CancellationToken.None;`.** | It is the folder-wide pattern for a measurement fact xUnit gives no token to. Changing it is neither asked for nor an ADR-0049 violation — the token is declared and threaded. |

---

## 1. The premise, checked by content before specifying

Every claim in issue #2223 was re-read against the working tree at
**`32a26c3a`** (2026-09-17). **The central claim holds and is sharper than
stated. Two of the three sub-claims are stale.**

### 1.1 The duplication — **holds**, and AS-2 is already fixed

| Claim | Verdict | Evidence |
|---|---|---|
| Two copies of a RabbitMQ `messages_unacknowledged` read exist | **Holds** | `tests/Integration.Tests/AuditObservability/AuditQueueProbe.cs:56-81` (`UnacknowledgedAsync`) and `.../AuditHandoverPopulationTests.cs:192-223` (`ReadAsync`). Both `GET /api/queues`, both filter by prefix, both sum `messages_unacknowledged`, both throw naming address + status on a non-2xx. |
| A *third* pair is duplicated too | **Holds — not in the issue** | `AuditQueueProbe.WaitForQuiescenceAsync:87-99` vs `AuditHandoverPopulationTests.WaitForQuiescenceAsync:139-151`, and `AuditQueueProbe.ClientAsync:41-53` vs `AuditHandoverPopulationTests.BrokerClientAsync:229-241`. The broker client is duplicated **character for character** including its doc comment. So it is three duplicated members, not one. |
| Both derive the prefix from the module, not a literal | **Holds** | `AuditQueueProbe.cs:35` and `AuditHandoverPopulationTests.cs:49` both read `AuditObservabilityInfrastructureModule.ContextName + "."`. Confirmed `ContextName = "audit-observability"` at `src/AuditObservability/Infrastructure/AuditObservabilityInfrastructureModule.cs:23`, and passed as `moduleQueuePrefix:` at `:94`; queue names are built `$"{moduleQueuePrefix}.{eventType.FullName}"` at `src/ServiceDefaults/WolverineDefaults.cs:96`. |
| The `wolverine_audit.*` wrong-prefix trap is real | **Holds, and is currently mitigated in both copies** | Both copies carry a doc comment naming the trap explicitly. The mitigation is that neither spells the prefix — both read it from the module. So the trap is *closed today*; the hazard the issue names is that it could be **reopened in one copy only**. |
| `ProcessInParallelWithNativeAcks` is what makes the count meaningful | **Holds** | `src/ServiceDefaults/WolverineDefaults.cs:128`. |
| The ~5 s `collect_statistics_interval` cached-zero trap is real | **Holds** | Mitigated by sizing, documented at `AuditHandoverPopulationTests.cs:57-70`: 50 writers × 60 events, sampled at 250 ms, plus a 20 s `DrainWindow` (`:81`). **This sizing lives only in the population test** — the probe has no opinion on it, which is correct and stays that way. |
| **`AuditQueueProbe.ClientAsync` is `async` with no trailing `CancellationToken`** | **STALE — false at the tip** | `AuditQueueProbe.cs:41` reads `ClientAsync(AspireFixture aspire, CancellationToken cancellationToken)`. Added by commit **`9bd2f216`**, which post-dates the issue (filed 2026-09-09). |
| **`AuditHandoverLegTests.cs:177` passes `CancellationToken.None` to the HTTP read** | **STALE — false at the tip** | The only `CancellationToken.None` in that file is `:90`, a declared local that is then threaded into every call (`:92`, `:101`, `:203`). Line 177 is `int landed = await WaitForRowsAsync(run.Context, identifiers, cancellationToken);`. |

**So AS-2 as written has nothing to fix.** The genuine ADR-0049 gap that
remains is in `AuditHandoverPopulationTests`'s three private helpers —
`ReadAsync`, `WaitForQuiescenceAsync`, `BrokerClientAsync` — none of which
takes a token at all. **US1 deletes all three.** Fixing AS-2 as a separate
task would mean adding a parameter to a method the same spec removes.

### 1.2 The finding the issue does not contain: the two reads are **not** equivalent

This is the most consequential result of the premise check, and it changes the
shape of the fix.

`AuditQueueProbe.UnacknowledgedAsync` returns **`int`**.
`AuditHandoverPopulationTests.ReadAsync` returns **`(int Unacknowledged, string[] Queues)`** — it
also collects the matched queues' short names (`AuditHandoverPopulationTests.cs:215`)
and the test prints them (`:93`):

```
audit queues seen: (none)
```

**That line is the mitigation for the exact trap the issue is about.** A wrong
prefix and an idle broker both produce `0`; only the queue list tells them
apart, by printing `(none)`. The population test is the premise check
*because* it can distinguish those two, and the diagnostic is how.

`AuditQueueProbe` drops it. **Consuming the probe as it stands would delete the
wrong-prefix tell from the premise check** — the dedup would be a net loss of
evidence, which is the opposite of the issue's intent.

**Therefore the fix is one-way in the direction of consumption but two-way in
content: the probe gains the queue names; the population test loses its copy.**
The issue's "one-way dependency and needs no new abstraction" still holds —
this is one extra field on the probe's return, not a new type hierarchy.

### 1.3 The file-length claim — **stale in both halves, and inverted**

| Claim | Verdict | Evidence |
|---|---|---|
| `NFR001_AuditIngestLatencyTests.cs` is 485 lines | **Stale** | **368 lines** at `32a26c3a`. Still over 300, by 68. |
| ADR-0084's 300-LOC limit is "stated" but the analyzer "is not enforcing on test projects" | **Understated — it is now an explicit exemption, freshly restated** | `.editorconfig` has `[src/**.cs]` → `dotnet_diagnostic.S104.severity = warning` and `[tests/**.cs]` → `dotnet_diagnostic.S104.severity = none`, with a comment saying the test section exists precisely because "editorconfig severity beats" `Directory.Build.props`'s `NoWarn`, so "the exemption has to be written where it wins" (spec 174 / #2209, 2026-09-17). ADR-0084's own decision text: "**Test projects exempt** — long, narrative test methods are valuable". `SonarLint.xml` sets `maximumFileLocThreshold` to 300 but carries thresholds only. |

**Answer to the scoping question the brief asked:** the `[tests/**.cs]`
carve-out **does** apply to this file. S104 will **not** warn on it, before or
after the split, and `dotnet build -c Release` is clean either way. Further,
ADR-0084's five limits are now **advisory (`warning`, never `error`)** even
inside `src/` — so there is no build gate here under any scoping.

**Consequence for the spec:** US2 is **readability, and must be written as
readability.** A spec that justified this split by citing ADR-0084 would be
claiming a limit that the repository deliberately declared inapplicable to this
file one day earlier. The honest justification is the one the issue also gives:
*"the file now holds two verdict-shaped facts that split naturally."* That is
true and sufficient on its own (§2.2).

**Flagged for the gate:** if the reviewer's position is that a split with no
enforcing rule behind it is not worth a PR, **US2 should be dropped and the
issue closed on US1 alone.** This spec recommends keeping it — 368 lines
carrying two independent verdicts is a real cost — but records that it is a
judgement, not a requirement.

---

## 2. User stories, prioritised

### US1 (P1) — The premise is certified against the read the conclusion uses

*As the person reading NFR-001's handover figure, I want the test that certifies
`messages_unacknowledged` tracks in-flight population to perform **the same
broker read** the figure is computed from, so that a later correction to the
read cannot leave the certification covering something the probe no longer
does.*

**Independently shippable.** Touches two test files and nothing else. The
handover figure and every other fact in the folder are unaffected.

### US2 (P2) — Two verdict-shaped facts, two files

*As someone opening `NFR001_AuditIngestLatencyTests.cs` to read one of its two
facts, I want each fact in its own file, so that the 368 lines I have to scan
are the ones belonging to the question I am asking.*

**Independently shippable, and independent of US1** — disjoint files, so `[P]`
(ADR-0109). Ships even if US1 is rejected, and vice versa.

---

## 3. Acceptance scenarios (Gherkin)

Ordinary auth / bad-request / conflict scenarios **do not apply**: no endpoint,
no request handling, no trust boundary is touched. The scenarios below are the
ones this change can actually fail.

### US1

```gherkin
Scenario: The premise check reads through the probe
  Given AuditHandoverPopulationTests no longer declares ReadAsync,
        WaitForQuiescenceAsync or BrokerClientAsync
  When the measurement fact runs against the Aspire stack
  Then every broker read it performs is a call into AuditQueueProbe
  And  a grep for "api/queues" across tests/Integration.Tests/AuditObservability
       returns exactly one hit, in AuditQueueProbe.cs
```

```gherkin
Scenario: The verdicts are unchanged
  Given the two Shouldly assertions in
        Unacknowledged_deliveries_on_the_audit_queues_are_zero_when_quiescent_and_not_when_handlers_run
  When the fact is run before the change and after it
  Then both assertions are byte-identical across the change
  And  both pass in both runs
  And  the quiescent reading is 0 and the peak reading is greater than 0 in both
```

```gherkin
Scenario: The wrong-prefix tell survives the consolidation
  Given the probe's read now answers the matched queue short-names alongside the count
  When the premise check runs
  Then it still prints an "audit queues seen:" line
  And  that line names the audit queues rather than "(none)"
```

```gherkin
Scenario: A wrong prefix is still distinguishable from an idle broker  [conflict case]
  Given the queue prefix is temporarily changed to one that matches nothing
  When the premise check runs
  Then it prints "audit queues seen: (none)"
  And  it fails on the peak assertion rather than passing quietly
  # Counterfactual, run once by hand at phase 5 and reverted — not committed.
  # Memory: "Prove a guard by counterfactual."
```

```gherkin
Scenario: Cancellation is honoured through the consolidated read  [ADR-0049]
  Given every broker-reading method reachable from the premise check
  When its signature is inspected
  Then each takes CancellationToken as its last parameter
  And  each passes it to the HTTP call it makes
```

### US2

```gherkin
Scenario: Both facts still run, from their new homes
  Given NFR001_AuditIngestLatencyTests.cs has been split
  When the AuditObservability measurement facts are discovered
  Then Where_the_ingest_span_goes is discovered exactly once
  And  Requirement_span_at_100_events_per_second_is_an_interval_not_a_verdict
       is discovered exactly once
  And  the body of each test method is unchanged from before the split
```

```gherkin
Scenario: The split does not duplicate what it moves  [the defect US1 removes]
  Given the members shared by both facts — P99BudgetMs, ServiceLogLevel,
        ChosenServiceLogLevel, ServiceLogLevelWasChosen, DrivePosition
  When the two new files are inspected
  Then each shared member is declared exactly once across both files
  # A split that copies the log-level guard into both files would reintroduce,
  # in the same PR, precisely the drift class US1 exists to remove.
```

```gherkin
Scenario: Release build stays clean  [bad-input case]
  Given .editorconfig [tests/**.cs] sets S104 severity to none
  When dotnet build -c Release runs before and after the split
  Then neither build emits S104 for these files
  And  the spec does not claim the split was required by a build gate
```

---

## 4. Independent end-to-end test procedure

Runnable by someone who did not write the change. The Aspire stack must be up
(ADR-0103; **one machine, one stack** — do not boot a second).

```sh
# 1. Before the change, on the branch point. Capture the characterisation.
dotnet test tests/Integration.Tests \
  --filter "FullyQualifiedName~AuditHandoverPopulationTests" \
  --logger "console;verbosity=detailed" 2>&1 | tee before-us1.txt

# Expect: 1 passed. Record from the output, verbatim:
#   audit queues seen : <the audit queue short names, NOT "(none)">
#   quiescent         : messages_unacknowledged = 0
#   mid-flight        : <n> samples ..., <k> non-zero, peak = <p>   with p > 0

# 2. After the change. Same command.
dotnet test tests/Integration.Tests \
  --filter "FullyQualifiedName~AuditHandoverPopulationTests" \
  --logger "console;verbosity=detailed" 2>&1 | tee after-us1.txt

# Pass: 1 passed; quiescent still 0; peak still > 0; the "audit queues seen"
# line still names the same queues. The assertion TEXT must be byte-identical —
# `git diff` must show no change inside either ShouldBe/ShouldBeGreaterThan.
# Peak and sample counts will differ run to run; that is load, not regression.

# 3. The duplication is actually gone.
grep -rn "api/queues" tests/Integration.Tests/AuditObservability/
# Pass: exactly one hit, AuditQueueProbe.cs.
grep -rn "messages_unacknowledged" tests/Integration.Tests/AuditObservability/
# Pass: hits only in AuditQueueProbe.cs plus assertion/comment prose.

# 4. US2 — both facts still discovered, bodies unchanged.
dotnet test tests/Integration.Tests --list-tests \
  | grep -E "Where_the_ingest_span_goes|Requirement_span_at_100_events_per_second"
# Pass: exactly two lines.

# 5. Release build clean, and S104 silent on tests either way.
dotnet build -c Release 2>&1 | grep -c "S104"
# Pass: no S104 diagnostic naming any file under tests/.

# 6. Counterfactual, by hand, reverted immediately (memory: prove a guard by
#    counterfactual). Temporarily point AuditQueueProbe.QueuePrefix at a
#    prefix that matches nothing, re-run step 2.
# Pass: the fact FAILS on the peak assertion AND prints
#       "audit queues seen: (none)". If it fails without printing "(none)",
#       the diagnostic did not survive the consolidation — that is a blocker.
```

**Note (memory: measurement runs need repeating).** `AuditHandoverPopulationTests`
is `Category=Measurement` and excluded from CI. The first run after machine
churn looks like a regression. **Run steps 1 and 2 twice each** before calling
any difference real.

---

## 5. Locked tech choices

Nothing new. Everything is already in the stack.

| Concern | Choice | Source |
|---|---|---|
| Test framework | xUnit + Shouldly | ADR-0052 |
| Test naming | sentence-style with underscores | ADR-0053 |
| Integration harness | `AspireFixture`, real stack, no Testcontainers | ADR-0103 |
| Cancellation | `CancellationToken` mandatory, last | ADR-0049 |
| Evidence source | the broker and Postgres, never a log search | ADR-0127 |
| Code metrics | ADR-0084, **advisory; `tests/**` exempt** | §1.3 |
| Parallel marking | `[P]` only on disjoint files | ADR-0109 |

---

## 6. Phase 4a colour — per piece, with reasoning

Constitution §Testing has two obligations. The declaration is per task, not
per spec, and ambiguity resolves to red.

| Task | Colour | Reasoning |
|---|---|---|
| **T001 / T002 (US1, the dedup)** | **GREEN — characterisation** | Behaviour-preserving by construction: the same broker, the same endpoint, the same prefix, the same sum, the same two assertions. `AuditHandoverPopulationTests`'s assertions **must pass unmodified**. §Testing: "A red test during a behaviour-preserving change is a regression." **If any assertion has to change to pass, the two reads were not equivalent — STOP and report; do not adjust the assertion.** The characterisation is captured by running the fact *before* the change (procedure §4 step 1) and keeping the output. |
| **AS-2 (the `CancellationToken`)** | **NO TEST — and no task** | Two reasons, both dispositive. (1) **It is already fixed** (§1.1); the stated defect does not exist at the tip. (2) Even for the residual gap in the deleted private helpers, it is not meaningfully testable red-then-green. Constructing an observable "in-flight sample outliving cancellation" would need to catch an outstanding `GET /api/queues` against the live management API after the token trips — the window is a single sub-second HTTP round trip, the observation would be a race, and a test that passes because the request happened to finish first is an assertion that cannot fail (memory: *an assertion must not check its own input*). **Verdict: mechanical ADR-0049 compliance, no practical test surface, and here not even a compliance gap — it rides along inside US1's deletion and is verified by signature inspection (§3, the ADR-0049 scenario), not by a test.** Stating this rather than defaulting, as the brief asked. |
| **T004 (US2, the split)** | **GREEN — characterisation** | Pure file movement. Both `[Fact]` bodies move byte-identically; only `class`/`using`/shared-member placement changes. The characterisation is the two facts passing before and after. **No new test is written** — the split's correctness is "nothing broke", and §4 steps 4-5 are the confirmation. |

**No task in this spec is red.** That is the correct outcome for a refactor,
and it is declared here explicitly so phase 4a does not have to infer it —
ADR-0144 gives 4a no exemption, only two colours, and both are chosen above.

---

## 7. Latency-budget impact

**N/A.** No leg of the 800 ms `event arrival → overlay rendered` path is
touched (constitution §IV). This spec changes **test infrastructure only** —
no file under `src/` is modified by any task.

The adjacent budget is NFR-001 (audit ingest, p99 ≤ 50 ms publish→row), which
is **not** one of §IV's six legs. This spec does not change what NFR-001
measures, only which file the measurement's code lives in — explicitly out of
scope per §0.1.

---

## 8. New ADR needed?

**No.** Every rule applied here is already decided:

- ADR-0049 — token mandatory and last. Applied, not amended.
- ADR-0084 — code metrics, amended 2026-09-17 by spec 174. This spec **reads**
  that amendment (§1.3) and changes nothing about it.
- ADR-0036 — smallest change; a refactor changes shape, not behaviour.

**One thing that would need a decision, and is therefore excluded:** whether
`tests/**` should be exempt from S104 at all. §1.3 found the exemption is
deliberate and one day old. Reopening it is an ADR, not a task — so US2 is
scoped as readability and the exemption is left exactly as spec 174 wrote it.

---

## 9. Files phase 4 may touch

**Exactly these five. No file under `src/` is in scope.**

| File | Story / task | Change |
|---|---|---|
| `tests/Integration.Tests/AuditObservability/AuditQueueProbe.cs` | US1 / T002 | Gains the matched queue short-names on its read (§1.2), **strictly additively** — existing members keep their signatures and behaviour. |
| `tests/Integration.Tests/AuditObservability/AuditHandoverPopulationTests.cs` | US1 / T003 | Loses `ReadAsync`, `WaitForQuiescenceAsync`, `BrokerClientAsync`, the `AuditQueuePrefix` field and four now-unused `using` lines. **Both assertions unchanged.** |
| `tests/Integration.Tests/AuditObservability/IngestMeasurementConditions.cs` *(new)* | US2 / T007 | Receives the four members both NFR-001 facts share — `ServiceLogLevel`, `ChosenServiceLogLevel`, `ServiceLogLevelWasChosen`, `DrivePosition` — declared **once**, doc comments intact. |
| `tests/Integration.Tests/AuditObservability/NFR001_RequirementSpanIntervalTests.cs` *(new)* | US2 / T008 | Receives `Requirement_span_at_100_events_per_second_is_an_interval_not_a_verdict`, plus `P99BudgetMs` and `BudgetPlacement`, which only it uses. |
| `tests/Integration.Tests/AuditObservability/NFR001_AuditIngestLatencyTests.cs` | US2 / T009 | Trimmed to the class doc and `Where_the_ingest_span_goes`. |

`AuditHandoverLegTests.cs` is **read** to confirm the probe's existing callers
still compile. It is **not edited** — plan §2.1 keeps the probe change additive
precisely so it does not have to move. If phase 4 finds itself editing it, the
change stopped being one-way: **stop and report.**
