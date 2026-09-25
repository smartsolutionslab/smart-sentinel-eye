# Tasks 247 — The service the filter never asked about

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2469 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: the guard's detection set grows). T003's reason-pin is
green by design and proved by counterfactual in T009.
**Engineer**: `test-writer` (4a) then `backend-engineer` (4b) · **Reviewer**: `backend-reviewer`
**Tracking**: feature-level issue #2469 (on Project #13, In Progress). No per-task issues.

Format: `[ID] [P?] [Story] description`. No foundational work (no Shared.Kernel / Contracts /
AppHost). No `[P]`: T001–T002 and T004–T006 share `OutboxCommitTests.cs`, and the whole slice is
two commits — one agent per phase, no fan-out.

Build note for every run: if an AppHost from this worktree holds `src/*/Api/bin`, use
`--artifacts-path <scratch>`; do not stop a stack you did not start.

## Phase 4a — red (test-writer)

- [ ] **T001 [US1]** Create `tests/Architecture.Tests/Attribution/OutboxCommitServiceProbe.cs`,
  namespace `SmartSentinelEye.Architecture.Tests.Attribution`, holding
  `OffenderAttributionService(ProbeDbContext)` whose `async` method awaits
  `dbContext.SaveChangesAsync(ct)` (plan §5), with the "never fix this probe" doc mirroring
  `Persistence/OutboxCommitProbe.cs`.
- [ ] **T002 [US1]** `OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit`: add the
  expected row `SmartSentinelEye.Architecture.Tests.Attribution.OffenderAttributionService`; doc
  "same namespace/name candidate filter" → "same candidate filter". Nothing else in the file.
- [ ] **T003 [US2]** `tests/StreamDistribution.Infrastructure.Tests/Attribution/StreamFabAttributionTests.cs`:
  add `The_pass_raises_nothing_a_direct_commit_would_drop` (plan §6 — clear pending events first,
  assert both attributed **and** both `PendingEvents` empty; doc names the guard's exception and
  says the fix on failure is `IStreamRepository.SaveAsync`, not editing this test).
- [ ] **T004** Run `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~OutboxCommitTests"`
  and `dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~StreamFabAttribution"`;
  capture verbatim. **Required**: probe fact red — actual list lacks `OffenderAttributionService`;
  the nine theory cases green; T003 green. Any other pattern → stop and report. Run the full
  `Architecture.Tests` project too: only the probe fact may fail. Commit
  `test(architecture): make the outbox guard's probe commit outside a repository` (T001–T003).

Depends: T001 → T002 → T004; T003 → T004.

## Phase 4b — the fix (backend-engineer; may not edit T001–T003's code)

- [ ] **T005 [US1]** `OutboxCommitTests.Offenders`: replace the two namespace/name predicates with
  `.Where(type => !type.IsNested)` (plan §1). Update `Offenders`' and `ReferencesSaveChanges`' docs
  ("repository body" → "any type's body"; nested-of-nested not walked, see #2470).
- [ ] **T006 [US2]** Add `PermittedDirectCommits = [typeof(StreamFabAttributionService)]` with the
  decision-record doc (plan §2); rename the theory to `Nothing_commits_without_its_announcements`
  and apply `.Except(...)` (plan §3); failure message still points only at `ITransactionalCommit`.
  Add `Every_permitted_direct_commit_still_commits_directly` (plan §4). Rewrite the class doc's
  "no exemption list" paragraph (plan, "Class doc").
- [ ] **T007** Filtered run (all green, T001–T003 unmodified since their commit — `git diff HEAD~1
  -- <those three files>` empty), full `Architecture.Tests`, full
  `StreamDistribution.Infrastructure.Tests`, Release build of `Architecture.Tests` (analyzers are
  errors there). Commit
  `fix(architecture): consider every type, not only repositories, as an outbox-guard candidate`.

Depends: T004 → T005 → T006 → T007.

## Phase 5 — verify

- [ ] **T008 [US1]** Corpus counterfactual: remove the permitted entry → theory red for
  StreamDistribution naming exactly `StreamFabAttributionService`. Restore.
- [ ] **T009 [US2]** Stale-entry counterfactual: add `typeof(StreamRepository)` → stale-entry fact
  red naming it. Reason counterfactual: raise an existing Stream domain event inside
  `AttributeToFab` → T003's test red. Restore both; `git diff` empty; touch restored files before
  re-running. All outputs verbatim into `specs/247-…/verification.md`.

## Bookkeeping (orchestrator)

- [ ] **T010** PR body: `Closes #2469`; quote T004's red and T008–T009 verbatim; state the decision
  in two sentences (widen to every type; one typed, reason-pinned exception) and the rejected
  option (routing through the seam would not protect the property). Re-check spec number 247
  against unmerged branches and the main worktree before merge. Confirm #2469 closed after merge.

## Out of scope — do not do

- Any file under `src/` (`StreamFabAttributionService`, `Stream`, `ITransactionalCommit`).
- IL-scan escapes (#2470) · `ITransactionalCommit.cs:8` doc (#2471) · the scanned assembly list.
