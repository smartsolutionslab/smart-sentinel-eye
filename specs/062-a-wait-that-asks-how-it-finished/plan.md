# Implementation Plan: A wait that asks how it finished

**Feature Branch**: `fix/2064-a-wait-that-asks-how-it-finished`
**Spec**: `specs/062-a-wait-that-asks-how-it-finished/spec.md`
**Issue**: #2064
**Created**: 2026-09-04

---

## The three declarations (ADR-0144)

### Declaration 1 — which engineer

**`infra-engineer`**, reviewed by **`infra-reviewer`**.

The change lives entirely in `tests/Integration.Tests/Fixtures/AspireFixture.cs`
— Aspire AppHost orchestration, `ResourceNotificationService`, resource
lifecycle and the boot sequence the whole integration suite stands on.
ADR-0144's implementation notes give the reason the reviewer role exists at all:
*"the layer every integration test stands on had no reviewer … an infra defect
does not fail like a code defect; it fails as everything failing, or as
everything passing for the wrong reason."* Both halves of that sentence describe
the risk here — a fix that fires on a healthy boot fails everything, and a fix
that never fires passes everything for the wrong reason.

Not `backend-engineer`: no bounded context, no domain, no persistence, no
messaging. Not `security-reviewer`: no trust boundary, no scope, no token, no
secret; the change ships no `src/` code at all.

### Declaration 2 — is the honest answer a new ADR?

**No. The lane may proceed.**

This was tested against ADR-0144's own boundary — *"an issue whose honest answer
is a new architectural decision is blocked"* — rather than assumed:

- **No new decision is made.** Every choice is an existing one applied:
  ADR-0103 (real Aspire fixture, no Testcontainers), ADR-0068 (fixture shape),
  ADR-0052/0053 (xUnit + Shouldly, sentence naming), ADR-0139 + constitution
  §Testing (red first), ADR-0036 (smallest change), ADR-0030/0086 (commits).
- **No constitution section is amended.** §IV is untouched (see Latency below).
  §II is untouched (see "Why §II does not bite" below).
- **No ADR is contradicted.** The change makes an existing wait ask a question
  the existing report (#2061) already asks; the rule it applies —
  *`null or 0` is success* — is #2061's, unchanged.
- **The one thing that could have needed an ADR was ruled out of scope**: a
  general "wait for running-or-terminal" policy across all twelve waits would be
  a harness-wide convention worth writing down. `spec.md` scope ruling 2 defers
  it to its own issue, where it can carry its own decision if it needs one.

### Declaration 3 — behaviour-changing or behaviour-preserving

**Behaviour-changing → phase 4a is red.**

On an input where the fixture previously proceeded (state `Finished`, exit code
non-zero) it will now throw. That is a new outcome, not a new shape for an old
outcome. ADR-0144: *"Ambiguity resolves to behaviour-changing"* — there is no
ambiguity here, but the direction is the same one.

It is **not** a refactor, so the characterisation path does not apply and no
characterisation suite is required. The one behaviour that must be *preserved*
(FR-004, the healthy boot) is held by the tier-2 healthy-path run and by the
present-null test, not by a characterisation harness.

---

## Phase 4a design — the shape the change lands in

Specified here so the engineer does not invent it at 4b and so the reviewer has
something to review the code *against*.

### The call site — `AspireFixture.cs:124–126`

Today:

```csharp
await _app.ResourceNotifications
    .WaitForResourceAsync("migrations", KnownResourceStates.Finished, cts.Token)
    .ConfigureAwait(false);
```

After:

```csharp
ResourceEvent migrations = await _app.ResourceNotifications
    .WaitForResourceAsync(
        "migrations",
        migration => migration.Snapshot.State?.Text == KnownResourceStates.Finished,
        cts.Token)
    .ConfigureAwait(false);

if (ExitedNonZero(migrations.Snapshot.ExitCode))
{
    // ... capture migrations' own log, then throw (below)
}
```

**The predicate is deliberately *not* the one in the issue.** It filters on
state only — exactly the condition the string overload already applies. The
overload is swapped for one reason: **it returns the `ResourceEvent` that
matched**, and the string overload returns only the state text. The exit code is
then read from that returned snapshot and decided on *outside* the predicate.

This is what makes the change fail **closed**. `spec.md` §"`null or 0` in a
predicate fails open" has the full argument; the short form is that
`ExitCode is null or 0` *inside* a predicate makes an unknown exit code satisfy
the wait, so the fix would silently do nothing, while a known-bad exit code
would merely postpone the same 8-minute timeout to the same instant. Reading the
code after the wait has neither property.

### The decision — one rule, one extra overload

`ExitedNonZero(string, Dictionary<string, int?>)` already exists at
`AspireFixture.cs:363` and already carries the rule, with #2061's comment
explaining it. Split the rule out rather than restate it:

```csharp
// #2061's rule, and now the only copy of it.
internal static bool ExitedNonZero(int? exitCode) => exitCode is not null and not 0;

private static bool ExitedNonZero(string name, Dictionary<string, int?> exitCodes) =>
    exitCodes.TryGetValue(name, out int? exit) && ExitedNonZero(exit);
```

The existing comment above the dictionary overload moves with the rule. FR-005
is satisfied by construction: there is one predicate, two call shapes, and
`SelectResourcesToReport` / `FormatLikelyCause` keep behaving identically —
which is what `AspireFixtureReportSelectionTests`' 18 existing tests assert, and
they must stay green **unmodified**.

**The 18 was counted, not inherited.** #2061's verification note records 13
passing in that class; phase 6 added five more and the note was not revisited.
`grep -cE "\[Fact\]|\[Theory\]"` on the file returns 18 today. Recorded because
this repository has had to correct a stale count four times, and because "the 13
existing tests still pass" would be a true-sounding sentence that checked five
fewer things than it claimed.

### The message — assembled where a test can read it

```csharp
internal static string FormatMigrationFailureMessage(int? exitCode, string migrationsLog) =>
    $"migrations exited with code {exitCode} — a non-zero exit is a failure, not a clean finish.\n" +
    $"The startup wait stopped here rather than spending the remaining budget on services that wait for it.\n" +
    $"migrations log:\n{migrationsLog}";
```

The wording of the first line is lifted from `FormatLikelyCause` on purpose:
someone who has seen a timeout report recognises the sentence. The function is
`internal static` and pure for the same reason #2061 made its three formatters
so — *"the three sections had tests; the assembly that orders them did not"*.
The ordering claim (cause first, log last) is only holdable by a test over the
assembled string.

### The throw

```csharp
Aspire.Hosting.ApplicationModel.ResourceLoggerService loggers =
    _app.Services.GetRequiredService<Aspire.Hosting.ApplicationModel.ResourceLoggerService>();

string migrationsLog = await CaptureOneResourceLogAsync(loggers, "migrations").ConfigureAwait(false);

throw new InvalidOperationException(
    FormatMigrationFailureMessage(migrations.Snapshot.ExitCode, migrationsLog));
```

Four rulings inside those four lines:

1. **`InvalidOperationException`, not `TimeoutException`** (FR-006). Nothing
   timed out. More sharply: it must not be an `OperationCanceledException`
   subtype either, or the `catch (Exception ex) when (ex is
   OperationCanceledException or TaskCanceledException)` at
   `AspireFixture.cs:182` would swallow it and re-report a 40-second failure as
   *"Aspire AppHost did not start within 8 minutes"* — the exact species of
   misleading diagnostic this family of issues exists to remove.
2. **The log is captured only on the failure branch.** A healthy boot must not
   pay `CaptureOneResourceLogAsync`'s 5-second bounded read (FR-004).
3. **`CaptureOneResourceLogAsync` is reused, not reimplemented.** It resolves
   the DCP instance id through `TryResolveResourceId` — the one resolver #2053
   and #2054 exist to keep single. A second copy would falsify that file's
   own documented claim.
4. **The message is scoped to `migrations`.** Not the whole timeout report:
   `spec.md` scope ruling 1 explains why nine `(Waiting)` sections would be
   noise reintroduced.

### Why §II does not bite

`PrimitiveBoundaryTests` now fails the build when a **domain model** exposes
primitive-typed state. `int? exitCode` here is a field of an Aspire framework
snapshot read at a framework boundary, in a test project, with no domain model
anywhere in the file. It is the same reasoning that exempts `Shared.Contracts`.
Recorded because the rule is newly enforced and someone will ask.

---

## The red test — design, and what it honestly cannot do

The brief asks for a concrete assertion and an argument about *what* is
asserted. Both follow.

### What is asserted: which wait threw, never elapsed duration

**The assertion is about identity, not duration.** A test that asserts
"initialisation finished in under N seconds" is:

- **machine-dependent** — the same code takes wildly different times on a
  Windows dev box with warm containers and a cold GitHub Linux runner;
- **asserting the wrong thing** — the saving is `StartupTimeout` minus
  time-to-death, and *time-to-death* is a property of Postgres, Keycloak and
  Docker, not of this change;
- **a flake generator on the one path that must never flake**, because a
  fixture-level failure reds 292 tests at once.

What *is* deterministic is **which wait threw and what it said**. Pre-fix the
run ends in `TimeoutException: Aspire AppHost did not start within 8 minutes`
raised from the `camera-catalog` wait; post-fix it ends in
`InvalidOperationException: migrations exited with code …` raised from the
migrations wait. Two different exception types, two different messages, one of
which names the cause. That is a binary, machine-independent discriminator.

**The duration is measured and reported, never asserted.** It is the *value*
claim and belongs in the verification note as a figure with its machine named —
which is exactly what the issue asks for ("Measure the wall-clock difference and
put the figure in the verification note").

### Tier 1 — the committed tests (`tasks.md` T002, T004)

New file `tests/Integration.Tests/Fixtures/AspireFixtureMigrationGateTests.cs`,
Docker-free, sibling to `AspireFixtureReportSelectionTests`. Five assertions:

| # | Test | Asserts |
|---|---|---|
| 1 | `A_migration_that_exited_non_zero_is_a_failure` | `ExitedNonZero(134)` is true |
| 2 | `A_migration_that_exited_cleanly_is_not_a_failure` | `ExitedNonZero(0)` is false |
| 3 | `A_migration_whose_exit_code_was_never_observed_is_not_a_failure` | `ExitedNonZero((int?)null)` is false — **written with an explicit present null**, never an absent key |
| 4 | `A_negative_exit_code_is_a_failure` | `ExitedNonZero(-532462766)` is true — the code #2061's phase 5 actually observed; kills a `> 0` mutant |
| 5 | `The_failure_message_names_the_code_before_it_shows_the_log` | `FormatMigrationFailureMessage(134, "…")` contains `"134"`, contains the log, and the code's index is **less than** the log's — the ordering claim, held over the assembled string |

Test 3 is the one that carries the whole change's mutation resistance and is the
reason the brief insists on a present null: #2061's blocker was a mutant that
dropped the null guard and named 35 innocent resources. Here the same mutant
would abort every *healthy* boot in the repository.

### Tier 2 — the runtime observation (`tasks.md` T005)

The scratch-line provocation from
`specs/061-a-boot-failure-names-its-cause/verification.md`, run **pre-fix** and
**post-fix** on the same machine. Procedure and expected outputs are in
`spec.md` §"Independent end-to-end test procedure".

### The red output, concretely

Phase 4a produces **two** red artifacts, and they are not equally strong. Both
go in the PR body; the PR must say which is which rather than let the weaker one
read as the stronger.

**Red 1 — tier 1, a compile failure.** The five tests reference members that do
not exist yet, so `dotnet test` fails at build:

```
error CS0117: 'AspireFixture' does not contain a definition for 'FormatMigrationFailureMessage'
```

That is a genuine failing output and it is what red-first looks like for a
brand-new pure function in C#. **It is weak evidence**: it proves the member is
absent, not that the behaviour is absent. Said plainly rather than dressed up.

**Red 2 — tier 2, a runtime observation, and the load-bearing one.** With the
scratch line in `src/MigrationRunner/Program.cs` and the fix *not yet applied*,
the run ends:

```
  [xUnit.net 00:08:53.82]     …A_camera_registration_reaches_the_camera_catalog_log_tail [FAIL]
System.TimeoutException : Aspire AppHost did not start within 8 minutes.
Likely cause: migrations exited with code <n> — a non-zero exit is a failure, not a clean finish.
…
Failed!  - Failed: 1, …
```

Two facts are transported verbatim from that run: **the exception type and site**
(a `TimeoutException` from the `camera-catalog` wait) and **the elapsed time**.
The first is what the fix changes; the second is what the fix is worth. Post-fix,
the same command must produce a different exception in a materially shorter
time.

**The elapsed time is the `[xUnit.net HH:MM:SS.ss]` marker on the `[FAIL]` line,
not the runner's `Duration:` line.** Phase 4a found this the expensive way: when
`InitializeAsync` throws, xUnit attributes the collection fixture's time to
neither the test nor the assembly summary, so a single `--filter`ed test reports
`Duration: 6 ms` for a run that burned nearly nine minutes. The marker read
`00:08:53.82` and `00:08:55.10` across two provocations. A passing run does not
behave this way — fixture time lands in `Duration:` there — which is exactly why
the wrong figure looks trustworthy.

### The honest limits — three, stated before phase 5 rather than after

1. **No committed test observes the wiring.** Tier 1 tests two pure functions;
   deleting the call site at `:124` would leave all five green — the precise
   defect #2061's phase 6 found in its own branch, and #2054's whole story. The
   wiring is evidenced only by tier 2, which is manual and unrepeatable in CI.
   **Not papered over with a source-scanning guard**: a test asserting that the
   file contains a call proves the design was written down, not that it works.
2. **Tier 2 needs a production-source scratch edit** that is reverted before
   commit. It is therefore an observation, not a regression test. If the fix
   later regresses, nothing in CI will notice — the honest mitigation is that
   the *symptom* (an 8-minute integration job) is loud.
3. **`CaptureOneResourceLogAsync` against an already-exited resource is proven
   on Windows only.** #2061's phase 5 §"What was not covered" item 1: locally
   every selected resource returned logs; in CI those same resources returned
   nothing, and that Linux behaviour is still unobserved. FR-003 therefore
   requires the message to stand up when the log is `(no logs captured)` — the
   exit code, not the log, is load-bearing.

---

## Architecture

### Bounded context and layers

**None.** This change lives in `tests/Integration.Tests/Fixtures/` and ships no
`src/` code. There is no Domain, Application, Infrastructure or Api layer
involved, no aggregate, no repository, no handler.

Recorded rather than omitted because the plan template asks, and because a
"bounded context: N/A" that is *reasoned* is different from one that is
forgotten.

### Entities, value objects, invariants

None. The two invariants the change does carry are expressed as a pure
predicate, not as a type:

- **`null or 0` is not a failure.** An unobserved exit code is unknown, not bad
  (#2061; test 3).
- **Anything else is a failure**, including negative codes (test 4).

A value object for "exit code" was considered and rejected: constitution §II
binds domain models, this is a framework boundary in a test project, and
introducing `ExitCode.From(...)` here would be exactly the speculative
generality ADR-0036 forbids.

### Messaging

`N/A` — no domain event, no integration event, no `Shared.Contracts` change,
no RabbitMQ.

### Boundary rules

- No cross-context project reference is added or touched; NetArchTest's rules
  bind `src/` and are unaffected.
- `tests/Integration.Tests` already references `AppHost` (with
  `IsAspireProjectResource="false"`) and `Aspire.Hosting.Testing`. **No new
  package reference and no new project reference.**
- The new members are `internal static` on the existing `AspireFixture` partial,
  visible to the test assembly because they are in it. No `InternalsVisibleTo`
  is needed or added.

### Files touched

| File | Change | Contention |
|---|---|---|
| `tests/Integration.Tests/Fixtures/AspireFixture.cs` | the call site, the `int?` overload, the new formatter | the only shared file; every task that touches it is serial |
| `tests/Integration.Tests/Fixtures/AspireFixtureMigrationGateTests.cs` | new | disjoint |
| `specs/062-…/verification.md` | new, phase 5 | disjoint |

`AspireFixture.cs` is 831 lines against ADR-0084's 300 and trips nothing by
configuration — `Directory.Build.props` scopes those limits to non-test
projects. This change adds roughly 20 lines. **No split is planned**; the brief
excludes it and it is a separate concern.

---

## Constitution and ADR alignment

| Rule | How this complies |
|---|---|
| ADR-0037 phases | 1–3 here; 4a red before 4b; 5 measures; 6 reviews; 7 PRs to `develop` |
| ADR-0144 lane | `agent:ready`, no ADR needed, behaviour-changing → red |
| ADR-0139 / §Testing | new behaviour observed failing, failure quoted in the PR |
| ADR-0036 | one call site, one overload, one formatter; the eleven-wait sweep deferred |
| ADR-0103 | the real Aspire fixture stays the harness; nothing moves to Testcontainers |
| ADR-0105 | no argument guard is added — the new members are `internal static` helpers on a private path with no external caller; `Ensure.That` on an `int?` that is *allowed* to be null would be wrong |
| ADR-0049 | `CancellationToken` last; no `ConfigureAwait` added beyond the file's existing (pre-ADR-0049) style, which is left alone rather than churned |
| ADR-0053 | sentence-style test names with underscores |
| ADR-0084 | +~20 lines in a file the limits do not bind; no method over 30 lines |
| ADR-0030 / ADR-0086 | Conventional Commits, **no `Co-Authored-By`** |
| §IV latency | `N/A` — see below |
| §II primitives | not engaged; framework boundary, test project, no domain model |

### Latency budget impact (constitution §IV)

**`N/A — test-harness startup diagnostic.`** It runs only inside
`AspireFixture.InitializeAsync`, only when the migration runner has already
exited non-zero, and ships no runtime code. It touches none of the six legs.

---

## Risks

**R1 — the exit code lags the state, making the fix a silent no-op.**
*Likelihood: low.* `spec.md` Finding C shows Aspire never separates them; what
cannot be inspected from here is DCP's own Go-side publishing. **Detection: tier
2's post-fix run.** If it still takes ~8 minutes, the code lagged.
**Contingency, named now so it is not invented at 4b:** wait on
`Finished && ExitCode is not null` under a *subordinate* token —
`CancellationTokenSource.CreateLinkedTokenSource(cts.Token)` with
`CancelAfter(30s)` — and on subordinate-only expiry fall through to today's
behaviour rather than throwing. That is fail-safe (it can never hang a healthy
boot) and bounded. **It is not built now**, because Finding C says it is
unnecessary and ADR-0036 forbids building for a need that does not exist.

**R2 — a throw from `InitializeAsync` skips `DisposeAsync`.** xUnit 2.9.3 does
not invoke `DisposeAsync` on a fixture whose `InitializeAsync` threw, so the
`DistributedApplication` is not explicitly disposed. **Pre-existing** — today's
`TimeoutException` has the identical shape — and under `isE2ETests` the
containers are ephemeral and die with the test process. Named so the reviewer
does not read it as new; fixing it is a different issue.

**R3 — the healthy path regresses.** The worst outcome available: a mutant or a
mistake that treats null as failure would abort *every* boot in the repository.
Held by tier-1 test 3 (present null) and tier-2 step 4
(`LogTailDeliversIntegrationTests` green after the scratch line is reverted).

**R4 — the red count in CI does not change.** A fast failure still reds all 292
collection members, just sooner. Recorded because a reviewer comparing failure
counts pre/post will see no difference and could read that as "nothing
happened". The difference is in the clock and in the exception, not the count.

---

## What is explicitly not being built

- The eleven `Running` waits (`spec.md` scope ruling 2 — its own issue).
- A "make migrations fail" switch in `src/MigrationRunner`.
- A source-scanning guard that the call site exists.
- Any split of `AspireFixture.cs`.
- Any change to `FormatTimeoutMessage`, `FormatLikelyCause`,
  `SelectResourcesToReport` or `FormatFailedResourceReport` behaviour. The only
  edit near them is factoring `ExitedNonZero`'s rule into an overload, which
  their 18 existing tests must keep passing **unmodified**.
