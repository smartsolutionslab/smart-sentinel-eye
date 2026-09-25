# Spec 252 — The calls the scan steps over

**Issue:** [#2470](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2470)
— *the outbox-commit guard's IL scan has call shapes it cannot see*. Labels
`tech-debt`, `agent:ready`. On Project #13, status **In Progress** — verified
2026-09-25 via `gh issue view 2470 --json projectItems`. No `item-add` needed.

**Spec number.** Every remote branch was listed on 2026-09-25: the highest
claimed is 250 (`specs/250-the-backoff-no-test-could-reach`). A worktree holds an
uncommitted `specs/251-the-boundary-the-delay-guesses`. This spec is **252**.
Re-check before the PR is opened.

**ADRs referenced:**

- **ADR-0088** (Postgres outbox, eager transactions) — why the rule exists.
- **ADR-0139** (rules fail the build; new behaviour observed red first) and
  constitution §Testing.
- **ADR-0144** (autonomous lane; phase-4a colours) — §6 picks **red**.
- **ADR-0036** (smallest change; surface assumptions).
- **ADR-0037** (phases and gates). **ADR-0109** (`[P]` rule).
- **ADR-0052 / 0053** (xUnit + Shouldly; sentence-style names).

**No new ADR.** This scopes a build-time test guard. No production assembly,
contract, constitution clause or runtime behaviour changes. ADR-0088 already
names the outbox as the commit seam; this decides which IL shapes the instrument
that enforces it recognises, and writes down which ones it knowingly does not.

**Latency budget (§IV): N/A.** Every changed file is under `tests/`.

---

## 1. The issue's premise, re-checked on this tree (`a54b11d0`, 2026-09-25)

The guard: `tests/Architecture.Tests/OutboxCommitTests.cs`. `BodiesOf` (136-150)
walks a candidate type and its **direct** nested types; `ReferencesSaveChanges`
(152-202) scans each body for opcode `0x28` (`call`) or `0x6F` (`callvirt`),
resolves the following 4-byte token, and reports exact names `SaveChanges` /
`SaveChangesAsync` declared on a type assignable to `DbContext`.

### 1.1 What the compiler actually emits — measured, not assumed

A scratch console project (net10.0, Release, Roslyn from the pinned SDK) held
one class per shape, with a stand-in `Ctx` whose `SaveChanges`/`SaveChangesAsync`
are `virtual` exactly as `DbContext`'s are. A reflection walk printed every IL
reference to a `SaveChanges*` member and the opcode that carried it:

```
BaseGroupRepository :: Save :: ldftn Ctx.SaveChanges
MethodGroupRepository :: Save :: ldvirtftn Ctx.SaveChanges
SyncLambdaRepository :: <Save>b__2_0 :: callvirt Ctx.SaveChanges
InterfaceRepository :: Save :: callvirt IDbCtx.SaveChanges
DisplayClassSyncRepository+<>c__DisplayClass0_0 :: <Save>b__0 :: callvirt Ctx.SaveChanges
AsyncLambdaRepository+<<Save>b__2_0>d :: MoveNext :: callvirt Ctx.SaveChangesAsync
DisplayClassAsyncRepository+<>c__DisplayClass0_0+<<Save>b__0>d :: MoveNext :: callvirt Ctx.SaveChangesAsync
StaticAsyncLambdaRepository+<>c+<<Save>b__1_0>d :: MoveNext :: callvirt Ctx.SaveChangesAsync
ParamLambdaRepository+<>c__DisplayClass0_0+<<Save>b__0>d :: MoveNext :: callvirt Ctx.SaveChangesAsync
```

Two corrections to the issue fall out of this:

1. **`Action save = dbContext.SaveChanges;` compiles to `ldvirtftn` (`0xFE 0x07`),
   not `ldftn` (`0xFE 0x06`).** `SaveChanges` is virtual, so Roslyn loads it
   through the vtable. `ldftn` appears only for `base.SaveChanges` — a
   non-virtual load, reachable only inside a `DbContext` subclass. A fix that
   added `ldftn` alone, as the issue proposes, would leave the shape the issue
   describes uncaught.
2. **A lambda lifted to a closure class is already caught — unless it is
   `async`.** A synchronous lambda lands in `Type+<>c__DisplayClass…` or
   `Type+<>c`, or as an instance method on the type itself: all depth ≤ 1, all
   walked today. An **async** lambda that captures a local or parameter (or is
   `static`) compiles to a state machine nested **inside** the closure class —
   `Type+<>c__DisplayClass0_0+<<Save>b__0>d` — depth 2, which `BodiesOf` never
   reaches. An async lambda capturing only `this` (e.g. a primary-constructor
   field) is lifted onto the type and stays depth 1, caught today. The escape is
   the depth, not the closure.

### 1.2 Live occurrences in `src/`

`grep -rn "SaveChanges" src --include=*.cs` returns four lines. Only one is a
call: `StreamDistribution/Infrastructure/Attribution/StreamFabAttributionService.cs:81`,
a plain awaited `callvirt` (the subject of #2469). The others are a doc comment
(`ITransactionalCommit.cs:8`), a base class name (`SaveChangesInterceptor`) and
the sanctioned seam (`SaveChangesAndFlushMessagesAsync`). **No method group, no
lambda, and no interface reaches `SaveChanges` anywhere in `src/`.**
`grep "interface I\w*DbContext"` over `src/`: nothing. No `SaveChanges` override
exists. No `CreateExecutionStrategy` (the EF pattern that most naturally puts
`SaveChangesAsync` in an async lambda) exists.

`ExecuteSql*` **is live** — seven call sites, four of them in assemblies the
guard scans:

| Site | Assembly scanned? | What it is |
|---|---|---|
| `AuditObservability/…/Persistence/AuditEventRepository.cs:40` | yes (and a current candidate) | `INSERT … ON CONFLICT DO NOTHING` for audit rows; `AuditEvent` raises nothing |
| `SystemVariables/…/Persistence/VariableValueRequestDedupStore.cs:32` | yes | dedup reservation insert |
| `EventIngestion/…/Persistence/FabPartitionProvisioner.cs:37` | yes | partition DDL |
| `EventIngestion/…/Persistence/EventPartitionRolloverMigrator.cs:84` | yes | partition DDL |
| `ServiceDefaults/Idempotency/IdempotencyStore.cs:65,121,138` | no | idempotency key table |
| `ServiceDefaults/Idempotency/IdempotencyReservationSweep.cs:115` | no | sweep delete |

`ExecuteUpdate*` / `ExecuteDelete*`: zero occurrences.

## 2. The decision, per shape

| # | Shape | Live? | Decision |
|---|---|---|---|
| A | Method group → delegate (`ldvirtftn`, and `ldftn` for `base.`) | no | **Guard it.** One more opcode family in the same token comparison. |
| B | Async lambda's state machine nested in a closure class (depth ≥ 2) | no | **Guard it.** Walk nested types transitively instead of one level. |
| C | `DbContext` reached through an interface | no (re-verified) | **Accepted blind spot, documented.** |
| D | `Database.ExecuteSql*` (and `ExecuteUpdate/Delete`) | **yes, 7 sites** | **Out of this guard's scope, documented.** |

### 2.1 Why A and B are in

Both are the #2292 / spec-193 precedent exactly: the rule's intent is unchanged
(a type's own code must not reach `DbContext.SaveChanges*`), and the change
widens the instrument's reading of the IL to shapes the compiler emits for that
same intent. Each is mechanical:

- **A** adds a second operand position to the existing loop (two-byte opcode
  `0xFE`, second byte `0x06`/`0x07`, token at `i + 2`). Same resolve, same
  exact-name + `IsAssignableFrom(DbContext)` comparison, same catch. It cannot
  report anything that is not a reference to `DbContext.SaveChanges*` from that
  body. `ldftn` is included with `ldvirtftn` because they are one family (load
  a method pointer) and a guard that caught `dbContext.SaveChanges` but not
  `base.SaveChanges` would be an arbitrary line; each opcode gets its own probe
  so neither arm is an untested claim.
- **B** replaces `type.GetNestedTypes(...)` with the transitive closure of
  nested types. Every type it newly reaches is lexically inside the candidate —
  it is the candidate's own code, split by the compiler. It cannot introduce a
  finding in a type that does not itself contain the call. Cycles are
  impossible (nesting is a tree).

Neither can turn the real theory red today: §1.2 shows no such shape in `src/`.

### 2.2 Why C is not

An interface dispatch resolves to the interface's method, whose declaring type
is not a `DbContext`. Catching it means either matching the name on **any**
declaring type (which would then flag every unrelated `SaveChanges`-named
member, a false-positive class the spec-193 control exists to prevent) or
resolving interface-to-implementation maps across assemblies (a type-system
walk, not a pattern match). Neither is justified for a shape with zero
occurrences whose introduction would itself be a design change: an
`IDbContext` abstraction is new architecture and should arrive with its own
review, at which point this guard is updated with it. The class doc will say so,
so the person adding such an interface meets the consequence.

### 2.3 Why D is not

`ExecuteSql*` is not a commit that bypasses the transaction; it is a statement
that **joins** the ambient `DbContext` transaction if there is one and
autocommits if there is not. Whether a given call is inside the outbox
transaction is a property of the caller's runtime context, not of the call site
— exactly what an IL pattern cannot see. Flagging the call site would turn the
guard red on four legitimate sites today (audit sink, dedup reservation, two
DDL provisioners — none of which announce anything) and would force the
exemption list the class doc argues against, four entries on day one. That is a
different rule ("a raw-SQL write must not modify an aggregate that announces
events"), with a different instrument, and it gets its own issue if anyone
wants it. This spec records the gap and the live sites in the guard's doc; it
files no follow-up issue, because nothing observed today is a defect.

### 2.4 Other blind spots written down at the same time

The same doc lists the shapes an IL pattern match cannot see at all, so the
list is complete rather than just the four the issue happened to name:
reflection (`GetMethod("SaveChanges").Invoke`), `dynamic`, and expression
trees (`ldtoken` + `Expression.Call`, compiled and invoked later). None occurs
in `src/`; none is guarded.

## 3. User stories

### US1 (P1) — A method-group commit is reported

As a maintainer, when a type in a scanned assembly converts
`DbContext.SaveChanges` into a delegate, the outbox guard reports it, so the
commit cannot escape the rule by being called later through the delegate.

```gherkin
Scenario: a virtual method group is caught
  Given a probe repository whose body is "Func<int> save = dbContext.SaveChanges"
  When the outbox guard scans the test assembly
  Then the probe is in the offender list

Scenario: a base method group is caught
  Given a probe DbContext-derived repository whose body is "Func<int> save = base.SaveChanges"
  When the outbox guard scans the test assembly
  Then the probe is in the offender list

Scenario: the substring control still holds
  Given FailureSubscriptionRepository subscribes to SaveChangesFailed and commits nothing
  When the outbox guard scans the test assembly
  Then it is not in the offender list
```

### US2 (P1) — An async-lambda commit is reported

As a maintainer, when `SaveChangesAsync` is awaited inside an async lambda
lifted to a closure class, the guard reports the enclosing type.

```gherkin
Scenario: an async lambda capturing a parameter is caught
  Given a probe repository that passes "async ct => await dbContext.SaveChangesAsync(ct)"
    where dbContext is a method parameter, not a field
  When the outbox guard scans the test assembly
  Then the probe's top-level type is in the offender list, once
```

The "method parameter, not a field" clause is load-bearing: a lambda that
captures only `this` is lifted onto the type and is caught **today** (§1.1), so a
probe written that way arrives green — a phase-4a failure, not a shortcut.

### US3 (P2) — The blind spots are written where a reader hitting them looks

The doc on `ReferencesSaveChanges` names shapes C and D and §2.4's list, each
with its one-line reason and D's live-site count, and says that adding an
`IDbContext` abstraction requires revisiting this guard. Documentation only; no
test.

### Bad-request / auth / conflict scenarios

N/A — a build-time reflection test, no endpoint, no caller identity, no
concurrent writer.

## 4. Acceptance

- The probe fact's exact expected list gains three rows (US1 ×2, US2 ×1) and is
  **red before the fix, green after**, with no edit to the probe types or the
  expected list between those two runs.
- The real theory (`No_repository_commits_without_its_announcements`, or
  `Nothing_commits_without_its_announcements` if #2469 has merged) stays green
  for all nine assemblies.
- Full `Architecture.Tests` green; Release build of it clean (analyzers are
  errors there).

## 5. Independent end-to-end test procedure

No Aspire stack: reflection over compiled assemblies only.

1. `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~OutboxCommitTests"`
   on the red commit — probe fact fails, its actual list missing exactly the
   three new rows; theory green.
2. Same on the fix commit — all green.
3. Counterfactuals, each run and reverted, output verbatim into
   `specs/252-the-calls-the-scan-steps-over/verification.md`:
   - drop the `0x07` arm → only the `ldvirtftn` probe goes missing;
   - drop the `0x06` arm → only the `base.` probe goes missing;
   - restore one-level `GetNestedTypes` → only the async-lambda probe goes missing.
   Each proves its arm is load-bearing and independent of the others.

## 6. Colour (ADR-0144 §phase 4a)

**Red — behaviour-changing.** The guard's detection set grows by two shapes.
The new probes are observed missing from the offender list before the fix. The
phase has no characterisation half: there is nothing live to characterise
(§1.2), and the blind-spot documentation (US3) carries no test by design.

## 7. Overlap with #2469 / PR #2587 (unmerged)

Re-verified against `origin/fix/2469-outbox-guard-candidate-filter` on
2026-09-25. #2469 changes the **candidate filter** (`Offenders`: every top-level
type), adds `PermittedDirectCommits` and a stale-entry fact, renames the theory,
and rewrites several docs. This spec changes the **detector** (`BodiesOf`, the
`ReferencesSaveChanges` loop). No logic overlaps, and the two compose: #2469's
`!type.IsNested` filter relies on `BodiesOf` reaching nested types through
their top-level type, which B makes true at every depth.

The textual collisions are wider than "one doc comment on `BodiesOf`":

1. **The probe fact's expected list** — both add rows to the same collection
   literal. Resolve by union.
2. **#2469's `Offenders` doc** says "Nested-of-nested bodies are not walked — a
   pre-existing gap, tracked separately (#2470)". After this spec that sentence
   is false. Whichever PR lands second deletes it (or states the transitive walk).
3. **`ReferencesSaveChanges`' summary** — #2469 edits "from a repository body"
   to "from any type's body"; US3 extends the same doc block. Adjacent hunks.
4. **The probe fact's doc** — both add a `<para>`.

All four are mechanical rebase resolutions. None changes what either PR proves.
