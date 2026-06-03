# ADR-0111 — Scenario Simulator for realistic dev/demo scenarios

**Status:** Accepted

**Relates to:** ADR-0011/0012 (WebRTC SFU), ADR-0095 (event ingestion / MQTT), ADR-0076 (realtime push → overlays), ADR-0024 (Aspire), constitution §IV (latency budget). **Supersedes** the static camera simulation in #1037.

## Context

We need a dev/demo harness that plays a **realistic industrial scenario** —
a rolling mill (Walzwerk) or production line — end to end, with believable,
**correlated** data and no real hardware. The point is to exercise the whole
pipeline with data that looks real: cameras → SFU, and **sensors → MQTT →
event-ingestion → overlays → kiosk → automation**, against the 800 ms budget
(constitution §IV).

The first camera simulation (#1037) used **static** MediaMTX path config. That
conflicts with two goals: the SFU config should stay clean, and the simulated
streams should be **driven by the camera catalog** (the source of truth). It
also has no notion of an *asset*, so sensor events could never be correlated to
a camera. This ADR supersedes it.

## Decision

A **dev-only Scenario Simulator** organised around **Assets** (stations).

### The spine — the Asset model
An asset is a station on the line with a stable **`key`** (e.g. `station-4`)
that correlates its camera and (later) its sensors. An asset owns: `key`,
`name`, a `camera` (path + which loop clip), and (M2) a `sensors` profile
(kind / unit / behaviour). The shared key is what makes an overlay land on the
right camera tile.

### Scenario definition
**JSON bound to the worker's config** (`IOptions`; appsettings + scenario
files), the active one selected by an env var. A scenario is a list of assets;
"rolling-mill" vs "loading-bay" is just a different file. **One source** feeds
both camera seeding and (M2) sensor events.

### ScenarioSimulator — a new dev-only worker (`src/ScenarioSimulator`)
Gated `isRunMode && !isE2ETests` (off under E2ETests/CI/prod — zero impact).

- **M1 — Camera (implemented now).** Reads the scenario; registers each asset's
  camera in camera-catalog with `RtspUrl = rtsp://camera-sim:8554/<path>`;
  subscribes to `CameraRegisteredV1`; provisions a **looping-video** source per
  camera on **`camera-sim`** — a *second, config-clean* MediaMTX that loops a
  generated clip via `runOnDemand`. The **main `mediamtx.yml` reverts to clean**
  (no static virtual config; sources are synced from the catalog).
- **M2 — Sensor / events (designed, not coded).** Per asset's sensor profile,
  the *same* worker publishes MQTT as a simulated PLC / inference device
  (event-ingestion's per-device topic convention, alongside the existing
  `station-4` / `camera-12` seeds), on a timeline → event-ingestion →
  integration events → overlays → the kiosk tile. Correlated by **asset key**.
  A stubbed extension point in the worker now.

**Boundary:** M1 and M2 share the asset identity and the scenario file. M1 ships
first; M2 plugs into the same asset loop with no rework.

## Consequences

**Positive:** realistic, *correlated* end-to-end demo/test data; overlays land
on the right tile because camera and sensors share an asset key; M1 now and M2
later without rework; the SFU config stays clean (streams synced from the
catalog, the stated requirement).

**Negative / cost:** a new dev-only worker project + a second RTSP server + a
generated loop clip + a scenario format. The worker must stay in sync with the
catalog (it subscribes to `CameraRegisteredV1`). All dev-only, so prod/CI are
untouched.

## Alternatives considered

- **Static per-camera MediaMTX config (#1037) — rejected.** No static virtual
  config; the streams must be driven by the catalog.
- **Generic loop server, no asset model — rejected.** Simpler, but sensors
  could never correlate to a specific camera, which is the whole point of M2.
- **Scenario seeding inside camera-catalog — rejected.** Splits the scenario
  across two homes; a standalone worker owns the whole scenario end to end.
