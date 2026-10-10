# Tasks 334: The naming rule the build never read

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2764

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2764 carries `agent:ready`, decision recorded on the issue
  (2026-10-08): enforce, as an advisory **warning**.
- **Engineers:** `test-writer` (4a: probe counterfactual + characterisation baseline) →
  **`infra-engineer`** (4b: `.editorconfig`, `Directory.Build.props`, the two renames, docs). The
  renames are two private identifiers with no design content; they ride with the build change
  rather than pulling in `backend-engineer`.
- **New ADR:** no (spec §7).
- **Phase 4a colour: RED** for the build change (a build counterfactual — the build gains a
  diagnostic), **characterisation (green)** for the two renames. Declared separately; ambiguity
  resolves to red.
- **Latency:** N/A (spec §8).
- **Board:** #2764 is on Project #13, status Todo (GraphQL, 2026-10-10). No per-task issues.
- **#2556:** closed 2026-10-07; nothing to sequence around (spec §1.6).
- **Assumption A1** (spec §2): the rule is narrowed to exempt PascalCase `const` /
  `static readonly`. Overlaps #2174 — comment there after merge; do not close it from the lane.

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files (ADR-0109).

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Phase 4a — red counterfactual + characterisation (test-writer). Quote every output verbatim.

The probe is defined in plan §5. `infra-engineer` may not edit it to change an outcome. Every
build uses `-c Release --no-incremental`.

- [ ] **T001** [US1] On the **unchanged** tree, create `src/Shared.Kernel/NamingProbe.cs` (plan §5)
      and run `dotnet build src/Shared.Kernel/SmartSentinelEye.Shared.Kernel.csproj -c Release
      --no-incremental`. **Expected: exit 0, zero IDE1006, zero diagnostics of any kind on
      `NamingProbe.cs`.** This is the red. Any other diagnostic on the probe = confounded probe:
      stop and report.
- [ ] **T002** [P] [US1] Same, `tests/Shared.Kernel.Tests/NamingProbe.cs` +
      `dotnet build tests/Shared.Kernel.Tests/SmartSentinelEye.Shared.Kernel.Tests.csproj -c Release
      --no-incremental`. Expected: exit 0, zero IDE1006. (`[P]` with T001: different projects.)
- [ ] **T003** [P] [US1] Characterisation baseline for the renames: `dotnet test
      tests/Automation.Infrastructure.Tests -c Release` — record passed/failed/skipped. Build
      `tests/Integration.Tests` in Release (`AppHostStackStatusTests` needs the stack; build-only
      locally, CI runs it). All green, or stop.

**Gate for 4a:** T001/T002 show zero IDE1006. If either shows one, the premise (spec §1.2) is
wrong — stop and report rather than proceed.

## Phase 4b — implementation (infra-engineer). Commits in plan §4 order.

- [ ] **T004** [US1] `src/Automation/Infrastructure/Cache/InMemoryRuleCache.cs`: `_byTrigger` →
      `byTrigger` (5 references). Case-sensitive grep for an existing `byTrigger` first (plan §3).
      Commit 1.
- [ ] **T005** [P] [US1] `src/AppHost/StackStatusReport.cs`: `notReported` → `NotReported`
      (lines 34, 167, 226). Commit 2. (`[P]` with T004: disjoint files; one branch, so committed in
      sequence.)
- [ ] **T006** [US1] `.editorconfig` (plan §2.1, exact keys) **and** `Directory.Build.props`
      (plan §2.2, unconditional `PropertyGroup`) — **one commit**, indivisible. Commit 3.
      Depends on T004, T005.
- [ ] **T007** [US1] *(after T006, probes still present)* Re-run T001's and T002's exact commands.
      **Expected each: exactly one `warning IDE1006` on `_probeValue` ("Prefix '_' is not
      expected"), none on `ProbeLimit` / `ProbeShared`, exit 0.** Re-run T003 — identical totals,
      no test edited. **Then delete both probe files**; `git status` must not list them.
- [ ] **T008** [US1] `dotnet build SmartSentinelEye.slnx -c Release` — exit 0, `grep -c "warning
      IDE1006"` = **0**. Must finish (plan §5); a running stack is the orchestrator's call, not
      yours.
- [ ] **T009** [P] [US1] Docs, commit 4: CLAUDE.md house-rule bullet (~line 399) and
      CONTRIBUTING.md §Code style (~206-211), wording per plan §4. Nothing else in either file.

## Phase 5 — verify

- [ ] **T010** Verification note on the PR: T001/T002 (red) beside T007 (green, exit 0); T008's
      zero; informational `dotnet format style tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj
      --verify-no-changes --diagnostics IDE1006 --severity warn` count (expected 0, was 350 on
      develop). `git diff --stat origin/develop` lists only `.editorconfig`,
      `Directory.Build.props`, the two `src` files, `CLAUDE.md`, `CONTRIBUTING.md`,
      `specs/334-*`. Latency: N/A.

## Phase 6 — review

- [ ] **T011** `infra-reviewer`: confirm the carve-out is in the unconditional `PropertyGroup`
      (a test-project `_x` must not fail Release); that the T007 exit codes are 0; that no probe
      file is committed; that `.editorconfig` keys match plan §2.1. `backend-reviewer` not needed
      (two private renames, characterised).

## Phase 7

- [ ] **T012** PR to `develop` (`--base develop`), body quotes T001/T002/T007/T008 verbatim,
      `Closes #2764`. After merge: comment on #2174 (naming half resolved, 350 → 0) and #2175
      (naming blocker gone; `error WHITESPACE` remains). Re-check the spec number is still free
      before opening.

## Dependencies

T001, T002, T003 (parallel) → T004, T005 (any order) → T006 → T007 → T008 → T009 → T010 → T011 →
T012. Foundational: T006 — nothing after it can be observed without it.
