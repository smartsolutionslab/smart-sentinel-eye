# Tasks 252 — The calls the scan steps over

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2470 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: the guard's detection set grows by two shapes). US3 is
documentation with no test, by design.
**Engineer**: `test-writer` (4a) then `backend-engineer` (4b) · **Reviewer**: `backend-reviewer`
**Tracking**: feature-level issue #2470 (on Project #13, In Progress). No per-task issues.

Format: `[ID] [P?] [Story] description`. No foundational work (no Shared.Kernel / Contracts /
AppHost). No `[P]`: every task touches `OutboxCommitTests.cs` or `OutboxCommitProbe.cs`, and the
slice is two commits — one agent per phase, no fan-out.

Build note for every run: if an AppHost from this worktree holds `src/*/Api/bin`, use
`--artifacts-path <scratch>`; do not stop a stack you did not start.

## Phase 4a — red (test-writer)

- [ ] **T001 [US1]** `tests/Architecture.Tests/Persistence/OutboxCommitProbe.cs`: add
  `MethodGroupOffenderRepository(ProbeDbContext)` returning `Func<int>` from
  `dbContext.SaveChanges` (method group, not a lambda), and
  `BaseMethodGroupOffenderRepository : DbContext` returning `Func<int>` from **`base.`**`SaveChanges`
  (plan §1). Summaries per plan §1.
- [ ] **T002 [US2]** Same file: add `AsyncLambdaOffenderRepository` — **no fields, no primary
  constructor**; `SaveAsync(ProbeDbContext dbContext, Func<Func<CancellationToken, Task>, Task> run)`
  passes `async cancellationToken => await dbContext.SaveChangesAsync(cancellationToken)`. The
  context must be a method parameter: a `this`-capture compiles to depth 1 and is caught today
  (spec §1.1), which would make this probe arrive green. Add one sentence naming spec 252 to the
  file-level doc.
- [ ] **T003 [US1][US2]** `OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit`: add
  the three expected rows (plan §2) and the spec-252 `<para>`. Nothing else in the file.
- [ ] **T004** Run `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~OutboxCommitTests"`;
  capture verbatim. **Required**: probe fact red, its actual list missing **exactly** the three new
  rows (the two existing offenders present, `SeamCommitRepository` and
  `FailureSubscriptionRepository` absent); theory green for all nine assemblies. If any new probe is
  already present, the probe is the wrong shape — stop and report, do not proceed. Run the full
  `Architecture.Tests` project: only the probe fact may fail. Commit
  `test(architecture): probe the outbox guard with a method-group and an async-lambda commit`.

Depends: T001, T002 → T003 → T004.

## Phase 4b — the fix (backend-engineer; may not edit T001–T003's code)

- [ ] **T005 [US2]** `OutboxCommitTests.BodiesOf`: walk nested types transitively (plan §3); extend
  the "nested types are the point" doc with the depth-2 async-lambda case.
- [ ] **T006 [US1]** `OutboxCommitTests.ReferencesSaveChanges`: read the token at `i + 2` after
  `0xFE 0x06` / `0xFE 0x07`, in addition to `i + 1` after `0x28` / `0x6F`, with the bounds check of
  plan §4. Comparison and catch clause unchanged. Update the opcode comment.
- [ ] **T007 [US3]** `ReferencesSaveChanges` doc: the "What this does not see, on purpose" block
  (plan §5) — interface dispatch; `ExecuteSql*`/`ExecuteUpdate*`/`ExecuteDelete*` with the live
  count; reflection/`dynamic`/expression trees.
- [ ] **T008** Filtered run all green; `git diff HEAD~1 -- tests/Architecture.Tests/Persistence/OutboxCommitProbe.cs`
  empty and the expected list unchanged since T004; full `Architecture.Tests` green; Release build
  of `Architecture.Tests` clean. Commit
  `fix(architecture): read delegate loads and nested closures in the outbox guard's IL scan`.

Depends: T004 → T005, T006 → T007 → T008.

## Phase 5 — verify

- [ ] **T009** Three counterfactuals, each applied alone, run, reverted (touch the restored file
  before the next run): (a) remove the `0x07` arm → only `MethodGroupOffenderRepository` missing;
  (b) remove the `0x06` arm → only `BaseMethodGroupOffenderRepository` missing; (c) restore
  one-level `GetNestedTypes` → only `AsyncLambdaOffenderRepository` missing. `git diff` empty after.
  Outputs verbatim into `specs/252-the-calls-the-scan-steps-over/verification.md`.

## Bookkeeping (orchestrator)

- [ ] **T010** PR body: `Closes #2470`; quote T004's red and T009 verbatim; state the decision per
  shape in one line each (A, B guarded; C, D documented blind spots with reasons) and the two
  corrections to the issue (method group is `ldvirtftn`; only *async* closures escaped). Re-check
  spec number 252 before merge. If #2469 (PR #2587) merged first: rebase, union the expected list,
  and delete #2469's "nested-of-nested … tracked separately (#2470)" sentence from `Offenders`' doc
  (spec §7). If this merges first, tell #2469's branch to drop that sentence on its rebase. Confirm
  #2470 closed after merge.

## Out of scope — do not do

- Any file under `src/`, including the seven `ExecuteSql*` sites.
- The candidate filter / `PermittedDirectCommits` / theory rename (#2469) · `ITransactionalCommit.cs`
  doc (#2471) · the scanned assembly list.
- Interface-dispatch detection, `ExecuteSql*` detection, reflection/expression-tree detection
  (spec §2.2–2.4).
