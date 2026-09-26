# Spec 269 — The unknown a source refuses

**Issue:** #2324 — *"A source declares whether an unknown event type is refused or
accepted"* (follow-up 1 of `specs/143-a-registry-to-be-unknown-of/spec.md` §10)
**Reads:** the registry built by spec 143, #1972, **PR #2327 (merged)**
**Branch:** `feat/2324-strict-discovery-source-mode`
**Phase:** 1 (Specify) · **Drafted:** 2026-09-26 · **Re-verified:** 2026-09-27
**Tree read at:** `2f477474` (origin/develop at re-verification; first drafted
against `b23b9307`, 65 commits earlier — §0.4 lists what moved)
**Lane:** supervised (ADR-0037). Not `agent:ready`.
**Feature bucket:** spec `specs/006-event-ingestion/`
**ADRs:** ADR-0000 decision **018** (the hybrid registration model — this is its
second third), ADR-0130 (the audit that found it unbuilt and did not amend 018),
ADR-0092 (per-aggregate Domain folder), ADR-0093 (per-message-kind Application
folder), ADR-0038 / ADR-0046 / ADR-0066 (hand-written value objects,
`IValueObject<T>`, `.From(...)` + `Ensure.That(...)`), ADR-0039 / ADR-0090 (Guid v7
identifiers with the `Identifier` suffix), ADR-0091 / ADR-0094 (no shortcuts;
noun-named identifier properties), ADR-0047 / ADR-0089 (`Result<T, Error>` +
`ApiError`), ADR-0105 (`Ensure.That`), ADR-0070 (minimal APIs), ADR-0042 / ADR-0057
(hand-rolled handlers + Wolverine dispatcher), ADR-0040 / ADR-0073 (domain events
are not integration events), ADR-0043 / ADR-0113 (two-layer optimistic concurrency),
ADR-0114 (write-fab resolution), ADR-0142 (`Idempotency-Key`), ADR-0143 (`POST` not
retried by default), ADR-0067 (`MigrationRunner`), ADR-0051 (per-context DI), ADR-0103
(Aspire fixture), ADR-0052 / 0053 / 0054 (xUnit + Shouldly, sentence names, builders),
ADR-0065 (coverage), ADR-0084 (metrics), ADR-0109 (disjoint files for `[P]`),
ADR-0117 (§VII binds implemented legs), ADR-0139 / ADR-0144 (new behaviour starts red;
the lane's limits), ADR-0141 (`Option<T>`), ADR-0036 (no speculative generality).
**Constitution:** §II (value objects), §III bullet 5 (the Event Ingestion charter:
*"hybrid strict/discovery model per source"*), **§IV (the latency budget — engaged,
§7)**, §VIII (safe by default at trust boundaries — its first bullet is corrected,
FR-016), §Testing.

**Naming collision, disambiguated once** (carried from spec 143): "**Decision
018**" means row 018 of `docs/adr/0000-initial-decisions.md`. "**Spec 018**" means
`specs/018-event-fab-scoping/`, which is unrelated.

---

## 0. The ADR question, answered before anything else

Spec 143 §0.3 and #2324 both said this slice *"likely needs an ADR first"*,
because one question was undecided: **what a *source* is** for the purpose of
the mode.

**That question is answered, and not by this spec.** The product owner decided it
on 2026-09-26, recorded on #2324:

> Picked up for the supervised lane. Product owner's decision (2026-09-26): a
> source is a (fab, Source) pair for strict/discovery mode purposes — matches spec
> 006 §IX's extensible-string-field convention, no new entity needed.

This spec **implements** that decision and does not reopen it. No ADR is written
here. Everything left for this spec to decide — where the setting is stored, what
an unconfigured source does, the shape of the ingest check — is design inside
decision 018 and constitution §III bullet 5, not architecture. §9 marks each
of those decisions so the phase-1 gate can overturn any of them.

### 0.1 One fact about `Source` the gate should see

The decision cites spec 006 §IX's *"`source` + `kind` are extensible string fields,
not closed enums"*. That sentence describes the **wire contract**
(`FabEventIngestedV1`), and it is still true there. **The domain value object is
closed**: `src/EventIngestion/Domain/Event/Source.cs` admits exactly `plc`,
`inference`, `manual`, `webhook` and throws on anything else.

This is recorded because it has a consequence. It does **not** reopen the decision:

- The key space is `fab × {plc, inference, manual, webhook}`: at most **four modes
  per fab**.
- **Every webhook integration in a fab shares one mode**: `(fab, webhook)`. Every
  MQTT PLC device in a fab shares `(fab, plc)`. A fab cannot make one webhook
  integration strict and another discovery.

If the product owner meant finer granularity than that, the gate is where to say
so. Nothing in this spec prevents a later split; see A2.

### 0.2 How strict mode squares with spec 006 §IX

#2324 asks this directly. **`kind` stays an extensible string everywhere.** Nothing
here closes the wire field, the contract or the `Kind` VO's grammar. Strict mode
is an **admission policy** that an operator chooses for one `(fab, Source)`. It
checks a `Kind` against a set the operator maintains as data (spec 143's
registry), not against an enum compiled into the code. When nobody has chosen
strict (the default, FR-003), ingest keeps today's open behaviour.

Decision 018 attaches two behaviours to each flag. This spec builds **one of the
four**: strict *"rejects unknown event types"*. The other three are elsewhere:
strict *"schema-validates known"* is #2326, discovery *"quarantines them in an
inspector UI"* is #2325, and promotion is #2325 as well. #2324's own body
paraphrases discovery as *"accepts unknown, quarantines"*; the *quarantines* half
is #2325's by name (*"An unknown event type is held where an operator can see
it"*), and §2 keeps it out.

### 0.3 What "no new entity needed" is read as — flagged for the gate (G1)

The product owner's comment ends *"no new entity needed"*. This spec adds a
`SourceMode` aggregate (FR-002), so the reading has to be stated rather than
assumed.

**The reading taken:** *no new entity is needed to say what a source is*. That
is the question #2324 asked (*"what a source is for this purpose"*), and the
answer is: the pair of two things that already exist, `FabIdentifier` and the
`Source` VO. No `EventSource`, `Device` or per-integration entity is introduced
to *identify* a source, and none is keyed on `WebhookIntegration` or an MQTT
device segment.

**What the reading does not cover:** the per-pair **mode** still has to be
stored somewhere an operator can change it. `SourceMode` is that setting — a
policy row keyed by the pair, not a model of the source. It is an aggregate only
because this codebase's `If-Match` versioning is attached to aggregates (§9 A1).

**The alternative, if the gate reads the comment strictly:** no table, no
endpoint; the mode is configuration (`EventIngestion:SourceModes:{fab}:{source}`)
read at start-up. It is smaller, but:
- a mode change is a redeploy, so US2 (switch it back) is an operations task, not
  an operator one;
- no record of who made a source strict, or when;
- the shared Aspire test stack has one configuration, so an integration test
  cannot make a pair strict and restore it (§6 steps 5–12, plan R1). US1 could
  then only be observed through a pair made permanently strict in the dev
  AppHost — a standing trap for every other test that ingests through it.

**If the gate chooses configuration**, US2, FR-010–FR-014 and plan §4/§5 are
dropped and plan §6's read port is re-implemented over `IOptionsMonitor`; FR-001,
FR-003–FR-009 and FR-015 stand unchanged, because the ingest collaborator does
not care where the mode is stored.

### 0.4 Provenance — this spec was drafted once already, as spec 267

An earlier session drafted this feature on 2026-09-26 as
`specs/267-the-unknown-a-source-refuses/`, one hour **after** the product
owner's comment (comment 17:30Z; work stashed 18:30Z), and began phase 4. It was
never committed. Its state on 2026-09-27:

| Where | What | Disposition |
|---|---|---|
| Index of this worktree (staged) | T001's domain prelude: 8 files under `src/EventIngestion/Domain/SourceMode/` and a root-count bump in `tests/Architecture.Tests/PrimitiveBoundaryTests.cs` | **Kept.** Already keyed `(FabIdentifier, Source)` — see below. Rebased onto `2f477474`; the root count re-derived as **14** (develop's spec 258 added `Wall`, 12 → 13, after the draft was written). Compiles clean in Release (`S107` on `Declare`, advisory under ADR-0084). |
| `git stash` entry *"T002-in-progress"* | spec/plan/tasks 267, plus T002's application skeleton (23 source/test files) | **Specs carried forward here, renumbered.** The T002 code is **not applied** by phase 3; it is phase 4's. The stash is left in place, unmodified, as reference for the phase-4 engineer — not as an input it must accept. |
| Number 267 | — | **Taken** since, by `fix/2624-adr0151-focus-loss-sites` (`specs/267-the-place-a-submit-keeps`); 268 is taken in the `feat/2336` worktree. **269** is the next free number across origin, every remote branch and every local worktree on 2026-09-27. |

**Why the staged scaffolding is kept, not reshaped or discarded.** The question
was whether it modelled something narrower than the decision — keyed on a
webhook integration, a device, or a single `Source`. It does not:
- `SourceMode` carries exactly `FabIdentifier Fab` and `Source Source` as its
  fixed identity-bearing state, and its doc comment rejects both narrower homes by
  name (`RegisteredEventType`, keyed `(fab, kind)`; `WebhookIntegration`, a
  credential MQTT and manual ingress never touch).
- `ISourceModeRepository.GetAsync(FabIdentifier fab, Source source, …)` is keyed
  on the pair.
- Both domain events carry `Fab` and `Source`.
- `Source` is the existing `Domain/Event/Source.cs` VO, not a re-model.

It is the decision, typed. Discarding it would rewrite the same eight files; the
only thing it predated was develop's `Wall` aggregate, which the rebase absorbed.

**What the scaffolding still owes** (it is T001, *behaviour withheld* by design,
plan §9): `EventTypeMode.From` returns `Discovery` for any input, `DeclaredAt.From`
does not normalise to UTC, `Declare` leaves `Mode`/`Declaration` null and raises
nothing, `Change` is guards only. These are the red Phase B will observe, not
defects.

**What moved between `b23b9307` and `2f477474`, re-checked for this spec:**
- `EndpointScopeDeclarationTests` pins **14** files / **65** mappings (was 13 /
  60) — spec 258's walls; T010's bump is now 14 → 15 and 65 → 68.
- `PrimitiveBoundaryTests` roots: 13 on develop, 14 with `SourceMode` (above).
- Unchanged: `Source`'s four closed values; both ingest handlers' shapes
  (`HandleAsync` → `ExistsAsync` → `Ingest` in `try`; batch `ExistingAsync` →
  synchronous `Build(envelope)`); `IngestEventErrors`; `IRegisteredEventTypeRepository`
  (one read, `GetRegisteredAsync`); `Scope.Sse.Events.TypesWrite`; constitution
  §VIII's first bullet (line 337 still names issue 2324 as unbuilt); §IV's
  `Event → overlay state` row (*recorded, not yet readable*); spec 006 §IX
  (`spec.md:448`).

---

## 1. The premise, checked

Re-run against `2f477474` (first run against `b23b9307`; every bullet held):

- `grep -rn -i "strict\|discovery" src/EventIngestion --include=*.cs` has no hit
  that names a mode. The only hits are the four false positives spec 143 §1
  recorded, plus spec 143's own doc comments. **No per-source mode exists.**
- `IRegisteredEventTypeRepository` has one read, `GetRegisteredAsync(FabIdentifier,
  Kind, …)`, keyed `(fab, kind)`. It returns a **tracked** aggregate and is called
  only by the register/retire handlers. **Nothing on the ingest path reads the
  registry.** Spec 143 FR-013 guaranteed that, and it still holds.
- There are two ingest insertion points, as #2324 says.
  `IngestEventCommandHandler.HandleAsync` serves `POST /events/manual`,
  `POST /events/webhook/{integrationName}` and the MQTT slow path
  (`PersistenceLoopHostedService.StoreOneAsync`).
  `IngestEventBatchCommandHandler.HandleAsync` → `Build` serves the MQTT fast path
  (`TryStoreBatchAsync`). **Both already carry a typed refusal outward**:
  - The HTTP endpoint maps `IngestEventError` through `error.ToProblem()`
    (`EventsEndpoints.Writes.cs:359`).
  - The batch returns `RefusedEnvelope(envelope, reason)`, and the persistence loop
    dead-letters it with `Because(reason)` (spec 213).
  - The slow path dead-letters any non-`EventAlreadyIngested` failure the same way.

  **A new `IngestEventError` variant therefore reaches a 4xx on HTTP and a dead
  letter on MQTT with no change to any Api or Infrastructure/Ingress file.** That
  finding is what keeps this slice small.
- `WebhookIntegration` is a per-integration credential aggregate. MQTT and manual
  ingress never touch it. It is not a home for a per-source policy (§9 A1).
- `AggregateRoot<TIdentifier>` constrains `TIdentifier : struct,
  IStronglyTypedId<Guid>` (`src/Shared.Kernel/AggregateRoot.cs:10`), and the
  ADR-0113 version interceptor keys on it. A natural `(fab, Source)` key with no
  surrogate cannot be an aggregate root in this codebase (plan §2).

---

## 2. The slice, and what it deliberately is not

**What ships:**
- An operator can declare, change and list a mode per `(fab, Source)`.
- Both ingest paths consult that mode:
  - **strict** refuses an event whose `Kind` is not registered for its fab;
  - **discovery**, or no declaration at all, accepts it exactly as today.

**What does not ship:**
- **Quarantine (#2325).** Under discovery an unknown event is stored and fanned out
  **exactly as it is today**. This spec persists nothing new for it and changes
  nothing about its path.
- **Schema validation (#2326)**, the inspector UI and promotion.
- Any `apps/` change, any `Shared.Contracts` change, any new integration event,
  any edit to §IV's table, per-source rate limits.
- **Any ADR.**

---

## 3. User stories

### US1 (P1) — A strict source refuses an event type its fab never declared

**As** the engineer responsible for a fab,
**I want** to declare that one of the fab's sources is `strict`,
**so that** an event whose type the fab never registered is refused at the door:
- **HTTP callers** get a 400 naming the reason.
- **MQTT deliveries** are dead-lettered with that reason, not fanned out to rules
  and overlays.

**Independent test:** §6 steps 1–8. Register a type, declare the source strict,
then ingest a registered and an unregistered kind through HTTP and through MQTT.
Observe one stored and one refused on each path.

**Why this is the whole shippable core:** decision 018's `strict` flag is a
behaviour of ingest. A declaration that ingest ignores is not a mode, and an
ingest check with no declaration is not reachable. Both halves ship together or
neither is observable.

### US2 (P2) — A declared mode can be read back and changed

**As** the same engineer, having made a source strict and found a legitimate type
missing from the registry,
**I want** to see each source's mode and switch it back to `discovery`,
**so that** strict mode is not a one-way door that can only be undone in SQL.

**Independent test:** §6 steps 9–12.

**Why P2 rather than a separate spec:** without it, US1 ships a switch that cannot
be switched off. That is a defect, not a smaller slice (the same reasoning as spec
143's US2). It is P2 so that, if the PR outgrows review, **`GET` is what leaves
first**. `PUT` cannot leave.

---

## 4. Acceptance scenarios

Fab and source names below are illustrative. Tests use the pairs chosen in
tasks.md T003f.

### Happy path — strict refuses the unknown and admits the known (HTTP)

```gherkin
Given fab "berlin" has "PlcCycleStart" registered (spec 143)
And an operator holding "berlin" and sse.events.types.write
When they POST /event-sources with { "source": "manual", "mode": "strict" }
Then the response is 201 Created
And the Location header is /event-sources/manual
When an event with kind "PlcCycleStart" is POSTed to /events/manual in "berlin"
Then the response is 201 Created and the event is listed by GET /events
When an event with kind "NobodyDeclaredThis" is POSTed to /events/manual in "berlin"
Then the response is 400 Bad Request
And the problem title is EVENT_TYPE_NOT_REGISTERED
And the detail names the fab, the source and the kind
And GET /events?kind=NobodyDeclaredThis returns nothing
```

### Happy path — strict refuses the unknown on the MQTT fast path, into a dead letter

```gherkin
Given (berlin, inference) is declared strict
And "PersonInRestrictedZone" is registered for "berlin" and "Undeclared1" is not
When both are published on fab/berlin/inference/<device> in one burst
Then "PersonInRestrictedZone" is stored
And "Undeclared1" is not stored
And a dead letter exists for "berlin" whose reason begins "EVENT_TYPE_NOT_REGISTERED:"
And the delivery is acknowledged (not redelivered for ever)
```

### Scope of a declaration — one pair, not the fab and not the source everywhere

```gherkin
Given (berlin, inference) is declared strict and (berlin, plc) is not declared
When an unregistered kind arrives on fab/berlin/plc/<device>
Then it is stored as today

Given (berlin, inference) is declared strict and (munich, inference) is not
When an unregistered kind arrives on fab/munich/inference/<device>
Then it is stored as today
```

### Default — an undeclared source behaves exactly as before this spec

```gherkin
Given no mode is declared for (dresden, manual)
And dresden has no registered event types at all
When an event with kind "SomethingNobodyDeclared" is POSTed to /events/manual
Then the response is 201 Created
And it is stored and fanned out exactly as before this spec
And no dead letter is written
```

*Asserted, not merely stated. This is the guard that catches a default that
silently became strict (FR-003).*

### Discovery declared explicitly — the same as undeclared

```gherkin
Given (berlin, manual) is declared discovery
When an event with an unregistered kind is POSTed to /events/manual in "berlin"
Then the response is 201 Created
```

### A retired type is unknown again

```gherkin
Given (berlin, manual) is strict and "PlcCycleStart" was registered then retired
When an event with kind "PlcCycleStart" is POSTed to /events/manual in "berlin"
Then the response is 400 and the problem title is EVENT_TYPE_NOT_REGISTERED
```

### Precedence — a redelivery is still a redelivery

```gherkin
Given an event was stored while (berlin, manual) was discovery
And (berlin, manual) has since been declared strict and its kind is unregistered
When the same event (same eventId) is delivered again
Then the answer is EVENT_ALREADY_INGESTED (200), not EVENT_TYPE_NOT_REGISTERED
```

```gherkin
Given (berlin, manual) is strict and the kind is unregistered
When an event whose occurredAt is 10 minutes in the future is POSTed
Then the problem title is EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE
```

*Precedence is redelivery, then future skew, then unregistered type (FR-007).*

### Conflict — a second declaration for the same pair

```gherkin
Given (berlin, manual) is already declared
When an operator holding "berlin" POSTs /event-sources with source "manual"
Then the response is 409 Conflict
And the problem title is SOURCE_MODE_ALREADY_DECLARED
```

### Conflict — a change against a stale version

```gherkin
Given (berlin, manual) is declared at version 0 and has since been changed to version 1
When an operator sends PUT /event-sources/manual/mode { "mode": "discovery" }
    with If-Match: 0
Then the response is 409 Conflict
And the problem title is SOURCE_MODE_STALE
And the mode is unchanged
```

### Bad request

```gherkin
When an operator POSTs /event-sources with { "source": "mqtt", "mode": "strict" }
Then the response is 400 and the problem title is SOURCE_MODE_INVALID_INPUT

When an operator POSTs /event-sources with { "source": "plc", "mode": "paranoid" }
Then the response is 400 and the problem title is SOURCE_MODE_INVALID_INPUT

When an operator sends PUT /event-sources/not-a-source/mode
Then the response is 400 and the problem title is SOURCE_MODE_INVALID_INPUT

Given an operator holding both "dresden" and "munich"
When they POST /event-sources without naming a fab
Then the response is 400 and the problem title is EVENT_FAB_REQUIRED
```

### Precondition required, and not declared

```gherkin
Given (berlin, manual) is declared
When an operator sends PUT /event-sources/manual/mode with no If-Match
Then the response is 428 Precondition Required and the mode is unchanged

Given (berlin, plc) has never been declared
When an operator sends PUT /event-sources/plc/mode with If-Match: 0
Then the response is 404 Not Found
And the problem title is SOURCE_MODE_NOT_DECLARED
```

### Auth — an event source cannot relax its own policing

```gherkin
Given a caller with no bearer token
When they POST /event-sources
Then the response is 401

Given a caller holding sse.events.write but not sse.events.types.write
    (an event-source-shaped client, planted for the test)
When they POST /event-sources or PUT /event-sources/{source}/mode
Then the response is 403 and nothing is written

Given an operator holding only "dresden"
When they POST /event-sources?fabId=munich
Then the response is 403 and the problem title is RESOURCE_FAB_NOT_AUTHORIZED
And munich's modes are unchanged

Given an operator holding only "dresden"
When they GET /event-sources
Then no row of any other fab is returned
```

---

## 5. Functional requirements

**FR-001 — A mode is declared per `(FabIdentifier, Source)`**, per the product
owner's decision (§0). `Source` is the existing VO in
`Domain/Event/Source.cs`, reused rather than re-modelled. The mode is a new VO,
`EventTypeMode`, with exactly two values, `strict` and `discovery`. They are
lowercase to match decision 018's own words and `Source`'s wire convention.

**FR-002 — The declaration is its own aggregate, `SourceMode`**, in
`Domain/SourceMode/` (ADR-0092). It is not a column on `RegisteredEventType`,
which is keyed `(fab, kind)` and carries no `Source` by spec 143 FR-001's
deliberate reading. It is not a field on `WebhookIntegration`, which is a
credential, and MQTT and manual ingress never touch it. It has a Guid v7 surrogate
`SourceModeIdentifier` because `AggregateRoot<TIdentifier>` requires one (§1).
The **natural key `(fab, source)` is enforced by a total unique index**. There is
no lifecycle state, so the index has no filter.

**FR-003 — An undeclared source is `discovery`.** Absence of a row means
discovery. No row is back-filled and no configuration switch is added. Reasoning
in §9 A3. This is the single most consequential decision in the spec.

**FR-004 — Strict refuses an unregistered kind; discovery admits it unchanged.**
- Under **strict**, an event is refused when its `Kind` has no `Registered` entry
  for its fab. "Registered" means exactly what spec 143's `GetRegisteredAsync`
  means: state `Registered`, in the same fab, and a retired entry does not count.
- Under **discovery**, the event takes today's path byte for byte. Until #2325
  lands, discovery has **no** observable effect of its own.

**FR-005 — One collaborator, consulted by both insertion points.**
`EventTypeAdmission` (Application, `Application/Ingress/`) takes a collection of
envelopes and returns verdicts. `IngestEventCommandHandler` calls it with one
envelope. `IngestEventBatchCommandHandler` calls it once per batch, with the whole
batch. Neither handler contains the policy. Shape in plan §6.

**FR-006 — The check costs a bounded number of queries, never one per event**
(spec 020 FR-010):
- **Default** (no strict declaration among the batch's fabs): **one** indexed
  query per HTTP event and **one** per MQTT batch.
- **Strict**: **plus one** query per strict fab in the batch.
- `GetRegisteredAsync` is *not* called per envelope. That method is a tracked,
  single-`(fab, kind)` aggregate load built for the write side, and calling it in
  the batch would be a round trip per distinct kind (plan §6).

**FR-007 — Precedence: redelivery, then future skew, then unregistered type.**
- A redelivery of an event stored before a source went strict still answers
  `EVENT_ALREADY_INGESTED`. The existence check already runs first on both paths,
  and it stays first.
- Future skew is intrinsic to the envelope and needs no I/O to decide, so it wins
  over the policy verdict when both apply.
- The same order holds on both paths.

**FR-008 — The refusal is a typed `IngestEventError`.** The new variant is
`EventTypeNotRegistered(string Fab, string Source, string Kind)`, code
**`EVENT_TYPE_NOT_REGISTERED`**, **`400 Bad Request`**. Why 400:
- It matches the only other domain-rule refusal in this error type
  (`EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE`, 400).
- Both ingest mappings already declare `ProducesProblem(400)`. No `apps/` or Api
  surface changes, and no status code the repo has never used (there is no 422
  anywhere in `src/`).

The message names fab, source and kind, so a webhook sender can act on it without
reading logs. It stays well under `RejectionReason.MaximumLength`, which the
dead-letter path needs.

**FR-009 — No Api or Infrastructure/Ingress file on the ingest path changes.**
The HTTP 400 comes from `error.ToProblem()`. The MQTT dead letter comes from the
existing `RefusedEnvelope` → `Because(reason)` path on the fast path, and from
`Ending.Rejected(Because(error))` on the slow path. Neither
`EventsEndpoints.Writes.cs` nor `PersistenceLoopHostedService.cs` is edited. A
diff that touches either is a review question.

**FR-010 — `POST /event-sources` declares a mode for one source in the resolved
fab.**
- The fab resolves through `EventIngestionFabResolution.ResolveWriteFabAsync`
  (ADR-0114), exactly as `POST /event-types` does.
- A second declaration for the same pair answers `409
  SOURCE_MODE_ALREADY_DECLARED`. When two requests race, the index's
  `23505` → `409 RESOURCE_ALREADY_EXISTS` is the backstop (spec 086). Tests
  assert the status, not the title, wherever a race is exercised (spec 143
  FR-004).
- It honours `Idempotency-Key` (ADR-0142), scoped by caller.

**FR-011 — `PUT /event-sources/{source}/mode` changes a declared mode.**
- It resolves the fab with `ResolveWriteFabAsync`. `{source}` plus a plural fab
  set would be ambiguous, one row per fab.
- `If-Match` is **required** (ADR-0113 Layer 1). The header is read and `428` is
  answered **before** any lookup, as spec 143 FR-006 does.
- An undeclared pair answers `404 SOURCE_MODE_NOT_DECLARED`. A stale version
  answers `409 SOURCE_MODE_STALE`.
- Setting the mode a source already has is a no-op `200`: no event is raised and
  the version does not change (mirrors `RegisteredEventType.Retire`'s idempotent
  early return).
- `PUT` is idempotent in RFC 9110's sense, so ADR-0143's default retries it. That
  is safe because it sets a fixed value.

**FR-012 — `GET /event-sources` lists the declared modes of the caller's readable
fabs** (`ResolveReadFabsAsync`).
- Each row carries `sourceModeId`, `fab`, `source`, `mode`, `declaredAt`,
  `declaredBy` and `version`.
- **Undeclared pairs are not listed.** Their mode is `discovery` by FR-003, and
  the endpoint summary says so.
- Unpaged: at most four rows per fab.

**FR-013 — Scopes.**
- **Writes reuse `sse.events.types.write`.** Spec 143 FR-010 gave that scope its
  reason to exist: an event source must not decide which event types are
  legitimate. Letting a source flip *itself* to discovery is the same defect with
  the sign reversed, and the same operators hold the scope.
- **Reads reuse `sse.events.read`.**
- **No `Scope.cs` edit and no realm edit.** *Gate question, like spec 143 FR-011:
  cheap to split later.*

**FR-014 — Domain events are raised, and no integration event is published.**
`SourceModeDeclaredDomainEvent` and `SourceModeChangedDomainEvent` are unconsumed.
The precedent is spec 143 FR-012 and `WebhookIntegration`'s events (ADR-0040,
ADR-0073, ADR-0036).

**FR-015 — A mode change takes effect for the next event stored, not for events
already stored or in flight.** The mode is read at storage time, inside the
handler.
- An event buffered in the ingest channel while the mode changes is judged by the
  mode at the moment it is stored.
- Stored events are never re-judged.

**FR-016 — Constitution §VIII's first bullet is corrected to describe what now
exists.** It reads *"there is still **no per-source strict/discovery mode (issue
2324)** and no promotion path (issue 2325)"*. After this spec the first clause is
false. The edit changes **only** that clause, and keeps the promotion/quarantine
clause and the *"not as a description of today"* sentence, both still true.
- **This is a factual status correction, not an amendment.** It is the same class
  of edit spec 143 FR-014 made to the same bullet, and ADR-0144 permits such an
  edit in the supervised lane only because a human passes this gate.
- **If the gate rejects it**, drop FR-016 and task T014. Nothing depends on them.

---

## 6. Independent end-to-end test procedure

Runnable by a person with the repo, Docker and a browser. Stop any running
AppHost first: a running stack holds the service binaries, and MSB3027 looks like
a broken build. One stack per machine.

1. `dotnet run --project src/AppHost`. Wait for `event-ingestion` to report
   healthy.
2. Mint a token for `op-berlin@berlin.test` / `Operator1234` from **Aspire's proxied
   Keycloak endpoint**, not the container's mapped port.
3. `POST /event-types` `{"kind":"PlcCycleStart"}`. Expect **201** (spec 143).
4. `POST /events/manual` with kind `Undeclared<run-id>`. Expect **201**. This is
   the **default**, and nothing is declared yet (FR-003).
5. `POST /event-sources` `{"source":"manual","mode":"strict"}`. Expect **201** and
   `Location: /event-sources/manual`.
6. `POST /events/manual` kind `PlcCycleStart`. Expect **201**.
7. `POST /events/manual` kind `Undeclared<run-id>-2`. Expect **400**
   `EVENT_TYPE_NOT_REGISTERED`, detail naming `berlin`, `manual` and the kind.
   `GET /events?kind=…` returns nothing.
8. **The MQTT step, which is the one the HTTP steps cannot stand in for.**
   - `POST /event-sources` `{"source":"inference","mode":"strict"}`.
   - Publish an unregistered kind on `fab/berlin/inference/e2e-1` with
     `mosquitto_pub` or the simulator.
   - Expect it absent from `GET /events` and present in `GET /events/dead-letters`, with
     a reason beginning `EVENT_TYPE_NOT_REGISTERED:`.
   - Publish the same kind on `fab/berlin/plc/e2e-1`. Expect it **stored**, because
     plc is undeclared.
9. `GET /event-sources`. Expect two rows, `manual` and `inference`, both `strict`,
   `version` 0.
10. `PUT /event-sources/manual/mode` `{"mode":"discovery"}` with **no** `If-Match`.
    Expect **428**.
11. The same with `If-Match: 0`. Expect **200**. Repeat step 7's request and expect
    **201**: the door is open again.
12. **The restart step.** Restart the `event-ingestion` resource (use
    `WaitOnResourceUnavailable` if scripting it). Re-`GET /event-sources` with a
    fresh token and expect `inference` still `strict`. Repeat step 8's first
    publish and expect it dead-lettered again. *A mode that does not survive a
    restart is a cache.*
13. **Clean up**: `PUT` both declared sources back to `discovery`. The dev database
    is persistent, and a leftover strict source breaks every later test that
    ingests an unregistered kind through it.

**If step 8 stores the event**, the batch path was not wired. The HTTP steps pass
through the single handler and cannot detect this. That is why FR-005 names both
insertion points.

---

## 7. Latency-budget impact: engaged, on the ≤ 200 ms `Event → overlay state` leg

Spec 103 §6 put both ingest handlers inside this leg, and spec 143 §7 predicted
this follow-up would *"owe one"*. It does. Spec 143's answer was **N/A by
construction**, because it changed no ingest file. That answer is not available
here.

**What is added, per FR-006:**

| Path | Default (no strict source) | With a strict source |
|---|---|---|
| HTTP (`/events/manual`, `/events/webhook/…`) | +1 indexed query per event | +2 per event |
| MQTT fast path (batch) | +1 per batch | +1 per strict fab in the batch |
| MQTT slow path (retry, one at a time) | +1 per event | +2 per event |

Each query hits a table of at most four rows per fab (`source_modes`) or tens of
rows per fab (`registered_event_types`), through a unique index on
`(fab, …)`, and neither is tracked. The **expectation** is sub-millisecond to a
low single-digit number of milliseconds per query on the dev stack. The existing
single path already pays one round trip (`ExistsAsync`) plus the insert, so the
default case adds **one round trip of the same shape**. Spec 006 §IV gave ingest a
50 ms p95 share of the 200 ms leg.

**That expectation is not a figure, and this spec does not record one.** Two
reasons stop the existing leg histogram from supplying it:
1. `sse.latency.segment.duration{segment=event_to_overlay_state}` starts at
   `IngestedAt`, which `Event.Ingest` stamps.
2. The admission lookup runs **before** that stamp on both paths (plan §6), so it
   is invisible to the leg histogram by construction. The same is already true of
   the `ExistsAsync` query.

A "no change in the leg histogram" reading after phase 4 would therefore prove
nothing. **Phase 5 owes a direct measurement** (plan §9):
- `IngestThroughputMeasurementTests` (category `Measurement`), which reports
  arrival-to-visible latency against this leg, run **twice on `develop` and twice
  on this branch** (the first run after machine churn looks like a regression).
- On the branch, once with the measured `(fab, source)` undeclared and once
  declared strict with its kinds registered.
- Plus the admission query's own span duration from the ingest trace.

**§IV's table is not edited.** The row stays *recorded, not yet readable*. A
measurement taken for this spec is a figure *about this change* in the
verification note, not a discharge of the leg. **If phase 5 shows the added cost
is not negligible**, the recorded fallback is an in-memory projection of strict
pairs and registered kinds (the `RuleCacheSeederHostedService` pattern that spec
143 §7 named). That is a follow-up issue, not something built speculatively here
(ADR-0036).

**§VII:** the leg is already subject (no leg is unbuilt). This spec does not
change what its dashboard obligation requires (#1940).

---

## 8. Locked tech choices applied

Nothing new. Every choice already exists in this context.

| Concern | Choice | Precedent |
|---|---|---|
| Persistence | PostgreSQL + EF Core, plain CRUD | ADR-0130; `RegisteredEventType` |
| Aggregate + VOs | Hand-written, `IValueObject<T>`, `.From`, `Ensure.That` | ADR-0038/0046/0066/0105; `RegistrationState` |
| Identifier | `SourceModeIdentifier`, Guid v7 | ADR-0039/0090; `RegisteredEventTypeIdentifier` |
| Layout | `Domain/SourceMode/` + `Events/`; `Commands/`, `Queries/`, `Handlers/`, `*Errors.cs` | ADR-0092/0093 |
| Ingest collaborator | Application class + read port in `Application/Ingress/`, Infrastructure implementation | `IFabStorageReadiness` / `CatalogFabStorageReadiness` |
| Errors | `Result<T, Error>`, `ApiError`, `*Failures` statics | ADR-0047/0089 |
| API | Minimal APIs, per-mapping scopes, `Required scope:` in summary | ADR-0070; `EventTypesEndpoints` |
| Concurrency | `If-Match` + EF token, no retry | ADR-0043/0113 |
| Idempotency | `Idempotency-Key` on the create, existing table | ADR-0142 |
| Migration | EF migration via `MigrationRunner`, generated Designer/snapshot | ADR-0067 |
| Tests | xUnit + Shouldly + hand-written fakes; Aspire fixture | ADR-0052/0053/0054/0103 |

---

## 9. Design decisions and assumptions, marked per ADR-0036

- **A1 — The mode lives in a new `SourceMode` aggregate, not on an existing one.**
  - `RegisteredEventType` is `(fab, kind)`, and giving it a `Source` would undo
    spec 143 FR-001.
  - `WebhookIntegration` would conflate *how this integration authenticates* with
    *how every source named X in fab Y is policed*, and MQTT/manual never reach it.
  - A settings table keyed `(fab, source)` with no aggregate would bypass the
    ADR-0113 version interceptor (`AggregateVersionInterceptor`, which bumps
    versioned aggregate roots). That would leave FR-011's `If-Match` with no
    version to compare.
  - The surrogate identifier is required by `AggregateRoot<TIdentifier>`, not
    chosen for its own sake. The natural key is the unique index.
- **A2 — The key is `(fab, Source)` with `Source`'s four closed values** (§0.1).
  If a finer grain is ever wanted, per integration or per device, it is a new
  nullable column plus an index change. The ingest collaborator's contract (FR-005)
  does not change.
- **A3 — The default is `discovery`.** Four reasons, in order of weight:
  1. **A strict default is an outage on upgrade.** Spec 143's registry shipped
     empty, and no fab has registered anything. Default-strict would refuse
     **every** event on **every** path in **every** deployment the moment this
     merges, and dead-letter the whole MQTT stream. A default that breaks every
     existing installation is not a safe default. It is a different product.
  2. **Discovery is today's behaviour.** Defaulting to it makes this spec
     behaviour-preserving for everyone who does not opt in, and the existing
     ingest suite becomes its characterisation net (plan §9).
  3. **Decision 018 does not privilege either value.** It gives *"each source"* a
     flag. Opt-in strict is reversible per source (US2). Opt-out strict could only
     be undone after the damage.
  4. **§VIII's "safe by default" is about the trust boundary**, and that boundary
     is unchanged. Every source is still authenticated and fab-scoped. What
     discovery admits, #2325 will quarantine rather than fan out, and that is where
     decision 018 puts the safety for unknown types.

  **One consequence for #2325, recorded so it is not discovered there:** once
  quarantine lands, "discovery" stops meaning "fanned out as today". Because
  discovery is the **default**, #2325 will change behaviour for **every
  undeclared source**. An unknown kind that reaches rules today will stop reaching
  them. That spec must treat it as a behaviour change for the default population
  and decide it on purpose.
- **A4 — A management endpoint is in scope.** #2324's *"Where it lands"* names only
  the two ingest insertion points. But the issue's title is *"A source
  **declares** …"*, and without a way to declare a mode the ingest check is
  unreachable except by SQL. That is untestable end to end and unusable by an
  operator. **Three endpoints (declare, change, list) are the minimum that makes US1
  observable and US2 reversible.** *Flagged for the gate. If the reviewer wanted
  ingest wiring only, the alternative is a seeded configuration row, which
  §6 steps 5–12 could not exercise.*
- **A5 — 400, not 422, for the refusal** (FR-008).
- **A6 — Undeclared pairs are not listed by `GET`** (FR-012). Synthesising four
  default rows per fab would invent version numbers for rows that do not exist,
  which is the lost-update trap FR-011's `404` avoids.
- **A7 — No cache on the hot path** (§7). This is a measured bet, with the fallback
  named.

---

## 10. Out of scope, and follow-ups

- **#2325 — quarantine under discovery.** It must read A3's consequence first.
- **#2326 — schema validation of known types.** It still needs an ADR (spec 143 §10
  item 4).
- **The inspector UI and promotion** (spec 143 §10 item 3).
- **A management-web view of source modes.** Not needed before the inspector is.
- **An in-memory projection for the admission lookup.** Filed only if phase 5's
  measurement asks for it (§7).
- **Finer-grained sources** (A2). Only if the product owner asks.

---

## 11. `[NEEDS CLARIFICATION]`

**None.** Four candidates were resolved rather than deferred, and each is marked
for the gate:
- *What a source is*: the product owner's decision (§0). §0.1 records the one
  consequence worth seeing.
- *Whether "no new entity needed" forbids storing the mode in a table*: read as
  no, §0.3 (G1), with the configuration alternative costed.
- *The default mode*: `discovery`, A3.
- *Whether an endpoint is in scope*: yes, A4, with the alternative stated.

---

## 12. Gate — phase 1

- [ ] Spec reviewed; no `[NEEDS CLARIFICATION]` outstanding (§11).
- [ ] §0.1's consequence seen: one mode per `(fab, webhook)` covers **every**
      webhook integration in the fab.
- [ ] **G1 (§0.3) accepted**: "no new entity needed" is read as *no entity to
      identify a source*; the mode itself is stored in a `SourceMode` row. Or the
      configuration alternative is chosen, and US2/FR-010–FR-014 drop.
- [ ] §0.4 seen: the staged T001 scaffolding is **kept** (already `(fab, Source)`),
      the stashed T002 code is **not** applied by phase 3, and the number is 269
      because 267 and 268 were taken.
- [ ] **A3 accepted: the default is `discovery`**, and A3's consequence for #2325
      noted.
- [ ] **A4 accepted: three endpoints are in scope**, or narrowed.
- [ ] FR-013 accepted: writes reuse `sse.events.types.write`, reads reuse
      `sse.events.read`, and there is no realm edit.
- [ ] **FR-016's constitution edit accepted as a factual status correction**, or
      dropped (T014).
- [ ] §7 accepted: §IV is **engaged**, and phase 5 owes a direct measurement, not a
      reading of the leg histogram.
- [ ] Phase 4a colour: **RED** (behaviour-changing). See `plan.md` §9.
