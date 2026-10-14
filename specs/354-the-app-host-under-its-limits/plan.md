# Plan 354: The AppHost composition root under its limits

**Spec**: [spec.md](./spec.md). **Issue**: #2898. **Engineer**: infra-engineer. **Test-writer**:
C1, C2. **Phase 4a colour**: characterisation, observed green.

## 1. Context and constitution check

- **Bounded context / layers**: none. `src/AppHost` is the Aspire composition root (decision row
  024); it has no Domain/Application/Infrastructure/Api layering and no domain model, so
  constitution §II and the value-object rules do not apply. No entity, value object, invariant,
  domain event or integration event is introduced or changed.
- **Boundaries**: no new project references; the AppHost already references every service project.
  NetArchTest rules are unaffected.
- **Messaging**: none.
- **§IV latency**: N/A (spec header).
- **ADR-0084**: the target. **ADR-0144**: no guard weakened; characterisation first. **ADR-0105**:
  AppHost remains excepted from `Ensure.That` — the new methods take Aspire builders from the
  framework's own call chain and add no argument guards (matching `OverridableParameters.cs`).
- **ADR-0109**: `AppHost.cs` is a contention file; this branch owns it for its duration. The new
  files are new, so no other in-flight branch contends for them.
- **Result: aligned. No ADR needed.**

## 2. Target shape

`AppHost.cs` keeps, in today's order: the builder, the parameter declarations, every container and
project declaration (with their image pins), the databases, the fixture-video and simulator blocks,
`Build`/`Attach`/`RunAsync`. Each extracted block becomes **one call at its original position**.

New files (names are proposals; the engineer may rename within ADR-0091's no-abbreviation rule):

| File | Contents | Replaces |
|---|---|---|
| `StackLanes.cs` | `internal sealed record StackLanes(bool IsRunMode, bool IsE2ETests, bool IsScenarioSimulatorEnabled, bool IsPersistentDevStack)` with `static StackLanes Read(IDistributedApplicationBuilder builder)`; the four parses verbatim, with the lane comment (lines 12-17, 22-37). | Lines 20-38 -> `var (isRunMode, isE2ETests, isScenarioSimulatorEnabled, isPersistentDevStack) = StackLanes.Read(builder);` so every existing `isX` use-site stays byte-identical. |
| `DeveloperLanePersistence.cs` | One extension per resource shape, each `if (!isPersistentDevStack) return; <the original chain>`: postgres (lifetime, data volume, pgAdmin), rabbitmq, keycloak (lifetime, data volume), plain container (lifetime only: mediamtx, camera-sim), mosquitto (lifetime, `mosquitto-data` volume), azurite (lifetime, data volume). The "Persistent means…" explanations that are about persistence move here. | The seven `if (isPersistentDevStack) { … }` blocks (129, 153, 195, 310, 344, the one inside `RunAsEmulator`, 950). |
| `RunLaneOverrides.cs` | MediaMTX ICE publish (`isRunMode && !isE2ETests`); MediaMTX auth-hook address callback (`isRunMode`, takes `streamDistribution`); gateway rate budget (`isRunMode && !isE2ETests`). | Blocks 291, 536, 782. |
| `IntegrationLaneOverrides.cs` | WHEP ceiling on stream-distribution (`isE2ETests`, with its long accounting comment); audit retention tick + ingest breakdown + ingestion concurrency (`isE2ETests`). | Blocks 521, 686. |
| `ServiceDependencies.cs` | Revocation-list wiring (takes the consumer array, `identity`, the secret); migration gate (takes the dependent array and `migrations`). Loop bodies verbatim. | Loops 643, 677; the two array literals stay at the call sites, unchanged. |
| `WebApplications.cs` | The web-application block, split into methods <= 30 LOC (e.g. the two federated remotes, management-web, the two kiosk instances), each chain verbatim and in today's order. Entry: `builder.AddWebApplications(apiGateway, keycloak, layoutComposition)` (4 parameters with `this`). | Block 792-910; `if (isRunMode && !isE2ETests)` stays in `AppHost.cs` around the one call (keeps S107 at 4). |

Parameter budget (S107 <= 4 including `this`): every method above fits by passing the needed
booleans individually (at most two) or the resources it touches; if one would need five, keep that
condition at the call site instead. Each method has CC <= 3.

**Rules for the move (from spec FR-001..FR-007):**
1. Never move a line containing `AddContainer(`, `AddDockerfile(`, `WithImage(`, `WithImageTag(`,
   `WithImageRegistry(` or `bluenviron/mediamtx:`; never move a declaration other than the web
   applications'.
2. Copy chains verbatim — same calls, same arguments, same order. No helper that collapses two
   different chains into one (kiosk-web vs kiosk-wall differ by `VITE_KIOSK_MODE` *before*
   `WaitFor`; the two remotes are identical and may share one method only because their call
   sequence is identical).
3. A lambda's captured resource becomes a parameter; effect identical.
4. Comments move with the code they explain. Re-point positional references only (e.g. line 458's
   already-stale "`:211-216` above" -> "the fixture-video block in `AppHost.cs`"; "see postgres
   above" -> "see `DeveloperLanePersistence`"). The spec FR-007 bans nothing else.
5. `var` everywhere; collections with explicit type + collection expression; one argument per line
   when wrapping; no underscores on fields (CLAUDE.md house rules).

## 3. Characterisation design (test-writer)

### C1 `tests/Integration.Tests/AppHostCompositionCharacterisationTests.cs`

- `[Trait("Category", "FixtureLogic")]`; appended to **one** `ci-shards/shard-N.filter` (the
  Docker-free step selects by trait; the shard filters must still list every class — a missing entry
  fails deterministically).
- Lanes (argument sets mirror `AppHostStackPersistenceTests`): **Developer** (`DevArguments`),
  **EndToEndJob** (+ `ScenarioSimulator=false`, `PersistentStack=false`), **IntegrationFixture**
  (+ `E2ETests=true`), **Unparseable** (+ `ScenarioSimulator=nope`, `PersistentStack=nope`,
  `E2ETests=nope`). Publish: attempt in-process (spec J4); if impossible, say so in the class doc.
- Render: per resource in `DistributedApplicationModel.Resources` order — name, CLR type name; then
  each annotation in list order with a stable rendering. Unknown annotation types render as their
  type name only (so new Aspire internals do not make the snapshot unreadable, but their presence
  and position are pinned). Environment: invoke callbacks as `AppHostGatewayRateBudgetTests`'
  `RawGatheredEnvironmentAsync` does (Run context), keep insertion order, render values: `string`
  literally; `IManifestExpressionProvider` by `ValueExpression`; `ParameterResource` by
  `ValueExpression` (never its value — secrets); else type name. Normalise the repository root to
  `<repo>` and `\` to `/` (Linux CI vs Windows).
- Snapshots: `tests/Integration.Tests/AppHostComposition/<Lane>.snapshot.txt`, copied to output.
  Generated **once** on the unrefactored tree by the test-writer (from the test's own failure
  output on first run), then committed. **No update switch, environment flag or regeneration path
  in committed code** — the engineer cannot refresh them.
- Assertion: line-by-line equality with a message showing the first differing line, the lane and
  the resource.
- Counterfactuals (record verbatim output for the PR body, then revert): (a) change
  `WhepAuthorizeRateLimiting__PermitLimit` `"50"` -> `"51"` (IntegrationFixture red); (b) swap
  `.WaitFor(rabbitmq)` and `.WaitFor(keycloak)` on `automation` (all lanes red); (c) change the
  postgres persistence condition to `isRunMode` (EndToEndJob red).

### C2 `tests/Architecture.Tests/AppHostExtractionBoundaryTests.cs`

- Enumerate `src/AppHost/**/*.cs` except `src/AppHost/AppHost.cs`, `bin/`, `obj/` (relative paths
  with `/`). Fail listing file:line for any match of: `AddContainer(`, `AddDockerfile(`,
  `WithImage(`, `WithImageTag(`, `WithImageRegistry(`, `bluenviron/mediamtx:`, and
  case-insensitive `prometheus`.
- Non-vacuity: >= 2 files scanned; `AppHost.cs` contains each of `AddContainer(`, `WithImageTag(`,
  `bluenviron/mediamtx:`.
- Doc comment names the two guards it protects and why they are not edited (#2898 owner decision).
- Counterfactual: plant `// prometheus` in `OverridableParameters.cs` -> red; plant
  `.WithImageTag("x")` in a scratch file under `src/AppHost` -> red; revert -> green.

## 4. Measurement

Before and after (Phase 5):

```sh
dotnet build src/AppHost -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -E 'warning (S|CA|IDE)' | grep -i 'src.AppHost.[A-Za-z]*\.cs' | sort -u
```

Baseline (2d460b4d): `AppHost.cs` S104 380, S1541 27; `StackStatusReport.cs(201,49)` S107 6.
Stop the running AppHost before building (memory: a running stack locks the binaries).

## 5. Guarded-token diff (proves FR-001 independently of the guards)

Run on `origin/develop` and on the tip; the outputs must be identical:

```sh
grep -nE 'AddContainer\(|WithImageTag\(|WithImage\(|WithImageRegistry\(|bluenviron/mediamtx:|AddDockerfile\(' \
  src/AppHost/AppHost.cs | sed -E 's/^[0-9]+://'
grep -ci prometheus src/AppHost/*.cs
```

## 6. Verification (Phase 5)

1. §4 build: no S104/S1541 for `AppHost.cs`; no new hit in the new files.
2. `dotnet test tests/Architecture.Tests -c Release` (includes `ContainerImagePinTests`,
   `FoundingDecisionRecordTests`, C2, the tree-wide scanners).
3. `dotnet test tests/Integration.Tests -c Release --filter "Category=FixtureLogic"` (C1 and the
   §2b model tests: persistence, E2E switch, migration gate, web-app hosting, remotes, gateway
   budget, replica count, parameter override, image pins).
4. §5 diff empty.
5. Boot: CI's end-to-end job boots the run-mode stack and its readiness wait (StackStatusReport)
   is the observation of record. A local `aspire run` only if no other stack is running on this
   machine (one machine, one Aspire stack); do not stop a shared stack.

## 7. Risks

- **Snapshot instability** (Aspire-internal annotation content, ordering of gathered env across
  callbacks): mitigated by rendering unknown annotations by type only and by capturing twice before
  committing (two identical runs).
- **Overload ambiguity** among persistence extensions (`IResourceBuilder<ContainerResource>` for
  mediamtx, camera-sim and mosquitto): distinct method names for distinct chains.
- **Silent condition drift** in publish mode if J4 cannot be covered: reviewer checks every call
  site's condition against `origin/develop` line by line.
