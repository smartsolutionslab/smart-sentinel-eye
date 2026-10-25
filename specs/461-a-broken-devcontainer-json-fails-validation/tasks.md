# Tasks 461: A broken devcontainer.json fails validation

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3151

Format: `[ID] [P?] [Story] description — file(s)`. One story (US1). No foundational tasks: Shared.Kernel,
Shared.Contracts and AppHost are untouched. Engineer: **infra-engineer**. Phase-4a colour: **red**.
Tasks are sequential (each depends on the previous) except where marked `[P]`.

## Phase 4a — tests first, observed red (test-writer)

- [ ] T001 [US1] Write `scripts/check-devcontainer-json.test.mjs` (node:test, node:assert/strict) covering:
  real file passes; comments + trailing commas + `"//"`/`"/*"` inside strings accepted with the expected value;
  missing comma, unclosed brace, unterminated block comment, empty input, non-object root rejected (`ok === false`,
  and the CLI exits 1 naming the file for the missing-comma case via `execFileSync` on a temp file). Do not pin V8
  error wording. — `scripts/check-devcontainer-json.test.mjs`
- [ ] T002 [US1] Stub `.devcontainer/check-devcontainer-json.mjs` exporting `parseJsonc` that always returns
  `{ ok: true, value: {} }` and a CLI that exits 0. Run `node --test scripts/check-devcontainer-json.test.mjs`; quote the
  verbatim red (each rejection case failing on `ok === true`). Depends on T001.
- [ ] T003 [P] [US1] Re-observe the defect: a scratch missing-comma copy (scratchpad, not committed) under
  `npx @devcontainers/cli@0.89.0 build --workspace-folder <scratch>` exits 0; quote. If Docker is unavailable, cite
  spec 456 T002 and say so. — no files.

## Phase 4b — implementation (infra-engineer; may not edit T001's test)

- [ ] T004 [US1] Implement the normaliser + `JSON.parse` + offset→line:column + non-object-root check + CLI per plan
  decisions 2-4. `pnpm test:guards` green. — `.devcontainer/check-devcontainer-json.mjs`. Depends on T002.
- [ ] T005 [P] [US1] Validate section: documented command becomes `node .devcontainer/check-devcontainer-json.mjs &&
  npx @devcontainers/cli@0.89.0 build --workspace-folder .`; replace the "does not catch JSON syntax errors ...
  follow-up" paragraph with what each half catches (syntax incl. JSONC rules vs Feature/build). — `docs/codespaces.md`.
  Depends on T004 (wording must match the shipped behaviour).

## Phase 5 — verification

- [ ] T006 [US1] Spec procedure steps 1-5; quote exit codes and messages. Guard counterfactual: remove a comma from the
  real `devcontainer.json` in the worktree → `pnpm test:guards` red; `git checkout -- .devcontainer/devcontainer.json`
  → green. Not committed. Depends on T004.
- [ ] T007 [US1] Close spec 456 T002: record the syntax-error half as MET with T006's quoted evidence and tick it.
  — `specs/456-a-codespace-that-builds/tasks.md`. Depends on T006.

## Parallelism

T003 can run alongside T001-T002 (no files). T005 and T006 can run in parallel after T004 (disjoint: docs vs no files).
Everything is one small slice; fan-out gains little.
