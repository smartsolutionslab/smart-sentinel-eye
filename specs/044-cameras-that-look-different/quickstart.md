# Quickstart: three plants that look like three plants

**Feature**: 044 | **Date**: 2026-08-26

Everything automated in this feature proves each camera is **pointed at a
different file**. Whether the walls *look* like different plants is a person
looking at them, because no check in this repo can see a picture — spec 043
established that and this feature inherits it.

---

## 1. Boot the stack

```sh
dotnet run --project src/AppHost
```

`camera-sim` and `scenario-simulator` only run under `aspire run`
(`isRunMode && !isE2ETests`), so this cannot be done from CI or an e2e run.

No realm change is involved, so the Keycloak volume can stay.

Wait for `scenario-simulator` to report its seeding complete, then confirm
`camera-sim`'s path list holds one path per asset across **all three** scenarios,
each `ready: true`.

---

## 2. Look at each wall

Sign in at `http://localhost:5173` as `operator` / `Operator1234`, open Layouts,
and open each of the three seeded walls in turn.

**Expected**: three plants. Hot steel, a paper mill, an electronics line.

**Record, per wall**:

- do all four tiles show *different* footage? (US1 / SC-001)
- can you name which asset a tile is **without** consulting the layout? (SC-002)
- could any tile be mistaken for a tile from a different plant? (US2 sc.3)

The middle question is the one that fails quietly. Four different clips of the
same machine from four angles satisfy SC-001 and still leave an operator unable
to tell tiles apart, which is the actual complaint this feature exists to fix.

---

## 3. Only one plant animates — confirm that, do not report it

The billet timeline and its MQTT sensors run for the **first** active scenario
only. The other two walls show live video with static overlays and no
highlights.

**This is deliberate** (plan §2). Three concurrent timelines is a different
feature. It is written here so nobody spends an afternoon debugging two walls
that are behaving exactly as designed.

Confirm the animated wall still highlights on threshold as it did before —
that is FR-011, and it is the thing most likely to have been broken by the
`Active`-becomes-a-list change.

---

## 4. A camera with no scenario

Register a camera by hand (any name, `rtsp://camera-sim:8554/anything`) and open
it.

**Expected**: it streams, with its own name burnt into the picture and a colour
shift distinct from its neighbours. Register a second **at a different path** —
say `rtsp://camera-sim:8554/anything-else` — and open both; they must not look
the same.

**The second URL must differ, and that is not a detail** (corrected 2026-08-27
at T023). The SFU pulls each `cam-<identifier>` from the address the operator
entered, so two cameras registered at the *same* simulator path are one source
and must show one picture. This step originally said to register the second at
the same URL, which is the one case the guarantee cannot cover: what actually
happens is that the second registration silently rewrites the first camera's
label and hue. The per-camera guarantee holds **per simulator path**.

**Note what this costs**: a labelled stream is re-encoded rather than copied.
If the dev box's fans are audible here, that is the FR-010 assumption showing,
not a bug.

---

## 5. Prove the two new failures fire

While the stack is up:

1. Point an asset at a clip that does not exist and restart the simulator. **It
   must fail at startup naming the asset and the clip** — not provision a path
   that never becomes ready.
2. Change an asset's clip to a different real file and restart. **The picture
   must change.** If it does not, `ProvisionLoopPathAsync` is still treating
   "already exists" as success, which is exactly the bug FR-008 names.

The second is worth doing deliberately. Its failure mode is silence: no error,
no log, the old picture. Nothing else in the suite catches it.

---

## What to write down

- Per wall: four different clips, yes or no; tiles nameable without the layout,
  yes or no.
- Whether any tile from one plant could pass for another plant's.
- That the animated wall still highlights.
- That two hand-registered cameras look different from each other.
- Both failures from §5, and which one did **not** fire if either did not.

If a step was not performed, **say which**. A wall of four near-identical shots
passes every automated check in this feature.
