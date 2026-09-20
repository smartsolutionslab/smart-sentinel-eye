# Plan 193 — Both spellings of an absolute rule

`spec.md`. Issue [#2292](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2292).
Branch `2292-outbox-sync-commit-guard`, worktree `D:/Github/sse-2292`, cut from
`origin/develop`.

---

## 1. Bounded context and layers

**None.** This slice touches no bounded context. It adds no entity, no value
object, no aggregate, no repository, no handler, no endpoint, no message and no
migration. There is no domain model here to carry an invariant, and therefore
nothing for constitution §II to bind.

The whole diff is two files:

| File | Status | Layer |
|---|---|---|
| `tests/Architecture.Tests/OutboxCommitTests.cs` | modified | test — architecture guard |
| `tests/Architecture.Tests/OutboxCommitProbe.cs` | **new** | test — deliberate-violation fixture |

**Shipped at `tests/Architecture.Tests/Persistence/OutboxCommitProbe.cs`, not the flat path
above** — `dotnet_style_namespace_match_folder = true:warning` (a Release-build error, not
advisory) requires the folder to match the `.Persistence` namespace the probe's own types need
to be candidates at all. Recorded here after the fact rather than silently left wrong.

`src/` is **read only**. The boundary rules (no cross-context project
references; communication only through `Shared.Contracts`) are untouched because
nothing in a context changes. `Architecture.Tests` already references all
thirty-seven projects, so no `.csproj` edit is needed — including for EF Core,
which reaches the test assembly transitively today (`OutboxCommitTests.cs:2` is
`using Microsoft.EntityFrameworkCore;` and compiles).

**Messaging: none.** No domain event, no integration event, no `V<N>` contract.
ADR-0088's outbox is the *subject* of the rule, not a participant in the change.

---

## 2. The detector as it is, and the one line that moves

### 2.1 The pipeline today

`No_repository_commits_without_its_announcements` (a `[Theory]` over nine
assembly names) composes four steps inline at `OutboxCommitTests.cs:42-46`:

1. `Assembly.Load(assemblyName)` → `GetTypes()`
2. namespace filter — `type.Namespace?.Contains(".Persistence", Ordinal) == true`
3. name filter — `type.Name.EndsWith("Repository", Ordinal)`
4. `CallsSaveChangesDirectly` → `BodiesOf` (declared methods, constructors, and
   **nested types**, the latter being how an `async` method's state machine is
   reached) → `ReferencesSaveChanges` (IL byte scan for `0x28 call` / `0x6F
   callvirt`, resolve the token, compare)

Only step 4's comparison is wrong. Steps 1-3 are correct for this slice and
`spec.md` §2 keeps their known hole (`StreamFabAttributionService`) out of scope.

### 2.2 The change

`OutboxCommitTests.cs:124`, `==` on one name becomes `is` over two:

```csharp
if (called?.Name is nameof(DbContext.SaveChanges) or nameof(DbContext.SaveChangesAsync)
    && typeof(DbContext).IsAssignableFrom(called.DeclaringType))
```

Both facts this depends on are verified in `spec.md` §3.1: `nameof` over the
overloaded `SaveChanges` method group compiles, and NRT still narrows `called`
so the following line needs no `!`. The `&&` binds looser than the `is` pattern,
so no parentheses are required around the disjunction; adding them is harmless
and a reviewer preferring them is not a finding.

`spec.md` §3.2 is the reason this is exact membership and not
`StartsWith`/`Contains` — `add_SaveChangesFailed` is declared on `DbContext`
itself, so the existing declaring-type predicate would not rescue a substring
match from it.

**Nothing else in `ReferencesSaveChanges` changes.** The opcode set, the
`ResolveMethod` call, the four-exception `catch` filter and the `i + 4 <
il.Length` bound all stay exactly as they are. A `SaveChanges()` call is reached
by the same `0x6F callvirt` as `SaveChangesAsync`, proven by the counterfactual
run in `spec.md` §1.3, so no opcode is missing.

---

## 3. What is added

### 3.1 `OutboxCommitProbe.cs` — four types in a `.Persistence` namespace

Namespace: `SmartSentinelEye.Architecture.Tests.Persistence`. It must contain
the literal `.Persistence` and the types must end in `Repository`, or they are
filtered out at step 2/3 above and the companion test proves nothing about the
detector — it would prove something about the filter instead.

| Type | Body | Expected verdict |
|---|---|---|
| `AsyncOffenderRepository` | `public async Task SaveAsync(CancellationToken cancellationToken) => await dbContext.SaveChangesAsync(cancellationToken);` | **offender** (today and after) |
| `SyncOffenderRepository` | `public void Save() => dbContext.SaveChanges();` | **offender** — today it is **not**, and that is the red |
| `SeamCommitRepository` | commits through an injected `ITransactionalCommit`; never touches `dbContext` | clean — the negative control |
| `FailureSubscriptionRepository` | constructor does `dbContext.SaveChangesFailed += OnFailed;` and nothing else | clean — the **substring** control |

`FailureSubscriptionRepository` is the counterfactual for the *rejected* design
(`spec.md` §3.2). Its IL contains `callvirt … DbContext::add_SaveChangesFailed`,
whose declaring type **is** a `DbContext`, so it is caught by any substring or
prefix widening and by neither the current nor the chosen comparison. Without
it, §3.2's argument is prose; with it, a future author who "simplifies" the
comparison to `StartsWith` gets a red test rather than a false accusation
shipped. *Prove a guard by counterfactual* — including against the design that
was not chosen.

Supporting type: `ProbeDbContext : DbContext` in the same file. It is **never
instantiated** — every use is reflection over its members — so it needs no
`OnConfiguring`, no connection string and no `DbSet`. A parameterless
`DbContext` subclass that would throw on construction is therefore fine, and the
file should say so, because it looks broken to a reader who assumes it runs.

The file carries a `PrimitiveBoundaryProbe.cs:6-18`-style header: these types
deliberately violate the rule, they must never be "fixed", and the expected
verdicts in §3.2's test are exact rather than a superset, so adding or retyping
a member here requires updating that assertion in the same change.

### 3.2 `The_rule_sees_both_spellings_of_a_direct_commit` — `[Fact]`

On `OutboxCommitTests`, so it can reach the `private static` detector without
widening anyone's visibility.

```
offenders = Offenders(typeof(OutboxCommitTests).Assembly)

offenders.ShouldBe([
    "SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository",
    "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository",
], ignoreOrder: true)
```

An **exact** list, not `ShouldContain`. `ShouldContain("…SyncOffenderRepository")`
would pass a detector that reports every candidate handed to it; the exact list
is what makes `SeamCommitRepository` and `FailureSubscriptionRepository` load
bearing rather than decorative (ADR-0053 sentence-style naming; ADR-0052
Shouldly; ADR-0054 hand-written fixtures).

Before the fix this fails with one entry where two are expected. That is T004's
red, and the message Shouldly prints names the missing `SyncOffenderRepository`
— which is the issue's own `offenders=1` restated as a test failure.

### 3.3 `Offenders(Assembly)` — the one extraction

Steps 1-4 of §2.1 move out of the theory body into

```csharp
private static List<string> Offenders(Assembly assembly)
```

and the theory becomes `List<string> offenders = Offenders(Assembly.Load(assemblyName));`.

**This is a refactor inside a bug fix, and it is the minimum that lets the new
test exist honestly.** The alternative is for the companion test to re-express
the namespace filter, the name filter and the detector call itself — a copy that
stays green when the guard is broken, which is the defect this whole spec is
about. One shared body, two call sites, no behaviour change: the theory runs the
same four steps in the same order over the same input.

The extraction is **behaviour-preserving** and the existing nine-assembly theory
is its covering test, green before and green after (`spec.md` §6). It is not a
second slice and does not need its own colour; it is not a licence to tidy
anything else in the file either.

### 3.4 The message and the doc comments (`spec.md` §3.4)

- `OutboxCommitTests.cs:49` — `"… calls SaveChangesAsync directly"` must name
  both spellings and keep pointing at `ITransactionalCommit`. The rest of the
  message (spec 021 FR-001, "committing directly is silent") is still true and
  should not be rewritten.
- The class doc (`:7`) — `<c>SaveChangesAsync</c>` → both.
- The `ReferencesSaveChanges` doc (`:70`) — the `<see cref>` points at
  `DbContext.SaveChangesAsync(CancellationToken)` alone. Name both, and keep the
  existing `<para>` about nested types verbatim: it records why the rule was
  wrong the *first* time and deleting it would repeat the mistake this spec is
  correcting.
- Add one sentence recording why the comparison is exact membership rather than
  a prefix, naming `add_SaveChangesFailed` and
  `SaveChangesAndFlushMessagesAsync`. A comment says *why*, and this why is not
  obvious (`CLAUDE.md`: no drive-by comments, but this is not drive-by — it is
  the reason a reader would otherwise "simplify" the line and break it).

---

## 4. Entities, value objects, invariants

**None.** No domain model is added or changed, so there is no invariant to
state, nothing for `PrimitiveBoundaryTests` to walk, and no `Ensure.That`
guard to write. Recorded explicitly so the absence is visible as a decision.

The probe types hold primitives (none, in fact — only a `ProbeDbContext`
reference and an `ITransactionalCommit`), are not `AggregateRoot<>` subclasses,
and are therefore outside `PrimitiveBoundaryTests`' roots walk. Confirmed by
reading `PrimitiveBoundaryTests.Roots`, which selects `AggregateRoot<>`
descendants; §6's whole-project run is the check that this reading is right.

---

## 5. Constraints and conventions

| Constraint | How this slice meets it |
|---|---|
| ADR-0084 — 300 LOC/file advisory | `OutboxCommitTests.cs` is 146 lines today; §3.2 + §3.3 + §3.4 add roughly 45. `OutboxCommitProbe.cs` is new, roughly 90 with its doc comments. Both well under |
| ADR-0084 — 30 LOC/method, 4 params, complexity ≤ 10 | `Offenders` is five lines and one parameter. `ReferencesSaveChanges` gains no branch — one `==` becomes one `is` over two constants |
| ADR-0053 — sentence-style test names with underscores | `The_rule_sees_both_spellings_of_a_direct_commit` |
| ADR-0054 — hand-written builders, no AutoFixture | The probe is hand-written types, not generated data |
| ADR-0049 — `CancellationToken` last, no `ConfigureAwait` | `AsyncOffenderRepository.SaveAsync(CancellationToken)` follows it, because the probe must look like a repository someone would actually write |
| ADR-0105 / ADR-0139 — `Ensure.That`, never `ArgumentNullException.ThrowIfNull` | The probe takes no argument it needs to guard; adding a guard would be drive-by. `RS0030` at `error` would catch a lapse anyway |
| CLAUDE.md — collection expressions with explicit type | `List<string> offenders = [.. …]` as the current code already does; the expected list in §3.2 likewise |
| CLAUDE.md — no leading underscore on private fields | Primary constructors for three of the four probe types; `FailureSubscriptionRepository` needs one real instance field (`dbContext`, no leading underscore) to carry the `+=` subscription statement a primary-constructor parameter alone cannot express |
| Constitution §II | N/A — no domain model. §4 |

### 5.1 The probe must not disturb the other guards

`Architecture.Tests` is walked by its own guards, and §3.1 adds types shaped
like the thing several of them look for — a `…Repository` in a `.Persistence`
namespace holding a `DbContext`. Reading says none of them is affected:
`PrimitiveBoundaryTests` walks `AggregateRoot<>` descendants, `BoundaryTests`
walks the context assemblies, `HandlerDeconstructionTests` reads handler source.

**Reading is not running.** `tasks.md` T010 runs the whole `Architecture.Tests`
project rather than the `OutboxCommitTests` filter, and a green filter with an
unrun project is not a discharge of this row.

### 5.2 `Assemblies()` must not gain the test assembly

The nine-name list at `OutboxCommitTests.cs:23-34` stays exactly as it is. If
the test assembly were added, the probe would fail the real theory and the only
available repair would be an exemption list — in the one guard whose stated
authority is that it has none. This is the single change a reviewer should check
has *not* happened.

---

## 6. Verification

`spec.md` §5, in full, as tasks T009-T010. The load-bearing step is planting a
synchronous commit in a **real** `CameraCatalog.Infrastructure` repository and
observing the guard ignore it before the change and report it after. The probe
proves the detector moved; only the plant proves the guard did.

---

## 7. Risk

| Risk | Mitigation |
|---|---|
| A future author "simplifies" the two-name pattern to `StartsWith("SaveChanges")` | `FailureSubscriptionRepository` (§3.1) turns that into a red test, and §3.4's comment says why at the line |
| The probe gets "fixed" by someone clearing warnings | The file header says it must not be, mirroring `PrimitiveBoundaryProbe.cs` |
| `ProbeDbContext` trips an analyzer for a `DbContext` without `OnConfiguring`, or `FailureSubscriptionRepository` trips a disposal rule for an unremoved event subscription | Build the project in Release before the PR, not only Debug — `TreatWarningsAsErrors` is a Release property. If a warning does fire, fix the probe's shape; **do not** add a suppression: ADR-0144 forbids a new suppression as a route to green |
| The revert in `spec.md` §5 step 4 leaves the test red | Known: a restored file keeps its old timestamp and MSBuild skips the rebuild. Touch it or build `--no-incremental` |
| The extraction in §3.3 is read as scope creep | §3.3 states the alternative and why it is worse. If a reviewer still objects, the fallback is to keep the theory inline and have the companion call `CallsSaveChangesDirectly` per type — weaker, because the filters then go untested, but not wrong |
