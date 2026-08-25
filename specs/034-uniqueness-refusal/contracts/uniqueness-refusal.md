# Contract: The uniqueness refusal

**Feature**: `034-uniqueness-refusal` · 2026-08-25

One response shape, for every mutating endpoint in every context. No endpoint's
own contract changes — this replaces a fault that was never part of any of them.

---

## The refusal

```
409 Conflict
Content-Type: application/problem+json
```

```json
{
  "title": "RESOURCE_ALREADY_EXISTS",
  "detail": "Something with that name or key already exists. Choose a different one — retrying this request unchanged will be refused again.",
  "status": 409
}
```

`title` carries the code, matching `ConcurrencyConflictExceptionHandler` and
every other `ApiError` on the wire — it is what `problemCode()` reads on the
client (ADR-0089, ADR-0119).

### Why the wording says what it says

| Clause | Because |
|---|---|
| *"name or key"* | Not every unique index guards an operator-chosen name. Some guarantee a structural rule, and *"choose a different name"* would be false there |
| *"Choose a different one"* | The caller's action. The generic answer can still say the one useful thing |
| *"retrying this request unchanged will be refused again"* | Distinguishes it from the refusal it most resembles. A stale version is fixed by re-reading and reapplying; this is not, and saying so stops a retry loop |

### Why it says no more

The handler knows a constraint was violated. It does **not** know which domain
concept collided, and acquiring that knowledge means teaching shared code the
vocabulary of nine contexts (spec Assumptions).

It is also unnecessary on the common path: every context with a user-facing
uniqueness rule **already** answers specifically — `CAMERA_NAME_TAKEN`,
`RULE_NAME_TAKEN`, `VARIABLE_NAME_TAKEN`, `LAYOUT_NAME_TAKEN`,
`OVERLAY_NAME_TAKEN`, `WEBHOOK_CLIENT_ALREADY_EXISTS`. This response is what a
caller sees only when one of those checked, was told the name was free, and lost
the race before its write landed.

---

## What must never appear in it

Structurally, by never reading the fields — not by stripping them (FR-007):

- The constraint or index name (`ux_cameras_fab_name_normalized_active`).
- The table or column name.
- Postgres's own `MessageText`, `Detail` or `Hint`, all of which quote the
  colliding values.

That last one matters beyond tidiness: Postgres's detail names the **values**
that collided. In a multi-fab deployment those can belong to a fab the caller
cannot see, which would turn this refusal into the enumeration oracle several
contexts are built to prevent (FR-008).

---

## What triggers it

| | |
|---|---|
| Condition | Postgres SQLSTATE **`23505`** (`unique_violation`) |
| Reached as | `PostgresException`, or `DbUpdateException` wrapping one — EF wraps every provider exception, so the bare form alone never fires |
| Scope | Request-driven writes. Message-driven writes have no caller and fail into the message pipeline |

**Matched on the SQLSTATE, never on the exception type.** Two consequences,
both load-bearing:

- **`DbUpdateConcurrencyException` cannot match.** It derives from
  `DbUpdateException` and carries no Postgres error, so a type-based match would
  swallow every lost update and report it as a collision.
- **A missing table cannot match.** `DirectWriteHonestyIntegrationTests` drops
  `events_<fab>` and requires `>= 500`. Under a type-based match that would
  become *"choose a different name"* for a table that does not exist.

---

## How it differs from the refusal it resembles

Both are `409`. That is the convention, not an oversight — ADR-0119 makes the
**code** what a caller keys on, precisely so statuses can be reused.

| | `AGGREGATE_VERSION_STALE` | `RESOURCE_ALREADY_EXISTS` |
|---|---|---|
| What happened | someone changed **your** resource | someone else holds the name |
| Your version | out of date | **fine** |
| Re-read and reapply? | **yes** | no — it would show you what you already had |
| Retry unchanged? | after re-reading | **never**, until the holder releases it |

Told the wrong one, a caller re-reads forever against a name that is not theirs.

**`RESOURCE_ALREADY_EXISTS` must not end `_STALE`, and must not contain
`CONCURRENCY_CONFLICT`** or the other five phrases `StaleCodeConventionTests`
treats as lost-update vocabulary. The near miss is worth naming: this failure
*is* caused by concurrency. The convention reserves that word for lost updates,
and naming a failure for its cause rather than its remedy is what ADR-0119
exists to prevent.

---

## What does not change

- **No endpoint's declared responses.** `409` was already declared wherever a
  uniqueness check exists; this makes an undeclared `500` into an already-declared
  `409`.
- **No application-level check.** Every one stays (FR-009). This is the backstop
  for a race, and a context that dropped its check would answer this generic
  response for *every* duplicate instead of the rare raced one.
- **No index, constraint or invariant.** The database already refused these
  writes correctly. Only the sentence changes.
