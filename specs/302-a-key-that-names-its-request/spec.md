# Spec 302 — A key that names its request

**Issues:** #2424 — *An Idempotency-Key replay is not matched against the request it replays: same caller, same key, different resource returns the wrong one's live secret* · #2492 — *IdempotencyScope has no fab, so one key reused across an operator's two fabs replays the wrong fab's answer*
**Branch:** `fix/2424-2492-idempotency-scope-fab-and-resource` (cut from `develop` at `55e44593`)
**Phase-4a colour:** **RED** (behaviour-changing) — a mismatched replay that used to answer `201`/`200` with another request's resource now answers `422 IDEMPOTENCY_KEY_REUSED`. A test that arrives green is a phase-4 failure (ADR-0139).
**ADRs:** **ADR-0142** (idempotency keys — the mechanism, and the source of the fab column this spec makes real), ADR-0143 (why `POST` retries reach these endpoints), ADR-0047 / ADR-0089 (error shape: named code + HTTP status), ADR-0103 (integration tests against the Aspire fixture), ADR-0105 (`Ensure.That`), ADR-0067 (migrations run by `MigrationRunner`), ADR-0084 (readable parameter lists), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane), ADR-0037 (phases), ADR-0109 (parallel markers)
**Severity:** Same-tenant cross-resource **secret disclosure** on three credential endpoints (`POST /webhook-integrations/{name}/rotate`, `POST /devices/register`, `POST /kiosks/enroll`), plus a silently-dropped create with a fabricated `201` on the other nine.

**Human decisions (recorded on the issues, the authority for this spec):**
- #2424: "Widen `IdempotencyScope` to include the bound resource (and fab — see #2492, resolving together, likely the same column landing once). New 409/422 surface across the 10 affected endpoints, but closes a real conformance gap ADR-0142 already assumed existed."
- #2492: "Resolving together with #2424 … Decision: widen the scope to include fab, likely the same schema change as #2424's resource binding."

**No new ADR.** See §*Why no new ADR*.

## Problem

Verified against the working tree at `55e44593`; line numbers re-checked, not copied from the issues.

`IdempotencyScope` (`src/ServiceDefaults/Idempotency/IdempotencyScope.cs:29`) is
`(IdempotencyKey Key, string Endpoint, string Caller)`. The table's primary key is
`(key, endpoint, caller)` (`IdempotencyKeyTable.cs:50`), and `IdempotencyStore.BeginAsync`
(`IdempotencyStore.cs:59, 90-105`) replays whatever identifier a row with that key carries.
Nothing records **which request** the key was first used for.

**The issues say ten call sites. There are twelve.** `grep IdempotencyScope.For src` at HEAD:

| # | Endpoint | File (call site) | `Endpoint` argument | What binds the resource | Fab source |
|---|---|---|---|---|---|
| 1 | `POST /rules` | `Automation/Api/RulesEndpoints.cs:331` | constant | body (`CreateRuleRequest`) | `?fabId`, resolved |
| 2 | `POST /cameras` | `CameraCatalog/Api/CameraEndpoints.cs:184` | constant | body (`RegisterCameraRequest`) | `?fabId`, resolved |
| 3 | `POST /events/manual` | `EventIngestion/Api/EventsEndpoints.Writes.cs:100` | constant | body (`IngestManualEventRequest`) | `?fabId`, resolved |
| 4 | `POST /event-sources` | `EventIngestion/Api/EventSourcesEndpoints.cs:127` | **`$"{DeclareEndpoint} {fab}/{source}"`** | body (`DeclareSourceModeRequest`) | `?fabId`, resolved |
| 5 | `POST /event-types` | `EventIngestion/Api/EventTypesEndpoints.cs:122` | constant | body (`RegisterEventTypeRequest`) | `?fabId`, resolved |
| 6 | `POST /devices/register` | `Identity/Api/DevicesEndpoints.cs:140` | constant | body (`RegisterDeviceRequest`) | `?fabId`, parsed + guarded |
| 7 | `POST /kiosks/enroll` | `Identity/Api/KiosksEndpoints.cs:142` | constant | body (`EnrollKioskRequest`) | `?fabId`, parsed + guarded |
| 8 | `POST /webhook-integrations/{name}/rotate` | `Identity/Api/WebhookRotationEndpoints.cs:160` | constant (route **template**) | route `{name}` + body + upsert precondition | body `FabId`, parsed + guarded |
| 9 | `POST /layouts` | `LayoutComposition/Api/LayoutEndpoints.Commands.cs:167` | constant | body (`CreateLayoutRequest`) | `?fabId`, resolved |
| 10 | `POST /walls` | `LayoutComposition/Api/WallEndpoints.Commands.cs:66` | constant | body (`CreateWallRequest`) | `?fabId`, resolved |
| 11 | `POST /overlays` | `OverlayDesigner/Api/OverlayEndpoints.Commands.cs:155` | constant | body (`CreateOverlayRequest`) | **none** — OverlayDesigner has no fab |
| 12 | `POST /system-variables` | `SystemVariables/Api/SystemVariableEndpoints.cs:208` | constant | body (`DefineVariableRequest`) | `?fabId`, resolved |

Rows 4 and 10 post-date the issues. Row 4 is a **local patch for this exact defect**
(`d57f2f0c`, phase-6 review of #2324): it folds fab and source into the endpoint string. It
closes the fab/source dimension for one endpoint only, can overflow `endpoint VARCHAR(128)`
with a long source name, and does not cover the rest of the body. This spec replaces it with
the general mechanism and returns row 4 to the constant.

**The bound resource is the body at eleven of twelve sites** — only row 8 has a route value.
A route-value-only fix would close #2424's headline case and leave e.g. `POST /cameras` (same
key, same fab, different camera name → camera A's identifier, camera B never created) open.
So "the bound resource" is implemented as a **request fingerprint** over everything that
selects the operation's target and content.

### Concrete failures (one per dimension)

1. **Resource (#2424, row 8).** Caller with `sse.webhooks.write` on `munich`:
   `POST /webhook-integrations/alpha/rotate {"fabId":"munich"}`, `Idempotency-Key: K` → rotates
   alpha. Then `POST /webhook-integrations/beta/rotate {"fabId":"munich"}`, key `K` → the fab
   guard passes, the scope matches, the replay reads **alpha's live secret** back from Keycloak
   and returns it under `name: "beta"` (`WebhookRotationEndpoints.cs` replay lambda builds the
   DTO with *this* request's `name`). beta is never rotated.
2. **Fab (#2492, row 2).** `op-multi` (member of `munich` and `dresden`):
   `POST /cameras?fabId=munich` body X, key `K` → 201 camera M. Then
   `POST /cameras?fabId=dresden` body X, key `K` → **201 carrying M's identifier**; no camera in
   dresden.

Same root cause reaches rows 6/7 (second device/kiosk gets the first one's live secret), row 5
and 12 (the `Location` header names the *new* kind/variable, which was never created — the
replay builds the location from this request's name), and every other create.

### A test pins the defect as intended behaviour

`tests/Integration.Tests/EventIngestion/EventTypeRegistryIdempotencyIntegrationTests.cs:86`,
`A_key_reused_for_a_different_kind_replays_the_first_registration`, asserts the replay and
calls it "the documented behaviour". It is **inverted** by this spec, not deleted (FR-009).
That is the decided behaviour change, not a weakened gate (ADR-0144): the human decision on
#2424 is the authority, and the inverted test asserts something strictly stronger.

## Locked technical choices

- **Widen `IdempotencyScope`** to `(Key, Endpoint, Caller, Fab, Fingerprint)`. The primary key
  stays `(key, endpoint, caller)`: a key names **one request**. Fab and fingerprint are stored
  on the row and **compared** on every non-claiming `BeginAsync`.
- **Mismatch → `422 Unprocessable Content`, `title: IDEMPOTENCY_KEY_REUSED`**, rendered by
  `IdempotentRequest` with `Results.Problem` exactly as `IDEMPOTENT_REQUEST_IN_PROGRESS` is
  today. The code constant lives beside the other two in `IdempotencyHeaders`.
- **Fab is the *resolved* fab** (`FabIdentifier.Value` after `ResolveWriteFabAsync` / guard),
  never the raw query string, so a single-fab caller who omits `fabId` once and names it on
  the retry still replays.
- **Fingerprint = SHA-256 (lowercase hex, 64 chars) of a canonical JSON serialisation of the
  bound request DTO plus the route values / operation-selecting headers**, computed by one
  helper in `ServiceDefaults`. The query string is never fingerprinted (fab has its own column).
- **Legacy rows fail closed.** A row written before the migration has `request_fingerprint IS
  NULL`; it cannot prove which request it answers, so any non-claiming match against it is a
  mismatch (422). A stale *unfinished* legacy row is still reclaimed by the existing reclaim.
- **One migration per context, seven contexts**, each calling a new
  `IdempotencyKeyTable.AddRequestBinding` / `DropRequestBinding` pair. `IdempotencyKeyTable.Create`
  is **not edited** — seven historical migrations call it, and widening it would make the
  `ALTER TABLE` fail on a fresh database.
- **No change to:** the key's validation, the opt-in rule (no key → no change), reclaim,
  sweep, the `reserved_at` fence (#2491), `CompleteAsync`/`ReleaseAsync` SQL, or
  `POST /webhook-integrations` (still unkeyed — CLAUDE.md records why).

### Why 422 and not 409

- **409 already means two things on these routes**: `IDEMPOTENT_REQUEST_IN_PROGRESS` ("wait
  and retry the same request") and the endpoints' own genuine-duplicate conflicts. A client
  must be able to tell "retry later" from "never retry this; your key is wrong" by status
  alone, without parsing a title.
- **The IETF draft this mechanism follows prescribes exactly this split**
  (`draft-ietf-httpapi-idempotency-key-header`, §2.7 *Error Scenarios*): `409` for a request
  whose key is still being processed, `422` for a key reused with a different payload. The
  repo already matches the first half; this matches the second.
- **It is a client error about the request, not about resource state** — the request is well
  formed (not 400), the caller is authorised (not 403), and no resource conflicts (not 409).
- ADR-0047/0089: a named `UPPER_SNAKE` code plus an explicit status, the same shape every
  idempotency refusal already uses. First `422` in `src/` — recorded so a reviewer does not
  read its novelty as an accident.

The resilience handler does not retry 4xx (ADR-0143), so a 422 is final for every in-house client.

## Why no new ADR

ADR-0142 §Decision.3 already states the row holds "the key, the endpoint, **the fab** and the
created resource's identifier"; the fab column is conformance. ADR-0142's Decision — "the server
guarantees the operation is applied once and that the same key returns the same answer" — and
its amendment ("the caller receives *its own* secret") both presuppose one key ↔ one request;
returning alpha's secret to beta's request violates the amendment as written. Refusing a
mismatched reuse enforces the ADR's premise inside its existing state machine (it adds a fourth
answer row to the ADR's own table, as #2290 added reclaim without an ADR).

**Caveat stated, not buried:** read literally, "same key returns the same answer" is compatible
with the old behaviour — which is how spec 143 came to pin it. The human decisions on #2424/#2492
resolve that reading; this spec is the record. A one-line dated clarification on ADR-0142 is a
reasonable human follow-up (the autonomous lane may not edit ADRs), not a blocker.

## Latency-budget impact

**Event → overlay state leg (≤ 200 ms): negligible, keyed `POST /events/manual` only.** Every
other route is off the event-to-overlay path. The added work is one SHA-256 over a request body
of at most a few KB and two extra columns in one `INSERT`/`SELECT` — and only when the caller
sends `Idempotency-Key`. Unkeyed requests execute no new code (`IdempotentRequest.ExecuteAsync`
returns before the scope is read; the fingerprint is computed inside `key.Map`). No measurement
obligation (constitution §VII binds per leg; this adds no leg and no measurable cost).

## User stories

### US1 (P1) — A key reused for a different request is refused, never replayed

An operator (or a script with a fixed key) who reuses an `Idempotency-Key` for a request that
targets a different resource, a different fab, or carries a different body gets
`422 IDEMPOTENCY_KEY_REUSED`, and nothing is created, rotated or disclosed. A genuine retry —
the same request — still replays exactly as today.

Both issues are this one story: the two dimensions share the column change, the store change,
the error, and all twelve call-site edits. Splitting them would mean two migrations per context.

## Acceptance scenarios

```gherkin
Feature: An Idempotency-Key is bound to the request it was first used for

  Background:
    Given the caller is authenticated and authorised for the endpoint and fab named

  # --- happy path (unchanged) ---
  Scenario: A genuine retry still replays
    Given "POST /cameras?fabId=munich" with body X and key K answered 201 with identifier M
    When the caller sends "POST /cameras?fabId=munich" with body X and key K again
    Then the answer is 201 with identifier M
    And exactly one camera named X exists in munich

  Scenario: The resolved fab, not the query string, is what is compared
    Given a single-fab dresden operator registered event type T with key K and no fabId
    When the operator repeats it with fabId=dresden, the same body and key K
    Then the answer is 201 with the original identifier

  # --- conflict: resource dimension (#2424) ---
  Scenario: Same key against a different webhook integration
    Given "POST /webhook-integrations/alpha/rotate" {"fabId":"munich"} with key K answered 200
    When the caller sends "POST /webhook-integrations/beta/rotate" {"fabId":"munich"} with key K
    Then the answer is 422 with title IDEMPOTENCY_KEY_REUSED
    And the body carries no client secret
    And beta's client was neither created nor rotated

  Scenario: Same key, same endpoint, different body
    Given "POST /devices/register?fabId=munich" for device D1 with key K answered 201
    When the caller registers device D2 in munich with key K
    Then the answer is 422 IDEMPOTENCY_KEY_REUSED and no secret is returned
    And D2 is not registered

  # --- conflict: fab dimension (#2492) ---
  Scenario: Same key across an operator's two fabs
    Given op-multi sent "POST /cameras?fabId=munich" body X with key K and got 201
    When op-multi sends "POST /cameras?fabId=dresden" body X with key K
    Then the answer is 422 IDEMPOTENCY_KEY_REUSED
    And no camera named X exists in dresden

  # --- conflict: in-flight ---
  Scenario: A mismatched key is refused immediately, not waited on
    Given an attempt with key K for request A is still running
    When the same caller sends request B with key K
    Then the answer is 422 IDEMPOTENCY_KEY_REUSED without the in-progress poll

  # --- bad request (unchanged) ---
  Scenario: A malformed key is still 400
    When the caller sends a keyed create whose Idempotency-Key contains a space
    Then the answer is 400 IDEMPOTENCY_KEY_MALFORMED

  # --- auth (unchanged; ordering) ---
  Scenario: The fab guard still runs before any replay or refusal
    Given the caller has no access to fab hamburg
    When the caller sends "POST /cameras?fabId=hamburg" with a key it used before in munich
    Then the answer is 403, not 422 and not a replay

  Scenario: Another caller's key is still not visible
    Given operator A completed a create with key K
    When operator B sends a different request with key K
    Then B's request executes as a first arrival (201), not 422 — the key is per caller

  # --- legacy ---
  Scenario: A row written before the migration cannot be replayed
    Given a completed idempotency_key row whose request_fingerprint is NULL
    When the same caller presents that key on that endpoint
    Then the answer is 422 IDEMPOTENCY_KEY_REUSED
```

## Functional requirements

- **FR-001** `IdempotencyScope` carries `Fab` (`Option<string>`) and `Fingerprint`
  (`IdempotencyFingerprint`); `IdempotencyScope.For` requires both.
- **FR-002** `idempotency_key` gains `fab VARCHAR(64) NULL` and `request_fingerprint CHAR(64) NULL`
  in all seven contexts' schemas. Primary key unchanged.
- **FR-003** A first arrival and a stale reclaim write the caller's fab and fingerprint onto the row.
- **FR-004** Any non-claiming `BeginAsync` whose stored fab or fingerprint differs from the
  scope's — including a NULL stored fingerprint — reports `IdempotencyOutcome.Mismatched`,
  whether the row is completed or still in progress.
- **FR-005** `IdempotentRequest.ExecuteAsync` answers a `Mismatched` claim with
  `422`, `title: IDEMPOTENCY_KEY_REUSED`, a detail that names no other request's resource,
  without running `work` or `replay` and without polling.
- **FR-006** All twelve call sites pass the resolved fab (`None` for overlays) and a fingerprint
  over the bound DTO plus route values; row 8 also includes `{name}` and the upsert precondition.
  Row 4 returns to the constant `DeclareEndpoint`.
- **FR-007** All twelve mapping chains declare `.ProducesProblem(StatusCodes.Status422UnprocessableEntity)`;
  `StatusProducerDeclarationTests`' census gains the mechanism, guarded.
- **FR-008** Unkeyed requests are byte-for-byte unchanged in behaviour.
- **FR-009** `A_key_reused_for_a_different_kind_replays_the_first_registration` is inverted to
  assert 422 and no second registration.

## Independent end-to-end test procedure

1. Boot the AppHost; confirm all seven `MigrationRunner` targets applied
   `*_AddIdempotencyRequestBinding` (`\d idempotency_key` shows `fab`, `request_fingerprint` in each).
2. With an admin token: `POST /webhook-integrations/alpha-<n>/rotate` (`If-None-Match: *`,
   body `{"fabId":"munich"}`, `Idempotency-Key: K`) → 200 with a secret.
3. Same call against `beta-<n>`, same key → **422 IDEMPOTENCY_KEY_REUSED**, no `clientSecret`
   in the body; `GET /webhook-integrations` (or Keycloak admin) shows no `beta-<n>` client.
4. Repeat step 2 verbatim → 200, same `registeredClientIdentifier` (replay intact).
5. As `op-multi`: `POST /cameras?fabId=munich` body X key K2 → 201; `?fabId=dresden` same body
   and key → **422**; `GET /cameras?fabId=dresden` has no X.
6. Query one context's `idempotency_key` row for K2: `fab = 'munich'`, a 64-char fingerprint.

## Out of scope

- Changing what the key *is* (charset, length, per-caller scoping) or the opt-in rule.
- Keying `POST /webhook-integrations` (cannot replay a hash-only token — CLAUDE.md).
- Storing responses (ADR-0142 rejected it).
- Reclaim/sweep/fence semantics (spec 201, #2491).
- Editing ADR-0142, CLAUDE.md's idempotency house rule, or `.claude/agents/security-reviewer.md`
  to mention fab + fingerprint — **human follow-up**, listed in `tasks.md` §Follow-ups.

## File contention

Open PRs at planning time: #2722 (`fix/2205-revoked-webhook-audit-line`, EventIngestion webhook
delivery) — no shared file. Any concurrent issue touching `src/ServiceDefaults/Idempotency/`,
the twelve endpoint files above, `StatusProducerDeclarationTests.cs`, or a context's
`Migrations/` folder (model-snapshot/migration-id ordering) must serialise with this one.
