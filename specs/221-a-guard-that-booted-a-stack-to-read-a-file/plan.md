# Implementation Plan: A guard that booted a stack to read a file

**Spec**: 221 · **Issue**: [#2514](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2514) · **Branch**: `2514-a-guard-that-booted-a-stack-to-read-a-file`

**Created**: 2026-09-23 · **Phase**: 2 (Plan)

---

## Bounded context and layers

**None — and that is the substantive finding, not a formality.**

This change touches no bounded context. It moves one `[Fact]` between two test
projects:

| From | To |
|---|---|
| `tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs` | `tests/Architecture.Tests/WhepAuthorizePartitionKeyTests.cs` |

The nine bounded contexts, all four layers (`Domain`, `Application`,
`Infrastructure`, `Api`), `Shared.Kernel`, `Shared.Contracts`, `AppHost`,
`ServiceDefaults`, the two React apps and `deploy/helm` are all untouched. The
single permitted `src/` edit (`ApiGateway/Program.cs:75`) is a comment line;
it changes no token the compiler emits.

**Files in the change set**

| File | Change | Owns |
|---|---|---|
| `tests/Architecture.Tests/WhepAuthorizePartitionKeyTests.cs` | **new** | the moved fact + its two private helpers + the regex |
| `tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs` | **delete 1 fact + 4 members + 1 using + fix 1 cref** | the six surviving runtime facts |
| `src/ApiGateway/Program.cs` | **1 comment line** | prose pointer only |
| `specs/221-…/{spec,plan,tasks}.md` | new | this workflow |

Three files. Nothing else.

---

## Entities / value objects / invariants

**None.** No domain model, no aggregate, no value object, no persisted row, no
migration. Constitution §II's primitive ban and `PrimitiveBoundaryTests` are
not engaged — nothing here is a domain model. `Ensure.That(...)` (ADR-0105) is
not engaged — no argument guard is written, and the one throw in the change set
(`RepositorySource.Root()`'s `InvalidOperationException`) already exists on
`develop` and is not authored here.

The one **invariant this change must preserve** is stated as a fact about text,
because that is what it is:

> For the substring of `src/StreamDistribution/Api/Program.cs` that lies
> between `RateLimitPartition.GetFixedWindowLimiter(` and
> `_ => new FixedWindowRateLimiterOptions`, with whole comment lines removed:
> it contains `context.Connection.RemoteIpAddress`, and it does not match
> `^\$?"[^{]*"$`.

Before the move and after it, that sentence must be evaluated by the same code
against the same file and reach the same verdict.

---

## Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` message,
no Wolverine handler, no outbox row, no saga. ADR-0040/0073's domain→integration
event split is not engaged.

Stated rather than omitted because the plan template asks, and a plan that
silently drops the section is indistinguishable from one whose author did not
check.

---

## Boundary rules

- **No cross-context project references** (`NetArchTest`, `BoundaryTests`):
  unaffected — nothing under `src/<Context>/` is added or referenced.
- **`Architecture.Tests` must not reference `Integration.Tests`.** It does not
  today, and must not start. `IntegrationTestSelectionTests`' class doc gives
  the reason in full: *"a project reference would drag the Aspire hosting and
  DCP dependency graph into a project that today runs in seconds with no
  Docker — defeating a guard whose entire purpose is to move a verdict out of
  the Docker job."* The moved fact reads `Program.cs` **from disk**, exactly as
  `LogTailCoverageTests`, `GuardBanWiringTests` and
  `IntegrationTestSelectionTests` already do. **T004 must verify the new
  `.csproj` gained no `ProjectReference`** — this is the one way this
  otherwise-trivial move could do real damage.
- **`Architecture.Tests` must not reference the `StreamDistribution` Api
  assembly either.** It reads `Program.cs` as *text*. No `using
  SmartSentinelEye.StreamDistribution.*`, no type load. Same reasoning.
- **The `Integration.Tests` class keeps `[Collection(AspireCollection.Name)]`.**
  Its six surviving facts genuinely need the stack. `IntegrationTestSelectionTests`
  requires every class in that project to declare either the collection or a
  category trait; the class keeps its collection attribute and stays compliant.
- **The new class declares neither.** `IntegrationTestSelectionTests` scans
  `tests/Integration.Tests` only; `Architecture.Tests` classes carry no trait
  today (verified: no `[Trait` attribute on any class in that project — the
  only `[Trait` strings there are inside `IntegrationTestSelectionTests`' own
  counterfactual fixtures). Adding one would be inventing a convention.

---

## The design

### 1. Home, file, class name

`tests/Architecture.Tests/WhepAuthorizePartitionKeyTests.cs`, class
`WhepAuthorizePartitionKeyTests`, namespace `SmartSentinelEye.Architecture.Tests`
(file-scoped, matching every other file in the project).

**Why a new file rather than appending to an existing guard.**
`ResilienceRegistrationTests` is the closest cousin in *technique* and the file
the original scan says it mirrors — but it guards a different subject (the
resilience-handler registration across `src/`), reads a whole tree rather than
one file, and appending an unrelated `Program.cs` fact to it would make a
focused guard a grab bag. Spec 220's `WhepAuthorizeCeilingTests` is the closest
cousin in *subject* — but it is not on `origin/develop` yet (verified at
`8ce9b5fa`), and making this spec's landing depend on PR #2538's merge order
would be a self-inflicted dependency for no gain. A new file is disjoint from
both, which also makes this task `[P]`-safe against anything spec 220 does.

**Why `…PartitionKeyTests` and not `…RateLimitTests`.** The old class name
covers seven facts of which six are runtime behaviour; carrying the name over
would suggest the runtime facts came too. The new name states the one thing the
file guards. `WhepAuthorizeCeilingTests` (the *value*) and
`WhepAuthorizePartitionKeyTests` (the *shape*) then read as the pair they are.

**Class modifiers.** `public sealed class` with no primary constructor — the
guard takes no fixture. Mirrors `SourceScanCharacterisationTests`. The old class
had a primary constructor (`AspireFixture aspire`) that the moved fact never
used; dropping it is required, not optional, and is not an assertion change.

### 2. What moves, verbatim

From `WhepAuthorizeRateLimitTests.cs`:

| Member | Lines | Fate |
|---|---|---|
| `Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket()` | 182-245 | moves, **body character-for-character** except the one line noted in §3 |
| its `<summary>`/`<remarks>` doc | 135-181 | moves, then is **extended** per FR-008 (see §4) |
| `BareLiteralShape` regex field + doc | 247-256 | moves verbatim |
| `IsComment(string)` | 258-260 | moves verbatim |
| `CodeLines(string)` | 262-264 | moves verbatim |
| `RepositoryRoot()` + doc | 736-757 | **deleted**, not moved — replaced by `RepositorySource.Root()` |
| `using System.Text.RegularExpressions;` | line 2 | **deleted** from the old file, **added** to the new one |

`BareLiteralShape` is `new(@"^\$?""[^{]*""$", RegexOptions.Compiled | RegexOptions.Singleline)`.
Note it takes **no timeout** — unlike `RepositorySource.StringLiteral`, which
passes `TimeSpan.FromSeconds(5)`. Carrying it over without a timeout is correct
under FR-003 (verbatim) even though the neighbouring file does it differently;
adding one would be an unrequested behaviour change to a compiled regex. If
`CA3012`/`S6444` (regex-without-timeout) fires in the new project where it did
not in the old, that is a **stop-and-report**, not a silent edit — see §*Risks*
R2.

### 3. The one substitution: `RepositoryRoot()` → `RepositorySource.Root()`

The moved body's first statement reads:

```csharp
string programSource = System.IO.File.ReadAllText(
    System.IO.Path.Combine(RepositoryRoot().FullName, "src", "StreamDistribution", "Api", "Program.cs"));
```

and becomes:

```csharp
string programSource = File.ReadAllText(
    Path.Combine(RepositorySource.Root().FullName, "src", "StreamDistribution", "Api", "Program.cs"));
```

Two changes, both non-behavioural:

1. `RepositoryRoot()` → `RepositorySource.Root()`. **Permitted under FR-003
   because the bodies are the same walk**, and T002 proves it by diff rather
   than by assertion: both start at `new DirectoryInfo(AppContext.BaseDirectory)`,
   both loop `while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))`,
   both `candidate = candidate.Parent`, both throw
   `InvalidOperationException($"could not locate the repository root above {AppContext.BaseDirectory}")`.
   `RepositorySource`'s own doc states the six copies it replaced were
   MD5-identical; the copy being deleted here is a seventh of the same shape.
2. Dropping the `System.IO.` qualification. The old file needed it —
   `Integration.Tests` has an implicit-usings conflict or simply a local style
   there; `Architecture.Tests` files (`RepositorySource.cs`,
   `IntegrationTestSelectionTests.cs`) use bare `File`/`Path`/`DirectoryInfo`
   throughout. **If the bare form does not compile in the new project, keep the
   qualified form** — it is equivalent, and fighting it is not this spec's
   business.

**Nothing else in the body changes.** In particular the `ShouldContain`,
`ShouldBeGreaterThan`, `ShouldBeGreaterThanOrEqualTo` and `ShouldBeFalse` calls,
their `Case.Sensitive` argument, and all four failure-message strings are
carried over exactly.

### 4. The doc comment the new file must carry (FR-008)

The original `<remarks>` carries three paragraphs of hard-won context: why the
original two-socket version of this test could not work (Aspire's DCP proxy
re-originates every connection, so both clients arrive as `remote=::1`), that
spec 208's `tasks.md` T004 pre-authorised exactly this fallback, and that the
fact is **not** phase-4a red evidence because it would pass equally before or
after the limiter was wired up. **All three must survive the move** — a guard
whose reason is lost gets deleted within a month, which is the argument
`WhepAuthorizeCeilingTests` makes at length for its own doc.

Added on top, per FR-008:

- Spec 221 / #2514 as the reason it is *here* rather than in `Integration.Tests`.
- That it needs no stack — the fact that justifies the whole move.
- That its masking is `CodeLines`, deliberately **not** `SourceMask`, with a
  one-line pointer to `spec.md` §*Assumptions* Q1. Without this, the very next
  reader "fixes" the inconsistency and silently changes what the scan sees.

### 5. What is deliberately *not* built

- No `SourceMask` adoption (spec Q1).
- No touch to `ResilienceRegistrationTests` (spec Q2).
- No shared extraction of `IsComment`/`CodeLines` into a helper. After this
  move there are two private copies **in the same project**, which is the
  natural precondition for a later extraction — but doing it now edits a file
  the issue does not name, and would need its own characterisation.
- No new test for the move itself. The moved fact *is* the test; SC-001/SC-002
  are observations, not new `[Fact]`s.
- No change to `AppHost.cs`, `AspireFixture.cs`, `appsettings.json`, or
  `ci.yml`. The verdict relocates by virtue of which project the fact lives in;
  no CI wiring change is needed, because `coverage-check.ps1` already runs the
  whole `Architecture.Tests` project.

---

## Reuse inventory — read before writing

| Existing thing | Where | Use it for |
|---|---|---|
| `RepositorySource.Root()` | `tests/Architecture.Tests/RepositorySource.cs:42-53` | locating the repo root. **This is the point of the issue.** Do not write an eighth copy. |
| `RepositorySource.RelativePath` | same file, 62-63 | not needed here (one known file, not a tree walk) — listed so it is not reinvented either |
| `SourceMask.Apply` | `tests/Architecture.Tests/SourceMask.cs:104` | **deliberately not used** — spec Q1. Listed so the omission is visibly a decision |
| `ContainerImagePinTests`, `DockerfileUpstreamPinTests` | `tests/Architecture.Tests/` | precedent for a guard that reads one known file from disk and asserts about its text |
| `SourceScanCharacterisationTests` | `tests/Architecture.Tests/` | precedent for `public sealed class` with no fixture; also the worked example of how this repo characterises a masking change, should Q1 ever be revisited |
| `IntegrationTestSelectionTests` class doc | `tests/Architecture.Tests/` | the canonical statement of *why* a no-Docker verdict belongs in the no-Docker job — quote it, do not restate it |

---

## Constitution and ADR alignment

| Source | Bearing |
|---|---|
| **ADR-0037** (guided phased development) | This is phases 1-3. Gate at phase 3: tasks atomic, feature issue on Project #13. |
| **ADR-0144** (autonomous lane, phase-4a colours) | Colour declared in `spec.md`: **characterisation, observed green**. The lane may not skip 4a; here 4a is the baseline capture (T001), not a red test. |
| **ADR-0103** (integration tests via Aspire fixture, no Testcontainers) | The reason the old home costs what it costs. This change does not weaken it — the six runtime facts stay exactly where ADR-0103 puts them. |
| **ADR-0052 / 0053** (xUnit + Shouldly; sentence-style names) | Both already satisfied by the moved fact; FR-002 keeps the name. |
| **ADR-0139** (rules that fail the build, not the review) | The guard is such a rule. Moving it to the fast job strengthens it on the same argument. |
| **ADR-0065** (coverage gates 90/80/90) | Not engaged. The guard reads `Program.cs` as text; it executes no production code, so it contributes no coverage in either home. `coverage-check.ps1` excludes `Integration.Tests` by project name and includes `Architecture.Tests` — so the *run* moves into the coverage pass, but the *measured* figures cannot move. T005 confirms the gate still passes rather than assuming it. |
| **ADR-0084** (advisory code metrics) | 300 LOC/file, 30 LOC/method. The moved fact's body is ~60 lines including blank lines and comments; its executable statement count is well under 30. Advisory anyway, and `Directory.Build.props:141` `NoWarn`s S138/S1541 for tests. Not a blocker; noted so nobody is surprised. |
| **ADR-0105** (`Ensure.That`) | Not engaged — no argument guard authored. |
| **ADR-0029 / 0087** (rebase-only, each commit builds alone) | Load-bearing here: see `tasks.md` T006. The delete and the add must be **one commit**, or an intermediate commit has the fact defined twice (duplicate `[Fact]` name is legal, but the old one still drags the fixture) or zero times (a lost guard that `git bisect` will happily step through). |
| **ADR-0030 / 0086** (Conventional Commits, no `Co-Authored-By`) | `test(stream-distribution): …` or `refactor(tests): …`. Phase-4 concern. |
| **Constitution §Testing** | *"Refactors stay green."* This is a refactor. A red test at any point in it is a regression to report, not a step. |
| **Constitution §IV** (latency budget) | N/A, and `spec.md` says so explicitly rather than by omission. |

---

## Phase-6 note for the dispatcher: is `/security-review` warranted?

**Recommendation: no.** Run `/code-review` only.

The reasoning, so phase 6 can overrule it on evidence rather than on vibes:

- **The subject is security-adjacent; the change is not.** The guard protects a
  real control — the rate limiter on `/streams/authorize`, the one route in this
  system reachable with nothing but network access. But the change adds no
  endpoint, no scope, no token path, no persisted row, no trust boundary and no
  input parsing. It moves a `[Fact]` between test projects.
- **`src/` is unchanged except for one comment line** (FR-007). Diff the
  compiled output of `src/` before and after: it is identical.
- **The control's strength is unchanged.** `PermitLimit`, `Window`, the
  partition key and the policy name are not edited. `AppHost.cs`'s `isE2ETests`
  override is not edited.

**Two things that would flip this to yes**, named so the dispatcher has a test
rather than a judgement call:

1. If the mover finds the assertion must be **weakened** to pass in its new home
   (spec §*Phase-4a colour* says stop; if it were done anyway, the guard on a
   security control has been relaxed and that is a security change).
2. If `src/StreamDistribution/Api/Program.cs` acquires any non-comment edit.

---

## Risks

| # | Risk | Mitigation |
|---|---|---|
| **R1** | **The guard is dropped rather than moved.** A delete commit lands, the add commit is forgotten or fails review, and the repo loses a design guard silently — exactly the failure mode `git bisect` cannot see. | One commit for both halves (T006). T007's grep for the method name in `tests/Architecture.Tests` is the check that it arrived. |
| **R2** | **An analyzer fires in the new project that did not fire in the old** (most plausibly a regex-timeout rule on `BareLiteralShape`, or a `sealed`/`static` suggestion). Release builds with `TreatWarningsAsErrors`. | T004 builds `Architecture.Tests` in **Release** before running anything. If an analyzer demands a change to the carried-over code, that is a stop-and-report under FR-003, not a quiet edit. |
| **R3** | **A `ProjectReference` is added to make something resolve**, dragging Aspire/DCP into the fast project and defeating the move. | T004 asserts the `.csproj` diff is empty. This is the one way this change can do lasting harm. |
| **R4** | **The mover "improves" the masking to `SourceMask` in passing.** It looks like tidying; it is an uncharacterised behaviour change. | Spec Q1, plan §5, and a required sentence in the new file's doc comment (§4) all say no, in the three places a mover actually reads. |
| **R5** | **Stale pointers left behind** (`WhepAuthorizeRateLimitTests.cs:380`, `ApiGateway/Program.cs:75`). Not build-enforced — `GenerateDocumentationFile` is unset, so `CS1574` never fires. | T003 and T005 name both sites by line. T007's grep is the backstop. |
| **R6** | **Collision with PR #2538 (spec 220).** | Disjoint files, verified. Both touch `tests/Architecture.Tests/` but never the same file, and neither reads the other. If #2538 lands first, rebase is a no-op on these paths; if this lands first, likewise. |
| **R7** | **The baseline cannot be captured** because the Aspire stack will not boot (memory: *One machine, one Aspire stack* — two concurrent boots produce `FailedToStart` that reads exactly like a code defect; this session has other worktrees). | T001 checks for a competing stack before booting and, failing that, records spec 208's twice-observed verification as the documented prior with an explicit note that it is a citation, not a fresh observation. The post-move run (T004) needs no stack at all and is the stronger half of the evidence anyway. |
