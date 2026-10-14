# Spec 355: A pre-commit hook that checks what CI's format gates check

**Feature Branch**: `build/2869-husky-pre-commit` (cut from `origin/develop` at `54f566e3`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked **[JUDGEMENT]** (§7).

**Input**: Issue [#2869](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2869),
*ADR-0034 describes Husky pre-commit hooks running dotnet format --include, but no .husky/ exists*,
and the owner decision on it (2026-10-14): **build the hook** — Husky pre-commit running
`dotnet format --include` on staged `.cs` files, plus `prettier --check` on staged frontend files;
staged files only; skippable only via the usual git mechanism; **no new CI behaviour**; ADR-0034 is
not edited by the lane — a needed text change is reported instead (§6).

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (a new developer-tool behaviour that did not
exist): Phase 4a is **RED** (constitution §Testing; ADR-0139).

**Spec number.** 355. Checked 2026-10-14: `origin/develop` (`54f566e3`) highest 354; every remote
head via `git ls-tree <branch> specs/` highest 354. Re-check before the PR is opened.

**ADRs.** ADR-0034 (code style; promises the Husky hook — the decision this spec implements),
ADR-0033 (CI checks are PR-blocking; the hook is a convenience in front of them, not a replacement),
ADR-0037 / ADR-0144 (workflow and lane), ADR-0139 (red first), ADR-0036 (smallest change; no
speculative generality), ADR-0087 (each commit builds and passes on its own). **No new ADR is
needed**: ADR-0034 already decided Husky + staged-files-only; the owner narrowed the scope. The
divergence between ADR-0034's text and what is built is reported in §6, not written.

**Latency budget.** N/A — developer tooling; nothing on the event-to-overlay path (constitution §IV).

---

## 1. Premise verified (Phase 1 investigation)

| Claim | Finding |
|---|---|
| No `.husky/` exists | Confirmed: no `.husky/` in the tree; no `husky` dependency; `core.hooksPath` unset in the main checkout; no non-sample hook in `.git/hooks`. |
| Something documents its absence | `CONTRIBUTING.md:136-139` says "no `.husky` directory and no `husky`/`commitlint` dependency exist". Becomes partly stale with this spec (§5 FR-011). |
| CI C# gate (`ci.yml:177`, job `backend`) | `dotnet format SmartSentinelEye.slnx --verify-no-changes --no-restore --exclude-diagnostics IDE1006` — default subcommand, so **whitespace + style + analyzers** at their configured severities, IDE1006 deliberately excluded (#2764). Scope: every project in the `.slnx`. All 75 tracked `.csproj` are in the `.slnx`; every tracked `.cs` lives under `src/` or `tests/`. |
| CI frontend gate (`ci.yml:304`, job `frontend`) | `pnpm format:check` = `prettier --check "{apps,e2e}/**/*.{ts,tsx,js,jsx,json,css,md,yaml,yml}" playwright.config.ts`, honouring `.prettierignore`. Runs **before** Lint/Typecheck/Test and skips them on failure. `docs/`, `specs/`, `scripts/`, `.github/` are **not** in its scope. |
| JS tooling | Single pnpm workspace at the root: `package.json` (`packageManager: pnpm@10.30.3`, `engines.node ^22.22.2 \|\| ^24.15.0 \|\| >=26`), `pnpm-lock.yaml`, `pnpm-workspace.yaml` (`apps/*`). `prettier` 3.9.9 is already a root devDependency, pinned exact. CI uses Node 22, `pnpm install --frozen-lockfile`. No `.nvmrc`. |
| Where installs run | CI `pnpm install --frozen-lockfile` (frontend, e2e jobs). No workflow commits. AppHost uses `WithNpm(install: false)` for every JS app (no install at boot). The only Dockerfile is `src/AppHost/mosquitto/Dockerfile` (no Node). |
| Guards that read package.json / lockfile / workflows as text | `scripts/node-types-alignment*` (reads `devDependencies['@types/node']`, `engines.node`), `scripts/lint-scope.test.mjs` (reads `scripts.lint`, `scripts['lint:e2e']`), `ci.yml:793` (`hashFiles('pnpm-lock.yaml')` for the Playwright cache key — a lockfile change only re-keys it), `DependabotConfigTests` (ecosystems only). None is tripped by a new devDependency or a `prepare` script. |
| Line endings | `.gitattributes` `* text=auto eol=lf`; `.editorconfig` `end_of_line = lf`; `core.autocrlf=true` locally. Hook files are LF on Windows checkouts. |

### 1.1 Measured: what `dotnet format --include` costs (2026-10-14, this machine, warm restore)

| Command (`--verify-no-changes --no-restore --exclude-diagnostics IDE1006 --include <file>`) | Wall time | Exit |
|---|---|---|
| against `SmartSentinelEye.slnx`, one Shared.Kernel file | **73 s** | 0 |
| against `src/Shared.Kernel/…Shared.Kernel.csproj`, same file | **13 s** | 0 |
| against `src/AuditObservability/Api/…Api.csproj`, one Api file | **23 s** | 0 |
| against the Shared.Kernel project, `--include` a `.md` and a non-existent `.cs` | 6 s | **0** (paths outside the workspace are ignored, not an error) |
| `whitespace --folder`, one file | 17 s | 0 (and misses style/analyzers — rejected) |
| counterfactual: whitespace broken in `AggregateRoot.cs`, project-scoped | — | **2**, `error WHITESPACE … AggregateRoot.cs(11,2)` |

`--include` narrows what is *reported*, not what is *loaded*: the solution-wide form loads all 75
projects, so it is unusable as a pre-commit step. **Project-scoped invocation** (the nearest
ancestor `.csproj` of each staged file) is what makes the C# leg viable. Exit `2` = formatting
violation; other non-zero = tool failure (e.g. assets not restored).

## 2. User stories

### US1 (P1) — A mis-formatted staged file blocks the commit, with the CI command's verdict

As a developer (human or lane agent) committing on Windows, macOS or Linux, from Git Bash, a
terminal, Rider or Visual Studio, I want `git commit` to fail when a staged file would fail CI's
format gate, naming the file and the fix command, so I find out before pushing instead of after a
red `backend`/`frontend` job.

**Independent test.** In a worktree that has run `pnpm install`: stage a `.ts` file under `apps/`
with double quotes → `git commit` fails and names it; run `pnpm prettier --write <file>`, re-stage →
commit succeeds. Same with a `.cs` file with trailing spaces after `{` → fails naming it; `dotnet
format <project> --include <file>` → re-stage → succeeds.

### US2 (P1, same slice) — The hook checks no more than CI does, and only staged files

A staged `docs/**/*.md` or `specs/**/*.md` (outside `format:check`'s scope) is never rejected; an
IDE1006 violation is never rejected; deleted files, renames, and commits with no `.cs`/frontend
files cost nothing.

### US3 (P1, same slice) — Install is automatic and inert where it must be

`pnpm install` in a checkout or a worktree installs the hook; `pnpm install --frozen-lockfile` in CI
and `HUSKY=0` installs are unaffected; `git commit --no-verify` skips it.

US1-US3 are one slice: none is observable without the others.

## 3. Acceptance scenarios (Gherkin)

```gherkin
Feature: pre-commit format check

  Background:
    Given a checkout where "pnpm install" has run

  # Happy
  Scenario: clean staged files pass
    Given a staged "apps/kiosk-web/src/x.ts" that "prettier --check" accepts
    And a staged "src/Shared.Kernel/X.cs" that CI's dotnet format accepts
    When I run "git commit"
    Then the hook exits 0 and the commit is created

  Scenario: nothing relevant staged
    Given only "docs/adr/README.md" is staged
    When I run "git commit"
    Then the hook exits 0 without starting prettier or dotnet

  # Conflict (the file disagrees with the formatter)
  Scenario: mis-formatted frontend file
    Given a staged "apps/management-web/src/y.tsx" that prettier would rewrite
    When I run "git commit"
    Then the hook exits non-zero
    And the output names "apps/management-web/src/y.tsx" and the command that fixes it
    And no file in the working tree or the index has been modified

  Scenario: mis-formatted C# file
    Given a staged "src/Shared.Kernel/X.cs" with a whitespace violation
    When I run "git commit"
    Then the hook exits non-zero
    And the output contains dotnet format's diagnostic for "X.cs"
    And no file has been modified

  Scenario: partially staged frontend file is judged by its staged content
    Given "apps/kiosk-web/src/x.ts" is staged well-formatted
    And its unstaged working-tree copy is mis-formatted
    When I run "git commit"
    Then the hook exits 0

  # Bad request (inputs the hook must tolerate)
  Scenario Outline: inputs outside CI's scope are not checked
    Given a staged "<path>" that the formatter would reject
    When I run "git commit"
    Then the hook exits 0
    Examples:
      | path                                  |
      | docs/notes.md                         |
      | specs/999-x/spec.md                   |
      | scripts/x.mjs                         |
      | apps/kiosk-web/dist/bundle.js         |
      | a .cs file whose only violation is IDE1006 |

  Scenario: deleted and renamed files
    Given a staged deletion of "apps/kiosk-web/src/old.ts"
    And a staged rename of "src/Shared.Kernel/A.cs" to "src/Shared.Kernel/B.cs" with B well-formatted
    When I run "git commit"
    Then the hook exits 0 and checks only "B.cs"

  Scenario: dotnet format cannot run (assets not restored)
    Given a staged .cs file in a project with no project.assets.json
    When I run "git commit"
    Then the hook exits non-zero
    And the output says the tool failed (not "mis-formatted") and suggests "dotnet restore" or "--no-verify"

  # Auth (who may skip / where it is inert)
  Scenario: the developer skips deliberately
    When I run "git commit --no-verify"
    Then the hook does not run

  Scenario: CI and opted-out installs
    Given "HUSKY=0" or a directory without ".git"
    When "pnpm install --frozen-lockfile" runs
    Then it succeeds and installs no hook

  Scenario: a git worktree
    Given "git worktree add ../sse-NNNN" and "pnpm install" inside it
    When I commit a mis-formatted staged file there
    Then the hook rejects it exactly as in the main checkout
```

## 4. Independent end-to-end test procedure (Phase 5)

1. In the PR's worktree (`D:\Github\sse-2869`): `pnpm install`; confirm `git config core.hooksPath`
   prints `.husky/_` and `.husky/_/pre-commit` exists.
2. Frontend counterfactual: add `const a = "x"` to an `apps/kiosk-web/src` file, `git add`,
   `git commit -m test` → quote the non-zero output. Revert; commit a clean change → exit 0. Do
   both from Git Bash **and** PowerShell.
3. C# counterfactual: insert spaces after the first `{` of `src/Shared.Kernel/AggregateRoot.cs`,
   stage, commit → quote the `error WHITESPACE` output and wall time. Revert.
4. Out-of-scope: stage a mis-formatted `docs/` markdown file → commit succeeds.
5. Partial staging: stage a clean `.ts` hunk, leave a mis-formatted unstaged edit → commit succeeds;
   `git diff` still shows the unstaged edit (nothing rewritten).
6. `HUSKY=0 pnpm install --frozen-lockfile` → exit 0. CI's own `frontend` job green on the PR is the
   proof `prepare` does not break `--frozen-lockfile` installs.
7. `git commit --no-verify` with a mis-formatted file → commit created (then reset it).
8. Record wall times for steps 2 and 3 in the verification note.

## 5. Functional requirements

- **FR-001** A Husky (v9) pre-commit hook runs on every `git commit` in a checkout or worktree whose
  `pnpm install` has run.
- **FR-002** The hook inspects only files in the index diff with status Added, Copied, Modified or
  Renamed (destination path) — `git diff --cached --name-only --diff-filter=ACMR -z`.
- **FR-003 (C# leg)** Staged `.cs` files are grouped by their nearest ancestor `.csproj`; for each
  group, `dotnet format <project> --verify-no-changes --no-restore --exclude-diagnostics IDE1006
  --include <files…>` runs. The flags mirror `ci.yml:177` exactly except the target (project, not
  solution — §1.1). A `.cs` file with no ancestor `.csproj` is not checked (CI does not check it
  either).
- **FR-004 (frontend leg)** Staged files matching `format:check`'s scope — `{apps,e2e}/**/*.{ts,tsx,
  js,jsx,json,css,md,yaml,yml}` and `playwright.config.ts` — and not ignored by `.prettierignore`
  are checked with prettier's `check` against their **staged (index) content**, using the resolved
  `.prettierrc.json`.
- **FR-005** The hook is **check-only**: it never writes to the working tree or the index.
- **FR-006** Exit 0 when every check passes or nothing relevant is staged; non-zero otherwise. The
  output names each failing file, which leg failed, and the fix command (`pnpm prettier --write
  <file>` / `dotnet format <project> --include <file>`) and mentions `--no-verify`.
- **FR-007** A dotnet-format exit code other than 0 or 2 is reported as a tool failure, distinct
  from a formatting failure; the commit is still blocked (fail closed).
- **FR-008** When a staged `.cs` file also has unstaged changes, the hook prints a one-line note
  that the working-tree copy was checked (dotnet format cannot read the index).
- **FR-009** Install: root `package.json` gains `"prepare": "husky"` and an exact-pinned `husky`
  devDependency. `HUSKY=0`, a missing `.git`, and CI installs must still exit 0.
- **FR-010** No change to `.github/workflows/*`, to CI's format commands, to `.editorconfig`,
  `.prettierrc.json`, `.prettierignore`, analyzers or severities.
- **FR-011** `CONTRIBUTING.md` documents the hook (what it checks, `--no-verify`, `HUSKY=0`,
  worktrees need their own `pnpm install`, the PATH note for GUI clients) and its line 136-139
  sentence is corrected to say the pre-commit hook exists while `commitlint`/`commit-msg` still
  does not.
- **FR-012** The hook's frontend scope cannot silently drift from `format:check`: a guard test fails
  if the two disagree.

## 6. ADR-0034 divergence (reported, not edited — owner decision)

What is built differs from ADR-0034 §Decision's Husky bullet in three ways. The text that would
need to change, for whoever owns the ADR:

| ADR-0034 says | Built | Why |
|---|---|---|
| `dotnet format --include <staged-cs-files>` (implies fix) | `dotnet format <owning project> --verify-no-changes --no-restore --exclude-diagnostics IDE1006 --include …` — check-only | Owner decision (check); mirrors CI's flags; project scope for speed (§1.1); IDE1006 per #2764. |
| `prettier --write <staged-non-cs-files>` | `prettier` check of staged content, limited to `format:check`'s scope | Owner decision ("prettier --check"); CI does not check `docs/`/`specs/`/YAML outside `apps`/`e2e`, so checking them locally would demand more than CI. |
| `eslint --fix <staged-ts-files>` | Not built | Not in the owner decision; ESLint needs type-aware project config per app, too slow per commit. CI still gates it. |

Also stale: ADR-0034 *Implementation Notes* ("Husky installation … happen during … Task #8 and
#9") and *TypeScript / React … configs shipped in `apps/web/`* (there is no `apps/web/`). ADR-0030
lines 49/78 promise `commitlint` on `commit-msg` via Husky — **out of scope here**, still unbuilt
after this spec; worth its own issue now that Husky will exist.

## 7. Judgement calls

- **[JUDGEMENT] Check-only, not auto-fix.** Fix-and-restage rewrites a partially staged file's
  index from the working tree, silently committing hunks the developer left out (the problem
  lint-staged needs a stash dance to avoid). Check-only cannot corrupt staging and keeps the hook's
  verdict identical to CI's. The cost — the developer runs the printed fix command — is one line.
- **[JUDGEMENT] Project-scoped dotnet format.** 13-23 s per project vs 73 s for the solution. A
  commit touching many projects runs one invocation per project; above a fixed threshold the plan
  falls back to one solution-wide run (plan §3.3).
- **[JUDGEMENT] Fail closed on tool failure** (FR-007) — a hook that passes when it could not check
  would teach people the green is meaningless. `--no-verify` remains the escape.
- **[JUDGEMENT] Own script + Husky, not lint-staged.** One new devDependency instead of two; the
  logic lives in `scripts/pre-commit.mjs`, testable with `node --test` like every other
  `scripts/*.mjs` guard.
- **[ASSUMPTION] Lane cost accepted.** Every lane commit that stages `.cs` files pays ~13-25 s per
  project touched. Owner asked for the hook knowing the lane commits; recorded so it is not a
  surprise.

## 8. Out of scope

ESLint in the hook; `commitlint`/`commit-msg`; formatting outside CI's scope; re-checking the whole
repo when `.editorconfig`/`.prettierrc.json` is staged (CI does); any CI change.
