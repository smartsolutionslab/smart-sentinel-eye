# Tasks 381: URL redaction refuses a userinfo the parser reads as host and port

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2993 (feature-level; no
per-task issues).

**Phase 4a colour: RED** (behaviour-changing: `ToOrigin`'s output for an input with an `@` after the
authority changes), **plus an adversarial pass**. Every red test compiles on unchanged code, so each must
be seen **failing on an assertion**, not on a compile error. A red test that arrives green is a phase-4
failure. The characterisation facts named green below (U2, U3, U7, U8; S1 (c); the "still sends / still
stores / still configured" facts in S2-S4) must arrive green and stay green unmodified.

**Engineer:** backend-engineer (T010). **Test-writer:** T001-T004. **Test-adversary:** T020.
**Security-sensitive: yes** — phase 6 runs `security-reviewer` alongside `/code-review`.
**New ADR:** no (ADR-0144: the owner delegated the direction on #2993).

**Forbidden in every task:** editing any existing test file (every `UrlRedaction*Tests.cs`, every
`AuditPayload*Redaction*Tests.cs`, `MediaMtxSourceRepairAdversarialTests.cs`, every `*LogRedactionTests.cs`,
any Integration.Tests file); any production file other than `src/Shared.Kernel/UrlRedaction.cs`;
`RtspUrl.cs`, `StreamSourceUrl.cs` (accepted input is an owner decision, spec §6), log templates,
`AuditPayloadRedaction.cs`; the `Unparseable` constant's text; `Shared.Contracts`; project references;
ADRs, the constitution, CLAUDE.md; suppressions, thresholds, `.editorconfig`, analyzer configs.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).
**Foundational:** none — no Shared.Contracts, AppHost or Aspire change. T010 is the only production task.
T001-T004 own disjoint new files in four test projects and may run in parallel (T003 owns two files in
one project).

---

## Phase 1: Red (test-writer; production code untouched)

- [x] **T001** [P] [US1] New `tests/Shared.Kernel.Tests/UrlRedactionUserInfoAsHostTests.cs` per plan
  §4.1: U1 (396 rows, red), U2 (564, green), U3 (count fact, green), U4 (49,152-input sweep fact, red with
  `768`), U5 (18 rows, red), U6 (6 rows, red), U7 (22 rows, green), U8 (idempotence over U1 ∪ U5 ∪ U6,
  green). Build U4's inputs in the test body (U+0000 does not survive theory serialisation). Run
  `dotnet test tests/Shared.Kernel.Tests -c Release --filter "FullyQualifiedName~UrlRedactionUserInfoAsHostTests"`.
  Expected: **420 + 1 failed** (U1 396, U5 18, U6 6, U4 1, message containing `768`); U2, U3, U7, U8
  passed. Then `--filter "FullyQualifiedName~UrlRedaction"` to show every pre-existing class still green.
- [x] **T002** [P] [US1] New
  `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadUserInfoRedactionTests.cs` per plan
  §4.2 S1: (a), (b) red; (c) green. Run
  `dotnet test tests/AuditObservability.Application.Tests -c Release --filter "FullyQualifiedName~AuditPayloadUserInfoRedactionTests"`.
- [x] **T003** [P] [US1] New
  `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayUserInfoLogRedactionTests.cs`
  (S2: two log facts red, two "still sends" facts green) and
  `tests/StreamDistribution.Infrastructure.Tests/Reconciler/MediaMtxSourceRepairUserInfoLogRedactionTests.cs`
  (S3: log fact red, "still configured" fact green). Run
  `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~UserInfoLogRedactionTests"`.
- [x] **T004** [P] [US1] New
  `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressUserInfoLogRedactionTests.cs` per plan
  §4.2 S4: log fact red, "still stores the full url" green. Run
  `dotnet test tests/CameraCatalog.Application.Tests -c Release --filter "FullyQualifiedName~ChangeCameraAddressUserInfoLogRedactionTests"`.

Commit T001-T004 together: `test(2993): pin a userinfo cut short by # ? or / to the redaction placeholder (red)`.
Return the **verbatim** red output of all four runs (failure counts plus at least one quoted assertion
message per class, e.g. U1's `should be "<unparseable url>" but was "rtsp://admin:12345"` and U4's `768`).

Gate: the orchestrator holds T001-T004 verbatim output, every red set failing on an assertion and every
green set passing, before dispatching T010.

## Phase 2: Green (backend-engineer)

- [x] **T010** [US1] `src/Shared.Kernel/UrlRedaction.cs` per plan §2: add `AtSignFollowsTheAuthority`
  (scheme prefix via the existing `IsSchemeCharacter`, skip the `/` `\` run, first `AuthorityTerminators`
  character, any `@` at or after it) as the **first** disjunct of the refusal condition; nothing else in
  the method changes. Counterfactual before committing: a build that locates the authority only after a
  literal `://` must leave U5's `rtsp:\\...` and `http:/\...` rows red — do not ship that form.
  T001-T004 green **unmodified**. Then run plan §4.4 in full. Stop any running AppHost first only if it is
  this worktree's and the user's (never a shared stack).
  Commit: `fix(2993): redact a URL whose @ follows the authority to the placeholder`.

## Phase 3: Adversarial (test-adversary)

- [x] **T020** [US1] New `tests/Shared.Kernel.Tests/UrlRedactionUserInfoAsHostAdversarialTests.cs` per plan
  §4.3. Every new fact passes against T010; any that fails is a finding returned verbatim to the
  orchestrator (fix in a follow-up T011 commit, red first), not an edited test. Commit:
  `test(2993): adversarial userinfo cases for URL redaction`.

## Phase 4: Verify, review, PR (orchestrator)

- [ ] **T030** Phase 5 per spec §5 step 3 only if no other Aspire stack runs on the machine; otherwise the
  PR states step 2 stands as the evidence. Quote T001-T004 red output and the post-fix green runs in the
  PR body.
- [ ] **T031** Phase 6: `/code-review` and `security-reviewer` (ask it to confirm no other redaction or log
  path renders `Uri.Host` / `Uri.Authority` / `Uri.Port` of a camera URL without `ToOrigin`). Raise in the
  PR body: the narrowing of the owner's suggested form (spec §6, with the two pinned rows it would change),
  the `\` [Assumption], the accepted over-refusals, the owner follow-up on `RtspUrl` / `StreamSourceUrl`,
  and the `mailto:` idempotence remark; offer follow-up issues for the last two.
- [ ] **T032** Re-check the spec number (381) against `origin/develop` and every open PR branch (380 is on
  the unmerged `refactor/2971-eventingestion-api-s107`) before `gh pr create --base develop`; PR body
  uses `Closes #2993`.

## Dependencies

T001 ‖ T002 ‖ T003 ‖ T004 -> T010 -> T020 -> T030 -> T031 -> T032.

## T010 outcome

Implemented as `AtSignFollowsTheAuthority` (first disjunct in `ToOrigin`), commit 9cb12308. Shared.Kernel
Tests 14,786 passed / 0 failed (421 reds green, unmodified). Counterfactuals on the new scan only
(Shared.Kernel.Tests failures): no `#` 147, no `?` 134, no `/` 137 (each: U4 sweep reports 256 leaking
inputs), marker run not skipped 12, check removed 421 (U4 reports 768). Independent sweep, 230,400 inputs
(8 schemes x 5 marker spellings x 8 shapes x 4 secrets x 10 delimiters incl. percent-encoded x 3 hosts x
3 tails x 2): 0 residual leaks; 20,052 outputs differ from develop, all with an `@` at or after the first
terminator. Release build 0 errors, S107 113 (#2971 not merged on this base) / S138 21 / S104 0.
