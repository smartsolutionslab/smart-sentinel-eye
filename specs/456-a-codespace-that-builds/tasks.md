# Tasks 456: A Codespace that builds, tests, and says what it cannot do

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3030 (feature-level, on Project #13)

Format: `[ID] [P?] [Story] description — file(s)`. One story (US1). No foundational tasks: Shared.Kernel,
Shared.Contracts and AppHost are untouched. Engineer: **infra-engineer**.

## Phase 4a — checks first (no product test; spec §Testing)

- [x] T001 [US1] Run the validation command against `develop`'s definition as it stands; quote output (baseline:
  expected exit 0 — if it fails, that failure is the first thing fixed and is quoted). — no files.
  Evidence: the command ran; what it does and does not catch is recorded in docs/codespaces.md ("Validate the definition without a Codespace").
- [ ] T002 [US1] Counterfactual: a scratch copy of `.devcontainer/` with a broken JSON / unknown Feature makes the
  validation command exit non-zero; quote. Not committed. Depends on T001.
  **Half MET, half NOT MET.** Unknown Feature / build failure: exits non-zero (met). Syntax error: **NOT MET** —
  `@devcontainers/cli@0.89.0` parses `devcontainer.json` leniently; a missing comma and an unclosed brace both exit 0.
  The JSON syntax check is not part of this PR; follow-up.
  **Syntax-error half closed by spec 461 (#3151).** Counterfactual, transient and reverted (git diff empty after):
  the comma ending line 3 of `.devcontainer/devcontainer.json` removed, then `pnpm test:guards` went RED on the
  devcontainer checks (`pass 317 / fail 2`):
  `✖ the real devcontainer.json parses to an object` and `✖ the CLI exits 0 on the real devcontainer.json`, with
  `.devcontainer\devcontainer.json:4:3: Expected ',' or '}' after property value in JSON at position 99 (line 4 column 3)`.
  Restored: `pnpm test:guards` green again (`pass 319 / fail 0`); `node --test scripts/check-devcontainer-json.test.mjs`:
  `tests 11 / pass 11 / fail 0`.
  Both halves now met.

## Phase 4b — implementation

- [ ] T003 [US1] FR-3 toolchain assertion (dotnet vs `global.json`, node vs `engines.node`, pnpm vs `packageManager`)
  — `.devcontainer/post-create.sh`. Depends on T001.
  Not ticked: implemented in `.devcontainer/check-toolchain.mjs` (called from post-create.sh), but no post-create run is recorded.
- [x] T004 [US1] Counterfactual for T003: tightened bound fails post-create with expected/actual named; quote. Not committed.
  Depends on T003.
  Evidence (scratch copies, not committed; `node .devcontainer/check-toolchain.mjs`, all exit 1): engines.node ">=99" -> "node: expected satisfies engines.node ">=99", actual 24.21.0"; sdk.version 99.0.100 -> "dotnet: expected an installed SDK >= 99.0.100 ..., actual <installed SDK list>"; rollForward latestFeature -> "check needs updating for rollForward latestFeature". Run on the host, not in post-create.
- [ ] T005 [P] [US1] FR-4: check the devcontainer/Codespaces schema for port visibility; declare `private` only if
  supported — `.devcontainer/devcontainer.json` (possibly unchanged; record which).
  Not ticked: docs/codespaces.md states the schema has no port-visibility key, but no check output is quoted.
- [ ] T006 [P] [US1] FR-1 + FR-2 docs page — `docs/codespaces.md`; link line — `README.md`. Content per spec FR-1;
  validation command with exact pinned CLI version; T005's finding on visibility.
  Not ticked: the page exists, but the README link and its fit to spec FR-1 are not confirmed by any record here.

T005 and T006 own disjoint files from T003 and from each other (T006 consumes T005's finding as text only — write T006
after T005 if run sequentially).

## Phase 5 — verify

- [ ] T007 [US1] `devcontainer up` + `exec`: toolchain assertion, `dotnet build -c Release`, unit-test projects,
  `pnpm lint`, `pnpm typecheck`, `pnpm test`; a no-op commit showing the Husky hook runs. Quote each. A failure that
  reproduces on Linux CI `develop` head is recorded, not fixed. Depends on T003-T006. Check C: free space first.
  Not ticked: no per-command output or Husky no-op commit is recorded on the branch; docs/codespaces.md lists those items accordingly.
- [ ] T008 [US1] Record unobserved: Codespace boot, in-Codespace Aspire stack and dashboard reachability, full-stack
  machine size. Verification note on the PR.
  Not ticked: no verification note on a PR exists yet.

## Phase 6 — review

- [ ] T009 `infra-reviewer` (ports, secrets, whether the checks prove anything).
  Not ticked: the infra review is in progress; no findings recorded yet.
