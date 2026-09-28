# Feature Specification: The story a scenario tells

**Feature Branch**: `289-the-story-a-scenario-tells` (cut from `origin/develop` at `2cbd044b`; not pushed)

**Created**: 2026-09-28

**Status**: Draft (Phase 1 gate). **Supervised lane, exploratory.** Phase 1 only: no `plan.md` and no `tasks.md` until the owner answers §4.

**Input**: The owner's request (in their own words): extend the dev-only Scenario Simulator so that simulated data, meaning sensor readings and simulated computer-vision detections, is generated **in sync with the simulated video** and can **drive the reactions, actions and overlays** a scenario sets up. A scenario should read as one story: *this happens on the video, this rule reacts, this action fires, this overlay changes.*

**Spec number.** 289. On 2026-09-28 `origin/develop` goes up to 286. Specs 282, 287 and 288 exist on unmerged branches. 284 is not on any branch in this clone's refs, but this session's sibling specs may have claimed it, so it is skipped. **Re-check before any PR** (memory: *spec number: origin/develop isn't enough*).

**ADRs and constitution sections referenced:** ADR-0111 (Scenario Simulator: asset model, M1/M2, dev-only gating), ADR-0095 (event ingestion / MQTT topic convention), ADR-0099 (AEL, the rule expression language), ADR-0129 (a label is aged to match its picture, not frame-matched), ADR-0128 (nothing we own sits in the media path), ADR-0114 (explicit `fabId` on seeded rules), ADR-0142/0143 (retry safety for the seeding POSTs), ADR-0103 (Aspire-fixture tests), ADR-0139 + constitution §Testing (red first), constitution §IV (latency budget), ADR-0037 (phases). `0000-initial-decisions.md` decision 018 and spec 143 cover the event-type registry. That registry exists but nothing enforces it; see §1.4.

**ADR gap: probably one.** ADR-0111 describes M2 as "a timeline" and says nothing about clip-relative timing, simulated detections, or the scenario file declaring reactions. If the owner picks the recommended options in §4, the scenario becomes a **declarative story format with a clip-phase clock**. That is a decision, and it should be recorded as an ADR-0111 amendment (or a new ADR) at the Phase 2 gate. I do not write that ADR here (ADR-0144 does not apply to this lane, but ADR-0037 still asks for the decision to be made by a human first).

---

## 1. What exists today (measured on `origin/develop` @ `2cbd044b`, 2026-09-28)

The request describes three gaps. **Gap 3 is mostly wrong, gap 2 is half wrong, and gap 1 is right but harder than it looks.** Details follow.

### 1.1 The scenario file already wires reactions (corrects gap 3)

`src/ScenarioSimulator/Scenarios/{rolling-mill,paper-mill,electronics}.json` are bound into `ScenarioOptions` (`Scenario/ScenarioOptions.cs`). `ScenarioSeeder` (`Seeding/ScenarioSeeder.cs`) seeds the following for **every active scenario and every asset**:

| Scenario-file field | What the seeder creates | Client |
|---|---|---|
| `Asset.Overlay` (label + normalised geometry + font) | an OverlayDesigner overlay named `{scenario}-{asset}`, first revision **published** | `OverlayDesignerClient.EnsureOverlayAsync` |
| `Asset.Highlight` (`TriggerKind`, `Comparison`, `Threshold`, `DurationMs`) | an Automation rule `{scenario}-{asset}-highlight`: trigger `(source, kind)`, predicate `$.device == '<camera path>' && $.payload.value <op> <threshold>`, action **`HighlightOverlay`** on that overlay, **published** | `AutomationRulesClient.EnsureRuleAsync` |
| `Asset.Tile` + `Scenario.Wall` | a layout/wall with each asset's camera and overlay on its tile | `WallSeeder`, `LayoutCompositionClient` |
| `Asset.Camera` | a camera-catalog registration; `CameraRegisteredV1` → camera-sim path provisioned | `CameraCatalogClient`, `CameraRegisteredSimHandler` |

The rule's `triggerSource` is taken from the sensor whose `Kind` matches `Highlight.TriggerKind`. If no sensor matches it silently falls back to `plc` (`ScenarioSeeder.cs`, `SeedOverlayAndRuleAsync`).

So **data → rule → action → visible effect already runs end to end** for one reaction shape. For example, rolling-mill's coiler steps `CoilWeight` from 4 t to 24 t at 60% of the dwell. The seeded rule `CoilWeight >= 20` fires, `OverlayHighlightRequestedV1` flows to LayoutComposition, and the kiosk flashes the coiler tile's banner for 4 s. The gap is that this is the **only** reaction shape. Specifically:

- **One reaction per asset, one shape.** `Highlight` is a single object. The predicate is hard-wired to a numeric comparison on `$.payload.value` (`AutomationRulesClient.cs`, `Operator(...)`, which also maps an unknown comparison to `>=` without saying so). A detection-style predicate (`$.payload.class == 'person' && $.payload.confidence >= 0.8`) cannot be expressed.
- **No `SetVariableValue` reaction is seeded.** `CreateRuleBody` always sends `VariableName: null`, `ValueExpression: null`. The simulator seeds no system variables, and its Keycloak client lacks `sse.variables.write` (`src/AppHost/Realms/smart-sentinel-eye-realm.json`, `scenario-simulator` default scopes). No seeded label contains a `{{placeholder}}`. So **every seeded label is static text**, and the only live effect on a wall is the highlight flash.
- **Seeding is create-once, not reconcile.** On a 409 the overlay and rule clients read back and publish a stuck Draft. They never update a changed definition. If a scenario file is edited after the first boot, the running rules and overlays do not follow it.

### 1.2 `inference` is already a first-class source (corrects half of gap 2)

- `SensorDefinition.Source` already accepts `plc | inference`. Its doc comment reads: "vision-derived kinds set `inference`". **No shipped scenario uses it**, because all 21 sensors are `plc`.
- The Mosquitto ACL already grants `scenario-simulator` `topic write fab/munich/inference/#` (and the same for dresden, berlin and hamburg) in `src/AppHost/mosquitto/acl.txt:26-34`.
- EventIngestion's `Source` VO accepts `inference`. `FabEventIngestedV1` carries any JSON payload up to 64 KB.

**The half of gap 2 that is correct:** every emitted sample has the shape `{ value: double, unit, station }` (`MqttSampleMapper`). There is no categorical or structured payload, no class, no confidence, no box and no zone.

### 1.3 The pipeline a detection would travel (already built)

```
simulator MQTT  fab/munich/{source}/{camera-path}  {eventId, kind, occurredAt, payload}
  → EventIngestion MqttSubscriberHostedService (fab/+/+/+) → persist → FabEventIngestedV1
  → Automation FabEventIngestedV1Handler → RuleEvaluator: Active rules for (fab, source, kind)
      AEL predicate over {source, kind, device, payload.*}
      ├─ HighlightOverlay  → OverlayHighlightRequestedV1 → LayoutComposition → SignalR → kiosk CSS flash
      └─ SetVariableValue  → SystemVariableValueRequestedV1 → SystemVariables sets typed value
             → re-resolves every overlay label containing {{name}} (PlaceholderParser)
             → ResolvedOverlayTextChangedV1 → LayoutComposition → SignalR → kiosk label text
```

**AEL constraints that shape the detection payload** (`src/Automation/Application/Ael/`): AEL has literals, `$.a.b` member access, **numeric** arithmetic, comparison, `&&`/`||`, and nothing else. There is **no array indexing, no function call and no string concatenation**: `+` is numeric only (`AelInterpreter.EvalArithmetic`). A payload holding a *list* of detections therefore cannot be matched. **One MQTT event per detection is forced by the rule language**, so I record it as a finding rather than asking it as a question. The same constraint means **a `SetVariableValue` expression cannot build display text**. A cue whose text should appear on a label must carry display-ready text in its payload (FR-002's `label`), and the label template supplies the surrounding text.

**Overlays are fixed-geometry text labels** (`OverlayDesigner/Domain/Overlay/Label.cs`). Nothing in OverlayDesigner or the kiosk can draw a box whose position comes from event data. See Q5.

### 1.4 Event-type registration does not constrain this

Spec 143 built the `RegisteredEventType` registry (#1972). **Ingest does not read it.** `IngestEventBatchCommandHandler` does not reference it, and strict/discovery mode (#2324), quarantine (#2325) and payload schemas (#2326) are all **OPEN and unbuilt**. A new kind such as `ObjectDetected` therefore flows today with no registration. When #2324 lands, a strict-mode `inference` source would refuse unregistered kinds. At that point the seeder would have to register its kinds. That is a forward dependency, not a blocker (FR-014).

### 1.5 The video: what the simulator can know about it (refines gap 1)

- The clips are **not synthetic.** Each is a **20-second excerpt of real footage from Wikimedia Commons** (`src/AppHost/Resources/clips/*.ATTRIBUTION.txt`). For example, the four `mill-*` clips are four offsets into one 477 s rolling-mill film. Their content is fixed and knowable, but only once a person **watches and annotates** them. No machine-readable description of what happens when in a clip exists today.
- camera-sim plays each clip with `ffmpeg -stream_loop -1 -re -i /media/{clip} -c copy …` as a MediaMTX **`runOnDemand`** command, with `runOnDemandRestart: true` and `runOnDemandCloseAfter: 10s` (`CameraSim/CameraSimProvisioner.cs`). So:
  - **Playback starts at offset 0 when the first reader arrives, not when the simulator starts.** It stops 10 s after the last reader leaves, and the next viewer restarts it from 0. The simulator does not decide when this happens and is not told.
  - While nobody is watching, no video plays at all.
- The billet timeline (`Timeline/BilletTimelineHostedService.cs`) runs on its own `Task.Delay` clock (`DwellMs` 6000, `TickMs` 500, `LoopGapMs` 3000). It is unrelated to any clip's playback position, and it animates **only the first active scenario** (`ScenarioOptions.Animated`).

So "in sync with the video" requires one thing that does not exist: **a way for the simulator to learn where each clip's loop currently is.** Q1 covers that.

### 1.6 The nearest working precedent for the full chain

The precedent is the seeded `HighlightOverlay` rule in §1.1 (spec 044 / ADR-0111 M2). #2618 (*An Automation rule switches a wall*, spec 263 US2) is **OPEN and unbuilt**: `RuleAction` still has exactly two variants. A scenario that switches a wall is therefore out of scope until #2618 ships. The reaction format proposed here leaves room for it (FR-008).

---

## 2. User Scenarios & Testing *(mandatory)*

The actor is a **developer or demo presenter** running the dev stack with the simulator enabled (`isRunMode && !isE2ETests && ScenarioSimulator != false`). Nothing here runs in CI end-to-end jobs, E2E or production, because ADR-0111's gating is unchanged.

### User Story 1: A detection seen on the video lights the tile at that moment (Priority: P1)

A presenter watching the rolling-mill wall sees a worker walk into the roughing stand's frame about 12 s into the clip. At that moment the tile's banner flashes, because the scenario states that the clip shows a person at 12 s. That statement produced an `inference` detection event, and a seeded rule reacting to "person, confidence ≥ 0.8, on this camera" fired a highlight. Each time the clip loops, it happens again at the same point in the picture.

**Why this priority**: this is the smallest slice that exercises everything new: a clip-cue manifest, the clip-phase clock, the detection payload, and a reaction whose predicate is richer than `$.payload.value >= n`. It reuses the only action shape already proven end to end (`HighlightOverlay`), so no Automation, SystemVariables or kiosk code changes.

**Independent Test**: boot the dev stack. Open the rolling-mill wall in a kiosk. Record the screen for three clip loops (about 60 s). In the recording, the roughing tile's highlight starts within the Q1-chosen tolerance of the frame where the annotated person appears, on every loop. In the Aspire structured logs, each emitted cue shows its clip offset and the matching `FabEventIngestedV1` / `OverlayHighlightRequestedV1` pair for that loop. Then close the wall for more than 10 s and reopen it. camera-sim restarts the clip from 0, and the highlight still lines up with the person rather than with the old phase.

**Acceptance Scenarios**:

```gherkin
Feature: Clip-anchored detection cues drive a seeded reaction

  Background:
    Given the dev stack runs with the Scenario Simulator enabled
    And rolling-mill's asset "station-4-roughing" plays "mill-roughing.mp4" (20 000 ms)
    And that clip's cues declare { atMs: 12000, kind: "ObjectDetected", class: "person", confidence: 0.92 }
    And the scenario declares a reaction on that asset:
      when inference/ObjectDetected where $.payload.class == 'person' && $.payload.confidence >= 0.8
      then HighlightOverlay for 4000 ms

  Scenario: The cue fires at its point in the picture (happy path)
    Given a kiosk is showing the rolling-mill wall
    When the roughing clip's playback reaches 12 000 ms
    Then exactly one ObjectDetected event for device "station-4-roughing" is ingested for that loop
    And the roughing tile's banner is highlighted within the sync tolerance of that frame
    And the same happens on the next loop, 20 000 ms later

  Scenario: Playback restarts and the cues follow it (conflict: the simulator's view vs camera-sim's)
    Given the wall has been closed long enough for camera-sim to stop the path
    When a kiosk opens the wall again and camera-sim restarts the clip from 0
    Then the next cue is emitted relative to the new start, not the old phase
    And no cue is emitted while the path has no reader

  Scenario: A cue below the rule's confidence does not react
    Given the clip also declares { atMs: 4000, kind: "ObjectDetected", class: "person", confidence: 0.55 }
    When playback passes 4 000 ms
    Then an ObjectDetected event is ingested
    And no highlight is requested for it

  Scenario: A malformed cue manifest is refused at load (bad request)
    Given a cue with atMs 25000 on a 20 000 ms clip
    When the simulator loads the scenario
    Then it logs a warning naming the scenario, asset, clip and cue index
    And that asset emits no cues and seeds no reaction that depends on them
    And every other asset seeds and runs normally

  Scenario: The seeded reaction is written with the simulator's own grant (auth)
    Given the scenario-simulator service account holds sse.rules.write
    When the reaction is seeded
    Then POST /rules?fabId=munich succeeds and the rule is published
    And a token without sse.rules.write is refused 403 on the same call (existing behaviour, unchanged)
```

---

### User Story 2: A reaction sets a variable and a label on the wall shows it (Priority: P2)

The electronics inspection tile's banner reads `INSPECTION: {{inspection_last_defect}}`. When the clip shows a defective board at 7 s, the label changes to `INSPECTION: solder bridge` and keeps that text until the next cue changes it.

**Why this priority**: this is the second, and only other, built action shape. It is what turns static labels into live ones, but it needs three things US1 does not: system-variable seeding, a new realm scope for the simulator, and placeholder labels.

**Independent Test**: boot a stack with a fresh Keycloak volume, because the realm edit only applies on a fresh volume (memory: *realm edits need the volume deleted*). Open the electronics wall. Watch the inspection label's text change at the annotated offset. `GET /system-variables/inspection_last_defect` returns the value, and the audit trail links the change to the causing event.

**Acceptance Scenarios**:

```gherkin
  Scenario: A detection sets a String variable the label renders
    Given the scenario declares variable "inspection_last_defect" of type String
    And a reaction: when inference/ObjectDetected where $.payload.class == 'defect'
                    then SetVariableValue inspection_last_defect = $.payload.label
    And the inspection overlay label is "INSPECTION: {{inspection_last_defect}}"
    When the inspection clip reaches its defect cue, whose payload label is "solder bridge"
    Then the variable's value becomes "solder bridge"
    And the kiosk label text changes to "INSPECTION: solder bridge"

  Scenario: A variable already defined with another type is not redefined (conflict)
    Given "inspection_last_defect" already exists as a Number
    When the simulator seeds the scenario
    Then it logs a warning naming the variable, the declared and the existing type
    And it does not seed the reaction that writes it

  Scenario: An unparseable value expression is refused before publishing (bad request)
    Given a reaction whose value expression does not parse as AEL
    When the simulator seeds it
    Then Automation answers 400 and the simulator logs the rule name and the error
    And the other reactions still seed

  Scenario: Variable seeding needs sse.variables.write (auth)
    Given the scenario-simulator client holds sse.variables.write
    When variables are seeded
    Then POST /system-variables succeeds
```

---

### User Story 3: A scenario that cannot tell its story says so at startup (Priority: P3)

A developer adds a reaction on `ObjectDetected` to an asset whose clip declares no such cue, or on a `Temperature` sensor that never reaches the threshold. At startup the simulator states that this reaction can never fire and names the scenario and asset. Without this, the developer finds out by watching a wall that never reacts.

**Why this priority**: this is what makes the scenario a *story* instead of three lists that happen to share a file. The failure it prevents already exists in miniature today: `Highlight.TriggerKind` with no matching sensor silently falls back to `plc`.

**Independent Test**: start the simulator against a scenario with one deliberately orphaned reaction. The structured log shows one `ScenarioReactionUnreachable` warning naming it, and the other reactions seed.

**Acceptance Scenarios**:

```gherkin
  Scenario: A reaction whose trigger no sensor or cue emits is reported
    Given a reaction on inference/ObjectDetected for asset "coiler"
    And coiler's clip declares no ObjectDetected cue and no sensor emits that kind
    When the scenario loads
    Then a warning names scenario "rolling-mill", asset "coiler" and the trigger
    And the reaction is still seeded (it is not the simulator's place to refuse a rule a human can also trigger)

  Scenario: The trigger source is derived and never silently defaulted
    Given a reaction's trigger kind matches a cue with source "inference"
    Then the seeded rule's trigger source is "inference"
    And a trigger kind matching nothing is reported by the check above rather than defaulted to "plc"
```

### Edge Cases

- **No reader, no video.** With `runOnDemand`, a clip nobody watches is not playing. The recommended answer to Q1 emits no clip cues for such a path. Sensors on the billet clock are unaffected and keep emitting as they do today.
- **Two kiosks, one path.** MediaMTX runs one FFmpeg per path however many readers there are. The phase is per path, not per viewer, so both kiosks see the same picture and the same cue.
- **Clip shared between assets.** `sim-loop.mp4` is the default clip. If cues live with the clip (Q2 option A), every asset playing it inherits its cues. The device on each emitted event is still the asset's camera path, so reactions stay per-tile.
- **Loop drift.** `-stream_loop -1 -c copy` loops at the container's duration, and `-re` paces on wall time. Over long runs the phase estimate may drift by up to a GOP. The phase anchor is re-read per loop (FR-004) rather than extrapolated forever.
- **The kiosk sees the frame later than the event arrives.** camera-sim publish → SFU → decode → presentation buffer is ≤ 400 ms by budget, and event → overlay state is ≤ 200 ms. By ADR-0129 a label is aged, not frame-matched, so a cue published at the frame's publish instant lands inside the picture's own lag. The sync tolerance (SC-001) must be wider than that difference.
- **Scenario file edited after first boot.** Seeding is create-once (§1.1). A changed reaction or label does not reach an existing database. This is recorded, not fixed, unless the owner picks Q4(b) with reconciliation.
- **Only the first active scenario animates today.** Clip cues are per-path and stateless, so FR-006 runs them for **every** active scenario whose paths have readers. That differs from the billet timeline, and the difference is stated so nobody later "fixes" it.
- **High-rate cues.** A cue list may declare several detections per second. EventIngestion's batch fast path absorbs this, but a manifest with more than 10 cues/s per clip is refused at load (FR-003) so that a typo cannot flood the dev broker.

## 3. Requirements *(mandatory)*

### Functional Requirements

- **FR-001 (cue manifest)**: A clip MUST be able to declare an ordered list of **cues**. Each cue has `atMs` (offset into the clip, `0 ≤ atMs < clip duration`), a `kind`, a `source` (default `inference`), and a `payload` object. Where cues live is Q2. [NEEDS CLARIFICATION: Q2]
- **FR-002 (detection payload)**: A detection cue MUST produce **one** MQTT event per detection on `fab/munich/{source}/{camera path}` with body `{ eventId, kind, occurredAt, payload }`. `payload` carries at least `class` (string), `confidence` (0..1), and `station` (asset key). Optional fields are `label` (human text), `zone` (string), `box` `{ x, y, width, height }` (normalised 0..1, the same convention as overlay geometry), and `trackIdentifier` (string, which lets enter/exit cues pair). One event per detection is forced by AEL's lack of arrays (§1.3). The kind name is an assumption; see A-3.
- **FR-003 (manifest validation)**: At load, the simulator MUST refuse a clip's cues if any `atMs` lies outside the clip's duration, if the cues are not in ascending order, or if the list exceeds 10 cues per second of clip. The refusal is a warning naming the scenario, asset, clip and offending cue index. The rest of the scenario seeds.
- **FR-004 (clip-phase clock)**: Cues MUST be emitted relative to the clip's **actual playback position on camera-sim**, not a simulator-local clock. The anchor is re-established every loop and on every camera-sim path restart. The mechanism is Q1. [NEEDS CLARIFICATION: Q1]
- **FR-005 (no reader, no cue)**: While a camera-sim path has no running source, its clip's cues MUST NOT be emitted. (This holds under Q1-A. Under Q1-C it is moot.)
- **FR-006 (all active scenarios)**: Cue emission MUST run for every active scenario, not only `ScenarioOptions.Animated`. The billet timeline's one-plant restriction stays as it is.
- **FR-007 (sensor timing)**: Sensor samples either stay on the billet clock or move to the clip-phase clock. That is Q3. [NEEDS CLARIFICATION: Q3]
- **FR-008 (reactions)**: A scenario MUST be able to declare, per asset, **zero or more reactions**. Each reaction has a trigger (`source`, `kind`), an AEL predicate, and an action. Actions in scope are `HighlightOverlay` (on the asset's seeded overlay, with a duration) and `SetVariableValue` (a variable name and an AEL value expression). The action list MUST be open to `SwitchWallScene` once #2618 ships, without a format change. The predicate is prefixed with `$.device == '<camera path>' &&` by the seeder, so a reaction always stays on its own tile. How reactions reach Automation is Q4. [NEEDS CLARIFICATION: Q4]
- **FR-009 (backward compatibility)**: The existing `Highlight` object MUST keep seeding the identical rule, with the same name, predicate and action. It is read as shorthand for one reaction. All three shipped scenario files MUST seed unchanged rules and overlays without being edited (characterisation, observed green).
- **FR-010 (variables)**: A scenario MUST be able to declare system variables (`name`, `type` ∈ `String|Number|Boolean`, and `BooleanLabels` where applicable). The simulator seeds them idempotently. An existing variable with a different type is reported and its dependent reactions are skipped, not overwritten.
- **FR-011 (live labels)**: An overlay label in a scenario MAY contain `{{variable}}` placeholders. They are stored verbatim and resolved by SystemVariables as they are today. No OverlayDesigner change is needed.
- **FR-012 (grant)**: The `scenario-simulator` realm client MUST gain `sse.variables.write` (and `sse.variables.read`, so it can read back on a 409). No other client changes.
- **FR-013 (story check)**: At load, every reaction whose trigger `(source, kind)` is emitted by **no** sensor and **no** cue on the same asset MUST be reported (US3). The `plc` fallback in `SeedOverlayAndRuleAsync` MUST be replaced by this report.
- **FR-014 (registry, forward)**: When strict mode (#2324) lands, the simulator must register the kinds it emits. That is out of scope here. It is recorded so #2324's spec finds it.
- **FR-015 (dev-only)**: No change reaches CI, E2E or production composition. camera-sim and the simulator stay behind ADR-0111's gate. No `Shared.Contracts` change, no new integration event, and no change to Automation, SystemVariables, OverlayDesigner, LayoutComposition or the kiosk.

### Key Entities

- **Clip manifest**: `{ clip, durationMs, cues[] }`. The ground truth of what a clip shows and when. It is hand-authored by someone who watched the clip.
- **Cue**: `{ atMs, source, kind, payload }`. One simulated event at one playback offset.
- **Detection payload**: `{ class, confidence, station, label?, zone?, box?, trackIdentifier? }`.
- **Reaction**: `{ name, trigger: { source, kind }, predicate, action }`, where `action` is `{ type: HighlightOverlay, durationMs }` | `{ type: SetVariableValue, variable, value }`.
- **Variable declaration**: `{ name, type, booleanLabels? }`.
- **Clip phase**: `(now − playbackStartedAt) mod durationMs` for one camera-sim path.

### Proposed scenario-file shape (illustrative, for the owner to react to)

This shows Q2 option A (cues beside the clip) and Q4 option (b) (reactions seeded from the scenario):

```jsonc
// src/AppHost/Resources/clips/mill-roughing.cues.json   (beside mill-roughing.mp4)
{
  "Clip": "mill-roughing.mp4",
  "DurationMs": 20000,
  "Cues": [
    { "AtMs": 4000,  "Kind": "ObjectDetected", "Payload": { "Class": "person", "Confidence": 0.55, "Zone": "walkway" } },
    { "AtMs": 12000, "Kind": "ObjectDetected", "Payload": { "Class": "person", "Confidence": 0.92, "Zone": "exclusion", "Label": "PERSON IN EXCLUSION ZONE",
                                                             "Box": { "X": 0.61, "Y": 0.40, "Width": 0.08, "Height": 0.31 } } }
  ]
}

// rolling-mill.json, one asset (existing fields elided)
{
  "Key": "station-4-roughing",
  "Camera": { "Path": "station-4-roughing", "Clip": "mill-roughing.mp4", "Loop": true },
  "Overlay": { "Label": "ROUGHING — {{roughing_zone_state}}", "X": 0.1, "Y": 0.05, "Width": 0.8, "Height": 0.18 },
  "Variables": [ { "Name": "roughing_zone_state", "Type": "String" } ],
  "Reactions": [
    { "Name": "person-in-exclusion",
      "When": { "Source": "inference", "Kind": "ObjectDetected",
                "Predicate": "$.payload.class == 'person' && $.payload.zone == 'exclusion' && $.payload.confidence >= 0.8" },
      "Then": { "Type": "HighlightOverlay", "DurationMs": 4000 } },
    { "Name": "zone-state",
      "When": { "Source": "inference", "Kind": "ObjectDetected", "Predicate": "$.payload.class == 'person'" },
      "Then": { "Type": "SetVariableValue", "Variable": "roughing_zone_state", "Value": "$.payload.label" } }
  ],
  "Highlight": { "TriggerKind": "Temperature", "Comparison": "gte", "Threshold": 1100, "DurationMs": 4000 },
  "Sensors": [ /* unchanged */ ]
}
```

The seeded rule names would be `{scenario}-{asset}-{reaction name}`, and `{scenario}-{asset}-highlight` stays for the legacy field (FR-009).

## 4. Open questions *(for the Phase 1 gate)*

### Q1: How does the simulator learn where each clip is in its loop? [NEEDS CLARIFICATION]

| Option | Mechanism | Trade-off in one line |
|---|---|---|
| **A. Anchor on camera-sim's path ready time** (recommended) | Per path, poll camera-sim's control API (`GET /v3/paths/get/{path}` → `ready`, `readyTime`). Phase = `(now − readyTime) mod durationMs`. Re-read each loop, and treat a changed `readyTime` as a restart. | Uses a server and an API the simulator already talks to (port 9997), so it needs no media-path change. Accuracy is roughly FFmpeg start-up plus the first keyframe, likely a few hundred ms. **`readyTime` in the pinned `1.21.0-ffmpeg` image must be verified at Phase 2** (memory: *a plan's SDK claim needs checking against the pinned version*). |
| **B. The simulator owns playback** | Switch paths from `runOnDemand` to always-on (`runOnInit`, or the simulator publishes itself), so the clip starts when the simulator says. | The phase is known exactly at start, but FFmpeg runs 12 streams 24/7 on a dev machine (today it runs only while watched), and a restart still needs detecting, so A's mechanism is needed anyway. |
| **C. No sync; line the timelines up by construction** | Set each station's dwell to its clip's duration and start both at boot. | Cheapest, but it is **not synced**: `runOnDemand` restarts the clip from 0 whenever a viewer returns, so it is right only by coincidence. |
| **D. Real inference on the clips** | Run a detector (for example YOLO) over the footage and publish its output. | The clips *are* real footage (§1.5), so this is possible, unlike with synthetic clips. It still means a model, a GPU or slow CPU inference, and non-determinism in a dev harness whose value is a repeatable story. It is out of scope, but it is the natural shape for a later "bring a real detector" spec, because it would publish the same FR-002 payload on the same topic. |

**Recommendation: A.** It is the only option that stays correct across the `runOnDemand` restarts that happen every time a wall is closed and reopened. It adds no always-on load, and it touches neither the SFU nor camera-sim's configuration. **I also recommend fixing the acceptable tolerance now: ±500 ms between the annotated frame and the start of the overlay change, as seen on the kiosk.** That is looser than FFmpeg's start-up jitter and tighter than a presenter would notice. If A's measured accuracy cannot meet it, fall back to B for the affected paths rather than loosening the number.

### Q2: Where do cues live: beside the clip, or inside the scenario? [NEEDS CLARIFICATION]

- **(A) A sidecar per clip, `<clip>.cues.json` next to the `.mp4` (recommended).** Cues describe *what is on the footage*, which is a property of the clip, not the plant. `sim-loop.mp4` is reused, and the four `mill-*` clips come from one film. The regeneration script (`scripts/generate-sim-clips.sh`) and the `.ATTRIBUTION.txt` files already treat the clip directory as the home for per-clip facts. The scenario references the clip and so inherits its cues. `ClipLibrary` already resolves clips there, so it gains a sibling lookup.
- **(B) Inline per asset in the scenario JSON (`Camera.Cues`).** One file tells the whole story, which is closest to the request's wording. But two assets on the same clip must repeat the cues, and a re-cut clip silently invalidates annotations held in three scenario files.
- **(C) Both: sidecar as default, with per-asset overrides in the scenario.** This is the most flexible, but it gives two homes for one fact, which is what ADR-0036 warns against when nothing needs it yet.

**Recommendation: A.** The owner asked for "one declarative story". With A, the *story* (assets, reactions, variables, labels) is still one scenario file. Only the *facts about the footage* live beside the footage, where re-cutting a clip forces its annotation to be looked at.

### Q3: Do sensor samples move to the clip clock too? [NEEDS CLARIFICATION]

- **(a) No. Sensors stay on the billet clock, and only cues are clip-anchored (recommended for this spec).** Sensor readings (temperature, force, weight) are process data that the footage does not show at a readable precision. The billet narrative spans stations and is intentionally longer than any single clip. Leaving it alone keeps FR-009's "shipped scenarios seed and animate unchanged" trivially true.
- **(b) Sensors become clip-relative too:** each station's billet dwell becomes that station's clip loop, and `StepAtFraction` and the other behaviour parameters are read against clip phase. This moment-matches, for example, the coiler weight step with the coil visibly dropping. It is a larger change, and it ends the "billet travels the line" narrative, because four clips loop independently.
- **(c) Per sensor, opt in with `"Clock": "clip"`,** where the default is billet.

**Recommendation: (a) now, and (c) as a follow-up story only if a scenario turns out to need a sensor moment-matched to the picture.** Nobody has yet asked for a specific sensor to match a specific frame, and cues can already carry numeric payloads for the moments that matter.

### Q4: How does a scenario's declared reaction reach Automation? [NEEDS CLARIFICATION]

- **(a) The scenario emits data only. Rules, variables and overlays are configured separately through the console.** There is no coupling, but it **reverses what exists today**: the simulator already seeds overlays, rules and walls (§1.1). It also leaves the "complete declarative story" to documentation.
- **(b) Generalise the existing seeding: `Reactions[]` and `Variables[]` in the scenario, seeded through the same REST clients with the same idempotent create-then-publish pattern (recommended).** `Highlight` stays as shorthand (FR-009). `AutomationRulesClient` takes a predicate string instead of building one, and a `SystemVariablesClient` is added beside it. This extends a working pattern and invents nothing. The cost is one realm scope (FR-012) and create-once semantics: a reaction edited after first boot does not update (§1.1). **Sub-question:** should (b) also *reconcile*, meaning update a rule or overlay whose definition changed? My recommendation is **no**, not in this spec. Reconciling means versioned updates with `If-Match` against rules a human may have edited in the console, and that is a policy decision about who owns a seeded rule. Document "delete the volume or the rule to re-seed" instead.
- **(c) The scenario declares expected reactions and verifies them** (it dry-runs each cue against the existing Active rules through Automation's dry-run query and reports mismatches), but seeds nothing. That is useful for checking hand-built configuration, but it duplicates (b)'s authoring with none of its convenience.

**Recommendation: (b), without reconciliation.** US3's story check covers what (c) would have added, at load time and without a round trip.

### Q5: Do "overlays update" include drawing the detection box on the video? [NEEDS CLARIFICATION]

This decides whether the spec stays inside the simulator or reaches OverlayDesigner, LayoutComposition and the kiosk.

- **(a) No. The box travels in the payload and nothing draws it (recommended).** The visible reactions are the two that exist today: a highlight flash and live label text. The box is carried so that a later spec (or real inference, Q1-D) has it.
- **(b) Yes.** A new overlay element whose geometry comes from event data would need a new `RuleAction` (or a new variable type), a new integration contract, kiosk rendering, and an ADR. It is also on the §IV event → overlay-state and composite legs. That makes it a product feature for real CV integrations, not a simulator feature, and it would be its own spec.

**Recommendation: (a).** Boxes drawn from simulated data would be designed around the simulator. If they are wanted, they should be designed around what a real detector sends, in their own spec with their own ADR.

## 5. Latency budget impact (constitution §IV)

**None on the six legs.** The simulator is upstream of "event arrival", which is where the budget starts. Detection events travel the existing `Event → overlay state` leg (≤ 200 ms) through unchanged EventIngestion, Automation, SystemVariables and LayoutComposition code, and the kiosk's composite-and-render leg is untouched (Q5-a). The clip-sync tolerance in SC-001 is a **demo-fidelity** figure, not an SLO figure, and must not be cited as a measurement of any §IV leg. If Q5 were answered (b), this section would change and the spec would split.

## 6. Success Criteria *(mandatory)*

- **SC-001**: On the rolling-mill wall, over 10 consecutive clip loops, the roughing tile's highlight begins within **±500 ms** (pending Q1) of the frame where the annotated person appears, on every loop. This is measured from a screen recording and quoted in the verification note with the per-loop offsets.
- **SC-002**: After closing a wall for 15 s and reopening it, the first cue after the restart meets SC-001's tolerance. Pass/fail is on the first loop, not the average.
- **SC-003**: The three shipped scenario files, unedited, seed rules and overlays that are byte-identical in name, predicate, action and label to `origin/develop @ 2cbd044b`. This is captured before the change and re-read after it (characterisation).
- **SC-004**: A scenario with one orphaned reaction produces exactly one `ScenarioReactionUnreachable` warning and seeds every other reaction.
- **SC-005**: No file under `src/{Automation,SystemVariables,OverlayDesigner,LayoutComposition,EventIngestion,Shared.Contracts}` or `apps/` changes (checked with `git diff --stat` against the base). The only edits outside `src/ScenarioSimulator` are the realm file, the clip directory and, if needed, the AppHost wiring.

## 7. Slicing (for Phase 3, subject to §4)

1. **US1** (P1): cue sidecar + validation, the clip-phase clock (Q1), detection publishing, generalised `Reactions[]` with `HighlightOverlay` only, and FR-009 characterisation. One PR. This is independently demonstrable on one tile.
2. **US2** (P2): `Variables[]`, `SystemVariablesClient`, the realm scope, `SetVariableValue` reactions, and a placeholder label in the electronics scenario. One PR, after US1, because it depends on US1's reaction format.
3. **US3** (P3): the story check and removal of the `plc` fallback. It can run in parallel with US2 once US1 has merged, since it owns the loader validation files and not the clients.
4. Scenario authoring: annotating cues on all 13 clips is **content work, not code**. Recommend annotating one clip per plant in US1/US2 and leaving the rest for a follow-up issue.

## 8. Assumptions (explicit guesses)

- **A-1**: The owner wants this to stay **dev-only** under ADR-0111's gate, as spec 064 (#2013) settled it: `ScenarioSimulator=false` in the e2e job and `!isE2ETests` in the integration fixture. This spec proposes no simulator data in any CI job, so the story is verified by hand at Phase 5, not by an automated end-to-end test. Unit tests cover the manifest, the phase arithmetic and the story check.
- **A-2**: Hand annotation of clips is acceptable. Someone watches each 20 s clip and writes its cues.
- **A-3**: The kind name is one generic `ObjectDetected`, with the class in the payload, rather than per-class kinds (`PersonDetected`). A generic kind matches the rule cache's `(source, kind)` lookup to one bucket, and the class goes into the predicate. Per-class kinds would make rules cheaper to select but multiply registry entries once #2324 lands. This is easily reversible per cue, so I did not ask it as a question.
- **A-4**: The fab stays `munich`, hard-coded in `MqttSampleMapper` and `AutomationRulesClient`, as today. Multi-fab scenarios are out of scope.
- **A-5**: "Enter/exit" style stories (a person present for 3 s) are expressed as two cues sharing a `trackIdentifier` with different payloads (for example `state: 'entered'` and `'left'`). No stateful detection semantics are added to the simulator.
