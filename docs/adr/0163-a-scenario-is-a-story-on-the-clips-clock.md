# ADR-0163: A scenario is a story, and its detections run on the clip's clock

**Status:** Proposed. It becomes Accepted when spec 289's Phase 2 gate is passed.
**Date:** 2026-09-29
**Amends:** ADR-0111 (Scenario Simulator): the M2 "timeline" clause and the scenario definition
**Supersedes:** —
**Superseded by:** —

**Relates to:** ADR-0095 (event ingestion / MQTT topic convention), ADR-0099 (AEL),
ADR-0129 (a label is aged to match its picture, not frame-matched), ADR-0128 (nothing we own is in
the media path), ADR-0036 (no speculative generality), spec 044 (per-asset clips), spec 064 (the
simulator is dev-only in CI too), spec 143 / #2324 (the event-type registry and its unbuilt strict
mode), #2618 (a rule switches a wall, unbuilt), spec 289 (the delivering spec).

## Context

ADR-0111 makes the Scenario Simulator an **asset**-centred worker. M1 is a looping camera clip per
asset. M2 is "sensor / events … on a timeline". It gives the timeline no clock beyond its own, and
it does not say whether the scenario file may declare how the system *reacts* to the data it emits.

Three facts, measured for spec 289 on `develop` at 2026-09-28/29, make both gaps matter:

1. **The simulator already seeds reactions, but only one shape.** `ScenarioSeeder` creates and
   publishes, per asset, an overlay, a `HighlightOverlay` rule with a hard-wired
   `$.payload.value <op> <threshold>` predicate, and a wall. ADR-0111 says none of this. The
   scenario file already *is* a story in practice, but a story with one sentence.
2. **The clips are real footage whose timing only a human can know.** Each is a 20 s excerpt from
   Wikimedia Commons. Nothing machine-readable says what happens when in a clip.
3. **camera-sim starts each clip when the first reader arrives, not when the simulator says.** The
   path runs FFmpeg as a MediaMTX `runOnDemand` command with `runOnDemandCloseAfter: 10s`, so a
   closed-and-reopened wall restarts the clip at offset 0. A simulator-local clock therefore cannot
   stay in step with the picture. camera-sim's control API does report when each path became ready
   (`readyTime` in `GET /v3/paths/get/{path}`), and this was verified on the pinned
   `bluenviron/mediamtx:1.21.0-ffmpeg` on 2026-09-29.

The request that prompted spec 289 is for simulated detections that are **in step with the video**
and that **drive configured reactions**, so that one scenario is one demonstrable story.

## Decision

1. **Detections are clip facts, declared beside the clip.** A clip MAY have a sidecar
   `<clip basename>.cues.json` in the clips directory. It declares the clip's duration and an
   ordered list of *cues*. Each cue is one typed detection at one playback offset: class,
   confidence, and optionally display text, zone, a normalised box, and a track identifier. Every
   asset that plays the clip inherits its cues. The emitted event's device is still that asset's
   camera path, so reactions stay per-tile.
2. **Cues run on the clip's clock.** The simulator reads the path's `readyTime` from camera-sim's
   control API. It emits a cue when `(now − readyTime) mod duration` reaches the cue's offset, and
   it re-reads the anchor before every emission. It emits nothing while the path is not ready. The
   target is **±500 ms** between the annotated frame and the kiosk's reaction. This is a
   demo-fidelity figure, not a §IV measurement.
3. **Sensors keep the billet clock.** ADR-0111's M2 timeline is unchanged. Only cues are
   clip-anchored.
4. **One MQTT event per detection.** The event is published on the existing
   `fab/{fab}/inference/{camera path}` topic with the existing envelope. AEL has no arrays,
   functions or string concatenation, so a list-shaped payload could not be matched and a rule
   could not compose display text. The cue therefore carries display-ready text.
5. **The scenario file declares reactions and variables, and the simulator seeds them** through
   the REST clients and the idempotent create-then-publish pattern it already uses. A reaction is
   a trigger `(source, kind)`, an AEL predicate that the seeder confines to the asset's own device,
   and one action: `HighlightOverlay` or `SetVariableValue`. The action list is open to further
   `RuleAction` variants, such as #2618's wall switch, when they exist. The legacy `Highlight`
   object remains and seeds exactly the rule it seeds today.
6. **Seeding creates once and never reconciles.** A definition changed after first seed does not
   update the running rule, variable or overlay. Who owns a seeded rule that a human has since
   edited in the console is a policy question this ADR does not answer.
7. **The simulator states when a reaction cannot fire.** At load, a reaction whose trigger no
   sensor or cue on its asset emits is reported by name. It is still seeded, because a human or a
   real device can trigger it.
8. **Nothing draws the box.** Detection geometry travels in the payload only. Rendering
   event-driven geometry is a product capability for real detectors. It needs its own spec, its own
   ADR and its own §IV review.

The simulator stays dev-only exactly as ADR-0111 and spec 064 gate it. No `Shared.Contracts`,
Automation, SystemVariables, OverlayDesigner, LayoutComposition or kiosk code changes to enable
this.

## Consequences

**Positive:**
- A scenario reads as one story, and every link in it runs through the production pipeline
  unchanged: detection → ingest → rule → effect → wall.
- Moving to real inference later changes only the *publisher*. A detector that emits the same
  payload on the same topic drives the same rules.
- The link between what the picture shows and what the wall does survives the `runOnDemand`
  restarts, without adding always-on FFmpeg load.

**Negative / costs:**
- Clip annotation is manual content work, and re-cutting a clip invalidates its sidecar.
  `generate-sim-clips.sh` and the sidecars must be kept together. A content test checks that every
  cue lies within its clip.
- The phase anchor sits on another process's clock: the Docker VM's clock against the host's.
  Skew or FFmpeg loop drift degrade sync silently. Spec 289's verification measures both,
  including a one-hour drift check, rather than assuming them.
- The simulator's service account gains `sse.variables.write` and `sse.variables.read`, so two
  integration tests that used it as a principal lacking the variables scope need another one.
- Create-once seeding means an edited scenario needs its seeded objects deleted before a re-seed.
- A new event kind (`ObjectDetected`) enters the system unregistered. When strict mode (#2324)
  lands, the simulator must register the kinds it emits.

## Alternatives Considered

- **Simulator-owned playback** (always-on FFmpeg started by the simulator). This gives an exact
  start phase, but runs 12 streams around the clock on a dev machine and still has to detect
  restarts, so it needs the chosen mechanism anyway. It is kept as the fallback for any path where
  the `readyTime` anchor measurably misses ±500 ms.
- **No sync: set each station's dwell to its clip length.** This is right only until the first
  viewer returns and the clip restarts at 0.
- **Real inference on the clips.** It is possible, since the footage is real, but it brings a model,
  a GPU or slow CPU, and non-determinism into a harness whose value is a repeatable story. It is
  deferred to a "bring a real detector" spec, which would publish the same payload.
- **Cues inline in the scenario file.** Two assets on one clip would repeat the cues, and a re-cut
  clip would silently invalidate annotations spread across several files.
- **Configuring reactions by hand in the console.** This would reverse what the simulator already
  does (§Context 1) and leave the story in documentation.
- **Verifying hand-built rules instead of seeding them** (dry-run each cue). This duplicates the
  authoring without the convenience. Decision 7's load-time check covers its value.
