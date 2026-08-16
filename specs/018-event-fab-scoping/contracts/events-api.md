# Contract: Events API

**Feature**: `018-event-fab-scoping` | **Date**: 2026-08-16

Eight endpoints exist across two groups. **Four change, one is exempt with a
reason, and three are out of scope.**

## The change that is easy to state backwards

`?fabId=` is **required** on three of these endpoints today. This feature makes
it **optional** — which sounds like a loosening and is the opposite:

| Caller | Today | After |
|---|---|---|
| passes a fab they hold | works | works, unchanged |
| passes a fab they do **not** hold | **works** ← the leak | **403** |
| omits it | 400 | their own fabs (read) / inferred or refused (write) |

Strictly narrowing for the illegitimate caller, strictly widening for the
legitimate one. No client that was behaving correctly needs to change.

## `GET /events` — list

| | |
|---|---|
| `?fabId=` | required → **optional**, and now **checked** |
| Fab resolution | `FabResolution.ResolveForReadAsync` — spans all the caller's fabs when omitted |
| New statuses | **403** (a fab not held, or holding none) |

The other nine query parameters — `source`, `deviceId`, `kind`, the four time
bounds, `pageSize`, `cursor` — are untouched.

## `GET /events/{eventId:guid}` — read one

| | |
|---|---|
| `?fabId=` | required → **optional**, and now **checked** |
| Scoping | **404** for an event outside the caller's fabs — **indistinguishable** from one that never existed (FR-004) |
| New statuses | **403** (a fab not held, or holding none) |

**404, not 403**, and the distinction is the usual one: the caller addressed an
*event*, so the answer is about that event, and "forbidden" would confirm it
exists. 403 is only for a caller naming a *fab*.

## `POST /events/manual` — submit by hand

**The write leak, and the most consequential endpoint in this feature.**

| | |
|---|---|
| `?fabId=` | required → **optional**, and now **checked** |
| Fab resolution | `FabResolution.ResolveForWriteAsync` — full ADR-0114 table |
| New statuses | **400** `EVENT_FAB_REQUIRED`, **403** |

| Caller holds | `?fabId=` | Outcome |
|---|---|---|
| exactly one fab | omitted | ingested into that fab (inferred) |
| several fabs | omitted | **400** `EVENT_FAB_REQUIRED` |
| any | a fab they hold | ingested into that fab |
| any | a fab they do **not** hold | **403**, and **nothing ingested** |
| no fab at all | either | **403** |

**Nothing ingested on refusal** (FR-007) is not a formality. This endpoint
enqueues onto the ingest channel; a partial acceptance would place a
fabricated event in another plant's stream, where it drives that plant's
automation rules and changes what its operators see. **Resolve the fab before
touching the channel.**

## `GET /events/dead-letters` — rejected deliveries

| | |
|---|---|
| `?fabId=` | **new**, optional |
| Fab resolution | `FabResolution.ResolveForReadAsync` |
| Scoping | only rejected deliveries whose fab is in the caller's fabs |
| Unattributed rows | returned to **nobody** — `NULL` satisfies no `IN` (FR-011) |
| New statuses | **403** |

The `limit` parameter is unchanged.

**Every row here carries `rawPayload`** — the production data verbatim and
unvalidated. That is why this endpoint is P1 rather than housekeeping.

## `POST /events/webhook/{integrationName}` — exempt, deliberately

**Unchanged (FR-014).** Its caller is a machine presenting its own credentials,
not an operator with a session, and the JWT path already refuses a fab the
caller does not hold:

```csharp
string targetGroup = "/fabs/" + fabId;
```

Same exemption shape as spec 016's `POST /streams/authorize`. Recorded here
rather than left as an unexamined endpoint.

> **This endpoint and `POST /events/manual` live in the same file and take the
> same `?fabId=` parameter. One checks it and one does not.** That symmetry is
> presumably how the gap survived, and it is the thing most likely to get
> mis-edited while implementing this feature: only the manual write changes.

## `/webhook-integrations` — out of scope

`POST /`, `GET /`, `DELETE /{name}` — **unchanged (FR-016).** The
`WebhookIntegration` aggregate has no fab, and whether it should is a real
question with two coherent answers: the per-delivery credential check already
proves entitlement, so an integration may legitimately be a shared template.
Settling it here would widen a feature whose purpose is closing a live leak.

## Wire

No response shape changes. `EventDto` and `DeadLetterDto` are unchanged — the
fab decides *who may ask*, not what comes back.

`DeadLetterDto` deliberately does **not** gain a `fab` field: every row a
caller can see is in a fab they hold, so it would tell them nothing they did
not already know, and it is one more place for an unattributed row to leak
through.
