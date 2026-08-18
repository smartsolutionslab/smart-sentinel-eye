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

## `POST /events/webhook/{integrationName}` — exempt, then amended

**Originally unchanged (FR-014)**, on the grounds that its caller is a machine
presenting its own credentials, not an operator with a session, and that the JWT
path already refuses a fab the caller does not hold:

```csharp
string targetGroup = "/fabs/" + fabId;
```

> **AMENDED 2026-08-18 (#1545).** That check exists only in the JWT branch.
> `StaticHash` — the default until an integration is rotated — matched the token
> hash and never looked at `fabId`, so a token issued for one plant could file
> events into another.
>
> | | |
> |---|---|
> | `?fabId=` | unchanged: still required, still names the target plant |
> | New check | the fab **must be the integration's own**, in *both* modes |
> | Where | `AuthenticateWebhookAsync`, before the envelope is built |
> | Refusal | **401**, identical to an unknown integration — so it cannot be used to discover that a name is taken in another plant |
>
> The check is on the integration's stored fab, not the caller's groups: a
> machine has no session to resolve, which is the whole reason this endpoint was
> exempt from the resolver in the first place.

Same exemption shape as spec 016's `POST /streams/authorize`. Recorded here
rather than left as an unexamined endpoint.

> **This endpoint and `POST /events/manual` live in the same file and take the
> same `?fabId=` parameter. One checks it and one does not.** That symmetry is
> presumably how the gap survived, and it is the thing most likely to get
> mis-edited while implementing this feature: only the manual write changes.

## `/webhook-integrations` — out of scope, then in it

Originally **unchanged (FR-016)**, because the `WebhookIntegration` aggregate
had no fab and whether it should was a question with two coherent answers.

> **AMENDED 2026-08-18 (#1545).** The premise fell with FR-014: the
> per-delivery check did *not* prove entitlement in `StaticHash` mode, so the
> aggregate now carries a `FabIdentifier` and the registry is scoped with it.
> Closing only the delivery side would have left one plant able to read
> another's integration names — and the version each needs to be revoked with,
> which stops that plant's machine ingest.
>
> | Endpoint | Change |
> |---|---|
> | `POST /` | `?fabId=` **new**, optional; resolved as a **write** (a multi-fab admin must choose). The integration is registered into that fab. **403**, **400** `EVENT_FAB_REQUIRED` |
> | `GET /` | `?fabId=` **new**, optional; scoped to the caller's fabs. **403**, **400** |
> | `DELETE /{name}` | scoped; another plant's integration is **404**, exactly as one that never existed. **403**, **400** |
>
> `WebhookIntegrationDto` gains `fab` — unlike `DeadLetterDto`, which
> deliberately does not. The difference is that this column is never null and
> every row a caller sees is already in a fab they hold, so it discloses
> nothing, and a multi-fab admin otherwise cannot tell two plants' integrations
> apart.
>
> **Names stay globally unique**, not per-fab: the name is the path segment of
> the ingest route, which has only the name to resolve by. So a name taken in
> another plant still answers `409` on registration and thereby discloses that
> it exists — the one residue, left on #1545 rather than closed by making the
> ingest route ambiguous.

## Wire

No response shape changes. `EventDto` and `DeadLetterDto` are unchanged — the
fab decides *who may ask*, not what comes back.

`DeadLetterDto` deliberately does **not** gain a `fab` field: every row a
caller can see is in a fab they hold, so it would tell them nothing they did
not already know, and it is one more place for an unattributed row to leak
through.
