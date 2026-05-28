# Feature Specification: Automation — turn events into overlay state

**Feature Branch:** `007-automation`

**Created:** 2026-05-28

**Status:** Draft (Phase 1 — Specify)

**Input:** Seventh feature of Smart Sentinel Eye and the first
end-to-end slice through the **Automation** bounded context. It
closes the camera → event → overlay loop: events ingested by
spec 006 flow into rule evaluation; rules emit
`SystemVariableValueRequestedV1` (consumed by spec 005's
SystemVariables) and `OverlayHighlightRequestedV1` (consumed by
LayoutComposition's existing `/hubs/layouts` SignalR hub). The
overlay-rendering pipeline that already exists for spec 005 then
carries the resolved text + the highlight CSS class to every
connected kiosk.

Automation v1 is **stateless** — every event is evaluated against
the full rule set in isolation. No sliding windows, no debouncing,
no escalation. Stateful rules (alarm-after-N-seconds, etc.) are
explicitly deferred. The point of v1 is to demonstrate the closed
loop at production latency (≤ 150 ms p95 from
`FabEventIngestedV1` arrival to the action's V1 event hitting the
bus) on the realistic 1 000 ev/s sustained rate that spec 006
underwrites.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Admin authors a rule (Priority: P1)

A fab admin opens **Rules** in management-web, clicks **New
rule**, picks a trigger source (e.g. ``plc``) and trigger kind
(``PlcCycleStart``), fills in a predicate
(``$.payload.cycleTime <= 30``), picks an action
(**Set system variable** ``oeeLine1`` to expression
``100 - ($.payload.cycleTime * 2)``), and clicks **Save**. The
rule is created in **Draft** state and is not yet evaluated
against incoming events.

**Why this priority:** Smallest vertical slice — exercises the
`Rule` aggregate, the predicate expression parser at validation
time, the action-value expression parser, the lifecycle state
machine, and the form-based UI. Every downstream story depends on
this one.

**Independent Test:**

1. Start the system locally via ``aspire run``.
2. Sign in to ``management-web`` as admin.
3. Click **Rules** in the nav, then **New rule**.
4. Fill in name = ``high-oee-on-fast-cycle``, trigger source
   = ``plc``, trigger kind = ``PlcCycleStart``, predicate
   = ``$.payload.cycleTime <= 30``, action = **Set
   system variable** ``oeeLine1`` to expression
   ``100 - $.payload.cycleTime * 2``.
5. Click **Save**. The new row appears with state **Draft**.

**Acceptance Scenarios:**

1. **Given** no rule named ``high-oee-on-fast-cycle`` exists,
   **when** the admin saves a new rule with that name, **then** a
   row appears in the rules list with state ``Draft``.
2. **Given** a malformed predicate (e.g. ``$.payload..cycleTime``),
   **when** the admin tries to save, **then** the request is
   rejected with a 400 carrying the parser's error location;
   the rule is not created.
3. **Given** an action expression that references a JSONPath the
   trigger schema doesn't typically supply, **when** the admin
   saves, **then** the rule is created with a warning (we can't
   actually verify schema-compat without a sample event).

---

### User Story 2 — Admin publishes a rule, real events flow through (Priority: P1)

The admin opens the ``high-oee-on-fast-cycle`` rule, clicks
**Publish**. The rule's state flips from ``Draft`` to ``Active``;
it now matches against every incoming
``FabEventIngestedV1`` whose source is ``plc`` and kind is
``PlcCycleStart``. When a PLC publishes a ``cycleTime: 27``
event, Automation evaluates the predicate (true), evaluates the
action expression (``100 - 27*2 = 46``), publishes
``SystemVariableValueRequestedV1(name=oeeLine1, value=46.0)``;
spec 005's SystemVariables subscriber sets the variable, the
broadcaster pushes the resolved overlay text to kiosks.

**Why this priority:** This is the actual product value — rules
exist to turn fab signals into operator-visible state. Exercises
the full event-driven path through Wolverine plus the integration
boundary with SystemVariables.

**Independent Test:**

1. Pre-conditions: spec 005's ``oeeLine1`` variable exists as a
   ``Number`` (defined via management-web). Spec 006's
   ``station-4`` MQTT device is authenticated and an overlay
   referencing ``{{oeeLine1}}`` is published on a layout bound to
   a kiosk.
2. On the rules list, click **Publish** on
   ``high-oee-on-fast-cycle``. The state flips to **Active**.
3. Publish a PLC event with ``cycleTime: 27`` via
   ``mosquitto_pub``.
4. Within ≤ 150 ms (excluding spec 006's 50 ms ingest leg), the
   kiosk's overlay text updates to show
   ``OEE: 46%`` (assuming the overlay was
   ``OEE: {{oeeLine1}}%``).

**Acceptance Scenarios:**

1. **Given** a published rule matches an event, **when** the event
   flows through Automation, **then** the matching
   ``SystemVariableValueRequestedV1`` lands on the bus within
   ≤ 100 ms p95 of consuming the
   ``FabEventIngestedV1``.
2. **Given** the rule's predicate evaluates to false, **when** the
   event is consumed, **then** no action event is published.
3. **Given** the action expression references a variable that
   doesn't exist (typo in the rule), **when** the event is
   consumed, **then** the action event is still published; the
   downstream SystemVariables handler is responsible for the
   404 + audit log (Automation doesn't pre-check existence).

---

### User Story 3 — Admin archives a rule (Priority: P1)

The admin notices ``high-oee-on-fast-cycle`` is firing too
aggressively and decides to retire it. Click **Archive**. The
rule transitions ``Active → Archived``. Subsequent events that
would have matched no longer fire the rule. The rule remains in
the audit/history list.

**Why this priority:** Closes the lifecycle. Without archiving,
mistakes accumulate and the system can't recover from a bad rule
without a redeploy.

**Independent Test:**

1. Pre-conditions: rule from US2 is **Active** and firing.
2. Click **Archive** on the row.
3. Publish another PLC event with ``cycleTime: 27``.
4. ``oeeLine1`` is not re-set; the kiosk's overlay value remains
   whatever it was set to before archive (no live update).
5. The rules list still shows the row with state **Archived**.

**Acceptance Scenarios:**

1. **Given** an archived rule, **when** matching events arrive,
   **then** no action events fire.
2. **Given** an archived rule, **when** the admin tries to
   re-activate via the API, **then** the request is rejected; the
   only path back is to clone the rule (preserves the audit trail).
3. **Given** the rule name was ``foo`` and the admin archives it,
   **when** the admin creates a fresh rule also named ``foo``,
   **then** it succeeds (archived names are released for re-use,
   same as spec 005 SystemVariables).

---

### User Story 4 — Two rules both fire on the same event; last write wins (Priority: P2)

Two **Active** rules both match the same ``PlcCycleStart`` event:
``rule-a`` (declared first) sets ``oeeLine1`` to ``50``;
``rule-b`` (declared second) sets ``oeeLine1`` to ``75``.
Automation evaluates both in declaration order; both fire; the
last write (``rule-b`` → ``75``) wins per spec FR-011.

**Why this priority:** P2 because conflicts are an edge case in
practice (admins don't usually author colliding rules on
purpose), but the deterministic behaviour is documented and
tested.

**Independent Test:**

1. Pre-conditions: ``oeeLine1`` exists.
2. Create + Activate ``rule-a`` and ``rule-b`` as above.
3. Publish a matching PLC event.
4. ``oeeLine1`` ends at ``75`` (the value from the later-
   declared rule).

**Acceptance Scenarios:**

1. **Given** two rules write the same variable, **when** an event
   matches both, **then** the variable's final value matches the
   later-declared rule's output.
2. **Given** ``rule-a`` writes ``oeeLine1`` and ``rule-b`` writes
   ``shiftStatus``, **when** an event matches both, **then** both
   variables are updated; no conflict.

---

### User Story 5 — Rule highlights an overlay on a critical event (Priority: P1)

An inference event ``PersonInRestrictedZone`` (from spec 006's
camera-side AI) fires. A rule with action
**Highlight overlay** ``camera-12-zone-alert`` publishes
``OverlayHighlightRequestedV1(overlay=…, highlight=true,
durationMs=10_000)``. LayoutComposition.Application subscribes,
pushes ``OverlayHighlightChanged`` on the existing
``/hubs/layouts`` SignalR hub. Every kiosk rendering the
overlay flips a CSS class (``ssE-overlay-highlight``) for the
next 10 seconds and reverts automatically.

**Why this priority:** Demonstrates that the action surface
extends beyond text updates. Closes the safety-event loop.

**Independent Test:**

1. Pre-conditions: an overlay ``camera-12-zone-alert`` is
   published on a layout bound to a kiosk.
2. Create + Activate a rule:
   - source = ``inference``, kind = ``PersonInRestrictedZone``,
   - predicate = ``$.payload.confidence > 0.8``,
   - action = **Highlight overlay** ``camera-12-zone-alert`` for
     ``10000`` ms.
3. Publish an inference event with confidence ``0.92`` via
   ``mosquitto_pub``.
4. The kiosk renders the overlay with a red border (or whatever
   styling the ``ssE-overlay-highlight`` CSS class applies) for
   ~10 seconds, then reverts.

**Acceptance Scenarios:**

1. **Given** the rule matches, **when** the event flows, **then**
   ``OverlayHighlightRequestedV1`` arrives on the bus and a
   ``OverlayHighlightChanged`` SignalR frame arrives at the kiosk
   within ≤ 100 ms p95 (Automation leg only).
2. **Given** the rule's ``durationMs`` is 10 000, **when** the
   highlight is applied, **then** the kiosk's CSS class is
   removed after 10 ± 0.5 seconds (client-side timer).
3. **Given** two rules both highlight the same overlay with
   overlapping durations, **when** both fire, **then** the kiosk
   keeps the highlight class until the later of the two
   durations expires (the kiosk OR's, not last-write-wins).

---

## Functional Requirements

### Rule shape + lifecycle
- **FR-001** Each rule is a row in the ``rules`` table with the
  canonical fields: ``{ ruleIdentifier, name, triggerSource,
  triggerKind, predicate, action, state, createdAt, createdBy,
  publishedAt?, archivedAt? }``.
- **FR-002** ``name`` follows the grammar
  ``^[a-z][a-z0-9-]{1,62}$`` (kebab-lowercase, 2–63 chars). The
  pair ``(fabId, name)`` is unique across non-archived rules;
  archived names are released for re-use (mirrors spec 005).
- **FR-003** ``state`` is one of ``Draft | Active | Archived``.
  Allowed transitions: ``Draft → Active`` (Publish),
  ``Active → Archived`` (Archive), ``Draft → Archived``
  (cancel). No other transitions.
- **FR-004** Only ``Active`` rules are evaluated against incoming
  events.

### Trigger + predicate
- **FR-005** ``triggerSource`` is a closed string (``plc |
  inference | manual | webhook``) matching spec 006 ``Source``.
- **FR-006** ``triggerKind`` is a free-form ``Kind`` (PascalCase,
  1–128 chars) matching spec 006 ``Kind``.
- **FR-007** ``predicate`` is a string in the **automation
  expression language** (AEL — see §AEL grammar below). Parsed
  + validated at save time; rejected at the API edge.
- **FR-008** A rule matches if ``triggerSource`` AND
  ``triggerKind`` both equal the event's, AND the predicate
  evaluates to ``true`` against the event's payload + envelope.

### Action shape
- **FR-009** Each rule has exactly one ``action`` in v1. Two
  action variants: ``SetVariableValue`` and ``HighlightOverlay``.
- **FR-010** ``SetVariableValue`` carries:
  ``{ variableName: string, valueExpression: string }`` where
  ``valueExpression`` is AEL (evaluated against the same context
  as the predicate; result coerced to the variable's declared
  type by SystemVariables).
- **FR-011** ``HighlightOverlay`` carries:
  ``{ overlayIdentifier: Guid, durationMs: int (>= 500, <= 60000) }``.
  No expression evaluation; the overlay + duration are fixed at
  authoring time.
- **FR-012** Conflict resolution: when multiple ``Active`` rules
  match the same event, ALL matching rules fire in
  ``createdAt`` ascending order. For ``SetVariableValue``,
  the later-fired rule's value overwrites earlier ones (last
  write wins per variable). For ``HighlightOverlay``, all
  highlight events fire independently; the kiosk OR's the
  durations.

### AEL grammar
- **FR-013** The AEL is a tight expression language with these
  productions:
  - Literals: integers, decimals, single- or double-quoted
    strings, ``true`` / ``false``.
  - Field access: ``$.payload.foo.bar`` (JSONPath subset:
    dot-separated keys, no bracket indexing in v1) and
    ``$.kind`` / ``$.source`` / ``$.device`` for envelope fields.
  - Binary operators: ``==``, ``!=``, ``<``, ``<=``, ``>``,
    ``>=``, ``+``, ``-``, ``*``, ``/``, ``%``.
  - Logical operators: ``&&``, ``||``, ``!``.
  - Grouping: ``(`` … ``)``.
  - String operator: ``contains`` (left.contains(right), case-
    sensitive).
- **FR-014** AEL has no function-call syntax in v1; no variables
  beyond ``$.…``. Operator precedence follows C# (unary > mul >
  add > comparison > equality > logical-and > logical-or).
- **FR-015** Predicate evaluation result type MUST be ``bool``;
  any other result type fails the rule's evaluation (logged but
  doesn't crash the loop).
- **FR-016** ``valueExpression`` result type can be any of
  ``int | decimal | string | bool``; SystemVariables coerces to
  the target variable's declared type per spec 005 FR-007.

### Event fan-out
- **FR-017** Automation subscribes to ``FabEventIngestedV1``
  (from spec 006) via Wolverine; queue prefix ``automation``.
  Per-source FIFO is preserved by routing on
  ``(source, deviceId)`` hash.
- **FR-018** Each matching ``SetVariableValue`` action publishes
  ``SystemVariableValueRequestedV1(name, value, requestedAt,
  causingEventIdentifier)``. SystemVariables subscribes and
  dispatches its existing ``SetVariableValueCommand`` internally.
  This is a NEW V1 contract in ``Shared.Contracts/SystemVariables/``.
- **FR-019** Each matching ``HighlightOverlay`` action publishes
  ``OverlayHighlightRequestedV1(overlayIdentifier, durationMs,
  requestedAt, causingEventIdentifier)``. LayoutComposition
  subscribes and pushes ``OverlayHighlightChanged`` on the
  existing ``/hubs/layouts`` hub. NEW V1 contract.
- **FR-020** Every action event carries ``causingEventIdentifier``
  (the ``FabEventIngestedV1.EventIdentifier``) so audit
  log + replay can correlate cause and effect.

### Read model + API
- **FR-021** ``GET /rules`` lists rules with optional ``state``
  filter and ``triggerSource`` / ``triggerKind`` filters.
- **FR-022** ``GET /rules/{name}`` returns one rule.
- **FR-023** ``POST /rules`` creates a Draft rule. Body:
  ``{ name, triggerSource, triggerKind, predicate, action }``.
  Validates predicate + value expressions at parse time; rejects
  the request with a 400 carrying parser error location if either
  is malformed.
- **FR-024** ``POST /rules/{name}/publish`` flips ``Draft →
  Active``. Idempotent on ``Active``.
- **FR-025** ``POST /rules/{name}/archive`` flips ``Active →
  Archived`` (or ``Draft → Archived`` for never-published).
  Idempotent on ``Archived``.
- **FR-026** ``GET /rules/{name}/dry-run`` accepts a sample event
  body and returns whether the predicate matches + the evaluated
  action result (without firing the action event). Critical for
  authoring confidence.

### Authorization
- **FR-027** All write endpoints (``POST /rules``,
  ``/publish``, ``/archive``) require admin policy.
- **FR-028** Reads require any authenticated user.

## Non-Functional Requirements

- **NFR-001** ``FabEventIngestedV1`` arrival → action V1 event on
  the bus: **≤ 100 ms p95**, with **≤ 150 ms p99**. Budget
  breakdown:
  - ≤ 5 ms Wolverine deserialise.
  - ≤ 20 ms rule lookup + filter (Active rules, by trigger
    source/kind — backed by an in-memory cache rebuilt on rule
    state-change V1 events).
  - ≤ 30 ms predicate evaluation (worst case across ≤ 100
    matching rules; the AEL interpreter targets ≤ 10 µs/eval).
  - ≤ 30 ms action evaluation + Wolverine outbox dispatch.
  - ≤ 15 ms headroom.
- **NFR-002** AEL interpreter throughput: **≥ 100 000 evals/sec
  per core** on the dev hardware envelope. Hand-rolled, no
  reflection in the hot path. ≤ 10 µs p99 per eval.
- **NFR-003** Rule cache rebuild on startup: ≤ 500 ms cold-start
  for 1 000 rules. The cache is an in-memory
  ``ConcurrentDictionary<(Source, Kind), List<CompiledRule>>``
  seeded by querying the rules table + kept fresh via
  ``RulePublishedV1`` / ``RuleArchivedV1`` subscribers.
- **NFR-004** Scale: 1 000 ``Active`` rules per fab,
  100 matches per second sustained worst case (within the spec
  006 1 000 ev/s envelope; assumes ≤ 10% of events match a
  rule). Above this, revisit the per-trigger-kind bucketing.
- **NFR-005** Replay-safety: ``causingEventIdentifier`` on every
  action event + Wolverine's at-least-once semantics + dedup at
  SystemVariables (idempotent on `(variable, causingEventId)`)
  ensure replay produces the same final state.

## Out of Scope (deferred or rejected)

- **Stateful rules / sliding windows / debouncing / escalation.**
  Spec 008+ once we have data on which patterns matter.
- **Notification actions** (email / Slack / kiosk toast). A
  Notifications context doesn't exist yet; spec 008+.
- **Recording-trigger actions** (StreamDistribution
  ``StartRecording`` integration command). Spec 008+.
- **AEL function calls** (e.g. ``round($.x, 2)``,
  ``now()``). Add when concrete need arises.
- **AEL bracket / array indexing** (``$.payload.tags[0]``). v1
  payload schemas don't need it yet.
- **Rule revisions / versioned audit history** (spec 004 style).
  Two-state lifecycle ``Draft → Active → Archived`` is enough;
  edits to a Draft are in-place; edits to an Active rule require
  Archive + clone.
- **Rule priorities beyond ``createdAt`` ordering.** If users
  start asking for an explicit ``priority`` field, revisit.
- **Multi-action rules.** One action per rule; compose by
  authoring multiple rules with the same trigger.
- **Rule grouping / categorisation.**
- **Hot-reload of rule files from disk.** All persistence is in
  Postgres.

## Cross-Context Reach

Automation is **wire-out only**. It publishes two new V1
integration events (``SystemVariableValueRequestedV1`` +
``OverlayHighlightRequestedV1``) and consumes
``FabEventIngestedV1``. **No new ``AllowedCrossContext``
entries** — both fan-outs go through ``Shared.Contracts``.

The consumer-side wiring lands inside the consumer context:
- ``SystemVariables.Application`` adds a new event handler that
  subscribes to ``SystemVariableValueRequestedV1`` and dispatches
  the existing ``SetVariableValueCommand``. This expands spec
  005's surface; no new SystemVariables cross-context refs.
- ``LayoutComposition.Application`` adds a new event handler that
  subscribes to ``OverlayHighlightRequestedV1`` and pushes
  ``OverlayHighlightChanged`` via the existing
  ``ILayoutLifecycleBroadcaster``. Same broadcaster bridge
  spec 005 introduced; the existing
  ``AllowedCrossContext`` entries cover the consumer side.

## Constitution Check

- **§I (walking skeleton):** spec 007 is the closing piece of the
  camera → event → overlay loop. Without it, ingested events
  don't drive overlay state automatically.
- **§II (locked tech stack):** no tech additions. The AEL
  interpreter is hand-rolled (no parser library). Re-uses
  Wolverine, EF Core, Postgres, RabbitMQ as before.
- **§III (bounded-context isolation):** Automation publishes V1
  events; consumers wire in within their own contexts. No new
  cross-context project refs needed.
- **§IV (latency budget):** ≤ 100 ms p95 for Automation's leg is
  the remaining slice of the 200 ms event-to-overlay-state
  budget after spec 006's 50 ms ingest and spec 005's 50 ms
  resolve. ≤ 50 ms headroom for SignalR fan-out is in spec 005.
  Total: 50 ms (ingest) + 100 ms (automation) + 50 ms
  (resolve+broadcast) ≈ 200 ms p95 to overlay state-change.
- **§V (spec-driven):** this spec. Plan + tasks follow.
- **§VI (Aspire composition root):** ``automation`` Aspire
  project resource + ``automation-db`` Postgres database.
- **§VII (no event sourcing without justification):** rules are
  CRUD aggregates. The in-memory rule cache is a derived read
  projection rebuilt from V1 events on cold start.
- **§VIII (safe at trust boundaries):** admin policy on writes;
  AEL parsing rejects malformed predicates at the API edge.
- **§IX (forward-compat):** ``SystemVariableValueRequestedV1``
  and ``OverlayHighlightRequestedV1`` are V1 — additive
  evolution only. The AEL grammar is documented + bounded; v2
  can add function-calls without invalidating v1 expressions.

## Gate (Phase 1 → Phase 2)

This spec is ready for the Plan phase once the architect lead
confirms:

1. No ``[NEEDS CLARIFICATION]`` markers remain. ✅
2. The five user stories cover the v1 product surface; each is
   independently testable.
3. The AEL grammar is acceptable as the v1 expression contract
   (subset of comparators + arithmetic + boolean ops; no
   function-calls; no bracket indexing).
4. The two new V1 integration events are acceptable as the
   wire-out contracts (no cross-context project refs needed).
5. The ≤ 100 ms p95 NFR-001 budget breakdown is plausible to
   verify in CI under Testcontainers.

When the gate is approved, Phase 2 (``/speckit-plan``) drafts
``plan.md`` against the locked tech stack — a new ``Rule``
aggregate (``Domain``), the AEL parser + interpreter
(``Application``), the rule cache + Wolverine subscriber
(``Infrastructure``), a partitioned ``rules`` table, two new V1
contracts, and the form-based React editor in
``apps/management-web``.
