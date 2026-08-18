# Contracts: provisioning and refusal

**Feature**: `019-fab-event-partitions` | **Date**: 2026-08-18

Two internal ports and one HTTP behaviour change. No message contract, no
`Shared.Contracts` version bump — nothing crosses a context boundary on the
wire.

## Port 1 — `IProvisionedFabSource`

**Owned by** EventIngestion (`Application/Ingress/`). **Implemented in**
MigrationRunner. That split is the whole reason the port exists: EventIngestion
needs the answer and may not ask Identity for it (research §R1).

```csharp
public interface IProvisionedFabSource
{
    /// The fabs that exist, as the system's own registry of plants defines
    /// them. Never empty on success — an empty realm is indistinguishable
    /// from an unreachable one, so the implementation throws instead.
    Task<IReadOnlyList<FabIdentifier>> GetFabsAsync(CancellationToken cancellationToken);
}
```

| Aspect | Contract |
|---|---|
| Success | Every fab whose name parses as a `FabIdentifier`, deduplicated |
| A name that does not parse | **Skipped**, and logged once with the offending name (FR-005) |
| Every name unusable, or none returned | **Throws.** Provisioning nothing is a legitimate outcome only if the realm genuinely has no fabs, which no real deployment has |
| Registry unreachable | **Throws** — never an empty list (FR-011) |
| Ordering | Unspecified; the caller must not depend on it |

**Why `FabIdentifier` and not `string`**: the value crosses into interpolated
DDL. Parsing at the port means no unvalidated name can reach a statement, which
is the argument that replaces the provenance one the feature invalidates
(research §R3).

## Port 2 — `IFabStorageReadiness`

**Owned by** EventIngestion. **Implemented in** EventIngestion's
Infrastructure against the Postgres catalog. Keycloak is deliberately not
involved — the precondition is that a partition exists, not that a group does.

```csharp
public interface IFabStorageReadiness
{
    /// Whether an event for this fab can be stored right now.
    Task<bool> IsReadyAsync(FabIdentifier fab, CancellationToken cancellationToken);
}
```

| Aspect | Contract |
|---|---|
| Common path | An in-memory set lookup; no I/O |
| Negative answer | Re-reads the catalog **before** answering false, so a fab provisioned moments ago is not refused by a stale cache |
| Cache staleness | Bounded by a short TTL; the set changes about as often as a plant is built |
| Database unreachable | **Throws.** It must not answer "not ready" for a database problem — that would report a provisioning gap that does not exist |

## `POST /events/manual` — refused before the channel

| | |
|---|---|
| New status | **503 Service Unavailable** |
| Error code | `EVENT_FAB_NOT_PROVISIONED` |
| When | The caller's resolved fab has no event storage |
| Ordering | Checked **after** fab resolution and **before** `channel.TryWrite` |

The ordering is the requirement, not an implementation note. Spec 018 imposed
the same on the authorization check for the same reason: a refusal that has
already enqueued is not a refusal — the event lands while the response says it
did not.

**Why 503 and not 400 or 403.** The request is well-formed and the caller is
entitled to that fab; nothing about it is wrong. The system is not ready, and
the condition is temporary by construction — the next provisioning run fixes
it. A 400 would blame the caller for a gap in our deployment, and a 403 would
tell an entitled operator they are not.

No `Retry-After`: how long is genuinely unknown, and a made-up number would be
worse than none.

## `POST /events/webhook/{integrationName}` — same refusal

The machine path gets the identical check and the identical 503 (FR-009). It is
the same precondition and the same consequence; only the caller differs.

Note the ordering against spec 018's amendment: the fab is already established
from the integration's own plant before this check runs, so readiness is asked
about a fab the caller is entitled to, never about one they merely named.

## The broker path — unchanged in shape

An MQTT delivery whose fab has no storage is **not** refused at the ingress:
there is nobody to refuse to. It reaches the persistence loop as it does today,
and the loop's handling is the change below.

## The persistence loop — `23514` becomes legible

| | Before | After |
|---|---|---|
| Log | one Error, identical for every dispatch fault | a distinct Error naming the fab and saying the partition is missing |
| Envelope | dropped | **still dropped** |

The second row is deliberate and is **not** this feature fixing #1546. What a
loop should do with an envelope it cannot persist — dead-letter it, retry it,
refuse to consume it — is that issue's question, and answering it here for one
cause would leave the loop with two behaviours to reconcile later.

What changes is only that the cause stops being invisible. A race remains by
design: a partition dropped between the readiness check and the insert lands
here, and the distinguishable log is what makes that residue legible while
#1546 is still open.

## Wire compatibility

Nothing changes for a correctly-behaving client of a correctly-provisioned
system. The 503 is reachable only in a state where the alternative was
accepting an event and discarding it.
