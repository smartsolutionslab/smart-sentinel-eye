# Spec 172 — Readiness is liveness, by choice

**Issue:** #2125
**Branch:** `docs/2125-readiness-is-liveness-by-choice`
**Decisions of record:** **ADR-0154** (new — drafted by this spec; see §5.1).
Secondary: **ADR-0106** §Consequences ("≥ 2 replicas with health checks" — the
sentence the issue says is cited for a guarantee the code does not provide),
**ADR-0153** (one instance per service; already cites #2125 as an open finding),
**ADR-0118** (one telemetry sink per environment — why the backlog signal has no
destination in Production), ADR-0037 (phases), ADR-0144 (autonomous lane),
ADR-0109 (`[P]`), ADR-0086 (no `Co-Authored-By`).
**Constitution:** §Availability (cites `#2125` by number at line 452 — see §5.4).
§IV latency budget: **N/A**, and demonstrably so — `ConfigureOpenTelemetry`
filters `/health` and `/alive` out of tracing (`Extensions.cs:138-141`), and no
probe sits on the `event → overlay` path.

---

## 1. Why

`GET /health` on every service in this system returns `200` under every
condition the code can produce. The check set has exactly two members and
neither can yield `Unhealthy`, so readiness that never fails is arithmetically
identical to no readiness probe at all.

**That property is not the defect this spec fixes.** The repository owner has
chosen to keep it: readiness deliberately does not gate on Postgres. A database
outage is shared by every replica of every service, so failing readiness on it
evicts all of them at once and converts a database outage into the loss of the
HTTP surface an operator would use to find out what is happening.

What this spec fixes is that **the choice is not recorded anywhere a reader can
find it**, and that the one place which explains it is a code comment whose
stated reason was, until eleven days ago, factually false. Three documents
(`ADR-0153`, the constitution §Availability, `specs/078/spec.md`) currently
describe the behaviour by pointing at an **open issue**, which reads as "known
defect, unfixed" rather than "decision, taken".

**Nothing in this spec changes runtime behaviour.** The `Healthy`/`Degraded`
split `OutboxBacklogHealthCheck` makes today is exactly what the chosen option
calls for. No check is added, removed, or re-tagged; no `failureStatus` moves;
no `ResponseWriter` is set. See §6 for what that excludes.

---

## 2. Premise, verified at the tip

Every row re-checked on `develop` at **`a4a36ea1`**, and the live rows driven
against the **running Aspire stack (pid 3312)** rather than a scratch host.
**Two rows changed the scope.**

| # | Claim from #2125 | Verdict | Evidence at the tip |
|---|---|---|---|
| 1 | `self` returns `Healthy()` unconditionally, tagged `live` | **Confirmed** | `src/ServiceDefaults/Extensions.cs:129` — `.AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])` |
| 2 | `outbox-{module}` returns only `Healthy` or `Degraded` on every path | **Confirmed** | `OutboxBacklogHealthCheck.cs` — five returns: `Healthy` at the empty case, `Healthy` under both thresholds, `Degraded` over either, **`Healthy` on the unreachable catch**, `Degraded` on the other `DbException`. `HealthCheckResult.Unhealthy` appears **nowhere** in `src/` (repo-wide grep: the only `Unhealthy` hits in `src/` are the two words in comments). |
| 3 | Registered `failureStatus: Degraded`, so an unhandled throw resolves to `Degraded` | **Confirmed** | `src/ServiceDefaults/WolverineDefaults.cs:168-172` |
| 4 | These are the **only** two checks in the solution | **Confirmed** | Repo-wide grep for `AddCheck`/`AddTypeActivatedCheck`/`AddHealthChecks` over `src/` returns exactly `Extensions.cs:127,129` and `WolverineDefaults.cs:168`. |
| 5 | No `ResultStatusCodes` override, so `Degraded` maps to `200` | **Confirmed** | `ResultStatusCodes` appears once in the whole repository, inside a comment (`OutboxBacklogHealthCheck.cs:119`). Both `MapHealthChecks` calls (`Extensions.cs:168,171`) pass no options but the `live` predicate. |
| 6 | Outcome "healthy (empty outbox) → 200 `Healthy`" | **Confirmed, live** | Against pid 3312's gateway: `GET http://localhost:64997/health` → `200 Healthy`; `/alive` → `200 Healthy`; `/camera-catalog/health` → `200 Healthy`; `/event-ingestion/health` → `200 Healthy`. |
| 7 | Outcomes "degraded → 200" and "unhealthy → 503" | **Confirmed, and already under test** | `tests/ServiceDefaults.Tests/DefaultEndpointsTests.cs` — `A_production_readiness_probe_answers_two_hundred_for_a_degraded_ready_tagged_check` and `A_development_readiness_probe_reports_a_failing_ready_tagged_check_as_one_word`. Both drive a synthetic `ready` check through the real `MapDefaultEndpoints`. |
| **8** | `OutboxBacklogHealthCheck.cs:131-133` says *"already reported by the connection's own check"* | **Already fixed — the issue is stale on this point** | Commit **`588e1e5e`** (2026-09-06, *"docs(servicedefaults): the comments name the forward and drop a check that does not exist"*) replaced it. The text at the tip (`:130-139`) already says there is no connection check and that the `Healthy` is deliberate. **What remains wrong is different and smaller** — see row 9. |
| **9** | *(new)* The same `catch` still hands back the description **`"Backlog not readable; the database check owns this."`** | **Found** | `OutboxBacklogHealthCheck.cs:141`. It asserts the same non-existent check the comment above it now explicitly denies, in a string that outlives the comment. The corrected comment also ends *"Whether that is the right answer, and what should own it instead, is #2125"* — which this spec answers, so the forward is now stale too. `tests/Integration.Tests/EventIngestion/OutboxBacklogIsVisibleTests.cs:18` quotes the string in its doc comment and moves with it. |
| 10 | The unreachable path is not covered by any test | **Confirmed** | Of the four outcomes, rows 6 and 7 are covered (live and by `DefaultEndpointsTests`). The `catch (DbException) when (IsUnreachable(...))` branch — the branch that *is* the decision — has **no test anywhere**. Grep for `OutboxBacklogHealthCheck` across `tests/` returns two files; neither drives an unreachable database. |
| 11 | In Production the backlog signal reaches nobody (ADR-0118) | **Confirmed, and out of scope** | Recorded in ADR-0154 §Consequences as a known consequence, not fixed here. |

**Scope consequence.** The issue's "also worth fixing while here" is **60% already
done**. The remaining edit is one string and one stale forward-reference, not the
paragraph the issue quotes. Saying so here so a reader of the PR is not looking for
a change that landed eleven days earlier under a different issue.

---

## 3. User stories

### P1 — The reader who asks why the probe cannot fail (the whole slice)

> As an engineer or an autonomous agent landing on `/health` — from `ADR-0106`'s
> "≥ 2 replicas with health checks", from the constitution's §Availability, or
> from the `catch` block itself — I want to find a decision with a reason,
> rather than a pointer to an open issue, so that I do not "fix" a property
> somebody chose.

This is the smallest independently-shippable vertical and it is also the whole
feature. It does not decompose usefully: the ADR without the comment fix leaves
the code asserting a check that does not exist; the comment fix without the ADR
leaves the only record of the decision in a `catch` block; and the test without
either locks in behaviour whose justification is still a forward-reference to an
open issue.

**No P2, no P3.** Both candidates are out of scope with reasons — see §6.

---

## 4. Acceptance scenarios

### 4.1 Happy path — the decision is findable

```gherkin
Scenario: a reader following ADR-0106's health-check clause finds the decision
  Given docs/adr/0154-readiness-is-liveness-by-choice.md exists with Status: Accepted
  When a reader greps docs/adr for "readiness"
  Then ADR-0154 is returned
  And it names ADR-0106, ADR-0153 and ADR-0118 in its Context
  And it states that /health is a liveness and routing signal only
  And it states that no check on /health may name an external dependency

Scenario: the code points at the decision instead of at an open issue
  Given src/ServiceDefaults/OutboxBacklogHealthCheck.cs' unreachable catch
  When the comment is read
  Then it cites ADR-0154
  And it does not forward to #2125 as an open question
  And no comment or string in src/ServiceDefaults claims a database or
      connection health check exists
```

### 4.2 The behaviour being locked in — characterisation, observed green

```gherkin
Scenario: a database that cannot be reached is reported Healthy
  Given an OutboxBacklogHealthCheck over a DbContext pointed at a closed port
  When CheckHealthAsync is called
  Then the result status is Healthy
  And the result carries the exception type name under the "error" key
  And no exception escapes the check
```

This test must pass **the first time it is run, before any source edit**, and
pass **unmodified** afterwards. See §8.

### 4.3 Conflict — the decision cannot be inverted by accident

```gherkin
Scenario: a future change makes the unreachable path fail readiness
  Given the characterisation test of 4.2 is on develop
  When someone changes the unreachable catch to return Unhealthy or Degraded
  Then that test fails
  And the failure names ADR-0154 in its assertion message
```

The assertion message is load-bearing: the point is not that a test went red,
it is that the person who made it go red is told they are reversing a decision
rather than fixing a bug.

### 4.4 Bad request / auth — not applicable, and why

`/health` and `/alive` take no input and carry **no authorization metadata by
design** — a kubelet presents no credentials. `DefaultEndpointsTests` already
asserts the absence of `IAuthorizeData` on both routes in both environments
(three tests). This spec adds no endpoint, no parameter and no auth surface, so
there is no bad-request or auth scenario to write. Recorded rather than omitted,
because "no auth scenario" and "nobody thought about auth" look identical in a
spec that simply has no such section.

### 4.5 The one way this comes out red, and what to do about it

If the characterisation test of §4.2 is **red on first run**, the `catch
(DbException ex) when (IsUnreachable(ex))` guard does not actually catch what a
closed port throws — i.e. the swallow never worked, `/health` *can* reach the
outer `Degraded`, and the decision record would be describing behaviour the code
does not have.

**That is a stop, not an adjustment.** Do not edit the test to match, and do not
edit the production `catch` to make it pass — the second is a behaviour change
this spec has no mandate for. Report the verbatim failure and block; it is a new
finding about the premise, and it needs its own issue.

---

## 5. What ships

### 5.1 `docs/adr/0154-readiness-is-liveness-by-choice.md` — new

**Chosen form: a new ADR, and the reasoning for that choice is itself part of
the deliverable.** Three alternatives were considered:

| Form | Rejected because |
|---|---|
| A doc comment at the check registration sites only | It is where the decision *acts*, not where a reader *looks*. Three documents already point at `#2125` by number; a comment cannot be the target of `ADR-0153`'s citation or of the constitution's. And the comment that used to carry this reasoning is precisely what went false and stood for months. |
| An amendment to `ADR-0106` | ADRs in this repository are records of a moment, superseded rather than rewritten (`_template.md:3,6`; `0039:3`, `0132:3`). ADR-0106 is about the gateway; readiness semantics are not its subject, and the sentence at issue is one bullet in its Consequences. |
| A constitution §Availability edit alone | §Availability already carries the *fact* (line 452). What is missing is the *reason* and the *scope of the refusal*, which is ADR-shaped: a decision, its context, its consequences, and the alternative that was declined. |

**This ADR implements a decision already made** — by the repository owner, in
the session that commissioned this spec, choosing option 1 of the two the issue
names. The spec does not make it. It is written because the autonomous lane may
not author decisions (ADR-0144), and this one therefore needs its authorship
recorded explicitly: see §9.2.

Its content, in outline:

- **Context** — the four outcomes of §2, the two options #2125 names, and the
  three documents currently citing an open issue.
- **Decision** —
  1. `/health` is a **liveness and routing signal**. It answers `200` unless the
     process cannot serve at all.
  2. **No check registered on `/health` may name or reach an external
     dependency.** This is both the availability argument and spec 078's
     security argument: the exposure analysis holds *because* no check name
     encodes a dependency, and `/health` is forwarded unauthenticated through
     the gateway's `/{context}/{**catch-all}` routes.
  3. A shared-dependency outage is reported through **telemetry and logs**, not
     through readiness — and ADR-0118 means that path has no destination in
     Production yet. The ADR records that as a known consequence with its own
     issue, not as something this decision discharges.
  4. What would **reverse** this: a per-replica dependency (one a single pod can
     lose while its siblings are fine), or a deployment topology where evicting
     one replica is not evicting all of them. Neither exists while ADR-0153
     holds every service at one instance.
- **Consequences** — positive and negative, including the one an operator
  actually feels: a pod that cannot serve a single request stays in rotation.
- **Alternatives Considered** — option 2 (a `ready`-tagged reachability check),
  declined, with the three costs #2125 names: it reopens spec 078's exposure
  analysis, it needs a timeout or it hangs the probe, and it drains every
  replica at once for a condition none of them caused.
- **Amends:** Constitution §Availability (supplies the record the sentence at
  line 452 forwards to; the sentence's *content* is unchanged).

### 5.2 `src/ServiceDefaults/OutboxBacklogHealthCheck.cs` — comment and one string

The `catch (DbException ex) when (IsUnreachable(ex))` block at `:128-143`.
**No control flow, no status, no guard changes.** The intended result:

```csharp
catch (DbException ex) when (IsUnreachable(ex))
{
    // ADR-0154. Readiness on this system is a liveness and routing signal, and
    // this is the path that makes that a decision rather than an omission.
    // Nothing is being deferred to: the check set is exactly two members —
    // "self", which returns Healthy unconditionally, and this one. There is no
    // connection check. The Healthy below is the refusal to fail readiness on a
    // dependency every replica shares: an unreachable database is unreachable
    // from all of them, so draining them all converts a database outage into
    // the loss of the HTTP surface an operator would use to find out why. The
    // backlog is unreadable, not bad; the signal for the outage is the log and
    // the trace, not the probe.
    return HealthCheckResult.Healthy(
        "Backlog not readable; a database outage does not fail this replica's readiness.",
        new Dictionary<string, object>(StringComparer.Ordinal) { ["error"] = ex.GetType().Name });
}
```

Two things move: the trailing *"Whether that is the right answer … is #2125"*
forward (now answered), and the description string, which still asserts the
check the comment above it denies. The description never reaches the wire — the
framework's default writer emits one word — so this is not a security-surface
change; it is a string an operator reads in a log.

### 5.3 The two registration sites get a pointer, not a paragraph

- `src/ServiceDefaults/WolverineDefaults.cs:168-172` — one line noting that the
  `Degraded` failure status is ADR-0154, so the next person adding a check has
  the constraint in front of them at the moment they would break it.
- `src/ServiceDefaults/Extensions.cs` `MapDefaultEndpoints` — the existing
  comment already says the exposure analysis is conditional on *"a check whose
  **name** encodes a dependency"* never being added. That condition is now a
  decision with a number; the sentence gains the citation and nothing else.

No comment is added to `AddDefaultHealthChecks` — `self` is not where anyone
would add a dependency check, and the house rule is no drive-by comments.

### 5.4 The three citations of an open issue

| Location | Action |
|---|---|
| `.specify/memory/constitution.md:452` — *"`/health` cannot return unhealthy (#2125)"* | **Cite ADR-0154 alongside the issue.** A **citation swap, not an amendment**: the sentence's claim is unchanged and still true. Flagged at §9.3 because ADR-0144 forbids the lane amending the constitution, and this is close enough to the line to be named rather than done quietly. |
| `docs/adr/0153-…:  "#2125 records that /health returns Healthy unconditionally"` | **Left alone.** ADR-0153's statement is true and an ADR records a moment. ADR-0154 cites 0153 in the other direction, which is how the two connect. |
| `specs/078-health-answers-in-production/spec.md:184` — *"until #2125 settles what"* | **Left alone.** A spec records what was believed when it was written; spec 164 §2 set this precedent explicitly. #2125 *is* now settled, by this spec — that is what the trail should show. |

### 5.5 One test — `tests/ServiceDefaults.Tests/OutboxBacklogHealthCheckTests.cs`

New file, one `[Fact]`, green from the first run. It drives
`OutboxBacklogHealthCheck<T>` over a `DbContext` configured against a closed
loopback port and asserts `HealthStatus.Healthy`.

**Why this one and not more.** Of the four outcomes, three are already observed:
`Healthy` live against pid 3312 (§2 row 6), `Degraded → 200` and `Unhealthy →
503` by `DefaultEndpointsTests` (§2 row 7). The unreachable path is the only
uncovered one, and it is not merely uncovered — **it is the decision**. A test
that locks the other three would be locking framework arithmetic that is already
locked.

**Why not a guard on the registration's `failureStatus`.** It would be the more
general protection — it catches a *new* check registered with the framework's
default `Unhealthy` — but there is no honest way to write it at this slice's
size. `AddWolverineForContext` cannot be called from a unit test (it needs
Postgres and RabbitMQ connection strings and calls `UseWolverine`), a test that
builds its own registration and asserts on it would be checking its own input,
and a source-scanning guard would prove the design was written down rather than
that it holds. Recorded as a known gap in §6, with its own follow-up, rather
than papered over with a test that cannot fail.

---

## 6. Explicitly out of scope

1. **Option 2 — a `ready`-tagged reachability check.** Declined by the owner.
   It reopens spec 078's exposure analysis, needs a timeout of its own, and
   drains every replica for a condition none of them caused. ADR-0154 records
   the refusal in its Alternatives section so the next reader meets it.
2. **Any change to `OutboxBacklogHealthCheck`'s logic.** The `Healthy`/
   `Degraded` split is what the chosen option calls for. Only the comment and
   the description string move.
3. **A guard against a future check registered with `failureStatus: Unhealthy`.**
   §5.5 explains why no honest version fits here. **File a follow-up issue**
   (task T007) — the shape that would work is an integration assertion over a
   booted host's `IOptions<HealthCheckServiceOptions>`, which belongs with the
   Aspire fixture, not in `ServiceDefaults.Tests`.
4. **Giving the outbox-backlog signal a destination in Production.** ADR-0118
   defers the production sink; ADR-0154 records the consequence and points at
   it. Not this spec's work.
5. **`#1015` item 3 — the public-edge posture.** `/health` is forwarded
   unauthenticated through the gateway today. That is the Ingress decision's
   call, already noted in `Extensions.cs`, and ADR-0154 does not pre-empt it.

---

## 7. Independent end-to-end test procedure

A reviewer with the branch checked out and **no knowledge of this spec**:

1. `dotnet test tests/ServiceDefaults.Tests/SmartSentinelEye.ServiceDefaults.Tests.csproj`
   — passes, including the new `OutboxBacklogHealthCheckTests`.
2. `git stash` the source changes, leaving only the new test file; run it again
   — **still passes**. That is what makes it characterisation rather than a test
   written to the new comment. Restore afterwards. *(Note: a restored file keeps
   its old timestamp and MSBuild will skip the rebuild — `dotnet build
   --no-incremental` or touch the file before re-running.)*
3. Invert the production line by hand — change the unreachable catch's
   `HealthCheckResult.Healthy` to `.Unhealthy` — and run the test again. **It
   fails, and the message names ADR-0154.** Revert. This is the counterfactual:
   without it the test's claim is unproven.
4. Against the running stack (pid 3312, already up):
   `curl -s -o /dev/null -w "%{http_code}" http://localhost:64997/health` → `200`.
   Unchanged, which is the point — nothing about the running behaviour moves.
5. `grep -rn "database check owns this" src/ tests/` → **no hits**.
6. `grep -rn "readiness" docs/adr/` → returns ADR-0154.
7. `dotnet build SmartSentinelEye.slnx -c Release` — clean, analyzers included.
   *(The stack holds the service binaries; stop it first if MSB3027 appears.)*

---

## 8. Phase 4a colour — **green characterisation**

Declared per ADR-0144. **Behaviour-preserving.** The chosen option keeps today's
runtime behaviour exactly: no status changes, no check is added or removed, no
`failureStatus` moves, no `ResponseWriter` is set, no route changes. What changes
is a comment, a log description string, an ADR, and one constitution citation.

The test of §4.2 is therefore captured **passing before** any source edit and
must pass **unmodified** afterwards. An assertion that has to be edited is
evidence the behaviour moved — block, do not adjust (§4.5).

There is **no red component**. The one candidate — the description string — is
not behaviour: it is never written to the wire (the framework's default writer
emits the aggregate word only, which `DefaultEndpointsTests` already asserts by
exact string), and no test asserts on it.

---

## 9. Declarations

### 9.1 Engineer

**`infra-engineer`** — confirmed. Everything in §5.2–§5.4 is health-check
registration, `ServiceDefaults` plumbing, ADR and constitution text: the
observability/service-defaults brief. No bounded context, no domain model, no
frontend.

**`test-writer`** writes §5.5 first and returns its verbatim output (phase 4a),
per ADR-0144's split. The engineer receives that output and may not edit it.

### 9.2 A new ADR is warranted, and whose decision it is

**Yes — ADR-0154.** §5.1 gives the form comparison. The decision it records was
**made by the repository owner** in the session commissioning this spec,
choosing option 1 of the two #2125 names. Drafting the record is therefore
**implementing a decision already made**, not making one, and ADR-0144's
prohibition on the lane authoring decisions is not engaged. The ADR's Decision
section must attribute it accordingly — an ADR that does not say who decided is
how the next reader ends up believing an agent chose it.

### 9.3 The constitution touch, named rather than done quietly

§5.4 changes `.specify/memory/constitution.md:452` from citing `#2125` to citing
ADR-0154 **as well**. The sentence's content — that `/health` cannot return
unhealthy — is unchanged and remains true. It is a citation swap.

ADR-0144 forbids the lane amending the constitution. This is named here rather
than performed silently so that a reviewer can refuse it: **if the reviewer or
orchestrator declines, T006 is dropped and the spec still ships** — the ADR is
findable by grep either way, and the constitution's forward stays pointing at
an issue that is closed rather than open. The other two citations (§5.4) are
deliberately untouched.

### 9.4 Exact files phase 4 may touch

| File | Change |
|---|---|
| `docs/adr/0154-readiness-is-liveness-by-choice.md` | **New.** |
| `src/ServiceDefaults/OutboxBacklogHealthCheck.cs` | The `catch … when (IsUnreachable(…))` block only: its comment and the description string. **No control flow.** |
| `src/ServiceDefaults/WolverineDefaults.cs` | One comment line at the `AddTypeActivatedCheck` registration (`:166-172`). **No argument changes.** |
| `src/ServiceDefaults/Extensions.cs` | One citation added to the existing `MapDefaultEndpoints` comment. **No code.** |
| `tests/ServiceDefaults.Tests/OutboxBacklogHealthCheckTests.cs` | **New.** |
| `tests/Integration.Tests/EventIngestion/OutboxBacklogIsVisibleTests.cs` | Doc comment only (`:14-21`), which quotes the string §5.2 changes. **No assertions.** |
| `.specify/memory/constitution.md` | Line 452 citation only — and only with §9.3's sign-off. |
| `specs/172-readiness-is-liveness-by-choice/*` | These artefacts. |

**Nothing else.** In particular: no `src/*/Api`, no AppHost, no Helm, no
frontend, and no other ADR file.

### 9.5 Latency budget

**N/A.** No leg of §IV is touched. Demonstrable rather than asserted:
`ConfigureOpenTelemetry` filters both probe paths out of tracing
(`Extensions.cs:138-141`), and no probe is on the `event → overlay` path.

---

## 10. Success criterion

Stated up front, verifiable, and not "it compiles":

> A reader who lands on `/health` from any of its four entry points — ADR-0106,
> the constitution, the `catch` block, or the registration — reaches ADR-0154
> and finds a decision with a reason. No comment or string in `src/` claims a
> health check that does not exist. And a change that makes the unreachable path
> fail readiness fails a test that names the decision it reverses.
