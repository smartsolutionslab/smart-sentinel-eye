# Smart Sentinel Eye Constitution

> Professional camera management system for industrial production fabs.
> Runs 24/7. On-prem first, cloud-ready.

This constitution captures the non-negotiable principles and constraints for
Smart Sentinel Eye. Every spec, plan, task, and pull request must be
consistent with it. Amendments require an explicit ADR entry in
`docs/adr/` and a version bump below.

---

## Core Principles

### I. On-Prem First, Cloud-Ready (ADR-006)

Every service ships in a configuration that runs fully self-contained inside
a single fab. The system must be operable with **no outbound internet
dependency** — no SaaS auth, no cloud telemetry, no cloud DB.

The cloud control plane is a **v2 additive layer**. Any v1 design that
would require the cloud to function — even for setup, license check, or
configuration — is rejected.

**How to apply:** every config-bearing service uses idempotent IDs (ULIDs)
and versioned config writes. Domain events are durable. A future cloud-sync
layer is therefore additive, not a rewrite. Configuration that is identical
across fabs lives in source-of-truth templates; per-fab divergence is
explicit.

### II. Domain-Driven Design with Value Objects (ADR-001)

The system is modelled as bounded contexts with explicit ubiquitous
language. **Value objects are the default;** primitive types
(`string`, `int`, `Guid`) do not cross domain boundaries.

- A `CameraId` is not a `Guid`; a `Percentage` is not a `double`; a
  `Timestamp` knows whether it is `source` or `ingestion` time-based.
- Aggregates are small and protect invariants.
- CQRS and event sourcing are tools, not defaults. Use them only when a
  context's invariants demand replayability or strict read/write
  separation. v1 candidates: Overlays, Automation.

### III. Bounded Context Isolation (ADR-016, ADR-027)

Nine bounded contexts:

1. **Camera Catalog** — registration, configuration, capabilities, health.
2. **Stream Distribution** — SFU pool, shard coordinator, WebRTC fan-out,
   PTP-synced presentation timestamps.
3. **Layout & Composition** — display devices, layout templates,
   multi-monitor video walls, live composition state.
4. **System Variables** — typed variables, defaults, value-change history.
5. **Event Ingestion** — event-type registry, REST + AMQP ingress,
   schema validation, hybrid strict/discovery model per source.
6. **Overlay Designer** — overlay primitives, CEL-bound expressions,
   draft → preview → publish lifecycle.
7. **Automation** — declarative rules + CEL conditions; commands only,
   never direct mutations of other contexts.
8. **Identity & Authorization** — Keycloak federation, kiosk enrolment,
   RBAC enforcement through `IAuthorizationDecisionPoint`.
9. **Audit & Observability** — central audit of config writes, ingested
   events, variable changes; OpenTelemetry collection.

**Rules:**

- No bounded context references another context's projects directly.
  Cross-context communication is **only** through `Shared.Contracts`
  (versioned RabbitMQ messages and HTTP DTOs).
- `Shared.Kernel` holds language-level value-object types and result
  abstractions — nothing domain-specific.
- Boundaries are enforced by `NetArchTest` rules in the test project.
  A violating PR cannot merge.

### IV. The Latency Budget Is Sacred (ADR-015)

End-to-end SLO: **event arrival → overlay rendered, frame-synced ≤ 800 ms**.

Sub-budgets (any leg breaching its budget triggers an ADR-class review):

| Leg | Budget |
|---|---|
| Camera → SFU (RTP ingest) | ≤ 80 ms |
| SFU → kiosk (decode) | ≤ 120 ms |
| Presentation buffer (PTP-coordinated playout) | ≤ 200 ms |
| Event → overlay state (RabbitMQ + projection) | ≤ 200 ms |
| Overlay composite + render | ≤ 50 ms |
| Headroom | ≤ 150 ms |

Every PR that touches the event-to-overlay path must cite which leg it
affects and demonstrate the budget still holds.

**Which legs are built, and which are watched** (ADR-0117). §VII binds
the implemented ones; a leg not yet built is not yet subject, and this
table is where that claim lives so it cannot be an unnoticed absence.

| Leg | Implemented | Measured | Dashboard |
|---|---|---|---|
| Camera → SFU | yes | yes (SFU metrics) | no |
| SFU → kiosk decode | **no** | — | — |
| Presentation buffer (PTP) | **no** | — | — |
| Event → overlay state | yes | **recorded, not yet readable** — see below | no |
| Overlay composite + render | **partly** — renders over no video | — | — |
| Headroom | n/a — arithmetic remainder | n/a | n/a |

The three unbuilt legs are #1714. Keep this table current: a leg left
recorded as unbuilt after it is built would exempt itself from §VII by
clerical error.

**"Recorded, not yet readable"** (spec 025): the event → overlay leg now
emits a latency distribution from the service that applies the effect,
but nothing outside that process can read it — there is no dashboard and
no metrics readout (#1707). Measured in the sense that the number exists;
not yet in the sense that anyone can consult it. §VII is **half**
discharged for this leg, and the column says so rather than rounding up.

### V. Spec-Driven Development (ADR-003)

No implementation without a spec. The workflow is:

```
/speckit-constitution  (this document; updates only via ADR)
/speckit-specify       per feature → spec.md
/speckit-clarify       (optional) resolve open questions
/speckit-plan          → plan.md with technical approach
/speckit-tasks         → tasks.md with ordered, atomic tasks
/speckit-implement     → execute tasks; PRs trace back to tasks
```

GitHub Project board mirrors specs and tasks as issues. Every commit and
PR references the relevant spec or task ID. Specs live in `specs/`; ADRs
live in `docs/adr/`.

### VI. .NET Aspire Is the Composition Root (ADR-024)

All runtime resources — services, Postgres, RabbitMQ, Keycloak, MinIO,
Prometheus, the React app — are declared in the `AppHost` project using
`Aspire.Hosting.*` integrations.

- **Dev:** `aspire run` starts the full stack.
- **Prod:** `aspire publish --target k8s` generates Helm charts deployed
  per fab on k3s (ADR-025).
- Aspire integrations are preferred over ad-hoc configuration. If a
  resource lacks an Aspire integration, wrap it as a custom resource in
  the AppHost rather than configuring it out-of-band.

### VII. Observability Is Non-Negotiable (ADR-026, ADR-0117, ADR-0118)

Every service auto-instruments traces, metrics, and logs through
OpenTelemetry (provided by Aspire defaults).

- Telemetry reaches **one sink per environment** (ADR-0118). Development
  and CI: the Aspire dashboard, fed by the OTLP exporter Aspire injects.
  Production: deferred until there is a production deployment to attach
  a sink to, and decided with that work.
- Latency-budget dashboards (per ADR-015) are mandatory for every
  **implemented** leg. A leg whose code path exists and has no dashboard
  cannot ship further work; a leg not yet built is **not yet subject**,
  and the obligation attaches to whichever spec builds it (ADR-0117).
  The state of each leg is recorded beside the budget in §IV, so
  "not yet subject" is a claim someone made rather than an absence.
- **There is no dual-sink comparison phase** (ADR-0118). ADR-026 planned
  one; it never started, and none of the Grafana stack was built. A
  comparison needs two options and only one exists, so the choice is made
  by environment instead. Grafana remains the expected production sink,
  uncommitted until there is something to run it against.

### VIII. Safe by Default at Trust Boundaries

- **External events** are validated against a registered schema before
  reaching rules or overlays (ADR-018). Unknown types are quarantined,
  not silently dropped.
- **Kiosks** authenticate with device-bound credentials and view-only
  scopes. PTZ and layout changes require an operator token bound to
  the kiosk (ADR-008).
- **Cameras** live on an isolated OT VLAN; StreamKeeper is the only
  bridge (ADR-013).
- **Authorization** is mediated by `IAuthorizationDecisionPoint`. v1 is
  fixed-RBAC mapped to Keycloak realm roles (ADR-023); v2 can plug in a
  policy engine without refactoring call sites.

### IX. Forward-Compatible Strategy Interfaces

For features explicitly scoped for evolution, define a strategy
interface in v1 so v2 can land without breaking changes.

| Feature | v1 implementation | v2 candidate |
|---|---|---|
| Rule engine | Declarative + CEL (ADR-020) | Visual workflow (n8n / Node-RED) |
| Authorization | Fixed RBAC (ADR-023) | ABAC via OPA / Cedar |
| Camera adapter | RTSP + ONVIF (ADR-005) | Vendor SDKs (Axis VAPIX, Hikvision, …) |
| Observability sink | Both Aspire + Grafana | Single chosen sink |

---

## Technology Stack (Locked)

### Streaming

- **Protocol:** WebRTC end-to-end (ADR-004), StreamKeeper acts as SFU.
- **Codec strategy:** RTP passthrough where camera output is
  WebRTC-compatible; GPU transcode (NVENC / Quick Sync) only when
  forced. Sizing: ~1 NVENC-class GPU per 50–100 transcodes (ADR-011).
- **Scaling:** Horizontal shard-by-camera. Coordinator owns the
  cam→SFU map. Failover ≤ 5 s (ADR-012).
- **Camera protocols:** RTSP + ONVIF Profile S/T on day one (ADR-005).
- **Time sync:** PTP (IEEE 1588) grandmaster per fab (ADR-014). NTP is
  fallback only and triggers `time_uncertain` flags.

### Backend

- **.NET 10**, ASP.NET Core, **.NET Aspire** (ADR-024).
- **PostgreSQL** as default persistence (ADR-009). **Marten** for
  event-sourced contexts. **TimescaleDB** (PostgreSQL extension)
  is permitted in time-series-shaped contexts; current use is
  AuditObservability per ADR-0101. **Prometheus** for metrics.
  **MinIO** for object storage (future snapshots, eventual
  recording).
- **RabbitMQ** for both internal and external messaging (ADR-010).
- **Keycloak** for identity (ADR-007), federated to customer SSO when
  required.

### Frontend

- **React** + **TypeScript**, **Vite** dev server, registered as an
  Aspire JS resource. Browser-only — no native client. Target browsers:
  evergreen Chromium-based (Chrome, Edge); WebRTC and PTP-aware time
  APIs required.

### Operations

- **k3s + Helm** in production (ADR-025). Pilot also uses k3s
  (single-node if needed) to keep one toolchain.
- **Argo CD / Flux** for v2 cloud-pushed releases per fab.
- **GitOps:** every fab has its own deployment branch / values file.

---

## Non-Functional Requirements

### Scale

- Pilot: 20 concurrent cameras.
- Production target: 250 concurrent cameras per fab.
- Recording / replay: **out of scope for v1**, but architecture must
  not preclude it (MinIO is pre-provisioned; presentation timestamps
  are persisted).

### Availability

- 24/7 operation. Rolling updates are zero-downtime.
- StreamKeeper failover ≤ 5 s.
- A wall of 20 kiosks rebooting must come up unattended (kiosks use
  device-bound `client_credentials`).

### Security

- Cameras on isolated OT VLAN; StreamKeeper is the only bridge.
- Token-bound, short-lived credentials. No long-lived secrets in
  browsers.
- All inbound external events schema-validated at the boundary.
- All admin and config writes appear in the audit log.

### Data Retention (default; customer-overridable)

- Audit log: **365 days** hot in Postgres, then archived to MinIO.
- Event log (ingested): **90 days** hot in Postgres, then archived.
- Metrics: **30 days** in Prometheus; long-term in Thanos/Mimir or
  cloud (v2).
- Variable-change history: **180 days** in Postgres.

---

## Development Workflow

### Repository (ADR-027)

Single monorepo. Single `SmartSentinelEye.sln`. Per-context folders use
the Clean Architecture split (`Domain` / `Application` /
`Infrastructure` / `Api`).

```
smart-sentinel-eye/
├── src/                     bounded contexts + shared + AppHost
├── apps/web/                React frontend (Aspire JS resource)
├── tests/                   unit, integration, arch tests
├── deploy/helm/             generated by aspire publish
├── specs/                   Spec-Kit specs per feature
├── docs/adr/                ADRs promoted from this constitution
└── .specify/                Spec-Kit machinery
```

### Testing

- **Domain logic:** TDD red-green-refactor.
- **Integration:** against real Postgres + RabbitMQ + Keycloak via the
  Aspire AppHost in test mode (the `AspireFixture`); no Testcontainers —
  CI runs the same fixture (Docker required on the runner). See ADR-0103.
- **Architecture:** `NetArchTest` rules enforce bounded-context
  boundaries. A failing arch test blocks merge.
- **Latency:** synthetic load tests covering the 250-camera target.

### Code Review and Merging

- Every PR has a linked spec or task (Spec-Kit ID or GitHub issue).
- PRs touching the event-to-overlay path must cite the latency budget
  legs they affect.
- `ultrareview` runs on PRs that touch security boundaries (Identity,
  Event Ingestion, StreamKeeper).

---

## Governance

This constitution supersedes ad-hoc contributing guidelines. Conflicts
between this document and another doc are resolved by amending one of
the two — never by ignoring it.

**Amendments** require:

1. An ADR entry in `docs/adr/NNNN-*.md` describing the change, the
   reason, and what it supersedes.
2. A PR that updates this constitution and bumps the version below.
3. Approval by the architecture lead.

**Complexity must be justified.** A context proposing CQRS, event
sourcing, an additional dependency, or a deviation from the locked
stack must include an "Alternatives Considered" section in its spec
and explicitly map back to the principles above.

**Decision history.** The Q&A rounds that produced this constitution
yielded ADRs 001–027. They are reproduced in
`docs/adr/0000-initial-decisions.md` for reference and supersede any
contradicting tribal knowledge.

---

**Version:** 1.2.0 | **Ratified:** 2026-05-25 | **Last Amended:** 2026-08-22

**Amendment history.**
1.2.0 — §VII's sink bullets rewritten: one sink per environment, and the
dual-sink comparison phase abandoned because it never started (ADR-0118,
#1707).
1.1.0 — §VII's dashboard requirement narrowed to implemented legs, with a
leg-state table added to §IV (ADR-0117, #1681).
