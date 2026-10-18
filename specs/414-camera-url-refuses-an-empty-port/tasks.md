# Tasks 414: A camera URL with a ':' and no port is refused, and the three guarded `Uri` reads fold into one helper

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #3031 (feature-level, already on Project
#13 and in the autonomous lane; no per-task issues, per CLAUDE.md Phase 3)

**Two PRs, two colours, never mixed.**

| PR | Tasks | #3031 item | Phase 4a colour |
|---|---|---|---|
| PR 1, `fix/3031-camera-url-residuals` | T001-T080 | 3 | **red** (behaviour-changing). Characterisation T010-T016 observed green first, then unmodified. Red T030-T036 observed failing, with the failure quoted verbatim in the PR |
| PR 2, `refactor/3031-guarded-uri-read-fold`, cut from `develop` **after PR 1 merges** (or first, if the owner holds PR 1 on Q2) | T101-T180 | 7 | **characterisation, observed green** (behaviour-preserving). T110-T111 green before the fold and **unmodified** after. An assertion that has to change is a block, never an edit |

Item 6 is **not** in either PR (spec §9 Q1). Ambiguity resolves to red. The engineer may not edit any test from
T010-T036 or T110-T111.

**Roles.** Tests: `test-writer` (T001, T010-T036, T060, T101, T110-T111, T150), then `test-adversary` (T070, PR 1 only).
Production code: `backend-engineer` only (T040, T140). Review: `backend-reviewer` plus `security-reviewer` for PR 1
(T080), `backend-reviewer` for PR 2 (T180). No frontend, infra or migration work.

**Out of the lane:** spec §9 Q1 (item 6) and Q2 (confirm A-2 / A-3). The PR 1 body lists them and the orchestrator
comments them on #3031.

Format: `[ID] [P?] [Story] description, files`. `[P]` means the task owns files disjoint from every other `[P]` task
in the same phase (ADR-0109). One branch, one index: `[P]` means disjoint *files*; the orchestrator commits them in
sequence.

**Blocking.** No foundational Shared.Kernel, Contracts or AppHost task blocks a fan-out here. Inside each PR, Phase 0
blocks Phase 1, Phase 1's gate blocks Phase 2, and so on. **PR 1 and PR 2 never run at the same time**, because both edit
`src/Shared.Kernel/UrlAuthority.cs`.

---

# PR 1: the empty port (US1, item 3)

## Phase 0: Setup (blocks everything in PR 1)

- [ ] **T001 [US1]** On the branch tip:
  1. The `From` callers are exactly `RegisterCameraRequest.cs`, `CameraEndpoints.cs`, `ProvisionStreamCommandHandler.cs`
     and `RepointStreamCommandHandler.cs` (a comment in migration `20260728210420_PersistStreamSourceUrl.cs` also names
     it; that is not a call).
  2. **Counterfactual** (plan §8): apply FR-001 to both `From`s in the worktree (plan §3), run every plan §6.4 command,
     `git checkout -- src`. List every failing assertion by file and line. Each must be in plan §8's table or pin
     acceptance of an empty port (then add it to plan §8). Anything else is a block.
  3. Re-measure the fuzz floor (`The_corpora_reach_the_shapes_the_fix_is_about`) under the counterfactual, **with and
     without** the C7 corpus row. The architect measured 411 accepted today, 354 after FR-001 on five corpora, 458
     with the sixth. **Below 400 with C7 is a block.** Quote all three numbers in the PR.
  4. For each spec §6 row, record today's outcome. A row an older rule already refuses moves into the older-message set
     *in the spec* before tests are written.

  No `tests/` change.

## Phase 1: Characterisation, captured green first

All on unmodified production code, all green. Each `[P]` owns its own file.

- [ ] **T010 [P] [US1]** C1 baseline: run every plan §6.4 command and quote the pass counts. No file change.
- [ ] **T011 [P] [US1]** C2 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlEmptyPortCharacterisationTests.cs`.
- [ ] **T012 [P] [US1]** C3 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlEmptyPortCharacterisationTests.cs`.
- [ ] **T013 [P] [US1]** C4 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredEmptyPortTests.cs` and
  `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredEmptyPortTests.cs`.
- [ ] **T014 [P] [US1]** C5 `tests/Architecture.Tests/Persistence/CameraUrlEmptyPortHydrationCharacterisationTests.cs`.
- [ ] **T015 [P] [US1]** C6 `tests/Shared.Kernel.Tests/UrlRedactionEmptyPortCharacterisationTests.cs` (A-1).
- [ ] **T016 [P] [US1]** C7 `tests/Architecture.Tests/CameraUrlZoneDelimiterFuzzAdversarialTests.cs`: add
  `{ "tokens", 3031 }` to `Corpora` and update the "Measured on the five seeds" comment to T001's six-corpus figures.
  **No other line of that file.** Green (spec A-3).

The U+200E row is named by code point in its display name. Inputs are built with C# escapes, never read from a file.

**Gate:** T010-T016 green, counts quoted. Commit 1 (plan §7).

## Phase 2: Red (US1)

No red test calls `WrittenPortIsEmpty` directly, because a compile error is not a red. G1 reaches it by source scan.

- [ ] **T030 [P] [US1]** R1 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlEmptyPortTests.cs`.
- [ ] **T031 [P] [US1]** R2 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlEmptyPortTests.cs`.
- [ ] **T032 [P] [US1]** R3 `tests/Architecture.Tests/Persistence/CameraUrlEmptyPortRefusalTests.cs`.
- [ ] **T033 [P] [US1]** R4 `tests/Architecture.Tests/CameraUrlEmptyPortEndpointTests.cs`.
- [ ] **T034 [P] [US1]** R5 `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyEmptyPortReplayTests.cs`.
- [ ] **T035 [P] [US1]** R6, the plan §8 retirements (spec A-2), in exactly these files and no other lines:
  `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAuthorityHostAdversarialTests.cs`,
  `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAuthorityHostAdversarialTests.cs`,
  `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAuthorityHostCharacterisationTests.cs`,
  `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAuthorityHostCharacterisationTests.cs`,
  `tests/Architecture.Tests/Persistence/CameraUrlAuthorityHostRefusalTests.cs`, plus any T001 addition.
- [ ] **T036 [US1]** G1, additive rows in `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs` (no existing line
  changes).

**Gate:** each red test is run and its failure quoted verbatim, with the expected reasons from plan §6.2: "did not
throw", 201/204, a gateway call recorded, the reclassified persistence counts, or "not declared or called". The rows
that pass on arrival (name-only PATCH, outcome agreement, and the R6 removals, which are deletions) are named as such.
Commit 2.

## Phase 3: Implementation (backend-engineer; tests read-only)

- [ ] **T040 [US1]** FR-001: `WrittenPortIsEmpty` in `src/Shared.Kernel/UrlAuthority.cs`; the last rule and
  `EmptyPortMessage` in `src/CameraCatalog/Domain/Camera/RtspUrl.cs` and
  `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` (plan §3). All of plan §6 green, every Phase 1 test still
  green. Release build, `dotnet format --verify-no-changes` on the three projects. Commit 3.

## Phase 4: Post-green and adversary

- [ ] **T060 [P] [US1]** Direct rows in a new `tests/Shared.Kernel.Tests/UrlAuthorityEmptyPortTests.cs`: null is refused;
  every spec §6 row through `WrittenPortIsEmpty` directly; the two naive-scan traps (`RTSP://[2821::/64]/` false,
  `rtsp://[7E8C::2214%251/64]:/s` true); a first-`@` / last-`@` pair (`rtsp://@[::1]:/x`, `rtsp://@@[::1]:/x`); leading
  whitespace (`" rtsp://cam:/x"` true, matching `TrimStart`). Commit 4.
- [ ] **T070 [US1]** `test-adversary` against FR-001: `:` before a backslash terminator (`rtsp://cam:\x`), fullwidth
  colon U+FF1A, `%3A` as the port separator, several `:` after `]`, an unclosed `[`, `rtsp:\\cam:/x` and `rtsp:cam:/x`
  scheme spellings, and the spec 394 corpora run with FR-001 (no `Inspect` defect). Findings go in as separate
  `test`/`fix` commits, or are recorded on #3031.

## Phase 5-7

- [ ] **T075 [US1]** Phase 5: spec §8 steps 1-5 on a booted stack. The verification note goes on the PR.
- [ ] **T080 [US1]** Phase 6: `backend-reviewer` plus `security-reviewer`. The PR body carries:
  - the red output (T030-T036);
  - the T001 counterfactual list and the three fuzz-floor figures;
  - the replay note (spec FR-004);
  - A-1 to A-5, with A-2 and A-3 called out for the owner (Q2);
  - Q1 (item 6) for the owner;
  - `Refs #3031`, not a closing keyword (item 6 and PR 2 remain).

---

# PR 2: fold the guarded `Uri` reads (US2, item 7, behaviour-preserving)

## Phase 0

- [ ] **T101 [US2]** Cut `refactor/3031-guarded-uri-read-fold` from `develop` (after PR 1 merges, unless PR 1 is held).
  Check that `TryReadHost`, `TryReadUserInfo` (`src/Shared.Kernel/UrlAuthority.cs`) and `TryParse`
  (`src/Shared.Kernel/UrlRedaction.cs`) are byte-identical to `18d967db`
  (`git diff 18d967db -- src/Shared.Kernel/UrlRedaction.cs`, and the two members of `UrlAuthority.cs`). If not, re-read
  plan §4 against the new code first.

## Phase 1: Characterisation, observed green before the fold

- [ ] **T110 [US2]** C-F0: run the plan §6.5 suites and quote the counts. Then the **per-arm counterfactuals** on
  today's code (plan §6.5): remove one catch at a time, run, quote what turns red, restore with `git checkout -- src`.
  An arm that turns nothing red gets a C-F1 row before Phase 3. No file change.
- [ ] **T111 [US2]** C-F1 `tests/Shared.Kernel.Tests/UrlAuthorityGuardedReadCharacterisationTests.cs` (plan §6.5 rows
  (a)-(e), each with its `Uri` precondition). Green.

**Gate:** T110-T111 green, counts and counterfactuals quoted. Commit 1 of PR 2.

## Phase 3: The fold (backend-engineer; tests read-only)

- [ ] **T140 [US2]** Plan §4 in `src/Shared.Kernel/UrlAuthority.cs` and `src/Shared.Kernel/UrlRedaction.cs`.
  T110-T111 unmodified and green, counts identical to T110. **Post-fold counterfactual**: delete the helper's catch,
  run C-F1 and the two named existing suites, quote the failures, restore. Commit 2.

## Phase 4-7

- [ ] **T150 [US2]** G2, additive rows in `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs`, green on arrival.
  Counterfactual (reinstate `UrlRedaction.TryParse`, so G2 goes red) quoted. Commit 3.
- [ ] **T160 [US2]** Phase 5: `Phase 5: explicit run/test` (ADR-0037). Evidence: T110/T140 identical counts and the
  counterfactuals. No runtime behaviour to observe.
- [ ] **T180 [US2]** Phase 6: `backend-reviewer`, which must confirm that no assertion moved (`git diff develop -- tests`
  shows only the two new files' additions and G2's additive rows). The PR body says `Phase 4a: characterisation` and
  quotes T110/T140.

---

## Dependencies

```
PR 1:  T001 → T010–T016 [P] → T030–T036 [P] → T040 → T060 [P], T070 → T075 → T080 → merge
PR 2:  (PR 1 merged, or PR 1 held) → T101 → T110, T111 → T140 → T150 → T160 → T180 → merge
```

Parallel fan-out is possible only inside Phase 1 and Phase 2 of PR 1, where every `[P]` task owns its own file (T035
edits five existing files that no other task in its phase touches). T036 and T150 share
`UrlAuthoritySingleSourceTests.cs`, but in different PRs that run in sequence.
