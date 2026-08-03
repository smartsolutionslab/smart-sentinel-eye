# Contract: Rules API after fab scoping

**Feature**: `013-automation-fab-scoping` | **Date**: 2026-08-03

Six endpoints. All of them gain a fab; none of them changes shape otherwise.

## Fab resolution (applies to every endpoint)

Resolved once, immediately after model binding and **before** the `If-Match`
precondition is read (R6):

| Caller assigned to | `fabId` supplied | Outcome |
|---|---|---|
| exactly one fab | omitted | inferred from the caller (FR-008) |
| exactly one fab | that fab | accepted |
| several fabs | omitted | **400** — a fab must be chosen (FR-009) |
| several fabs | one of theirs | accepted |
| any | a fab they lack | **403** `RESOURCE_FAB_NOT_AUTHORIZED` (FR-010) |
| no fabs | anything | **403** (spec Edge Cases) |

Inference is the deviation ADR-0114 records.

## Endpoints

### `POST /rules` — author

- Optional `?fabId=` per the table above.
- Rule is created in the resolved fab.
- **409** `RULE_NAME_TAKEN` now means *taken in this fab*; the same name in
  another fab is accepted (FR-004).

### `POST /rules/{name}/publish` — publish

- Resolves fab, then looks the rule up **within that fab**.
- Unknown name *or* a name belonging to another fab → **404**, identical
  response either way (FR-007).
- Existing `If-Match` behaviour unchanged, evaluated after the fab check.

### `POST /rules/{name}/archive` — archive

Same as publish.

### `GET /rules` — list

- Returns only rules in fabs the caller is assigned to (FR-005).
- With `?fabId=`, narrowed to that one fab after the guard.
- Without it and the caller has several fabs: returns rules across **all**
  of their fabs — a read is not an authoring action, so nothing has to be
  chosen. This differs deliberately from `POST /rules`.

### `GET /rules/{name}` — read one

- Resolved within the caller's fabs.
- Another fab's rule → **404**, indistinguishable from a name that does not
  exist (FR-007).
- With several fabs and no `?fabId=`, the name is resolved across the
  caller's fabs; ambiguity across two of their own fabs → **400** naming the
  candidates.

### `POST /rules/{name}/dry-run` — trial run

- Fab-scoped like the reads (FR-006), so a trial cannot be used to probe
  another fab's rule behaviour.
- **Still carries no `If-Match`** — it persists nothing and sits in the reads
  group. Spec 012 T048 pinned that with a test; this feature does not change
  it.

## Response shapes

| Status | Title | When |
|---|---|---|
| 400 | `RULE_FAB_REQUIRED` | multi-fab caller omitted `fabId` on a write |
| 400 | `RULE_FAB_AMBIGUOUS` | name resolves in more than one of the caller's fabs |
| 403 | `RESOURCE_FAB_NOT_AUTHORIZED` | fab named that the caller lacks |
| 404 | `RULE_NOT_FOUND` | unknown name, **or** a rule in a fab the caller lacks |
| 409 | `RULE_NAME_TAKEN` | name already used **in that fab** |
| 409 | `RULE_STALE` | unchanged from spec 012 |
| 428 | `IF_MATCH_REQUIRED` | unchanged from spec 012 |

The 404-for-another-fab choice is deliberate and is the reason FR-007 exists:
returning 403 would confirm the rule exists, letting an operator enumerate
another fab's rule names one guess at a time.

## Event-driven path (no HTTP)

`FabEventIngestedV1Handler` passes `message.Fab` into evaluation. An event
carrying no fab triggers nothing (FR-012) rather than falling back to
evaluating everything, which is the current behaviour and the bug.
