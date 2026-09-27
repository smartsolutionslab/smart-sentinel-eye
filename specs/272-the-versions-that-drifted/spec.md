# Spec 272 — The versions that drifted

**Issue:** [#2638](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2638)
— *Update all frontend and backend dependencies*. **Lane:** autonomous (ADR-0144;
`agent:ready`, Project #13 status Todo). The feature issue is already on the board, so
no `item-add` is needed and no per-task issues are created (CLAUDE.md, Phase 3).

**Branches.** `chore/2638-update-dependencies` (this branch, cut from `origin/develop`
at `41ab72ec`; carries this spec and the backend bumps) and
`chore/2638-update-frontend-dependencies` (to be cut from `origin/develop` at phase 4;
carries the frontend bumps). §4 explains the split.

**Spec number.** 272. Checked on 2026-09-27 against `git ls-tree origin/develop -- specs/`
(highest 270) and every `D:/Github/sse-*/specs/` worktree directory (highest 271,
`sse-2537`). **Re-check before opening either PR** (memory: *spec number: origin/develop
isn't enough*).

**ADRs and constitution sections referenced:** ADR-0034 (analyzers, warnings as errors
in Release), ADR-0037 (phases), ADR-0139 / ADR-0144 and constitution §Testing
(characterisation, not red), ADR-0109 (contention files: `Directory.Packages.props`),
ADR-0087 (rebase-merge, each commit builds on its own), ADR-0052 / ADR-0103 (test stack,
Aspire fixture), ADR-0074 / ADR-0075 / ADR-0077 / ADR-0079 (the frontend stack whose
packages move), ADR-0042 / ADR-0088 (Wolverine), ADR-0050 (OpenTelemetry), ADR-0084
(SonarAnalyzer metrics). Constitution §IV (see §7).

**ADR gap, flagged, not filled.** No ADR records a dependency policy: no cadence, no
rule for major bumps, no tooling (there is no Dependabot or Renovate config under
`.github/`). This spec applies the issue's own scoping (patch and minor only). A standing
policy would be an architectural decision, which the autonomous lane may not make
(ADR-0144). Raise it with a human if the cadence gap is to be closed for good.

---

## 1. What was measured (2026-09-27, tree `41ab72ec`)

Every figure below comes from a command that was run, not from a guess.

| Side | Command | Result |
|---|---|---|
| Backend | `dotnet list SmartSentinelEye.slnx package --outdated --format json` | 31 packages outdated across 74 projects, 0 problems |
| Backend | `dotnet list SmartSentinelEye.slnx package --outdated --include-prerelease` | `Aspire.Hosting.Keycloak` 13.5.3-preview.1.26425.3 → 13.5.4-preview.1.26464.4 (the stable pass reports it "Not found") |
| Backend | `dotnet list SmartSentinelEye.slnx package --outdated --highest-minor` | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` has no newer 9.x |
| Backend | `dotnet list SmartSentinelEye.slnx package --vulnerable --include-transitive` | **0 vulnerable packages** in all 74 projects |
| Frontend | `pnpm install --frozen-lockfile` then `pnpm outdated -r --format json` | 24 packages outdated |
| Frontend | `pnpm view <pkg>@<current-major> version` for each package whose `latest` is a new major | 4 have a newer release inside their current major (§3.2) |
| Frontend | `pnpm audit --json` | **8 advisories** (6 high, 1 moderate, 1 low) in 3 transitive packages, all under `eslint@9.18.0` (§3.3) |

**Where versions live.**

- **NuGet is centrally managed.** `Directory.Packages.props` sets
  `ManagePackageVersionsCentrally` and `CentralPackageTransitivePinningEnabled`. No
  `.csproj` carries a `Version=`. Every backend bump is an edit to one file. That file is
  an ADR-0109 contention file.
- **pnpm versions are exact pins** in four manifests: `package.json` (root) and
  `apps/{kiosk-web,management-web,shared}/package.json`, plus `pnpm-lock.yaml`. Because the
  pins are exact, `pnpm outdated` reports `wanted = current` for everything. Its `latest`
  column is the only signal, and it hides in-major releases whenever a new major exists.
  That is why §1 has the extra `pnpm view` row.
- **The SDK stays pinned.** `global.json` pins SDK `10.0.401` with `latestPatch`. The
  issue excludes a runtime upgrade, so `global.json` is not touched.

## 2. User stories

### US1 (P1): The frontend's advisories are cleared and its packages are current within their majors

A maintainer runs `pnpm audit` and it reports **zero** advisories. `pnpm outdated -r`
reports nothing except the six deferred majors in §3.2. Lint, typecheck, unit tests,
`format:check` and the Playwright e2e suite pass, and no test file changed.

**Why P1:** this story contains every security advisory in scope. The issue says
advisories take priority.

**Independent test:** on the frontend branch, run `pnpm install --frozen-lockfile`,
`pnpm audit`, `pnpm outdated -r`, `pnpm lint`, `pnpm typecheck`, `pnpm test`,
`pnpm format:check` and `pnpm build`. Then CI's `e2e (Playwright, full stack)` jobs.

### US2 (P1): The backend's packages are current within their majors

A maintainer runs `dotnet list package --outdated` and it reports only the deferred
entries in §3.1. `dotnet build -c Release` is clean (analyzers as errors, ADR-0034). The
unit, architecture and integration suites pass with no test file changed.

**Independent test:** on the backend branch, run `dotnet build SmartSentinelEye.slnx -c Release`,
then `dotnet list SmartSentinelEye.slnx package --outdated` and `--vulnerable --include-transitive`.
Then CI's `backend`, `integration tests (Docker)` and `e2e` jobs, which boot the real Aspire
stack. This matters here because Aspire, Wolverine, EF Core and Npgsql all move.

US1 and US2 share no file, so each can be built, observed and merged without the other.

## 3. Candidate lists

Classification is by semantic version against the **currently pinned** version:
**patch/minor → in scope**; **major → deferred** with a reason, and not bumped. A major
that is also a security fix would need explicit sign-off. None was found (§3.3).

### 3.1 Backend (NuGet, `Directory.Packages.props`)

**In scope: 31 `PackageVersion` entries** (30 reported plus one family alignment):

| Group | Package(s) | From → To | Kind |
|---|---|---|---|
| .NET runtime family | Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.OpenApi, Microsoft.AspNetCore.SignalR.Client, Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Design, Microsoft.EntityFrameworkCore.Relational, Microsoft.Extensions.Hosting, Microsoft.Extensions.Logging.Abstractions | 10.0.11 → 10.0.12 | patch |
| .NET extensions | Microsoft.Extensions.Http.Resilience, Microsoft.Extensions.ServiceDiscovery, Microsoft.Extensions.ServiceDiscovery.Yarp | 10.9.0 → 10.10.0 | minor |
| Aspire | Aspire.Hosting.Testing, Aspire.Hosting.PostgreSQL, Aspire.Hosting.RabbitMQ, Aspire.Hosting.Azure.Storage, Aspire.Azure.Storage.Blobs | 13.5.3 → 13.5.4 | patch |
| Aspire (preview) | Aspire.Hosting.Keycloak | 13.5.3-preview.1.26425.3 → 13.5.4-preview.1.26464.4 | patch (same preview line) |
| Wolverine | WolverineFx, WolverineFx.EntityFrameworkCore, WolverineFx.Postgresql, WolverineFx.RabbitMQ, WolverineFx.RuntimeCompilation | 6.30.0 → 6.40.0 | minor |
| Wolverine (alignment) | WolverineFx.Http | 6.30.0 → 6.40.0 | minor. Declared, but referenced by no project, so `dotnet list` cannot report it. It moves with its family so the props file does not declare a mixed Wolverine version. |
| OpenTelemetry | OpenTelemetry.Exporter.OpenTelemetryProtocol, OpenTelemetry.Extensions.Hosting | 1.18.0 → 1.19.1 | minor |
| OpenTelemetry | OpenTelemetry.Instrumentation.AspNetCore, .Http, .Runtime | 1.18.0 → 1.19.0 | minor |
| Test stack | Microsoft.NET.Test.Sdk | 18.9.0 → 18.10.1 | minor |
| Test stack | Moq | 4.20.72 → 4.21.0 | minor |
| Analyzer | SonarAnalyzer.CSharp | 10.33.0.1635 → 10.34.0.3385 | minor |

**Deferred (major): 1**

| Package | Current → Latest | Reason deferred |
|---|---|---|
| Aspire.Npgsql.EntityFrameworkCore.PostgreSQL | 9.5.2 → 13.5.4 | A major version jump that moves the client integration onto Aspire 13's line. It is referenced by 4 projects on the persistence path, and 9.5.2 is the newest 9.x. It needs its own issue. |

**Noted, not a bump:** `Aspire.Hosting.NodeJs` 9.5.2 is the last release of that package.
Aspire 13 supersedes it with `Aspire.Hosting.JavaScript`. Moving the three `AddNpmApp`
call sites in `src/AppHost/AppHost.cs` is a package replacement plus an AppHost change,
so it is out of scope here. `xunit` 2.9.3, `xunit.runner.visualstudio` 4.0.0,
`NetArchTest.Rules` 1.3.2, `Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0, `Npgsql` /
`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `MQTTnet`, `Yarp.ReverseProxy`, `Shouldly`
and `coverlet.collector` are current.

**Security: 0.** No advisory is reported for any direct or transitive package.

### 3.2 Frontend (pnpm)

**In scope: 22 packages**

| Package | From → To | Kind | Manifests |
|---|---|---|---|
| react, react-dom | 19.2.8 → 19.3.0 | minor | kiosk-web, management-web, shared |
| @types/react, @types/react-dom | 19.2.18 / 19.2.5 → 19.3.0 | minor | kiosk-web, management-web, shared |
| react-router-dom | 7.18.2 → 7.18.4 | patch | kiosk-web, management-web |
| react-hook-form | 7.86.0 → 7.89.0 | minor | management-web |
| zod | 4.4.3 → 4.6.5 | minor | management-web, shared |
| tailwind-merge | 3.6.0 → 3.7.0 | minor | shared |
| vite | 8.2.2 → 8.3.1 | minor | kiosk-web, management-web, shared |
| @vitejs/plugin-react | 6.1.0 → 6.1.1 | patch | kiosk-web, management-web |
| postcss | 8.5.26 → 8.5.28 | patch | kiosk-web, management-web, shared |
| jsdom | 30.0.1 → 30.1.1 | minor | kiosk-web, management-web, shared |
| @testing-library/react | 16.3.2 → 16.3.3 | patch | kiosk-web, management-web, shared |
| @testing-library/user-event | 14.6.6 → 14.6.7 | patch | kiosk-web, management-web |
| @typescript-eslint/eslint-plugin, @typescript-eslint/parser | 8.68.0 → 8.70.1 | minor | root, kiosk-web, management-web, shared |
| prettier | 3.9.6 → 3.9.9 | patch | root |
| @playwright/test | 1.62.1 → 1.63.0 | minor | root |
| **eslint** | 9.18.0 → **9.39.5** (latest is 10.11.0) | minor, **security** | root, kiosk-web, management-web, shared |
| **@eslint/js** | 9.18.0 → **9.39.5** (latest is 10.0.1) | minor, **security** (moves with eslint) | root, kiosk-web, management-web, shared |
| eslint-config-prettier | 9.1.0 → 9.1.2 (latest is 10.1.8) | patch | root, kiosk-web, management-web, shared |
| @types/node | 22.20.1 → 22.20.4 (latest is 26.6.3) | patch | root |

The last four had no in-scope `latest` in `pnpm outdated`, because a new major hides the
in-major release. They were found with `pnpm view <pkg>@<major> version`.

**Deferred (major): 6**

| Package | Current → Latest | Reason deferred |
|---|---|---|
| eslint | 9.x → 10.11.0 | Flat-config and rule-default changes across three apps' lint gates (`--max-warnings 0`). Needs its own issue. |
| @eslint/js | 9.x → 10.0.1 | Moves with eslint 10. |
| eslint-config-prettier | 9.x → 10.1.8 | Moves with eslint 10. |
| @types/node | 22.x → 26.6.3 | CI runs Node 22 (`ci.yml` `node-version: '22'`). The types must match the runtime floor in `engines`, not the newest Node. |
| typescript | 6.0.3 → 7.0.2 | A major compiler change. `6.0.3` is already the newest 6.x. |
| vitest | 4.1.11 → 5.0.2 | Test-runner major: the runner is the safety net for this very change. `4.1.11` is already the newest 4.x. |

### 3.3 Frontend security advisories (`pnpm audit`)

All eight sit under the lint toolchain (`eslint@9.18.0`), which is a devDependency. None
ships in a kiosk or management-web bundle. **All eight are fixable inside the current
major**, so none needs sign-off for a breaking upgrade.

| Severity | Package (resolved) | Advisory | Patched | Path |
|---|---|---|---|---|
| high | brace-expansion 1.1.14 | GHSA-3jxr-9vmj-r5cp | ≥ 1.1.16 | eslint > minimatch > brace-expansion |
| high | brace-expansion 1.1.14 | GHSA-mh99-v99m-4gvg | ≥ 1.1.17 | same |
| high | brace-expansion 1.1.14 | GHSA-rgw5-rvv9-x895 | ≥ 1.1.18 | same |
| high | js-yaml 4.1.1 | GHSA-52cp-r559-cp3m | ≥ 4.3.0 | eslint > @eslint/eslintrc > js-yaml |
| high | js-yaml 4.1.1 | GHSA-5p4m-2wfm-xmqj | ≥ 4.3.1 | same |
| high | js-yaml 4.1.1 | GHSA-2883-xcg3-v3hh | ≥ 4.3.2 | same |
| moderate | js-yaml 4.1.1 | GHSA-h67p-54hq-rp68 | ≥ 4.2.0 | same |
| low | @eslint/plugin-kit 0.2.8 | GHSA-xffm-g5w8-qvg7 | ≥ 0.3.4 | eslint > @eslint/plugin-kit |

**How they clear.** `eslint@9.39.5` depends on `@eslint/plugin-kit ^0.4.1`,
`@eslint/eslintrc ^3.3.6` (whose 3.3.7 requires `js-yaml ^4.3.2`) and
`minimatch ^3.1.5` (→ `brace-expansion ^1.1.7`, newest 1.1.21). Bumping eslint and letting
pnpm re-resolve those lock entries clears all eight. `pnpm.overrides` is **not** to be
added unless the re-resolve leaves an advisory standing. If it does, the override is
recorded in the PR body with the advisory it answers.

## 4. Decision: two PRs, split by toolchain

**Two PRs: frontend (US1) and backend (US2).** The two sides share no file, no build
and no failure mode. A NuGet regression surfaces in the Release build or in the Aspire
integration suite. A pnpm regression surfaces in lint, vitest or Playwright. Separate PRs
let a red on one side be retried or parked (ADR-0144) without holding the other side's
green, and each diff stays reviewable by its own reviewer (`backend-reviewer`,
`frontend-reviewer`). Splitting further, per package or per family, is the "per-package
trickle" the issue rules out. Instead, the families become **commits** inside each PR
(plan §3).

**Closing keyword.** `Closes #2638` goes on **the PR that merges second**. The default is
the backend PR (this branch, which also carries the spec). The frontend PR says
`Part of #2638` and links spec 272 by path on the backend branch. If the frontend PR
ends up merging last, move the keyword into its body before merging. Either way, check
the issue state after the second merge (memory: *a PR mention rarely auto-closes the
issue*).

**Order.** The two are independent. Open the frontend PR first, because it carries the
advisories.

## 5. Acceptance scenarios

```gherkin
Feature: Dependencies are current within their majors, with behaviour unchanged

  # --- Happy path ---
  Scenario: Frontend advisories are cleared
    Given the frontend branch after its bumps
    When a maintainer runs "pnpm install --frozen-lockfile" and "pnpm audit"
    Then pnpm audit reports 0 vulnerabilities
    And "pnpm outdated -r" lists only eslint, @eslint/js, eslint-config-prettier,
        @types/node, typescript and vitest, each with a newer major only

  Scenario: Backend is current within majors and builds under Release analyzers
    Given the backend branch after its bumps
    When a maintainer runs "dotnet build SmartSentinelEye.slnx -c Release"
    Then the build succeeds with 0 warnings and 0 errors
    And "dotnet list package --outdated" lists only Aspire.Npgsql.EntityFrameworkCore.PostgreSQL
    And "dotnet list package --vulnerable --include-transitive" reports none

  Scenario: The existing suites are the safety net and pass unmodified
    Given the characterisation baseline was captured green on origin/develop
    When the same suites run on each branch
    Then they pass
    And "git diff origin/develop --stat" on each branch touches no test source file
    # test sources: tests/**, e2e/**, apps/**/*.test.ts(x), apps/**/__tests__/**

  # --- Conflict: a bump needs more than a version change ---
  Scenario: A bump that needs a source or test change is held back, not forced
    Given bumping package P makes a build, lint, analyzer or test fail
    When the engineer confirms the failure is caused by P (revert P alone and the check is green)
    Then P is restored to its previous version in that PR
    And P is listed under "Held back" in the PR body with the verbatim failure
    And a follow-up issue is filed for P
    And no test is edited, no suppression is added and no rule is relaxed to make P pass

  # --- Bad request: out-of-scope version movement ---
  Scenario: A major version or an SDK change does not slip in
    Given either branch's diff
    Then no package crosses a major version boundary
    And global.json is unchanged
    And no .csproj gains a Version attribute (central management stays intact)

  # --- Auth: the auth packages move, the auth behaviour does not ---
  Scenario: Bearer and OIDC behaviour is unchanged by the bump
    Given Microsoft.AspNetCore.Authentication.JwtBearer moved 10.0.11 → 10.0.12
    And oidc-client-ts / react-oidc-context did not move
    Then the existing auth integration tests (scope, audience, revocation, 401/403) pass unmodified
    And the e2e login flows for management-web and the kiosk pass unmodified
```

## 6. Functional requirements

- **FR-001** Every in-scope package in §3.1 and §3.2 is at its target version, or is listed
  as held back with its verbatim failure (§5, conflict scenario).
- **FR-002** No package crosses a major version. `global.json` is untouched.
- **FR-003** `pnpm audit` reports 0 advisories on the frontend branch.
  `dotnet list package --vulnerable --include-transitive` reports none on the backend branch.
- **FR-004** No file under `tests/`, `e2e/`, or any `*.test.ts(x)` / `__tests__` path changes
  (characterisation, plan §5).
- **FR-005** No analyzer or lint rule is suppressed, relaxed or narrowed. No
  `NoWarn`, `#pragma`, `.editorconfig` severity change, `eslint-disable` or
  `SonarLint.xml` change is added (ADR-0144: no weakening a gate).
- **FR-006** Each commit builds and passes its toolchain's checks on its own
  (ADR-0087; plan §3).
- **FR-007** Every deferred major in §3 is carried into the PR bodies so it can be filed
  as follow-up issues. They are not filed silently, and not bumped.

## 7. Latency budget (constitution §IV)

**No leg's budget changes, but two legs have code under them that moves.** This is not N/A:

- **Event → overlay state (≤ 200 ms):** Wolverine 6.30 → 6.40, the RabbitMQ transport, EF
  Core and Npgsql are on this path.
- **Composite + render (≤ 50 ms):** `react` / `react-dom` 19.2.8 → 19.3.0 render the kiosk
  overlay (`apps/shared`, `apps/kiosk-web`).

Phase 5 compares the latency tests' figures (e.g. `AcceptToDecideLatencyTests`,
`NFR001_AuditIngestLatencyTests`) from the green CI TRX artifacts of each PR against a
recent green `develop` run (memory: *green CI runs keep their measured figures*). Run
twice before calling a regression (memory: *measurement runs need repeating*). The other
legs (camera → SFU, SFU → decode, presentation buffer) sit in MediaMTX and the browser,
and no package here touches them.

## 8. Out of scope

- The .NET runtime or SDK (`global.json`), the Node version in CI, `packageManager`
  (`pnpm@10.30.3`).
- Every major listed in §3 as deferred, and the `Aspire.Hosting.NodeJs` →
  `Aspire.Hosting.JavaScript` replacement.
- Container image tags in `AppHost.cs` (Postgres, RabbitMQ, Keycloak, MediaMTX,
  Mosquitto). They are runtime resources, not packages.
- GitHub Actions versions in `ci.yml`.
- A standing dependency policy or automation (the ADR gap above).

## 9. Coordination with in-flight work

- **Spec 266 / #2335** (worktree `sse-2335`, branch `feat/2335-radix-primitives`,
  supervised) edits `apps/shared/package.json` and `pnpm-lock.yaml` to remove unused Radix
  packages. Whichever PR merges second rebases and **regenerates** `pnpm-lock.yaml` with
  `pnpm install` rather than hand-merging it.
- `Directory.Packages.props` is an ADR-0109 contention file. Before opening the backend
  PR, check that no parked PR edits it (`gh pr list --search "Directory.Packages.props"`
  or the file list of each open PR).
