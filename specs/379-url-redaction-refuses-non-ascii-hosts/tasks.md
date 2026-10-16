# Tasks 379: URL redaction refuses a host that is not plain ASCII

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2989 (feature-level;
no per-task issues).

**Phase 4a colour: RED** (behaviour-changing: `ToOrigin`'s output for a non-ASCII host changes),
**plus an adversarial pass**. Every red test compiles on unchanged code, so each must be seen
**failing on an assertion**, not on a compile error. A red test that arrives green is a phase-4
failure. The characterisation facts named green below (K2, K5, K6, and the "still sends / still
stores" facts in S2/S3) must arrive green and stay green unmodified.

**Engineer:** backend-engineer (T010). **Test-writer:** T001-T004. **Test-adversary:** T020.
**Security-sensitive: yes** — phase 6 runs `security-reviewer` alongside `/code-review`.
**New ADR:** no (ADR-0144: the owner delegated the direction on #2989).

**Forbidden in every task:** editing any existing test file (`UrlRedactionTests.cs`,
`UrlRedactionAdversarialTests.cs`, every `AuditPayloadRedaction*Tests.cs`,
`MediaMtxRtspGatewayLogRedactionTests.cs`, `ChangeCameraAddressLogRedactionTests.cs`, any
Integration.Tests file); any production file other than `src/Shared.Kernel/UrlRedaction.cs`;
`RtspUrl.cs`, `StreamSourceUrl.cs`, log templates, `AuditPayloadRedaction.cs`; the `Unparseable`
constant's text; `Shared.Contracts`; project references; ADRs, the constitution, CLAUDE.md;
suppressions, thresholds, `.editorconfig`, analyzer configs.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).
**Foundational:** T010 is the only production task and blocks nothing but phase 5. T001-T004 own four
disjoint new files in four test projects and run in parallel.

---

## Phase 1: Red (test-writer; production code untouched)

- [x] **T001** [P] [US1] New `tests/Shared.Kernel.Tests/UrlRedactionHostCharsetTests.cs` per plan
  §4.1: K1 (1014 generated rows, red), K2 (234 rows, green), K3 (BMP sweep, red), K4 (9 rows, red),
  K5 (13 rows, green), K6 (idempotence over K1 ∪ K4, green), K7 (10 rows, red). Marker checks
  case-insensitive. Run
  `dotnet test tests/Shared.Kernel.Tests -c Release --filter "FullyQualifiedName~UrlRedactionHostCharsetTests"`.
  Expected: K1 1014 failed, K3 failed with "126654" in its message, K4 9 failed, K7 10 failed;
  K2, K5, K6 passed;
  `UrlRedactionTests` / `UrlRedactionAdversarialTests` untouched and green (run
  `--filter "FullyQualifiedName~UrlRedaction"` to show both).
- [x] **T002** [P] [US1] New
  `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadHostCharsetRedactionTests.cs`
  per plan §4.2 S1 (a)-(c), red. Run
  `dotnet test tests/AuditObservability.Application.Tests -c Release --filter "FullyQualifiedName~AuditPayloadHostCharsetRedactionTests"`.
- [x] **T003** [P] [US1] New
  `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayHostCharsetLogRedactionTests.cs`
  per plan §4.2 S2: log facts red, "still sends the full source" green. Run
  `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~MediaMtxRtspGatewayHostCharsetLogRedactionTests"`.
- [x] **T004** [P] [US1] New
  `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressHostCharsetLogRedactionTests.cs`
  per plan §4.2 S3: log fact red, "still stores the full url" green. Run
  `dotnet test tests/CameraCatalog.Application.Tests -c Release --filter "FullyQualifiedName~ChangeCameraAddressHostCharsetLogRedactionTests"`.

Commit T001-T004 together: `test(2989): pin non-ASCII URL hosts to the redaction placeholder`.
Return the **verbatim** red output of all four runs (failure counts plus at least one quoted
assertion message per class, e.g. K1's `should not contain "SECRET"` on
`rtsp://cam／token＝secret`).

Gate: the orchestrator holds T001-T004 verbatim output, every red set failing on an assertion and
every green set passing, before dispatching T010.

## Phase 2: Green (backend-engineer)

- [x] **T010** [US1] `src/Shared.Kernel/UrlRedaction.cs` per plan §2 (I0-I3): both checks — (a) the
  host charset on `Uri.Host` and (b) the authority as written on the raw input — after the existing
  parse check, placeholder otherwise; nothing else in the method changes. A build with (a) alone must
  leave K7's first eight rows red: do not ship (a) alone.
  T001-T004 green **unmodified**. Then run plan §4.4 in full (four full test projects,
  Architecture.Tests, `dotnet build -c Release`, `dotnet format --verify-no-changes`). Stop any
  running AppHost first only if it is this worktree's and the user's (never a shared stack).
  Commit: `fix(2989): redact a URL with a non-ASCII host to the placeholder`.

## Phase 3: Adversarial (test-adversary)

- [x] **T020** [US1] New `tests/Shared.Kernel.Tests/UrlRedactionHostCharsetAdversarialTests.cs` per
  plan §4.3. Every new fact passes against T010; any that fails is a finding returned verbatim to the
  orchestrator (fix in a follow-up T011 commit, red first), not an edited test. Commit:
  `test(2989): adversarial host charset cases for URL redaction`.

## Phase 4: Verify, review, PR (orchestrator)

- [x] **T030** Phase 5 per spec §5 step 3 only if no other Aspire stack runs on the machine;
  otherwise the PR states step 2 stands as the evidence. Quote T001-T004 red output and the post-fix
  green runs in the PR body.
- [x] **T031** Phase 6: `/code-review` and `security-reviewer` (ask it to confirm the C1-C7 caller
  list, and whether any other redaction or log path renders `Uri.Host` / `Uri.Authority` without
  `ToOrigin`). Raise the two [Assumption]s in spec §6 and the ordinal-`"SECRET"` weakness (plan §6)
  in the PR body; offer a follow-up issue for the latter.
- [x] **T032** Re-check the spec number (379) against `origin/develop` and every open PR branch
  before `gh pr create --base develop`; PR body uses `Closes #2989`.

## Dependencies

T001 ‖ T002 ‖ T003 ‖ T004 -> T010 -> T020 -> T030 -> T031 -> T032.

## Phase 5: Third pass (D4/D5)

- [x] **T040** `UrlRedaction.ToOrigin`: strict `scheme://` form required for non-ASCII input (D4) and
  a typed `IndexOutOfRangeException` catch around `Uri.TryCreate` (D5). `UrlRedactionAuthorityLocationTests`
  (792) green unmodified; all earlier tests green unmodified.
