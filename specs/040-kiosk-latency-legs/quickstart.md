# Quickstart: reading two numbers CI cannot produce

**Feature**: `040-kiosk-latency-legs` · **Plan**: [plan.md](./plan.md)

**This procedure is not optional and not a convenience.** CI has no video
(research §2), so the two figures this feature exists to produce can only be seen
by a person, here. Everything automated proves the guards and the plumbing; none
of it proves a number came from a frame.

---

## Boot — run mode, not e2e

```sh
dotnet run --project src/AppHost
```

**`dotnet run`, specifically.** `camera-sim`, `scenario-simulator` and the ICE
host-publishing are all inside `if (isRunMode && !isE2ETests)`. Under the e2e
profile there is no simulated camera and no route for media to reach a browser,
so a kiosk there shows no picture and neither leg exists to measure.

Wait for `scenario-simulator` to finish seeding — it registers cameras and
provisions a looping clip per camera on `camera-sim`.

---

## See the picture first

1. Open **management-web** (<http://localhost:5173>) and confirm a camera shows
   live video. If it does not, nothing below will work and the problem is the
   stack, not the measurement.
2. Publish a layout with at least **two tiles** on different cameras — two,
   because the figures are per-tile and one tile cannot show that.
3. Open the **kiosk** (<http://localhost:5174>) on that layout. Both tiles should
   show moving video with their overlays drawn on top.

**That screen is the thing four documents said did not exist.** Worth looking at
before measuring it.

---

## Read the two numbers

**In the Aspire dashboard** — the sink for this environment (ADR-0118). Find the
metrics for `stream-distribution` and look for:

- `kiosk.overlay_draw` — should be single-digit to low-tens of milliseconds
  against its 50 ms budget.
- `kiosk.receive_to_decoded` — **has no budget attached**, deliberately. It is a
  fragment of the decode leg, not the leg.

Both carry the camera as a dimension. Confirm you can tell the two tiles apart,
because per-tile is the whole point: a wall average hides one frozen camera among
three good ones.

**In the browser console**, the structured `[resilience]`-style line for each
measurement. This is the same information, and it is *not* how §VII is
discharged — reading it needs devtools attached, which is the "attach a debugger"
FR-010 rules out. It is here because it is free and it makes this procedure
easier.

---

## Provoke the guards

- **Cover a camera / stop its clip.** The stream drops. Confirm **no** figure is
  recorded for the gap — not a zero (**FR-008**).
- **Background the kiosk tab** for ten seconds and return. Confirm no figure
  spanning the gap is recorded (**FR-009**): browsers throttle background work,
  and a figure measured across that describes the throttling.
- **Reconnect.** Confirm the recovery is timed as a new journey, not as a
  continuation of the interrupted one.

---

## Verification note for the PR

State each with what was observed:

- **The correction landed in all four documents**, and they agree. Show it —
  §IV's table, `CLAUDE.md`, spec 024's `verification.md` §6, and the comment on
  the issue.
- **§IV distinguishes four states** across six legs: watched, in part,
  recorded-not-readable, unbuilt. Say which leg is which. **SC-007** exists to
  stop any of them being rounded up.
- **Both numbers were read from the dashboard**, with their values, on a
  two-tile wall, per tile. Not "the metric is emitted" — the number, and where it
  was read.
- **The decode figure carries no budget**, and its name does not claim the leg.
- **The guards were provoked**, not merely unit-tested: what was done, and that
  nothing was recorded.
- **The kiosk behaves as before** (**FR-011**): same picture, same overlay, same
  reconnection.
- **What is automated and what is not.** Say plainly that CI proves the guards and
  the plumbing and cannot prove either number, and that these claims rest on this
  procedure. A green suite that never saw a frame is the same class of claim as a
  document saying a leg is unbuilt when it runs on every kiosk — which is what
  this feature is fixing.
