# Tasks 373: Camera URLs are logged as their origin only

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2908 (feature-level;
no per-task issues).

**Phase 4a colour: RED** (behaviour-changing: the rendered log text changes). R1-R3 must be seen
**failing on assertions** on unchanged production code; R4 is red by compilation (`UrlRedaction`
does not exist) and lands with the fix. A test that arrives green is a phase-4 failure.

**Engineer:** backend-engineer (T010-T011). **Test-writer:** T001-T003, T004 (R4 written, compile
red quoted). **Security-sensitive: yes** — phase 6 runs `security-reviewer` alongside `/code-review`.

**Forbidden in every task:** `RtspUrl.cs`, `StreamSourceUrl.cs`, any contract in `Shared.Contracts`,
any template text or placeholder name in either `Log.cs`; any existing assertion other than C7
line 89; any project reference; ADRs, the constitution, CLAUDE.md; suppressions, thresholds,
`.editorconfig`, analyzer configs; the two ScenarioSimulator sites (spec §6).

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).
**Foundational:** T010 (`Shared.Kernel`) blocks T011. T001-T003 are disjoint test projects and run
in parallel.

---

## Phase 1: Red (test-writer; production code untouched)

- [ ] **T001** [P] [US1] R1 — new
  `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayLogRedactionTests.cs`
  per plan §4 (stub handler recording bodies, existing `Fakes/CapturingLogger.cs`; Add + Repoint x
  the two SECRET URLs; exact line, no `SECRET`, body keeps the full URL). Run
  `dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~MediaMtxRtspGatewayLogRedactionTests"`;
  return verbatim output: the exact-line and no-`SECRET` facts **fail**, the body facts **pass**.
- [ ] **T002** [P] [US1] R2 — new
  `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressLogRedactionTests.cs` per plan
  §4 (existing fakes and `CapturingLogger`; exact line, no `SECRET`, stored URL is full). Do not
  edit `ChangeCameraAddressCommandHandlerTests.cs`. Run
  `dotnet test tests/CameraCatalog.Application.Tests -c Release --filter "FullyQualifiedName~ChangeCameraAddressLogRedactionTests"`;
  return verbatim output: exact-line and no-`SECRET` facts fail, stored-URL facts pass.
- [ ] **T003** [P] [US1] R3 — edit **only** the string on line 89 of
  `tests/Integration.Tests/StreamDistribution/MediaMtxReconcilerPassCharacterisationTests.cs` to
  `"… -> rtsp://10.0.5.5."`. Run
  `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~MediaMtxReconcilerPassCharacterisationTests"`
  **only if no other Aspire stack is running on the machine** (check first); return the verbatim
  failure (expected vs actual on that one element). If a stack is running, say so and leave R3's red
  to be argued from R1 and observed green in CI after T011; do not stop a shared stack.
- [ ] **T004** [US1] R4 — write `tests/Shared.Kernel.Tests/UrlRedactionTests.cs` per plan §4 in the
  working tree, run
  `dotnet test tests/Shared.Kernel.Tests -c Release --filter "FullyQualifiedName~UrlRedactionTests"`,
  return the verbatim compile error naming `UrlRedaction`. **Do not commit it** (it would break
  commit 1's build); hand the file to T010.
  Commit T001-T003 only: `test(2908): pin camera URL log lines to their origin`.

Gate: the orchestrator holds T001, T002 verbatim red output (and T003's, or the stated reason it
was not run) and T004's compile error before dispatching T010.

## Phase 2: Fix (backend-engineer; depends on Phase 1)

- [ ] **T010** [US1] **Foundational.** `src/Shared.Kernel/UrlRedaction.cs` per plan §1-§2
  (I1-I5). Add T004's `UrlRedactionTests.cs` unmodified. R4 green.
- [ ] **T011** [US1] Apply at S1-S4 per plan §3: `MediaMtxReconciler.cs:223` (+ `using`),
  `MediaMtxRtspGateway.cs:51` and `:67`, `ChangeCameraAddressCommandHandler.cs:64`, and
  `CameraCatalog/Application/Log.cs:32` parameter `RtspUrl url` -> `string url`. Do not touch any
  test. R1, R2, R4 green unmodified; R3 green (locally if T003 ran it, else CI); then run plan §5's
  full-project, Architecture.Tests, Release build and `dotnet format --verify-no-changes`. Run the
  plan §4 counterfactuals in a scratch edit, confirm each goes red, revert, `git diff` clean.
  Commit T010+T011 together: `fix(2908): log camera URLs as scheme, host and port only`.

## Phase 3: Verify, review (orchestrator)

- [ ] **T020** Phase 5 per spec §5 step 3 (stack booted only if no other stack runs): register a
  camera with `?token=PHASE5SECRET`, change its address, confirm the origin-only lines in the
  dashboard and no `PHASE5SECRET` hit in `stream-distribution` / `camera-catalog` logs, and that the
  stream still plays. Quote T001-T004 red output and the post-fix green runs in the PR.
- [ ] **T021** Phase 6: `/code-review` and `security-reviewer` (ask it to look for any other path
  where a camera URL reaches a sink). Re-check spec number 373 is still free.
- [ ] **T022** Open a follow-up issue for `src/ScenarioSimulator/Log.cs:71`
  (`SkippedNonSimulatedCamera`, spec §6), linking #2908. Not fixed here.

## Dependencies

T001 ‖ T002 ‖ T003, then T004 -> T010 -> T011 -> T020 -> T021 -> T022.
Commit 1 (`test`) builds and leaves R1-R3 red by design (the ADR-0139 pair; see CLAUDE.md on
bisecting across such a pair). Commit 2 (`fix`) builds and is green.
