# Tasks 456: A Codespace that builds, tests, and says what it cannot do

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3030 (feature-level, on Project #13)

Format: `[ID] [P?] [Story] description — file(s)`. One story (US1). No foundational tasks: Shared.Kernel,
Shared.Contracts and AppHost are untouched. Engineer: **infra-engineer**.

## Phase 4a — checks first (no product test; spec §Testing)

- [ ] T001 [US1] Run the validation command against `develop`'s definition as it stands; quote output (baseline:
  expected exit 0 — if it fails, that failure is the first thing fixed and is quoted). — no files.
- [ ] T002 [US1] Counterfactual: a scratch copy of `.devcontainer/` with a broken JSON / unknown Feature makes the
  validation command exit non-zero; quote. Not committed. Depends on T001.
  **Half MET, half NOT MET.** Unknown Feature / build failure: exits non-zero (met). Syntax error: **NOT MET** —
  `@devcontainers/cli@0.89.0` parses `devcontainer.json` leniently; a missing comma and an unclosed brace both exit 0.
  The JSON syntax check is not part of this PR; follow-up.

## Phase 4b — implementation

- [ ] T003 [US1] FR-3 toolchain assertion (dotnet vs `global.json`, node vs `engines.node`, pnpm vs `packageManager`)
  — `.devcontainer/post-create.sh`. Depends on T001.
- [ ] T004 [US1] Counterfactual for T003: tightened bound fails post-create with expected/actual named; quote. Not committed.
  Depends on T003.
- [ ] T005 [P] [US1] FR-4: check the devcontainer/Codespaces schema for port visibility; declare `private` only if
  supported — `.devcontainer/devcontainer.json` (possibly unchanged; record which).
- [ ] T006 [P] [US1] FR-1 + FR-2 docs page — `docs/codespaces.md`; link line — `README.md`. Content per spec FR-1;
  validation command with exact pinned CLI version; T005's finding on visibility.

T005 and T006 own disjoint files from T003 and from each other (T006 consumes T005's finding as text only — write T006
after T005 if run sequentially).

## Phase 5 — verify

- [ ] T007 [US1] `devcontainer up` + `exec`: toolchain assertion, `dotnet build -c Release`, unit-test projects,
  `pnpm lint`, `pnpm typecheck`, `pnpm test`; a no-op commit showing the Husky hook runs. Quote each. A failure that
  reproduces on Linux CI `develop` head is recorded, not fixed. Depends on T003-T006. Check C: free space first.
- [ ] T008 [US1] Record unobserved: Codespace boot, in-Codespace Aspire stack and dashboard reachability, full-stack
  machine size. Verification note on the PR.

## Phase 6 — review

- [ ] T009 `infra-reviewer` (ports, secrets, whether the checks prove anything).
