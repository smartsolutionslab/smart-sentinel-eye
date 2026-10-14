# Plan 364: The scenario simulator under its method-length limit

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2933.

## 1. Constitution and ADR alignment

- **Context and layers.** `src/ScenarioSimulator` is a dev-only worker (ADR-0111), not a bounded
  context: no Domain/Application/Infrastructure/Api split, no aggregates, no value objects, no
  messaging contracts touched. §II's primitive ban does not reach it (it scopes Domain models).
- **Boundary rules.** Unchanged. The simulator already reaches every context over HTTP only
  (`AutomationRulesClient.RuleSummary`, `CameraCatalogClient.CameraPage` are deliberate local
  projections, not `Shared.Contracts` types); no project reference is added.
- **Messaging.** None touched. `CameraRegisteredV1` handling (`EventHandlers/`) is out of scope.
- **ADR-0084.** Clears S138 rows 1-7, 9, 10 and S134 row 8 (spec §1). S107 excluded by the issue;
  FR-003 forbids adding to it.
- **ADR-0144.** Characterisation colour: Phase 4a writes the gap tests of §4, observes the whole
  suite green on unrefactored code, and Phase 4b may not edit any test file.
- **CLAUDE.md conventions** apply to every new helper: `var`, one parameter per line when a
  signature wraps, no leading underscore, explicit-type collection expressions, `Ensure.That` guards
  stay where they are (helpers are private and take already-guarded values, so they get none - no
  drive-by guards), no new comments beyond moved ones.

## 2. Shape

Ten hits, five files, all private extractions. Files touched in `src/`:
`CameraCatalog/CameraCatalogClient.cs`, `CameraSim/CameraSimPathClient.cs`,
`Cues/ClipManifestLoader.cs`, `Cues/ClipManifestValidation.cs`, `Seeding/AutomationRulesClient.cs`,
`Seeding/ReactionRuleSeed.cs`, `Seeding/ScenarioSeeder.cs`. Disjoint per file, so the refactor
tasks are `[P]`; only `ScenarioSeeder.cs` carries three hits and is one task.

S138 counts code lines, not comments (verified: `ReadBackAsync` spans 59 physical lines and is
reported as 36). Moving a comment does not change a count; comments move with their code (FR-005).

## 3. Production shape (binding on the engineer)

Names are binding unless the build forces otherwise; if one is changed, say so in the PR.

### 3.1 `CameraCatalogClient.ReadBackAsync` (36)

Extract one page fetch:

```csharp
private async Task<(IReadOnlyList<CameraSummary> Items, int Count)> ReadPageAsync(
    int offset,
    string token,
    CancellationToken cancellationToken)
```

Body: build the `GET /cameras?limit={PageSize}&offset={offset}` request, bearer header, send,
`EnsureSuccessStatusCode`, deserialize, return `(payload?.Items ?? [], payload?.Count ?? 0)`.
`ReadBackAsync` keeps the `try`/`catch (... when (ex is not OperationCanceledException))`, the
loop, the match, the empty-page break and `reported`/`offset` bookkeeping. Invariant: `reported` is
assigned only after a non-empty page, exactly as now.

### 3.2 `CameraSimPathClient.GetAsync` (34)

Extract the body of `using (response)`:

```csharp
private async Task<Option<PathState>> ReadPathStateAsync(
    string path,
    HttpResponseMessage response,
    CancellationToken cancellationToken)
```

404 -> None (no log); non-success -> log + None; deserialize in its own `try` with the same
`IsUnexpectedFailure` filter -> log + None; `null` body -> None; else `BuildPathState`. `GetAsync`
keeps the guard, the send `try`, and `using (response) { return await ReadPathStateAsync(...); }`
- the response is still disposed by `GetAsync`, after the read completes.

### 3.3 `ClipManifestLoader.Load` (35)

Extract everything after the existence check:

```csharp
private static ClipManifestLoadResult LoadSidecar(string sidecar, string clip)
```

Read + deserialize with both `catch` clauses (same messages), the `null`-manifest violation, then
`ClipManifestValidation.Validate`. `Load` keeps the guard, the blank-directory and missing-file
early returns, and computes `sidecar`.

### 3.4 `ClipManifestValidation.Validate` (34)

Extract the empty/density `if`/`else if` into a sibling of the existing `AddRangeViolation`/
`AddOrderViolation`:

```csharp
private static void AddCountViolation(ClipManifest manifest, List<CueViolation> violations)
```

Order in `Validate` stays: clip mismatch, duration, null-cues early return, `AddCountViolation`,
`ValidateCues`. The null-cues check must remain **before** the call (the helper may assume
`manifest.Cues` non-null).

### 3.5 `AutomationRulesClient.EnsureRuleAsync` (36)

Two extractions:

```csharp
private static CreateRuleBody CreateBody(RuleSeed seed)

private async Task PublishAsync(
    string name,
    int expectedVersion,
    string token,
    CancellationToken cancellationToken)
```

`CreateBody` deconstructs `seed` and holds the `action switch` including the
`NotSupportedException` arm. It is called **before** `GetAccessTokenAsync`, as now, so an unsupported
action still throws without fetching a token. `PublishAsync` builds the `POST
/rules/{name}/publish?fabId={FabId}` with bearer and `If-Match: ConcurrencyHeaders.ETag(...)`, sends,
`EnsureSuccessStatusCode` (a failed publish still throws - `A_publish_that_fails_is_not_swallowed`).
`EnsureRuleAsync` then needs only `name` and `action`: `var (name, _, _, _, action) = seed;`. The
logging `switch` stays inline.

### 3.6 `ReactionRuleSeed.From` (32)

Extract the three blank-trigger checks, in order Source, Kind, Predicate, same messages:

```csharp
private static Option<ReactionSeedResult> RefuseBlankTrigger(ReactionTrigger when)
```

`From`: guards, name build, name check (stays **first** - spec J3), then
`var blankTrigger = RefuseBlankTrigger(reaction.When); if (blankTrigger.HasValue) return blankTrigger.Value;`,
then predicate and the action `switch`. `Option<T>` rather than a nullable return per ADR-0141's
direction (advisory) and the existing `Option<RuleSummary>` precedent in `AutomationRulesClient`.

### 3.7 `ScenarioSeeder` (rows 7, 8, 9, 10)

**`ExecuteAsync` (55) + S134.** Extract:

```csharp
private async Task<int> SeedScenarioAsync(
    string key,
    ScenarioDefinition scenario,
    string clipsDirectory,
    CancellationToken cancellationToken)   // returns this scenario's failure count

private async Task SeedAssetAsync(
    string key,
    AssetDefinition asset,
    IReadOnlyDictionary<string, ClipManifestLoadResult> manifestsByClip,
    CancellationToken cancellationToken)   // the old per-asset try body

private async Task<int> SeedWallsAsync(CancellationToken cancellationToken)   // 0 or 1
```

- `ExecuteAsync`: options, `failed`, `foreach key` { not-found -> `ScenarioNotFound` + continue;
  `failed += await SeedScenarioAsync(...)` }, `failed += await SeedWallsAsync(...)`, the
  `SeedingIncomplete` summary.
- `SeedScenarioAsync`: `SeedingScenario` log; manifest `try` (load + `ReportUnreachableTriggers`) -
  on failure `ScenarioManifestLoadFailed` and `return 1` **without** the `ScenarioSeeded` log (as
  now); per-asset `try { await SeedAssetAsync(...) } catch (... when not OCE) { failed++;
  AssetSeedFailed }`; `ScenarioSeeded`; return the count.
- `SeedAssetAsync`: `SeedOverlayAndRuleAsync`, `RegisterCameraAsync`, `if (camera.HasValue)
  RecordCamera` - the S134 `if` is now at depth 1.
- The two long block comments (StopHost reasoning, read-back null) move with their code.

**`SeedOverlayAndRuleAsync` (38).** Two extractions:

```csharp
private async Task<Guid> SeedOverlayAsync(
    string scenario,
    AssetDefinition asset,
    CancellationToken cancellationToken)   // label build, EnsureOverlayAsync, RecordOverlay

private async Task SeedHighlightRuleAsync(
    HighlightSeedResult seedResult,
    string assetKey,
    CancellationToken cancellationToken)   // the Refused/Valid switch incl. server refusal log
```

The caller keeps `SeedVariablesAsync`, the overlay/tile null early return (into
`SeedReactionsAsync(..., overlay: null, ...)`), the `asset.Highlight is null` ->
`AssetMissingField` branch, and in the `else` computes `cues` and calls
`SeedHighlightRuleAsync(HighlightRuleSeed.From(scenario, asset, overlay, cues), asset.Key, ...)`.
A five-parameter highlight helper is **not** an option (FR-003).

**`SeedReactionsAsync` (39).** Signature unchanged (S107 out of scope). Two extractions:

```csharp
private static bool IsUnreachable(
    AssetDefinition asset,
    ReactionTrigger when,
    ClipManifestLoadResult clip)   // !sensor && !cue && clip.Violations.Count > 0

private async Task EnsureReactionRuleAsync(
    string assetKey,
    string reactionName,
    RuleSeed seed,
    CancellationToken cancellationToken)   // EnsureRuleAsync + ReactionRefused on Refused
```

The loop keeps the `ReactionSkippedRefusedManifest` log (with `reaction.When.Source`/`Kind` - the
same values the old `source`/`kind` locals held), the `ReactionRuleSeed.From` call, and the
three-arm `switch` whose `Valid` arm becomes one `await EnsureReactionRuleAsync(...)`. The order of
the `switch` arms (the `when`-guarded `SetVariableValue` arm first) is unchanged.

**If the build still reports any of these over 30** after the prescribed extractions, the
engineer extracts further within the same constraints (private, <= 4 parameters) and states it in
the PR; it does not widen a helper past four parameters to get there.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

Coverage map on `0296275e` (existing tests, verbatim names) and the gaps to fill first. All new
tests go in `tests/ScenarioSimulator.Tests` - existing files where a fixture fits, otherwise a new
`*CharacterisationTests.cs`. No shard-filter entry is needed (that mechanism is
`Integration.Tests` only). Reuse `Fakes/RecordingLogger.cs` for log assertions and each file's
existing stub handlers; no new fake infrastructure unless no existing one fits.

| Method | Covered by (existing) | Gaps -> tests to write first |
|---|---|---|
| `ReadBackAsync` | `CameraCatalogClientTests`: `A_camera_past_the_first_page_is_still_found`, `The_walk_continues_past_the_page_the_catalog_will_serve`, `No_request_asks_for_more_than_the_catalog_will_serve`, `A_camera_on_the_first_page_costs_one_request`, `A_camera_that_is_genuinely_absent_is_reported_absent`, `A_count_larger_than_the_rows_ends_the_walk_rather_than_looping` | C1 `A_read_back_the_catalog_refuses_degrades_to_an_unknown_identifier_rather_than_throwing` (409 then 403 on the list -> `null`, no throw); C2 `Every_read_back_request_carries_the_bearer_token` |
| `GetAsync` | `CameraSimPathClientTests`: `A_ready_path_parses_to_Ready_and_its_readyTime`, `A_path_with_no_reader_maps_to_not_ready`, `A_404_for_an_unprovisioned_path_maps_to_None`, `A_server_error_is_logged_and_returns_None_rather_than_throwing`, `A_malformed_readyTime_string_returns_None_rather_than_throwing` | C3 `An_unreachable_camera_sim_returns_None_rather_than_throwing` (handler throws `HttpRequestException`); C4 `An_unparseable_body_returns_None_rather_than_throwing`; C5 `A_json_null_body_returns_None`; C6 `An_http_client_timeout_not_requested_by_the_caller_returns_None` (`TaskCanceledException`, token not cancelled); C7 `A_cancellation_the_caller_requested_propagates` (token cancelled -> throws `OperationCanceledException`) |
| `Load` | `ClipManifestValidationTests`: `A_missing_sidecar_gives_no_manifest_and_no_violations`, `Unparseable_JSON_is_one_violation_and_yields_no_manifest`, `A_valid_sidecar_loads_to_Some_with_no_violations`, `An_invalid_but_parseable_sidecar_surfaces_Validates_violations_and_yields_no_manifest`, `A_sidecar_with_no_Cues_property_at_all_is_refused_rather_than_throwing`, `An_unreadable_sidecar_is_one_violation_rather_than_a_thrown_exception` | C8 `A_blank_clips_directory_gives_no_manifest_and_no_violations`; C9 `A_sidecar_containing_json_null_is_one_did_not_deserialize_violation` (exact message) |
| `Validate` | `ClipManifestValidationTests` (14 rule tests) + `ClipManifestValidationOrderingTests` (18, exact lists and order, incl. `A_clip_mismatch_with_no_cues_reports_the_mismatch_then_the_empty_cue_list`, `More_cues_than_the_density_limit_report_one_density_violation`, `A_non_positive_duration_skips_the_range_and_density_checks`) | **None.** Ordering and every branch of the extracted block are pinned. |
| `EnsureRuleAsync` | `AutomationRulesClientTests`: `A_rule_a_previous_run_left_in_Draft_is_published`, `The_stranded_rules_own_version_is_echoed_back_as_the_precondition`, `An_already_published_rule_is_left_alone`, `An_archived_rule_is_not_resurrected`, `A_freshly_created_rule_is_published_at_version_zero`, `A_publish_that_fails_is_not_swallowed`, `A_400_on_create_is_refused_without_throwing`; wire bodies via `LegacyHighlightRuleBodyTests.Every_asset_in_the_three_shipped_scenarios_seeds_its_legacy_highlight_rule_with_the_exact_wire_body` and `ScenarioSeederVariableTests.A_successfully_seeded_variable_lets_its_SetVariableValue_reaction_create_the_rule_with_the_expected_wire_body` | C10 `A_published_highlight_rule_logs_the_overlay_and_a_published_variable_rule_logs_the_variable` (RecordingLogger; the moved-adjacent log switch); C11 `The_publish_request_carries_the_bearer_token_and_the_If_Match_precondition` - only if no existing assertion already checks the bearer on publish (test-writer checks `The_stranded_rules_own_version_...` first and skips C11 if it does) |
| `ReactionRuleSeed.From` | `ReactionRuleSeedTests`: `The_predicate_is_wrapped_in_the_devices_own_guard_with_the_declared_predicate_parenthesised`, `A_declared_OR_stays_inside_the_device_guards_parentheses`, `The_rule_is_named_scenario_asset_reaction`, `A_resulting_rule_name_longer_than_63_characters_is_refused`, `A_reaction_name_containing_characters_outside_the_rule_name_grammar_is_refused`, `A_HighlightOverlay_reaction_on_an_asset_with_no_seeded_overlay_is_refused`, `An_unrecognised_action_type_is_refused`, `A_SetVariableValue_reaction_maps_to_the_declared_variable_and_its_value_expression`, `Naming_a_variable_not_declared_on_the_asset_is_refused` | C12 `A_blank_trigger_source_is_refused`; C13 `A_blank_trigger_kind_is_refused`; C14 `A_blank_trigger_predicate_is_refused` (exact reasons; may be one `[Theory]`); C15 `A_blank_source_and_kind_report_the_source` (order); C16 `An_invalid_name_is_refused_before_a_blank_trigger` (pins J3) |
| `ExecuteAsync` | `ScenarioSeederResilienceTests`: `An_asset_that_throws_does_not_take_the_worker_down`, `Every_failing_asset_is_named_rather_than_silently_skipped` | C17 `An_active_key_missing_from_configuration_is_logged_and_the_next_scenario_still_seeds`; C18 `A_scenario_whose_manifests_cannot_load_is_counted_once_logs_no_seeded_line_and_the_next_scenario_still_seeds` (provoke with an asset whose `Camera.Clip` is null, which throws inside `LoadManifestsByClip`); C19 `A_registered_cameras_identifier_is_recorded_for_its_wall_tile` and `A_camera_whose_identifier_is_unknown_is_not_recorded` (via `AssetCorrelationTable.CompleteTiles`/`IsWallComplete`); C20 `The_incomplete_summary_counts_asset_and_wall_failures_together` - if no existing seam makes `WallSeeder.TryCreateAsync` throw without a production change, the test-writer records that in the PR and C20 becomes asset-count only |
| `SeedOverlayAndRuleAsync` | `ScenarioSeederVariableTests.Variables_are_seeded_before_the_overlay_and_the_reactions`, `ScenarioSeederVariableFailureTests.A_variables_IO_failure_does_not_lose_the_assets_overlay_highlight_rule_or_camera_registration`, `LegacyHighlightRuleBodyTests` (3), `ScenarioSeederStoryCheckTests.A_highlight_whose_kind_*` (2), `ScenarioSeederReactionTests.A_declared_HighlightOverlay_reaction_produces_one_extra_create_then_publish_with_the_wrapped_predicate` | C21 `An_asset_without_an_overlay_still_seeds_its_SetVariableValue_reaction_and_refuses_its_HighlightOverlay_one`; C22 `An_asset_with_an_overlay_but_no_highlight_logs_the_missing_field_and_still_seeds_its_reactions`; C23 `A_highlight_rule_the_server_refuses_is_logged_and_the_reactions_still_seed` (400 on the highlight create); C24 `The_overlay_is_recorded_with_its_scenario_and_tile` |
| `SeedReactionsAsync` | `ScenarioSeederReactionTests` (3), `ScenarioSeederRuleRefusalTests.A_400_on_one_reactions_rule_create_does_not_drop_the_assets_other_reaction_or_its_camera_registration`, `ScenarioSeederVariableTests.A_refused_variable_skips_exactly_the_reactions_that_write_it`, `ScenarioSeederStoryCheckTests.A_refused_manifest_reaction_logs_both_the_existing_and_the_new_warning` | C25 `A_reaction_a_sensor_satisfies_is_seeded_even_when_the_clips_sidecar_is_refused`; C26 `A_reaction_a_valid_cue_satisfies_is_seeded` - test-writer first checks `ScenarioSeederStoryCheckTests` for an equivalent and skips if pinned |

Each test must be able to fail (memory: "an assertion must not check its own input"): assert the
production output (requests, return, log entries), never a value the test itself supplied
unchanged. The test-writer runs the full project and returns the verbatim summary
(`Passed! - Failed: 0 ...`) for the PR body.

## 5. Measurement and run commands (before and after, quoted in the PR)

```sh
dotnet build src/ScenarioSimulator/SmartSentinelEye.ScenarioSimulator.csproj -c Release --no-incremental > build.log 2>&1
grep -E 'warning S(138|134|107)' build.log | grep -E 'src.ScenarioSimulator.[A-Z]' | awk -F'src' '{print $2}' | cut -d'[' -f1 | sort -u
dotnet test tests/ScenarioSimulator.Tests -c Release
```

Before (`0296275e`): 11 S138/S134 rows (spec §1) + 15 S107. After: exactly `Program.cs(125)` S138
and the same 15 S107 rows (line numbers inside `ScenarioSeeder.cs` may shift; the member must not).
Stop any running AppHost before building (it holds the binaries).

## 6. Analyzer-forced and convention-forced shapes (declare in the PR)

- `var (name, _, _, _, action) = seed;` in `EnsureRuleAsync` (discards per CLAUDE.md).
- Tuple return in §3.1 rather than a new record (FR-001: no new type).
- `Option<ReactionSeedResult>` in §3.6.

## 7. Security

Bearer headers and `If-Match` move into `ReadPageAsync`/`PublishAsync` verbatim (C2, C11 pin them).
No scope, token source, URL or logging of secrets changes. Dev-only binary. No security-reviewer
pass needed beyond phase-6 code review; flag if a reviewer disagrees.

## 8. Risks

| Risk | Mitigation |
|---|---|
| `ReadPageAsync` changes `reported` semantics when `payload` is null or `Items` is null | Tuple keeps `?? []`/`?? 0` per field; C1 + existing count tests |
| `using (response)` disposes before the extracted read awaits | `return await` inside the `using`; C4/C5 exercise the body path |
| `CreateBody` moved after `GetAccessTokenAsync` | §3.5 binds the order; review check |
| Name check reordered behind trigger checks | C16 pins it |
| Seeder `failed` accounting drifts (manifest failure counted twice, or `ScenarioSeeded` logged after it) | C18, C20 and existing resilience tests |
| Line counts are estimates; a helper lands at 31 | §3 fallback rule; the build, not a grep, decides (§5) |
| New S107 from a five-parameter helper | FR-003; §5 diff of the S107 set |
| Characterisation for a branch with no seam (wall throw) | C20 fallback recorded, not forced with a production change |
