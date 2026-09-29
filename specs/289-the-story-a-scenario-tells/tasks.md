# Tasks: Spec 289, the story a scenario tells

**Spec:** `spec.md` · **Plan:** `plan.md` · **ADR:** ADR-0163 (amends ADR-0111) · **Issue:** not yet created (see Board) · **Lane:** supervised

**Phase 4a colour, declared here (ADR-0144):**

- **PR-A, all tasks: CHARACTERISATION.** The rule-seed refactor is behaviour-preserving. T-A01 pins the 12 legacy create bodies and is captured **green on `develop` code before T-A03 edits anything**. T-A03 then passes with the pins **unmodified**. An edited pin is a block, not an adjustment.
- **PR-B: RED.** All new behaviour. Every test task below is observed failing before its implementation task, and the failure is quoted in the PR.
- **PR-C: T-C01 is CHARACTERISATION** (swapping the test principal must leave the assertions unmodified and green). **Everything else is RED.**
- **PR-D: RED for the story check.** PR-A's pins stay green, unmodified (SC-003).

**Engineers:**

- `test-writer`: every `T-*-Tnn` test task.
- `backend-engineer`: implementation tasks.
- `infra-engineer`: T-C07 (realm), T-C08 (AppHost).
- `backend-reviewer`: phase 6 for every PR. `security-reviewer`: PR-C (a realm scope grant).

**Board.** The feature-level issue does not exist yet. Before Phase 4, create it (label `enhancement`, linking spec 289 and ADR-0163) and add it to Project #13 by hand (the CLAUDE.md Phase 3 gate). No per-task issues.

**Contention (ADR-0109).** `src/ScenarioSimulator/Log.cs` and `Seeding/ScenarioSeeder.cs` are edited by every PR, so **A → B → C → D is strictly sequential**, with each PR cut from `develop` after its predecessor merges. `src/AppHost/AppHost.cs` and the realm file are repo-wide contention files, touched only by PR-C. Rebase immediately before opening it.

**Per-commit rule (ADR-0087).** Every commit builds (`dotnet build -c Release`), passes `dotnet format --verify-no-changes`, and passes `tests/ScenarioSimulator.Tests` on its own. Conventional Commits, no `Co-Authored-By` (ADR-0086).

**Stack rules.** One Aspire stack per machine, and stop it before building. `ScenarioSimulator.Tests` needs no stack. Only Phase 5 (plan §8) and T-C01's integration run boot one.

`[P]` = can run in parallel with the other `[P]` tasks in the same group (disjoint files, no ordering dependency).

---

## Phase 0: Docs (this branch, `289-the-story-a-scenario-tells`)

- [x] **T-000** Spec, plan and tasks for 289. ADR-0163 plus the `Amended by` line on ADR-0111.
- [ ] **T-001** Create the feature issue and add it to Project #13. Verify with `gh project item-list 13 --owner smartsolutionslab --limit 2000` and filter by `content.url` (memory: *Project #13 issues*).

## PR-A: the legacy highlight becomes a rule seed (foundational, characterisation)

Blocks PR-B, PR-C and PR-D. It touches only `Seeding/*` and `tests/ScenarioSimulator.Tests`.

- [ ] **T-A01** `tests/ScenarioSimulator.Tests/LegacyHighlightRuleBodyTests.cs`: for each of the 12 assets in the three shipped scenarios, drive `ScenarioSeeder`'s overlay-and-rule path against a recording `HttpMessageHandler` (reuse `Fakes/FakeHttpClientFactory.cs`). Assert the exact `POST /rules?fabId=munich` JSON body: name, `triggerSource`, `triggerKind`, predicate string, `actionType`, overlay, duration and null variable fields. Also assert the publish call's path and `If-Match`. Build the expected bodies from literals written in the test, never from the code under test (memory: *an assertion must not check its own input*). **Run it on unmodified `develop` code and record it green.**
- [ ] **T-A02** `[P]` The same class: add a fact that pins the unknown-comparison default. `Comparison: "between"` produces `>=`. This characterises the defect so that F-2 later changes it deliberately and visibly.
- [ ] **T-A03** Add `Seeding/RuleSeed.cs` and `Seeding/HighlightRuleSeed.cs` (plan §3.4). Change `AutomationRulesClient.EnsureRuleAsync` to `(RuleSeed seed, CancellationToken)`, keeping the private `CreateRuleBody` mapping and the `Operator` switch as they are. `ScenarioSeeder.SeedOverlayAndRuleAsync` builds the seed through `HighlightRuleSeed.From`. The existing `AutomationRulesClientTests` change **only** at their call sites, so the assertions stay unmodified. T-A01 and T-A02 pass unmodified.
- [ ] **T-A04** Phase 5 V-1: dump the rules on a fresh stack before and after, and quote the diff (identifiers and timestamps excepted) in the PR.

## PR-B: US1, a detection seen on the video lights the tile (P1, red)

Depends on PR-A being merged.

**Tests first. The three groups below are disjoint and run `[P]`:**

- [ ] **T-B01** `[P]` `tests/ScenarioSimulator.Tests/CueScheduleTests.cs` (pure). Covers:
  - the first cue in loop 0;
  - wrap to the next loop;
  - no re-emission of `lastEmitted` within the same (anchor, loop);
  - an anchor change resets state;
  - a cue more than 250 ms late is skipped, not emitted;
  - a negative elapsed time (a future anchor from clock skew) waits;
  - one cue per clip;
  - a cue at `AtMs 0` on a loop boundary.
- [ ] **T-B02** `[P]` `tests/ScenarioSimulator.Tests/ClipManifestValidationTests.cs` (pure). One refusal fact per plan §5.3 rule, and each violation names the cue index. A valid manifest yields no violations. Unparseable JSON is one violation. A missing sidecar gives `Option.None` and no warning.
- [ ] **T-B03** `[P]` `tests/ScenarioSimulator.Tests/CameraSimPathClientTests.cs`:
  - a canned MediaMTX 1.21 `/v3/paths/get/{path}` JSON body (copied from the verified live response in plan §2) parses to `Ready` + `ReadyTime`;
  - `ready:false` maps to not ready;
  - a 404 maps to `None`;
  - a 5xx is logged and gives `None`, not a throw.

**Tests that follow the reaction format:**

- [ ] **T-B04** `tests/ScenarioSimulator.Tests/ReactionRuleSeedTests.cs`:
  - a predicate is wrapped as `$.device == '<path>' && (<predicate>)`, and a declared `||` stays inside the parentheses;
  - the rule is named `{scenario}-{asset}-{name}`;
  - a name longer than 63 characters or containing illegal characters is refused (the `RuleName` grammar);
  - a `HighlightOverlay` on an asset with no `Overlay` is refused;
  - an unknown `Then.Type` is refused.
- [ ] **T-B05** `[P]` `tests/ScenarioSimulator.Tests/MqttSampleMapperTests.cs` (existing class, new facts):
  - a cue maps to `fab/munich/inference/<path>` with the plan §3.2 body;
  - absent optional fields are **omitted**, not null;
  - `clipOffsetMs` is present;
  - the existing sensor facts pass unmodified.
- [ ] **T-B06** `[P]` `tests/ScenarioSimulator.Tests/MqttPublisherStartTests.cs`: two concurrent `StartAsync` calls start exactly one connect loop. Use the existing `Fakes/FakeMqttClient.cs` and count connect attempts.
- [ ] **T-B07** `tests/ScenarioSimulator.Tests/ClipCueHostedServiceTests.cs`, with a fake path client, a fake publisher sink and a **hand-written** manual `TimeProvider` in `tests/ScenarioSimulator.Tests/Fakes/`. That follows the house convention: `ServiceDefaults.Tests` deliberately does not use `FakeTimeProvider` (ADR-0052/0054), and the package is not referenced anywhere. Covers:
  - a ready path emits the cue at `readyTime + AtMs`;
  - a not-ready path emits nothing (FR-005);
  - a changed `readyTime` re-anchors;
  - a camera-sim failure on one path does not stop another path;
  - cues run for **every** active scenario, not only `Animated` (FR-006).
- [ ] **T-B08** `tests/ScenarioSimulator.Tests/ScenarioSeederReactionTests.cs`:
  - a declared `HighlightOverlay` reaction produces one extra create-then-publish with the wrapped predicate;
  - a refused reaction is logged by name and the asset's other rules still seed;
  - a reaction whose trigger only a cue could satisfy is skipped when that clip's sidecar was refused (spec US1, malformed manifest);
  - T-A01's pins still pass unmodified.
- [ ] **T-B09** `[P]` `tests/ScenarioSimulator.Tests/ScenarioFileTests.cs` (existing, content tests):
  - every `*.cues.json` in the clips directory names a clip that exists;
  - every sidecar passes validation;
  - `DurationMs` matches the `.mp4`'s duration to ±1 ms. Read the MP4 `mvhd` box directly, with no ffprobe dependency in unit tests.

**Implementation, in order:**

- [ ] **T-B10** `Cues/ClipManifest.cs`, `Cues/ClipManifestLoader.cs`, `Cues/ClipManifestValidation.cs` (makes T-B02 green).
- [ ] **T-B11** `[P]` `Cues/CueSchedule.cs` (makes T-B01 green).
- [ ] **T-B12** `[P]` `CameraSim/CameraSimPathClient.cs`, plus its registration (makes T-B03 green).
- [ ] **T-B13** `[P]` `Mqtt/MqttSampleMapper.cs` cue overload, and a race-safe `Mqtt/MqttPublisher.StartAsync` (makes T-B05 and T-B06 green).
- [ ] **T-B14** `Scenario/ReactionDefinition.cs`, `AssetDefinition.Reactions`, `Seeding/ReactionRuleSeed.cs`, and reaction seeding in `ScenarioSeeder` (makes T-B04 and T-B08 green).
- [ ] **T-B15** `Cues/ClipCueHostedService.cs`, `Cues/ClipCueExtensions.cs`, `Program.cs` `AddClipCues()`, and the `Log.cs` entries `ClipCueEmitted`, `ClipCueSkippedLate`, `ClipAnchorChanged`, `ClipManifestRefused` and `CameraSimPathUnreadable` (makes T-B07 green).
- [ ] **T-B16** Content:
  - `src/AppHost/Resources/clips/mill-roughing.cues.json`: frame-step the clip, and annotate at least one `person` cue at ≥ 0.8 and one below 0.8. If the footage shows no person, annotate a visible object class and adjust the spec's example in the PR body.
  - The `station-4-roughing` reaction in `Scenarios/rolling-mill.json`.
  - T-B09 green.
- [ ] **T-B17** Phase 5 V-2, V-3 and V-4 (plan §8). Quote the per-loop offsets, the one-hour drift and the clock skew in the PR.

## PR-C: US2, a reaction sets a variable and a label shows it (P2)

Depends on PR-B being merged.

- [ ] **T-C01** **Characterisation.**
  - `tests/Integration.Tests/SystemVariables/VariableReadScopeIntegrationTests.cs` and `ResolveOverlayTextTests.cs`: change `ServiceAccountClientId`/`ServiceAccountSecret` to `stream-distribution-attribution` / `dev-only-stream-distribution-secret`, and update the prose naming `scenario-simulator` and its scope count. Assertions are unmodified.
  - Run both classes and `RealmImportMirrorTests` on the Aspire fixture **before** T-C07. All must be green.
  - Confirm both classes are still listed in the shard filters: `shard-1.filter` and `shard-4.filter` on 2026-09-29.
  - This is its own commit.
- [ ] **T-C02** `[P]` `tests/ScenarioSimulator.Tests/SystemVariablesClientTests.cs`:
  - a 201 means created;
  - a 409 followed by a `GET` with a matching type means reused;
  - a 409 followed by a mismatched type is a named refusal;
  - a 400 carries its problem detail;
  - the fab query is `munich`;
  - the bearer is attached;
  - the `POST` is **not** retried on a 5xx (ADR-0143).
- [ ] **T-C03** `[P]` `tests/ScenarioSimulator.Tests/ReactionRuleSeedTests.cs` (extend):
  - a `SetVariableValue` reaction maps to `actionType: "SetVariableValue"`, `variableName` and `valueExpression`, with a null overlay and duration;
  - naming a variable not declared on the asset is refused.
- [ ] **T-C04** `tests/ScenarioSimulator.Tests/ScenarioSeederVariableTests.cs`:
  - variables are seeded before the overlay and the reactions (plan §5.4);
  - a refused variable skips exactly the reactions that write it;
  - the same variable declared by two assets with the same type seeds once, and the second gets a 409 reuse.
- [ ] **T-C05** Implement `Scenario/VariableDefinition.cs`, `AssetDefinition.Variables`, `Seeding/SystemVariablesClient.cs` and its registration in `ScenarioSeedingExtensions`, `SimulatorOptions.SystemVariablesUrl`, the `Program.cs` `BindRuntime` key `services:system-variables:http:0`, `RuleSeedAction.SetVariableValue` in `AutomationRulesClient`, and seeder ordering (makes T-C02 to T-C04 green).
- [ ] **T-C06** Content:
  - `src/AppHost/Resources/clips/electronics-inspection.cues.json` (a `defect` cue carrying `Label`);
  - in `Scenarios/electronics.json`, the variable `inspection_last_defect`, a `SetVariableValue` reaction, and the label `INSPECTION: {{inspection_last_defect}}`;
  - T-B09 green.
- [ ] **T-C07** `src/AppHost/Realms/smart-sentinel-eye-realm.json`: add `sse.variables.write` and `sse.variables.read` to `scenario-simulator`'s `defaultClientScopes`. Re-run T-C01's classes, which stay green. Its own commit, after T-C01.
- [ ] **T-C08** `[P]` `src/AppHost/AppHost.cs`: the simulator adds `.WithReference(systemVariables)` and `.WaitFor(systemVariables)`. Check that `AppHostE2ESwitchTests` stays green.
- [ ] **T-C09** Phase 5 V-5 on a fresh Keycloak volume. The PR body tells developers to delete the volume.

## PR-D: US3, a scenario that cannot tell its story says so (P3, red)

Depends on PR-C being merged.

- [ ] **T-D01** `tests/ScenarioSimulator.Tests/ScenarioStoryCheckTests.cs` (pure):
  - an orphan reaction yields exactly one finding naming the scenario, asset, reaction and trigger;
  - a trigger satisfied by a sensor, or by a valid cue, yields none;
  - a trigger satisfied only by a **refused** sidecar's cue yields a finding;
  - the legacy `Highlight` is checked the same way;
  - all three shipped scenarios yield **zero** findings.
- [ ] **T-D02** `tests/ScenarioSimulator.Tests/ScenarioSeederStoryCheckTests.cs`:
  - an orphan reaction is still seeded and the warning is logged once;
  - a `Highlight` whose kind no sensor emits is seeded with source `plc` **and** reported (the `plc` fallback is no longer silent);
  - T-A01's pins pass unmodified.
- [ ] **T-D03** Implement `Scenario/ScenarioStoryCheck.cs`, call it from `ScenarioSeeder` once per scenario before seeding, add the `Log.cs` entry `ScenarioReactionUnreachable`, and replace the silent `?? "plc"` in `SeedOverlayAndRuleAsync` with the check's derivation (plan §5.5).
- [ ] **T-D04** Phase 5 V-6.

## Follow-ups (file as issues when PR-B merges; not part of this spec)

- **F-1**: annotate the remaining 11 clips (content, label `enhancement`).
- **F-2**: `AutomationRulesClient.Operator` silently maps an unknown comparison to `>=`. Refuse it instead. T-A02's pin is expected to go red, deliberately.
- **F-3**: when #2324 lands, register `ObjectDetected` for the simulator's fab. Cross-reference it on #2324.
- **F-4** (only if V-3 fails): simulator-owned playback for drifting paths (ADR-0163, first alternative).
