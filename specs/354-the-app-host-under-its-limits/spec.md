# Spec 354: The AppHost composition root under its limits

**Feature Branch**: `refactor/2898-apphost-unguarded-split` (cut from `origin/develop` at `2d460b4d`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked explicitly (§7).

**Input**: Issue [#2898](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2898),
*Decide how AppHost.cs is split, then clear its S104 and S1541 (27 guards read it as text)*, and
the owner decision on it (2026-10-14): **option 3 — split only what no guard reads.** Do not edit
any source-reading guard, do not add a shared reader helper, prove per block that no test reads it
as text (with a counterfactual), and if the remainder cannot get under the limits, deliver what it
can and say which hits remain.

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green").

**Spec number.** 354. Checked on 2026-10-14: `origin/develop` (`2d460b4d`) highest 353; every
remote branch via `git ls-tree <branch> specs/` highest 353; local worktrees (`sse-1545`,
`sse-2898`) hold nothing above 353; the two open PRs (#2837 Dependabot, #2811) carry nothing above
353. Re-check before the PR is opened.

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17; S104 <= 300 lines of code per file, S1541
<= 10, S138 <= 30 lines/method, S107 <= 4 parameters; advisory `warning`), ADR-0144 (autonomous
lane; characterisation colour; the lane may not weaken a gate), ADR-0105 (AppHost is excepted from
the argument-guard rule), ADR-0103 (integration tests via the Aspire fixture; model tests compose
the AppHost without Docker), ADR-0109 (`AppHost.cs` is a contention file), ADR-0036 (smallest
change), decision row 024 of `0000-initial-decisions.md` (Aspire is the composition root). **No new
ADR is needed**: the composition, the resources and the lanes are unchanged; only the source file
layout of the composition root changes.

**Latency budget.** N/A — no leg of constitution §IV is touched. The AppHost composes resources at
boot; nothing here runs on the event-to-overlay path.

---

## 1. What was measured (premise verified, not assumed)

The issue's command, run on `2d460b4d` with all `src/AppHost` warnings kept:

```sh
dotnet build src/AppHost -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -E 'warning (S|CA|IDE)' | grep -i 'src.AppHost.[A-Za-z]*\.cs' | sort -u
```

Result: 0 errors, **three hits in `src/AppHost`**:

| File | Rule | Measured | Limit | In scope |
|---|---|---|---|---|
| `AppHost.cs(1,1)` | S104 | 380 lines | 300 | **Yes** |
| `AppHost.cs(1,1)` | S1541 | 27 (top-level file) | 10 | **Yes** |
| `StackStatusReport.cs(201,49)` | S107 | 6 parameters | 4 | No (not in #2898; §8) |

The file is 988 physical lines; S104 counts **lines of code** (non-blank, non-comment), and a count
over the file reproduces 380 exactly. The issue's figures are still current.

## 2. Who reads `src/AppHost` as text (the premise that the issue's "27 guards" stated)

`grep -rl "AppHost.cs" tests` returns 21 files today, not 27. Every one was read for **how** it
consumes the AppHost:

**2a. Text readers of `AppHost.cs` — exactly two.**

| Test | What it reads | Exact match it needs |
|---|---|---|
| `tests/Architecture.Tests/ContainerImagePinTests.cs` | `src/AppHost/AppHost.cs` by literal path, line by line, plus `scripts/` | Regex `AddContainer\(\s*"name"\s*,\s*"image"\s*,\s*"tag"` (3 hits: `mediamtx` l.228, `fixture-video` l.284, `camera-sim` l.939); regex `WithImageTag\(\s*"tag"` (3 hits: postgres l.103, rabbitmq l.149, keycloak l.175); regex `bluenviron/mediamtx:<tag>` as written prose (1 hit, l.277, in the `fixture-video` comment). Needs >= 1 pin, >= 3 MediaMTX pins, one MediaMTX tag. `WithImageTagOwner` looks up to 12 lines back for `^var X = builder` — message text only, never the verdict. |
| `tests/Architecture.Tests/FoundingDecisionRecordTests.cs` (`The_constitution_does_not_claim_a_metrics_stack_that_was_never_built`) | `src/AppHost/AppHost.cs` whole text | Case-insensitive `prometheus`. An **absence** check: it is green because no line contains it, and it reads every line of the file to say so. |

**2b. Model readers — not text readers (13 classes).** They compose the application model through
`DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(...)` and
inspect resources/annotations/gathered environment, so they are indifferent to which source file a
statement lives in: `AppHostStackPersistenceTests`, `AppHostE2ESwitchTests`,
`AppHostStackStatusTests`, `AppHostReplicaCountTests`, `AppHostGatewayRateBudgetTests`,
`AppHostContainerImagePinTests`, `AppHostMediaMtxImageTests`, `AppHostMigrationGateTests`,
`AppHostParameterOverrideTests`, `AppHostCamerasRemoteTests`, `AppHostEventIngestionRemoteTests`,
`AppHostWebAppHostingTests`, `RealmImportMirrorTests` (+ `.ProductionDefaults`). Three of them also
read `.github/workflows/ci.yml` text (for the boot command), not AppHost source.

**2c. Prose only.** `WhepAuthorizeCeilingTests` and `WhepAuthorizePartitionKeyTests` (the issue
named both) read `src/StreamDistribution/Api/Program.cs` and `appsettings.json`; they mention
`AppHost.cs` in doc comments only. Likewise `WhepAuthorizeRateLimitTests`, `AspireFixture`,
`PostgresConnectionBudgetIntegrationTests`, `BruteForceLockoutIntegrationTests`,
`MasterRealmBruteForceIntegrationTests`, `PasswordPolicyEnforcementIntegrationTests`,
`MqttResubscribeAfterBrokerOutageIntegrationTests`, `DockerfileUpstreamPinTests`. Outside `tests/`,
`AppHost.cs` appears only in comments (`ci.yml`, `remotes.ts`, `kioskLatency.ts`, one script).

**2d. Other `src/AppHost` paths.** Read as data, never as C#: `Realms/*.json` (eleven tests),
`mosquitto/acl.txt`, `mosquitto/Dockerfile`, `Resources/clips`. None is touched here.

**2e. Tree-wide C# scanners** (`StaleCodeConventionTests`, `NameMutabilityConventionTests`,
`HandlerDeconstructionTests`, `EventMetadataFabDeclarationTests`, `ResilienceRegistrationTests`,
`RetiredSeededPasswordTests`, `FoundingDecisionRecordTests.DeclaredInSource`) enumerate
`src/**/*.cs`. Moving text between files under `src/AppHost` leaves the union of lines they read
unchanged; none matches anything in `AppHost.cs` today (handlers, `STALE`, route templates,
resilience registration, retired passwords, `IRuleEngine`/`AelInterpreter`). They keep passing for
the same reason they pass now. `.editorconfig`'s `[src/AppHost/**.cs]` section and
`Directory.Build.props`' project-level AppHost exemption already cover new files in the project.

**Agent briefs** (`infra-engineer.md`, `infra-reviewer.md`, `backend-engineer.md`,
`frontend-engineer.md`) say new runtime resources go in `src/AppHost/AppHost.cs` and list it as a
contention file. Not guards; see §7 J2.

## 3. Block classification (evidence: §2a's matches, by line on `2d460b4d`)

"Guarded" = a text reader matches a line inside it. No reader asserts order or adjacency across
blocks (`ContainerImagePinTests` documents that it has "no notion of call order").

| Block (lines) | LOC | CC | Guarded? | Decision |
|---|---|---|---|---|
| `using`, `builder` (1, 19) | 2 | — | No | Stays (root) |
| Lane flags (20-38) | 7 | 5 | No | **Move** (`StackLanes`) |
| Parameters (40-89) | 12 | — | No | Stays (§7 J3) |
| postgres declaration (100-120) | 5 | — | **Yes** `WithImageTag` l.103 | Stays |
| postgres persistence (129-135) | 7 | 1 | No | **Move** |
| databases (137-145) | 9 | — | No | Stays |
| rabbitmq declaration (147-150) | 4 | — | **Yes** `WithImageTag` l.149 | Stays |
| rabbitmq persistence (153-158) | 6 | 1 | No | **Move** |
| keycloak declaration (172-177) | 6 | — | **Yes** `WithImageTag` l.175 | Stays |
| keycloak persistence (195-200) | 6 | 1 | No | **Move** |
| mediamtx declaration (227-248) | 8 | — | **Yes** `AddContainer` l.228 | Stays |
| fixture-video (281-289) | 8 | 1 | **Yes** `AddContainer` l.284, written ref l.277 | Stays |
| mediamtx ICE publish (291-306) | 6 | 2 | No | **Move** |
| mediamtx persistence (310-313) | 4 | 1 | No | **Move** |
| mosquitto declaration (331-341) | 11 | — | No (but a container declaration, J2) | Stays |
| mosquitto persistence (344-349) | 6 | 1 | No | **Move** |
| storage + azurite persistence (375-387) | 12 | 1 | No | Declaration stays; the `if` inside `RunAsEmulator` **moves** |
| migrations (390-420) | 25 | — | No | Stays (J2) |
| nine context services (422-449, 545-633) | 91 | — | No | Stays (J2) |
| WHEP ceiling, integration lane (521-526) | 6 | 1 | No | **Move** |
| MediaMTX auth-hook address, run lane (536-543) | 8 | 1 | No | **Move** |
| revocation-list wiring loop (643-653) | 11 | 1 | No | Loop **moves**; the consumer list stays at the call site |
| migration gate loop (677-684) | 8 | 1 | No | Loop **moves**; the dependent list stays at the call site |
| integration-lane tuning (686-711) | 6 | 1 | No | **Move** |
| api-gateway (741-753) | 13 | — | No | Stays (J2) |
| gateway rate budget (782-785) | 4 | 2 | No | **Move** |
| web applications (792-910) | 50 | 2 | No | **Move whole** (J2); the lane `if` stays at the call site |
| scenario simulator (936-984) | 36 | 4 | **Yes** `AddContainer` l.939 | Stays; only its inner `if (isPersistentDevStack)` (950-953, CC 1) **moves** |
| `Build`, report, run (986-988) | 3 | — | No | Stays |

CC column is the decision points each block contributes to S1541 (`if`, `foreach`, `&&`, `||`);
it sums to 27 + 1 base = 28 while Sonar reports 27, so one point (most likely the `if` inside the
`RunAsEmulator` lambda) is not counted. Plan §4 re-measures; the table is the estimate, the build
is the authority.

**Expected result** (estimate, re-measured at Phase 5): S104 380 -> about **255-265** (clears);
S1541 27 -> about **7** (base 1 + fixture-video `if` 1 + simulator `if && &&` 3 + web-application
`if &&` 2; clears). If the build disagrees, the hit stays and the PR says so (FR-008).

## 4. User stories

### US1 (P1): the composition root clears ADR-0084 without any guard losing sight of what it checks

As the owner, I want `AppHost.cs` under S104 and S1541 by extracting only the blocks no text guard
reads, so that the composition is unchanged, every existing guard is unedited and still sees every
statement it matched before, and nothing a guard looks for can later hide in a new file.

**Independent test.** On the branch tip: (1) the §1 build shows no S104/S1541 for `AppHost.cs` and
no new `S*` hit anywhere in `src/AppHost`; (2) `git diff origin/develop --stat -- tests/` lists only
the two new characterisation test files, their snapshot files and one shard-filter line; (3) the
model snapshot test (C1) passes unmodified against the snapshots captured before the first
production commit; (4) the extraction-boundary test (C2) passes; (5) the guarded-token diff (plan
§5) is empty; (6) `Architecture.Tests` and the `FixtureLogic` category of `Integration.Tests` pass.

**Acceptance scenarios**

1. *Happy path.* **Given** `AppHost.cs` at 380 LOC / CC 27, **when** the unguarded blocks of §3 are
   extracted, **then** the Release build reports neither S104 nor S1541 for `AppHost.cs`, and the
   new files report no S104/S107/S138/S1541/S3776/S134.
2. *Guard still sees what it matched.* **Given** `ContainerImagePinTests` matched 3 `AddContainer`
   pins, 3 `WithImageTag` pins and 1 written `bluenviron/mediamtx:` reference in `AppHost.cs`,
   **when** the refactor is complete, **then** the same seven (name, image, tag) tuples are matched
   in `AppHost.cs` (line numbers may differ) and the test passes unedited.
3. *Absence guard keeps its scope honest.* **Given** `FoundingDecisionRecordTests` reads only
   `AppHost.cs` for `prometheus`, **when** code moves to new files, **then** C2 fails if any file in
   `src/AppHost` other than `AppHost.cs` contains `prometheus`, `AddContainer(`, `AddDockerfile(`,
   `WithImage(`, `WithImageTag(`, `WithImageRegistry(` or `bluenviron/mediamtx:` — proven by
   counterfactual (C2 goes red when one is planted, green when removed).
4. *Conflict — a moved statement changes the model.* **Given** C1's snapshots captured on the
   unrefactored tree, **when** a refactor commit reorders two annotations, drops an environment
   variable or changes a lane condition, **then** C1 fails with a diff naming the resource and the
   lane; the engineer fixes the production code, never the snapshot.
5. *Bad request — a guard cannot be satisfied without editing it.* **Given** a refactor commit that
   turns any existing guard red, **when** the only route to green is editing that guard, **then** the
   commit is reverted and the block stays in `AppHost.cs` (the lane may not weaken a gate).
6. *Auth/lanes.* **Given** the four lane argument sets (developer, end-to-end CI job, integration
   fixture, unparseable switches), **when** each is composed, **then** persistence (lifetime, data
   volumes, pgAdmin), the ICE port map, the auth-hook address, the WHEP and gateway rate overrides,
   the integration-lane tuning, the web applications and the simulator appear in exactly the lanes
   they do today.

## 5. Functional requirements

- **FR-001** Every statement a text reader matches today (§2a) stays in `src/AppHost/AppHost.cs`,
  as part of the unchanged statement it belongs to.
- **FR-002** Every resource declared through `AddContainer`, `AddDockerfile`, `AddPostgres`,
  `AddRabbitMQ`, `AddKeycloak`, `AddAzureStorage`, `AddProject` stays in `AppHost.cs` (J2). The
  web-application block is the one exception: its `AddJavaScriptApp` declarations move with it.
- **FR-003** Every extracted block is replaced by one call **at the position the block occupied**,
  so the order in which builder methods execute — across resources and within each resource's
  annotation list — is unchanged. No statement is reordered, merged, deduplicated or rewritten as a
  loop that was not one.
- **FR-004** Lane conditions keep their exact boolean meaning; the four flags keep their parse
  shapes and fail-open defaults (`ScenarioSimulator`, `PersistentStack` absent or unparseable =
  on; `E2ETests` absent or unparseable = off).
- **FR-005** New files live in `src/AppHost`, namespace `SmartSentinelEye.AppHost`, as `internal
  static` extension classes (the `OverridableParameters.cs` pattern) plus one `internal` record for
  the lane flags. No new project, package, abstraction layer or configuration knob.
- **FR-006** No new ADR-0084 hit in `src/AppHost`: every new method <= 30 LOC, <= 4 parameters
  (an extension's `this` counts), CC <= 10; every new file <= 300 LOC.
- **FR-007** Comments move with the code they explain and are otherwise unchanged, except that
  positional references made false by the move ("see postgres above", "`:211-216` above", "below")
  are re-pointed to the named block or file. No moved comment may introduce a C2-banned token.
- **FR-008** If the measured result leaves S104 or S1541 over the limit, the PR states the
  remaining figure and why; no guard, threshold, suppression or `.editorconfig` entry changes to
  reach green.
- **FR-009** No existing test file changes. The only test-tree edits are C1, C2, C1's snapshot
  files, and one line appended to one `tests/Integration.Tests/ci-shards/shard-N.filter`.

## 6. Characterisation (Phase 4a, observed green before any production edit)

- **C1 — composed-model snapshot** (`Integration.Tests`, `[Trait("Category", "FixtureLogic")]`, no
  Docker). For each lane argument set, compose the AppHost with
  `DistributedApplicationTestingBuilder` and render a canonical text snapshot: every resource in
  model order with its type, and every annotation in order with its stable content (image/tag/
  registry, lifetime, mounts, endpoints, waits incl. wait type, relationships, references, replica
  count, health checks, container runtime args), plus every environment variable gathered by
  invoking each `EnvironmentCallbackAnnotation` in a Run execution context (the pattern
  `AppHostGatewayRateBudgetTests`/`AppHostStackPersistenceTests` already use), values rendered by
  `ValueExpression` for references/parameters and literally for strings. Paths normalised
  (repository root -> `<repo>`, `/` separators). Compared against committed snapshot files captured
  on the unrefactored tree. Counterfactual required: three planted production edits (one in a moved
  block, one ordering swap, one lane condition) each turn it red; reverting turns it green.
- **C2 — extraction boundary** (`Architecture.Tests`). Reads every `*.cs` under `src/AppHost`
  except `AppHost.cs` (excluding `bin/`, `obj/`) and fails on any of the tokens in §4 scenario 3.
  Non-vacuity: asserts the scan found >= 2 files (today `OverridableParameters.cs`,
  `StackStatusReport.cs`) and that `AppHost.cs` itself still contains `AddContainer(`,
  `WithImageTag(` and `bluenviron/mediamtx:` (so a broken scan cannot pass). Counterfactual
  required: a planted `// prometheus` in `OverridableParameters.cs` turns it red.

Existing model tests in §2b already pin much of the moved behaviour (persistence, ICE, gateway
budget, migration gate, web apps, simulator lanes) and must pass unmodified.

## 7. Judgement calls

- **J1 — C2 is a new guard, not a shared reader helper.** The owner forbade teaching the existing
  readers to read more files. C2 leaves them exactly as they are and asserts the complement: what
  they look for exists nowhere they do not look. Without it, the honest answer to "would a guard
  that matches nothing silently pass?" is *yes* for `FoundingDecisionRecordTests` (a Prometheus
  added in a new file would pass) and for `ContainerImagePinTests`' floating-tag check (a
  `WithImageTag("latest")` in a new file would pass). C2 makes that impossible instead of unlikely.
  If the owner rejects C2, the fallback is to move nothing that can host a declaration, which
  leaves S104 at about 300-306 (not provably under) — see J2.
- **J2 — resource declarations stay, except the web applications.** The briefs and
  `FoundingDecisionRecordTests`' own comment treat `AppHost.cs` as the place resources are
  declared, so every container and project declaration stays. The web-application block moves
  whole: no text reader matches anything in it, C2 covers it, it is one self-contained concern
  (Vite dev servers in the run lanes), and without it S104 lands at about 300-306 — not provably
  under. Moving the nine context services and the migration runner would also clear S104 but needs
  a parameter object to pass nine databases and the shared infrastructure (S107), which is new
  structure bought only for a line count; rejected. The agent briefs are not edited (the statement
  "new runtime resources go in `AppHost.cs`" stays true for new resources).
- **J3 — parameters stay.** Twelve LOC, CC 0, and `AppHostParameterOverrideTests` documents them
  as "`AppHost.cs:28-64`" (prose). Not needed to reach the limits.
- **J4 — publish mode.** `DistributedApplicationTestingBuilder` composes in run mode. If the
  test-writer can compose a publish-mode model in-process, C1 adds that lane; if not, C1 records
  the gap in its doc comment and FR-003/FR-004 are verified at review by reading each call site's
  condition against the original.

## 8. Out of scope

- `StackStatusReport.cs` S107 (6 parameters) — not in #2898; file a separate issue if wanted.
- Any change to resources, images, tags, lanes, environment, ordering, comments' substance, agent
  briefs, `.editorconfig`, `SonarLint.xml`, `Directory.Build.props`, or any existing test.
- Stale line references in tests' doc comments (`AppHost.cs:441-442`, `AppHost.cs:30,144`,
  `AppHost.cs:28-64`): already stale, prose only, left alone.

## 9. Success criteria

- **SC-001** §1 build on the tip: no S104/S1541 for `AppHost.cs`; `src/AppHost` hits = exactly
  `StackStatusReport.cs` S107 (pre-existing) — or the PR states each remaining hit (FR-008).
- **SC-002** C1 and C2 observed green on the unrefactored tree, red under their counterfactuals,
  green unmodified on the tip.
- **SC-003** Plan §5's guarded-token diff is empty; `grep -ci prometheus src/AppHost/*.cs` is 0 for
  every file.
- **SC-004** `git diff origin/develop --stat -- tests/` matches FR-009 exactly.
- **SC-005** CI green, including the integration shards and the end-to-end job (which boots the
  run-mode stack with `ScenarioSimulator=false PersistentStack=false`).
