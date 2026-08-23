# Phase 1 Data Model: Retire a camera

**Feature**: `028-retire-camera` · 2026-08-23

No new tables and **no migration**. Two aggregates gain a terminal state; one of
them already had the value for it.

---

## Camera (CameraCatalog)

### Changed

| Field | Change |
|---|---|
| `Status` | Can now reach `CameraStatus.Decommissioned`. Previously unreachable — the value existed and nothing set it (#1433). |

### New behaviour

**`Retire(OperatorIdentifier retiredBy, IClock clock)`**

- Transitions `Status` → `Decommissioned`.
- Raises `CameraRetiredDomainEvent`.
- **Idempotent**: retiring an already-retired camera returns without raising a
  second event (FR-005).
- **Terminal**: there is no behaviour out of `Decommissioned`.

### State transitions

```
Registered ──Retire()──► Decommissioned ──► (terminal)
     ▲                        │
     └────── nothing ─────────┘
```

Retirement does **not** erase `Name`, `Fab` or `Url`. The camera is recorded as
gone, not deleted — the history that it was there is what an audit trail exists
to explain.

### Persistence

Unchanged. `status` already stores the literal `"Decommissioned"`, and the
partial unique index

```
ux_cameras_fab_name_normalized_active
  UNIQUE (fab, name_normalized) WHERE status <> 'Decommissioned'
```

already excludes retired rows, so the name is released the moment the status
changes (research §1). **This is the whole of FR-006's implementation.**

---

## Stream (StreamDistribution)

### Changed

| Field | Change |
|---|---|
| `State` | Gains a terminal value for a stream whose camera has been retired. |

### New behaviour

**`Retire(IClock clock)`**

- Transitions `State` → terminal.
- Idempotent, for the same reason retiring a camera is: the announcement can be
  redelivered.
- **Terminal, and enforced**: `ReportHealthy`, `ReportDegraded` and
  `ReportOffline` MUST refuse a retired stream. This is the guard that stops a
  late health probe resurrecting it.

### State transitions

```
Provisioning ─┬─► Healthy ⇄ Degraded ──► Offline
              │        │         │           │
              └────────┴─────────┴───────────┴──► Retired (terminal)
```

Any state can be retired: a camera can be pulled off the wall while its stream
is healthy, degraded, offline, or still provisioning.

### Persistence

The row is retained (FR-008). No column is added — `state` already stores its
value as a string, exactly as `status` does on Camera.

---

## Events

### `CameraRetiredDomainEvent` (in-process, CameraCatalog)

| Field | Type |
|---|---|
| `Camera` | `CameraIdentifier` |
| `Fab` | `FabIdentifier` |
| `Name` | `CameraName` |
| `RetiredBy` | `OperatorIdentifier` |
| `RetiredAt` | `DateTimeOffset` |

Carries `Name` so a subscriber can log *which* name was released without
re-reading the aggregate.

### `CameraRetiredV1` (integration, `Shared.Contracts`)

Primitives only at the wire boundary (ADR-0040), mirroring `CameraRegisteredV1`:

| Field | Type |
|---|---|
| `Camera` | `Guid` |
| `Fab` | `string` |
| `Name` | `string` |
| `RetiredAt` | `DateTimeOffset` |
| `RetiredBy` | `Guid` |
| `Metadata` | `EventMetadata` |

**Consumers**: AuditObservability (records it, as it records every V1) and
StreamDistribution (retires the stream, removes the SFU path).

---

## What is deliberately absent

- **No `UnretireCamera`.** Retirement is terminal by decision (spec
  Assumptions); replacement hardware is a new camera that may take the old name.
- **No cascade delete.** Nothing is removed from either database.
- **No new column, index or migration.** If implementation finds one is needed,
  that contradicts research §1 and is a finding to raise, not to absorb quietly.
