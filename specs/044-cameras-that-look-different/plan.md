# Implementation Plan: Simulated cameras that look like different cameras

**Feature**: `044-cameras-that-look-different` · **Spec**: [spec.md](./spec.md)

**Input**: "we need more different simulation cameras streams more realistic"

---

## Summary

One hard-coded string is the whole defect. `CameraSimProvisioner` provisions
every `camera-sim` path with the same command, so every simulated camera plays
`/media/sim-loop.mp4` and a 2×2 wall shows one clip four times.

The fix is to let an asset name its own clip and to add two more plants. The
code change is small and concentrated; the substance is **eight licensed clips,
two scenario files, and a config shape that stops assuming one plant**.

**Scale/Scope**: one method signature, one options type, one seeding pass, two
new scenario JSONs, eight new clips (~40 MB), one generalised generate script,
three attribution files. No `apps/` change. No product code change.

## Technical Context

**Language**: C# 13 / .NET 10 (ScenarioSimulator worker), bash (generate script),
JSON (scenario files)

**Primary Dependencies**: MediaMTX v3 config API, FFmpeg via the
`bluenviron/mediamtx:latest-ffmpeg` image, Wolverine (`CameraRegisteredV1`)

**Testing**: xUnit + Shouldly for the options/provisioner units. **No integration
test**: `camera-sim` and `scenario-simulator` sit inside
`if (isRunMode && !isE2ETests)`, so CI never boots either.

**Scale**: ~20 simulated cameras (FR-010), not 250

**Constraint**: the host has no FFmpeg. Everything that touches video runs in the
MediaMTX image, as `scripts/generate-sim-loop.sh` already does.

## Constitution Check

| Principle | How this complies |
|---|---|
| **§IV latency budget** | Untouched. Dev-only tooling; no leg affected, nothing on the event-to-overlay path. |
| **§VII dashboards** | N/A — adds no measurement and claims none. |
| **Karpathy: smallest change** (ADR-0036) | The seam is `Camera { Path, Clip }` and one provisioner argument. Everything else is content. The provisioner is *not* refactored, and `useWhepSession`/`CameraViewer` are not touched. |
| **No speculative generality** | No clip-resolution abstraction, no plugin points. A scenario file names a file; the provisioner passes it through. |
| **No drive-by error handling** | One new failure is introduced deliberately (FR-007) at the seeding boundary, which is a trust boundary for operator-authored config. |
| **ADR-0111** | The scenario file stays the durable source of truth and camera-sim paths stay runtime state. This plan does not move that line. |

## Project Structure

### Documentation (this feature)

```
specs/044-cameras-that-look-different/
  spec.md          # done
  plan.md          # this file
  tasks.md         # next
  quickstart.md    # the by-eye verification, which is the only real one
```

No `data-model.md` — nothing persists. No `contracts/` — no API changes.

### Source Code (repository root)

```
src/ScenarioSimulator/
  Scenario/ScenarioOptions.cs        # CameraDefinition gains Clip; Active becomes plural
  CameraSim/CameraSimProvisioner.cs  # takes a clip; stops swallowing a changed source
  CameraSim/CameraSimReconciler.cs   # walks every seeded scenario, not just one
  EventHandlers/CameraRegisteredSimHandler.cs  # bulk cameras get the labelled variant
  Scenarios/paper-mill.json          # new
  Scenarios/electronics.json         # new
src/AppHost/Resources/
  clips/*.mp4                        # new, ~8 files
  clips/*.ATTRIBUTION.txt            # new, one per clip
scripts/generate-sim-clips.sh        # generalises generate-sim-loop.sh
tests/…/ScenarioSimulator.Tests/     # options binding + provisioner command shape
```

## Approach

### 1. An asset names its clip

`CameraDefinition` gains `Clip`. `ProvisionLoopPathAsync(path, clip, …)` builds
the FFmpeg command from it instead of a constant. That is the entire mechanism
for US1 — everything else in this feature is content poured through it.

`Clip` is a **file name**, not a path: the clips directory is a bind mount whose
location is the AppHost's business, and a scenario file that knew container paths
would be coupled to the compose topology.

### 2. Three plants, seeded together — but only one animated

`ScenarioOptions.Active` is a single string, and `CameraSimReconciler` and the
seeder both read exactly one scenario. US2 wants three walls that can be compared
side by side, so `Active` becomes a list.

**Seed all three; run the timeline for the first.** The billet timeline and its
MQTT sensor emission are per-plant and stateful; running three concurrently is a
different feature and would put M2's cadence under test for no gain here. Three
scenarios' cameras, overlays, rules and walls all exist and are all watchable —
the sensors animate on one.

This is a deliberate partial, and `quickstart.md` must state it so nobody reads
three static walls as a bug.

### 3. A bulk camera gets the labelled variant

A camera with no scenario asset — hand-registered, or one an e2e run leaves
behind — provisions the shared clip with `drawtext` (its name) and `hue` (a shift
derived from its identifier). `CameraRegisteredSimHandler` already resolves the
asset, so the fallback branch is where this lands.

This forces a re-encode, which is why FR-010's ~20 matters (§Risks).

### 4. The clips

`scripts/generate-sim-loop.sh` becomes `generate-sim-clips.sh` over a table of
`(source URL, offset, output name)`. Same mechanics: curl from Commons, excerpt
20 s at 1280×720 H.264 through the MediaMTX image, commit the result.

Sixteen candidates are identified in the spec, all CC BY 3.0. **Selection of four
per scenario happens here**, by watching them — a clip whose 20 s excerpt happens
to be a static shot is useless for telling tiles apart, and only looking reveals
that.

One attribution file per clip, following `sim-loop.ATTRIBUTION.txt`. Read the
**file page**, not the `extmetadata` API, which disagrees with it.

### 5. The two failures worth introducing

**FR-007 — a missing clip fails at seed time.** Today an asset naming a
non-existent file provisions a path that never becomes ready, which is
indistinguishable from a broken camera. The reconciler validates the clip exists
before provisioning and names both asset and clip when it does not.

**FR-008 — a changed source takes effect.** `ProvisionLoopPathAsync` treats a 400
containing "already exists" as success and returns. That is right for an
unchanged path and wrong for a changed one: edit a scenario's clip, restart, and
nothing happens. Replace the path (`/v3/config/paths/replace/<path>`) rather than
add-and-shrug, or compare and re-add.

The second is the one most likely to be skipped and most likely to waste an hour
later, because the symptom is "my change did nothing" with no error anywhere.

## What must fail

| Break this | Expected |
|---|---|
| Give two assets the same clip | unit test red — no two assets in a scenario share a clip |
| Point an asset at a missing clip | reconciler fails at startup, naming asset and clip |
| Change an asset's clip and restart | the stream changes; if it cannot, it says so |
| Revert the provisioner to the constant | unit test red — the command must contain the asset's clip |
| Delete a scenario file | the other two still seed |
| **Make every clip visually identical** | **everything stays green** — see below |

The last row is the honest one, and it is the same shape spec 043 ended on. No
check here can see a picture. The suite proves each camera is *pointed at a
different file*; that the files *look* different is a person.

## Risks

**The re-encode.** `-c copy` exists so FFmpeg does no work per stream. A burnt-in
label retires it for bulk cameras. At ~20 cameras that is affordable and FR-010
says so — but the number is load-bearing, and if anyone later runs 250 on a dev
box this is where it hurts. Scenario assets keep `-c copy` where their clip needs
no label.

**Repo weight.** ~40 MB of clips against a repo holding 5.5 MB today. Irreversible
in practice once committed — git keeps them. Fewer, shared clips is the lever if
that is judged too much, and the choice should be made before the commit, not
after.

**Clip selection is a judgement, not a task.** Four clips per scenario have to
*look* different at 20 s and at tile size. That cannot be delegated to a rule, and
budgeting it as "copy eight files" is how this ends up with three walls that are
each four near-identical shots of the same machine.

**Scope creep toward load realism.** Three plants, twenty streams and per-camera
encoding all sit next to "make it handle 250". They were ruled out; the plan
should stay ruled out.

## Out of scope

- Load realism: varied resolution/bitrate, 250 concurrent streams.
- Failure injection: cameras that drop, degrade or reconnect.
- Running three timelines at once (see §2 — deliberate).
- Anything in `apps/`.
- The `Audit …` / `Attribution …` cameras left by integration tests — a different
  residue source from the e2e one already fixed.
