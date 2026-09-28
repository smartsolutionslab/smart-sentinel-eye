# Plan 283: The host that outlived its package

**Spec:** `spec.md` · **Issue:** #2643 · **Lane:** supervised (dispatched for phases 1–2 only;
the issue carried no `agent:ready` at dispatch) · **Branch:** `chore/2643-aspire-hosting-javascript`

## 0. Shape of the change

A chore in the composition root. No bounded context, entity, value object, event, endpoint,
contract or migration is added or changed. No domain rule moves. So this plan has no data-model,
contracts or messaging section: there is nothing to model. The template's `research.md`,
`data-model.md` and `contracts/` are folded into §1–§2 below or omitted, as specs 272–281 did.

| File | Change | Owner (phase 4b) |
|---|---|---|
| `Directory.Packages.props` | Replace `Aspire.Hosting.NodeJs` 9.5.2 with `Aspire.Hosting.JavaScript` 13.5.4; update the comment above it | `infra-engineer` |
| `src/AppHost/SmartSentinelEye.AppHost.csproj` | Swap the `PackageReference` | `infra-engineer` |
| `src/AppHost/AppHost.cs` | Three call sites (§3) plus a short *why* comment | `infra-engineer` |
| `apps/shared/src/observability/kioskLatency.ts` | Comment at line 90 only (spec FR-007) | `infra-engineer` (comment-only, same PR) |
| `tests/Integration.Tests/AppHostWebAppHostingTests.cs` | **New**, characterisation (§4) | `test-writer` (phase 4a) |
| `tests/Integration.Tests/ci-shards/shard-N.filter` | Add the new class to one shard | `test-writer` |

**Any other file in the diff is a finding.** In particular `apps/*/vite.config.ts`,
`package.json`, `pnpm-lock.yaml` and `global.json` must not move (spec, bad-request scenario).

## Constitution / ADR check

| Rule | How this plan satisfies it |
|---|---|
| ADR-0074, decision row 024 | The apps stay "Aspire JS resources" in the AppHost. Only the provider package changes. No ADR needed (spec header). |
| ADR-0037 / ADR-0144 | Phases 1–2 here; stops at the Phase 2 gate. Phase 4a colour is declared now: **characterisation (green)**, §4. |
| Constitution §Testing, ADR-0139 | Behaviour-preserving. The covering test is written first, observed green on `develop`, and must pass unmodified after the swap. |
| ADR-0144 "may not weaken a gate" | No `NoWarn`, no `#pragma`, no test edit. The APIs used are not `[Experimental]` in 13.5.4 (checked at source). |
| ADR-0034 | Every commit builds under `-c Release`, with warnings as errors. |
| ADR-0087 | Each commit builds on its own. The package swap and the call-site change are one commit, because neither compiles without the other (§5). |
| ADR-0103 | The characterisation test uses `DistributedApplicationTestingBuilder.CreateAsync` model-only, like `AppHostE2ESwitchTests`. It starts no containers and needs no Testcontainers. |
| ADR-0109 | `Directory.Packages.props` and `AppHost.cs` are contention files. They have one writer and a pre-PR check for parked PRs touching them. |
| §III boundaries | No project reference changes. NetArchTest runs unmodified. |
| §IV latency | N/A for every leg (spec, Latency). No browser-shipped code changes. |
| CLAUDE.md "Aspire is the composition root" | The wiring stays in `AppHost.cs`. |

## 1. Research: what the new package actually does

**Sources, all read and none recalled from memory:** the 13.5.4 nupkg (`.nuspec` records source
commit `9c1b401dd67746739044f68959cbf4d3d7af93a6`), its `Aspire.Hosting.JavaScript.xml` docs,
`src/Aspire.Hosting.JavaScript/JavaScriptHostingExtensions.cs` at that commit, the 9.5.2
`NodeExtensions.cs` at tag `v9.5.2`, and aspire.dev's *Set up JavaScript apps in the AppHost*.
**Phase 4b re-verifies each line cited below against the restored package** (memory: *a plan's
SDK claim needs checking against the pinned version*). Source line numbers are in
`JavaScriptHostingExtensions.cs@9c1b401d`.

| Question | 9.5.2 `AddNpmApp` | 13.5.4 `AddJavaScriptApp` | Source |
|---|---|---|---|
| Resource type | `NodeAppResource(name, "npm", dir)` | `JavaScriptAppResource(name, "npm", dir)` | L802 |
| Spawned command | `npm run <script>` | `npm` + `run` + `<script>`. `ScriptCommand` comes from the npm annotation, and the command is re-set from the annotation `OnBeforeStart` | L1145-1161, L1388-1399, L1803 |
| Endpoint | none. The caller adds it | none. The caller adds it (docs: "use `WithHttpEndpoint(port, env: \"PORT\")`") | L1136-1166 |
| `NODE_ENV` | `development` / `production` by AppHost environment | same rule | L310 |
| OTLP exporter | yes | yes | L308 |
| **Install step** | **none** | **`WithNpm()` with `install: true`**: adds `<name>-installer` running `npm install`, and the app `WaitForCompletion`s on it | L1166, L2329-2393 |
| Required-command check | no | yes (`WithRequiredCommandsFromPackageManager`) | L309 |
| Certificate-trust env | no | `NODE_EXTRA_CA_CERTS` / `NODE_OPTIONS` when a trust scope applies | L311-… |
| `[Experimental]` | — | not on `AddJavaScriptApp` or `WithNpm` (only `PublishAs*`, `AddNextJsApp`) | L792, L1795 |

**Calling `WithNpm(install: false)` after the default** takes the "installer already exists"
branch (L2338-2352). It removes the app's `WaitAnnotation` on the installer and adds
`WithExplicitStart()` to it. The installer resource **stays in the model** as a `NotStarted`
child. `StackStatusReport.ExpectedResourceNames` already excludes `ExplicitStartupAnnotation`
resources (`StackStatusReport.cs:99`, the same rule that exempts `*-rebuilder`), so the e2e
readiness gate is unaffected. The characterisation test pins this (§4, fact 3).

## 2. Decisions

### D1: `AddJavaScriptApp`, not `AddViteApp`

`AddViteApp` registers its own proxied `http` endpoint and appends `--port <targetPort>`
(L1490-1505). Keeping ports 5173/5174/5175 fixed and unproxied would mean replacing the three
`WithHttpEndpoint(...)` lines with `WithEndpoint("http", e => { e.Port = …; e.IsProxied = false; })`.
That rewrites the one wiring detail the realm (`smart-sentinel-eye-realm.json:132-230`) and the
gateway CORS list (`ApiGateway/appsettings.json:12-17`) depend on. It would also add CLI args that
`npm run dev` never passed before. What `AddViteApp` offers in exchange (opt-in HTTPS, `--config`
wrapping, static-site publish) is unused: HTTPS is out of scope and the block never runs in
publish mode. `AddJavaScriptApp` keeps every existing chained call byte-identical.
*Rejected alternative, recorded:* `AddViteApp`, as a separate issue if HTTPS dev servers are ever
wanted.

### D2: `WithNpm(install: false)`, not `WithPnpm(...)` and not the default

- **Default (`install: true`) is not an option.** `npm install` cannot resolve `workspace:*`,
  so all three apps would wait on a failed installer and never start.
- **`WithPnpm(install: false)`** would match the declared package manager. It would also make a
  dashboard click on `<app>-installer` do something useful (`pnpm install`). But it changes the
  spawned process from `npm` to `pnpm`, which invalidates the "`npm run dev` spawns Vite through
  `sh -c`" reasoning in `AppHost.cs:707-716` and `apps/kiosk-web/vite.config.ts`. That is a
  behaviour change, so under §Testing it goes in its own issue.
- **`WithNpm(install: false)`** reproduces 9.5.2 exactly: `npm run dev`, no install, no wait.

This is the one choice a reviewer might reasonably flip. **Flip it at this gate, not in phase 4.**
If flipped to pnpm, §4's fact 1 changes (`pnpm`, not `npm`), and the phase-4a colour turns
**red** for that assertion, since the process changes.

### D3: One commit for the swap

Removing the package deletes `AddNpmApp`. Adding the new one without changing the call sites
leaves `AddNpmApp` undefined. So `props` + `csproj` + `AppHost.cs` form the smallest unit that
builds (ADR-0087). The comment in `kioskLatency.ts` rides in the same commit, because it names
the call that commit changes.

## 3. The AppHost change (target shape, for review; not implementation)

Each of the three call sites changes only its first line and gains one chained call:

```csharp
builder.AddJavaScriptApp("management-web", "../../apps/management-web", "dev")
    .WithNpm(install: false)
    .WithHttpEndpoint(env: "PORT", port: 5173, isProxied: false)
    // …every following line unchanged…
```

`kiosk-web` (5174) and `kiosk-wall` (5175) change the same way. The block comment above them
gains one *why* sentence: install is off because the repo is a pnpm workspace installed at the
root, and `npm install` in an app directory cannot resolve `workspace:*`. The block stays inside
`if (isRunMode && !isE2ETests)`.

**Transitive pins.** `CentralPackageTransitivePinningEnabled` is on. `Aspire.Hosting.JavaScript`
13.5.4 depends on `Microsoft.Extensions.*` 10.0.11 and `OpenTelemetry.*` 1.15.3, and the props
pin them higher (10.0.12, 1.19.x). Upward pins are fine. A restore reporting NU1605 or NU1608 is
a blocker to report, not a pin to add silently.

## 4. Phase 4a: characterisation, observed green

**Colour: characterisation.** The change is behaviour-preserving (spec FR-005). `test-writer`
writes `tests/Integration.Tests/AppHostWebAppHostingTests.cs` (`[Trait("Category",
"FixtureLogic")]`), runs it **on `develop`'s package (9.5.2)** and returns the verbatim green
output. The engineer then swaps the package, and the file must pass **byte-identical**.

It builds the model with the run-mode arguments `AppHostE2ESwitchTests.SimulatorDisabledArguments`
uses: `Parameters:*` plus `ScenarioSimulator=false`, and no `E2ETests`. It asserts only what both
package versions expose through `Aspire.Hosting` types. **It never names `NodeAppResource` or
`JavaScriptAppResource`**, since that type is exactly what changes. For each of `management-web`
/ 5173, `kiosk-web` / 5174, `kiosk-wall` / 5175:

1. It is an `ExecutableResource` whose `Command` is `npm`, and whose evaluated arguments
   (`GetArgumentValuesAsync()` or the equivalent `CommandLineArgsCallbackAnnotation`
   evaluation) are exactly `["run", "dev"]`. **Before relying on this, check that argument
   evaluation needs no allocated endpoint.** The three apps pass no endpoint-valued args, so it
   should not. If it does, assert the `CommandLineArgsCallbackAnnotation` output against a
   context with no endpoints instead.
2. `WorkingDirectory` ends with `apps/management-web` or `apps/kiosk-web` (the kiosk path twice).
   Compare after normalising separators (memory: *source-scanning tests need slash normalising*).
3. **No `WaitAnnotation` on the resource targets a resource whose name ends `-installer`.** Also,
   `StackStatusReport.ExpectedResourceNames(model)` contains no name ending `-installer`. Both
   hold vacuously on 9.5.2 and must hold substantively on 13.5.4.
4. Exactly one `EndpointAnnotation`: name `http`, `Port` = the fixed port, `IsProxied == false`,
   `TargetPortEnvironmentVariable == "PORT"`.
5. It carries `ResourceRelationshipAnnotation` of type `Parent` to `api-gateway`.
6. Its `EnvironmentCallbackAnnotation`s produce, at minimum, the keys `VITE_API_GATEWAY_URL`,
   `VITE_KEYCLOAK_URL` and `NODE_ENV`, plus `VITE_LAYOUT_HUB_ORIGIN` for both kiosks and
   `VITE_KIOSK_MODE=wall` for `kiosk-wall`. It asserts **key presence** for endpoint-valued
   entries, not their values, because those need allocation. If key enumeration cannot run
   without allocation, drop fact 6 and say so in the PR. Do not weaken it into a string search
   of `AppHost.cs` (memory: *an assertion must not check its own input*).

**Counterfactual (memory: *prove a guard by counterfactual*).** On the branch, temporarily delete
`.WithNpm(install: false)` from one call site and observe fact 3 go red. Temporarily change one
port and observe fact 4 go red. Quote both failures in the PR, then restore.

**Shard entry.** Append `FullyQualifiedName~SmartSentinelEye.Integration.Tests.AppHostWebAppHostingTests.`
to the shard file with the fewest discovered tests. `integration-shard-coverage` fails
deterministically without it (memory: *new test classes need a shard-filter entry*).

**Existing nets that must also pass unmodified:** `AppHostE2ESwitchTests` (the three names
present), `AppHostMediaMtxImageTests`, the Architecture suite, and CI's four `e2e (Playwright,
full stack)` shards. The e2e shards are the end-to-end proof that all three apps boot, sign in
and render.

## 5. Commits (Conventional Commits, ADR-0030; no `Co-Authored-By`, ADR-0086)

1. `test(apphost): characterise the three web app resources before the host package swap`: the
   new test and its shard entry. Green on the 9.5.2 package.
2. `chore(apphost): host the web apps with Aspire.Hosting.JavaScript 13.5.4`: props, csproj,
   `AppHost.cs`, and the `kioskLatency.ts` comment. Test file untouched.

Both commits must build under `-c Release` on their own.

## 6. Risks and residuals

| Risk | Mitigation |
|---|---|
| `<app>-installer` shows in the dashboard as `NotStarted`. A click runs `npm install`, which fails on `workspace:*`. | Known residual, recorded in the spec's edge cases. Nothing starts it by default. If D2 flips to pnpm, it becomes useful instead. |
| Argument or environment evaluation in the test needs endpoint allocation. | §4 facts 1 and 6 name the fallback. Dropping an assertion is disclosed in the PR, never done silently. |
| A local boot collides with a sibling worktree's stack. | Phase 5 boots only after confirming no other AppHost is running (memory: *one machine, one Aspire stack*). Otherwise rely on CI's e2e shards and say so. |
| 13.5.4 behaviour differs from the source read here. | Phase 4b re-checks the cited lines against the restored package's decompiled or XML-doc surface before editing. |
| Parked PRs edit `Directory.Packages.props` / `AppHost.cs`. | Pre-PR check (ADR-0109). Rebase after any merge (CLAUDE.md autonomous-lane rule applies equally here). |

## 7. Phase 5 evidence to collect

- The verbatim `dotnet test --filter FullyQualifiedName~AppHostWebAppHostingTests` output on
  `develop` and on the branch, with the file hash equal.
- The two counterfactual failures (§4).
- A local `aspire run` (if the machine is free): a dashboard reading of three apps `Running` on
  5173/5174/5175, three installers `NotStarted`, and each app's first page loading. Otherwise the
  four green e2e shard runs, with their run IDs.
- `git grep -n "Aspire.Hosting.NodeJs\|AddNpmApp" -- ':!specs'` returning nothing (SC-001).
