# Tasks 411: A camera URL's IPv6 zone holds only printable ASCII, a host of only dots names nothing, and the audit's bracket scan folds onto `UrlAuthority`

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #3023 (feature-level, already on Project
#13 and in the autonomous lane; no per-task issues, per CLAUDE.md Phase 3)

**Two PRs, two colours, never mixed.**

| PR | Tasks | Phase 4a colour |
|---|---|---|
| PR 1, `fix/3023-camera-url-authority-residuals` | T001-T080 | **red** (behaviour-changing). Characterisation T010-T016 observed green first, then unmodified. Red T030-T036 observed failing, with the failure quoted verbatim in the PR |
| PR 2, `refactor/3023-bracket-scan-fold`, cut from `develop` **after PR 1 merges** | T101-T180 | **characterisation, observed green** (behaviour-preserving). T110-T111 green before the fold and **unmodified** after. An assertion that has to change is a block, never an edit |

Ambiguity resolves to red. The engineer may not edit any test from T010-T036 or T110-T111.

**Roles.** Tests: `test-writer` (T001, T010-T036, T060, T101, T110-T111, T150), then `test-adversary` (T070, PR 1
only). Production code: `backend-engineer` only (T040-T041, T140). Review: `backend-reviewer` plus
`security-reviewer` for PR 1 (T080), `backend-reviewer` for PR 2 (T180). No frontend, infra or migration work.

**Out of the lane:** spec §9 Q1-Q3. The PR 1 body lists them, and the orchestrator comments them on #3023. Item 4 is
**not** closed by the lane (Q3).

Format: `[ID] [P?] [Story] description, files`. `[P]` means the task owns files disjoint from every other `[P]` task
in the same phase (ADR-0109). One branch, one index: `[P]` means disjoint *files*, and the orchestrator commits them
in sequence.

**Blocking.** No foundational Shared.Kernel, Contracts or AppHost task blocks a fan-out here. Inside each PR, Phase 0
blocks Phase 1, Phase 1's gate blocks Phase 2, and so on. **PR 2 is blocked on PR 1's merge**, because both edit
`src/Shared.Kernel/UrlAuthority.cs`.

---

# PR 1: the zone charset and punctuation-only host (US1, US2)

## Phase 0: Setup (blocks everything in PR 1)

- [ ] **T001 [US1, US2]** On the branch tip:
  1. `grep -rn "RtspUrl\.From(\|StreamSourceUrl\.From(" src` lists exactly `RegisterCameraRequest.cs:19`,
     `CameraEndpoints.cs:323`, `ProvisionStreamCommandHandler.cs:33` and `RepointStreamCommandHandler.cs:108`
     (line numbers may drift; the set must not).
  2. **Counterfactual** (plan §8): apply FR-001 and FR-002 to both `From`s in the worktree, run every plan §6.4
     command, and `git checkout -- src`. List every failing assertion by file and line in plan §8 and the PR. Any hit
     that pins *acceptance* is a block for the owner (plan §8).
  3. Re-measure the A-8 fuzz floor (`The_corpora_reach_the_shapes_the_fix_is_about`, accepted at least 400) under
     the counterfactual. The architect measured 430 today and 411 after. **Below 400 is a block.**
  4. For each spec §6 row, record today's outcome. A row that an older rule already refuses (for example `rtsp://-._/x`
     if `Uri.TryCreate` fails on it) moves into the "older message" set *in the spec*, before tests are written.

  No `tests/` change unless step 4 moves a row.

## Phase 1: Characterisation, captured green first

All on unmodified production code, all green. Each `[P]` owns its own new file.

- [ ] **T010 [P] [US1, US2]** C1 baseline: run every plan §6.4 command and quote the pass counts. No file change.
- [ ] **T011 [P] [US1, US2]** C2 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlZoneCharsetCharacterisationTests.cs`.
- [ ] **T012 [P] [US1, US2]** C3 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlZoneCharsetCharacterisationTests.cs`.
- [ ] **T013 [P] [US1, US2]** C4 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredZoneCharsetTests.cs` and
  `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredZoneCharsetTests.cs`.
- [ ] **T014 [P] [US1, US2]** C5 `tests/Architecture.Tests/Persistence/CameraUrlZoneCharsetHydrationCharacterisationTests.cs`.
- [ ] **T015 [P] [US1, US2]** C6 `tests/Shared.Kernel.Tests/UrlRedactionZoneCharsetCharacterisationTests.cs` (A-1).
- [ ] **T016 [P] [US1, US2]** C7 `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadZoneCharsetCharacterisationTests.cs` (A-1).

Theory data holding a raw control or invisible character is named by code point in the display name
(`$"U+{cp:X4} in zone"`). Inputs are built with C# escapes, never read from a file.

**Gate:** T010-T016 green, counts quoted. This is commit 1 (plan §7).

## Phase 2: Red (US1, US2)

No red test calls `NonPrintableCharacterInsideTheZone` or `IsPunctuationOnlyHost` directly, because a compile error
is not a red. G1 reaches them by source scan.

- [ ] **T030 [P] [US1]** R1 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlZoneCharsetTests.cs`.
- [ ] **T031 [P] [US2]** R2 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlPunctuationHostTests.cs`.
- [ ] **T032 [P] [US1, US2]** R3 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlZoneCharsetTests.cs` and
  `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlPunctuationHostTests.cs`.
- [ ] **T033 [P] [US1, US2]** R4 `tests/Architecture.Tests/Persistence/CameraUrlZoneCharsetRefusalTests.cs`.
- [ ] **T034 [P] [US1, US2]** R5 `tests/Architecture.Tests/CameraUrlZoneCharsetEndpointTests.cs`.
- [ ] **T035 [P] [US1, US2]** R6 `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyZoneCharsetReplayTests.cs`.
- [ ] **T036 [US1, US2]** G1, additive rows in `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs` (an
  existing file: no existing line changes).

**Gate:** each red test is run and its failure quoted verbatim, with the expected reasons from plan §6.2: "did not
throw", 201/204, a gateway call recorded, or "not declared or called". The rows that pass on arrival (the name-only
PATCH, outcome agreement) are named as such. Commit 2.

## Phase 3: Implementation (backend-engineer; tests read-only)

- [ ] **T040 [US1]** FR-001: `NonPrintableCharacterInsideTheZone` plus the private zone scan in
  `src/Shared.Kernel/UrlAuthority.cs`; the rule and `NonPrintableZoneMessage` in
  `src/CameraCatalog/Domain/Camera/RtspUrl.cs` and `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs`
  (plan §3.1-§3.2). R1, R3 (zone), R4 (zone rows), R5 (zone rows), R6 (first row) and G1 (zone) turn green, and
  every Phase 1 test stays green. Commit 3.
- [ ] **T041 [US2]** (depends on T040, same files) FR-002: `IsPunctuationOnlyHost`, `HostIsNotPunctuationOnly` and
  `PunctuationOnlyHostMessage`. All of plan §6 is green. Release build, `dotnet format --verify-no-changes` on the
  three projects. Commit 4.

## Phase 4: Post-green and adversary

- [ ] **T060 [P] [US1, US2]** Direct rows in a new `tests/Shared.Kernel.Tests/UrlAuthorityZoneCharsetTests.cs`:
  - each octet 0x00-0xFF percent-encoded in `%25eth0{%XX}`, with exactly 0x21-0x7E accepted;
  - each code unit U+0000-U+00FF, plus the spec §6 invisible set, raw in the zone;
  - the bare-`%`, `/64`-prefix and first-`@` / last-`@` reading forms;
  - `IsPunctuationOnlyHost` over `""`, `.`, `..`, `-`, `_`, `-._`, `cam.`, `a-b`, `cam_1`, `0`.

  Commit 5.
- [ ] **T070 [US1, US2]** `test-adversary` against FR-001 and FR-002: zone readings under several `@`, a zone that
  holds `]` percent-encoded (`%5D`), nested `%2525`, surrogate pairs in the zone, `IsPunctuationOnlyHost` against
  what `Uri` normalises (`RTSP://..`, fullwidth full stop U+FF0E, ideographic full stop U+3002, which `Uri` may map to
  `.`). Findings go in as separate `test`/`fix` commits, or are recorded on #3023.

## Phase 5-7

- [ ] **T075 [US1, US2]** Phase 5: spec §8 steps 1-5 on a booted stack. The verification note goes on the PR.
- [ ] **T080 [US1, US2]** Phase 6: `backend-reviewer` plus `security-reviewer`. The PR body carries:
  - the red output (T030-T036);
  - the T001 counterfactual and fuzz-floor figures;
  - the replay note (spec FR-005);
  - A-1 to A-8;
  - Q1-Q3 for the owner;
  - `Refs #3023`, not a closing keyword. Item 3's host half and item 4 stay open.

---

# PR 2: fold the bracket scan (US3, behaviour-preserving)

## Phase 0 (blocked on PR 1's merge)

- [ ] **T101 [US3]** Cut `refactor/3023-bracket-scan-fold` from `develop` after PR 1 merges. Check that
  `AuditPayloadRedaction.cs`'s `CarriesZone` / `BracketContentIsAddressOnly` / `IsAddressText` are byte-identical to
  `00a66722` (`git diff 00a66722 -- src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs`). If
  they are not, re-read plan §4 against the new code before writing the oracle.

## Phase 1: Characterisation, observed green before the fold

- [ ] **T110 [US3]** C-F0: run the plan §6.5 suites and quote the counts. No file change.
- [ ] **T111 [US3]** C-F1 `tests/Architecture.Tests/AuditPayloadBracketFoldCharacterisationTests.cs`, with the oracle
  copied verbatim, the reach floors, and the explicit userinfo-bracket rows. It includes the one-word `internal`
  visibility change to `CameraUrlZoneDelimiterFuzzAdversarialTests.Generate`, and no other line of that file. Green.

**Gate:** T110-T111 green, counts quoted. This is commit 1 of PR 2.

## Phase 3: The fold (backend-engineer; tests read-only)

- [ ] **T140 [US3]** Plan §4: `UrlAuthority.BracketContentIsAddressOnly` in `src/Shared.Kernel/UrlAuthority.cs`; delete
  the two private members and call the new one in
  `src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs`. T110-T111 unmodified and green, with
  the counts identical to T110. **Counterfactual**: drop the userinfo-bracket arm, run C-F1, quote the failure, then
  restore the arm. Commit 2.

## Phase 4-7

- [ ] **T150 [US3]** G2, additive rows in `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs`, green on
  arrival. Its counterfactual (reinstate a local `TextFromTheAuthority(` call, so G2 goes red) is quoted. Commit 3.
- [ ] **T160 [US3]** Phase 5: `Phase 5: explicit run/test` (ADR-0037). The evidence is T110/T140's identical counts and
  C-F1's counterfactual. There is no runtime behaviour to observe.
- [ ] **T180 [US3]** Phase 6: `backend-reviewer`, which must confirm that no assertion in any file moved
  (`git diff develop -- tests` shows only additions plus the one `internal`). The PR body says
  `Phase 4a: characterisation` and quotes T110/T140.

---

## Dependencies

```
PR 1:  T001 → T010–T016 [P] → T030–T036 [P] → T040 → T041 → T060 [P], T070 → T075 → T080 → merge
PR 2:  (PR 1 merged) → T101 → T110, T111 → T140 → T150 → T160 → T180 → merge
```

Parallel fan-out is possible only inside Phase 1 and Phase 2 of PR 1, where every `[P]` task creates its own file.
T036 shares `UrlAuthoritySingleSourceTests.cs` with T150, but they are in different PRs and run in sequence.
