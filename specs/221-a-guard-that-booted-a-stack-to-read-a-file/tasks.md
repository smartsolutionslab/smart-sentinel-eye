# Tasks: A guard that booted a stack to read a file

**Spec**: 221 · **Issue**: [#2514](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2514) · **Branch**: `2514-a-guard-that-booted-a-stack-to-read-a-file`

**Created**: 2026-09-23 · **Phase**: 3 (Tasks)

**Phase-4a colour**: **characterisation, observed green** (`spec.md` §*Phase-4a colour*). The moved fact passes today; it must pass **unmodified** after. An assertion that has to be edited is a stop-and-report, not an adjustment.

---

## Parallelism, stated once

**Nothing here is `[P]`.** Eight tasks, one user story, three files, and the two
file edits (T003's delete, T004's add) belong in **one commit** (T006), so they
cannot be split across agents. There is no foundational/fan-out structure to
exploit and no second bounded context to parallelise into.

The `[P]` opportunity that *does* exist is **between specs, not within this
one**: this spec and spec 220 (#2515, PR #2538) own disjoint files in
`tests/Architecture.Tests/` and can be delivered by different agents in either
order. That is a note for the orchestrator, not a marker on any task below.

**Estimated size: one sitting.** This is a relocation. If it grows a second
sitting's worth of work, something in `spec.md` §*Assumptions* was wrong — stop
and report rather than absorbing it.

---

## User Story 1 — The partition-key guard runs without a stack (P1)

### `[T001]` `[US1]` Capture the characterisation baseline — green, before touching anything

**This is the phase-4a artifact. It cannot be done after the move.**

1. Confirm the premise before spending a boot on it (memory: *Verify the issue
   premise before planning*). On this branch, read
   `tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs`
   and confirm the fact is at lines 182-245 with the helpers at 247-264 and
   736-757. If the file has moved under us, re-read before proceeding.
2. Check no competing Aspire stack is running before booting one (memory: *One
   machine, one Aspire stack* — a second concurrent boot yields `FailedToStart`
   that reads exactly like a code defect). This session has worktrees at
   `D:\Github\sse-2510` and `D:\Github\sse-2515`.
3. Run **only the fact being moved**:
   ```
   dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
     --filter "FullyQualifiedName~Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket"
   ```
4. **Record the verbatim output** — the `Passed!` summary line and the test's
   own name and duration. This goes in the PR body. A summary in your own words
   is not the evidence (memory: *Self-review catches contradictions, never
   omissions*).

**Done when**: the verbatim green output is written down.

**If the stack will not boot** (plan §*Risks* R7): record that fact explicitly,
cite `specs/208-a-ceiling-the-hook-never-had/verification.md` §2 (the fact
observed passing twice, 12 ms and 10 ms) as a **documented prior, not a fresh
observation**, and say so in those words in the PR. Do not present a citation as
a run. T004's no-Docker run is then the load-bearing half of the evidence.

**If it is red**: `spec.md` assumption A1 is wrong, this issue's premise does
not hold, **stop and report**. Do not "fix" it as part of a move.

---

### `[T002]` `[US1]` Prove the one permitted substitution is not a behaviour change

FR-004 allows exactly one non-verbatim edit: `RepositoryRoot()` →
`RepositorySource.Root()`. Prove it rather than assert it.

1. Read both bodies side by side —
   `tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs:745-757`
   and `tests/Architecture.Tests/RepositorySource.cs:42-53`.
2. Confirm, element by element: same start (`AppContext.BaseDirectory`), same
   sentinel (`SmartSentinelEye.slnx`), same loop, same `Parent` step, same
   exception type, same message text.
3. Write the comparison down in the PR body as a two-column diff or a stated
   list. One sentence saying "they're the same" is not it.

**Done when**: the comparison is recorded, or a difference is found — in which
case **stop and report**, because FR-003's verbatim rule then bites and the
private copy must move as-is instead.

---

### `[T003]` `[US1]` Remove the fact and its orphans from `Integration.Tests`

Edit `tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs`:

1. Delete the `[Fact]` `Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket`
   **and its `<summary>`/`<remarks>` doc block** (lines 135-245). Keep the text
   — T004 needs it.
2. Delete `BareLiteralShape` and its doc (247-256).
3. Delete `IsComment` (258-260) and `CodeLines` (262-264).
4. Delete `RepositoryRoot()` and its doc (736-757).
5. Delete `using System.Text.RegularExpressions;` (line 2). **It has no other
   user in this file** — verify by grepping the file for `Regex` before
   deleting, and verify again by building. `TreatWarningsAsErrors` is on in
   Release (`Directory.Build.props:17`) and CI builds Release, so an unused
   `using` left behind is a **build failure**, not a nit (SC-005).
6. **Fix the surviving dangling `cref` at line 380**, inside
   `Health_and_readiness_are_never_throttled`'s `<remarks>`:
   `<see cref="Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket"/>`
   now names a member this class no longer declares. Replace with a `<c>`
   reference naming the new class and method, e.g.
   `<c>WhepAuthorizePartitionKeyTests.Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket</c>`
   — `<see cref>` cannot resolve across to `Architecture.Tests`, which
   `Integration.Tests` does not reference and must not start referencing (plan
   §*Boundary rules*). Keep the surrounding sentence's meaning: it draws a
   distinction between two facts that are both standing guards for different
   reasons.
   **Note honestly**: this is not build-enforced.
   `GenerateDocumentationFile` is unset, so `CS1574` never fires and a dangling
   `cref` compiles silently (`spec.md` Q4). Fix it anyway.
7. Confirm the class is left with **exactly six** `[Fact]`s and that its primary
   constructor parameter `aspire` is still used (it is — by all six).

**Done when**: the file compiles in Release with no warnings and declares six facts.

---

### `[T004]` `[US1]` Create the guard in `Architecture.Tests` and run it with no Docker

Create `tests/Architecture.Tests/WhepAuthorizePartitionKeyTests.cs`:

1. `namespace SmartSentinelEye.Architecture.Tests;` (file-scoped),
   `using System.Text.RegularExpressions;`,
   `public sealed class WhepAuthorizePartitionKeyTests` — **no primary
   constructor, no fixture, no `[Collection]`, no `[Trait]`** (FR-005; plan
   §*Boundary rules* — `Architecture.Tests` carries no traits today and this is
   not the spec that invents one).
2. Paste the fact's body **character-for-character** from T003's deletion,
   applying only the substitution plan §3 authorises:
   `RepositorySource.Root()` in place of `RepositoryRoot()`, and bare
   `File`/`Path` in place of `System.IO.File`/`System.IO.Path` **if and only if**
   that compiles; keep the qualified form otherwise.
3. Paste `BareLiteralShape`, `IsComment` and `CodeLines` verbatim, doc comments
   included. **Do not** swap `CodeLines` for `SourceMask.Apply` — `spec.md` Q1,
   plan §5, and step 5 below all say no, and it is an uncharacterised behaviour
   change to the scan.
4. Carry the original `<remarks>`' three paragraphs across intact — the DCP
   proxy finding, spec 208 `tasks.md` T004's pre-authorised fallback, and the
   "not phase-4a red evidence" statement. A guard whose reason is lost gets
   deleted within a month.
5. Add the FR-008 paragraphs on top: #2514/spec 221 as the reason it lives
   here; that it needs no stack; and one sentence saying the masking is
   `CodeLines` **deliberately, not `SourceMask`**, pointing at `spec.md` Q1.
   Without that sentence the next reader "fixes" the inconsistency.
6. **Verify no `.csproj` change was needed.**
   `git diff tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj`
   must be empty. A `ProjectReference` added here would drag Aspire/DCP into the
   fast project and defeat the entire move (plan §*Risks* R3). If something will
   not resolve without one, **stop and report**.
7. Build **Release** (`TreatWarningsAsErrors`) and run with the Docker daemon
   **stopped**:
   ```
   docker ps                        # expect: cannot connect
   dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj \
     -c Release --filter "FullyQualifiedName~WhepAuthorizePartitionKeyTests"
   ```
   Record the verbatim output. **This is SC-001**, and a pass here with no
   daemon is the claim the whole issue rests on, observed rather than asserted.

**Done when**: 1 passed, 0 failed, no Docker, Release-clean, empty `.csproj` diff.

**Stop-and-report conditions** (do not work around any of these):
- the assertion needs any edit to pass (FR-003, `spec.md` §*Phase-4a colour*);
- an analyzer that did not fire in `Integration.Tests` fires here (plan R2 —
  most plausibly a regex-timeout rule on `BareLiteralShape`);
- a `ProjectReference` seems necessary.

---

### `[T005]` `[US1]` Fix the production-source pointer and confirm both suites

1. `src/ApiGateway/Program.cs:75` reads *"see spec 208 spec.md's Assumptions
   section and `WhepAuthorizeRateLimitTests.cs`'s own remarks"*. Those remarks
   have moved. Update the filename to `WhepAuthorizePartitionKeyTests.cs`.
   **Comment text only** — FR-007 permits no other `src/` edit. Confirm with
   `git diff src/` that the diff is one comment line.
2. Run the whole `Architecture.Tests` project in Release, not just the new
   filter — the guard now shares a project with ~35 other classes and must not
   have disturbed any of them.
3. Run `scripts/coverage-check.ps1 -Configuration Release` and confirm the
   ADR-0065 gate still passes. The *run* has moved into the coverage pass even
   though the guard executes no production code; confirm rather than assume
   (plan §*ADR alignment*, ADR-0065 row).
4. Run the full `WhepAuthorizeRateLimitTests` class against the Aspire fixture:
   six facts, six passes (**SC-003**). If T001 could not boot a stack, say so
   here too rather than reporting an un-run suite as green.

**Done when**: `src/` diff is one comment line; `Architecture.Tests` green in
Release; coverage gate green; the six integration facts green (or the boot
failure recorded honestly).

---

### `[T006]` `[US1]` Commit the delete and the add together

ADR-0087 rebase-merges commits **individually** onto `develop`, so every commit
must build and behave on its own. Splitting T003 and T004 across two commits
produces an intermediate state where the guard either does not exist (a lost
design guard that `git bisect` steps straight through — plan §*Risks* R1) or
exists twice with one copy still dragging the Aspire fixture.

- **One commit** carries the `Integration.Tests` deletion, the
  `Architecture.Tests` addition, and the `ApiGateway/Program.cs` comment fix.
- Conventional Commits (ADR-0030), **no `Co-Authored-By`** (ADR-0086) — the
  session attribution reminder does not override the repo rule (memory: *No
  Co-Authored-By footer here*). PR bodies still get the Claude Code line.
- Suggested subject: `refactor(tests): move the whep-authorize partition-key guard out of the Aspire collection`

**Done when**: one commit, builds on its own, message conforms.

---

## Wrap-up

### `[T007]` Prove the guard by counterfactual, then prove nothing dangles

Two checks, both of which this repository has been burned by omitting.

1. **Counterfactual** (memory: *Prove a guard by counterfactual* — it disproved
   three guards' own claims in a single day). Edit
   `src/StreamDistribution/Api/Program.cs:95`'s partition key to the bare literal
   `"whep-authorize-bucket"`, **leaving the four explanatory comment lines above
   it (86-94) intact** — that is the exact regression the comment-stripping
   exists to catch, and leaving them proves the stripping works. Re-run T004's
   filter. Expect **failure**, with the offending argument quoted in the message.
   **Revert, and confirm the revert with `git status` and a re-run.**
   This is **SC-002**, and it is **not** phase-4a red evidence — say so in the
   PR, in those words.
2. **Dangling-pointer sweep**:
   ```
   grep -rn "Authorize_partitions_the_rate_limiter" --include=*.cs src/ tests/
   grep -rn "WhepAuthorizeRateLimitTests" --include=*.cs src/ tests/
   ```
   Every hit must name a file that exists and a member it declares. Expected
   survivors that are **correct and must not be touched**:
   `src/AppHost/AppHost.cs:430,436` (names `ExhaustWindowAsync` and
   `PermitLimit`, both still in the integration class) and
   `tests/Integration.Tests/Fixtures/AspireFixture.cs:268` (names the class for
   `StreamDistributionThrottleProbe`, which the moved fact never used).
   `specs/208-…/spec.md:273` stays as written — delivered spec artifacts record
   what was true when written (`spec.md` §*Scope*, out-of-scope item 5).

**Done when**: the counterfactual failed and was reverted; the sweep is clean.

---

### `[T008]` Self-review against the requirement table, then housekeeping

1. Walk FR-001 … FR-008 and SC-001 … SC-005 one by one and record, for each,
   **what was observed** — not what was intended. Every measurement gets written
   down, not merely reported upward (memory: *Self-review catches contradictions,
   never omissions* — a figure that exists only in a subagent's report is
   invisible to every later grep and reviewer).
2. **Add the feature issue to Project #13** — the phase-3 gate. `/speckit-tasks`
   adds nothing to the board; it is a manual step:
   ```
   gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2514
   ```
   `item-add` prints nothing on success, and `item-list` defaults to 30 items —
   verify with `--limit 2000` or a `content.url` query (memory: *Project #13
   issues* — the number filter returns zero). **Note**: #2514 already shows
   `projects: Smart Sentinel Eye (Todo)` in `gh issue view`, so this is most
   likely a confirm, not an add. Confirm rather than skip.
3. **No per-task issues.** Phase 3 stopped creating them after spec 028;
   `tasks.md` is the artifact this work is tracked against (CLAUDE.md
   §*Workflow*). Do not run `/speckit-taskstoissues`.
4. Verify the issue's labels and state are as you left them after every
   subagent pass (memory: *A subagent closed an issue unasked*).

**Done when**: the requirement walk is written down and the board item is
confirmed present by `content.url`.

---

## Dependency graph

```
T001 (baseline, green)  ─┐
T002 (prove Root() same) ─┼─→ T003 (delete) ─→ T004 (add + no-Docker run) ─→ T005 (pointer + both suites)
                          │                                                        │
                          └────────────────────────────────────────────────────────┴─→ T006 (one commit)
                                                                                          │
                                                                           T007 (counterfactual + sweep)
                                                                                          │
                                                                                      T008 (self-review + board)
```

T001 and T002 are independent of each other and both must precede T003. T003 and
T004 are sequential only because T004 pastes what T003 removes; they land in one
commit regardless (T006).

---

## Phase-3 gate

Stop here. Hand `spec.md`, `plan.md` and `tasks.md` back for review before
phase 4.

Before the gate is satisfied:
- [ ] Spec reviewed, no `[NEEDS CLARIFICATION]` outstanding
- [ ] Plan aligns with the constitution and ADRs 0037, 0052, 0053, 0065, 0087, 0103, 0139, 0144
- [ ] Tasks atomic, dependencies stated, `[P]` markers explained (there are none, and why)
- [ ] Phase-4a colour declared: **characterisation, observed green**
- [ ] Phase-6 recommendation recorded for the dispatcher: `/code-review` only, `/security-review` not warranted, with two named conditions that would flip it
- [ ] #2514 confirmed on Project #13
