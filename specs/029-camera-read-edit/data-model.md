# Data Model: Read a single camera, and correct one

**Feature**: `029-camera-read-edit` · 2026-08-24

**No new tables and no migration.** One aggregate gains a behaviour, two
read-side shapes gain a field they were always able to carry, and — if
research finding 2 is adopted — one aggregate in another context gains a
behaviour of its own.

---

## Camera (CameraCatalog)

### Unchanged

No column is added. `Version` already exists on `AggregateRoot<TIdentifier>`
and is already mapped by `CameraConfiguration` with `.IsConcurrencyToken()`;
this feature is the first to *expose* it, not the first to keep it.

### New behaviour

**`ChangeAddress(RtspUrl url, OperatorIdentifier changedBy, IClock clock)`**

- Replaces `Url`.
- Raises `CameraAddressChangedDomainEvent`.
- **Refuses a retired camera** — `Status == Decommissioned` throws, mirroring
  the terminal rule `Retire` already enforces (FR-005). The guard is on the
  aggregate, never in the handler.
- **Raises nothing when the address is unchanged.** Idempotency as *no event*,
  not *no error* — the lesson spec 028 recorded. A no-op that re-announces
  would tell StreamDistribution to re-point a path that never moved and would
  put a second row in the audit trail for a change that did not happen.

### What stays immutable, and why the guarantee is structural

| Field | Why |
|---|---|
| `Id` | It is the key this feature chose (FR-009) |
| `Fab` | A camera cannot move plants (spec 015 FR-004); a stream's fab is its camera's (spec 016 FR-002) |
| `Name` | Out of scope (FR-012), tracked as #1850 |
| `RegisteredAt`, `RegisteredBy` | They record what happened (FR-009) |

The aggregate exposes no behaviour that could change any of them — the
guarantee is the absence of a method, not a validation that could be relaxed.

### State

Unchanged by this feature. `Registered → Decommissioned` remains spec 028's,
and `ChangeAddress` is legal only in `Registered`.

```
Registered ──ChangeAddress()──► Registered        (address replaced, event raised)
Registered ──Retire()────────► Decommissioned    (terminal, spec 028)
Decommissioned ──ChangeAddress()──► refused      (FR-005)
```

---

## Read-side shapes

### `CameraDto` (new — the read-one shape)

| Field | Type | Note |
|---|---|---|
| `CameraIdentifier` | `Guid` | |
| `Version` | `int` | ADR-0113. What the caller echoes in `If-Match` |
| `Fab` | `string` | |
| `Name` | `string` | |
| `RtspUrl` | `string` | |
| `RegisteredAt` | `DateTimeOffset` | |
| `Status` | `string` | `Registered` or `Decommissioned` — FR-002 returns retired cameras |

### `CameraSummaryDto` (changed)

Gains **`Version`**, for the reason `RuleDto` already gives: *"also on the body
so the list endpoint hands every row a version without a per-row fetch."* An
operator can then edit straight from the listing, which is the one place this
feature measurably reduces traffic — as against the saving SC-001 claims, which
research finding 1 shows is not available.

Additive, so existing consumers are unaffected: the management app's
`CameraSummary` is a plain TypeScript interface with no runtime validation.

---

## Events

### `CameraAddressChangedDomainEvent` (in-process, CameraCatalog)

| Field | Type |
|---|---|
| `Camera` | `CameraIdentifier` |
| `Fab` | `FabIdentifier` |
| `PreviousUrl` | `RtspUrl` |
| `Url` | `RtspUrl` |
| `ChangedBy` | `OperatorIdentifier` |
| `ChangedAt` | `DateTimeOffset` |

`PreviousUrl` is carried because the audit trail's value is in the delta — "the
address changed" without saying from what is a row that records that something
happened and not what.

### `CameraAddressChangedV1` (integration, `Shared.Contracts`)

Primitives only, per ADR-0040.

| Field | Type |
|---|---|
| `Camera` | `Guid` |
| `Fab` | `string` |
| `PreviousUrl` | `string` |
| `Url` | `string` |
| `ChangedAt` | `DateTimeOffset` |
| `ChangedBy` | `Guid` |
| `Metadata` | `EventMetadata` |

**Two consumers, one event.** AuditObservability records it (FR-011, and
`Architecture.Tests` fails the build if no audit handler exists). StreamDistribution
re-points the path (research finding 2). This is why finding 2 is cheaper than
it looks: the announcement is needed for the audit trail regardless, so adopting
it costs only the consumer.

---

## Stream (StreamDistribution) — only if research finding 2 is adopted

### New behaviour

**`RepointTo(StreamSourceUrl url, IClock clock)`**

- Replaces `SourceUrl`, which today is assigned **only** in `Provision` and has
  no other writer — the gap that makes finding 2 real.
- Idempotent: re-pointing to the current URL raises nothing.
- **Refuses a retired stream**, mirroring the guard spec 028 put on
  `ReportHealthy`/`ReportDegraded`/`ReportOffline`. Re-pointing hardware that
  has been retired changes nothing except the record.
- The MediaMTX **path name does not change** — it is derived from the camera
  identifier (`MediaMtxPath.For(camera)`), and the camera identifier is
  immutable (FR-009). Only the source the path pulls from changes, so the
  teardown is a re-add rather than a rename, and no kiosk's WHEP URL breaks.

### Persistence

No column added. `source_url` already exists because the startup reconciler
needs it — and that same reconciler is why a stale `SourceUrl` is not
self-healing: it would faithfully restore the *wrong* address.

---

## Explicitly not modelled

- **No `CameraNameChangedV1`.** Renaming is out of scope (FR-012, #1850).
- **No un-retire.** Terminal by spec 028's decision.
- **No edit history table.** The audit trail is the history, and it already
  holds every `*V1` with its actor.
- **No `Camera.ChangeFab`.** See the immutability table — the guarantee is that
  no such method exists.
