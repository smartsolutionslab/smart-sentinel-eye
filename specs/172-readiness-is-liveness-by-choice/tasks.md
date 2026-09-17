# Tasks 172 — Readiness is liveness, by choice

**Spec:** `specs/172-readiness-is-liveness-by-choice/spec.md`
**Plan:** `specs/172-readiness-is-liveness-by-choice/plan.md`
**Issue:** #2125
**Branch:** `docs/2125-readiness-is-liveness-by-choice` (cut from `develop` at `a4a36ea1`)

**Phase 4a colour: GREEN CHARACTERISATION.** Nothing in this feature changes
runtime behaviour. The test of T002 is captured **passing before** any source
edit and must pass **unmodified** afterwards. A red first run is a stop, not an
adjustment — see T002's escape clause.

**Agents.** `test-writer` owns T002 and T004 and returns verbatim output.
`infra-engineer` owns T003, T005, T006a–c, T008, T010. The orchestrator owns
T007 and T009.

**Single user story.** Every task is `[US1]` — "the reader who asks why the probe
cannot fail" (spec §3). There is no US2; spec §6 says why.

---

## Phase 0 — Premise

### T001 [US1] Re-verify the four outcomes and the stale row at the branch tip

**Depends on:** nothing. **Blocks:** everything.

The issue is a few weeks old and **one of its two asks was already 60% done**
before this spec was written. Confirm at `a4a36ea1` before touching anything:

```sh
grep -n 'AddCheck("self"' src/ServiceDefaults/Extensions.cs                 # → :129, Healthy(), ["live"]
grep -rn "HealthCheckResult.Unhealthy\|HealthStatus.Unhealthy" src/          # → no hits in src/
grep -n "failureStatus" src/ServiceDefaults/WolverineDefaults.cs             # → :170, HealthStatus.Degraded
grep -rn "ResultStatusCodes" src/ tests/                                     # → one hit, a comment
grep -rn "AddCheck\|AddTypeActivatedCheck" --include=*.cs src/               # → exactly Extensions.cs:127,129 + WolverineDefaults.cs:168
grep -n "database check owns this\|is #2125" src/ServiceDefaults/OutboxBacklogHealthCheck.cs
```

The last line is the one that matters: the **comment** #2125 quotes (*"already
reported by the connection's own check"*) is **gone** — `588e1e5e`, 2026-09-06.
What remains is the **description string** at `:141` and the stale *"…is #2125"*
forward. If the string is also gone, **stop and report**: the scope has shrunk
again and T005 may be a no-op.

**Done when:** every expectation above holds, or a divergence is reported before
any file is edited.

---

## Phase 4a — Evidence captured before the change

### T002 [P] [US1] Characterise the unreachable-database path, observed GREEN

**Agent:** `test-writer`. **Depends on:** T001. **Blocks:** T004, T010.
**Owns:** `tests/ServiceDefaults.Tests/OutboxBacklogHealthCheckTests.cs` (new).
**Parallel with:** T003 (different tree, different agent).

Write **one** `[Fact]`, per plan §4:

- `A_database_that_cannot_be_reached_is_reported_healthy`
- Private `sealed` `DbContext` in the test file, no model, `UseNpgsql` against
  `Host=127.0.0.1;Port=1;Database=probe;Username=probe;Password=probe;Timeout=1`
- `NullLogger<…>.Instance`; outbox schema `"wolverine_probe"` (lower-case +
  underscore, so `Identifier()` accepts it — a rejected schema throws **before**
  the connection attempt and would make this green for the wrong reason)
- `result.Status.ShouldBe(HealthStatus.Healthy, "…")` where the `because` message
  **names ADR-0154 and says this is a decision, not a bug**
- Assert the result's `Data` carries the exception type name under `"error"`
- A doc comment stating what the test believes about port 1 and why `Timeout=1`
  bounds the alternative

**Run it on the unmodified tree and return the verbatim output.** No source file
may be edited in this task.

**Escape clause — the one legitimate red.** If it fails because the exception is
not caught by `catch (DbException ex) when (IsUnreachable(ex))`, the swallow does
not work and the decision record would describe behaviour the code lacks.
**Stop. Report the verbatim failure. Do not edit the test. Do not edit the
`catch`.** That is a new finding and needs its own issue (spec §4.5).

**Done when:** the test passes on the untouched tree and its output is quoted.

### T004 [US1] Counterfactual — prove the test can fail

**Agent:** `test-writer`. **Depends on:** T002. **Blocks:** T005.

Edit `OutboxBacklogHealthCheck.cs`'s unreachable `catch` by hand:
`HealthCheckResult.Healthy(...)` → `HealthCheckResult.Unhealthy(...)`. Run T002's
test. **It must fail**, and the failure message must name ADR-0154. Capture the
output. **Revert with `git checkout -- src/ServiceDefaults/OutboxBacklogHealthCheck.cs`.**

Mandatory, not optional: a characterisation test nobody has seen fail is a test
whose claim is unverified, and three guards in this repository have failed their
own claim when someone finally constructed what they said they caught.

**Note:** a reverted file keeps its old timestamp and MSBuild will skip the
rebuild. Touch it or use `--no-incremental` before the next run.

**Done when:** the red output is captured and the working tree is clean of the
inversion (`git diff --stat src/` → empty).

---

## Phase 4b — The change

### T003 [P] [US1] Write ADR-0154

**Agent:** `infra-engineer`. **Depends on:** T001. **Blocks:** T005, T006a,
T006b, T006c. **Owns:** `docs/adr/0154-readiness-is-liveness-by-choice.md` (new).
**Parallel with:** T002.

Follow `docs/adr/_template.md`. `**Status:** Accepted`, today's date,
`**Amends:** Constitution §Availability`.

Content per spec §5.1. Non-negotiable elements:

1. **Attribution.** The Decision section states that the repository owner chose
   option 1 of the two #2125 names, in the session that commissioned spec 172.
   An ADR that does not say who decided is how the next reader concludes an agent
   decided.
2. **The four outcomes**, as the table from spec §2 — `Healthy → 200`,
   `Degraded → 200`, `Postgres unreachable → Healthy → 200`, `throw → Degraded →
   200` — with the live figures from the running stack.
3. **Decision 1:** `/health` is a liveness and routing signal. It answers `200`
   unless the process cannot serve at all.
4. **Decision 2:** no check registered on `/health` may name or reach an external
   dependency — both the availability argument and spec 078's exposure argument,
   stated as two reasons, because a reader who keeps one and drops the other will
   reintroduce the problem from the side they dropped.
5. **Decision 3:** a shared-dependency outage is reported through telemetry and
   logs, not readiness — **and ADR-0118 means that path has no destination in
   Production yet.** Record it as an undischarged consequence with its issue, not
   as something this decision settles.
6. **Decision 4 — what would reverse this:** a per-replica dependency (one a
   single pod can lose while its siblings are fine), or a topology where evicting
   one replica is not evicting all of them. Neither exists while ADR-0153 holds
   every service at one instance.
7. **Consequences,** including the negative an operator actually feels: a pod
   that cannot serve a single request stays in rotation.
8. **Alternatives Considered:** option 2 (a `ready`-tagged reachability check),
   declined, with its three costs — it reopens spec 078's exposure analysis, it
   needs a timeout or it hangs the probe, and it drains every replica for a
   condition none of them caused.
9. Cites **ADR-0106** (the "≥ 2 replicas with health checks" bullet this is read
   against), **ADR-0153**, **ADR-0118**, and **spec 078**. **Does not edit
   ADR-0106** — ADRs here are superseded, not rewritten, and readiness semantics
   are not 0106's subject.

**Done when:** the file exists, `grep -rn "readiness" docs/adr/` returns it, and
all nine elements are present.

### T005 [P] [US1] Correct the `catch` comment and the description string

**Agent:** `infra-engineer`. **Depends on:** T003, T004.
**Owns:** `src/ServiceDefaults/OutboxBacklogHealthCheck.cs` — **the
`catch (DbException ex) when (IsUnreachable(ex))` block only** (`:128-143`).
**Blocks:** T008, T010.

Two changes, both inside that block. Target state in spec §5.2.

- The comment: cite ADR-0154, keep the "there is no connection check" correction
  that `588e1e5e` already made, state the real reason (a shared-dependency outage
  is not this replica's problem and readiness is liveness-only by design), and
  **drop the trailing "Whether that is the right answer … is #2125"** — it is
  answered.
- The description string `"Backlog not readable; the database check owns this."`
  → `"Backlog not readable; a database outage does not fail this replica's
  readiness."` It asserts the same non-existent check the comment above it
  denies. It never reaches the wire (the framework's default writer emits one
  word, asserted by `DefaultEndpointsTests`), so this is a log string, not a
  security surface.

**Forbidden:** any change to the status returned, the `when` filter,
`IsUnreachable`, the `data` dictionary's keys, or any other `catch`/`return` in
the file. `git diff` must show comment and string lines only.

**Done when:** `git diff src/ServiceDefaults/OutboxBacklogHealthCheck.cs` touches
no executable line other than the description string literal, and
`grep -n "database check owns this" src/ServiceDefaults/OutboxBacklogHealthCheck.cs`
is empty.

### T006a [P] [US1] Point the outbox registration at ADR-0154

**Agent:** `infra-engineer`. **Depends on:** T003.
**Owns:** `src/ServiceDefaults/WolverineDefaults.cs` (`:166-172`).

One line in the existing comment above `AddTypeActivatedCheck`: the `Degraded`
failure status is ADR-0154, not a local preference. This is where someone adding a
tenth check will be standing, so the constraint belongs in front of them.

**Forbidden:** any change to the registration's arguments — name, `failureStatus`,
`tags` or `args`.

### T006b [P] [US1] Add the citation to `MapDefaultEndpoints`

**Agent:** `infra-engineer`. **Depends on:** T003.
**Owns:** `src/ServiceDefaults/Extensions.cs` — the `MapDefaultEndpoints` comment
block only.

The comment already says the exposure analysis is conditional on *"a check whose
**name** encodes a dependency"* never being added. That condition now has a
number: add the ADR-0154 citation to that sentence. Nothing else.

**Forbidden:** any code change; no comment on `AddDefaultHealthChecks` (`self` is
not where a dependency check would be added, and the house rule is no drive-by
comments).

### T006c [US1] **CONDITIONAL** — swap the constitution's citation

**Agent:** `infra-engineer`. **Depends on:** T003 **and an explicit sign-off**.
**Owns:** `.specify/memory/constitution.md` line 452 only. **Its own commit.**

`§Availability` says *"`/health` cannot return unhealthy (#2125)"*. Cite
**ADR-0154** alongside the issue. **The sentence's claim does not change** — it is
still true. This is a citation swap, not an amendment.

**ADR-0144 forbids the lane amending the constitution.** This task exists so the
edit is named rather than performed quietly. **If the reviewer or orchestrator
declines it, drop it — the spec still ships** (spec §9.3): the ADR is findable by
grep either way.

**Do not touch** ADR-0153's reference to #2125 or
`specs/078-health-answers-in-production/spec.md:184`. An ADR and a spec each
record a moment; spec 164 §2 set that precedent explicitly.

### T008 [US1] Follow the string into the integration test's doc comment

**Agent:** `infra-engineer`. **Depends on:** T005.
**Owns:** `tests/Integration.Tests/EventIngestion/OutboxBacklogIsVisibleTests.cs`
— the class doc comment (`:14-21`) only.

It quotes the old string: *"the failure is swallowed as \"the database check owns
this\""*. Update the quotation to the new wording and, in one clause, to the real
reason. **No assertion, no test method, no `[Fact]` may change** — this file runs
against the Aspire fixture and is not part of this slice's behaviour.

---

## Phase 4c — Evidence after the change

### T010 [US1] Re-run unmodified, and build Release

**Agent:** `infra-engineer`. **Depends on:** T002, T004, T005, T006a, T006b, T008.

1. `dotnet test tests/ServiceDefaults.Tests/SmartSentinelEye.ServiceDefaults.Tests.csproj`
   — green, **T002's test file unmodified since T002**
   (`git diff --stat` against the T002 commit → empty for that path). An
   assertion that had to be edited is evidence the behaviour moved: **block**.
2. `dotnet build SmartSentinelEye.slnx -c Release` — clean, analyzers included.
   **Stop the Aspire stack first** if `MSB3027` appears; it holds the service
   binaries.
3. `grep -rn "database check owns this" src/ tests/` → **no hits**.
4. `grep -rn "is #2125" src/` → **no hits**.
5. Against the running stack (pid 3312), **before** any stop:
   `curl -s -o /dev/null -w "%{http_code}" http://localhost:64997/health` → `200`.
   Unchanged is the expected result and the point of the declaration in spec §8.

Quote every figure. A measurement reported only to the orchestrator is invisible
to every later grep.

---

## Phase 3 gate / no-code tasks

### T007 [US1] File the follow-up: nothing guards a future `Unhealthy` registration

**Agent:** orchestrator. **Depends on:** nothing.

Spec §6 item 3. Body must contain:

- The gap: a tenth check registered with the framework's **default**
  `failureStatus` (`Unhealthy`), or with a name that encodes a dependency,
  violates ADR-0154 and **no test catches it**.
- Why this slice did not close it: `AddWolverineForContext` is not callable from a
  unit test (Postgres + RabbitMQ connection strings, `UseWolverine`); a test that
  builds its own registration would be asserting on its own input; a
  source-scanning guard proves the design was written down, not that it holds.
- The shape that would work: an assertion over a **booted** host's
  `IOptions<HealthCheckServiceOptions>` — every registration's `FailureStatus`
  is not `Unhealthy`, and no registration name matches a dependency vocabulary.
  That belongs with the Aspire fixture.
- Cite ADR-0154 and link spec 172.

Add it to Project #13. **Check the board for the same defect first** — a related
issue may already exist.

### T009 [US1] Board gate

**Agent:** orchestrator. **Depends on:** T007.

#2125 is already on Project #13 (status Todo). Verify rather than assume —
`item-list` defaults to 30 items, so a filled board reads as empty:

```sh
gh project item-list 13 --owner smartsolutionslab --limit 2000 --format json \
  -q '.items[] | select(.content.url | test("2125$")) | .content.url'
```

Match on `content.url`; the number filter returns zero. Per-task issues are
**not** created (feature-level since spec 028) — `tasks.md` is the artefact this
work is tracked against.

---

## Commit plan

Conventional Commits, **no `Co-Authored-By`** (ADR-0086). Each commit builds on
its own — rebase-merge lands them individually (ADR-0087).

| # | Tasks | Message |
|---|---|---|
| 1 | spec/plan/tasks | `docs(specs): record why readiness is liveness only (172)` |
| 2 | T002 | `test(service-defaults): characterise the unreachable-database path as healthy` |
| 3 | T003 | `docs(adr): readiness is a liveness signal by choice, not by omission` |
| 4 | T005, T006a, T006b, T008 | `docs(service-defaults): drop the connection check that never existed` |
| 5 | T006c *(conditional)* | `docs(constitution): cite ADR-0154 for the readiness decision` |

Commit 2 lands before commit 3 deliberately: the test passes on the tree *before*
the ADR exists, which is what makes it characterisation of existing behaviour
rather than a test written to a new document.

**Do not push.** Phase 3 gate: hand these artefacts back for review.
