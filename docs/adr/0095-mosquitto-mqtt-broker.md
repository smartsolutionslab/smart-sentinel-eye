# ADR-0095: Mosquitto as the canonical MQTT broker

**Status:** Accepted
**Date:** 2026-05-28
**Supersedes:** —
**Superseded by:** —

## Context

Spec 006 (EventIngestion) introduces two of the four event sources
that EventIngestion will accept on MQTT: PLC events from per-fab
factory gateways, and inference events from camera-side AI models.
Both are high-volume (~70% inference + ~25% PLC of the 1 000 ev/s
v1 target) and operationally critical. The protocol choice was
made in Phase 1 Q&A (split-protocol ingress: HTTP for manual +
webhook, MQTT for PLC + inference) — see spec.md §FR-006 / §FR-007.

The broker becomes a new fab-side infrastructure dependency
alongside Postgres + RabbitMQ. We need to pick one before PR A
of spec 006 can land. Constitution §II requires an ADR for
tech-stack additions.

Constraints:

- Runs per-fab (no cross-fab broker federation per spec 006 Out
  of Scope).
- TLS-mandatory in prod; plaintext acceptable in dev.
- Per-device auth so a compromised PLC can publish only on its
  own topic.
- QoS 1 (at-least-once) — exactly-once handled at our edge via
  `(fabId, eventId)` dedup.
- Sustained 1 000 msg/s with ≤ 50 ms p95 broker → subscriber
  delivery as the bound on our `ingest.parse` budget.
- Must compose cleanly into Aspire AppHost (dev) and Helm (prod).
- Aligned with the team's "boring infrastructure" preference —
  ops surface should be minimal.

## Decision

**Eclipse Mosquitto 2.0.x** is the canonical MQTT broker for
Smart Sentinel Eye.

- Per-fab single-instance StatefulSet in production (no
  clustering in v1 — broker per fab is already the natural
  sharding boundary). Persistent volume for the QoS 1 persistent
  session store.
- Authentication via Mosquitto's built-in `password_file` + `acl_file`.
  Each PLC/camera device gets its own username + password pair
  generated at provisioning time. A dedicated `event-ingestion`
  service account subscribes wildcard `fab/+/+/+`.
- Topic taxonomy `fab/{fabId}/{source}/{deviceId}` (locked in
  spec 006 §FR-007).
- TLS on port 8883 in prod; plaintext on 1883 in dev (Aspire
  bind-mounts the dev config).

## Consequences

**Positive:**

- Mosquitto is the de-facto factory-floor MQTT broker — every PLC
  / camera vendor tests against it. Onboarding new devices is
  straightforward; the protocol surface they're already shipping
  works out of the box.
- Single C binary; minimal CPU + memory footprint at our scale
  (well under 1 GB RAM for 1 000 msg/s sustained).
- Configuration files (`mosquitto.conf` + `passwords.txt` +
  `acl.txt`) are git-friendly text — no GUI / cloud control plane
  to manage; password file is rotated via Helm secrets at deploy
  time.
- Helm community charts are mature (eclipse-mosquitto / k8s@home
  variants).

**Negative:**

- A new infrastructure component to operate alongside RabbitMQ.
  Ops surface +1 (config files, password rotation, TLS cert
  rollover, Prometheus exporter for broker metrics).
- No native clustering in the open-source build. If a single
  fab ever needs more than one broker instance, we'd have to
  swap to EMQX or HiveMQ — but per-fab sharding is the natural
  scale-out path, so this is unlikely.
- Mosquitto's auth integration with Keycloak (via `mosquitto-go-auth`)
  is more brittle than we'd like; we accept per-device static
  passwords + ACL files as the v1 model and revisit if a single
  fab grows beyond ~50 devices.

## Alternatives Considered

**RabbitMQ MQTT plugin — REJECTED.** Activating `rabbitmq_mqtt` on
the existing RabbitMQ instance was attractive (zero new
components) but has a noisy-neighbour risk: a sustained 1 000
msg/s of MQTT publish traffic competes for the same broker
threads / I/O as Wolverine's command + event queues. A burst on
the MQTT side could degrade Wolverine outbox throughput. The
mitigation (separate RabbitMQ vhost + monitoring) adds the same
ops surface as just running Mosquitto, and Mosquitto's MQTT
implementation is more mature than RabbitMQ's plugin.

**EMQX — REJECTED.** Clusterable, dashboard-rich, high-throughput
(100k+ connections). All capabilities we don't need at 1 000
msg/s on a single fab. Open-core licensing model adds friction.
Heavier resource footprint.

**HiveMQ Community Edition — REJECTED.** Java; bigger memory
baseline than Mosquitto; same feature set we need; no
operational advantage to justify a JVM dependency in the data
plane.

**VerneMQ — REJECTED.** Erlang-based, clusterable, but the
community has shrunk noticeably since 2022; less safe long-term
bet than Mosquitto.

## Implementation Notes

- The Aspire AppHost adds Mosquitto via
  `AddContainer("mosquitto", "eclipse-mosquitto", "2.0")` with
  bind-mounts on `src/AppHost/mosquitto/{mosquitto.conf,passwords.txt,acl.txt}`
  for dev (see spec 006 task T006).
- The Helm chart fragment under `deploy/helm/mosquitto/` (task
  T107) configures the prod deployment: StatefulSet replica
  count = 1, PVC for `/mosquitto/data`, ConfigMap for the auth
  files mounted at `/mosquitto/config`.
- Prod password rotation cadence and broker observability metrics
  are operational concerns tracked separately; spec 006 ships the
  baseline only.
