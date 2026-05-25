# Smart Sentinel Eye

> Professional camera management system for industrial production fabs.
> 24/7 operation. WebRTC streaming. On-prem first, cloud-ready.

Smart Sentinel Eye unifies hundreds of IP cameras across an industrial
plant into a single low-latency video wall, with dynamic overlays driven
by events from MES, SCADA, and other shop-floor systems.

**Status:** pre-implementation. The Q&A foundation and architectural
constitution are in place; first-feature specs are being written.

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
