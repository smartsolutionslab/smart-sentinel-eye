# Tasks: Frontend import and export conventions

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Research**: [research.md](./research.md) | **Quickstart**:
[quickstart.md](./quickstart.md) | **Issue**: #3257 (ADR-0074, ADR-0150 as precedent)

**Phase 4a colour: mixed, declared per task.** The import rewrite (US1) is **characterisation**: baselines captured green first, existing
suites run **unmodified** with identical counts, and the resolve snapshot identical. The three eslint rules are **new behaviour: red first**,
each shown failing on a constructed or real violation and the output quoted in the PR (ADR-0139).

**Deviation from plan "Sequence" step 2, recorded:** wiring rule 1 before the codemod would leave `lint` red on every commit in between,
which breaks `git bisect`. Instead the rule is observed red from a scratch config (not committed), the codemod lands per app, and the rule is
wired in the commit after it, so each commit builds and lints green.

**Out of scope, never touched**: `@microsoft/signalr` (owner instruction), any behaviour, backend code, path aliases, import ordering,
`apps/shared`'s `exports` map. No test file is edited. Never `git stash` or `git checkout -- .`. One vitest at a time. No `Co-Authored-By`.
`eslint` by exit code, `prettier --check` beside it.

**§IV legs touched: none.**

## Phase 1: Baseline (nothing edited)

- [ ] **T001** Rebase on `develop` and check open frontend PRs (`gh pr list`); merge or rebase over #3258 (comment-only, `devProxy.ts`,
      `vite.config.ts`) first. Install lockfile-true.
- [ ] **T002** Per app (`shared`, `management-web`, `management-cameras`, `management-event-ingestion`, `kiosk-web`), one at a time, record
      vitest files and tests, `tsc --noEmit`, `eslint src` exit code, `prettier --check .`, and `vite build` for the four apps. Record in
      `verification.md`.
- [ ] **T003** Write `tools/resolve-snapshot.mjs`: with the TypeScript compiler API and each app's real compiler options, emit sorted
      `{file, specifier, resolved}` for every relative specifier (static, re-export, side-effect, `import()`, `vi.mock` family). Run it on the
      untouched tree and keep `baseline-resolve.json` out of git (scratchpad); record the specifier count.
- [ ] **T004** Counterfactual for the checker: in a scratch copy, make `./App` resolve to a different file than before (add a shadowing file)
      and see the snapshot differ; revert. Record the diff.

## Phase 2: Foundational

- [ ] **T005** Write `tools/codemod.mjs` (AST, removes a trailing `.js` from relative specifiers only, same forms as T003, idempotent, prints
      the count it changed and the count left). Dry-run mode first; no tree edit in this task.

## Phase 3: User Story 1: drop the `.js` suffix (P1, MVP)

**Independent test**: zero relative specifiers end in `.js`; resolve snapshot identical; suites, tsc, eslint, prettier, builds as baseline.

- [ ] **T006** [US1] Run the codemod on `apps/shared`; compare resolve snapshot for that app, vitest counts, tsc, eslint, prettier. Commit
      `refactor(shared): drop the .js suffix from relative imports`.
- [ ] **T007** [P] [US1] Same for `apps/kiosk-web`, then `apps/management-cameras`, then `apps/management-event-ingestion`: one commit each, each checked as T006 (builds included).
- [ ] **T008** [US1] Same for `apps/management-web`; additionally run `tsc --noEmit` for `e2e` if it imports from apps (`pnpm typecheck:e2e`, known to fail on a clean `develop`: compare with baseline).
- [ ] **T009** [US1] Whole-tree check: the snapshot is identical for every app, the codemod reports zero left, `git diff --stat -- '*.test.*'` shows
      only import-specifier lines (no assertion or test-name line), the `Architecture.Tests` source-scanning tests pass. Record.

## Phase 4: User Story 2: the import rule (P2)

**Independent test**: a constructed `.js` specifier fails lint in each form and in a test file; asset and package specifiers do not.

- [ ] **T010** [US2] Red first: before wiring, run a scratch ESLint config holding rule 1 against the **baseline** tree
      (run it before T006, while the tree is still baseline) and quote the violation count and two sample lines.
- [ ] **T011** [US2] Write `eslint.frontend-imports.mjs` (root): `no-restricted-imports` regex for `.js|.jsx|.ts|.tsx` on relative paths, plus the
      `ImportExpression` and `vi.mock` family selectors, exported as reusable arrays. Spread it in all five `eslint.config.js`, and spread the
      selector array into the ADR-0150 test block in each (the replace-not-merge trap).
- [ ] **T012** [US2] Counterfactuals, quoted: constructed violation in each form (static, `export … from`, side-effect, `import()`,
      `vi.mock('./x.js')` **inside a test file**) fails with the message; a `./x.css`, `./x.svg?url`, package and alias specifier passes; removing each passes.
      `eslint src --max-warnings 0` exit code 0 on the real tree in all five apps. Commit.

## Phase 5: User Story 3: export shape (P3)

**Independent test**: a default export in a non-allowed file fails; a re-export in a non-entry file fails; the allowed files pass.

- [ ] **T013** [US3] Add `no-restricted-exports` (`restrictDefaultExports`, all forms) to the fragment, with a block switching it off for exactly
      `management-cameras/src/CamerasSurface.tsx`, `management-event-ingestion/src/EventIngestionSurface.tsx`,
      `management-web/src/app/navigation/mismatchRuntimePlugin.ts`, one-line why each.
- [ ] **T014** [US3] Add the barrel selectors (`ExportAllDeclaration`, `ExportNamedDeclaration[source]`) on `src/**`, off for
      `apps/shared/src/{index,api/index,hooks/index,streaming/index,types/index,ui/index}.ts`; add the selectors to the test-block array.
- [ ] **T015** [US3] Counterfactuals, quoted: `export default` in a normal component fails and in `CamerasSurface.tsx` passes; `export * from` in a
      feature file fails and in `shared/src/ui/index.ts` passes; the same two in a test file. Lint exit code 0 on the real tree, five apps. Commit.
- [ ] **T016** [US3] Add "Frontend imports and exports" to `CONTRIBUTING.md`: no extension, named exports, the three allowed defaults and why,
      the barrel rule and its reason, what the rules cannot see. Prettier clean on that section (the file may not be clean elsewhere: do not `--write` it whole). Record `ui/primitives/index.ts` as a placeholder in research.md.

## Phase 6: Polish and verification

- [ ] **T017** Final run of T002's list on the tip; compare to baseline; full snapshot diff; counts identical. Write `verification.md`.
- [ ] **T018** `/verify`, then `/code-review`; resolve or accept findings in writing.
- [ ] **T019** Re-measure `grep -rEc "from '\\.\\.?/[^']*\\.js'"` over `apps` and quote 0.
- [ ] **T020** PR to `develop` (`--base develop`, `Closes #3257`), body with the red output, the counterfactuals, the snapshot result, per-app counts. Park; do not wait on CI.
- [ ] **T021** Rebase check: if `develop` moved and touched imports, re-run the codemod (idempotent) rather than resolving by hand.

## Dependencies

T001 → T002, T003 → T004 → T005 → T006 → T007, T008 → T009. T010 needs the baseline tree, so it runs before T006. T011/T012 follow T009.
T013–T016 follow T012 (same fragment file). Phase 6 last. US2 and US3 depend on US1 only for a green lint on the real tree.

## Suggested MVP

US1 alone (T001–T009) is shippable and removes the visible noise; US2 and US3 are what keep it from drifting and ship in the same PR.
