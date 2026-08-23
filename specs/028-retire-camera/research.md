# Phase 0 Research: Retire a camera

**Feature**: `028-retire-camera` · **Spec**: [spec.md](./spec.md) · 2026-08-23

Four questions had to be answered before design, and the first is the one the
spec's own assumption rested on.

---

## 1. Does retiring actually free the name, with no schema change?

**Decision: yes. No migration is needed for FR-006.**

Verified rather than assumed, because the spec's Assumptions section says a
schema change is not expected and "if that proves wrong it is a finding for the
planning phase, not a silent scope increase".

Two facts have to line up, and they do:

| | |
|---|---|
| The unique index | `ux_cameras_fab_name_normalized_active` on `(fab, name_normalized)` **`WHERE status <> 'Decommissioned'`** |
| The stored status | `CameraStatus.Decommissioned` has `Value = "Decommissioned"` and is persisted as that literal string |

A retired camera therefore falls out of the partial index, and its
`(fab, name_normalized)` pair becomes available. Nothing else in the schema
holds the name.

**Rationale for checking first**: the index was rewritten hours earlier by
#1434, which replaced `(fab, name)` with the normalised column. Had that change
dropped the filter, this feature's payoff story would have collapsed at
implementation time rather than here.

**Alternatives considered**: none needed. Had the filter been missing, the
options were a migration adding it, or FR-006 moving to a follow-up.

**Consequence for the plan**: US2 is *assertion* work, not migration work. It
needs an integration test, not a schema task.

---

## 2. Where does the terminal state live on the Stream side?

**Decision: add a terminal `StreamState` and a `Retire` behaviour on the Stream
aggregate. Do not delete the row.**

`Stream` today has `Provision`, `AttributeToFab`, `ReportHealthy`,
`ReportDegraded`, `ReportOffline`. `StreamState` has `Provisioning`, `Healthy`,
`Degraded`, `Offline`. **There is no terminal state** — every existing state is
one the health watcher can move a stream out of.

That matters more than it looks. Without a terminal state the health watcher
keeps polling a retired camera's path forever, and since #1801 was fixed it now
announces *every* health change rather than one per sweep — so a retired camera
would become a permanent generator of health announcements and audit rows. The
new state must therefore be one the watcher **excludes from its sweep**, not
merely one it can report.

**Alternatives considered**:

- *Delete the Stream row.* Rejected: the audit trail should be able to explain a
  stream that once existed, and FR-008 says the record is retained.
- *Reuse `Offline`.* Rejected: `Offline` means "should be working and is not",
  which is a fault an operator investigates. Conflating it with "gone on
  purpose" is exactly the distinction this feature exists to draw for cameras.

---

## 3. How does the retirement cross the context boundary?

**Decision: an integration event, `CameraRetiredV1`, mirroring
`CameraRegisteredV1`. StreamDistribution consumes it exactly as it consumes
registration.**

The registration path is already the template: `CameraRegisteredV1` →
`CameraRegisteredIntegrationEventHandler` → `ProvisionStreamCommand`. Retirement
is the same shape in reverse — a handler translating the announcement into a
command that retires the stream and removes the SFU path.

Contract carries primitives only (ADR-0040): the camera's Guid, its fab, who
retired it and when.

**This satisfies FR-008a by construction.** The two contexts share an
announcement, not a transaction: the camera is retired in the catalogue whether
or not stream distribution has caught up, and the outbox (ADR-0088) guarantees
the announcement survives a crash.

**Alternatives considered**: a synchronous call from CameraCatalog into
StreamDistribution — rejected outright, it violates bounded-context isolation
(constitution §III) and would make retirement fail when the SFU is unreachable.

---

## 4. What does the health watcher do with a retired stream?

**Decision: exclude retired streams from the sweep.**

`StreamHealthWatcher.PollOnceAsync` lists *every* stream and probes each one. A
retired stream whose MediaMTX path has been removed would probe, fail, and — for
as long as the process runs — produce health transitions for hardware that does
not exist.

The listing must filter the terminal state out. This is a one-line change to the
query but it is the difference between this feature reducing noise and adding
it.

**Worth stating plainly**: this is the second time in one day that #1801's fix
has changed what a design has to account for. Before it, most of those
announcements were being silently dropped, and the cost of getting this wrong
would have been invisible.

---

## Open, and deliberately not researched

**FR-008 is a decision, not a consensus.** Whether stream teardown belongs in
this feature was put to the user twice and adopted on the assistant's
recommendation. Everything in sections 2–4 exists only because of that choice.
If it is overturned, sections 2–4 drop out whole and only sections 1 and 3's
contract survive — the announcement is required by FR-009 either way.

Recorded here so the blast radius of reversing it is legible, rather than
discovered by unpicking a plan.
