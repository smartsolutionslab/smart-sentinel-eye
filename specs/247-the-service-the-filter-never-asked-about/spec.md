# Spec 247 — The service the filter never asked about

**Issue:** [#2469](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2469)
— *"The outbox-commit guard's candidate filter cannot see
StreamFabAttributionService's direct commit"*. Labels `tech-debt`, `agent:ready`.
On Project #13, status **In Progress** — verified 2026-09-25 by `content.url`
over `gh project item-list 13 --owner smartsolutionslab --limit 2000`. No
`item-add` needed.

**Spec number.** `origin/develop`'s highest is 237. Unmerged remote branches
claim 238–245; the main worktree (`fix/2451-mqtt-fake-thread-safety`) holds an
uncommitted `specs/246-the-count-that-outruns-its-credential`. This spec is
**247**. Re-check before the PR is opened — 246 is not on any remote yet and a
number claimed only on disk is exactly how two specs collide.

**File contention: none found.** No open PR or remote branch touches
`tests/Architecture.Tests/OutboxCommitTests.cs`,
`tests/Architecture.Tests/Persistence/OutboxCommitProbe.cs` or
`tests/StreamDistribution.Infrastructure.Tests/Attribution/StreamFabAttributionTests.cs`.
**#2470** (the IL scan's other escapes) will edit the same guard file; whichever
lands second rebases.

**ADRs referenced:**

- **ADR-0088** (Postgres outbox, eager transactions) — why the rule exists.
- **ADR-0139** (rules fail the build; new behaviour observed red first) and
  constitution §Testing as it amends it.
- **ADR-0144** (autonomous lane; phase-4a colours) — §6 picks red.
- **ADR-0036** (smallest change; surface assumptions).
- **ADR-0037** (phases and gates). **ADR-0109** (`[P]` rule).
- **ADR-0052 / 0053 / 0054** (xUnit + Shouldly; sentence-style names;
  hand-written builders).

**No new ADR.** This scopes a build-time test guard. No production assembly,
contract, constitution clause or runtime behaviour changes. ADR-0088 already
names the outbox as the commit seam; this decides which types the instrument
that enforces it looks at, and records the one type it knowingly lets through.

**Latency budget (§IV): N/A.** Every changed file is under `tests/`. No leg of
`event arrival → overlay rendered` is touched.

---

## 1. The issue's premise, re-checked on this tree (`a54b11d0`, 2026-09-25)

| Claim | Found |
|---|---|
| The candidate filter admits only `.Persistence` namespaces and `…Repository` names | **True.** `OutboxCommitTests.cs:108-113` (`Offenders`) |
| `StreamFabAttributionService` calls `SaveChangesAsync` directly | **True.** `StreamFabAttributionService.cs:81` — `await context.SaveChangesAsync(cancellationToken)` |
| It is invisible to the guard | **True.** Namespace `…Infrastructure.Attribution`, name ends `Service` |
| `Stream.AttributeToFab` raises no domain event | **True.** `Stream.cs:117-128` guards, sets `Fab`, returns. No `Raise` |

### 1.1 How many types would a wider filter catch? Measured, not grepped

Source: `grep -rn "\.SaveChanges\(Async\)\?(" src --include=*.cs` returns one
line — `StreamFabAttributionService.cs:81`. No `DbContext` subclass exists
outside Infrastructure/ServiceDefaults (`grep ": DbContext"` over
`src/*/{Application,Api,Domain}` returns nothing), so Application code, which
does reference EF Core, has no context to commit.

IL, which is what the guard actually reads: on 2026-09-25 the candidate filter
was replaced in a scratch edit by `.Where(type => !type.IsNested)` (every
top-level type in the nine assemblies) and the guard run —

```
Failed ...No_repository_commits_without_its_announcements(assemblyName: "SmartSentinelEye.StreamDistribution.Infrastructure")
   Shouldly.ShouldAssertException : offenders
["SmartSentinelEye.StreamDistribution.Infrastructure.Attribution.StreamFabAttributionService"]
Failed!  - Failed:     1, Passed:     9, Skipped:     0, Total:    10
```

Eight other assemblies green; the spec-193 probe fact green (no other type in
the test assembly trips the wider filter). The scratch edit was reverted.
(Built with `--artifacts-path` into the scratchpad: a running AppHost in this
worktree holds `src/*/Api/bin` — see "stop the stack before building".)

**So widening is one finding, not a triage exercise.** That is what makes the
decision below small enough to belong in this issue.

### 1.2 Why this service cannot simply use the seam

Three facts, of different strength:

1. **The seam commits a different context.** `ITransactionalCommit` is
   `OutboxTransactionalCommit<TDbContext>(IDbContextOutbox<TDbContext>)`
   registered scoped (`WolverineDefaults.cs:150`); it saves the *scoped*
   `StreamDistributionDbContext`. The service creates its own context from
   `IDbContextFactory` (`StreamFabAttributionService.cs:57-62`). Routing it
   through the seam means rewriting how it loads and tracks streams, not
   swapping one call. **Verified by reading.**
2. **It runs before Wolverine does.** `AddHostedService<StreamFabAttributionService>()`
   (`StreamDistributionInfrastructureModule.cs:108`) is registered before
   `AddWolverineForContext` → `UseWolverine` (`:110`, `WolverineDefaults.cs:68`),
   whose startup also builds the outbox storage
   (`AutoBuildMessageStorageOnStartup`, `WolverineDefaults.cs:84`). Hosted
   services start in registration order. **Registration order verified; what
   the outbox does when flushed before its runtime starts was not tested** —
   recorded as a risk of the rejected option, not relied on.
3. **Complying would not protect anything.** In this codebase an integration
   event reaches the outbox by a repository draining the aggregate's
   `PendingEvents` into `IDomainEventDispatcher` before it commits
   (`StreamRepository.cs:38-62`). The service holds no dispatcher and does not
   go through `IStreamRepository`. If `AttributeToFab` ever raised an event,
   the event would be lost **whichever method the service committed with** —
   because nothing drains it. Swapping `SaveChangesAsync` for
   `ITransactionalCommit.CommitAsync` would turn the guard green and leave the
   real hazard exactly where it is.

Point 3 is the one that decides it: the guard's check (which commit method)
is a proxy for the property that matters (are the aggregate's events
announced). For every repository the proxy is faithful. For this service it is
not, and forcing compliance would make the proxy lie.

---

## 2. The decision

**Both halves of the issue's question have an answer, and they are not
alternatives.**

**(a) The candidate filter is wrongly scoped — widen it.** The filter is a
proxy for "code that commits": it assumes commits happen in repositories. The
realistic way the defect returns is not a new repository (they are copied from
existing ones, all of which use the seam) but a new hosted service, sweep or
backfill that takes `IDbContextFactory<T>` — which **every one of the nine
contexts registers** (`AddDbContextFactory`, nine persistence modules) — and
calls `SaveChangesAsync` on it, precisely as this service does. A name-based
filter is blind to that shape by construction. New filter: **every top-level
type** in the nine assemblies (`!type.IsNested`; nested types, including the
async state machines, are already walked through their declaring type by
`BodiesOf`, so admitting them as candidates would only double-report).

**(b) `StreamFabAttributionService` is a legitimate exception — record it, by
type, with its reason under test.** It is the guard's first and only exemption.
The class doc's argument against exemption lists is that *"the next repository
added by copying an exempt one inherits the exemption without the reason"*.
The design answers that argument rather than overriding it:

| The rot the doc warns about | Why it cannot happen here |
|---|---|
| A copy inherits the exemption | The entry is `typeof(StreamFabAttributionService)`. A copied class is a different type and is reported |
| The exemption outlives its reason | The reason — the pass raises no event — is asserted in the service's own test file. Add an event to `AttributeToFab` and that test fails, naming the guard |
| The exemption outlives the offence | A second fact asserts every permitted type **still** commits directly. Route the service through the seam later and the stale entry fails the build |
| The list grows quietly | It is one element, named in the class doc; the failure message for any new offender still says "use `ITransactionalCommit`", not "add yourself to the list" |

**Rejected: route the service through the seam** (the `DeadLetterRepository`
precedent). §1.2: it is a production change to a startup path to satisfy a
test, it carries an untested ordering risk, and — decisively — it would not
protect the property the guard exists for. If `AttributeToFab` ever does raise
an event, the right fix is to go through `IStreamRepository.SaveAsync`, and the
reason-pin test's failure message says exactly that.

**Rejected: keep the filter, document the exception in prose.** Leaves every
future factory-context commit invisible, which is the actual regression path.

**Rejected: filter by capability** (only types holding `IDomainEventDispatcher`
or a bus are candidates). Backwards: a type that mutates an event-raising
aggregate *without* a dispatcher is the most dangerous shape, not a safe one.

---

## 3. User stories

### US1 (P1) — a direct commit is caught wherever it is written

**As** the engineer adding the next background sweep that takes
`IDbContextFactory<T>`, **I want** the outbox guard to fail on my
`SaveChangesAsync` whatever I named the class and whatever namespace it is in,
**so that** the rule does not depend on my following a naming convention it
never states.

### US2 (P1, same slice) — the one exception is recorded and cannot rot

**As** the next reader of `OutboxCommitTests`, **I want** the one type allowed
to commit directly named in the guard, with its reason held by a test that
fails when the reason stops being true, **so that** the exemption is a decision
I can read, not a gap I have to rediscover.

US1 alone would turn `develop` red; US2 alone has nothing to exempt from. They
ship as one slice.

---

## 4. Acceptance scenarios

```gherkin
Feature: the outbox guard's candidates are every type, with one recorded exception

  Scenario: a direct commit outside a repository is caught (the gap)
    Given a probe type in the test assembly, in a namespace without ".Persistence"
      And whose name does not end in "Repository"
      And whose async method awaits dbContext.SaveChangesAsync(ct)
     When the detector is run over the test assembly
     Then that type is reported alongside the two spec-193 offenders
      And the seam and failure-subscription controls are still not reported

  Scenario: the real corpus reports the known service, and only it
    Given the nine Infrastructure assemblies on develop
     When the widened filter is applied without the exception
     Then exactly StreamFabAttributionService is reported

  Scenario: the recorded exception keeps the corpus green
    Given StreamFabAttributionService is the guard's permitted direct commit
     When the guard runs over the nine assemblies
     Then no offender is reported

  Scenario: a copy of the exempt service is not exempt (conflict)
    Given a new type that commits directly, whatever its name
     When the guard runs
     Then it is reported, because the exception is keyed by type, not by shape

  Scenario: a stale exception fails (conflict)
    Given a permitted type that no longer calls SaveChanges or SaveChangesAsync
     When the guard runs
     Then it fails, naming the entry to remove

  Scenario: the exception's reason is pinned (bad request to the premise)
    Given unattributed streams with no pending events
     When StreamFabAttributionService.Attribute gives them their fabs
     Then no stream has a pending event
      And if one ever does, the failure says the pass must go through
          IStreamRepository.SaveAsync and the guard's exception must be removed
```

**Auth:** none — a reflection-only build-time guard with no HTTP surface, scope
or caller. Recorded so the absence is a decision.

---

## 5. Independent end-to-end test procedure

1. On unmodified `develop`: `dotnet test tests/Architecture.Tests --filter
   "FullyQualifiedName~OutboxCommitTests"` — green (10). Record.
2. After the test commit (probe + expectation): same command — the probe fact
   is **red**, reporting two offenders where three are expected. Record verbatim.
3. After the fix commit: same command — green. Then the whole
   `Architecture.Tests` project (other guards walk the test assembly and the
   probe adds a type to it), and `tests/StreamDistribution.Infrastructure.Tests`.
4. **Corpus counterfactual:** delete the exception entry → the StreamDistribution
   theory case is red naming `StreamFabAttributionService` (matches §1.1). Restore.
5. **Stale-entry counterfactual:** add `typeof(StreamRepository)` to the
   permitted list → the stale-entry fact is red naming it. Restore.
6. **Reason counterfactual:** add a `Raise(...)` of any existing Stream domain
   event to `AttributeToFab` → the reason-pin test is red. Restore; `git diff
   src/` empty.
7. Write every output verbatim into `verification.md`.

If a stack is running from this worktree, build with `--artifacts-path`
outside the repo (MSB3027 otherwise), and touch any file restored by
`git checkout --` before re-running (a restored file keeps its old mtime).

---

## 6. Phase-4a colour: **RED**

**Behaviour-changing**: the guard's detection set grows — a type previously
never considered is now reported. The probe fact is extended first and observed
failing (it sees two offenders, expects three). ADR-0144: ambiguity resolves to
red, and this is not ambiguous.

**One test is green by design and says so:** the reason-pin test in
`StreamFabAttributionTests` characterises a premise that is true today (§1). It
is proved able to fail by §5 step 6, not by arriving red. The stale-entry fact
is the engineer's (it references the permitted list the fix introduces) and is
proved by §5 step 5.

The existing spec-193 expectations (two offenders, two controls unreported)
must hold unmodified apart from the one added row.

---

## 7. Out of scope

| What | Where |
|---|---|
| Other ways out of the seam (`ExecuteSql*`, `ldftn`, interface-typed context, nested-of-nested types) | #2470 |
| `ITransactionalCommit.cs:8` doc comment | #2471 |
| Adding `ServiceDefaults` or Application assemblies to the scanned set | No `DbContext` is reachable there that commits (§1.1); not a gap today |
| Any change to `StreamFabAttributionService` or `Stream` | Deliberately none — §2 |
