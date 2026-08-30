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
   playout alignment against the SFU's clock (ADR-0128).
3. **Layout & Composition** — display devices, layout templates,
   multi-monitor video walls, live composition state.
4. **System Variables** — typed variables, defaults, value-change history.
5. **Event Ingestion** — event-type registry, REST + AMQP ingress,
   schema validation, hybrid strict/discovery model per source.
6. **Overlay Designer** — overlay primitives, AEL-bound expressions,
   draft → preview → publish lifecycle.
7. **Automation** — declarative rules + AEL conditions; commands only,
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

End-to-end SLO: **event arrival → overlay rendered ≤ 800 ms**.

**The overlay is not frame-synced, and no longer claims to be** (ADR-0129).
A label is layered over the video rather than composited into a frame, so it
cannot be paired with the frame whose instant it describes — that needs a clock
shared between the camera and the event source, which is PTP hardware this
system does not have. **A label is instead *aged* to match its picture**: held
back by its own tile's measured frame age, so both describe about the same
moment.

**Which way buffering moves it.** The picture is `buffer + processing` old, so
**anything that adds playout buffer makes the picture older** — spec 045's wall
alignment does exactly that, and widened this gap before it was noticed. Aged
labels move *with* the buffer, so the pairing survives. **Any future change that
adds buffer should read this paragraph before assuming it is free.**

Sub-budgets (any leg breaching its budget triggers an ADR-class review):

| Leg | Budget |
|---|---|
| Camera → SFU (RTP ingest) | ≤ 80 ms |
| SFU → kiosk (decode) | ≤ 120 ms |
| Presentation buffer (playout alignment) | ≤ 200 ms |
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
| SFU → kiosk decode | yes | **in part** — receive-to-decoded only; see below | no |
| Presentation buffer (playout alignment) | yes | **recorded, not yet observed** — see below | no |
| Event → overlay state | yes | **recorded, not yet readable** — see below | no |
| Overlay composite + render | yes | yes | no |
| Headroom | n/a — arithmetic remainder | n/a | n/a |

**No leg is unbuilt any more** (spec 045). The presentation buffer was the
last, and #1714's premise — *"three of six legs are unbuilt"* — is now
spent: two were never unbuilt (spec 040 found the record wrong), and this
one has been built.

**That is not the same as the 800 ms path holding end to end.** Every leg
existing is a precondition for measuring the whole path, not a
measurement of it. #1714 stays open until someone has watched a wall and
re-measured the path with alignment active, and **inter-display
synchronisation remains unbuilt and out of scope** (ADR-0128) — it is not
a row here, and its absence is not covered by this paragraph.

**It also spends the last §VII exemption, and that is now urgent rather
than theoretical.** ADR-0117 exempts a leg by its being *not yet
subject*; no leg is unbuilt, so **every leg is now subject** — and the
Dashboard column reads `no` for all five. Whether that column is
satisfied by a figure being readable in the sink, or demands a
purpose-built view, is **#1940, and it is not settled here**. Spec 045
does not resolve it and must not be read as having done so; it has
removed the last row that did not depend on the answer.

Keep this table current: a leg left recorded as unbuilt after it is built
would exempt itself from §VII by clerical error. **The reverse now costs
as much**: a leg recorded as measured before anyone has read its figure
claims a discharge nobody earned.

**That sentence describes something that happened** (spec 040). Two legs
above stood at "no" and "partly" while their code ran on every kiosk, and
so carried no §VII obligation for as long as the record was wrong. The
kiosk's `CellPage` renders `CameraViewer` — a **shared** composite that
owns the `<video>` element, drives the peer connection, and draws the
overlay onto the live frame. The claim came from a search scoped to
`apps/kiosk-web`, where the capability is not; it lives in `apps/shared`.
Four documents agreed with each other and none had been checked against
the code. The warning stays because it was right, and it can now point at
an instance.

**"In part"** (spec 040), defined as deliberately as "recorded, not yet
readable" below it: the decode budget spans *SFU sends → kiosk has
decoded*, and a browser cannot see the sending end without a clock shared
with the SFU. So what is recorded is `receive_to_decoded` — first packet
of a frame received through to that frame decoded — under a name that
does not claim the leg, and with **no budget attached**, because the
recorded fragment is the cheaper half and reporting it against 120 ms
would look like the budget passing. The leg is measured in part; the
column says so rather than rounding up.

**Its stated reason has changed, and the verdict has not** (spec 045,
FR-011). That paragraph used to end *"establishing one is the
presentation-buffer leg, which is not built"* — and that leg is now
built. A shared clock **does** exist: every tile of a wall is served by
one SFU, whose RTCP sender reports carry one clock (ADR-0128).

**It still does not close this leg.** Chromium exposes no per-frame
send-to-arrival mapping, so `SFU sends → decoded` can only be
*estimated* — round-trip time halved, plus buffer, plus decode — and an
estimate is not a measurement. Raising the column on that basis would be
the rounding up this table forbids everywhere else. So the entry stays
**in part**, now for a reason about what a browser exposes rather than
about a leg that did not exist.

**"Recorded, not yet observed"** (spec 045): the presentation-buffer leg
emits `kiosk-presentation-buffer` per tile, as a whole leg against its
200 ms budget — the kiosk both causes the delay and observes it, so
nothing is missing from the figure. Its skew rides a **separate**
instrument, because a spread between two tiles is not a duration any
frame spent travelling.

**Distinct from "not yet readable" below it, and weaker.** That entry
means the number cannot be reached from outside its process. This one
can be — it reaches the sink by the same path as the other kiosk legs —
but **no person has yet read it off a running wall**. The code is tested;
the wall is not. It becomes *yes* when spec 045's T026 is walked and a
figure is recorded, and not before: a leg marked measured on the strength
of a passing unit test is a §VII discharge nobody earned.

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
Mosquitto, MediaMTX, the React apps — are declared in the `AppHost` project
using `Aspire.Hosting.*` integrations.

**Prometheus was listed here and is not declared** (ADR-0130). ADR-0118
abandoned the Grafana/Prometheus stack and chose the Aspire dashboard as
the single sink; this list never followed.

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

- **External events.** Rejected deliveries are **dead-lettered**, captured
  with payload and error, audit-only and never fanned out. But there is
  **no event-type registry, no per-source strict/discovery mode and no
  promotion path**, so an *unknown* type is ingested like any other rather
  than quarantined for review. The intended guarantee stands as a
  requirement (ADR-018, ADR-0130, issue 1972), **not as a description of
  today**.
- **Kiosks.** Device-bound credentials **exist** — `POST /kiosks/enroll`
  mints a per-kiosk confidential client with a service account and a
  single-reveal secret. **The kiosk app does not use them**: it signs in
  as the shared public client through the authorization-code flow, with
  view-only scopes (ADR-008, ADR-0130, issue 1976). Kiosk-bound operator
  elevation is not built, and PTZ is not built, so that constraint has
  nothing to bind.
- **Cameras** are reached only by the SFU; no other service opens a
  connection to one, and that much is enforced in code. **The OT VLAN
  split and the dual-NIC bridge are deployment properties this repository
  cannot verify**, and *StreamKeeper* does not exist as a component
  (ADR-013, ADR-0130).
- **Authorization** is enforced by scope checks at every endpoint, plus
  fab-group membership. **`IAuthorizationDecisionPoint` does not exist**,
  so v2 cannot plug in a policy engine without touching call sites — the
  §IX obligation this bullet assumed is unmet (ADR-023, ADR-0130, issue
  1970).

### IX. Forward-Compatible Strategy Interfaces

For features explicitly scoped for evolution, define a strategy
interface in v1 so v2 can land without breaking changes.

| Feature | v1 implementation | Strategy interface | v2 candidate |
|---|---|---|---|
| Rule engine | Declarative + **AEL** (ADR-020, ADR-0130) | **`IRuleEngine` — absent** (issue 1970) | Visual workflow (n8n / Node-RED) |
| Authorization | **Scopes + fab groups** (ADR-023, ADR-0130) | **`IAuthorizationDecisionPoint` — absent** (issue 1970) | ABAC via OPA / Cedar |
| Camera adapter | **RTSP only** — ONVIF absent (ADR-005, ADR-0130) | **Absent** (issue 1973) | Vendor SDKs (Axis VAPIX, Hikvision, …) |
| Observability sink | **Aspire dashboard** (ADR-0118) | n/a — one sink by decision | Production sink, deferred |

**Three of these four rows were stale, and the audit that found them is
ADR-0130.** The section's purpose is that v2 lands without breaking
changes — and **two of the interfaces it mandates "in v1" do not exist**,
while a third depends on an adapter seam that does not either. A column
for the interface has been added, because listing only the v1 and v2
implementations let the obligation this section exists to impose go
unrecorded for as long as nobody looked.

---

## Technology Stack (Locked)

### Streaming

- **Protocol:** WebRTC end-to-end (ADR-004), StreamKeeper acts as SFU.
- **Codec strategy:** RTP passthrough where camera output is
  WebRTC-compatible; GPU transcode (NVENC / Quick Sync) only when
  forced. Sizing: ~1 NVENC-class GPU per 50–100 transcodes (ADR-011).
- **Scaling:** Horizontal shard-by-camera. Coordinator owns the
  cam→SFU map. Failover ≤ 5 s (ADR-012).
- **Camera protocols:** RTSP. **ONVIF Profile S/T was decided "on day
  one" and is absent** (ADR-005, ADR-0130, issue 1973).
- **Time sync:** PTP (IEEE 1588) grandmaster per fab (ADR-014). NTP is
  fallback only and triggers `time_uncertain` flags. **This stands**, for
  fab-wide correlation and for inter-display sync — ADR-0128 amended
  ADR-014 only in that the *presentation-buffer leg* does not depend on
  PTP, not that PTP is dropped.

### Backend

- **.NET 10**, ASP.NET Core, **.NET Aspire** (ADR-024).
- **PostgreSQL** as default persistence (ADR-009). **TimescaleDB**
  (PostgreSQL extension) is permitted in time-series-shaped contexts;
  current use is AuditObservability per ADR-0101. **MinIO** for object
  storage — **in use today**: audit chunks are archived to it by
  `MinioAuditChunkArchiver` (ADR-0101, ADR-0130). Snapshots and recording
  remain future uses.
  **Marten** remains permitted for event-sourced contexts and **is not
  used anywhere** — no context has yet justified it (ADR-0130).
  **Metrics go to the sink ADR-0118 chose**, not to Prometheus.
- **RabbitMQ** for both internal and external messaging (ADR-010).
- **Keycloak** for identity (ADR-007), federated to customer SSO when
  required.

### Frontend

- **React** + **TypeScript**, **Vite** dev server, registered as an
  Aspire JS resource. Browser-only — no native client. Target browsers:
  evergreen Chromium-based (Chrome, Edge). Required: WebRTC with
  `RTCRtpReceiver.jitterBufferTarget`, and `getStats` reporting
  `inbound-rtp` jitter-buffer and processing counters (ADR-0128).
  **Not** PTP-aware time APIs — no browser exposes any, and that
  requirement stood here unsatisfiable from ratification until 2026-08-28.

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
- A wall of 20 kiosks rebooting must come up unattended. **Met for one
  screen; not demonstrated for twenty.** A screen signs in as a
  wall-display account rather than as a person, holding a grant that does
  not expire, so it keeps its wall past the session limits that ended it
  before — 30-minute idle, 10-hour ceiling — and comes back from an outage
  that outlasts them (ADR-0132).

  **What was actually shown, because this entry has claimed too much
  before.** The ceiling was **shortened to seconds** on a test realm: that
  demonstrates the mechanism, **not** the production configuration, and no
  wall has been watched for ten real hours. **Two** screens were exercised,
  not twenty — enough to show that withdrawing one leaves the other
  running, which is a different claim from twenty recovering together.

  **The device-bound credentials this target once assumed are still not
  the answer.** They exist (`POST /kiosks/enroll`) and **cannot be used
  from a browser** — a page has no secure store, and a secret shipped to
  it is published. They remain minted and unconsumed (issue 1988), and the
  only design that could use them needs a device runtime (issue 1987).

  **The cost, recorded rather than implied.** A stolen powered-off screen
  now yields a grant with no expiry, where before it yielded one lasting
  at most thirty minutes. What bounds the loss is that the grant is
  view-only in a single fab, that ending one screen's session stops only
  that screen, and that **no operator gained anything** — the privilege
  reaches wall-display accounts alone. **Nothing cleans these grants up**
  (issue 1988 makes the same objection about credentials nothing consumes).

### Security

- Cameras on isolated OT VLAN; StreamKeeper is the only bridge.
- Token-bound, short-lived credentials. No long-lived secrets in
  browsers.
- All inbound external events schema-validated at the boundary.
- All admin and config writes appear in the audit log.

### Data Retention (default; customer-overridable)

- Audit log: **365 days** hot in Postgres, then archived to MinIO.
- Event log (ingested): **90 days** hot in Postgres, then archived.
- Metrics: retention is **whatever the chosen sink provides** (ADR-0118);
  in dev and CI that is the Aspire dashboard's in-memory window, which is
  not a retention policy. **The former "30 days in Prometheus, long-term
  in Thanos/Mimir" described a stack that was never built** (ADR-0130), and
  a real policy is owed once there is a production sink.
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

**Version:** 1.6.0 | **Ratified:** 2026-05-25 | **Last Amended:** 2026-08-29

**Amendment history.**
1.6.0 — §IV withdraws the SLO's **frame-synced** claim (ADR-0129,
issue 1967). The overlay is a label layered over the video, not paired with
the frame whose instant it describes — which needs a clock shared between
camera and event source, i.e. PTP hardware this system does not have. A
label is **aged** to match its picture instead. §IV now also records **which
way playout buffering moves the gap**, because spec 045 widened it by adding
buffer and only a code reading noticed.
1.5.0 — the founding decisions audited against the code (ADR-0130,
issue 1969). §IX gains a **strategy interface** column and three of its
four rows are corrected: the rule engine runs AEL not CEL, authorization
is by scopes and fab groups not four named roles, the camera adapter has
RTSP but no ONVIF, and the observability row still described the
dual-sink stack ADR-0118 abandoned. **Two interfaces §IX mandates "in
v1" do not exist** (issue 1970). §VI's resource list named Prometheus,
which the AppHost does not declare; §Stack claimed Prometheus for
metrics and overstated Marten; §Retention promised 30 days in Prometheus
with Thanos or Mimir, none of which exist. Of 89 audited claims, 52 hold.
1.4.0 — §IV's leg table: the presentation buffer moves from unbuilt to
implemented and **recorded, not yet observed**, a fourth state defined
beside the existing three — the figure reaches the sink but no person has
read it off a running wall, and it becomes *yes* only when spec 045's
T026 is walked. No leg is unbuilt any more, which is not the same as the
800 ms path holding end to end. The decode leg stays **in part**: its
stated reason (that no shared clock existed) is now wrong, but Chromium
exposes no per-frame send-to-arrival mapping, so the leg can only be
estimated and an estimate is not a measurement (spec 045, #1714).
1.3.0 — the presentation-buffer leg is renamed from *(PTP)* to *(playout
alignment)* in §III and both §IV tables, and §Frontend's "PTP-aware time
APIs required" is replaced with the WebRTC capabilities a kiosk actually
needs — no browser exposes a PTP time API, so that requirement had been
unsatisfiable since ratification. PTP itself is unchanged for fab-wide
time and inter-display sync (ADR-0128, #1714). **The leg's Implemented
column is deliberately untouched: renaming a leg is not building it.**
1.2.0 — §VII's sink bullets rewritten: one sink per environment, and the
dual-sink comparison phase abandoned because it never started (ADR-0118,
#1707).
1.1.0 — §VII's dashboard requirement narrowed to implemented legs, with a
leg-state table added to §IV (ADR-0117, #1681).
