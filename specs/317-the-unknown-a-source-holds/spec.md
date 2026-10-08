# Spec 317 — The unknown a source holds

**Issue:** #2325 — *"An unknown event type is held where an operator can see it, and
can be promoted into the registry"* (follow-ups 2 and 3 of
`specs/143-a-registry-to-be-unknown-of/spec.md` §10)
**Reads:** spec 143 (the registry, #1972) and spec 269 (the per-`(fab, Source)`
mode, #2324, PR #2706 merged)
**Branch:** `feat/2325-quarantine-unknown-event-types`
**Phase:** 1 (Specify) · **Drafted:** 2026-10-08 · **Tree read at:** `ba37ca5a`
**Lane:** autonomous (ADR-0144, `agent:ready`). Halted at the phase-1 gate by
§11 Q1 on 2026-10-08; **Q1 answered by the user the same day (option A)**, recorded
on #2325. `plan.md` and `tasks.md` follow.
**Feature bucket:** spec `specs/006-event-ingestion/`
**ADRs:** ADR-0000 decision **018** (this is its last third: *"`discovery` flag
(accepts unknown, quarantines them in an inspector UI for promotion to the
registry) … quarantined events are audit-only"*), ADR-0130, ADR-0092 / ADR-0093
(layout), ADR-0038 / ADR-0046 / ADR-0066 (value objects), ADR-0047 / ADR-0089
(`Result` + `ApiError`), ADR-0105 (`Ensure.That`), ADR-0070 (minimal APIs),
ADR-0042 / ADR-0057 (handlers), ADR-0040 / ADR-0073 (domain vs integration
events), ADR-0067 (`MigrationRunner`), ADR-0103 (Aspire fixture), ADR-0109
(disjoint files), ADR-0117 (§VII binds implemented legs), ADR-0139 / ADR-0144 (new
behaviour starts red), ADR-0141 (`Option<T>`), ADR-0036 (no speculative
generality), **ADR-0107 / ADR-0168** (the operator MFE re-architecture — why the
inspector UI is split out, §2.1).
**Constitution:** §II, §III bullet 5 (Event Ingestion charter: *"hybrid
strict/discovery model per source"*), **§IV (engaged — §7)**, §VIII (its first
bullet describes this exact gap and is corrected by FR-012), §Testing.

**Naming collision, carried from specs 143/269:** "Decision 018" is row 018 of
`docs/adr/0000-initial-decisions.md`; "spec 018" is `specs/018-event-fab-scoping/`.

---

## 0. Decisions already taken, and the one that is not

### 0.1 The quarantine shape — decided by the user (2026-10-08)

Recorded on #2325:

> Decision (user, 2026-10-08): quarantine shape is a new column + reason code on
> the existing row, not a new aggregate — cheaper, reuses the existing table; the
> reason code explains why it's held.

This spec implements that and does not reopen it. The existing row is
`DeadLetter` (`src/EventIngestion/Domain/DeadLetter/DeadLetter.cs`, table
`dead_letters`). No new aggregate, no new table. FR-001–FR-003 say exactly which
columns.

### 0.2 What happens to the *default* population — **decided by the user: option A**

> Decision (user, 2026-10-08, relayed and recorded on #2325): option A — only
> sources that explicitly declared `discovery` mode quarantine. Undeclared sources
> keep today's open behaviour (events flow through as normal); "undeclared"
> becomes a distinct third state, separate from both strict and declared-discovery.
> It overturns spec 269's accepted wording and constitution §VIII's current text
> for undeclared sources specifically — recorded as a documented note needed, not
> a weakening.

The analysis that put the question to the user is kept below, because it is why
the three-state model exists.

Spec 269 made **an undeclared `(fab, Source)` behave as `discovery`** (its FR-003,
A3), and its gate accepted that. Spec 269 A3 then left this spec a warning,
verbatim:

> once quarantine lands, "discovery" stops meaning "fanned out as today". Because
> discovery is the **default**, #2325 will change behaviour for **every undeclared
> source**. An unknown kind that reaches rules today will stop reaching them. That
> spec must treat it as a behaviour change for the default population and decide
> it on purpose.

The facts that make this a human call rather than a design detail:

- **No fab has registered anything.** Spec 143's registry shipped empty, and spec
  269 A3 reason 1 says so. Today every event of every kind in every deployment is
  "unknown".
- So if quarantine applies to the default, **merging this spec stops all event
  fan-out to rules and overlays in every deployment** until each fab registers
  every kind it uses. That is the outage-on-upgrade spec 269 A3 refused to ship
  for strict, reached by the other door.
- If quarantine applies only to **explicitly declared** `discovery`, the
  undeclared default stops being "discovery" and becomes a third, implicit state
  ("open") that decision 018 does not name, and spec 269 FR-003, the
  `GET /event-sources` summary (`EventSourcesEndpoints.cs:65`) and constitution
  §VIII (*"an undeclared or explicitly `discovery` pair defaults to today's open
  behaviour"*) all have to change what they say. That overturns a reading a human
  accepted at spec 269's gate.

Either branch overrides something a human signed off, or breaks production. The
autonomous lane may do neither (ADR-0144: it implements decisions, it does not
make them). §11 Q1 states the options; the user chose A.

### 0.3 Two consequences of option A, decided here as design inside it

The three states after this spec:

| State | How it arises | Unregistered kind |
|---|---|---|
| **Undeclared** ("open") | no `source_modes` row | stored and fanned out, as today |
| **`discovery`** | declared | **held** (this spec) |
| **`strict`** | declared | refused (spec 269) |

Option A was chosen so that nothing changes on merge. Two things in the tree
would still change behaviour on merge, or trap an operator, unless handled:

1. **Existing `discovery` rows.** Before this spec, declaring `discovery` was
   behaviourally identical to declaring nothing. Rows already declared
   `discovery` — by an operator in any deployment, and by spec 269's own
   integration tests, which "restore" every pair they touch to `discovery` in a
   `finally` (`EventSourceModeApi.RestoreDiscoveryAsync`, used by
   `StrictSourceIngestIntegrationTests`, `IdempotencyKeyReuseEventIngestionIntegrationTests`
   and `EventSourceModeIntegrationTests`, across the berlin, hamburg and dresden
   personas) — would silently start holding events. **FR-014: the migration deletes
   every `discovery` row**, returning each such pair to undeclared, which is
   exactly the behaviour it had. A `strict` row is untouched.
2. **A one-way door.** With no way back to undeclared, an operator who tries
   `discovery` can never return a source to open; and the shared integration
   stack would accumulate held-forever pairs. **FR-013: `DELETE
   /event-sources/{source}` undeclares a pair** (US4), and spec 269's test helper
   restores to undeclared instead of to `discovery`.

Both are flagged at the gate (§12); both follow from the user's "undeclared keeps
today's open behaviour" rather than adding to it.

---

## 1. The premise, checked against `ba37ca5a`

- **`DeadLetter` is as #2325 describes, with one correction.** Nullable `Fab`
  (permanently, spec 018 FR-010), unbounded `RawPayload` (`text`, ADR-0139
  exemption), free-text `RejectionReason` (≤ 512), `RejectedAt`, a version, no
  domain events, no state, no reason discriminator. Audit-only, no fan-out.
  **Correction:** `IDeadLetterRepository` is still write-only (`Add`, `SaveAsync`),
  but a read side **does** exist — `IDeadLetterQuerySource` +
  `ListDeadLettersQueryHandler` behind `GET /events/dead-letters`, fab-scoped,
  ordered newest first, limit ≤ 1 000, DTO
  `(DeadLetterIdentifier, Topic, RawPayload, Error, RejectedAt)`.
- **Two writers, and they are distinguishable today only by topic shape:**
  - `MqttSubscriberHostedService.CaptureDeadLetterAsync` — a delivery that failed
    to *parse*. Topic is the raw MQTT topic (`fab/{fab}/{source}/{device}`); fab
    may be null.
  - `PersistenceLoopHostedService.RecordRejectionAsync` — a parsed envelope the
    ingest handlers *refused* (future skew, spec 269's
    `EVENT_TYPE_NOT_REGISTERED`, …). Topic is synthesised
    `event/{fab}/{source}/{device}`; reason is `"{Code}: {Message}"` (spec 213,
    `Because`). Fab is always set.
- **No kind is stored.** A refused envelope's `Kind` survives only inside the
  error text. A listing filtered by kind, or a promotion by kind, has nothing to
  key on. FR-002 adds it.
- **HTTP ingest never dead-letters.** `POST /events/manual` and
  `POST /events/webhook/{integrationName}` map an `IngestEventError` to a 4xx and
  write nothing. Under strict that is correct (spec 269 FR-008). A quarantine on
  HTTP is therefore a **new write on that path**, not a re-use of an existing one.
- **What discovery does today (post-#2324):** `EventTypeAdmission.AssessAsync`
  loads strict pairs only; `EventTypeVerdicts.Refuses` is true only for a strict
  pair with an unregistered kind. **A discovery pair — declared or not — is stored
  and fanned out exactly as before spec 269.** Nothing reaches `dead_letters` for
  being unknown. The premise of #2325 holds.
- **Two existing tests pin declared-discovery as open, and must invert under
  option A** — the behaviour they pin is the one the user changed:
  `EventTypeAdmissionTests.A_discovery_source_admits_an_unregistered_kind` (whose
  own doc comment says explicit discovery is *"indistinguishable from an absent
  one at this collaborator's read port"*) and the "discovery declared explicitly"
  case in `StrictSourceIngestIntegrationTests` (`SetAsync(berlin, "manual",
  "discovery")`, line 156). These are named so phase 4 edits them on purpose and
  phase 6 does not read the edit as a weakened gate.
- **There is no way to undeclare a pair.** `/event-sources` has `POST`, `PUT
  …/mode` and `GET`; no `DELETE`. `SourceMode` has `Declare` and `Change`.
- **`management-web` has no Event Ingestion feature at all.** `src/features/` is
  `audit`, `cameras`, `layouts`, `overlays`, `rules`, `systemVariables`, `walls`;
  a grep for `event-types`, `dead-letters`, `event-sources` across
  `apps/management-web/src` and `apps/shared/src` finds nothing. **Spec 143 did
  not build a list/retire view** (its §10 item 3 says so), and neither did 269.
  There is no API client for this context in the frontend either.

---

## 2. The slice, and the split

### 2.1 Split decision: **two issues**, this one backend-only

#2325 bundles *quarantine* and *inspector UI + promotion*, "grouped because the
second cannot be built without the first". That dependency is real but it argues
for **ordering**, not for **one PR**. This spec splits it:

| | This issue (#2325), this spec | Follow-on issue #2780 |
|---|---|---|
| What | Quarantine columns + reason code + state; the ingest paths writing held rows; the listing filter; **promotion via the API** | The `management-web` inspector: list held types, show reason, promote |
| Engineer | backend-engineer | frontend-engineer |
| Observable end to end | **Yes, through the API alone** (§6) | Needs this merged first |
| Lane | autonomous, once Q1 is answered | `agent:blocked` until #2325 merges **and** the operator-MFE shell lands |

Reasons, in order of weight:

1. **The frontend's home is moving under it.** ADR-0168 (accepted today, spec 316
   in flight on `feat/316-operator-mfe-shell-and-first-remote`) re-architects
   `management-web` into a shell plus one Module-Federation remote per bounded
   context, with claims-driven navigation. Event Ingestion has no feature folder
   today, so the inspector is a **new remote** — whose shape, nav-manifest contract
   and scope gating are being defined right now in an unmerged branch. Building it
   against today's monolithic router means building it twice.
2. **The backend is independently shippable and observable.** "Held where an
   operator can see it" is satisfied at the API (`GET /events/dead-letters?reason=…`),
   and promotion is an API call. §6 exercises the whole of decision 018's
   discovery half without a browser.
3. **Disjoint files, disjoint engineers (ADR-0109).** Nothing under `src/` and
   nothing under `apps/` overlap. One PR carrying both is two reviews (backend-
   and frontend-reviewer) on one diff for no integration benefit — the API
   contract is the seam either way.
4. **Two phase-4 slices in one spec would still be one PR held hostage** by
   whichever half is slower, and the slower half is blocked on spec 316.

### 2.2 What ships (this spec)

- `dead_letters` rows carry a machine-readable **reason code**, the **kind** where
  one exists, and a **hold state**.
- Under a **declared** `discovery` pair (Q1, option A), an event whose kind is not
  registered for its fab is **held** — written to `dead_letters` with reason
  `UnknownEventType`, **not stored as an event, not fanned out** (decision 018:
  *"quarantined events are audit-only"*) — on all three ingest paths.
- `GET /events/dead-letters` filters by reason and state and returns the new fields.
- **Registering a kind promotes its held rows** for that fab:
  `Held → Promoted`, by one set-based update after the registration commits,
  convergent on retry (FR-007, A7).
- `DELETE /event-sources/{source}` returns a pair to undeclared (FR-013), and the
  migration returns every existing `discovery` row to undeclared (FR-014) — §0.3.

### 2.3 What does not ship

- **Any `apps/` change** — the inspector UI is the follow-on (§2.1).
- **A read-only registry list/retire view** in management-web. Not needed by the
  backend slice; the follow-on decides whether its remote includes it. Not filed
  separately (ADR-0036: nothing asks for it before the inspector does).
- **Replaying held events into ingest on promotion** (A3).
- Schema validation (#2326), any `Shared.Contracts` change, any new integration
  event, any edit to §IV's table, any ADR.

---

## 3. User stories

### US1 (P1) — An unknown event type under discovery is held, not fanned out

**As** the engineer responsible for a fab whose source is in discovery,
**I want** an event of a type the fab never registered to be kept aside where I can
see it, rather than reaching rules and overlays,
**so that** an unrecognised third-party event cannot drive the wall, and I still
have the evidence to decide whether it is legitimate.

**Independent test:** §6 steps 1–8.

### US2 (P1) — A held type can be listed apart from parse failures

**As** the same engineer,
**I want** to list only what is held for being unknown, separately from deliveries
that failed to parse or were refused,
**so that** a quarantine review is not a hunt through malformed MQTT payloads.

**Independent test:** §6 step 6. *P1 with US1: a hold nobody can find is a silent
drop.*

### US3 (P2) — Registering a held type promotes it

**As** the same engineer, having decided a held type is legitimate,
**I want** registering it to mark its held rows promoted and admit the next event
of that type,
**so that** "quarantined, then promoted" is on the record and the door opens.

**Independent test:** §6 steps 9–11. *P2: without it US1 is reversible only by
registering the kind, which still admits future events — the only thing lost is
the `Promoted` marker on past rows. If the PR outgrows review, FR-007's state
update is what leaves; FR-006's admission change cannot.*

### US4 (P2) — A declared source can be returned to undeclared

**As** the same engineer, having put a source in discovery to see what it sends,
**I want** to return it to undeclared,
**so that** trying quarantine is not a one-way door away from today's behaviour
(§0.3 item 2).

**Independent test:** §6 step 13. *P2 for the same reason as spec 269's US2: a
switch with no off position is a defect. Without it the shared integration stack
accumulates held-forever pairs, so it cannot leave this PR.*

---

## 4. Acceptance scenarios

Fab/source names are illustrative; "a discovery pair" means a pair **declared**
`discovery` through `/event-sources` (Q1, option A).

### The default — an undeclared pair is unchanged (characterisation)

```gherkin
Given no mode is declared for (dresden, manual)
And "SomethingNobodyDeclared" is not registered for "dresden"
When an event with that kind is POSTed to /events/manual in "dresden"
Then the response is 201 Created and it is stored and fanned out as before
And no dead letter is written
```

*Asserted, not merely stated: this is the guard that catches option B arriving by
accident.*

### Happy path — HTTP, an unknown kind is held

```gherkin
Given (berlin, manual) is a discovery pair
And "NobodyDeclaredThis" is not registered for "berlin"
When an event with kind "NobodyDeclaredThis" is POSTed to /events/manual in "berlin"
Then the response is 202 Accepted
And GET /events?kind=NobodyDeclaredThis returns nothing
And GET /events/dead-letters?reason=UnknownEventType returns one row
    with fab "berlin", kind "NobodyDeclaredThis", reason "UnknownEventType",
    state "Held", and the event's payload verbatim
And no rule evaluation or overlay update is triggered by it
```

### Happy path — HTTP, a registered kind is unaffected

```gherkin
Given (berlin, manual) is a discovery pair and "PlcCycleStart" is registered
When an event with kind "PlcCycleStart" is POSTed to /events/manual
Then the response is 201 Created and it is listed by GET /events
And no dead letter is written
```

### Happy path — MQTT fast path, held in a mixed burst

```gherkin
Given (berlin, inference) is a discovery pair
And "PersonInRestrictedZone" is registered for "berlin" and "Undeclared1" is not
When both are published on fab/berlin/inference/<device> in one burst
Then "PersonInRestrictedZone" is stored
And "Undeclared1" is not stored
And a dead letter exists for "berlin" with reason "UnknownEventType",
    kind "Undeclared1", state "Held"
And the delivery is acknowledged (not redelivered for ever)
```

### Strict is unchanged, and is distinguishable from a hold

```gherkin
Given (berlin, plc) is declared strict and "Undeclared2" is not registered
When "Undeclared2" arrives on fab/berlin/plc/<device>
Then a dead letter exists with reason "Refused" and kind "Undeclared2"
And its error still begins "EVENT_TYPE_NOT_REGISTERED:"
And it is not returned by GET /events/dead-letters?reason=UnknownEventType
And POSTing "Undeclared2" to /events/manual on a strict (berlin, manual)
    still answers 400 EVENT_TYPE_NOT_REGISTERED and writes nothing
```

### Parse failures are labelled, and existing rows are back-filled

```gherkin
Given a malformed payload is published on fab/berlin/plc/<device>
Then a dead letter exists with reason "ParseFailure", kind absent, state "Held"

Given dead letters written before this spec's migration
Then each row whose topic begins "event/" has reason "Refused"
And every other row has reason "ParseFailure"
And every row has state "Held" and no kind
```

### Promotion — registering a held kind

```gherkin
Given three rows are held for (berlin, "NobodyDeclaredThis")
And one row is held for (munich, "NobodyDeclaredThis")
When an operator holding "berlin" POSTs /event-types { "kind": "NobodyDeclaredThis" }
Then the response is 201 Created
And the three berlin rows have state "Promoted"
And the munich row is still "Held"
And the held rows are not stored as events and nothing is fanned out for them
When the next "NobodyDeclaredThis" event arrives in "berlin"
Then it is stored and fanned out
```

### Precedence — a redelivery is still a redelivery

```gherkin
Given an event was stored while its kind was unknown and its pair was undeclared
When the same eventId is delivered again under a discovery pair
Then the answer is EVENT_ALREADY_INGESTED, and nothing is held
```

```gherkin
Given (berlin, manual) is a discovery pair and the kind is unregistered
When an event whose occurredAt is 10 minutes in the future is POSTed
Then the response is 400 EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE and nothing is held
```

*Order on every path: redelivery, future skew, strict refusal, discovery hold
(FR-005).*

### Undeclaring a pair (US4)

```gherkin
Given (berlin, manual) is declared discovery at version 0
When an operator holding "berlin" sends DELETE /event-sources/manual with If-Match: 0
Then the response is 204 No Content
And GET /event-sources does not list (berlin, manual)
And an unregistered kind POSTed to /events/manual in "berlin" answers 201 and is stored

Given (berlin, manual) is declared
When DELETE /event-sources/manual is sent with no If-Match
Then the response is 428 and the declaration is unchanged

Given (berlin, manual) is declared at version 1
When DELETE /event-sources/manual is sent with If-Match: 0
Then the response is 409 SOURCE_MODE_STALE

Given (berlin, plc) has never been declared
When DELETE /event-sources/plc is sent with If-Match: 0
Then the response is 404 SOURCE_MODE_NOT_DECLARED

When DELETE /event-sources/not-a-source is sent with If-Match: 0
Then the response is 400 SOURCE_MODE_INVALID_INPUT

Given a caller holding sse.events.write but not sse.events.types.write
When they send DELETE /event-sources/manual
Then the response is 403 and the declaration is unchanged
```

### Upgrade — existing `discovery` declarations return to undeclared (FR-014)

```gherkin
Given before the migration (berlin, manual) is declared discovery
And (berlin, plc) is declared strict
When the migration runs
Then (berlin, manual) has no declaration and behaves as undeclared
And (berlin, plc) is still strict, at its prior version
```

### Bad request — the listing filter

```gherkin
When an operator sends GET /events/dead-letters?reason=Unknown
Then the response is 400 and the problem title is DEAD_LETTER_INVALID_FILTER

When an operator sends GET /events/dead-letters?state=Released
Then the response is 400 and the problem title is DEAD_LETTER_INVALID_FILTER
```

### Auth

```gherkin
Given a caller with no bearer token
When they GET /events/dead-letters
Then the response is 401

Given an operator holding only "dresden"
When they GET /events/dead-letters?reason=UnknownEventType
Then no row of any other fab is returned

Given a caller holding sse.events.write but not sse.events.types.write
When they POST /event-types for a held kind
Then the response is 403 and every held row is unchanged
```

*The last block is spec 143 FR-010 re-asserted for the promotion path: an event
source must not be able to promote its own unknown type into the registry.*

### Conflict

```gherkin
Given rows are held for (berlin, "X") and an operator registers "X"
And a second operator registers "X" in "berlin" concurrently
Then exactly one registration answers 201 and the other 409
And no row for (berlin, "X") is left "Held"
```

```gherkin
Given "X" is registered for "berlin" and one row for (berlin, "X") is still "Held"
    (held by a batch assessed just before the registration committed)
When an operator POSTs /event-types { "kind": "X" } in "berlin"
Then the response is 409 EVENT_TYPE_ALREADY_REGISTERED
And that row is now "Promoted"
```

---

## 5. Functional requirements

**FR-001 — A reason code on every dead letter.** New VO `DeadLetterReason`
(`Domain/DeadLetter/`), closed: `ParseFailure`, `Refused`, `UnknownEventType`.
Column `reason` `text NOT NULL`. `DeadLetter.Capture` takes it as a required
argument; both existing writers pass it (`ParseFailure` from the MQTT subscriber,
`Refused` from the persistence loop's refusal path). The free-text `Error` column
stays and keeps its meaning — the reason code says *which kind of hold*, the error
says *what exactly*.

**FR-002 — The kind, where one exists.** Column `kind` `text NULL`
(`Kind.MaximumLength`), the existing `Domain/Event/Kind` VO, nullable because a
parse failure has none. Set for `Refused` and `UnknownEventType` rows (the
envelope is parsed). Invariant: `UnknownEventType ⇒ kind is not null`.

**FR-003 — A hold state.** VO `HoldState`: `Held`, `Promoted`. Column `state`
`text NOT NULL DEFAULT 'Held'`. Every new row is `Held`. **The only transition is
`Held → Promoted`, and only for an `UnknownEventType` row.** It is performed by
FR-007's set-based update, whose `WHERE` clause carries the invariant (`reason =
'UnknownEventType' AND state = 'Held'`), so a row already `Promoted` is untouched
and no other reason can ever be promoted. There is deliberately **no**
`DeadLetter.Promote` method: no code path would load a row to call it (A7), and an
uncalled domain method is speculative (ADR-0036). `DeadLetter.Capture` sets
`Held`; the domain test pins that, and an integration test pins the update's
predicate. No domain event is raised (nothing would consume it).

**FR-004 — Migration back-fills existing rows** (ADR-0067). `topic LIKE 'event/%'`
→ `Refused`; everything else → `ParseFailure`. `state` → `Held`. `kind` left null
(it is recoverable from `error` text but parsing free text in a migration is not
worth it for audit rows — A4). One composite index
`ix_dead_letters_fab_reason_state` on `(fab, reason, state)` for the quarantine
listing and FR-007's update.

**FR-005 — A declared `discovery` pair holds an unregistered kind, on all three
ingest paths.** `EventTypeVerdicts` gains a third outcome: **Admit / Refuse /
Hold**. `Hold` is returned for a pair **declared** `discovery` whose kind is not
registered for its fab. An undeclared pair is always admitted (Q1, option A).
Precedence, identical on all paths: redelivery (`ExistsAsync`), future skew,
strict refusal, discovery hold.

**The handler that decides the hold writes it**, in the same `SaveAsync` as
anything else it stores, as a `DeadLetter` with reason `UnknownEventType`, the
envelope's kind, and topic `event/{fab}/{source}/{device}` (the persistence loop's
existing synthesised shape, so one topic grammar covers every envelope-level row).
No `Event` row, no outbox message.
- **HTTP** (`IngestEventCommandHandler`): after writing, returns the new
  `IngestEventError.EventTypeHeld` (`EVENT_TYPE_HELD`, status `202`), which the
  write endpoints map to **`202 Accepted`** with no `Location` (A2). Precedent for
  a 2xx carried as an `IngestEventError`: `EventAlreadyIngested` (200).
- **MQTT fast path** (`IngestEventBatchCommandHandler`): held envelopes are
  written in the batch's single commit and are **not** put in `Refused`, so the
  persistence loop acknowledges them like stored ones. A batch that fails falls
  back to singles with nothing committed, as today.
- **MQTT slow path** (`PersistenceLoopHostedService.StoreOneAsync`): treats
  `EventTypeHeld` like `EventAlreadyIngested` — the delivery ends `Stored` (it is
  on the record) and is not dead-lettered a second time.

**FR-006 — The admission check costs the same bounded number of queries as spec
269.** The one declared-pairs lookup now returns every declared pair with its mode
(still one query per batch), and `RegisteredKindsAsync` runs once per fab that has
a strict **or** discovery pair present in the batch — never per envelope. With no
declaration among the batch's fabs, the cost is exactly spec 269's default: one
query, no registry lookup.

**FR-007 — Registering a kind promotes its held rows, convergently.**
`RegisterEventTypeCommandHandler`, after the registration commits, sets
`state = 'Promoted'` (and bumps `version`) on every `dead_letters` row with that
`fab`, that `kind`, `reason = 'UnknownEventType'`, `state = 'Held'`, as **one
set-based `UPDATE`** — never by loading rows, because a chatty source can hold
millions of rows for one kind in an hour (A7). **The `409
EVENT_TYPE_ALREADY_REGISTERED` path runs the same promotion** before answering, so
promotion converges: a registration whose promotion step failed, or a row held by
a batch that was assessed just before the registration committed, is promoted by
the next attempt to register the kind (the follow-on UI's "promote" on an already
registered kind). The `POST /event-types` contract (request, `201`/`409`, body,
`Idempotency-Key`, scope) is **unchanged**; promotion is what registering *means*
once quarantine exists (A1). A replayed `Idempotency-Key` does not re-run the
handler and promotes nothing.

**FR-008 — The listing filters and reports the new fields.**
`GET /events/dead-letters` gains optional `?reason=` (`ParseFailure` | `Refused`
| `UnknownEventType`) and `?state=` (`Held` | `Promoted`); an unknown value is
`400 DEAD_LETTER_INVALID_FILTER`. `DeadLetterDto` gains `fab`, `reason`, `kind`
(nullable), `state`, appended after the existing fields (additive; no client
exists). Fab scoping, ordering and limits are unchanged. Scope stays
`sse.events.read`.

**FR-009 — HTTP strict refusal is unchanged.** `400 EVENT_TYPE_NOT_REGISTERED`,
nothing written (spec 269 FR-008). Only the MQTT strict refusal gains a reason
code (`Refused`) and a `kind`, because it already wrote a row.

**FR-010 — Held events are never fanned out.** No `Event` row, no outbox message,
no `FabEventIngestedV1`. Promotion does not change that for rows already held (A3).

**FR-011 — No integration event, no `Shared.Contracts` change.** As spec 143
FR-012 / spec 269 FR-014.

**FR-012 — Constitution §VIII's first bullet must be corrected** to describe what
now exists: undeclared pairs open, declared `discovery` holding, registration
promoting. Today it says *"an undeclared or explicitly `discovery` pair defaults
to today's open behaviour … There is still no promotion path (issue 2325)"*, and
both halves become false at merge. **This PR does not make the edit.** ADR-0144
forbids the autonomous lane amending the constitution, and the user's Q1 decision
— relayed through the coordinator, which is not itself consent — decided the
admission predicate, not this text. The drafted replacement is in `plan.md` §10;
the lane files it as a `documentation` issue for a human to apply (task T016).
Spec 269's FR-003 wording is left as a historical record; this spec supersedes it
for undeclared-vs-discovery and says so (§0.2).

**FR-013 — `DELETE /event-sources/{source}` undeclares a pair** (US4). Resolves
the fab with `ResolveWriteFabAsync`; `If-Match` required, read and `428` answered
before any lookup (spec 143 FR-006, spec 269 FR-011); `404
SOURCE_MODE_NOT_DECLARED`; `409 SOURCE_MODE_STALE`; `400
SOURCE_MODE_INVALID_INPUT` for a bad `{source}`; `204 No Content` on success. The
row is **deleted** — undeclared means no row (spec 269 FR-003) — after
`SourceMode.Undeclare` raises `SourceModeUndeclaredDomainEvent` (unconsumed, as
its siblings are; ADR-0040). Scope `sse.events.types.write` (spec 269 FR-013).
`DELETE` is idempotent in RFC 9110's sense, so ADR-0143's default retries it; a
retried delete answers 404, which is truthful.

**FR-014 — The migration returns every `discovery` declaration to undeclared.**
`DELETE FROM source_modes WHERE mode = 'discovery'` (§0.3 item 1). Before this
spec the two were behaviourally identical, so the deletion preserves every
deployment's behaviour exactly; leaving the rows would silently start holding
events in every fab an operator or a test had touched. The down-migration cannot
restore the rows and drops only the new columns.

**FR-015 — Spec 269's integration-test cleanup restores to undeclared.**
`EventSourceModeApi.RestoreDiscoveryAsync` becomes an undeclare (`DELETE`), and
its call sites follow. Without it, every test that "restores" a berlin, hamburg or
dresden pair leaves it holding, and any later test in the shared stack that
ingests an unregistered kind through that pair — every kind is unregistered by
default — gets a 202 instead of a 201.

---

## 6. Independent end-to-end test procedure

Stop any running AppHost first. One stack per machine.

1. `dotnet run --project src/AppHost`; wait for `event-ingestion` healthy.
2. Mint a token for `op-berlin@berlin.test` / `Operator1234` from **Aspire's
   proxied Keycloak endpoint**.
3. Declare `(berlin, manual)` and `(berlin, inference)` discovery:
   `POST /event-sources {"source":"manual","mode":"discovery"}`, and the same for
   `inference` (if either is already declared, `PUT …/mode` instead).
4. `POST /event-types {"kind":"PlcCycleStart"}` → **201**.
5. `POST /events/manual` kind `PlcCycleStart` → **201**, listed by `GET /events`.
6. `POST /events/manual` kind `Held<run-id>` → **202**. `GET /events?kind=…` →
   empty. `GET /events/dead-letters?reason=UnknownEventType&state=Held` → one row,
   kind `Held<run-id>`, fab `berlin`. Publish a malformed payload on
   `fab/berlin/plc/e2e-1` and confirm it appears under `?reason=ParseFailure` and
   **not** under `?reason=UnknownEventType`.
7. **The MQTT step.** Publish kind `Held<run-id>` on `fab/berlin/inference/e2e-1`.
   Expect a second held row for the same kind, absent from `GET /events`.
8. **The default step** (Q1, asserted): as `op-dresden`, with `(dresden, manual)`
   undeclared, `POST /events/manual` kind `Open<run-id>`. Expect **201**, listed by
   `GET /events`, and no dead letter.
9. `POST /event-types {"kind":"Held<run-id>"}` → **201**.
10. `GET /events/dead-letters?reason=UnknownEventType` → both rows `Promoted`;
    `?state=Held` returns neither.
11. `POST /events/manual` kind `Held<run-id>` → **201**, listed by `GET /events`.
12. **Restart** `event-ingestion` (`WaitOnResourceUnavailable` if scripted);
    re-run step 10 with a fresh token — still `Promoted`.
13. **The undeclare step (US4) and clean-up.** `DELETE /event-sources/manual` with
    no `If-Match` → **428**; with the version from `GET /event-sources` → **204**.
    `POST /events/manual` kind `Open<run-id>-2` → **201**: the pair is open again.
    Undeclare `inference` the same way; retire `Held<run-id>` and `PlcCycleStart`.
    The dev database is persistent, and a leftover discovery pair holds every later
    test's unregistered kind.

**If step 7 stores the event**, the batch path was not wired — the HTTP steps go
through the single handler and cannot detect that.

---

## 7. Latency-budget impact: engaged, on the ≤ 200 ms `Event → overlay state` leg

Both ingest handlers sit inside this leg (spec 103 §6). This spec adds:

| Path | Cost over spec 269 |
|---|---|
| Batch with no declared pair among its fabs (the default) | **none** — one declared-pairs query, as today |
| Batch with a declared discovery pair | +1 `RegisteredKindsAsync` per such fab (spec 269 already pays this for strict) |
| Held kind | the event insert is replaced by a dead-letter insert in the same commit; the event never reaches the overlay, so it leaves the leg |

Option A is why the default population pays nothing: option B would have put a
registry query on every batch in every fab.

As in spec 269 §7, the admission lookup runs **before** `IngestedAt` is stamped,
so the leg histogram cannot see it. **Phase 5 owes a direct measurement**:
`IngestThroughputMeasurementTests` twice on `develop` and twice on the branch, the
branch run once with the measured pair undeclared and once as a discovery pair
with its kinds registered, plus the admission query's span duration. §IV's table
is not edited; the row stays *recorded, not yet readable*.

---

## 8. Locked tech choices applied

Nothing new. PostgreSQL + EF Core plain CRUD (ADR-0130); hand-written VOs with
`.From` + `Ensure.That` (`RegistrationState` / `EventTypeMode` as precedent for
closed state VOs); `Result<T, Error>` + `ApiError`; minimal APIs; EF migration via
`MigrationRunner`; xUnit + Shouldly + fakes + Aspire fixture.

---

## 9. Design decisions and assumptions (ADR-0036)

- **A1 — Promotion is registration, not a separate endpoint.** #2325 says *"a
  `POST /event-types` variant that promotes from quarantine"*. A variant (a flag,
  or `POST /event-types/{kind}/promotion`) would be a second way to register a
  kind, differing only in whether held rows are marked — and the first way would
  then leave held rows `Held` after the kind became known, which is a lie on the
  record. Making every registration promote is smaller, has one path, and keeps
  the scope (`sse.events.types.write`) and idempotency of spec 143 FR-008/FR-010
  for free. *The follow-on UI's "promote" button is a `POST /event-types`.*
- **A2 — HTTP answers `202 Accepted` for a held event.** It is a 2xx, so a webhook
  sender does not retry it (correct: retrying an unknown type changes nothing),
  and it is not `201`, because no resource was created at `/events/{id}`. No
  `Location`. The ingest mappings gain `.Produces(202)`.
- **A3 — Promotion does not replay held events.** Decision 018: *"quarantined
  events are audit-only"*. Releasing them late into rules would fire automations
  on stale events. A replay, if ever wanted, is a separate feature.
- **A4 — Back-filled rows get no kind** (FR-004). They are pre-quarantine audit
  rows; none can be `UnknownEventType`.
- **A5 — Duplicate holds are possible.** An MQTT redelivery (QoS 1) of a held
  event writes a second row; nothing de-duplicates by event identifier. Acceptable
  for an audit table; the follow-on UI groups by `(fab, kind)`. Adding an
  `event_id` column and partial unique index is a reversible later change.
- **A6 — No promoted-at / promoted-by.** Matches spec 143 A3 (no retired-at);
  the registry row's `registeredAt` / `registeredBy` already records who promoted
  and when.
- **A7 — Promotion is a set-based update after the registration commits, not one
  transaction with it.** One transaction would need either loading every held row
  as a tracked aggregate (unbounded: an unknown kind at 1 000 events/s holds 3.6 M
  rows an hour) or an explicit transaction around `SaveChanges` plus
  `ExecuteUpdate`, a pattern used nowhere in `src/` and awkward under the retrying
  execution strategy Aspire's Npgsql enrichment configures. Convergence (FR-007's
  409 path) closes the gap the lost atomicity opens, and also closes the batch race
  atomicity would not have closed. `ExecuteUpdateAsync` is itself new to `src/`;
  it is standard EF Core and is the smallest correct tool here.

---

## 10. Out of scope, and follow-ups

- **The inspector UI** — #2780 (§2.1), frontend-engineer, blocked on
  this issue and on the operator-MFE shell (spec 316 / ADR-0168).
- **A management-web view of the registry and source modes** — the follow-on
  remote may include it; not filed separately.
- **Replay on promotion** (A3), **de-duplication of holds** (A5).
- **An in-memory admission projection** — only if phase 5's measurement asks.
- **#2326 schema validation** — still needs an ADR.

---

## 11. `[NEEDS CLARIFICATION]`

**None.** Q1 was the one item, and the user answered it on 2026-10-08: **option
A**. The options as they were put, kept for the record:

### Q1 — Does quarantine apply to the undeclared default? **Resolved: A**

| Option | What happens on merge | Cost |
|---|---|---|
| **A (chosen) — declared `discovery` only.** Undeclared pairs keep today's open behaviour; an operator opts a source into quarantine by declaring it `discovery`. | Nothing changes for any deployment until an operator declares a pair. | Undeclared becomes a third, implicit state ("open"). Spec 269 FR-003's wording, the `GET /event-sources` summary and constitution §VIII's sentence change. Arguably a reading of decision 018 ("each source has a strict **or** discovery flag") that leaves room for "not yet enrolled"; a human may judge it needs an ADR note. |
| **B — the default quarantines too** (literal spec 269 FR-003). | **Every event of every kind in every deployment is held** — rules and overlays stop receiving events — until each fab registers every kind it uses. | An outage on upgrade. Plus a registry query on every batch (§7). |
| **B′ — B, plus the migration seeds each fab's registry** from the distinct `(fab, kind)` already in `events`. | Kinds seen before are admitted; genuinely new kinds are held. | Faithful to 018, but a rare kind outside retention (a monthly alarm) is silently diverted to an audit table in a 24/7 safety system; and seeding writes registry rows nobody declared. |
| **C — discovery stores *and* holds** (fan-out continues, a held row is also written for review). | No outage. | Contradicts decision 018's *"quarantined events are audit-only"* — **needs an ADR**, which this lane may not write. |

**Recommendation was A, and A was chosen.** It is the only option that neither
breaks every installation nor contradicts decision 018's text, and it makes the
default population pay nothing on the hot path. It overturns spec 269's accepted
"undeclared = discovery" wording, which is why a human chose it; §0.3 records the
two consequences this spec handles because of it.

---

## 12. Gate — phase 1

- [x] **Q1 answered: option A** (user, 2026-10-08, recorded on #2325).
- [ ] §0.3 accepted: FR-014 deletes existing `discovery` rows on migration, and
      FR-013 adds `DELETE /event-sources/{source}` so undeclared is reachable again.
- [ ] §1's two inverting tests seen: `A_discovery_source_admits_an_unregistered_kind`
      and the explicit-discovery case in `StrictSourceIngestIntegrationTests`
      change their assertion on purpose.
- [ ] §2.1 split accepted: this issue is backend-only; the inspector UI is #2780,
      blocked on this one and on spec 316.
- [ ] A1 accepted: promotion is plain registration, no endpoint variant.
- [ ] A2 accepted: `202 Accepted` for a held HTTP event.
- [ ] FR-012: the constitution §VIII correction is **filed for a human, not made
      in this PR** (T016); drafted text in `plan.md` §10.
- [ ] §7 accepted: §IV engaged; phase 5 owes a direct measurement.
- [ ] Phase 4a colour: **RED** (behaviour-changing — a new outcome on every ingest
      path, a new listing filter, a new effect of registration, a new endpoint).
      The existing ingest suite is also the characterisation net for the
      undeclared population and must stay green **unmodified**, apart from the two
      named inversions and FR-015's cleanup helper.
