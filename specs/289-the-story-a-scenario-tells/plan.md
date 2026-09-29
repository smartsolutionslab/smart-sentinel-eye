# Implementation Plan: The story a scenario tells

**Spec**: [`spec.md`](spec.md) · **Tasks**: [`tasks.md`](tasks.md) · **ADR**: ADR-0163 (amends ADR-0111)

**Branch**: `289-the-story-a-scenario-tells` (rebased onto `origin/develop` @ `af79b5ac`, 2026-09-29)

**Created**: 2026-09-29 · **Status**: Phase 2 draft, for review

---

## 1. Summary

The dev-only Scenario Simulator gains three capabilities. All of them live in `src/ScenarioSimulator`
and none touches a bounded context:

1. **Clip cues.** A `<clip>.cues.json` sidecar declares typed detections at playback offsets. A new
   hosted service publishes each detection to MQTT as an `inference` / `ObjectDetected` event when
   camera-sim's playback of that clip reaches the offset. The anchor is the path's `readyTime`.
2. **Declared reactions and variables.** The scenario file lists `Reactions[]` and `Variables[]`
   per asset. The simulator seeds them through `AutomationRulesClient` and a new
   `SystemVariablesClient`, using the create-then-publish pattern the seeder already follows. The
   legacy `Highlight` stays, byte-identical on the wire.
3. **A story check.** At load, the simulator reports any reaction that nothing on its asset can
   trigger. This replaces the silent `plc` fallback.

Settled decisions (spec §4): Q1-A (`readyTime` anchor, ±500 ms), Q2-A (sidecar), Q3-a (sensors
unchanged), Q4-b (seed, no reconciliation), Q5-a (no box rendering).

## 2. Technical context: what Phase 2 measured

| Fact | Evidence | Consequence for the plan |
|---|---|---|
| `GET /v3/paths/get/{name}` on the pinned `bluenviron/mediamtx:1.21.0-ffmpeg` returns `ready`, `readyTime` (RFC 3339, ns precision), `available`, `online`, `source`, `readers` | Queried the running dev `camera-sim` on 2026-09-29 (`/v3/paths/list`, 14 paths) | Q1-A works as designed. The path name is the asset's `Camera.Path`. |
| All clips are exactly `20.000000` s | `ffprobe` inside camera-sim on `mill-roughing`, `electronics-inspection` and `paper-packaging` | The sidecar still declares `DurationMs`. A content test compares it with the file (T-B09), so a re-cut clip fails loudly. |
| The camera-sim container clock and the host clock agree to the second | `date -u` on both, same second | This is second-level evidence only. Phase 5 measures skew at ms level (V-4). |
| AEL: `+` is numeric only. There is no string concatenation, no arrays and no functions | `AelInterpreter.EvalArithmetic` | A cue carries display text (`Label`). A `SetVariableValue` expression can only select a field. |
| `RuleName`: 2-63 characters, first `[a-z]`, then `[a-z0-9-]` | `Automation/Domain/Rule/RuleName.cs` | `{scenario}-{asset}-{reaction}` is validated at load (T-B04). |
| `AutomationRulesClient.EnsureRuleAsync` takes 9 parameters and builds the predicate itself. No test pins the create body | `Seeding/AutomationRulesClient.cs`; `tests/ScenarioSimulator.Tests/AutomationRulesClientTests.cs` (6 facts, none on the body) | PR-A pins the body **before** refactoring. |
| `MqttPublisher.StartAsync` guards with a plain `loop is not null` check | `Mqtt/MqttPublisher.cs:118-146` | A second concurrent caller (the cue service) could start two connect loops. T-B06 makes it race-safe. |
| `scenario-simulator` is the principal **without** `sse.variables.read` in `VariableReadScopeIntegrationTests` (T14) and `ResolveOverlayTextTests` (T13, the 403-unscoped fact) | Those files, and `RealmImportMirrorTests.IntegrationTestCopies.cs:43-44` | FR-012 would break them. They move to `stream-distribution-attribution`, whose defaults are identity, audience, groups and `sse.cameras.read`, in `/fabs/munich` and `/fabs/dresden`. The mirror test reads the class's own consts, so it follows automatically. |
| `service-account-scenario-simulator` is in `/fabs/munich` | realm `:584-588` | `POST /system-variables?fabId=munich` passes the fab guard. |
| AppHost wires the simulator to camera-catalog, camera-sim, overlay-designer, automation and layout-composition, **not** system-variables | `src/AppHost/AppHost.cs:821-842` | PR-C adds `WithReference(systemVariables)` and `WaitFor`. |
| The seeder is create-once: on 409 it publishes a stuck Draft and never updates a definition | `OverlayDesignerClient`, `AutomationRulesClient` | This is kept (Q4-b), and the documentation says so. |

**Bounded contexts touched: none.** The simulator references only `Shared.Contracts` and
`ServiceDefaults`, and that does not change. It talks to the APIs over HTTP with its own private
DTO records, as it does today. Constitution §II does not bind the simulator's configuration
classes: it is not a Domain project, and `PrimitiveBoundaryTests` scopes to `src/*/Domain`. The
new configuration classes mirror `ScenarioOptions`' existing mutable-POCO style, because
`IOptions` binding needs it.

**§IV latency budget: N/A.** The simulator is upstream of "event arrival". Detection events use
the existing `Event → overlay state` leg unchanged. The ±500 ms figure is demo fidelity and is
never cited as a §IV measurement.

## 3. Data model

### 3.1 Clip sidecar (`src/AppHost/Resources/clips/<clip basename>.cues.json`)

```jsonc
{
  "Clip": "mill-roughing.mp4",          // must equal the sidecar's own basename + ".mp4"
  "DurationMs": 20000,                   // must equal the file's duration (content test, ±1 ms)
  "Cues": [
    { "AtMs": 12000,                     // 0 ≤ AtMs < DurationMs, strictly ascending
      "Kind": "ObjectDetected",          // default; ≤ 128 characters (TriggerKind.MaximumLength)
      "Source": "inference",             // default; plc | inference (the ACL grants the simulator only these two)
      "Class": "person",                 // required, non-blank
      "Confidence": 0.92,                // required, 0..1
      "Label": "PERSON IN EXCLUSION ZONE", // optional display text (AEL cannot build strings)
      "Zone": "exclusion",               // optional
      "Box": { "X": 0.61, "Y": 0.40, "Width": 0.08, "Height": 0.31 }, // optional, each 0..1, X+Width ≤ 1, Y+Height ≤ 1
      "TrackIdentifier": "p-1" }         // optional
  ]
}
```

Density limit (FR-003): at most `ceil(DurationMs / 1000) × 10` cues per clip.

C# (namespace `SmartSentinelEye.ScenarioSimulator.Cues`): `ClipManifest { Clip, DurationMs, Cues }`,
`CueDefinition { AtMs, Kind, Source, Class, Confidence, Label?, Zone?, Box?, TrackIdentifier? }`, and
`CueBox { X, Y, Width, Height }`. These are deserialised with `System.Text.Json`, not bound through
`IConfiguration`: the sidecars are not configuration, and case-insensitive property matching
gives the same authoring shape as the scenario files.

### 3.2 Emitted MQTT event (FR-002)

Topic `fab/munich/{Source}/{asset.Camera.Path}`. Body (camelCase, mirroring `MqttSampleMapper`):

```json
{ "eventId": "<guid v7>", "kind": "ObjectDetected", "occurredAt": "<now>",
  "payload": { "class": "person", "confidence": 0.92, "station": "station-4-roughing",
               "label": "PERSON IN EXCLUSION ZONE", "zone": "exclusion",
               "box": { "x": 0.61, "y": 0.40, "width": 0.08, "height": 0.31 },
               "trackIdentifier": "p-1", "clipOffsetMs": 12000 } }
```

Absent optional fields are **omitted**, not `null`, so an AEL predicate on a missing field fails
evaluation, which is logged and skipped per rule (`RuleEvaluator`), instead of comparing against
null. `clipOffsetMs` is a verification aid. It lets Phase 5 join an ingested event to its cue
without a stopwatch.

### 3.3 Scenario-file additions (per `AssetDefinition`)

```jsonc
"Variables": [ { "Name": "roughing_zone_state", "Type": "String",       // String | Number | Boolean
                 "TruthyLabel": null, "FalsyLabel": null } ],             // both required iff Boolean
"Reactions": [
  { "Name": "person-in-exclusion",                                        // → rule "{scenario}-{asset}-{name}"
    "When": { "Source": "inference", "Kind": "ObjectDetected",
              "Predicate": "$.payload.class == 'person' && $.payload.confidence >= 0.8" },
    "Then": { "Type": "HighlightOverlay", "DurationMs": 4000 } },          // on this asset's seeded overlay
  { "Name": "zone-state",
    "When": { "Source": "inference", "Kind": "ObjectDetected", "Predicate": "$.payload.class == 'person'" },
    "Then": { "Type": "SetVariableValue", "Variable": "roughing_zone_state", "Value": "$.payload.label" } }
]
```

C# (in a new `Scenario/ReactionDefinition.cs`, which keeps `ScenarioOptions.cs` under 300 LOC):
`ReactionDefinition { Name, When: ReactionTrigger, Then: ReactionAction }`,
`ReactionTrigger { Source, Kind, Predicate }`,
`ReactionAction { Type, DurationMs?, Variable?, Value? }`, and
`VariableDefinition { Name, Type, TruthyLabel?, FalsyLabel? }` (in `Scenario/VariableDefinition.cs`).
`AssetDefinition` gains `List<ReactionDefinition> Reactions = []` and
`List<VariableDefinition> Variables = []`. Both default to empty and are **replaced, never
appended**, by the binder, because no default contents exist (see the `Active` note in
`ScenarioOptions` for why that matters).

Variables are declared per asset for locality. Variable names are fab-global in SystemVariables,
so two assets declaring the same name with the same type is fine (the second seed gets a 409 and
a matching type). With different types it is a load-time refusal (T-C05).

### 3.4 Internal seeding model (PR-A)

```csharp
// Seeding/RuleSeed.cs
internal sealed record RuleSeed(string Name, string TriggerSource, string TriggerKind, string Predicate, RuleSeedAction Action);
internal abstract record RuleSeedAction
{
    internal sealed record HighlightOverlay(Guid Overlay, int DurationMs) : RuleSeedAction;
    internal sealed record SetVariableValue(string VariableName, string ValueExpression) : RuleSeedAction; // PR-C
}
```

`AutomationRulesClient.EnsureRuleAsync(RuleSeed seed, CancellationToken)` maps it onto the
existing private `CreateRuleBody` and fixes the 9-parameter signature (ADR-0084, advisory).
`HighlightRuleSeed.From(scenario, asset, overlay)` reproduces **exactly** today's name,
`triggerSource` derivation, predicate string (no parentheses, `threshold.ToString(InvariantCulture)`,
and the same `Operator` switch **including its silent `>=` default**), and body. That default is a
defect, but fixing it is behaviour change, and it gets its own issue (§9, follow-up F-2), not a
ride-along in a characterisation PR.

`ReactionRuleSeed.From(scenario, asset, reaction, overlay?)` produces
`$.device == '{asset.Camera.Path}' && ({reaction.When.Predicate})`.

## 4. Components and file ownership

| File (under `src/ScenarioSimulator/` unless noted) | New/Changed | PR |
|---|---|---|
| `Seeding/RuleSeed.cs`, `Seeding/HighlightRuleSeed.cs` | new | A |
| `Seeding/AutomationRulesClient.cs` | changed (signature to `RuleSeed`; body unchanged) | A, C (SetVariableValue mapping) |
| `Seeding/ScenarioSeeder.cs` | changed | A (use `HighlightRuleSeed`), B (reactions), C (variables), D (story check, no `plc` fallback) |
| `Scenario/ReactionDefinition.cs`, `Scenario/VariableDefinition.cs` | new | B (reactions), C (variables) |
| `Scenario/ScenarioOptions.cs` | changed (two list properties on `AssetDefinition`) | B, C |
| `Seeding/ReactionRuleSeed.cs` | new | B |
| `Cues/ClipManifest.cs`, `Cues/ClipManifestLoader.cs`, `Cues/ClipManifestValidation.cs` | new | B |
| `Cues/CueSchedule.cs` (pure) | new | B |
| `Cues/ClipCueHostedService.cs`, `Cues/ClipCueExtensions.cs` | new | B |
| `CameraSim/CameraSimPathClient.cs` (GET `/v3/paths/get/{path}`) | new | B |
| `Mqtt/MqttSampleMapper.cs` | changed (adds `Map(asset, cue, offset)`) | B |
| `Mqtt/MqttPublisher.cs` | changed (race-safe `StartAsync`) | B |
| `Log.cs` | changed (new `[LoggerMessage]` entries) | B, C, D (**contention file**, which is why the PRs are sequential) |
| `Program.cs` | changed (`AddClipCues()`, then `SystemVariablesUrl`) | B, C |
| `Seeding/SystemVariablesClient.cs`, `Seeding/ScenarioSeedingExtensions.cs` | new / changed | C |
| `Configuration/SimulatorOptions.cs` | changed (`SystemVariablesUrl`) | C |
| `Scenario/ScenarioStoryCheck.cs` (pure) | new | D |
| `Scenarios/rolling-mill.json` | changed (one reaction on `station-4-roughing`) | B |
| `Scenarios/electronics.json` | changed (variable, reaction and placeholder label on `electronics-inspection`) | C |
| `src/AppHost/Resources/clips/mill-roughing.cues.json` | new (content) | B |
| `src/AppHost/Resources/clips/electronics-inspection.cues.json` | new (content) | C |
| `src/AppHost/AppHost.cs` | changed (simulator → system-variables reference + wait) | C (**contention file**, ADR-0109) |
| `src/AppHost/Realms/smart-sentinel-eye-realm.json` | changed (two scopes on `scenario-simulator`) | C (**contention file**) |
| `tests/Integration.Tests/SystemVariables/{VariableReadScopeIntegrationTests,ResolveOverlayTextTests}.cs` | changed (principal consts and prose) | C |
| `tests/ScenarioSimulator.Tests/*` | new test classes | A-D |

No `tests/Integration.Tests` class is **added**, so no `ci-shards/shard-N.filter` entry is needed.
The two changed classes are already listed (memory: *new test classes need a shard-filter entry*).
PR-C's task T-C01 re-checks this.

## 5. Behaviour

### 5.1 Cue scheduling (`CueSchedule`, pure, no I/O)

```
Next(anchor: DateTimeOffset, now: DateTimeOffset, manifest, lastEmitted: (anchor, loop, index)?)
  elapsed = now − anchor;  if elapsed < 0 → wait |elapsed| (clock skew; logged once per anchor)
  loop    = floor(elapsed / D);  offset = elapsed mod D
  candidate = first cue i with AtMs ≥ offset in this loop, not ≤ lastEmitted for (anchor, loop)
              else cue 0 of loop + 1
  → (cue, loop, index, dueAt = anchor + loop·D + AtMs)
```

A cue whose `dueAt` is more than **250 ms** in the past (the service woke late, or the anchor
moved) is **skipped, not emitted late**. A late detection would show the wall reacting to
something the picture has already passed, which is exactly what the feature exists to prevent.
Skips are counted and logged per loop.

### 5.2 `ClipCueHostedService`

- At start, it resolves every **active** scenario's assets whose clip has a valid manifest
  (FR-006). It builds a `path → (asset, manifest)` map. Two assets on one path are impossible,
  since the path is unique per camera.
- It runs one `Task` per path, all under the service's stopping token. Each loop:
  1. `CameraSimPathClient.GetAsync(path)` → `Option<PathState(Ready, ReadyTime)>`. If the result
     is none or not ready, wait 1 s (FR-005).
  2. If `ReadyTime` differs from the last anchor, it is a restart. Reset `lastEmitted` and log
     `ClipAnchorChanged(path, old, new)`.
  3. `CueSchedule.Next(...)`. Wait `min(dueAt − now, 1 s)`. If the wait was clipped, go back to 1.
     This re-reads the anchor at least once a second, so a restart is noticed before the next cue.
  4. At `dueAt`, map and publish, record `lastEmitted`, and log
     `ClipCueEmitted(path, loop, index, offset, lateness)`.
- Failures: an HTTP failure from camera-sim is logged, and the path waits 1 s and retries. It is
  scoped per path, never letting an exception escape the `BackgroundService`, which is the same
  reasoning as `ScenarioSeeder`'s per-asset catch.
- `MqttPublisher.StartAsync` is called by both hosted services. PR-B makes it race-safe with
  `Interlocked.CompareExchange` on a started flag.

### 5.3 Manifest loading and validation (FR-001, FR-003)

`ClipManifestLoader.Load(clipsDirectory, clip)` returns `Option<ClipManifest>`. None means no
sidecar, which is normal. For a sidecar that exists, `ClipManifestValidation.Validate(manifest,
clip)` returns a list of named violations:

- `Clip` does not match the sidecar name.
- `DurationMs ≤ 0`.
- Any `AtMs` is out of range or not strictly ascending.
- The density limit is exceeded.
- `Class` is blank.
- `Confidence` is outside 0..1.
- `Box` is out of range.
- `Source` is not `plc`/`inference`.
- `Kind` is blank or longer than 128.

Any violation refuses the **whole** sidecar for that clip. The simulator logs each violation with
the scenario, asset, clip and cue index, emits no cues for that clip, and skips any reaction on
that asset whose trigger **only** a cue could satisfy (spec US1, "malformed manifest"). An
unreadable or unparseable JSON file is one violation. `ClipsDirectory` empty means no sidecars are
read, the same self-disabling rule as `ClipLibrary`.

### 5.4 Seeding order per asset (after PR-C)

1. Variables (PR-C).
2. Overlay (existing).
3. Legacy `Highlight` rule (existing, via `HighlightRuleSeed`).
4. `Reactions[]` in file order.
5. Camera (existing).

A reaction whose load-time validation failed is skipped with a warning, and the others continue.
Validation covers: rule name, a `HighlightOverlay` on an asset with no `Overlay`, a
`SetVariableValue` naming a variable not declared on that asset or whose seed was refused, and an
unknown `Then.Type`. The per-asset `try/catch` in `ScenarioSeeder` stays as the backstop for I/O
failures.

`SystemVariablesClient.EnsureVariableAsync(definition)`: `POST /system-variables?fabId=munich`
with body `{ Name, Type, InitialValue: null, TruthyLabel, FalsyLabel }`.
- On **201**: done.
- On **409**: `GET /system-variables/{name}?fabId=munich`. A matching `Type` means reuse. A
  mismatch returns a refusal, and the caller skips dependent reactions (US2 conflict scenario).
- On **400**: return the problem detail as a refusal.
- Any other status: `EnsureSuccessStatusCode` (caught by the per-asset backstop).
- No `RetryEveryMethod` (ADR-0143: `POST` is not idempotent here, and the 409 path already
  handles "it exists"). No `Idempotency-Key`, consistent with the other seeding clients.

### 5.5 Story check (`ScenarioStoryCheck`, pure, PR-D)

For each asset: the emitted set is `{(sensor.Source, sensor.Kind)}` ∪
`{(cue.Source, cue.Kind) for its clip's valid manifest}`. Every reaction, and the legacy
`Highlight`, whose trigger is not in that set produces one `ScenarioReactionUnreachable` warning
naming the scenario, asset, reaction and trigger. The reaction **is still seeded** (spec US3).

The legacy `Highlight`'s `triggerSource` is derived from the emitted set, sensors first and then
cues. When nothing matches, the `plc` fallback is **removed**: the highlight is still seeded with
source `plc` (the value it would have got), and the check now reports it. For all three shipped
scenarios every highlight matches a sensor, so their seeded rules are unchanged, and SC-003's
characterisation still holds in PR-D.

## 6. Realm and test-principal change (PR-C)

The order within PR-C, each step its own commit that builds and passes on its own (ADR-0087):

1. `VariableReadScopeIntegrationTests` and `ResolveOverlayTextTests` switch
   `ServiceAccountClientId`/`ServiceAccountSecret` to `stream-distribution-attribution` /
   `dev-only-stream-distribution-secret`. They update the prose that names `scenario-simulator`
   and its "eleven default scopes". This is behaviour-preserving (characterisation). The
   `The_refused_service_account_holds_a_fab_but_not_the_read_scope` fact guards the new
   principal's shape, and it passes unmodified in its assertions.
2. The realm grants `scenario-simulator` `sse.variables.write` and `sse.variables.read`.
   Developers must delete the Keycloak volume (memory: *realm edits need the volume deleted*).
   The PR body says so.

## 7. Slices, order and phase-4a colour

| PR | Story | Colour | Depends on | Independently observable as |
|---|---|---|---|---|
| **A** | (foundational) rule-seed refactor | **Characterisation, observed green.** New tests pin all 12 legacy create bodies byte-for-byte on `develop` before the refactor and pass unmodified after it | — | Identical rules on a fresh stack. `GET /rules?fabId=munich` diffed before and after |
| **B** | US1: cues, clip clock, detections, `Reactions[]` with `HighlightOverlay` | **Red** (new behaviour) | A | The roughing tile flashes when the person appears, ±500 ms, across a restart |
| **C** | US2: `Variables[]`, `SetVariableValue`, realm, AppHost | Step 6.1 is **characterisation**. Everything else is **red** | B (reaction format, `Log.cs`) | The inspection label text changes at the defect cue |
| **D** | US3: story check, removal of the `plc` fallback | **Red** (the check). SC-003's pins stay green, unmodified | C (`ScenarioSeeder.cs`, `Log.cs`) | One named warning for a planted orphan reaction |

**Why strictly sequential:** A, B, C and D all edit `ScenarioSeeder.cs` and `Log.cs`. Parallel
branches would conflict on every rebase (ADR-0109). Parallelism is available **within** a slice,
for example B's `CueSchedule`, manifest validation and `CameraSimPathClient` tests and code, which
are disjoint files. tasks.md marks it with `[P]`.

**Stacking:** none. Each PR is cut from `develop` after its predecessor merges. The dependency is
on merged code, and nothing needs to be reviewed together.

## 8. Verification (Phase 5, per PR)

- **V-1 (A):** on a fresh stack (delete the automation DB volume or the rules), dump
  `GET /rules?fabId=munich` for the 12 seeded rules on `develop` and on the branch. The dumps must
  be equal apart from identifiers and timestamps.
- **V-2 (B):**
  1. Screen-record the rolling-mill wall for 10 loops. Log per-loop offsets between the annotated
     frame (located by frame-stepping the clip) and the highlight onset.
  2. Close the wall for 15 s, reopen it, and repeat for 1 loop (SC-001, SC-002). Correlate using
     `ClipCueEmitted` logs and `clipOffsetMs` in the ingested events.
  3. Also record the ≥ 0.8 rule not firing on the 0.55 cue.
- **V-3 (B):** a **one-hour drift check**. At t=0 and t=60 min, compare `ClipCueEmitted` with the
  visible frame. If drift exceeds 250 ms per hour, open a follow-up issue for the
  simulator-owned-playback fallback (ADR-0163 alternative 1) and record it in the PR. Do not
  widen the tolerance.
- **V-4 (B):** ms-level clock skew between the host and the Docker VM. Take a `readyTime` read
  immediately after a path add and compare it with the simulator-side `TimeProvider` stamp of the
  same moment. Quote the figure.
- **V-5 (C):**
  1. On a fresh Keycloak volume, observe that the inspection label text changes at the defect
     cue.
  2. `GET /system-variables/inspection_last_defect` returns the value.
  3. Pre-create the variable as `Number` and restart the simulator. Observe the named refusal
     and that no reaction is seeded.
- **V-6 (D):** start with a planted orphan reaction (a local, uncommitted scenario edit). Observe
  exactly one `ScenarioReactionUnreachable` warning and that the rest seeds.

The simulator is dev-only (spec 064), so none of this runs in CI. Unit tests carry the automated
evidence.

## 9. Risks and follow-ups

- **R-1 FFmpeg loop drift.** `-stream_loop -1 -c copy -re` should keep the loop at the file's
  duration, but that is not measured. V-3 measures it, and ADR-0163's fallback exists for it.
- **R-2 Anchor semantics.** `readyTime` is when the publisher session became ready, which is
  about offset 0 plus FFmpeg's start-up. A constant offset appears as a consistent early or late
  bias in V-2. If it exceeds 200 ms consistently, add a single constant, `CueLeadMs` in
  `ScenarioOptions` (measured, not guessed), rather than a per-clip knob.
- **R-3 Contention files.** `AppHost.cs` and the realm file in PR-C: rebase just before opening.
- **F-1 (follow-up issue):** annotate the remaining 11 clips (content).
- **F-2 (follow-up issue):** `AutomationRulesClient.Operator` silently maps an unknown comparison to
  `>=`. This is a behaviour fix, kept out of PR-A's characterisation.
- **F-3 (follow-up, when #2324 lands):** register `ObjectDetected` for the simulator's fab.

## 10. Constitution and ADR check

| Rule | Status |
|---|---|
| ADR-0111 / ADR-0163 | Implemented as amended. Dev-only gating is unchanged (`isRunMode && !isE2ETests && ScenarioSimulator`) |
| No cross-context references (NetArchTest) | No new project references |
| §II primitives | N/A: no Domain project touched |
| §IV latency | N/A: upstream of event arrival. No leg changes |
| ADR-0143 / 0142 retry safety | The new `GET` client uses the default handler. The `POST /system-variables` client does not opt into retry-every-method, and 409 covers the re-run |
| ADR-0105 guards, ADR-0141 `Option<T>` | `Ensure.That` on public entry points. The loader and path client return `Option<T>` |
| ADR-0050 logging | New `[LoggerMessage]` entries in `Log.cs` with structured fields |
| ADR-0084 metrics | `EnsureRuleAsync` drops from 9 parameters to 2 |
| ADR-0109 contention | `Log.cs`, `ScenarioSeeder.cs`, `AppHost.cs` and the realm file are sequenced, not parallel |
| ADR-0139 / §Testing | Colours are declared per PR in §7. PR-A is characterisation-only |
