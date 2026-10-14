# Plan 357: Hook scope and flag guards

**Spec**: [spec.md](./spec.md). **Issue**: #2922.

## 1. Context and layers

No bounded context; developer tooling (`scripts/`). The only file changed is
`scripts/pre-commit.test.mjs`. No domain model, no messaging, no `Shared.Contracts`, no AppHost.
Constitution §IV: N/A. Boundary rules: N/A.

## 2. Design

Add a section `// Unit: the hook cannot drift from CI's format gates` to
`scripts/pre-commit.test.mjs`, beside the existing `// Unit: the scope cannot drift from
format:check` block (`:803`). Reuse the file's `repositoryRoot`, `loadHook()`, `readFileSync`,
`spawnSync`. New imports: none beyond `node:path` (already imported; use `path.posix.matchesGlob`).

Pure helpers (test-file local, not exported from the hook):

- `formatCheckGlobs(command)` — tokenise (`"…"` or `\S+`), require tokens 0-1 to be
  `prettier --check`, return the remaining tokens that do not start with `-`, quotes stripped.
- `scopeDifference(globs, files, classify)` — `expected = files.filter(f => globs.some(g =>
  path.posix.matchesGlob(f, g)))`, `actual = classify(files).prettier`; return
  `{ checkOnly: expected − actual, hookOnly: actual − expected }`, both sorted.
- `trackedFiles()` — `git ls-files -z` at `repositoryRoot`, split on NUL.
- `dotnetFormatStepFlags(ciYaml)` — find a `- name: Format check` line whose following `run:`
  line begins `dotnet format`; return `{ project, flags }` (tokens 2 and 3..n) or `null`.
- `ciFlagsFromDotnetArgs(dotnetArgs)` — `dotnetArgs({ project: 'P', files: [] })` with `format`,
  `P`, `--include` removed.

No YAML dependency: a line scan suffices for one known step and keeps ADR-0036's no-new-dependency
stance; the "not found" case fails explicitly (spec §3 bad request).

## 3. Tests (exact names, sentence style)

Live guards:

1. `the hook's prettier scope is exactly format:check's globs expanded over the tracked files`
2. `the hook's dotnet format flags are exactly ci.yml's Format check flags`

Permanent counterfactuals (pure helpers on mutated inputs):

3. `an extension added to format:check and not to the hook is reported as check-only`
   (`,mjs` → `e2e/support/render-leg.test.mjs`)
4. `a glob added to format:check is reported as check-only` (`"scripts/**/*.mjs"` → non-empty)
5. `an extension removed from format:check but kept by the hook is reported as hook-only`
   (drop `css` → 7 `.css` files)
6. `a flag added to or removed from ci.yml's Format check fails the flag comparison`
   (`--severity warn` appended; `--no-restore` removed)
7. `a ci.yml without a dotnet Format check step is reported as not found`

## 4. Colour and proof

Characterisation, observed green (spec §1.3: no drift today). Proof by counterfactual twice:
permanently (tests 3-7) and once on the live files (spec §4.2), red output quoted in the PR.

## 5. Risks

- Node 22 may print an `ExperimentalWarning` for `path.matchesGlob`; harmless to `node --test`.
  If CI's Node 22 lacks it, the test fails loudly (not silently) — accepted, `engines` floor is
  22.22.2.
- `git ls-files` needs a git checkout; CI's `actions/checkout` provides one.
