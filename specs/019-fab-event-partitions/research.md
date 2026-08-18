# Research: A plant that exists can store its events

**Feature**: `019-fab-event-partitions` | **Date**: 2026-08-18

Five questions had to be settled before the design could be written. The
mechanism itself was **not** one of them — it was chosen before the spec
(derive from the identity groups, plus fail loudly), and this document takes
that as given and works out what it costs.

## R1 — Who is allowed to read the list of fabs

**Decision**: EventIngestion declares a port for "the fabs that exist";
**MigrationRunner** implements it by composing Identity's existing Keycloak
admin client. EventIngestion never references Identity.

**Why this shape and not a simpler one.** Keycloak federation belongs to
Identity (constitution §III, context 8), and `AllowedCrossContext` in
`BoundaryTests` is **empty** — no bounded context may reference another, at any
layer, and a violating PR cannot merge. So EventIngestion cannot ask Identity
for anything directly.

MigrationRunner is not a bounded context. It is the composition root for
migrations (ADR-0067) and already references all nine contexts' Infrastructure
projects. Putting the adapter there is the one place where both halves are
legally in scope, and it needs no new allow-rule — the boundary test's list of
context prefixes does not include it.

| Alternative | Why rejected |
|---|---|
| EventIngestion gets its own Keycloak client | Duplicates the capability Identity exists to own, and puts Keycloak knowledge in a context that has had none. A second implementation of "read the realm" is a second thing to get wrong. |
| MigrationRunner gets its own Keycloak client (not Identity's) | Legal, and simpler in wiring, but still a second implementation. Identity's client already handles token acquisition, refresh and the realm path. |
| Ask Identity's HTTP API | **Impossible by ordering.** MigrationRunner runs to completion *before* any Api service starts (`WaitForCompletion(migrations)` in AppHost). Identity's API is not up when the answer is needed. |
| A fabs table in EventIngestion, fed by an integration event | Nothing publishes such an event today, and it does not answer the first-deploy case: the table would be empty exactly when the partitions are first needed. |

**Consequence to accept**: Identity's `IKeycloakAdminClient` gains a
group-listing method. That is Identity's own surface growing by one read, not a
boundary being crossed.

## R2 — Which credential reads the groups

**Decision**: a **dedicated Keycloak client for MigrationRunner**, holding
`query-groups` and nothing else.

**Why not reuse `identity-admin`.** It already has `query-groups` and would
work today with no realm change. But it also holds `manage-users`,
`manage-clients`, `view-users` and `view-clients` — the rights Identity needs
to enrol devices and rotate webhook credentials. Handing that credential to a
job that only ever needs to list groups widens what a compromised migration
runner can do, for no benefit beyond saving one realm entry.

Spec 016 set the precedent in the opposite direction and recorded it as a cost:
ADR-0116's standing service account was "the one thing in spec 016 that widened
who can read the camera catalogue". The lesson from that is to make the new
credential as narrow as the job, not to avoid having one.

**Cost accepted**: one more client in the realm and one more secret in Helm.

**Corrected during implementation.** `query-groups` alone is not enough:
`GET /admin/realms/{realm}/group-by-path/fabs` answers **403** with it, verified
directly against Keycloak 26.5 with the realm imported. The working minimum is
`query-groups` **+ `view-users`**, which is what the realm now grants. That is
wider than intended and still far narrower than `identity-admin`, which can
create clients and users as well.

The first integration run found this the way the feature is designed to be
found: the migration job failed, no service started, and the whole stack
refused to come up rather than provisioning nothing and reporting success. An
inconvenient way to learn it, and the right one.

## R3 — The DDL trust argument changes, and must be re-argued

`EventPartitionRolloverMigrator` interpolates table names into DDL, because
Postgres cannot parameterise identifiers. Its current suppression of S2077 is
justified by provenance:

> `fabPartition` comes from `pg_class.relname` via `DiscoverFabPartitionsAsync`
> — a constant catalog query, so it can only name a table that already exists

**That argument does not survive this feature.** The names now originate in a
Keycloak group path, which is administrator-controlled rather than
database-derived.

**Decision**: replace provenance with **validation**. Every group name is
parsed through `FabIdentifier.From` before it reaches any statement, and only
the parsed value is used. The grammar `^[a-z][a-z0-9-]{1,31}$` is a strict
allow-list: no quote, semicolon, whitespace, comment marker, backslash or
non-ASCII character can pass it, and length is bounded to 32. A name that fails
is skipped (FR-005), so nothing unvalidated ever reaches the DDL.

This is stronger than the argument it replaces, not weaker: the old one relied
on the catalog being trustworthy, the new one relies on a grammar that excludes
the entire attack alphabet. The comment in the migrator must be rewritten
rather than left — a stale justification is worse than none, because the next
reader will trust it.

## R4 — What happens when Keycloak cannot be reached

**Decision**: the run **fails**. It does not proceed as though no fabs exist.
AppHost gains `.WaitFor(keycloak)` on the migrations resource so the ordinary
case is "wait", not "fail".

**The trade, stated plainly.** This makes a Keycloak outage block a deployment
— including for fabs whose partitions already exist and would have been fine.
That is a real availability cost on a 24/7 system, and it was weighed against
the alternative: treating an unreachable realm as an empty list, which
provisions nothing, reports success, and leaves exactly the silent gap this
feature exists to close. "No fabs" and "cannot tell" are indistinguishable from
inside the process and mean opposite things.

It is also less costly than it first appears: no service in this system can
authenticate a single request without Keycloak, so a deployment that completes
while Keycloak is down produces a stack that cannot serve anyone. The failure
is being moved earlier and made legible, not created.

**Bounded**: the client uses ServiceDefaults' standard resilience handler, so
the wait is a bounded retry rather than an indefinite hang (FR-012).

## R5 — How an event that cannot be stored is refused (FR-007)

This is the hardest requirement in the spec, because of an ordering the spec
deliberately does not mention: `POST /events/manual` answers **202 Accepted**
the moment the envelope is queued, and persistence happens later on a
background loop. By the time Postgres raises `23514`, the response is long
gone. Nothing downstream can un-accept it.

**Decision**: check **before** enqueuing, against the database rather than
against Keycloak.

The endpoint asks a `IFabStorageReadiness` port whether storage exists for the
resolved fab, and refuses before touching the ingest channel — the same
ordering FR-007 of spec 018 imposed on the fab-authorization check, for the
same reason: a refusal that has already enqueued is not a refusal.

**Why the database and not Keycloak.** The actual precondition is "a partition
exists", not "a group exists" — those differ for exactly as long as this
feature's gap is open, which is the window that matters. Asking Postgres also
keeps Keycloak off the request path entirely, so nothing about ingest
availability or latency becomes coupled to the realm.

**Cost**: a catalog lookup per write. Mitigated by caching the set of
provisioned fabs in memory with a short TTL, and re-reading on a miss before
refusing — so a fab provisioned seconds ago is not refused by a stale cache,
while the common path stays a set lookup. The set changes at most when a plant
is created.

**Status code**: **503**, not 400 and not 403. The caller's request is
well-formed and they are entitled to that fab; the system is not ready to
store it, and the condition is temporary by construction — the next
provisioning run fixes it. A `Retry-After` is not promised, because how long is
genuinely unknown.

## R6 — The relationship to #1546, kept honest

#1546 is the general defect: the persistence loop's broad catch drops an
envelope it has already accepted. A missing partition is **one cause** of it.

**Decision**: this feature makes that cause rare (R1–R4) and refuses it up
front (R5), and additionally makes `23514` **distinguishable** in the loop's
logging so it is never again indistinguishable from an arbitrary dispatch
fault. It does **not** fix the general case, and must not be read as having
done so.

A race survives on purpose: a partition dropped by hand between the readiness
check and the insert still lands in the loop. That path is why the
distinguishable log is required here rather than deferred wholesale to #1546 —
the residue must be legible even while the general fix is outstanding.

**Explicitly not done here**: dead-lettering the envelope, or retrying it. Both
are answers to #1546's question of what a loop should do with an envelope it
cannot persist, and answering it in passing — for one cause only — would make
the general fix harder by giving the loop two behaviours to reconcile.
