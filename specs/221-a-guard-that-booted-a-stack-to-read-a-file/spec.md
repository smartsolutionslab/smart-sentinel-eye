# Feature Specification: A guard that booted a stack to read a file

**Feature Branch**: `2514-a-guard-that-booted-a-stack-to-read-a-file`

**Spec**: 221

**Issue**: [#2514](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2514) — follow-up to [#2284](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2284) (spec 208), sibling of [#2515](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2515) (spec 220)

**Created**: 2026-09-23

**Status**: Draft

**Input**: "`WhepAuthorizeRateLimitTests`'s source-scanning guard belongs in `Architecture.Tests`." (issue #2514, sourced from `specs/208-a-ceiling-the-hook-never-had/verification.md`, phase-6 code review S4)

---

## Why this exists

`tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs`
holds seven `[Fact]`s. Six of them post to a running
`stream-distribution` through the Aspire fixture, exhaust a rate-limit window,
read the log tail and wait for admission to recover. The seventh —
`Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket`
(`WhepAuthorizeRateLimitTests.cs:183-245`) — does none of that. It opens
`src/StreamDistribution/Api/Program.cs` **as text**, strips comment lines,
locates the `AddPolicy("whep-authorize"` registration, extracts the partition-key
argument that follows `RateLimitPartition.GetFixedWindowLimiter(`, and asserts
two things about that text: that it contains
`context.Connection.RemoteIpAddress`, and that it is not a bare string literal
with no interpolation hole. Zero HTTP calls. No stack. It ran in **10-12 ms**
inside a fixture whose other six facts took 18-20 seconds each
(`specs/208-a-ceiling-the-hook-never-had/verification.md` §2).

That is not a small inefficiency in the abstract — it has three concrete costs,
all of them named in the issue:

**1. It waits for a boot it never uses.** `[Collection(AspireCollection.Name)]`
serialises every class in `Integration.Tests` against one running Aspire stack.
This fact cannot run until that stack is up, and its verdict is deferred to the
thirty-minute `integration` job. Move it to `tests/Architecture.Tests/` and it
runs in the fast `build` job instead, through `scripts/coverage-check.ps1`'s
"Unit + architecture tests + coverage gate" step (`.github/workflows/ci.yml:89-92`)
— no Docker, no fixture, seconds. `IntegrationTestSelectionTests`' own class doc
already states the general form of this defect: a verdict that could be read in
the cheap job and is silently read in the expensive one.

**2. It carries a hand-rolled copy of a helper that already exists.** Its
`RepositoryRoot()` (`WhepAuthorizeRateLimitTests.cs:745-757`) walks up from
`AppContext.BaseDirectory` looking for `SmartSentinelEye.slnx`. Its own doc
comment says so: *"Same shape as the copies in `AppHostE2ESwitchTests` and
`AppHostStackStatusTests`"* — a third copy, admitted in writing. Spec 190
(#2257) extracted exactly this walk into `RepositorySource.Root()`
(`tests/Architecture.Tests/RepositorySource.cs:42-53`) after confirming six
copies were MD5-identical, and recorded the remaining 25 as a follow-up. This is
one of the 25.

**3. Its home is the wrong one for what it asserts.** It asserts a **design
property of source text**, not a runtime behaviour. Its own remarks say so at
length: *"nothing about this shape depends on the limiter being wired up, so it
would pass equally before or after T007. It remains a standing design guard
against a global-bucket regression, not red evidence."* `tests/Architecture.Tests/`
is where this repository puts standing design guards that read source as text —
`ResilienceRegistrationTests`, `ConcurrencyConflictDeclarationTests`,
`EndpointScopeDeclarationTests`, `PreconditionDeclarationTests`,
`RouteValueRefusalDeclarationTests`, `StatusProducerDeclarationTests`,
`ContainerImagePinTests`, `DockerfileUpstreamPinTests`, and a dozen more.

**Nothing is wrong with the assertion.** It passes today, twice observed
(`verification.md` §2, runs 1 and 2). This spec moves a correct test to a
correct home. See §*Phase-4a colour*.

---

## Scope

**In scope.**

1. A new file `tests/Architecture.Tests/WhepAuthorizePartitionKeyTests.cs`
   carrying the one fact, its `[Fact]` name unchanged, its assertions unchanged
   character-for-character, and the helpers it needs.
2. Removing that fact and its now-orphaned helpers
   (`BareLiteralShape`, `IsComment`, `CodeLines`, `RepositoryRoot`) and the
   `using System.Text.RegularExpressions;` they were the only users of, from
   `tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs`.
3. Repointing the hand-rolled `RepositoryRoot()` at `RepositorySource.Root()`
   — the one substitution this move makes, and the one the issue asks for by
   name. The two bodies are the same walk (see §*Requirements* FR-004).
4. Correcting the two prose pointers this move invalidates:
   `WhepAuthorizeRateLimitTests.cs`'s surviving `<see cref="Authorize_partitions_…"/>`
   at line 380, and `src/ApiGateway/Program.cs:75`'s *"see … `WhepAuthorizeRateLimitTests.cs`'s
   own remarks"*, which points at the remarks that move.

**Out of scope, deliberately.**

- **Adopting `SourceMask` for this guard.** See §*Assumptions and open
  questions* Q1 — this is the single most load-bearing scope decision in this
  spec, and it is a *no*.
- **`ResilienceRegistrationTests`' own duplication.** It carries its own
  `IsComment`/`CodeLines`/`IsQuoted` line masking (`ResilienceRegistrationTests.cs:290-307`)
  **and** its own inline repo-root walk inside `ReadSources`
  (`ResilienceRegistrationTests.cs:309-329`), using neither `SourceMask` nor
  `RepositorySource`. That is a real second duplication, confirmed by reading —
  and it is a different file, a different spec 190 follow-up, and not this
  mechanical move's business. Recorded as a follow-up candidate, not fixed here.
- Any change to what the assertion checks, the strings it searches for, its
  failure messages, or the regex.
- The six remaining facts in `WhepAuthorizeRateLimitTests`, `AspireFixture`'s
  `StreamDistributionThrottleProbe`, `AppHost.cs`'s `isE2ETests` ceiling
  override, and the `PermitLimit = 50` accounting. Untouched.
- `specs/208-a-ceiling-the-hook-never-had/spec.md:273`, which names the moved
  method. Delivered spec artifacts record what was true when they were written;
  this repository does not rewrite them (precedent: spec 208's own
  `verification.md:31` flagged a wrong count in `SC-003` rather than editing
  it). The new file's doc comment names spec 208 and #2284, so the pointer is
  resolvable in the direction that matters.

---

## User Scenarios & Testing

### User Story 1 — The partition-key guard runs without a stack (Priority: P1)

*As the engineer who runs the fast CI job, I want the partition-key design
guard's verdict in the build job, so that a global-bucket regression fails in
seconds rather than waiting thirty minutes behind a Docker boot it does not
need.*

**Why this priority**: it is the whole feature. There is no P2.

**Independently shippable**: yes, and it is the only slice. The fact either
lives in `Architecture.Tests` and passes, or it does not.

#### Acceptance scenarios

**Happy path**

```gherkin
Scenario: The moved guard passes in the architecture suite
  Given tests/Architecture.Tests/WhepAuthorizePartitionKeyTests.cs exists
    And src/StreamDistribution/Api/Program.cs registers the "whep-authorize"
        policy with a partition key built from context.Connection.RemoteIpAddress
   When the Architecture.Tests project is run with no Docker daemon available
        and no Aspire stack booted
   Then Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket
        passes
    And the run completes without starting any container
```

```gherkin
Scenario: The integration class is lighter by exactly one fact
  Given the fact has been removed from WhepAuthorizeRateLimitTests
   When that class is run against the Aspire fixture
   Then six facts execute and all six pass
    And no orphaned helper, field or using directive remains in the file
```

**Conflict / regression case — the guard still catches what it exists to catch**

```gherkin
Scenario: A global bucket fails the moved guard
  Given the partition key in Program.cs is changed to the bare literal
        "whep-authorize-bucket"
   When the Architecture.Tests project is run
   Then Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket
        fails
    And its failure message quotes the offending partition-key argument
```

```gherkin
Scenario: A comment mentioning RemoteIpAddress does not rescue a global bucket
  Given the partition key is a bare literal
    And a comment line above it still contains the words RemoteIpAddress
   When the Architecture.Tests project is run
   Then the guard still fails, because the comment line is stripped before the
        search
```

**Bad-request / malformed-input case — the guard's own preconditions**

```gherkin
Scenario: The registration is renamed out from under the guard
  Given Program.cs no longer registers a policy named "whep-authorize"
   When the Architecture.Tests project is run
   Then the guard fails with its own first precondition message, naming the
        missing registration, rather than with an IndexOutOfRange or a
        substring assertion on an empty string
```

```gherkin
Scenario: The guard cannot find the repository
  Given the test assembly is run from a directory with no SmartSentinelEye.slnx
        above it
   When RepositorySource.Root() is called
   Then it throws InvalidOperationException naming AppContext.BaseDirectory,
        the same failure the deleted private copy produced
```

**Auth / scope case**

Not applicable, and stated rather than omitted: this feature adds no endpoint,
no scope, no token path and no persisted row. The *subject* of the guard is a
security control — the anonymous `/streams/authorize` hook's rate limiter — but
the guard itself is a file read in a test assembly. See §*Auth / authorization
scenario* below and `plan.md` §*Phase-6 note*.

### Auth / authorization scenario

```gherkin
Scenario: The moved guard changes nothing an attacker can reach
  Given the guard has moved from Integration.Tests to Architecture.Tests
   When src/StreamDistribution/Api/Program.cs is compared before and after
   Then it is byte-identical except for no bytes at all
    And the whep-authorize policy, its permit limit, its window and its
        partition key are unchanged
```

---

## Independent end-to-end test procedure

Runnable by a reviewer who trusts nothing in this spec. No Docker required for
steps 2-4.

1. **Capture the baseline (before any edit).** On `origin/develop`:
   ```
   dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
     --filter "FullyQualifiedName~Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket"
   ```
   Requires the Aspire stack (that is the point). Record the verbatim
   `Passed!` line. This is the characterisation baseline (§*Phase-4a colour*).

2. **After the move, run the new home with no Docker at all:**
   ```
   docker ps            # confirm the daemon is stopped, or stop it
   dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj \
     --filter "FullyQualifiedName~WhepAuthorizePartitionKeyTests"
   ```
   Expect `Passed!  - Failed: 0, Passed: 1`. A pass here with no daemon is the
   claim in §*Why this exists* item 1, observed rather than asserted.

3. **Prove the guard by counterfactual** (memory: *Prove a guard by
   counterfactual*; this repository has had three guards whose own claims did
   not survive this step). Edit `src/StreamDistribution/Api/Program.cs:95` to
   read `"whep-authorize-bucket"` — a bare literal, keeping the explanatory
   comment lines above it intact. Re-run step 2. Expect **failure**, with the
   offending argument quoted. Revert.

4. **Confirm the old home is intact:**
   ```
   dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
     --filter "FullyQualifiedName~WhepAuthorizeRateLimitTests"
   ```
   Expect six facts, six passes. Requires the stack.

5. **Confirm nothing points at a method that no longer exists:**
   ```
   grep -rn "Authorize_partitions_the_rate_limiter" --include=*.cs src/ tests/
   grep -rn "WhepAuthorizeRateLimitTests" src/
   ```
   Every surviving hit must name a file that exists and a member it actually
   declares.

---

## Requirements

| ID | Requirement |
|---|---|
| **FR-001** | The fact `Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket` MUST exist in `tests/Architecture.Tests/`, under a class in namespace `SmartSentinelEye.Architecture.Tests`, and MUST NOT exist in `tests/Integration.Tests/`. |
| **FR-002** | The fact's **method name MUST NOT change**. It is the join key between the baseline test output and the post-move test output, and the only form of the characterisation evidence a later reader can check. |
| **FR-003** | The assertion body — every searched string, every index computation, every failure message, and the `BareLiteralShape` regex — MUST be carried over **character-for-character**. If any character must change for the test to pass in its new home, the mover STOPS and reports (§*Phase-4a colour*). |
| **FR-004** | The private `RepositoryRoot()` MUST be replaced by `RepositorySource.Root()`. This is permitted under FR-003 because the two are the same walk over the same sentinel file (`SmartSentinelEye.slnx`) from the same origin (`AppContext.BaseDirectory`) throwing the same `InvalidOperationException` with the same message; T002 verifies that by diff before relying on it. |
| **FR-005** | The moved fact MUST require neither Docker nor a booted Aspire stack, and MUST NOT carry `[Collection(AspireCollection.Name)]` or any category trait. |
| **FR-006** | `WhepAuthorizeRateLimitTests` MUST be left with exactly six facts, no unused fields, no unused private methods and no unused `using` directives. |
| **FR-007** | No file under `src/` MUST change behaviour. The only permitted `src/` edit is `ApiGateway/Program.cs`'s comment pointer (§*Scope* item 4), which is prose. |
| **FR-008** | The new file MUST carry a class doc comment that names #2514, spec 221, #2284, spec 208, and states why the fact is a standing design guard rather than phase-4a red evidence — that is the context the original remarks carried, and losing it in transit is how a guard becomes unexplainable and then deleted. |

### Success criteria

| ID | Criterion |
|---|---|
| **SC-001** | The guard passes in `Architecture.Tests` with the Docker daemon stopped. Observed, not asserted. |
| **SC-002** | The guard fails on the bare-literal counterfactual, with the offending text quoted in the message. Observed. |
| **SC-003** | `WhepAuthorizeRateLimitTests` runs six facts, six passes, against the Aspire fixture. |
| **SC-004** | `grep -rn "Authorize_partitions_the_rate_limiter" src/ tests/` yields only resolvable references. |
| **SC-005** | The Release build is clean — `TreatWarningsAsErrors` is on in Release (`Directory.Build.props:17`) and CI builds Release, so an unused `using` left behind in `WhepAuthorizeRateLimitTests.cs` is a build failure, not a nit. |

---

## Locked tech choices this spec relies on

| Choice | ADR | How this spec uses it |
|---|---|---|
| xUnit + Shouldly, hand-written fakes | 0052 | The fact is already written this way and stays that way. |
| Sentence-style test names with underscores | 0053 | FR-002 keeps the existing name, which already complies. |
| Integration tests via the Aspire fixture, no Testcontainers | 0103 | The *reason* the old home is expensive: `AspireCollection` serialises on one real stack. |
| Hand-written fluent builders, no AutoFixture | 0054 | Nothing new is built; noted because a mover might be tempted to "tidy" the fixture-free test. |
| Rules that fail the build, not the review | 0139 | This guard is one of those rules. Moving it to the fast job makes it fail *sooner*, which is the same argument. |
| Guided phased workflow, seven gates | 0037 | This document is the phase-1 artifact. |
| Autonomous lane, phase-4a two colours | 0144 | §*Phase-4a colour* below declares which colour applies. |
| Conventional Commits, no `Co-Authored-By` | 0030, 0086 | Phase-4/7 housekeeping. |
| Rebase-only merge, linear history | 0029, 0087 | Each commit must build on its own — relevant here, because deleting from the old home and adding to the new home in *separate* commits leaves one commit where the fact exists twice or not at all. `tasks.md` T006 settles that. |

**No ADR is needed for this change.** It relocates a test between two existing
test projects using two existing helpers, under conventions ADR-0052 and
ADR-0103 already settle. Nothing about the system's architecture is being
decided. Stated explicitly because every spec must either cite an ADR for its
decisions or flag that one is missing, and this one does the former.

---

## Phase-4a colour: **characterisation, observed green**

Declared here, per ADR-0144 and CLAUDE.md §*Phase 4a has two colours*.

The fact **exists and passes today**. It was observed passing twice in spec
208's own verification (`verification.md` §2, 12 ms and 10 ms). This change
preserves behaviour, so:

- The baseline is captured **green, before the move**, from the old home, with
  the verbatim output quoted in the PR body (procedure step 1).
- After the move it must pass **unmodified** — same method name, same
  assertions, same messages.
- **An assertion that has to be edited to pass in its new home is a stop
  signal, not an adjustment.** It would mean the move is not the mechanical
  relocation this spec claims, most plausibly because the new project resolves
  a path, a `using`, or a masking helper differently. Report it; do not
  silently reconcile. CLAUDE.md is explicit: *"An assertion that has to be
  edited is evidence the behaviour moved: block, don't adjust."*
- The counterfactual (procedure step 3) is **not** phase-4a red evidence and
  must not be presented as such. It is a deliberate, reverted mutation proving
  the guard can fail — the discipline this repository adopted after three
  guards' own claims did not survive it.

---

## Latency budget impact

**N/A.** No leg of constitution §IV's six-row table is touched. Nothing under
`src/` changes behaviour (FR-007); the one permitted `src/` edit is a comment.
The event-to-overlay path is not on this change's map at all.

Stated rather than omitted, per constitution §IV and CLAUDE.md §*Latency
budget*: a spec that says nothing about the budget is indistinguishable from
one that forgot to look.

---

## Assumptions and open questions

**Q1 — Should the moved guard adopt `SourceMask`? Answer: no, and this is a
decision, not an omission.**

The issue describes the existing scan as *"a comment-stripped, quoted-string-aware
scan mirroring `ResilienceRegistrationTests`'s existing pattern"*, and this
repository has a shared masker — `SourceMask.Apply(text, MaskStrictness.…)`
(`tests/Architecture.Tests/SourceMask.cs`), used by six guards. Adopting it
here would look like the obvious tidy-up. It is out of scope because **it is a
behaviour change to the scan**, and this spec's phase-4a colour forbids one:

- The current `CodeLines` **drops whole comment lines** and rejoins the
  survivors with `Environment.NewLine`, which **changes every offset** in the
  scanned text.
- `SourceMask.Apply(…, CommentsBlankedLiteralsIntact)` **blanks in place and
  preserves length**, so offsets are unchanged — a deliberately different
  contract, documented as such in `SourceMask`'s own enum doc.
- The extracted `partitionKeyArgument` is taken from *between* two located
  offsets and then `.Trim().TrimEnd(',').Trim()`-ed. Against today's
  `Program.cs` — whose four explanatory comment lines sit entirely **between**
  `GetFixedWindowLimiter(` and the key literal (`Program.cs:86-95`) — the two
  maskers very probably produce the same trimmed string. **"Very probably" is
  not "characterised."** A behaviour-preserving change may not rest on it.

If the shared masker is wanted here, it is a second, *separately characterised*
change with its own before/after comparison — the same discipline spec 190
applied when it extracted `SourceMask` in the first place (it built a throwaway
frozen copy of each pre-refactor masker and compared char-for-char; see
`SourceScanCharacterisationTests`' class doc, M2). Filing that is a follow-up,
not a task in this spec.

**Q2 — `ResilienceRegistrationTests`' own duplication, confirmed by reading.**
It uses **neither** shared helper: its own `IsComment`/`CodeLines`/`IsQuoted`
(lines 290-307) and its own inline `SmartSentinelEye.slnx` walk inside
`ReadSources` (lines 309-329). `RepositorySource`'s class doc records that 31
files under `tests/` carried that walk on 2026-09-20, that spec 190 migrated
six, and that the other 25 are a recorded follow-up. `ResilienceRegistrationTests`
is one of the 25. That is a genuine second cleanup this issue's neighbourhood
invites — and folding it in would turn a mechanical relocation into a refactor
of a file the issue never names. **Not done here.** Recorded so the next reader
does not have to re-derive it.

**Q3 — Coordination with spec 220 (#2515, PR #2538), not a dependency.**
That spec adds `tests/Architecture.Tests/WhepAuthorizeCeilingTests.cs`, which
also reads `src/StreamDistribution/Api/Program.cs` from
`RepositorySource.Root()`, and it had not merged when this spec was written
(verified: the file is absent from `origin/develop` at `8ce9b5fa`). The two
touch **disjoint files** — `WhepAuthorizeCeilingTests.cs` vs
`WhepAuthorizePartitionKeyTests.cs` — and neither reads the other, so they can
land in either order. Two notes for whoever holds both:
(a) the two classes will sit adjacent and guard the same `Program.cs` from
different angles (a configured *value* vs a code *shape*); if a later reader
wants them merged, that is a third spec, not a merge-order problem;
(b) spec 220 uses `SourceMask` and this one does not, for the reason in Q1 —
the asymmetry is deliberate and should be read as such rather than as drift.

**Q4 — Two prose pointers break, and only one of them is enforced.**
`WhepAuthorizeRateLimitTests.cs:380`'s `<see cref="Authorize_partitions_…"/>`
and `src/ApiGateway/Program.cs:75`'s named reference both point at the moving
text. **Neither is build-enforced** — `GenerateDocumentationFile` is not set
anywhere in `Directory.Build.props` or the test projects, so `CS1574` does not
fire and a dangling `cref` compiles silently. They are fixed anyway, because a
silent dangling pointer is precisely the clerical-error class CLAUDE.md records
three separate corrections for. Stated honestly: this is a correctness-of-the-record
fix, not a build fix.

**Assumption A1 — the guard passes on `origin/develop` right now.** Held on
spec 208's twice-observed verification, not re-run at spec time. T001 re-runs
it before anything is moved; if it is red on `develop`, this spec's premise is
wrong and the mover stops (memory: *Verify the issue premise before planning* —
11 of ~20 board issues were stale by delivery).
