# Plan 285 — The types that follow the runtime

**Spec:** [spec.md](./spec.md) · **Issue:** #2645 · **Branch:** `chore-2645-types-follow-the-runtime`
**Assumes Decision D1 = A (guard).** If the gate reviewer picks B, only §7's close-out survives; if C, this plan is withdrawn and a new issue plus ADR take its place.

## 1. Technical context

| Item | Value |
|---|---|
| Language / runtime | Plain ES modules (`.mjs`) run by `node --test` — the root `test:guards` script (`node --test "scripts/**/*.test.mjs" "e2e/support/**/*.test.mjs"`) |
| Where it runs in CI | `ci.yml` job *frontend — lint + typecheck + test*, step `pnpm test`, which chains `pnpm test:guards` — under the job's own `setup-node` Node 22. No CI or `package.json` edit is needed for the guard to be picked up. |
| Dependencies | `node:` built-ins only. No YAML or semver package: neither is a root dependency (`yaml` and `semver` exist only transitively), and adding one for a four-number comparison is the speculative generality ADR-0036 rules out. |
| Precedent mirrored | `scripts/lint-scope.test.mjs` (reads `ci.yml` and root `package.json` as text) and `scripts/render-leg-check.mjs` + `.test.mjs` (pure module + test, *doubly red*). |
| Bounded context / layers | None — a repository-tooling check. No `src/`, `apps/`, `Shared.Contracts` or AppHost change; no messaging; no NetArchTest impact. |
| Latency (constitution §IV) | N/A — nothing on the event-to-overlay path. |

## 2. Constitution and ADR check

| Rule | Status |
|---|---|
| §V / ADR-0037 — spec-driven, gates | This plan stops at the Phase 2 gate. |
| §Testing / ADR-0139 — new behaviour starts red | Applies: the guard is **new behaviour**, so Phase 4a is **red**. §5 defines what red means for a guard over a tree that is currently aligned. |
| ADR-0087 — each commit builds on its own | The red commit fails `pnpm test` by design (precedent: `7edf96f5` "test(stream-distribution): red …"). Every commit still installs, lints and type-checks. |
| ADR-0036 — smallest change | One module, one test file, zero version changes. |
| ADR-0144 — lane may not write ADRs | Supervised lane; no ADR written. The runtime-policy ADR gap is flagged in spec §0 and belongs to Option C. |
| §IV latency | N/A. |

No violation, no justification needed.

## 3. Files

| File | Change |
|---|---|
| `scripts/node-types-alignment.mjs` | **New.** Pure functions; no file I/O except in the one exported convenience that reads the real repository. |
| `scripts/node-types-alignment.test.mjs` | **New.** Fixture-string cases plus one real-repository case. |
| `specs/285-the-types-that-follow-the-runtime/*` | This spec and plan. |

Nothing else. In particular `package.json`, `pnpm-lock.yaml` and `.github/workflows/ci.yml` are **not** edited (FR-001, SC-003). Neither new file is inside the `lint:e2e` or `format` globs, matching the other `scripts/*.mjs`.

## 4. Design

### 4.1 The values compared

| Name | Source | Reduction |
|---|---|---|
| **CI majors** | every `.github/workflows/*.yml` / `*.yaml` file; every step whose `uses:` is `actions/setup-node@…` | the step's `with: node-version:` value, quotes stripped. `22`, `22.x`, `22.22.2` → 22. Anything else (`lts/*`, `node`, missing) → *unreadable*. Scanning every workflow, not only `ci.yml`, closes spec assumption A2 at no cost. |
| **Runtime floor** | root `package.json` `engines.node` | split on `\|\|`; each alternative must start with `^`, `~`, `>=`, `=` or a bare version, then a major; the floor is the **minimum** across alternatives (edge case: out-of-order ranges). An alternative starting with `<` or with no number → *unreadable*. |
| **Types major** | root `package.json` `devDependencies["@types/node"]` | only `^N…`, `~N…` or exact `N…` are readable — a `>=` or `*` range could resolve to any major, so it is *unreadable*. Missing → *unreadable*. |
| **Running major** | `process.versions.node` | leading integer. |

### 4.2 The problems reported

`checkNodeTypesAlignment({ workflows, manifest, runningVersion })` → an array of problem strings; empty means aligned. `workflows` is `[{ path, text }]`, `manifest` the parsed root `package.json` object. Each problem names the values and the file to change (FR-006):

1. **No setup-node step found** in any workflow → problem (never a vacuous pass).
2. **Any unreadable value** (per §4.1) → one problem per value, naming the file and, for workflows, the `node-version` text found.
3. **CI steps disagree** (more than one distinct CI major) → one problem listing each `path: version` (FR-003).
4. **Types major ≠ CI major** (FR-002).
5. **Runtime floor ≠ CI major** (FR-004).
6. **Running major < types major** (FR-005). Greater is allowed: the typings are a floor, and the maintainer machine runs Node 24.

Checks 4–6 run only when their inputs are readable and CI agrees, so one root cause yields one message rather than a cascade.

Locating a step's `node-version`: take the text from each `uses: actions/setup-node@` match up to the next line that starts a sibling step (`^\s*- ` at an indent ≤ the matched step's dash) or end of file, and read `node-version:` inside that slice. Regex over text, as `lint-scope.test.mjs` already does for `ci.yml`; the fixture cases (§5) pin the shapes it must handle — quoted and unquoted values, `with:` before or after `name:`, and a following step that is *not* setup-node.

`readRepositoryInputs(repositoryRoot)` is the only I/O: it reads the workflow directory and root `package.json` and returns the object above. Kept separate so every rule is testable on strings.

### 4.3 Test cases (`node-types-alignment.test.mjs`)

Fixture cases, one per acceptance scenario and edge case in spec §2:

| Case | Expect |
|---|---|
| three setup-node steps at `'22'`, `^22.20.4`, engines `^22.22.2 \|\| ^24.15.0 \|\| >=26.0.0`, running `22.22.2` | `[]` |
| same, running `24.21.0` | `[]` (newer local runtime passes) |
| types `^26.6.3` | one problem naming 26, 22 and `package.json` |
| one step at `'24'` | one problem listing all three steps |
| engines `>=24.0.0 \|\| ^22.22.2` (out of order, floor 22) | `[]` |
| engines floor 24 | one problem naming the floor |
| running `20.19.0` | one problem naming the running version |
| `node-version: lts/*`; step with no `node-version`; types missing; types `>=22`; engines `<23` | one *unreadable* problem each |
| workflow with no setup-node step | the *no step found* problem |
| two workflow files, second with its own setup-node at `'24'` | CI-disagree problem naming both paths |

Plus one real-repository case: `checkNodeTypesAlignment({ ...readRepositoryInputs(root), runningVersion: process.versions.node })` → `[]`. This is the case CI actually enforces, and the only one that asks the running system.

## 5. Phase 4a — red, and what red means here

The real tree is aligned, so the real-repository case cannot be red by itself. Red is established the way `render-leg-check.test.mjs` does it — **doubly red**, both runs quoted verbatim in the PR:

1. **Module absent.** `test-writer` commits the test file alone; `node --test scripts/node-types-alignment.test.mjs` fails with `ERR_MODULE_NOT_FOUND` for every case. Proves nothing on its own.
2. **Stub that returns `[]`.** Every fixture case expecting a problem now fails *for its own reason* (the missing message), while the aligned cases pass. This is the meaningful red.

After green, **four counterfactuals on the real files** (SC-002; memory *prove a guard by counterfactual*), each run then reverted, each output quoted: root `@types/node` → `^26.6.3`; one `ci.yml` step → `'24'`; `engines.node` floor → `^24.15.0`; one step → `lts/*`. Revert with `git checkout --` and confirm `git status` is clean before committing (memory: *a restored file keeps its old timestamp* does not bite here — nothing is compiled — but the clean-status check does).

## 6. Sequence and commits

No parallelism: two new files that depend on each other. Nothing is foundational to another slice.

1. `test(scripts): red the @types/node alignment guard` — test file only; red run 1 and run 2 captured (run 2's stub is not committed).
2. `test(scripts): guard @types/node against the CI Node major` — the module; `pnpm test:guards` green; the four counterfactuals run and reverted.
3. Local gates: `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm format:check` — noting memory *typecheck:e2e fails on a clean develop* before blaming the branch.
4. Phase 5: CI's frontend job green on the PR is the end-to-end observation (the guard running under the real Node 22 runner); cite the job's `test:guards` output line for this file.
5. Phase 6: `infra-reviewer` (CI/tooling). No security surface.
6. Phase 7: PR to `develop` with `Closes #2645`, spec §1's table, both red runs and the four counterfactuals. Confirm #2645 is closed after merge (SC-004).

The spec commit precedes these: `docs(285): specify and plan @types/node tracking the CI Node major`.

## 7. Close-out text for #2645 (FR-007)

> Not bumped. CI runs Node 22 on all three `setup-node` steps and `engines.node`'s floor is 22, so `@types/node` stays on 22.x — already its newest release, 22.20.4. Bumping to 26 would type-check Node 26 APIs the CI runner does not have. The rule now fails `pnpm test` if the typings, the CI runtime and `engines` drift apart. Moving the runtime itself (Node 22 leaves maintenance on 2027-04-30) is a separate decision and needs its own issue and ADR.

## 8. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| A workflow restructure (composite action, matrix `node-version: ${{ matrix.node }}`) makes the regex miss the value | Low | It reads as *unreadable* and fails loudly, not as a pass — the failure is the prompt to extend the reader. |
| A deliberate runtime move is slowed by the guard | Certain, intended | The message names all three files; the move is one commit touching them together. |
| The guard is read as permission to skip an e2e run on a runtime move | Low | Out of scope here; Option C's issue must carry its own full e2e run. |
