# Feature Specification: The host that outlived its package

**Feature Branch**: `chore/2643-aspire-hosting-javascript` (cut from `origin/develop` at `edf41b97`)

**Created**: 2026-09-28

**Status**: Draft (Phase 1 gate)

**Input**: Issue [#2643](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2643):
*Replace Aspire.Hosting.NodeJs (final release) with Aspire.Hosting.JavaScript*. Deferred from
spec 272 (§3.1, "Noted, not a bump"; §8, out of scope), issue #2638.

**Spec number.** 283, pre-reserved by the dispatching orchestrator because sibling agents work
the same problem family in parallel worktrees. `origin/develop` tops out at 281 on 2026-09-28.
**Re-check before opening the PR** (memory: *spec number: origin/develop isn't enough*).

**ADRs and constitution sections referenced:** ADR-0074 (two apps, each "its own Aspire JS
resource in the AppHost"), `0000-initial-decisions.md` row 024 (.NET Aspire as composition root),
ADR-0037 (phases), ADR-0139 and constitution §Testing (characterisation, not red), ADR-0144
(phase 4a colour), ADR-0103 (Aspire-hosted tests, no Testcontainers), ADR-0109
(`Directory.Packages.props` and `AppHost.cs` are contention files), ADR-0087 (each commit builds
on its own), ADR-0034 (analyzers as errors in Release), ADR-0036 (smallest change). Constitution §IV
(see Latency, below).

**No ADR gap.** ADR-0074 names the resource kind ("Aspire JS resource"), not the package that
provides it. Swapping the provider package does not change a decision, so no ADR is written.

---

## What was measured (2026-09-28, tree `edf41b97`)

Every figure below comes from a file that was read, not from memory.

| Fact | Source |
|---|---|
| `Aspire.Hosting.NodeJs` is pinned at **9.5.2**, the only Aspire package not on 13.5.x | `Directory.Packages.props:28` |
| Every other Aspire hosting package is at **13.5.4**; the AppHost SDK is `Aspire.AppHost.Sdk/13.5.4` | `Directory.Packages.props:25,32-34,81-82`; `src/AppHost/SmartSentinelEye.AppHost.csproj:1` |
| Exactly **one** project references the package | `src/AppHost/SmartSentinelEye.AppHost.csproj:4` |
| Exactly **three** call sites, all `AddNpmApp(name, dir, "dev")`, all inside `if (isRunMode && !isE2ETests)` | `src/AppHost/AppHost.cs:722` (`management-web`, port 5173), `:732` (`kiosk-web`, 5174), `:754` (`kiosk-wall`, 5175, same `apps/kiosk-web` directory) |
| Each call site chains `.WithHttpEndpoint(env: "PORT", port: N, isProxied: false)`, `WithReference`, `WithEnvironment("VITE_*", …)`, `WithExternalHttpEndpoints`, `WithParentRelationship(apiGateway)`; the two kiosk instances also `WaitFor(layoutComposition)` | `src/AppHost/AppHost.cs:722-765` |
| What 9.5.2's `AddNpmApp` does: `new NodeAppResource(name, "npm", dir)`, args `run <script>`, `WithOtlpExporter()`, `NODE_ENV` = `development` / `production` by AppHost environment. **Nothing else. No install step.** | `dotnet/aspire@v9.5.2:src/Aspire.Hosting.NodeJs/NodeExtensions.cs` |
| `Aspire.Hosting.JavaScript` 13.5.4 exists on nuget.org (versions 13.4.4 … 13.5.4) and depends on `Aspire.Hosting` 13.5.4 | nuget flat-container index and the 13.5.4 `.nuspec` (source commit `9c1b401d`) |
| The repo is a **pnpm workspace**: `packageManager: pnpm@10.30.3` in root `package.json`, `pnpm-workspace.yaml` (`apps/*`), `pnpm-lock.yaml`. The apps depend on `"@smart-sentinel-eye/shared": "workspace:*"` | root files; `apps/*/package.json` |
| CI's end-to-end job boots this exact run-mode path: `pnpm install --frozen-lockfile`, then `dotnet run --project src/AppHost … -- ScenarioSimulator=false`, then Playwright through all three apps | `.github/workflows/ci.yml:597-695` |
| The stack-readiness gate expects every resource **without** `ExplicitStartupAnnotation` to reach `Running` | `src/AppHost/StackStatusReport.cs:88-104`; `scripts/wait-for-e2e-stack.sh` |
| One source comment names `AddNpmApp` | `apps/shared/src/observability/kioskLatency.ts:90` |

## Why this is not a rename

The issue suspected "a package swap plus an AppHost wiring change". Reading the pinned 13.5.4
source (`src/Aspire.Hosting.JavaScript/JavaScriptHostingExtensions.cs` at `9c1b401d`) confirms
that, and says precisely where the behaviour moves:

1. **`AddNpmApp` no longer exists.** Its replacements are `AddJavaScriptApp(name, appDirectory,
   runScriptName = "dev")` (package-script runner, no endpoint of its own) and
   `AddViteApp(name, appDirectory, runScriptName = "dev")` (Vite-specific).
2. **Every JavaScript resource now installs packages by default.** Both methods call `WithNpm()`
   with `install: true`, which adds a `<name>-installer` child resource running `npm install` in
   the app directory, and makes the app `WaitForCompletion` on it. `npm` does not understand the
   `workspace:*` protocol the apps use, so **left at its default, the swap breaks all three apps
   at boot** — the one outcome this change must not have. `WithNpm(install: false)` /
   `WithPnpm(install: false)` removes the wait and marks the installer `WithExplicitStart()`.
3. **`AddViteApp` registers its own `http` endpoint** (`WithHttpEndpoint(env: "PORT")`, proxied)
   and appends `--port <targetPort>`. Calling `WithHttpEndpoint` again is a documented duplicate-
   endpoint error. The fixed, unproxied ports (5173/5174/5175) are load-bearing — the Keycloak
   realm's redirect URIs and the gateway's CORS origins name them — so adopting `AddViteApp`
   means rewriting the endpoint wiring, not keeping it.
4. **New defaults that ride along** with either method (`WithNodeDefaults`): a required-command
   check for the package manager on `PATH`, certificate-trust environment configuration
   (`NODE_EXTRA_CA_CERTS`) when a trust scope applies, a VS Code debug annotation, and a dashboard
   icon. `WithOtlpExporter()` and the `NODE_ENV` rule are unchanged from 9.5.2.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The three front ends start exactly as before, on a supported package (Priority: P1)

A developer runs `aspire run` (or CI boots the run-mode stack). `management-web`, `kiosk-web` and
`kiosk-wall` start as `npm run dev` in the same directories, on the same fixed ports, with the
same environment, nested under `api-gateway` in the dashboard — and the AppHost no longer depends
on a package whose last release was 9.5.2 while everything else is on 13.5.4.

**Why this priority**: it is the whole issue. There is no P2: the package cannot be half-replaced.

**Independent Test**: on the branch, `dotnet build SmartSentinelEye.slnx -c Release` is clean; the
characterisation test (plan §4) passes unmodified before and after; a local `aspire run` shows the
three resources `Running` on 5173/5174/5175 and each app's sign-in page loads; CI's `e2e
(Playwright, full stack)` shards are green.

**Acceptance Scenarios** (Gherkin):

```gherkin
Feature: The web apps are hosted by Aspire.Hosting.JavaScript with behaviour unchanged

  # --- Happy path ---
  Scenario: The three apps start on their fixed ports
    Given the run-mode stack without E2ETests
    When the AppHost starts
    Then management-web, kiosk-web and kiosk-wall each run "npm run dev" in their app directory
    And they listen on 5173, 5174 and 5175 respectively, unproxied, with PORT set to that number
    And each carries the same VITE_* variables and references as on develop
    And each is a child of api-gateway in the dashboard

  Scenario: Nothing installs packages behind the developer's back
    Given the run-mode stack without E2ETests
    When the AppHost starts
    Then no package-install process runs for any of the three apps
    And none of the three apps waits on an installer resource
    And the stack-readiness report lists exactly the resources it listed on develop

  # --- Conflict: the new default would break the workspace ---
  Scenario: The installer the new package adds cannot block boot
    Given Aspire.Hosting.JavaScript adds a "<app>-installer" resource per app
    When the stack boots in CI's e2e job
    Then each installer is NotStarted and carries ExplicitStartupAnnotation
    And scripts/wait-for-e2e-stack.sh reaches "ready" without waiting on any installer

  # --- Bad request: out-of-scope movement ---
  Scenario: Only the host package moves
    Given the branch diff
    Then Aspire.Hosting.NodeJs is absent from Directory.Packages.props and every .csproj
    And Aspire.Hosting.JavaScript is pinned at 13.5.4, the same line as the other Aspire packages
    And no file under apps/ changes except the one comment naming AddNpmApp
    And package.json, pnpm-lock.yaml, pnpm-workspace.yaml and global.json are unchanged

  # --- Auth: the fixed ports are what the realm trusts ---
  Scenario: Sign-in still redirects back to each app
    Given the realm's redirect URIs name ports 5173, 5174 and 5175
    When a user signs in through management-web, and a kiosk signs in through kiosk-web and kiosk-wall
    Then the existing e2e login flows pass unmodified
```

---

### Edge Cases

- **Package manager missing from `PATH`.** 9.5.2 left DCP to fail the spawn; 13.5.4 adds a
  required-command check that fails the resource with a named message. Acceptable: better
  diagnostics for the same failure, no new failure.
- **A developer clicks Start on an `<app>-installer` in the dashboard.** It runs `npm install` in
  the app directory, which cannot resolve `workspace:*` and fails. It changes nothing on disk that
  the workspace depends on and cannot be reached without a deliberate click. Recorded as a known
  residual (plan §6), not fixed here.
- **Fresh clone with no `node_modules`.** Unchanged: the apps fail to start exactly as today,
  and the remedy is still `pnpm install` at the repo root. Adopting auto-install is a behaviour
  change and a separate issue (Assumptions).
- **Integration fixture (`E2ETests=true`) and publish mode.** Both skip the block entirely
  (`isRunMode && !isE2ETests`), so neither sees any difference. The `Publish*` / Dockerfile
  generation the new package offers is never reached.
- **Linux shells and hyphenated env keys.** The kiosk's `VITE_LAYOUT_HUB_ORIGIN` alias exists
  because `npm run dev` spawns Vite through `sh -c`. The runner stays `npm run`, so the alias
  remains necessary and correct.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** `Aspire.Hosting.NodeJs` is removed from `Directory.Packages.props` and from
  `SmartSentinelEye.AppHost.csproj`; `Aspire.Hosting.JavaScript` **13.5.4** replaces it in both.
- **FR-002** The three resources keep their names, app directories, run script (`dev`), runner
  (`npm run`), endpoint (`http`, fixed port, `isProxied: false`, `env: "PORT"`), references,
  `VITE_*` environment, `WaitFor(layoutComposition)` on the two kiosk instances,
  `WithExternalHttpEndpoints` and parent relationship — each unchanged from `develop`.
- **FR-003** No package-install step runs automatically for any of the three apps, and none of them
  waits on one (the 9.5.2 behaviour).
- **FR-004** `StackStatusReport.ExpectedResourceNames` for a run-mode model is the same set as on
  `develop`.
- **FR-005** Behaviour-preserving (constitution §Testing, ADR-0139): a characterisation test is
  captured **green on `develop`** before the swap and passes **unmodified** after it. No existing
  test file is edited to reach green.
- **FR-006** No analyzer, warning or diagnostic is suppressed to reach green (ADR-0144). If the new
  API needs an `[Experimental]` opt-in, that is a blocker to report, not a `NoWarn` to add.
  (Neither `AddJavaScriptApp` nor `WithNpm` is `[Experimental]` in 13.5.4.)
- **FR-007** The comment in `apps/shared/src/observability/kioskLatency.ts:90` that names
  `AddNpmApp` names the new call instead. The `Directory.Packages.props` comment names the new
  package.
- **FR-008** Each commit builds on its own under `-c Release` (ADR-0087, ADR-0034).

### Key Entities

- **JavaScript app resource** — one of `management-web`, `kiosk-web`, `kiosk-wall`: an executable
  that runs a package script in an app directory, exposes one fixed HTTP port, and receives
  service URLs as environment variables.
- **Installer resource** — new with this package: `<app>-installer`, a child of each app resource.
  Must exist only in the explicit-start state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** 0 references to `Aspire.Hosting.NodeJs` remain in the repository outside `specs/`.
- **SC-002** All four CI `e2e (Playwright, full stack)` shards pass on the PR with 0 edited test
  files.
- **SC-003** The characterisation test is observed green on `develop` and on the branch, with its
  source byte-identical between the two runs.
- **SC-004** A local run-mode boot shows 3 of 3 app resources `Running` on 5173/5174/5175 and each
  app's first page loads in a browser (phase 5 note).
- **SC-005** The stack-readiness gate's resource list is identical on both runs (0 added, 0 removed).

## Latency (constitution §IV)

**N/A for every leg.** The change is dev-time process orchestration of the Vite dev servers; it
touches no code shipped to a browser and no service on the event path. The kiosk bundle, the
composite-and-render code and every backend service are byte-identical. The only file under
`apps/` that changes is a comment.

## Assumptions

- **Runner stays `npm run dev`, via `AddJavaScriptApp(...).WithNpm(install: false)`.** Chosen as
  the strictly behaviour-preserving option. The alternatives were weighed and are recorded in
  plan §2: `WithPnpm(install: false)` (matches the declared package manager, but changes the spawned
  process and the kiosk config's `npm run dev` reasoning) and `AddViteApp` (forces the endpoint
  rewrite in "Why this is not a rename" item 3). Either is a follow-up issue if wanted.
  *Reviewable at this gate — flip it here rather than mid-implementation.*
- **Auto-install is not adopted.** Turning it on would be new behaviour (three concurrent
  installs over one workspace, and `npm` cannot install it at all), so under constitution
  §Testing it is a separate issue, not part of this refactor.
- **The pinned 13.5.4 source is the API authority.** Method names and defaults above come from
  `microsoft/aspire@9c1b401d` (the commit the 13.5.4 `.nuspec` records) and the package's XML
  docs, plus aspire.dev's "Set up JavaScript apps in the AppHost". Not from memory.
- **Out of scope:** `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` 9.5.2 → 13.x (spec 272's other
  deferred major, its own issue); publish-mode hosting of the apps (`PublishAsStaticWebsite` etc.);
  HTTPS for the Vite dev servers; any change to `apps/*/vite.config.ts`, `package.json` or the lockfile.
- **Coordination.** `Directory.Packages.props` and `AppHost.cs` are ADR-0109 contention files.
  Before opening the PR, check no parked PR edits either (`gh pr list` file lists).
