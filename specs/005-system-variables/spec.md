# Feature Specification: System Variables Bind Overlay Labels to Live Values

**Feature Branch:** `005-system-variables`

**Created:** 2026-05-27

**Status:** Draft (Phase 1 — Specify)

**Input:** Fifth feature of Smart Sentinel Eye — the first end-to-end
slice through the **SystemVariables** bounded context. Builds on
spec 004 (OverlayDesigner) by giving the previously-literal
``{{placeholder}}`` tokens in overlay labels real meaning: each token
is the name of a typed system variable whose value an admin sets in
the management UI; every kiosk rendering an overlay that references
the variable updates its label within 200 ms of the value change.

Spec 005 is **manual data entry only** — no event-driven feeds, no
external producers. EventIngestion-driven values are a spec 006
concern and explicitly out of scope here. The point of v1 is to land
the *resolution + render-push pipeline* without coupling it to MES
integration; once spec 006 lands, the same pipeline carries
event-sourced values for free.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Admin defines a system variable (Priority: P1)

A fab admin opens the management app, clicks **System variables**
in the nav, then **New variable**. They pick a name (e.g.
``oeeLine1``), a type (``String`` / ``Number`` / ``Boolean``), and
optionally an initial value. Click **Save**. The variable is now
referenceable from any overlay's label text via ``{{oeeLine1}}``.

**Why this priority:** Smallest vertical slice — exercises the
``Variable`` aggregate, its typed value VO with an ``Unset`` state,
and the validation rules (name uniqueness across the fab, type-
appropriate initial value, lifecycle). Every downstream story
assumes this one is in place.

**Independent Test:**

1. Start the system locally via ``aspire run``.
2. Sign in to ``management-web`` as admin.
3. Click **System variables** in the nav, then **New variable**.
4. Enter the name ``oeeLine1``, type ``Number``, leave the value
   empty.
5. Click **Save**. The new row appears with state **Defined**
   and value **(unset)**.

**Acceptance Scenarios:**

1. **Given** no variable named ``oeeLine1`` exists, **when** the
   admin saves a new variable with that name and type ``Number``
   and no initial value, **then** a row appears in the variables
   list with state ``Defined``, type ``Number``, and value
   ``(unset)``.
2. **Given** a variable ``oeeLine1`` already exists, **when** the
   admin tries to create another with the same name, **then**
   they get a clear ``Name already in use`` error and the second
   variable is not created.
3. **Given** a non-conforming name (empty, > 64 chars, contains
   space or punctuation), **when** the admin tries to save,
   **then** they get a validation error before the request is
   submitted.

---

### User Story 2 — Admin sets a variable's value (Priority: P1)

The admin clicks **Set value** on the ``oeeLine1`` row, enters
``82.4``, and saves. Every kiosk rendering an overlay whose label
contains ``{{oeeLine1}}`` updates the rendered text within **200 ms**.

**Why this priority:** This is the actual product value — variables
are useful only if their changes flow to the wall. Exercises the
full resolution + push pipeline through the reverse-index, the
SignalR fan-out, and the kiosk render path.

**Independent Test:**

1. Pre-condition: spec 004 — at least one Layout published, an
   overlay published with label text ``OEE: {{oeeLine1}}%``, and
   the overlay bound to the Layout. A kiosk is rendering the
   Layout's cell. ``oeeLine1`` exists as a ``Number`` variable.
2. On the management-web variables page, click **Set value** on
   ``oeeLine1``. Type ``82.4`` and save.
3. Observe the kiosk's label change from ``OEE: {{oeeLine1}}%``
   (or whatever the previous value was) to ``OEE: 82.4%`` within
   ≤ 200 ms of the save click.

**Acceptance Scenarios:**

1. **Given** a kiosk is rendering a layout whose overlay references
   ``{{oeeLine1}}``, **when** the admin sets ``oeeLine1`` to a new
   value, **then** the kiosk's rendered text updates within 200 ms.
2. **Given** the variable type is ``Number``, **when** the admin
   tries to set the value to ``not-a-number``, **then** the request
   is rejected with a clear type-mismatch error.
3. **Given** the variable type is ``Boolean``, **when** the admin
   sets the value to ``true``, **then** the kiosk renders the
   variable's configured truthy string (default ``Yes``).

---

### User Story 3 — Overlay label resolves placeholders at render time (Priority: P1)

A kiosk fetches a Layout. The bound overlay's label text contains
``OEE: {{oeeLine1}}% Status: {{lineStatus}}``. The server resolves
the two placeholders using the current values of ``oeeLine1`` and
``lineStatus`` and the kiosk renders the resolved text — without
any client-side parsing of the placeholder syntax.

**Why this priority:** Closes the loop. Without resolution at fetch
time, the kiosk would render literal ``{{}}`` tokens on the cold
load (before the first SignalR push). Exercises the server-side
resolver + the reverse-index that maps variable → referencing
overlays.

**Independent Test:**

1. With ``oeeLine1 = 82.4`` and ``lineStatus = "Healthy"`` already
   set, publish a new overlay with label ``OEE: {{oeeLine1}}%
   Status: {{lineStatus}}``.
2. Bind it to a Layout and publish the Layout.
3. The kiosk picks up the layout and renders the label as ``OEE:
   82.4% Status: Healthy`` immediately on first fetch (no
   placeholder flicker, no SignalR-driven late substitution).

**Acceptance Scenarios:**

1. **Given** every referenced variable is set, **when** the kiosk
   fetches the overlay, **then** the rendered text contains every
   value substituted, no ``{{}}`` tokens remain.
2. **Given** a referenced variable is ``(unset)``, **when** the
   kiosk fetches the overlay, **then** the literal ``{{name}}``
   placeholder appears in the rendered text in place of the
   value.
3. **Given** a referenced variable doesn't exist at all, **when**
   the kiosk fetches the overlay, **then** the literal
   ``{{name}}`` placeholder appears unchanged (identical to the
   unset case from the kiosk's perspective).

---

### User Story 4 — Admin archives a variable (Priority: P2)

The admin clicks **Archive** on a variable row. The variable can no
longer accept value updates. Any overlay still referencing it
renders the literal ``{{name}}`` placeholder (same as the unset and
missing cases) until the admin updates the overlay's label to
remove the reference.

**Why this priority:** Cleanup path. Without it, the variables
list grows without bound and accidentally-published variables
have no out. Mirrors the layout/overlay archive lifecycle from
specs 003 + 004.

**Acceptance Scenarios:**

1. **Given** ``oeeLine1`` is archived, **when** the admin opens
   the variable detail page, **then** the **Set value** button is
   disabled with a tooltip explaining the archive state.
2. **Given** an overlay references an archived variable, **when**
   the kiosk renders that overlay, **then** the literal
   ``{{name}}`` placeholder appears (no rendering crash, no
   stale value).
3. **Given** a variable was archived, **when** the admin tries to
   create a new variable with the same name, **then** the create
   succeeds (the archived variable's name is freed for reuse).

---

## Functional Requirements *(mandatory)*

### Domain
- **FR-001** A *system variable* is uniquely identified by name
  within the fab; name is case-sensitive, 1-64 chars, alphanumeric
  + underscore only, must start with a letter.
- **FR-002** A variable has exactly one type chosen at creation
  time: ``String``, ``Number`` (IEEE-754 double, culture-invariant
  decimal string on the wire), or ``Boolean``.
- **FR-003** A variable's type is immutable. To change a type,
  archive the variable and recreate it with a new type.
- **FR-004** A variable can be ``Unset`` at any time. Creating a
  variable without an initial value yields an ``Unset`` variable.
  Once a value is set, ``Unset`` is unreachable except via archive.
- **FR-005** A variable's lifecycle has exactly three states:
  ``Defined`` (created, may be Unset or have a value), ``Archived``
  (terminal, name freed for reuse on the next ``CreateVariable``).
- **FR-006** A ``Boolean`` variable carries optional ``TruthyLabel``
  / ``FalsyLabel`` strings (defaults ``Yes`` / ``No``) used by the
  resolver to render the value.
- **FR-007** A ``Number`` variable serialises on the wire as a
  culture-invariant decimal string with no thousands separator
  (e.g. ``82.4``, ``-1.5``, ``1000000``).
- **FR-008** Setting a value validates the value against the
  variable's type. Type mismatch is rejected with a ``ApiError``
  carrying a ``VARIABLE_TYPE_MISMATCH`` code.

### Placeholder syntax + resolution
- **FR-009** A placeholder in an overlay label is the literal
  string ``{{name}}`` where ``name`` matches FR-001's grammar.
  Anything else inside ``{{}}`` is left literal.
- **FR-010** The server resolves overlay labels at fetch time.
  ``GET /overlays/{id}`` returns the resolved label for the
  currently-Published revision, with every placeholder either
  substituted with its current value or left literal per FR-011.
- **FR-011** A placeholder resolves to the variable's current
  value if the variable exists, is not archived, and is not
  ``Unset``. In every other case (missing, archived, unset), the
  literal ``{{name}}`` placeholder is kept in the rendered text.
- **FR-012** The server maintains a reverse index ``{variable name
  → [overlay identifiers that reference it]}``. The index is
  derived from the overlay label text on every
  ``OverlayRevisionPublishedV1`` and is kept consistent across
  the chain's lifecycle (re-published revisions update the index;
  archived overlays leave it).

### Update push
- **FR-013** When a variable's value changes, the server resolves
  every overlay in the reverse-index entry for that variable and
  pushes a ``ResolvedOverlayTextChangedV1`` SignalR frame to the
  existing ``/hubs/layouts`` hub. The frame carries
  ``{ overlayIdentifier, resolvedText }``.
- **FR-014** When a variable is archived, the server pushes one
  ``ResolvedOverlayTextChangedV1`` frame per affected overlay with
  the new resolved text (i.e., literal ``{{name}}`` reinstated).
- **FR-015** A connected kiosk reacts to ``ResolvedOverlayTextChangedV1``
  by replacing the rendered text on every CameraViewer that's
  showing the affected overlay. No kiosk-side parsing of ``{{}}``
  is required — the client treats the text as opaque.

### Read model + API
- **FR-016** ``GET /system-variables`` lists every variable in the
  fab with name, type, value (or ``(unset)``), state.
- **FR-017** ``GET /system-variables/{name}`` returns one variable.
- **FR-018** ``POST /system-variables`` creates a new variable
  (admin only). Body: ``{ name, type, value? }``. Returns 201 with
  Location header.
- **FR-019** ``PUT /system-variables/{name}/value`` sets the
  variable's current value (admin only). Body: ``{ value }`` typed
  by the variable's declared type. Returns 200; the value is
  effective on the next push.
- **FR-020** ``POST /system-variables/{name}/archive`` archives the
  variable (admin only). Returns 200.

### Authorization
- **FR-021** Every write endpoint requires the admin policy
  (per ADR-0007 + ADR-0008). Reads require any authenticated user.

## Non-Functional Requirements

- **NFR-001** Variable update → kiosk render: ≤ 200 ms p95 measured
  from ``PUT .../value`` success to the kiosk's rendered text
  change. Budget breakdown: ≤ 50 ms server-side resolution (in-
  memory reverse-index lookup), ≤ 50 ms SignalR fan-out, ≤ 100 ms
  network round-trip on a local fab.
- **NFR-002** Reverse-index lookup is O(1) per variable name. The
  index lives in-memory in the SystemVariables process; it is
  rebuilt on process start by replaying
  ``OverlayRevisionPublishedV1`` from the Wolverine outbox-backed
  log. The constitution's "no event sourcing without justification"
  rule (§VII) is honoured by keeping the *authoritative* state in
  Postgres; the reverse-index is a fast cache.
- **NFR-003** Scale target: 1 000 variables per fab, 100 overlays
  referencing variables. Above this, revisit the in-memory
  reverse-index assumption.
- **NFR-004** Resolver concurrency: a single variable update may
  fan out to up to 50 overlays. Each resolution is in-memory string
  replacement; total elapsed must stay within the 50 ms server-side
  budget from NFR-001.

## Out of Scope (deferred or rejected)

- **Event-driven values** — variable values set from
  ``EventIngestion`` integration events. Spec 006.
- **Variable history / time series** — only the current value is
  exposed. Audit log captures who set what when in spec 007+.
- **Namespaces** — flat global names only.
- **Formatting modifiers** in placeholder syntax (e.g.
  ``{{oee:F2}}``). Producers emit pre-formatted strings if they
  need decimal control; a Number variable rendering ``82.4`` is the
  ceiling for v1.
- **Per-camera / per-layout overrides** — one global scope.
- **Renaming** — name immutable. Archive + recreate to change.
- **Webhook ingest** — admin UI is the only setter in v1.

## Cross-Context Reach

SystemVariables.Application **must read** OverlayDesigner's overlay
label text to maintain the reverse-index. The clean path is to
subscribe to ``OverlayRevisionPublishedV1`` / ``OverlayRevisionArchivedV1``
on the integration bus (already published by spec 004's
broadcaster bridge). No new project reference to OverlayDesigner is
needed; the V1 events carry the label text already.

SystemVariables.Application **must push** SignalR frames to the
``/hubs/layouts`` hub owned by LayoutComposition.Infrastructure.
This is a second documented exception alongside the spec 004 bridge.
Both will be expressed in the architecture's
``AllowedCrossContext`` table (introduced in the recent hygiene
bundle) as a one-line addition.

## Constitution Check

- **§I (walking skeleton):** spec 004's overlay rendering becomes
  *dynamic*. Operators see live values on the wall — the original
  promise.
- **§IV (latency budget):** the 200 ms variable-push budget is a
  proper subset of the overall 800 ms event-to-overlay budget; spec
  006 will spend the remaining ≤ 600 ms on EventIngestion's leg.
- **§VII (no event sourcing without justification):** Variables are
  CRUD aggregates in Postgres. The reverse-index is a derived
  read model rebuilt from V1 events on cold start — same pattern as
  every other read projection in the system.
- **§IX (forward-compat):** the ``ResolvedOverlayTextChangedV1``
  frame name is intentionally future-friendly — spec 006 can reuse
  it unchanged when event-driven values land.

## Gate (Phase 1 → Phase 2)

This spec is ready for the Plan phase once the reviewer (the
architect lead) confirms:

1. No ``[NEEDS CLARIFICATION]`` markers remain. ✅
2. The four user stories cover the v1 product surface. Each is
   independently testable on its own.
3. The cross-context reach is acceptable to add to
   ``BoundaryTests.AllowedCrossContext``.
4. The 200 ms NFR is achievable on the available SignalR
   infrastructure; the alternative (1 s) is explicitly considered
   and rejected. The user has chosen 200 ms; the plan will need to
   verify the math.

When the gate is approved, Phase 2 (``/speckit-plan``) drafts
``plan.md`` against the locked-in tech stack — likely a new
``SystemVariables.Domain`` aggregate, an in-memory reverse-index
read model, two new V1 integration-event types, a SignalR hub
extension on the layout-composition Api, and a new React page in
management-web.
