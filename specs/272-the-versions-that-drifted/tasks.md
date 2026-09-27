# Tasks: Spec 272, the versions that drifted

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2638 · **Lane:** autonomous (ADR-0144)
**Phase-4a colour:** **CHARACTERISATION (behaviour-preserving). NOT red.** No new tests.
The existing suites are captured green before any bump and must pass **unmodified** after
(plan §5, constitution §Testing, ADR-0139). A test that has to change means the package
is held back (plan §4). It is never a reason to edit the test.
**Engineers:** `test-writer` (4a baseline capture), then `frontend-engineer` (US1) and
`backend-engineer` (US2) for 4b, **in parallel**.

`[P]` = disjoint files, may run concurrently (ADR-0109). `[US1]` = frontend PR.
`[US2]` = backend PR.

**Board.** The feature issue is #2638. It is already on Project #13 (status Todo when this
was written). No `item-add` is needed and no per-task issues are created (CLAUDE.md,
Phase 3).

**Branches.** US2 plus these artifacts: `chore/2638-update-dependencies` (exists).
US1: `chore/2638-update-frontend-dependencies`, cut from `origin/develop` (T010), **not**
stacked on the US2 branch.

**Contention files** (one writer each): `Directory.Packages.props` (US2 only);
`pnpm-lock.yaml`, `apps/shared/package.json` (US1 only, and shared with in-flight spec 266,
see spec §9).

**Fan-out.** There is no shared foundation. T001/T002 (baseline) gate their own story
only. US1 (T010–T017) and US2 (T020–T027) own disjoint files and run fully in parallel.
**Within** each story every task edits the same file(s), so each story is strictly
sequential.

**Per-commit rule (ADR-0087).** Each commit below must pass its toolchain's checks **on
its own**, not only at the tip. Backend: `dotnet build SmartSentinelEye.slnx -c Release`.
Frontend: `pnpm install --frozen-lockfile && pnpm lint && pnpm typecheck && pnpm test && pnpm format:check && pnpm build`.
Commit messages: `chore(deps): …`, Conventional Commits (ADR-0030), **no Co-Authored-By
trailer** (ADR-0086).

---

## Phase 4a: characterisation baseline (`test-writer`). No test file is created or edited.

- [ ] **T001 [P] [US1]** Frontend baseline on `origin/develop` (`41ab72ec` or the current tip; record the SHA): run `pnpm install --frozen-lockfile`, `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm format:check`, `pnpm build`, `pnpm audit`, `pnpm outdated -r`. Return the **verbatim** summary lines: test counts per app, `test:guards` count, and the audit total (expected: 8 vulnerabilities, 6 high / 1 moderate / 1 low). If any check is already red on the base, report it verbatim. It is pre-existing, not this change's fault.
  - **Depends on:** nothing. **Blocks:** T011.
- [ ] **T002 [P] [US2]** Backend baseline on the same base SHA: `dotnet build SmartSentinelEye.slnx -c Release` (warnings/errors line), then `dotnet test` over every test project **except** `tests/Integration.Tests` (it needs the Aspire stack, which must not be booted on this machine). Return verbatim pass/fail/skip totals per project. Also record the URL of the most recent green `develop` CI run as the integration/e2e baseline, and download its TRX artifacts for the latency figures (spec §7).
  - **Depends on:** nothing. **Blocks:** T021.

## Phase 4b, US1: frontend PR (`frontend-engineer`), sequential

- [ ] **T010 [US1]** Cut `chore/2638-update-frontend-dependencies` from a fresh `origin/develop` in its own worktree. Do not reuse `D:\Github\sse-2638`, which holds the US2 branch (memory: *one branch, one index*).
- [ ] **T011 [US1]** Commit F1: eslint and @eslint/js `9.18.0 → 9.39.5`, and eslint-config-prettier `9.1.0 → 9.1.2`, in root + kiosk-web + management-web + shared. Run `pnpm install`. **Then `pnpm audit` must print 0.** If an advisory survives the re-resolve, add a minimal `pnpm.overrides` entry for that transitive package and name the advisory in the PR body (spec §3.3). Quote audit before and after.
  - **Depends on:** T001, T010.
- [ ] **T012 [US1]** Commit F2: `@typescript-eslint/eslint-plugin` and `@typescript-eslint/parser` `8.68.0 → 8.70.1` in all four manifests.
  - **Depends on:** T011.
- [ ] **T013 [US1]** Commit F3: `react`, `react-dom` `19.2.8 → 19.3.0`; `@types/react` `19.2.18 → 19.3.0`; `@types/react-dom` `19.2.5 → 19.3.0`, in kiosk-web, management-web and shared. After install, check that `pnpm why react` shows a single version.
  - **Depends on:** T012.
- [ ] **T014 [US1]** Commit F4: `react-router-dom` `7.18.2 → 7.18.4` (kiosk-web, management-web); `react-hook-form` `7.86.0 → 7.89.0` (management-web); `zod` `4.4.3 → 4.6.5` (management-web, shared); `tailwind-merge` `3.6.0 → 3.7.0` (shared).
  - **Depends on:** T013.
- [ ] **T015 [US1]** Commit F5: `vite` `8.2.2 → 8.3.1`; `@vitejs/plugin-react` `6.1.0 → 6.1.1`; `postcss` `8.5.26 → 8.5.28`; `jsdom` `30.0.1 → 30.1.1`; `@testing-library/react` `16.3.2 → 16.3.3`; `@testing-library/user-event` `14.6.6 → 14.6.7`; `prettier` `3.9.6 → 3.9.9` (root). If `format:check` reports reformatting, hold prettier back (plan §5).
  - **Depends on:** T014.
- [ ] **T016 [US1]** Commit F6: `@playwright/test` `1.62.1 → 1.63.0` and `@types/node` `22.20.1 → 22.20.4` (root). Run `pnpm test:e2e:install` and `pnpm typecheck:e2e` locally. Do not run a local full-stack e2e. Check first whether `typecheck:e2e` is already red on develop (memory).
  - **Depends on:** T015.
- [ ] **T017 [US1]** Final local check: `pnpm outdated -r` lists only the six deferred majors (eslint, @eslint/js, eslint-config-prettier, @types/node, typescript, vitest); `pnpm audit` = 0; `git diff origin/develop --stat` shows only the five files in plan §0. Record any held-back package with its verbatim failure (plan §4).
  - **Depends on:** T016.

## Phase 4b, US2: backend PR (`backend-engineer`), sequential, all in `Directory.Packages.props`

- [ ] **T020 [US2]** Confirm `chore/2638-update-dependencies` in `D:\Github\sse-2638` is rebased on the current `origin/develop`, and that no open PR edits `Directory.Packages.props` (ADR-0109).
- [ ] **T021 [US2]** Commit B1: `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.OpenApi`, `Microsoft.AspNetCore.SignalR.Client`, `Microsoft.EntityFrameworkCore`, `.Design`, `.Relational`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Logging.Abstractions` `10.0.11 → 10.0.12`; `Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.ServiceDiscovery`, `Microsoft.Extensions.ServiceDiscovery.Yarp` `10.9.0 → 10.10.0`. Release build.
  - **Depends on:** T002, T020.
- [ ] **T022 [US2]** Commit B2: `Aspire.Hosting.Testing`, `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.RabbitMQ`, `Aspire.Hosting.Azure.Storage`, `Aspire.Azure.Storage.Blobs` `13.5.3 → 13.5.4`; `Aspire.Hosting.Keycloak` `13.5.3-preview.1.26425.3 → 13.5.4-preview.1.26464.4`. Leave `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` (9.5.2, deferred major) and `Aspire.Hosting.NodeJs` (9.5.2, final) untouched. Release build.
  - **Depends on:** T021.
- [ ] **T023 [US2]** Commit B3: all six `WolverineFx*` entries (including the unreferenced `WolverineFx.Http`, for family alignment) `6.30.0 → 6.40.0`. Release build, then the non-integration test projects (`ServiceDefaults.Tests` and every `*.Infrastructure.Tests`). This is the highest-risk commit, so its integration verdict comes from CI.
  - **Depends on:** T022.
- [ ] **T024 [US2]** Commit B4: `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Extensions.Hosting` `1.18.0 → 1.19.1`; `OpenTelemetry.Instrumentation.AspNetCore`, `.Http`, `.Runtime` `1.18.0 → 1.19.0`. Release build.
  - **Depends on:** T023.
- [ ] **T025 [US2]** Commit B5: `Microsoft.NET.Test.Sdk` `18.9.0 → 18.10.1`, `Moq` `4.20.72 → 4.21.0`. Release build and the full non-integration test run.
  - **Depends on:** T024.
- [ ] **T026 [US2]** Commit B6: `SonarAnalyzer.CSharp` `10.33.0.1635 → 10.34.0.3385`. Release build. **If any new Sonar diagnostic fires, hold the bump back** (drop the commit, record the verbatim diagnostics, file a follow-up). Never add a `NoWarn` or a `SonarLint.xml` change. A Release analyzer error on a file outside the diff can be flaky (memory), so re-run once before attributing it.
  - **Depends on:** T025.
- [ ] **T027 [US2]** Final local check: `dotnet list SmartSentinelEye.slnx package --outdated` lists only `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`; `--vulnerable --include-transitive` reports none; `git diff origin/develop --stat` shows only `Directory.Packages.props` plus `specs/272-*/`; every test project from T002 passes with the same counts.
  - **Depends on:** T026.

## Phase 5–7 (orchestrator / reviewers)

- [ ] **T030 [P] [US1]** Phase 5 for the frontend PR: spec §5 commands quoted, CI green on every bucket (frontend, e2e ×4 plus the guard; never merge on skipped or cancelled), and the composite + render leg noted (spec §7).
- [ ] **T031 [P] [US2]** Phase 5 for the backend PR: CI green on backend, integration ×4 plus the guard, and e2e ×4 plus the guard. Compare the latency-test TRX figures against T002's `develop` baseline, running twice before calling a regression (spec §7).
- [ ] **T032 [P] [US1]** Phase 6: `frontend-reviewer` on the frontend PR. `security-reviewer` only confirms that the eight advisories cleared, since the diff touches no auth code.
- [ ] **T033 [P] [US2]** Phase 6: `backend-reviewer` on the backend PR (transitive-pin consistency, no `.csproj` `Version=`, no suppression).
- [ ] **T034 [US1]** Phase 7, frontend PR: `gh pr create --base develop`. The body carries `Part of #2638`, a link to `specs/272-the-versions-that-drifted/` on the backend branch, the T001 and after outputs verbatim, the audit before and after, **Held back** (if any), and **Deferred majors** (spec §3.2).
- [ ] **T035 [US2]** Phase 7, backend PR: `gh pr create --base develop`. The body carries the T002 and after outputs verbatim, **Held back** (if any), and **Deferred majors** (spec §3.1). **`Closes #2638` goes on whichever of T034/T035 merges second.** The default is this PR. If the frontend PR merges last, move the keyword into its body before merging (spec §4).
  - **Depends on:** both PRs merged → then verify #2638 is closed (memory: a mention rarely auto-closes).
- [ ] **T036** File follow-up issues (no `agent:ready`) for each deferred major: eslint 10 (plus @eslint/js and eslint-config-prettier 10), typescript 7, vitest 5, @types/node 26 (tied to the CI Node version), `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` 13, and the `Aspire.Hosting.NodeJs` → `Aspire.Hosting.JavaScript` replacement. Also file one per held-back package. Add each to Project #13 and cross-link from #2638.
  - **Depends on:** T017, T027.
