# Tasks 421: A camera URL's plain DNS host is written in ASCII

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #3023 (feature-level, already on Project
#13 and in the autonomous lane; no per-task issues, per CLAUDE.md Phase 3)

**One PR, colour red** (behaviour-changing: `From` accepts less), on `fix/3023-ascii-only-plain-hosts`.
- Characterisation T010-T015 is observed green first, then left unmodified.
- Red T030-T036 is observed failing, and the failure is quoted verbatim in the PR.

**Authorised edits.** The owner (2026-10-20) authorised exactly the five existing-test edits E1-E5 of plan §8 (12
assertions). **No other existing assertion may change.** The engineer may not edit any test from T010-T036.

**Roles.**
- Tests: `test-writer` (T001, T010-T036, T060), then `test-adversary` (T070).
- Production code: `backend-engineer` only (T040).
- Review: `backend-reviewer` plus `security-reviewer` (T080).
- No frontend, infra or migration work.

Format: `[ID] [P?] [Story] description, files`. `[P]` means the task owns files disjoint from every other `[P]` task
in the same phase (ADR-0109). One branch, one index: the orchestrator commits in sequence.

**Blocking.** No foundational Shared.Kernel, Contracts or AppHost task blocks a fan-out. Phase 0 blocks Phase 1,
Phase 1's gate blocks Phase 2, and so on.

---

## Phase 0: Setup (blocks everything)

- [x] **T001 [US1]** On the branch tip:
  1. Re-check spec number 421 (no remote head or open PR holds a 421 directory).
  2. **Counterfactual** (spec §1.4): apply plan §3 to both `From`s in the worktree, run every plan §6.4 command, then
     `git checkout -- src`. The failing assertions must be **exactly** the 12 of plan §8. A thirteenth is a block.
  3. Record each spec §6 row's outcome today. A row an older rule already refuses moves into the older-message set
     *in the spec* before tests are written.

  No `tests/` change.

## Phase 1: Characterisation, captured green first

All on unmodified production code, all green. Each `[P]` owns its own file.

- [x] **T010 [P] [US1]** C1 baseline: run every plan §6.4 command and quote the pass counts. No file change.
- [x] **T011 [P] [US1]** C2 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlNonAsciiHostCharacterisationTests.cs`.
- [x] **T012 [P] [US1]** C3 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlNonAsciiHostCharacterisationTests.cs`.
- [x] **T013 [P] [US1]** C4 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredNonAsciiHostTests.cs` and
  `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredNonAsciiHostTests.cs`.
- [x] **T014 [P] [US1]** C5 `tests/Architecture.Tests/Persistence/CameraUrlNonAsciiHostHydrationCharacterisationTests.cs`.
- [x] **T015 [P] [US1]** C6 `tests/Shared.Kernel.Tests/UrlRedactionNonAsciiHostCharacterisationTests.cs`.

Inputs are built with C# escapes, never read from a file. Display names name the code point.

**Gate:** T010-T015 green, counts quoted. Commit 1 (plan §7).

## Phase 2: Red (US1)

- [x] **T030 [P] [US1]** R1 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlNonAsciiHostTests.cs`.
- [x] **T031 [P] [US1]** R2 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlNonAsciiHostTests.cs`.
- [x] **T032 [P] [US1]** R3 `tests/Architecture.Tests/Persistence/CameraUrlNonAsciiHostRefusalTests.cs`.
- [x] **T033 [P] [US1]** R4 `tests/Architecture.Tests/CameraUrlNonAsciiHostEndpointTests.cs`.
- [x] **T034 [P] [US1]** R5 `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyNonAsciiHostReplayTests.cs`.
- [x] **T035 [P] [US1]** R6, the authorised edits E1-E5 of plan §8, in exactly these files and lines and no others:
  - E1 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlZoneCharsetCharacterisationTests.cs`: remove the 4 host rows
    from `AcceptedPendingTheOwnersQuestions`; the count changes from 6 to 2.
  - E2 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlZoneCharsetCharacterisationTests.cs`: the same.
  - E3 `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressHostCharsetLogRedactionTests.cs`:
    `RtspUrl.From(url)` becomes `RtspUrl.FromStored(url)` in `CommandFor`, plus one summary sentence. Assertions
    unchanged.
  - E4 `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedAdversarialOrderingTests.cs`: the
    Cyrillic-host `InlineData` moves from the "ignored" theory to the "rejects … fails" theory. Bodies unchanged.
  - E5 `tests/StreamDistribution.Infrastructure.Tests/Fakes/SourceRepairFakes.cs:110`: `StreamSourceUrl.From` becomes
    `StreamSourceUrl.FromStored`.
- [x] **T036 [US1]** G1, additive rows in `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs` (no existing line
  changes).

**Gate:** each red test is run and its failure quoted verbatim, with the expected reasons from plan §6.2: "did not
throw", 201/204, a gateway call recorded, or "not declared or called". E4 must be among the reds. The rows that pass on
arrival (name-only PATCH, the outcome-agreement half of R3, and E1/E2/E3/E5) are named as such. Commit 2. It must
build on its own: no test references `NonAsciiCharacterInsideThePlainHost` as a symbol.

## Phase 3: Implementation (backend-engineer; tests read-only)

- [x] **T040 [US1]** In `src/Shared.Kernel/UrlAuthority.cs`: FR-001's `NonAsciiCharacterInsideThePlainHost` (plan §3.1)
  and FR-007's two comment rewordings.

  In `src/CameraCatalog/Domain/Camera/RtspUrl.cs` and `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs`:
  `NonAsciiHostMessage`, the **last** `Satisfies`, and the summary sentence (plan §3.2). `FromStored` and
  `EnsureStoredInvariants` are untouched.

  Done when:
  - all of plan §6 is green, and every Phase 1 test and E1-E5 are green, unmodified;
  - the Release build passes;
  - `dotnet format --verify-no-changes` passes on the three projects.

  Commit 3.

## Phase 4: Post-green and adversary

- [x] **T060 [P] [US1]** Plan §6.3 direct rows in a new `tests/Shared.Kernel.Tests/UrlAuthorityNonAsciiHostTests.cs`.
  Commit 4.
- [ ] **T070 [US1]** `test-adversary` against FR-001 (plan §6.3 list). Findings go in as separate `test`/`fix` commits,
  or are recorded on #3023.

## Phase 5-7

- [ ] **T075 [US1]** Phase 5: spec §8 steps 1-5 on a booted stack. The verification note goes on the PR.
- [ ] **T080 [US1]** Phase 6: `backend-reviewer` plus `security-reviewer`. The PR body carries:
  - the red output (T030-T036);
  - the T001 counterfactual list (exactly the 12 assertions);
  - plan §8's before/after table, with the owner's authorisation of 2026-10-20;
  - the replay note (spec FR-004), including the older-change-now-fails outcome (E4);
  - the mDNS / U-label cost (spec §5);
  - `Closes #3023`, if nothing else of #3023 is open at merge (Q2 decided "no", item 4 closed, item 6 delivered by
    spec 416). Otherwise `Refs #3023`. Check the issue's state after the merge.
