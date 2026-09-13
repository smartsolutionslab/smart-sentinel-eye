# Spec 143 — A registry to be unknown of

**Issue:** #1972 (from spec 047's audit of ADR-0000 decision 018)
**Branch:** `feat/1972-event-registration-registry`
**Phase:** 1 (Specify) · **Date:** 2026-09-13 · **Tree read at:** `06db93bf`
**Feature bucket:** spec `specs/006-event-ingestion/`
**ADRs:** ADR-0000 decision **018** (the hybrid registration model — the decision
this implements the first third of), ADR-0130 (the audit that found it unbuilt and
did *not* amend 018), ADR-0092 (per-aggregate Domain folder), ADR-0093
(per-message-kind Application folder), ADR-0038 / ADR-0046 / ADR-0066 (hand-written
value objects, `IValueObject<T>`, `.From(...)` + `Ensure.That(...)`), ADR-0039 /
ADR-0090 (Guid v7 identifiers with the `Identifier` suffix), ADR-0091 / ADR-0094
(no shortcuts; identifier-typed properties named after the noun), ADR-0047 /
ADR-0089 (`Result<T, Error>` + `ApiError(Code, Message, HttpStatusCode)`),
ADR-0105 (`Ensure.That`), ADR-0070 (minimal APIs only), ADR-0042 / ADR-0057
(hand-rolled `ICommandHandler`/`IQueryHandler` + Wolverine dispatcher), ADR-0040 /
ADR-0073 (domain events are not integration events), ADR-0043 / ADR-0113 (two-layer
optimistic concurrency — `If-Match` plus the EF token), ADR-0142 (caller-supplied
`Idempotency-Key`), ADR-0143 (`POST` is not retried by default), ADR-0067
(`MigrationRunner`), ADR-0051 (per-context `Add<Context>{Infrastructure,Api}`),
ADR-0103 (integration tests on the Aspire fixture, no Testcontainers), ADR-0052 /
ADR-0053 / ADR-0054 (xUnit + Shouldly, sentence-style names, hand-written builders),
ADR-0065 (coverage gates), ADR-0084 (code metrics), ADR-0109 (disjoint files for
`[P]`), ADR-0139 / ADR-0144 (new behaviour starts red; the lane may not skip phase
4a and may not write an ADR), ADR-0037 (the phased workflow).
**Constitution:** §II (value objects), §III (bounded-context isolation, and bullet 5
— the Event Ingestion charter), §IV (the latency budget — **not touched**, argued in
§7), §VIII (safe by default at trust boundaries — the bullet this spec changes),
§Testing.

**A naming collision, disambiguated once.** "**Decision 018**" throughout this
document means row 018 of `docs/adr/0000-initial-decisions.md`. "**Spec 018**"
means `specs/018-event-fab-scoping/`, which is unrelated. Code comments in
`src/EventIngestion` that say "spec 018" mean the latter.

---

## 0. The ADR question, answered before anything else

ADR-0144 forbids this lane from writing an ADR or amending the constitution, and
names spec 047's audit issues as the family whose *"honest first output is an
ADR"*. #1972 is one of that family, so the question is owed an answer before a
line of spec.

**The answer is no ADR, for the slice scoped here.** Three checks, each run
rather than assumed.

### 0.1 Decision 018 is Locked and unamended

`docs/adr/0000-initial-decisions.md:32`, verbatim:

> | 018 | External Event ingestion = hybrid registration model. Each source has a
> `strict` flag (rejects unknown event types, schema-validates known) or
> `discovery` flag (accepts unknown, quarantines them in an inspector UI for
> promotion to the registry). Validated events feed rules / overlays; quarantined
> events are audit-only. | Locked |

The Status cell reads `Locked` with no `**Amended by …**` prefix and no
`(Superseded by …)` marker — contrast rows 008, 009, 014, 017, 019, 023, 026 and
027, which ADR-0130 marked `Amended`, and row 004, `Superseded`. A grep of all of
`docs/adr/` for `quarantine|discovery flag|strict flag|event.type registry|
registered event type` returns decision 018 itself and nothing else. **No ADR
names 018 as amended, and no ADR covers the registry.** The decision stands as
written and wants implementing, which is what the issue says and what the label
on it says.

### 0.2 The constitution already charters this exact thing

`.specify/memory/constitution.md:90`, §III bullet 5 — the Event Ingestion context
charter:

> 5. **Event Ingestion** — event-type registry, REST + AMQP ingress, schema
>    validation, hybrid strict/discovery model per source.

So "does an event-type registry belong in EventIngestion" is not an open
architectural question. It is written down, in the section that assigns
responsibilities to the nine contexts. Building it here applies a decision; it
does not make one.

### 0.3 The part that *would* need a decision is the part this spec leaves out

Decision 018 has three moving parts, and they are not equally decided:

| Part | Decided? | In this spec? |
|---|---|---|
| An event-type registry to be unknown *relative to* | **Yes** — §III bullet 5 and 018 both name it; nothing constrains its shape beyond the house DDD rules | **Yes** |
| A per-source `strict` / `discovery` mode that changes what ingest does | **Partly.** 018 says *"each source"*; `Source` is a closed four-value VO; but nothing says whether the mode is keyed `(fab, source)`, per `WebhookIntegration`, or per device — and **spec 006 §IX's forward-compat claim says `source` + `kind` are "extensible string fields, not closed enums"**, which strict mode contradicts | **No** — §10 |
| Schema validation of known types | **No, and it is refused twice on the record.** `specs/006-event-ingestion/spec.md:256` (FR-005, *"`payload` schema is **not validated** by ingestion"*), `:402` and `:415` (out of scope, *"a follow-on spec"*). ADR-0059 makes validation hand-rolled `Ensure.That`; there is no JSON-Schema precedent anywhere in the repo | **No** — §10 |

**The registry alone requires no decision. The mode and the schema validation
each require one.** That is the boundary this spec is cut along, and it is cut
there for that reason rather than for size. Both follow-ups are named in §10 with
the question each has to answer first.

**Consequence, stated plainly so it is not discovered at phase 6:** this spec
closes issue #1972 only in part. The issue itself opens with *"Probably more than
one feature, and the registry is the foundation"* and numbers three. This is
number one. The PR should say so and the issue should stay open, or be closed
against the two follow-ups filed in its place.

---

## 1. The premise, checked

Every grep in #1972 was re-run against `06db93bf`:

```
grep -rn -i "strict|quarantin|EventTypeRegistry|RegisteredEventType|unregistered|unknownEvent|promote" \
  src/EventIngestion --include=*.cs | grep -v obj/
```

Four hits, all of them false positives on the word *strict*: `EventPageDto.cs:6`
("strictly after the previous one"), `IProvisionedFabSource.cs:22` ("the grammar
is a strict allow-list"), `ListEventsQueryHandler.cs:120` ("Strict 'less than'"),
and `Kind.cs:8` ("restricted zone" inside an example event name). Zero hits for
`quarantine`, `EventTypeRegistry` or `RegisteredEventType` anywhere in `src/` or
`apps/`. **The premise holds: there is no registry, no per-source mode, no
promotion path, and `Kind` is validated for *grammar* and never for
*membership*.**

`Kind.cs:6-11` documents the gap itself:

> Application-level event kind tag (e.g. `PlcCycleStart`,
> `PersonInRestrictedZone`). Per spec 006 the field is required on every event but
> **never validated against a schema**; downstream consumers match on it. Grammar:
> `^[A-Z][A-Za-z0-9]{0,127}$` (PascalCase, 1-128 chars).

### The one correction the audit already made, carried forward

The audit corrected its own first pass, and the correction matters for §10 rather
than for this slice (`specs/047-the-decisions-we-made/audit.md:264`):

> | Quarantine of unknown events | **Partly — under another name** *(corrected)* |
> `src/EventIngestion/Domain/DeadLetter/DeadLetter.cs` captures rejected
> deliveries with topic, payload and error, documented *"Audit-only — no
> fan-out"*, listable via `ListDeadLettersQuery`. **This is quarantine for what
> *fails*; decision 018 is about what is *unknown*.**

So quarantine *storage* half-exists. `DeadLetter` is write-only
(`IDeadLetterRepository` has `Add` and `SaveAsync` and no read method), raises no
domain event, has no state transitions, and its `RejectionReason` is free text
with no discriminator — so "quarantined, then promoted" is not expressible on it
as it stands. That is a finding for follow-up 2, not a blocker here.

---

## 2. The slice, and what it deliberately is not

**What ships:** a fab-scoped registry of the event types a fab expects, with a
create, a list and a retire. Nothing reads it.

**What does not ship, and this is the load-bearing sentence:** *no ingest path
changes*. `IngestEventCommandHandler`, `IngestEventBatchCommandHandler`,
`MqttSubscriberHostedService` and `EventsEndpoints.Writes.cs` are not touched.
An unknown `Kind` is ingested exactly as it is today. `source` and `kind` remain
extensible string fields, so spec 006 §IX's forward-compat claim is untouched,
and §IV's `Event → overlay state` leg gains no work (§7).

A registry nothing enforces sounds inert. It is not: it is the thing that makes
"unknown" a *decidable* question, and neither the mode, the quarantine nor the
promotion flow can be specified until there is something to be unknown of. The
audit's own words for the absence — *"There is no registry to be unknown
relative to"* — are what this spec answers, and the title is taken from them.

---

## 3. User stories

### US1 (P1) — An operator declares the event types a fab expects

**As** the engineer responsible for a fab that takes events from third-party
systems,
**I want** to write down which event types this fab is supposed to produce, and
read the list back,
**so that** "is `PersonInRestrictedZone` a type we expect from Dresden?" has an
answer in the system rather than in someone's head — and so that the strict /
discovery mode that comes next has a set to compare against.

**Independent test:** §6, steps 1–6. Register two types for a fab, list them,
list from a fab that has none, and observe the registration survive a restart.

**Why P1 and why it is the whole of the shippable core:** it is the smallest
thing that is both useful on its own (a queryable declaration of intent, audited
by the operator who made it) and a precondition for everything else in decision
018.

### US2 (P2) — A wrongly-registered type can be taken back out

**As** the same engineer, having registered `PesonInRestrictedZone` with the `r`
missing,
**I want** to retire that entry and register the correct one,
**so that** a typo is not a permanent row in the declaration the mode will later
enforce.

**Independent test:** §6, steps 7–9.

**Why P2 rather than a separate spec:** it is two methods on an aggregate that
US1 already creates, one endpoint, and one index predicate. Shipping US1 without
it would ship an operator-facing registry with no way to correct it — a defect,
not a smaller slice. It is marked P2 so that if the PR grows past review size,
this is what leaves.

---

## 4. Acceptance scenarios

### Happy path — a type is registered and comes back in the list

```gherkin
Given an operator holding fab "dresden" and scope sse.events.types.write
When they POST /event-types with { "kind": "PersonInRestrictedZone" }
Then the response is 201 Created
And the Location header is /event-types/PersonInRestrictedZone
And the body is the bare eventTypeId — `IdempotentRequest.ExecuteCreateAsync`
    (FR-008) answers `Results.Created(location, identifier)`, the same shape
    every other creator on this pattern returns; the kind and fab are read
    back from the Location header and the list, not the create response
When they GET /event-types
Then the response is 200 OK
And the list contains exactly one entry, kind "PersonInRestrictedZone",
    fab "dresden", state "Registered", carrying registeredAt, registeredBy
    and version 0
```

### Conflict — the same type twice in one fab is refused

```gherkin
Given "PersonInRestrictedZone" is already registered for fab "dresden"
When an operator holding "dresden" POSTs /event-types with the same kind
Then the response is 409 Conflict
And the problem title is EVENT_TYPE_ALREADY_REGISTERED
And GET /event-types still shows exactly one entry for that kind
```

```gherkin
Given "PersonInRestrictedZone" is already registered for fab "dresden"
When an operator holding "munich" POSTs /event-types with the same kind
Then the response is 201 Created
And each fab's list shows exactly one entry
```

*The second block is the point of the first: uniqueness is per fab, not global.
`webhook_integrations` is the opposite — globally unique, because its name is a
route path segment. A `Kind` is not.*

### Conflict — a retire against a stale version is refused

```gherkin
Given "PersonInRestrictedZone" is registered for fab "dresden" at version 0
And another operator has already retired and re-registered it
When the first operator sends DELETE /event-types/PersonInRestrictedZone
    with If-Match: 1
Then the response is 409 Conflict
And the problem title is EVENT_TYPE_STALE
```

### Bad request — a kind that is not a kind

```gherkin
Given an operator holding fab "dresden"
When they POST /event-types with { "kind": "person in restricted zone" }
Then the response is 400 Bad Request
And the problem title is EVENT_TYPE_INVALID_INPUT
And the detail names the grammar Kind.From enforces
And nothing is written
```

```gherkin
Given an operator holding fab "dresden"
When they send DELETE /event-types/not%20a%20kind
Then the response is 400 Bad Request
And the problem title is EVENT_TYPE_INVALID_INPUT
```

```gherkin
Given an operator who holds both "dresden" and "munich"
When they POST /event-types without naming a fab
Then the response is 400 Bad Request
And the problem title is EVENT_FAB_REQUIRED
```

### Bad request — a retire with no precondition

```gherkin
Given "PersonInRestrictedZone" is registered for fab "dresden"
When an operator sends DELETE /event-types/PersonInRestrictedZone
    with no If-Match header
Then the response is 428 Precondition Required
And the entry is still registered
```

### Auth — the registry is not writable by an ingesting client

```gherkin
Given a caller with no bearer token
When they POST /event-types
Then the response is 401 Unauthorized

Given a caller holding sse.events.write but not sse.events.types.write
When they POST /event-types
Then the response is 403 Forbidden
And nothing is written

Given an operator holding only fab "dresden"
When they POST /event-types?fabId=munich
Then the response is 403 Forbidden
And the problem title is RESOURCE_FAB_NOT_AUTHORIZED
And munich's list is unchanged
```

*The middle block is why a new scope exists rather than reusing
`sse.events.write` — see FR-010.*

### Cross-fab — a retire addressed at someone else's row is a 404, not a 403

```gherkin
Given "PersonInRestrictedZone" is registered for fab "munich" only
When an operator holding only "dresden" sends
    DELETE /event-types/PersonInRestrictedZone with If-Match: 1
Then the response is 404 Not Found
And munich's entry is unchanged
```

*Mirrors `RevokeWebhookIntegrationCommandHandler`: a row outside the caller's
fabs is reported as absent rather than forbidden, so the endpoint does not
confirm the existence of rows the caller may not see.*

### The quiet case — ingest is unchanged

```gherkin
Given fab "dresden" has no registered event types at all
When an event with kind "SomethingNobodyDeclared" is ingested by any path
Then it is stored and fanned out exactly as before this spec
And no dead letter is written
And nothing in the registry is read or written
```

*This scenario exists to be asserted, not merely stated. It is the guard that
catches a phase-4 engineer who "finishes" decision 018 by wiring the registry
into the ingest path, which this spec does not authorise (§0.3, §10).*

---

## 5. Functional requirements

**FR-001 — The registry is an aggregate in EventIngestion, keyed by fab and
kind.** A `RegisteredEventType` carries the fab it belongs to, the `Kind` it
names, a lifecycle state, when it was registered and by whom, and an
`AggregateVersion`. It carries **no** `Source`: decision 018 says strict mode
*"rejects unknown event **types**"*, and its promotion path promotes *into the
registry*, singular. A per-source registry would make a type promoted from one
source still unknown to another, which is not what the sentence says. Recorded
here because it is a reading, and the mode follow-up is where it gets tested.

**FR-002 — `Kind` is reused, not re-modelled.** The registry is a registry *of*
`src/EventIngestion/Domain/Event/Kind.cs`. Its existing grammar
(`^[A-Z][A-Za-z0-9]{0,127}$`) is the validation at the boundary; there is no
second spelling of an event-type name in the codebase after this spec, and there
must not be.

**FR-003 — Registration is per fab and the fab is never taken unchecked.**
`POST /event-types` resolves the fab through
`EventIngestionFabResolution.ResolveWriteFabAsync` exactly as
`POST /webhook-integrations` does (ADR-0114): omit `fabId` and hold exactly one
and it is inferred; hold several and omit it and the answer is
`400 EVENT_FAB_REQUIRED`; name one you do not hold and the answer is
`403 RESOURCE_FAB_NOT_AUTHORIZED`.

**FR-004 — Uniqueness is enforced twice, per spec 086.** An application-level
lookup produces an answer an operator can act on
(`409 EVENT_TYPE_ALREADY_REGISTERED`), and a **partial unique index**
`ux_registered_event_types_fab_kind` on `(fab, kind) WHERE state <> 'Retired'`
guarantees the invariant under concurrency. `UniqueConstraintExceptionHandler`
already maps SQLSTATE 23505 to `409 RESOURCE_ALREADY_EXISTS`, so the race loses
correctly without new plumbing. Retiring releases the name — the same shape as
`ux_system_variables_*` (`VariableConfiguration.cs:116`,
`HasFilter("state <> 'Archived'")`).

Two requests that genuinely race each other therefore surface **two different
problem titles for the same condition** — whichever loses the app-level lookup
gets `EVENT_TYPE_ALREADY_REGISTERED`, whichever loses only the index gets
`RESOURCE_ALREADY_EXISTS` — both `409`, and an operator sees whichever one their
request happened to lose on. This is expected, not a bug a reviewer should file:
tests assert the status, not the title, wherever a race is exercised.

**FR-005 — Retire is a state transition, not a delete.** The row stays; `State`
moves `Registered → Retired`. Retiring an entry that is already retired, or that
belongs to a fab the caller does not hold, is `404` — the lookup is over
registered entries in the caller's fabs, so both cases are genuinely "not
found" from where the caller stands.

**FR-006 — Retire requires `If-Match`, register does not.** ADR-0043 / ADR-0113:
a write that addresses an existing row carries the expected version, read with
`ConcurrencyHeaders.TryReadExpectedVersion`; absent ⇒ `428`; stale ⇒
`409 EVENT_TYPE_STALE`. There is no retry-on-conflict. A create
addresses no existing row and takes no precondition. **The header is read, and
`428` answered, before the row is looked up** — deliberately, not incidentally:
looking the row up first would make the 428-vs-404 choice an existence oracle
for kinds in fabs the caller cannot read (a caller who omits `If-Match` against
someone else's row would learn, from the status code alone, whether that row
exists). A missing header on a row in a fab the caller cannot read must answer
`428`, the same as one they can, never `404`.

**FR-007 — The list carries the version, because there is no single-resource
GET.** `GET /event-types` returns `IReadOnlyList<RegisteredEventTypeDto>` with
`eventTypeId`, `fab`, `kind`, `state`, `registeredAt`, `registeredBy` and
`version`. No cursor and no paging: the population is tens per fab, and
`ListWebhookIntegrationsQuery` is the precedent for an unpaged registry listing
in this context. Retired entries are **excluded** by default; a
`?includeRetired=true` flag is *not* added, because nothing needs it yet
(ADR-0036). **The list is scoped to the caller's readable fabs**, exactly as
`ListWebhookIntegrationsQuery` scopes its own list — resolved through
`EventIngestionFabResolution.ResolveReadFabsAsync`, not a bare unfiltered
query. A caller holding one fab must never see another fab's rows in the list,
even though no single-resource GET exists to 404 or 403 against.

**FR-008 — `POST /event-types` honours `Idempotency-Key`.** ADR-0142, and the
table it needs already exists in this context (migration
`20260903094940_AddIdempotencyKey`). `IdempotencyHeaders.TryRead` plus
`IdempotentRequest.ExecuteCreateAsync`, scoped with `IdempotencyScope.For(key,
"POST /event-types", callerIdentifier)` — the caller is part of the scope
because keys are strings callers invent and `"1"` will collide. With no key the
behaviour is unchanged, including the `409` a genuine duplicate has always
earned.

**FR-009 — `POST` and `DELETE` are not retried by the client stack.** ADR-0143's
default holds; nothing here calls `RetryEveryMethod()`.

**FR-010 — The registry write needs its own scope: `sse.events.types.write`.**
Reusing `sse.events.write` would be a security defect, not a shortcut: that scope
is held by every webhook-integration service account and every MQTT publisher
persona, so reusing it would let an *event source* declare which event types are
legitimate. Under the strict mode that follows, that is a source self-authorising
its own types. `sse.webhooks.write` is the precedent for a distinct write-only
admin scope in this context.

**A testing gotcha this FR creates, worth stating so nobody re-derives it under
deadline.** Every existing `ClientFor(...)`-style integration test client in
this repo mints against a client (`management-web`, or the `sse.management`
grandfather bundle) whose **default** Keycloak client scopes already include
the entire `sse.*` catalogue — the `scope` parameter passed to a client-
credentials grant only *narrows among a client's declared optional scopes*, it
cannot subtract a default one. A token minted this way holds
`sse.events.types.write` regardless of what the test asks for, once this FR's
grant lands, so it can never demonstrate the negative case ("a caller who does
NOT hold the new scope is refused"). Proving the negative needs a client whose
default scopes are narrow by construction — an event-source-shaped client
(webhook integration or MQTT persona), planted for the test and torn down
after, holding only `sse.events.write`. The existing four-token-mint clients in
this suite are the wrong tool for this one assertion.

**FR-011 — The registry read reuses `sse.events.read`.** Reading which event
types a fab expects is within the events read surface, it is granted to the
personas who already read events, and a second new scope would double the realm
surface for no separation that matters. *This is the one place where a reader who
disagrees should say so at the phase-1 gate; it is cheap to split later and
expensive to un-split.*

**FR-012 — Domain events are raised and no integration event is published.**
`EventTypeRegisteredDomainEvent` and `EventTypeRetiredDomainEvent` are raised on
the aggregate per ADR-0092's `Events/` convention. Nothing subscribes. That is
not an oversight: `WebhookIntegrationRegisteredDomainEvent` and
`WebhookIntegrationRevokedDomainEvent` are raised by the sibling aggregate in the
same context and are likewise consumed by nobody but their own domain tests
(verified by grep across `src/` and `tests/`). Publishing a `*V1` would oblige a
line in `AuditObservability`'s `IntegrationEventAuditHandler` — a cross-context
file, pinned by `Every_integration_event_has_an_audit_handler` — for an audience
that does not exist (ADR-0036, ADR-0040, ADR-0073).

**FR-013 — Ingest is untouched, and a test says so.** No file under
`Application/Commands/Handlers/IngestEvent*`, `Application/Ingress/`,
`Infrastructure/Ingress/` or `Api/EventsEndpoints.*` is modified. The registry is
not read on any ingest path.

**FR-014 — Constitution §VIII's first bullet is corrected to describe what now
exists.** It currently reads *"there is **no event-type registry**, no per-source
strict/discovery mode and no promotion path"*. After this spec the first clause
is false. The edit removes that clause and leaves the other two standing, with
the follow-up issue numbers beside them. **This is a factual status correction,
not an amendment:** no principle changes, no rule changes, no obligation is
added or removed — the same class of edit as keeping §IV's leg table current,
which CLAUDE.md and §IV itself both require on pain of *"exempting itself by
clerical error"*. It is called out here, and again in `tasks.md`, so a reviewer
who reads it differently can stop the PR at the phase-1 gate rather than at
phase 7. **If that reading is rejected, drop FR-014 and file the constitution
edit as a separate human-owned issue; nothing else in this spec depends on it.**

---

## 6. Independent end-to-end test procedure

Runnable by a person with the repo, Docker and a browser. Unit tests prove the
aggregate's invariants and the handlers' branches; integration tests prove the
endpoints answer. What neither proves is that a registration made through the API
is still there after the process that made it is gone — which is the whole claim
of a registry.

Stop any running AppHost first (a running stack holds the service binaries;
MSB3027 looks exactly like a broken build).

1. `dotnet run --project src/AppHost` — one stack per machine. Wait for
   `event-ingestion` to report healthy.
2. Mint a token from **Aspire's proxied Keycloak endpoint**, not the container's
   mapped port, for `op-dresden@dresden.test` / `Operator1234`.
3. `POST /event-types` with `{"kind":"PersonInRestrictedZone"}`. Expect **201**,
   a `Location` of `/event-types/PersonInRestrictedZone`, and an id in the body.
4. `POST /event-types` again with the same body. Expect **409**
   `EVENT_TYPE_ALREADY_REGISTERED`.
5. `POST /event-types` with `{"kind":"PlcCycleStart"}`. Expect **201**.
   `GET /event-types`. Expect **200** and exactly the two entries, each at
   `version` 0, `state` `"Registered"`, `registeredBy` the operator's subject.
6. **The restart step, which is the one that matters.** In the Aspire dashboard,
   restart the `event-ingestion` resource (use `WaitOnResourceUnavailable` if
   scripting it — the default wait gives up on the transition it should watch).
   Re-`GET /event-types` with a fresh token. Expect the same two entries,
   unchanged. *A registry that does not survive this is a cache.*
7. `DELETE /event-types/PlcCycleStart` with **no** `If-Match`. Expect **428**.
8. `DELETE /event-types/PlcCycleStart` with `If-Match: 0`. Expect **200**.
   `GET /event-types`. Expect one entry.
9. `POST /event-types` with `{"kind":"PlcCycleStart"}` again. Expect **201** —
   the partial index released the name (FR-004). `GET /event-types` shows two
   entries again, the new one at `version` 0.
10. **The quiet step (FR-013).** `POST /events/manual` with
    `{"deviceId":"proc-1","kind":"NothingDeclaredAnywhere","occurredAt":"<now>",
    "payload":{}}`. Expect **201**, and `GET /events?kind=NothingDeclaredAnywhere`
    to return it. **Ingest does not consult the registry, and this step is how a
    reader confirms that rather than taking §2's word for it.**
11. **The auth step.** Mint a token for a webhook integration's service account
    (which holds `sse.events.write`) and `POST /event-types` with it. Expect
    **403** — FR-010's whole reason for existing.

**If step 6 returns an empty list**, the rows are being written to a
non-persisted store or the migration did not run — the only two causes, and the
only step that separates them from a working in-memory implementation.

**If step 11 returns 201**, the new scope was defined in `Scope.cs` but the
endpoint is still declaring `sse.events.write`, or the legacy `sse.management`
bundle is satisfying the policy for a token that should not hold it. Both are
review blockers.

---

## 7. Latency-budget impact — none, and the reason is structural

Constitution §IV binds any change *on the event-to-overlay path*. Spec 103 §6
established that `IngestEventCommandHandler` and `IngestEventBatchCommandHandler`
sit inside the ≤ 200 ms `Event → overlay state` leg.

**This spec modifies neither, nor any other file on that path** (FR-013). The
three endpoints it adds are an operator-facing CRUD surface reached by a human
with a browser; no event, no overlay and no kiosk touches them. Nothing is read
from the registry during ingest, because nothing reads the registry at all.

**Leg touched: none. N/A, and N/A by construction rather than by measurement.**

**§IV's table is unchanged and phase 4 must not edit it.** No leg moves state, no
cell moves column. Recording a figure here would be the "discharge nobody earned"
error §IV names. **§VII's dashboard obligation is likewise not engaged**: it binds
implemented latency legs, and this spec implements none.

**The follow-up that adds the mode is a different answer, and it should expect
to owe one.** Wiring a per-source mode into ingest puts a registry lookup inside
that ≤ 200 ms leg, on the hottest path in the system at 1 000 events/sec/fab. It
will need either an in-memory projection (the `RuleCacheSeederHostedService`
pattern) or a measured argument that the lookup is free. Named here so the
follow-up's author meets the constraint before writing the design rather than at
review.

---

## 8. Locked tech choices applied

Nothing new is introduced. Every choice below already exists in this context.

| Concern | Choice | Where the precedent is |
|---|---|---|
| Persistence | PostgreSQL + EF Core, plain CRUD | ADR-0130 (Marten permitted, unused); `WebhookIntegration` |
| Aggregate + VOs | Hand-written, `IValueObject<T>`, `.From(...)`, `Ensure.That(...)` | ADR-0038/0046/0066/0105 |
| Identifier | `RegisteredEventTypeIdentifier`, Guid v7, `Identifier` suffix | ADR-0039/0090; `WebhookIntegrationIdentifier` |
| State VO | `RegistrationState` record with static singletons, throwing `From` | `VariableState.cs`, `Source.cs` |
| Domain layout | `Domain/RegisteredEventType/` + `Events/` subfolder | ADR-0092 |
| Application layout | `Commands/`, `Queries/`, each with `Handlers/` + paired `*Errors.cs` | ADR-0093 |
| Mediator | Hand-rolled `ICommandHandler<T,R>` / `IQueryHandler<T,R>` | ADR-0042/0057 |
| Errors | `Result<T, Error>`, `ApiError(Code, Message, HttpStatusCode)`, a `*Failures` static because generics are invariant | ADR-0047/0089 |
| API | Minimal APIs, `RouteGroupBuilder`, `.Match<IResult>(onSuccess, onFailure: e => e.ToProblem())` | ADR-0070; `WebhookIntegrationsEndpoints` |
| Concurrency | `If-Match` + EF token, no retry | ADR-0043/0113 |
| Idempotency | `Idempotency-Key`, existing table | ADR-0142 |
| Migration | EF migration run by `MigrationRunner` | ADR-0067 |
| Nulls | NRT on; `Option<T>` for the repository lookup | ADR-0048/0141 |
| Tests | xUnit + Shouldly + hand-written fakes; integration on the Aspire fixture | ADR-0052/0053/0054/0103 |

---

## 9. Assumptions, marked per ADR-0036

- **A1 — Registry membership is `(fab, kind)`, not `(fab, source, kind)`.**
  Reasoned in FR-001 from decision 018's own words. If the mode follow-up
  concludes otherwise, the migration is a column add plus an index change, not a
  reshape. *Marked because it is the single reading in this spec that a later
  decision could overturn.*
- **A2 — A registered type carries no description and no schema.** Decision 018's
  registry exists to answer *membership* — "is this type known here". A human
  description and a payload schema are both real wants and both additive; neither
  is needed to answer that question, and ADR-0036 forbids building for a need
  that does not exist yet. Follow-up 4 in §10 is where the schema lands, and it
  is the one that needs an ADR.
- **A3 — Retirement records no `retiredAt` / `retiredBy` on the row.** It is
  carried on the raised domain event and stored nowhere, mirroring
  `Variable.Archive`. Nothing reads it. If an inspector UI later needs it, it is
  a column add.
- **A4 — `GET /event-types` needs no paging.** Tens of types per fab, mirroring
  `ListWebhookIntegrationsQuery`. If a fab ever registers thousands, it wants the
  cursor `ListEventsQueryHandler` already implements.
- **A5 — The existing seeded operator personas will hold the new scope.** The
  realm grant goes on the `management-web` client. Note that the legacy
  `sse.management` bundle satisfies every policy except `sse.events.publish`
  (`RequireScopeExtensions`), so a token holding it passes regardless — which
  means **the realm edit is about correctness, not about making the tests pass,
  and a green integration suite does not prove it landed.** `RealmIdentityTests`
  is what proves it.

---

## 10. Out of scope — named, with the follow-ups to file

Each of these is a separate issue. The lane must not build any of them in this
PR; two of them cannot be built by the lane at all.

**1. Per-source `strict` / `discovery` mode** — *suggested title:* **"A source
declares whether an unknown event type is refused or accepted"**. Wires the
registry into ingest. Needs a decision first: what a *source* is for this purpose
(`(fab, Source)`? per `WebhookIntegration`? per device?), and how strict mode
squares with spec 006 §IX's *"`source` + `kind` are extensible string fields, not
closed enums"*. Two insertion points, not one:
`IngestEventCommandHandler.HandleAsync` (manual, webhook, MQTT retry) and
`IngestEventBatchCommandHandler.Build` (the MQTT fast path) — or one collaborator
injected into both. **On the ≤ 200 ms `Event → overlay state` leg; owes §IV a
figure or an argument.** *Likely needs an ADR — flag it at that spec's §0.*

**2. Quarantine of unknown types under discovery** — *suggested title:* **"An
unknown event type is held where an operator can see it"**. `DeadLetter` is
mechanically reusable (nullable fab, unbounded payload, audit-only, no fan-out)
but has no machine-readable reason discriminator and no state transitions, so a
quarantine listing cannot be filtered from parse failures and "quarantined, then
promoted" is not expressible on it. Either a new column plus a reason code, or a
new aggregate. Depends on 1.

**3. An inspector UI and promotion** — *suggested title:* **"An operator
promotes a quarantined event type into the registry"**. `apps/management-web`,
frontend-engineer, plus a `POST /event-types` variant that promotes from
quarantine. Depends on 1 and 2. A read-only management-web view of *this* spec's
registry could be split off earlier and cheaply; it is not in this PR because
nothing in decision 018 needs it before the inspector does.

**4. Schema validation of known types** — *suggested title:* **"A registered
event type can declare the payload shape it promises"**. Refused twice on the
record by spec 006 (FR-005, and twice in its out-of-scope list). No JSON-Schema
precedent exists in the repo and ADR-0059 makes validation hand-rolled.
**This one needs an ADR before it needs a spec**, and the ADR has to reckon with
ADR-0139's exemption for opaque captured payloads — which is exactly the
exemption a schema-validating ingest would start to consume.

**Also out of scope:** anything under `apps/`; any change to `Shared.Contracts`;
any new integration event; any change to §IV's table; per-source rate limits;
and closing #1972 outright (§0.3).

---

## 11. `[NEEDS CLARIFICATION]`

**None.** Two items were candidates and both were resolved rather than deferred:

- *What "each source" means in decision 018* — resolved by **removing it from
  scope** (§0.3, follow-up 1) rather than by guessing. The one reading this spec
  does commit to is A1, and it is marked.
- *Whether the registry read deserves its own scope* — resolved as FR-011 with
  the reasoning shown and the reversal cost stated. A reviewer who wants it split
  should say so at this gate.

---

## 12. Gate — phase 1

- [ ] Spec reviewed; no `[NEEDS CLARIFICATION]` outstanding (§11).
- [ ] **§0's ADR answer accepted**: no ADR for this slice; follow-ups 1 and 4
      flagged as likely needing one.
- [ ] **FR-014's constitution edit accepted as a factual status correction**, or
      dropped and re-filed (the spec says how).
- [ ] FR-011's scope reuse accepted, or split into `sse.events.types.read`.
- [ ] Understood that this **partially** addresses #1972; three follow-ups get
      filed from §10.
- [ ] Phase 4a colour: **RED** (new behaviour throughout). See `plan.md`.
