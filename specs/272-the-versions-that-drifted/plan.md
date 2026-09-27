# Plan 272: The versions that drifted

**Spec:** `spec.md` · **Issue:** #2638 · **Lane:** autonomous (ADR-0144)

## 0. Shape of the change

This is a chore. It adds no bounded context, entity, value object, event or endpoint,
and no domain rule moves. The whole change is version strings in five files plus one
regenerated lockfile:

| PR | Files it may change | Agent (phase 4b) |
|---|---|---|
| Backend (US2), branch `chore/2638-update-dependencies` | `Directory.Packages.props` only (and `specs/272-*/` artifacts) | `backend-engineer` |
| Frontend (US1), branch `chore/2638-update-frontend-dependencies` | `package.json`, `apps/kiosk-web/package.json`, `apps/management-web/package.json`, `apps/shared/package.json`, `pnpm-lock.yaml` | `frontend-engineer` |

**Any other file in the diff is a finding.** It means a bump needed a source change, and
§4 says what happens then.

## Constitution / ADR check

| Rule | How this plan satisfies it |
|---|---|
| ADR-0037 phases, ADR-0144 lane | Phases 1–3 here. Phase 4a is characterisation (§5), which is still a phase 4a and not a skip. |
| Constitution §Testing, ADR-0139 | Behaviour-preserving → covering suites observed green **before**, pass **unmodified** after (§5). |
| ADR-0144 "may not weaken a gate" | No suppression, no rule relaxation, no test edit, no threshold change (spec FR-004/FR-005). A package that needs one is held back (§4). |
| ADR-0034 | `-c Release` build with `TreatWarningsAsErrors` is a per-commit check for the backend. |
| ADR-0087 | Rebase-merge lands commits individually. §3 groups them so each one builds alone. |
| ADR-0109 | `Directory.Packages.props` is a contention file with one writer (the backend engineer). The frontend files are disjoint, so the two PRs run in parallel. |
| ADR-0103 | Integration verification uses the Aspire fixture in CI. **No local Aspire boot** while other worktrees hold the machine's one stack (memory: *one machine, one Aspire stack*). |
| §III boundaries | No project reference changes. NetArchTest runs unmodified. |
| §IV latency | Spec §7: two legs sit over moving code. Phase 5 cites the figures. |
| §IX / ADR gaps | No dependency-policy ADR exists (spec header). None is written here. |

## 1. Why version-only is the rule

The issue asks for freshness, not for adaptation. If a bump is allowed to pull in
"small" code changes, a dependency PR becomes a refactor PR, which then needs its own
characterisation argument. And if that code change is a test edit, it destroys the
safety net that makes the bump reviewable. So the rule is binary: **a package either
moves with zero source changes, or it is held back.** That keeps phase 4a's promise
checkable with one command (`git diff --stat`).

One narrow exception, **lockfile and generated metadata only.** `pnpm-lock.yaml` is
regenerated and not hand-edited. `packages.lock.json` files do not exist in this repo
(checked: central management without lock files), so the backend has no equivalent.

## 2. Mechanics

### 2.1 Backend

- Edit `<PackageVersion Include="X" Version="…"/>` in `Directory.Packages.props` only.
  Never add `Version=` to a `.csproj` or `VersionOverride`.
- `CentralPackageTransitivePinningEnabled` is on, so a `PackageVersion` entry also pins
  that package when it arrives transitively. Pins inside a family must move **together**
  to avoid NU1605 (detected downgrade), which is an error in Release. For example, if
  Aspire 13.5.4 or WolverineFx.EntityFrameworkCore 6.40 needs
  `Microsoft.EntityFrameworkCore ≥ 10.0.12`, the runtime family must already be at 10.0.12.
  §3's commit order puts the runtime family first for exactly this reason.
- `Aspire.Hosting.Keycloak` stays on its preview line: `13.5.4-preview.1.26464.4`, which
  matches the 13.5.4 stable Aspire packages.
- Per commit: `dotnet restore`, then `dotnet build SmartSentinelEye.slnx -c Release`
  (**stop the stack first**, memory: *stop the stack before building*; this worktree has
  no stack of its own, so a lock is another worktree's AppHost holding shared binaries,
  which cannot happen from a separate checkout, but check anyway if MSB3027 appears).
- After the last commit: `dotnet test` for the unit and architecture suites, filtered to
  exclude `Integration.Tests` locally (it needs the Aspire stack). CI runs the integration
  shards.

### 2.2 Frontend

- Edit the exact pin in **every** manifest that declares the package (spec §3.2 lists
  which). The workspace keeps one version per package. A version that differs between
  `apps/shared` and an app would produce two React copies (`react`, `react-dom`, and
  `@types/react` all need to be single).
- Regenerate with `pnpm install` (not `pnpm update`, which rewrites ranges the repo pins
  exactly). Then `pnpm install --frozen-lockfile` must succeed on the result.
- Per commit: `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm format:check`, `pnpm build`.
- After the last commit: `pnpm audit` (must print 0) and `pnpm outdated -r` (only the six
  deferred majors).
- **Playwright 1.63 changes the bundled Chromium.** CI's cache key is
  `hashFiles('pnpm-lock.yaml')`, so the new lockfile misses the cache and
  `playwright install --with-deps chromium` fetches the matching browser. Locally, run
  `pnpm test:e2e:install` before any e2e run. Full-stack e2e runs in CI only (no local
  stack, see ADR-0103 row).

## 3. Commit grouping ("each commit builds standalone")

Commits are grouped by **coupling**, not by package. A group is the smallest set that has
to move together to build. The groups are ordered so that no commit depends on a later one.

### 3.1 Backend PR (all in `Directory.Packages.props`, so sequential, no `[P]`)

| # | Commit (Conventional Commits, `chore(deps): …`) | Packages | Why this boundary |
|---|---|---|---|
| B1 | bump the .NET runtime family to 10.0.12 / 10.10.0 | 8 × `10.0.11→10.0.12`, 3 × `10.9.0→10.10.0` | The floor everything else resolves against. |
| B2 | bump Aspire to 13.5.4 | 5 stable + Keycloak preview | Aspire hosting and client integrations version together. |
| B3 | bump Wolverine to 6.40.0 | the 6 WolverineFx entries | One family. EF and RabbitMQ adapters must match core. Highest behavioural risk (codegen, outbox, transport). |
| B4 | bump OpenTelemetry to 1.19 | 5 entries | One family. |
| B5 | bump the test stack | Microsoft.NET.Test.Sdk, Moq | Affects only test projects. |
| B6 | bump SonarAnalyzer.CSharp to 10.34 | 1 | Last because it is the most likely to be held back. New rules can fire under Release. |

### 3.2 Frontend PR (the manifests and the lockfile, so sequential, no `[P]`)

| # | Commit | Packages | Why this boundary |
|---|---|---|---|
| F1 | bump eslint to 9.39.5 to clear the eight advisories | eslint, @eslint/js, eslint-config-prettier | Security first (issue: priority). The body quotes `pnpm audit` before and after. |
| F2 | bump typescript-eslint to 8.70.1 | @typescript-eslint/eslint-plugin, parser | Lint rule-set change, isolated from F1 so a new report is attributable. |
| F3 | bump React and its types to 19.3.0 | react, react-dom, @types/react, @types/react-dom | Must be one version across the workspace. On the render leg (spec §7). |
| F4 | bump the runtime libraries | react-router-dom, react-hook-form, zod, tailwind-merge | Independent libraries, grouped to avoid a trickle. Each is revertible alone if held back. |
| F5 | bump build and unit-test tooling | vite, @vitejs/plugin-react, postcss, jsdom, @testing-library/react, @testing-library/user-event, prettier | The toolchain the checks themselves run on. |
| F6 | bump Playwright and Node types | @playwright/test, @types/node (22.20.4) | Root-only. The e2e runner moves last so every earlier commit is covered by the old runner. |

A commit whose package gets held back is dropped or reduced, and the remaining commits
still build, because every group only adds versions on top of the previous state.

## 4. Held-back protocol (the conflict scenario, operationalised)

If a check fails after a commit:

1. **Attribute it.** Revert one package at a time within the commit's group until the check
   is green. Also rule out a pre-existing failure: run the same check on `origin/develop`
   (memory: *typecheck:e2e fails on a clean develop*) and, for a flaky e2e, retry once
   (memory: *kiosk label-span flake*).
2. **Hold it back.** Keep the package at its old pin. Record under **Held back** in the PR
   body: package, target version, the check, and the **verbatim** failure.
3. **File a follow-up issue** for it, with the same content, labelled like the deferred
   majors. Do **not** add `agent:ready`, since a human decides whether it is worth adapting.
4. **Never** edit a test, add a suppression, lower a threshold, or change a rule's severity
   to make it pass (ADR-0144).

## 5. Test strategy: **characterisation, observed green (not red)**

**Phase-4a colour: BEHAVIOUR-PRESERVING / CHARACTERISATION.** This is stated
explicitly for the `test-writer` and both engineers:

- **No new tests are written.** A version bump adds no behaviour for a test to fail on
  first. Writing a red test here would be inventing behaviour.
- **The existing suites are the covering tests.** The `test-writer` captures them
  **green on the base commit before any bump**, and returns the verbatim summary lines:
  - Backend: `dotnet build SmartSentinelEye.slnx -c Release` and `dotnet test` over every
    non-integration test project (unit, application, infrastructure, architecture,
    ServiceDefaults, Shared.*).
  - Frontend: `pnpm lint`, `pnpm typecheck`, `pnpm test` (vitest in all three apps plus the
    `test:guards` node tests), `pnpm format:check`, `pnpm build`.
  - Integration (Aspire) and full-stack e2e are **not** run locally, because the machine's
    one Aspire stack is in use. Their baseline is the most recent green CI run on
    `develop` (cite the run URL). The PR's CI runs are the "after".
- **After every bump, the same suites must pass unmodified.** An assertion that has to
  change is evidence that behaviour moved. **Block, do not adjust** (CLAUDE.md), which
  means the package is held back (§4).
- **Proof that nothing was modified:** the PR body quotes
  `git diff origin/develop --stat` showing only the files in §0.
- The before and after outputs are both quoted in the PR body (ADR-0139: the only form of
  evidence a later reader can check).

### Known sensitivity points (where a hidden behaviour shift would surface)

| Package | What could move | Covered by |
|---|---|---|
| WolverineFx 6.40 | Codegen (`TypeLoadMode.Dynamic` + RuntimeCompilation), outbox, RabbitMQ topology | Integration shards (every cross-context flow), `ServiceDefaults.Tests` |
| EF Core / Npgsql 10.0.12 | SQL translation, migrations snapshot | `MigrationRunner` boot in the Aspire fixture. A pending-model-changes warning would fail it. |
| JwtBearer 10.0.12 | Token validation | Integration auth tests (spec 270's revocation tests, scope/audience tests) |
| OpenTelemetry 1.19 | Span and metric names that the latency tests read | Latency integration tests (spec §7) |
| SonarAnalyzer 10.34 | New rules → Release errors | Release build per commit |
| zod 4.6 | Error message text and parse edge cases | management-web form tests |
| react 19.3 | Effect and act timing | vitest suites in all three apps. Kiosk render tests are known to flake (memory), so retry once. |
| @typescript-eslint 8.70 | New `recommended` reports | `pnpm lint` (`--max-warnings 0`) |
| prettier 3.9.9 | Formatting output | `pnpm format:check`. A reformatting diff counts as a source change, so hold prettier back. |
| Playwright 1.63 | Bundled Chromium, locator semantics | CI e2e shards |

## 6. Messaging, entities and boundaries

None. No domain or integration event, no aggregate, no `Shared.Contracts` change. The
NetArchTest suite runs unmodified as part of §5.

## 7. Phase 5 (verify) expectations

- The spec §5 happy-path commands, run on each branch, with output quoted.
- CI green on all buckets for each PR: backend, frontend, integration ×4 plus the guard,
  e2e ×4 plus the guard. **Never on a skipped or cancelled bucket** (memory: *develop has no
  required status checks*).
- Latency figures from the TRX artifacts against a recent `develop` run (spec §7).
- `git diff origin/develop --stat` matches §0's file list.

## 8. Risks

| Risk | Mitigation |
|---|---|
| Wolverine minor changes runtime codegen or outbox behaviour | Its own commit (B3), full integration CI. Hold it back on any unexplained red. |
| Sonar or typescript-eslint adds rules that fire | Held back (§4), never suppressed. |
| Lockfile conflict with spec 266 (`apps/shared/package.json`, `pnpm-lock.yaml`) | Regenerate with `pnpm install` after rebasing, never hand-merge (spec §9). |
| Transitive pin NU1605 between families | Commit order B1 → B2 → B3. The Release build per commit catches it. |
| Playwright cache miss slows CI | Expected, once. The cache key follows the lockfile. |
| Parked-PR rebases (ADR-0144) replay a lockfile | `git rebase origin/develop`, then `pnpm install` and commit the regenerated lock if it conflicts. |
