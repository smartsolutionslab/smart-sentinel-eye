# Spec 193 — Both spellings of an absolute rule

**Issue:** [#2292](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2292)
— *"`OutboxCommitTests` catches only the async spelling of the defect it calls
absolute"*. Labels `bug`, `tech-debt`, `agent:ready`, no `agent:blocked`.
**Already on Project #13**, status **Todo** — verified 2026-09-20 with
`gh project item-list 13 --owner smartsolutionslab --limit 2000`, which returned
`2292 Todo OutboxCommitTests catches only the async spelling of the defect it
calls absolute`. **No `item-add` needed.** (`--limit 2000`, not the default 30:
a filled board reads as empty otherwise.)

**Spec number.** `git ls-tree -d origin/develop -- specs/` shows **191**
(`191-one-source-of-the-admin-credentials`) as the highest merged. Every remote
branch was swept after `git fetch --prune`: `192-a-form-the-masker-cannot-read`
is claimed by open PR #2468 (branch `2278-masker-assumptions-guard`); nothing
claims 193. This spec is **193**. The number one above develop's highest is not
automatically free — it has collided before — so it was checked rather than
assumed, and it is worth re-checking before the PR is opened.

**File contention: none.** The only open PR is #2468, whose files are
`specs/192-…/*` and `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`.
No open branch touches `tests/Architecture.Tests/OutboxCommitTests.cs`, and the
new probe file does not exist anywhere yet. Nothing under `src/` changes, so the
157 files declaring a `.Persistence` namespace are read, never written.

**ADRs referenced:**

- **ADR-0088** (per-module Wolverine queue isolation, eager transactions,
  **Postgres outbox**) — the reason the rule exists at all. A repository that
  commits outside `ITransactionalCommit` lands its rows without the integration
  events they announce.
- **ADR-0139** (rules that fail the build, not the review; new behaviour is
  observed red first) and constitution §Testing as that ADR amends it.
- **ADR-0144** (the autonomous lane and its two phase-4a colours). §6 picks
  **red** and says why the alternative was considered and rejected.
- **ADR-0036** (smallest possible change; a bug fix changes the bug and nothing
  else; surface assumptions rather than burying them).
- **ADR-0037** (the seven phases and their gates).
- **ADR-0109** (the `[P]` disjoint-file rule — `tasks.md` §4).
- **ADR-0052 / 0053 / 0054** (xUnit + Shouldly; sentence-style test names with
  underscores; hand-written fixtures, no AutoFixture).
- **ADR-0084** (code metrics advisory, 300 LOC/file — `plan.md` §5).

**No new ADR is needed.** This decides nothing architectural. ADR-0088 already
says the outbox is the commit seam, and the class doc already claims the rule is
absolute; this makes the instrument match the claim. No rule changes, no pinned
count changes, no production assembly changes.

**Latency budget (§IV): N/A.** The whole diff lands under
`tests/Architecture.Tests/`. None of the six legs of `event arrival → overlay
rendered` is touched and no leg's measurement status moves.

---

## 1. Verifying the issue's premise on the current tree

The issue was found during the **2026-09-13 whole-project backend review** and
this repository has moved since — spec 190 consolidated six guards, spec 192 is
in flight. Per *"verify the issue premise before planning"*, every claim below
was re-checked against this worktree's tip (`ca4788c8`) on 2026-09-20 rather
than taken from the issue.

### 1.1 The guard is still shaped the way the issue describes

| The issue says | What is there now |
|---|---|
| `tests/Architecture.Tests/OutboxCommitTests.cs:124` matches `nameof(DbContext.SaveChangesAsync)` only | **Still true.** `OutboxCommitTests.cs:124` reads `if (called?.Name == nameof(DbContext.SaveChangesAsync)` |
| `CallsSaveChangesDirectly` / `ReferencesSaveChanges` | **Still the method names** — `OutboxCommitTests.cs:84` and `:103`. Neither moved to a shared reader in spec 190 |
| the class doc calls itself *"deliberately a rule with no exemption list… absolute"* | **Still true** — `OutboxCommitTests.cs:16-18` |

The matching is **exact string equality against one name**, not a regex and not
a substring test. `"SaveChanges" == "SaveChangesAsync"` is false, so the
synchronous commit cannot be reached by this code under any input. That is the
defect, and it is a property of the comparison rather than of any corpus.

### 1.2 The fixture types do **not** exist in the tree

`AsyncOffenderRepository` and `SyncOffenderRepository` appear **nowhere** under
`tests/` or `src/` — `grep -rn "OffenderRepository" --include=*.cs` returns
nothing. The issue's counterfactual was run at review time against a planted
pair and then reverted; the tree carries no record of it. **It has to be
reconstructed**, and §3.3 makes reconstructing it part of the fix rather than a
step towards it.

This is the same meta-defect the file already documents about itself: the doc
comment at `OutboxCommitTests.cs:74-82` records that the *first* version of this
rule scanned only declared methods and therefore missed every
`await dbContext.SaveChangesAsync(ct)` — found by "a repository deliberately
broken to fail it" (`OutboxCommitTests.cs:122-123`) that is likewise not in the
tree. **The guard has now been wrong twice in the same way, and both times the
proof was thrown away.**

### 1.3 The counterfactual reproduces — verbatim

Run on 2026-09-20 against a verbatim transcription of `CallsSaveChangesDirectly`
/ `BodiesOf` / `ReferencesSaveChanges` from this worktree's
`OutboxCommitTests.cs`, compiled against `Microsoft.EntityFrameworkCore` 10.0.11
(the version `Directory.Packages.props:50` pins), with the issue's two
repositories in a `.Persistence` namespace:

```
candidates considered: AsyncOffenderRepository, SyncOffenderRepository
   [current] hit: DbContext.SaveChangesAsync
CURRENT: offenders=1 — AsyncOffenderRepository
   [widened] hit: DbContext.SaveChangesAsync
   [widened] hit: DbContext.SaveChanges
WIDENED: offenders=2 — AsyncOffenderRepository, SyncOffenderRepository
```

`offenders=1` where it should be `2`, matching the issue's transcription line
for line. The `WIDENED` rows are the same run with §3.1's one-line change
applied — so the fix is known to work before a task is written, not hoped to.

### 1.4 Nothing in `src/` is currently hiding behind the gap

`grep -rn "SaveChanges" --include=*.cs src/` returns four hits, and none of them
is a repository committing synchronously:

| Hit | Why it is not an offender |
|---|---|
| `src/ServiceDefaults/OutboxTransactionalCommit.cs:31` — `outbox.SaveChangesAndFlushMessagesAsync(…)` | The sanctioned seam itself. Wolverine extension method; declaring type is not a `DbContext` |
| `src/ServiceDefaults/Persistence/AggregateVersionInterceptor.cs:32` — `: SaveChangesInterceptor` | A base type name, not a call |
| `src/Shared.CQRS/ITransactionalCommit.cs:8` | A doc comment |
| `src/StreamDistribution/Infrastructure/Attribution/StreamFabAttributionService.cs:81` — `await context.SaveChangesAsync(ct)` | A `…Service`, not a `…Repository`, so outside the theory's candidate filter today and after the change alike |

**So widening the match finds no existing offender.** That is the honest finding
and it is worth writing down: the value of this change is entirely prospective —
it closes the second of the two ways the defect comes back, it does not repair
damage already done. A reader who expects the widened guard to go red against
`develop` should expect green.

---

## 2. Prioritized user stories

### US1 (P1) — the rule sees both spellings of the commit it forbids

**As** the engineer who adds the next repository by copying an EF tutorial,
**I want** the outbox guard to fail the build whether I wrote
`dbContext.SaveChanges()` or `await dbContext.SaveChangesAsync(ct)`,
**so that** the rule the class calls absolute is absolute in the instrument and
not only in the prose.

This is the whole slice. It ships independently, is observable end to end by
§5's procedure, and nothing else in the repository is blocked on it.

### Out of scope, deliberately

- **Other missing spellings.** `Database.ExecuteSql*`, a raw `NpgsqlCommand`, or
  a `DbContext` reached through an interface variable are all ways to commit
  outside the seam that this IL scan does not catch. They are a different
  question about the *call* surface, not about this comparison. Recorded as a
  follow-up in §7, not folded in (ADR-0036: smallest change).
- **The candidate filter** (`.Persistence` namespace + `Repository` suffix).
  `StreamFabAttributionService` (§1.4) shows the filter has its own hole. Same
  reasoning: a separate defect, a separate issue.
- **`src/Shared.CQRS/ITransactionalCommit.cs:8`'s doc comment**, which also says
  only `SaveChangesAsync`. Comment-only, in a production assembly, and it would
  carry its own phase-4a obligation. §7.

---

## 3. What changes

Three things, in one existing file plus one new file, all under `tests/`.

### 3.1 The comparison — one line

`OutboxCommitTests.cs:124`:

```csharp
// before
if (called?.Name == nameof(DbContext.SaveChangesAsync)
    && typeof(DbContext).IsAssignableFrom(called.DeclaringType))

// after
if (called?.Name is nameof(DbContext.SaveChanges) or nameof(DbContext.SaveChangesAsync)
    && typeof(DbContext).IsAssignableFrom(called.DeclaringType))
```

**Exact set membership, not a prefix or a substring test.** That choice is the
one real subtlety in this spec and §3.2 is why.

Two mechanical facts, both checked rather than assumed:

- `nameof(DbContext.SaveChanges)` compiles even though `SaveChanges` is
  overloaded (`SaveChanges()` and `SaveChanges(bool)`); `nameof` over a method
  group does not require a unique overload. Both overloads share the name, so
  both are matched, exactly as both `SaveChangesAsync` overloads already are.
- The `called.DeclaringType` on the next line still needs no `!`. NRT null-state
  analysis narrows `called` through `called?.Name is <non-null constant
  pattern>` just as it did through `== <non-null constant>`. Compiled against
  `net10.0` with `<Nullable>enable</Nullable>` and
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`: **0 warnings, 0
  errors.** Worth stating because the obvious defensive edit — hoisting the test
  into a local `bool` — *does* lose the narrowing and then needs a `!`, which is
  a worse line than the one it replaces.

### 3.2 Why not `StartsWith("SaveChanges")` or `Contains("SaveChanges")`

Because `SaveChangesAsync` contains `SaveChanges`, a prefix or substring test
looks like the tidier widening. It is the wrong one, and this repository
contains live counter-examples in both directions:

| Name | Declaring type | `Contains("SaveChanges")` | Should it be an offence? |
|---|---|---|---|
| `SaveChanges` | `DbContext` | yes | **yes** |
| `SaveChangesAsync` | `DbContext` | yes | **yes** |
| `add_SaveChangesFailed` / `remove_SaveChangesFailed` | **`DbContext`** | **yes** | **no** — subscribing to the failure event commits nothing |
| `SaveChangesAndFlushMessagesAsync` | Wolverine's outbox extensions | yes | **no** — it *is* the sanctioned seam (`OutboxTransactionalCommit.cs:31`) |

The two rejection rows were enumerated by reflection over
`typeof(DbContext).GetMembers(…)`, not reasoned about:

```
DbContext member: add_SaveChangesFailed
DbContext member: remove_SaveChangesFailed
DbContext member: SaveChanges
DbContext member: SaveChangesAsync
DbContext member: SavedChanges
DbContext member: SavingChanges
DbContext member: SaveChangesFailed
```

`add_SaveChangesFailed` is the dangerous one: its declaring type **is**
`DbContext`, so the existing `IsAssignableFrom` guard does **not** rescue a
substring match from it. A repository that did nothing but subscribe to
`SaveChangesFailed` for logging would be reported as committing outside the
outbox — a false accusation from a rule whose whole authority is that it has no
exemptions and therefore never needs to be argued with.

`SaveChangesAndFlushMessagesAsync` is rescued by the declaring-type check today,
but only incidentally. A guard whose correctness rests on a second predicate
catching what the first one got wrong is one refactor from being wrong.

**Exact membership in a two-element set has neither hazard**, needs no
double-counting care (the two names are disjoint strings, and the loop returns
on the first hit regardless), and reads as what the rule means.

### 3.3 The probe — the proof stops being thrown away

A new file, `tests/Architecture.Tests/OutboxCommitProbe.cs`, holding the issue's
counterfactual as permanent code: a `.Persistence` namespace inside the **test**
assembly with `AsyncOffenderRepository`, `SyncOffenderRepository`, and a
compliant control. A new companion `[Fact]` on `OutboxCommitTests` runs the
detector against those three and asserts it names **both** offenders and **not**
the control.

The probe cannot turn the real theory red: `Assemblies()` yields nine named
`…Infrastructure` assemblies and the test assembly is not among them. This is
exactly the arrangement spec 184 built for `PrimitiveBoundaryTests` —
`PrimitiveBoundaryProbe.cs` holds deliberate §II violations that the main walk
never sees, and `The_walk_sees_a_primitive_reached_through_a_collection_or_array`
asserts an **exact** offender list against
`typeof(PrimitiveBoundaryTests).Assembly`. Mirror it rather than invent
something (`CLAUDE.md`: prefer reusing existing patterns).

**Why a permanent probe and not a transient counterfactual.** §1.2: this guard
has been wrong twice in the same way, and on both occasions the evidence was
planted, observed, and deleted — so the second defect could not have been found
by reading the tree, only by reviewing the file afresh. A probe that stays turns
"the rule is absolute" from a sentence in a doc comment into an assertion. It
also gives the next widening (§7) somewhere to add a row instead of starting
over.

The control matters as much as the offenders: without it the companion test
cannot distinguish a detector that is right from one that returns everything it
is handed. *An assertion must not check its own input.*

### 3.4 The failure message and the doc comments

`OutboxCommitTests.cs:49` tells an offender it *"calls SaveChangesAsync
directly"* — which, after §3.1, can be false of the repository it is printed
for. It must name both spellings. Likewise the class doc (`:7`) and the
`ReferencesSaveChanges` doc (`:70`), whose `<see cref>` points at
`DbContext.SaveChangesAsync(CancellationToken)` alone.

Not cosmetic: a build failure that misnames what the reader wrote is how the
reader concludes the guard is broken and reaches for a suppression.

---

## 4. Acceptance scenarios (Gherkin)

```gherkin
Feature: the outbox guard sees both spellings of a direct commit

  Scenario: the synchronous commit is caught — the gap this closes
    Given a type named SyncOffenderRepository in a .Persistence namespace
      And its Save() body calls dbContext.SaveChanges()
     When the detector is run over it
     Then it is reported as an offender

  Scenario: the asynchronous commit is still caught — no regression
    Given a type named AsyncOffenderRepository in a .Persistence namespace
      And its SaveAsync(ct) body awaits dbContext.SaveChangesAsync(ct)
     When the detector is run over it
     Then it is reported as an offender
      And it is reported from the async state machine's MoveNext, not the
          declared method, exactly as before this change

  Scenario: the sanctioned seam is not an offence — the substring conflict
    Given a repository that commits only through ITransactionalCommit
      And that seam calls SaveChangesAndFlushMessagesAsync
     When the detector is run over it
     Then it is not reported

  Scenario: subscribing to the failure event is not an offence — the
            DbContext-declared substring conflict
    Given a repository whose constructor does dbContext.SaveChangesFailed += …
      And which never calls SaveChanges or SaveChangesAsync
     When the detector is run over it
     Then it is not reported
      And the reason is exact name membership, not the declaring-type check,
          because add_SaveChangesFailed is declared on DbContext itself

  Scenario: the real assemblies stay clean — the no-collateral case
    Given the nine Infrastructure assemblies as they are on develop
     When No_repository_commits_without_its_announcements runs for each
     Then no offender is reported
      And the probe types are not among the candidates considered, because the
          test assembly is not in the Assemblies() list

  Scenario: the message names what the reader actually wrote
    Given a repository reported for a synchronous commit
     When the assertion fails
     Then the message names SaveChanges and SaveChangesAsync, and directs the
          reader to ITransactionalCommit
```

There is no auth or authorization scenario: the change is a reflection-only
build-time guard with no HTTP surface, no scope and no caller. Recorded
explicitly so its absence is a decision rather than an omission.

---

## 5. Independent end-to-end test procedure

Not "the suite is green". The observation is that the guard **fails on a
synchronous commit in a real Infrastructure assembly**, which today it does not.

1. On the unmodified tree, run
   `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~OutboxCommitTests"`.
   Record green (nine theory cases).
2. Plant a synchronous offender in a real Infrastructure assembly — add to an
   existing repository under `src/CameraCatalog/Infrastructure/**/Persistence/`
   a method whose body is `dbContext.SaveChanges();`. Re-run step 1. **Record
   that it still passes.** This is the defect, observed on the real corpus
   rather than on a probe, and it is the strongest single line of evidence in
   the PR body.
3. Apply §3.1. Re-run. **Record that it now fails**, naming that repository.
4. `git checkout --` the planted file. Re-run: green again.
5. Run the whole `Architecture.Tests` project — not the filter — because the
   probe adds types to an assembly other guards also walk (`BoundaryTests`,
   `PrimitiveBoundaryTests`, `NameMutabilityConventionTests`,
   `StaleCodeConventionTests` among them). "Reasoned that it is fine" is not
   "ran it".
6. Write all four outputs verbatim into
   `specs/193-both-spellings-of-an-absolute-rule/verification.md`.

Step 2 is the part that cannot be skipped. A probe inside the test assembly
proves the *detector* changed; only a plant inside a real candidate assembly
proves the *guard* changed, and those are different claims.

**Timestamp caution** for step 4: a `git checkout --` restore hands the file
back its original mtime, so MSBuild can skip the rebuild and the test still
fails after the revert. Touch the file or build with `--no-incremental` if step
4 does not go green.

---

## 6. Phase-4a colour: **RED**

**Behaviour-changing.** The guard's detection set grows: an input that was
reported clean is reported as an offence. ADR-0144's rule is that a test
arriving green is a phase-4 failure, and constitution §Testing's first clause
applies — the test is written first, observed failing, and that failure is
quoted in the PR body (ADR-0139).

Two reds are available and **both are taken**, because they prove different
things:

- **The probe red** (T004): the companion `[Fact]` asserts two offenders against
  the probe and sees one. Small, fast, deterministic, and it is the issue's own
  counterfactual.
- **The corpus red** (T009, §5 steps 2-3): a synchronous commit planted in
  `CameraCatalog.Infrastructure` that the guard ignores before the change and
  reports after. This is the one that proves the *shipped* rule moved.

**Characterisation was considered and rejected.** The argument for it would be
that the nine real assemblies are green before and green after (§1.4), so
nothing observable changes — which is exactly the reasoning that would let a
guard be widened without ever demonstrating the widening works. The rule is not
"did the suite's colour change", it is "did behaviour change"; it did, for every
input containing a synchronous commit. ADR-0144: *ambiguity resolves to red*.

One companion assertion is declared **green from the start** and must pass
unmodified throughout: the existing nine-assembly theory
`No_repository_commits_without_its_announcements`. If it goes red at any point
in phase 4, that is a real offender discovered — stop and report it, do not
adjust anything.

---

## 7. Follow-ups, recorded not folded in

| # | What | Why not here |
|---|---|---|
| F001 | The candidate filter misses `StreamFabAttributionService` (§1.4) — a non-`Repository` type in a non-`.Persistence` namespace calling `SaveChangesAsync` directly. Decide whether it is an offender or correctly out of scope, and if the former, widen the filter. | A different question (which types are candidates), a real production-code decision, possibly a genuine bug. Its own issue. |
| F002 | Other escapes from the seam the IL scan does not model: `Database.ExecuteSql*`, a `DbContext` reached through an interface-typed field, `SaveChanges` called inside a lambda lifted to a closure class the scan does not walk. | A different question (which calls count). §2. |
| F003 | `src/Shared.CQRS/ITransactionalCommit.cs:8`'s doc comment names only `SaveChangesAsync`. | Comment-only, in a production assembly, own phase-4a obligation. |

## 8. Risk

**The probe file is new and other guards walk the test assembly.** Types named
`…Repository` in a namespace containing `.Persistence`, holding a `DbContext`
field, are exactly the shape several other conventions look for. §5 step 5 is
the mitigation, and it is a task rather than a note.

**The probe must never be "fixed".** Its whole value is that it violates the
rule. `PrimitiveBoundaryProbe.cs:6-18` carries a doc comment saying so in as
many words; this probe needs the same, or the next reader tidying up warnings
will make the guard untestable while the build stays green.
