# Tasks: Simulated cameras that look like different cameras

**Feature**: `044-cameras-that-look-different` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)

**23 tasks across six phases.** The code is one field, one argument and one
config shape. The rest is eight clips, two scenario files, and two failures that
do not exist yet.

**The clips are the work, and they are not a copy job.** Four clips per plant
have to look different *at 20 seconds and at tile size*. Nothing automates that
judgement, and getting it wrong produces three walls of four near-identical shots
that pass every check in this feature.

**Nothing here proves a picture appears.** The suite proves each camera is
pointed at a different file. That the files *look* different is T023, and T023 is
a person. This is the second feature running to carry that row.

---

## Do not

- **Do not touch `CameraViewer`, `useWhepSession` or `WhepClient`.** Nothing in
  `apps/` changes. This feature ends at the RTSP source.
- **Do not add a clip-resolution abstraction.** A scenario names a file name; the
  provisioner passes it through. No strategy, no registry, no plugin point.
- **Do not put container paths in a scenario file.** `Clip` is a bare file name —
  the mount location is the AppHost's business, and a scenario coupled to the
  compose topology is a scenario that breaks when the mount moves.
- **Do not run three timelines.** Seed three plants, animate the first. Plan §2.
- **Do not refactor `CameraSimProvisioner`** beyond the clip argument and the
  FR-008 fix. It works.
- **Do not trust Commons' `extmetadata` for licences.** It reports the packaging
  clip as CC-BY 4.0; its file page says CC BY 3.0. Read the page.
- **Do not commit a clip without its attribution file** in the same commit. A
  binary that arrives unattributed and gets one later is unattributed in history.
- **Do not write `#1886`-style bare issue numbers** in committed docs — the
  automation closes a merely-mentioned issue on merge.

---

## Phase 1: An asset names its clip

**Goal**: the mechanism. After this phase two assets can differ, even though no
new clip exists yet.

- [x] T001 In `src/ScenarioSimulator/Scenario/ScenarioOptions.cs`, add `Clip` to `CameraDefinition` — a bare file name, defaulting to `sim-loop.mp4` so an unedited `rolling-mill.json` behaves exactly as today (FR-011).
- [x] T002 In `src/ScenarioSimulator/CameraSim/CameraSimProvisioner.cs`, replace the hard-coded `RunOnDemandCommand` constant with a builder taking the clip. Keep `-c copy` for an unlabelled clip: it is why FFmpeg does no work per stream.
- [x] T003 [P] In `tests/…/ScenarioSimulator.Tests/`, assert the provisioned command contains the asset's clip and not `sim-loop.mp4` when a clip is named. **This is the test that fails if someone restores the constant** — the only automated guard on US1's mechanism.
- [x] T004 [P] Assert no two assets within one scenario share a clip. Cheap, and it catches the copy-paste that produces a wall of identical tiles.

**Checkpoint**: `rolling-mill` still behaves as it does today, and a clip can be named.

---

## Phase 2: Three plants, one animated

**Goal**: three walls exist and can be compared. Sequential — all four touch the
same seeding path.

- [x] T005 [US2] In `ScenarioOptions.cs`, `Active` becomes a list. Keep binding a single string as a one-element list if that is free; `appsettings.json` currently says `"Active": "rolling-mill"` and `ScenarioSimulator__Active` is set nowhere, so nothing external breaks.
- [x] T006 [US2] In `CameraSim/CameraSimReconciler.cs`, provision every active scenario's assets rather than `Scenarios[Active]`. It currently logs `ScenarioNotFound` and returns — keep that per scenario, so one bad key does not silently drop the other two.
- [x] T007 [US2] In the seeding pass, seed cameras, overlays, rules and walls for every active scenario. Idempotency already works — the log shows `already registered; skipping (idempotent)` on 409 — so a re-run must stay quiet.
- [x] T008 [US2] Run the billet timeline for the **first** active scenario only, and say why in a comment. Three concurrent timelines is a different feature (plan §2); an unexplained single-plant animation reads as a bug.
- [x] T009 [P] [US2] Test: three scenarios configured → three walls seeded, one timeline started. Asserting the *count* of started timelines is what stops T008 being quietly "fixed" later.

**Checkpoint**: three walls, three sets of cameras. One animates.

---

## Phase 3: The clips

**Goal**: eight clips in the repo, each licensed and attributed.

- [x] T010 Generalise `scripts/generate-sim-loop.sh` into `scripts/generate-sim-clips.sh`: a table of `(source URL, offset, output name)`, same mechanics (curl from Commons, 20 s excerpt at 1280×720 H.264 through the `bluenviron/mediamtx:latest-ffmpeg` image, because the host has no FFmpeg). Keep the old script working or delete it outright — do not leave two that disagree.
- [x] T011 [US2] **Watch the seven Goričane clips and pick four.** Listed in [spec.md](./spec.md) §Clips. The criterion is not subject but *legibility at tile size*: a 20 s excerpt that is a static shot of a pipe is useless. Record which four and why, and which you rejected.
- [x] T012 [US2] **Watch the nine Gigaset clips and pick four**, same criterion. These are 1280×720 against Goričane's 1920×1080, which matches the existing `sim-loop.mp4` exactly, so no rescale is needed.
- [x] T013 Generate the eight clips and commit them under `src/AppHost/Resources/clips/`. **~40 MB, and git keeps it** — if that is judged too much, share clips between assets *before* this commit, not after.
- [x] T014 One `*.ATTRIBUTION.txt` per clip, following `sim-loop.ATTRIBUTION.txt`'s shape: title, author, source URL, licence with its URL, and the note that the excerpt is a derivative under the same licence. All eight are CC BY 3.0 — **attribution only, no share-alike**, unlike the existing clip.
- [x] T015 [P] Fix `src/AppHost/Resources/README.md`. It describes `sim-loop.mp4` as "a `testsrc2` moving pattern … plus a blue box that scrolls" — it is not, and has not been since the clip became a rolling-mill excerpt. `sim-loop.ATTRIBUTION.txt` and `generate-sim-loop.sh` both already say so; only the README is wrong. Found while planning this feature.

**Checkpoint**: eight attributed clips on disk.

---

## Phase 4: The two new scenarios

**Goal**: two plants that are plants, not two copies of the mill's shape.

- [x] T016 [P] [US2] `src/ScenarioSimulator/Scenarios/paper-mill.json` — four assets, each naming its clip, with overlays, a 2×2 wall, and **sensors that suit a paper mill** (consistency, basis weight, moisture, line speed). Reusing the mill's Temperature/RollingForce would prove the file takes another entry, not that the simulator supports another kind of plant.
- [x] T017 [P] [US2] `src/ScenarioSimulator/Scenarios/electronics.json` — same shape, sensors that count and rate (placement rate, reject count, cycle time, conveyor throughput). This is the plant furthest from hot steel and carries the contrast US2 is for.
- [x] T018 [US2] Register both in the active list, and confirm all three seed together from a cold database.

**Checkpoint**: US2 complete.

---

## Phase 5: Bulk cameras, and the two failures

- [x] T019 [US3] In `EventHandlers/CameraRegisteredSimHandler.cs`, a camera resolving to no scenario asset provisions the shared clip with `drawtext` (its name) and `hue` (shift derived from its identifier). This branch **loses `-c copy`** — note that in the code, since it is where FR-010's ~20 stops being an abstraction.
- [x] T020 [US1] **FR-007**: validate an asset's clip exists before provisioning; fail naming the asset *and* the clip. Today a missing clip provisions a path that never becomes ready, which looks exactly like a broken camera — the failure has to happen where the cause is.
- [x] T021 [US1] **FR-008**: a changed clip must take effect. `ProvisionLoopPathAsync` treats a 400 containing `already exists` as success and returns — right for an unchanged path, wrong for a changed one. Use `/v3/config/paths/replace/<path>`, or compare and re-add. **Its failure mode is silence**: no error, the old picture, and an hour looking in the wrong place.
- [x] T022 [P] Tests for T020 and T021: a missing clip throws naming both; a changed clip issues a replace rather than a swallowed add.

**Checkpoint**: the silent failures are gone.

---

## Phase 6: The part no machine can do

- [ ] T023 Follow [quickstart.md](./quickstart.md) against `dotnet run --project src/AppHost`. Per wall: are all four tiles different, and can you name a tile's asset **without** the layout? Confirm no tile from one plant could pass for another's. Confirm the animated wall still highlights (FR-011). Register two cameras by hand and confirm they differ. Then cause both failures from quickstart §5 and record which did **not** fire. Name any step not performed.

---

## Dependencies

```
T001 ─▶ T002 ─▶ T003, T004          (the seam, then its guards)
          │
          ├─▶ T005 ─▶ T006 ─▶ T007 ─▶ T008 ─▶ T009    (one seeding path, sequential)
          │
          ├─▶ T010 ─▶ T011, T012 ─▶ T013 ─▶ T014      (clips: script, choose, generate, attribute)
          │                                    │
          │                                    ▼
          └────────────────────────────▶ T016, T017 ─▶ T018
                                                          │
          T019, T020, T021 ─▶ T022 ───────────────────────┤
                                                          ▼
                                                        T023
```

**T002 before everything.** Until the provisioner takes a clip, no scenario file
can name one and no test can assert one.

**T013 before T016/T017.** A scenario naming a clip that is not yet committed
fails T020's new validation — correctly, and confusingly, while the clip is
merely late.

**T011/T012 before T013.** Generating eight clips nobody has watched is how the
near-identical-wall failure happens.

## Parallel opportunities

- **T003 and T004** — two tests on one method.
- **T011 and T012** — two clip sets, two people could watch them at once.
- **T016 and T017** — two scenario files, no shared lines.
- **T015** — the README fix touches nothing else in this feature.
- **Phase 2 is NOT parallel**: T005–T008 are one seeding path.

## Implementation strategy

**MVP is T002 plus one edited line of `rolling-mill.json`.** The moment an asset
can name its own clip, the mill's four tiles can differ — which is US1, the whole
complaint, before any new plant exists.

**Do Phase 1 as one commit.** The field, the argument and the two tests only make
sense together.

**Do not batch the clips.** One commit per scenario's clips *with* their
attribution files, so a binary is never in history unattributed.

**Budget real time for T011/T012.** Watching sixteen clips and choosing eight is
an hour, and it is the hour that decides whether this feature worked.

---

## Three things most likely to go wrong

1. **Three walls of four near-identical shots.** Every check passes: eight
   distinct files, eight distinct commands, three walls seeded. And an operator
   still cannot tell tiles apart, which is the complaint that started this. Only
   T011/T012 and T023 prevent it, and all three are judgement rather than code.

2. **FR-008 gets skipped.** It is the least visible task here and the most
   expensive to omit: editing a scenario's clip appears to do nothing, with no
   error, and the natural conclusion is that the scenario file is not being read
   at all. Someone will then go looking in the config binding.

3. **The re-encode is discovered rather than decided.** Bulk cameras lose
   `-c copy` at T019. At ~20 cameras that is fine and FR-010 says so — but if
   someone points 250 cameras at this later, the dev box is where they find out.
   T019's comment is what turns that into a decision someone made.

---

## What the automated suite does and does not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| An asset's clip reaches the FFmpeg command | T003 | — |
| No two assets in a scenario share a clip | T004 | — |
| Three plants seed; one animates | T009 | — |
| A missing clip fails where it is caused | T022 | — |
| A changed clip takes effect | T022 | — |
| Every clip is attributed | T014, by inspection | any test |
| **The three walls look like three plants** | **T023 — a person** | everything above |

The last row is the honest one. Point all eight assets at the same file and every
row above it stays green.

---

## T011 / T012 result — which clips, and why

Chosen by extracting three frames from each generated clip and looking at them.
The criterion was legibility at tile size, not subject matter.

**Rolling mill** — four offsets into the one 477 s Acroni source (CC BY-SA 3.0):

| Clip | Offset | Verdict |
|---|---|---|
| `mill-roughing` | 60 s | Wide, bright mill floor with DANIELI machinery. Strong. |
| `mill-finishing` | 150 s | Glowing plate under bright light. Strong. Moved off 250 s, which was byte-identical to `sim-loop`. |
| `mill-cooling` | 360 s | Glowing slab on a table, dark surround. Good. |
| `mill-coiler` | 440 s | Dark; yellow railings carry it. **Weakest of the four.** |

**Paper mill** — Goričane, CC BY 3.0:

| Clip | Verdict |
|---|---|
| `paper-packaging` | Pallets, blue conveyor, forklift, people. **Best of all twelve.** |
| `paper-after-drying` | Operators at a control desk, coloured lamps, constant motion. Strong. |
| `paper-press-group` | Yellow and purple machinery, unmistakable palette. Good. |
| `paper-refiners` | Pale grey pulp. Near-static, low contrast, **reads as a blur at tile size**. |

**Electronics** — Gigaset, CC BY 3.0:

| Clip | Verdict |
|---|---|
| `electronics-inspection` | Cobot arms, blue LEDs, a human hand. Strong, and the furthest thing from hot steel in the set. |
| `electronics-moulding` | ENGEL machine, safety pictograms, green trim. Good. |
| `electronics-smd-line` | Seen through glass, greenish. Adequate. 16.5 s — its source is short. |
| `electronics-conveyor` | Overhead conveyor with a mirrored dome. Near-static, but the dome identifies it instantly. |

**Rejected**, unwatched, available if a tile is judged unusable: Goričane *Broke
chest*, *pressure screener*, *vacuum pumps*; Gigaset *In Mould Decoration I/II*,
*Mainboard and Microphone*, *Screwing the backs*, *Attaching the Label*.

**`paper-refiners` is kept on a reservation.** It is genuinely distinct from its
three siblings, so SC-001 holds and the automated checks pass. Whether it is
distinct *enough* for SC-002 — naming the asset from the tile alone — is a
judgement only T023 can settle, on a real wall. If it fails there, the three
Goričane alternates above are the replacements and the change is one line in
`paper-mill.json` plus one clip.
