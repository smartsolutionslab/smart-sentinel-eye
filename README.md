# Smart Sentinel Eye

> Professional camera management system for industrial production fabs.
> 24/7 operation. WebRTC streaming. On-prem first, cloud-ready.

Smart Sentinel Eye unifies hundreds of IP cameras across an industrial
plant into a single low-latency video wall, with dynamic overlays driven
by events from MES, SCADA, and other shop-floor systems.

**Status:** specs `001-register-camera` and `002-watch-camera-live`
shipped end-to-end. Admins can register a camera through the
management UI, see it in the list with a live stream-health badge,
click **Watch** to view a low-latency WebRTC stream, and observe
state transitions (Healthy / Degraded / Offline) in real time.

## What it does

- Ingests RTSP / ONVIF streams from up to 250 industrial IP cameras.
- Distributes them as WebRTC to operator browsers and control-room
  video walls (frame-accurate sync via PTP).
- Composites dynamic overlays on top of live streams, driven by
  registered external events and typed system variables.
- Lets operators steer cells, switch cameras, and control PTZ from a
  browser; lets admins author layouts, overlays, and automation rules
  through a draft → preview → publish workflow.
- Runs entirely on-prem per fab; a cloud control plane is the v2
  competitive feature.

## Architecture at a glance

| Layer | Choice |
|---|---|
| Frontend | React + TypeScript + Vite |
| Backend | .NET 10 + ASP.NET Core + **.NET Aspire** |
| Persistence | PostgreSQL (+ Marten for event-sourced contexts) |
| Object store | MinIO |
| Messaging | RabbitMQ |
| Identity | Keycloak (OIDC) per fab |
| Streaming | WebRTC SFU; passthrough + GPU transcode fallback |
| Time sync | PTP (IEEE 1588) per fab |
| Observability | OpenTelemetry → Aspire dashboard + Grafana stack |
| Orchestration | Aspire AppHost (dev) → k3s + Helm (prod) |

## Quickstart — register your first camera

Prerequisites: .NET 10 SDK, Docker Desktop running, Node 20+, pnpm.

```pwsh
# 1. Restore + build
dotnet restore
pnpm install

# 2. Start the full stack (Postgres, RabbitMQ, Keycloak, the
#    camera-catalog API, and both React apps). The Aspire dashboard
#    URL is printed on startup.
dotnet run --project src/AppHost
```

In the Aspire dashboard, wait for `migrations` to reach **Finished**
and `camera-catalog` + `keycloak` + the React apps to reach
**Running**. Then:

1. Open the **management-web** URL from the dashboard (default
   `http://localhost:5173`).
2. Sign in with the seeded admin (Keycloak realm
   `smart-sentinel-eye`): **`admin` / `Admin1234`**.
3. Click **Register camera**, give it a unique name, paste an
   `rtsp://...` URL, submit.
4. The new row appears in the list. Open the **rabbitmq** management
   UI from the dashboard and inspect the
   `camera-catalog.SmartSentinelEye.Shared.Contracts.CameraCatalog.CameraRegisteredV1`
   queue — the integration event is sitting there waiting for the
   first subscriber.

## Quickstart — watch a camera

Once a camera is registered (above), the **Stream** column in the
cameras list polls `/streams` every five seconds and renders a
coloured pill: **Healthy** (green), **Degraded** (yellow),
**Offline** (red), **Provisioning** (grey). Hover the pill for the
last successful frame time and any error message.

1. Wait for the **Stream** column to settle. With a reachable RTSP
   source the badge moves Provisioning → Healthy within ~5 s; with
   an unreachable source it transitions to Degraded.
2. Click the **Watch** button on the row to open the viewer panel.
   A WebRTC peer connection is negotiated through MediaMTX via WHEP
   and a live frame appears within ~3 s (warm cache).
3. Disconnect the camera (pull the cable / stop the RTSP source).
   Within ~10 s the badge flips to **Degraded** and the viewer panel
   shows a "Reconnecting…" overlay. Restore the source and watch the
   badge return to **Healthy**.

## Tests

```pwsh
# Unit + architecture tests (fast, no Docker)
dotnet test --filter "FullyQualifiedName!~Integration"

# Integration tests (slow, requires Docker)
dotnet test tests/Integration.Tests/

# Coverage gate per ADR-0065 (also runs all unit tests)
pwsh scripts/coverage-check.ps1
```

Latency budget enforcement (constitution §IV) ships with the
integration suite: `CommandLatencyTests.POST_cameras_p95_stays_within_the_command_path_budget`
asserts `POST /cameras` p95 ≤ 200 ms across 100 sequential calls.

## Documents

- **[.specify/memory/constitution.md](.specify/memory/constitution.md)** —
  principles, locked stack, NFRs, governance.
- **[docs/adr/](docs/adr/)** — every architectural decision with
  reasoning. Start with `0000-initial-decisions.md`.
- **[CLAUDE.md](CLAUDE.md)** — orientation for Claude Code sessions.
- **[specs/](specs/)** — per-feature specs (Spec-Kit), populated as
  features are designed.

## Workflow

This project follows
[Spec-Kit](https://github.com/github/spec-kit) for spec-driven
development:

```
/speckit-constitution   amend principles (rare, requires ADR)
/speckit-specify        write a feature spec
/speckit-clarify        de-risk ambiguities (optional)
/speckit-plan           propose technical approach
/speckit-tasks          break plan into ordered atomic tasks
/speckit-implement      execute tasks
```

Every PR traces back to at least one task ID. Every spec references at
least one ADR. The latency SLO (≤ 800 ms event-to-overlay) is enforced
on every PR that touches the relevant path.

## License

TBD.
