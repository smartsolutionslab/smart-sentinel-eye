# Contract: System variables API after fab scoping

**Feature**: `014-system-variable-fab-scoping` | **Date**: 2026-08-05

Five endpoints. All gain a fab; none changes shape otherwise. The context has
no fab check at all today, so every row below is new behaviour.

## Fab resolution (applies to every endpoint)

Identical to the rules API (ADR-0114, as amended by this feature). Resolved
immediately after model binding and **before** any precondition is read.

| Caller assigned to | `fabId` supplied | Outcome |
|---|---|---|
| exactly one fab | omitted | inferred from the caller (FR-010) |
| exactly one fab | that fab | accepted |
| several fabs | omitted | **400** — a fab must be chosen (FR-011) |
| several fabs | one of theirs | accepted |
| any | a fab they lack | **403** `RESOURCE_FAB_NOT_AUTHORIZED` (FR-012) |
| no fabs | anything | **403** (FR-013) |

Reuses `FabResolution` and `FabClaims` from `ServiceDefaults` unchanged. This
feature adds no resolution mechanism; it applies the existing one.

## Endpoints

### `POST /system-variables` — define

- Optional `?fabId=` per the table.
- Created in the resolved fab.
- **409** `VARIABLE_NAME_TAKEN` now means *taken in this fab*; the same name in
  another fab is accepted (FR-002).

### `GET /system-variables` — list

- Returns only variables in fabs the caller holds (FR-008).
- With `?fabId=`, narrowed to that one after the guard.
- Without it and the caller holds several: spans **all** of theirs. A read does
  not have to choose — the same deliberate asymmetry with `POST` that the rules
  API has.

### `GET /system-variables/{name}` — read one

- Resolved within the caller's fabs.
- Another fab's variable → **404**, indistinguishable from a name that does not
  exist (FR-009).
- Held in two of the caller's own fabs and no `?fabId=` → **400**
  `VARIABLE_FAB_AMBIGUOUS`, naming the candidates. They are all fabs the caller
  holds, so naming them leaks nothing.

### `POST /system-variables/{name}/archive` — archive

- Resolves fab, then looks the variable up **within that fab**.
- Unknown name *or* another fab's → **404**, identical either way.
- Existing `If-Match` behaviour unchanged, evaluated after the fab check.

### `GET /system-variables/snapshot` — overlay snapshot

- Already requires `overlayIdentifier`; resolution is scoped to **the caller's**
  fab (FR-014, as amended by
  [ADR-0115](../../../docs/adr/0115-overlays-are-fab-neutral-templates.md)).
- A referenced variable absent from the caller's fab renders the literal
  placeholder, exactly as for a name that exists nowhere.
- A multi-fab caller is **not** refused here as they are on `GET /{name}`: a
  snapshot returns rendered text rather than a row to act on, so resolving each
  placeholder within the caller's fabs is well-defined even when several are
  held. Where one name exists in two of them the first by fab name wins, which
  is arbitrary but stable — a kiosk, the only real caller, holds exactly one.

## Response shapes

| Status | Title | When |
|---|---|---|
| 400 | `VARIABLE_FAB_REQUIRED` | multi-fab caller omitted `fabId` on a write |
| 400 | `VARIABLE_FAB_AMBIGUOUS` | name resolves in more than one of the caller's fabs |
| 403 | `RESOURCE_FAB_NOT_AUTHORIZED` | fab named that the caller lacks, or caller holds none |
| 404 | `VARIABLE_NOT_FOUND` | unknown name, **or** a variable in a fab the caller lacks |
| 409 | `VARIABLE_NAME_TAKEN` | name already used **in that fab** |

The 404-for-another-fab choice is the reason FR-009 exists: a 403 would confirm
the variable is there, letting an operator enumerate another fab's variable
names one guess at a time.

Every endpoint that gains a 400 or 403 path must declare it, or the generated
OpenAPI claims a status that can happen cannot. Spec 013 shipped this wrong on
one endpoint and it took a review to catch.

## Event-driven path (no HTTP)

`SystemVariableValueRequestedV1` is unchanged on the wire — it already carries
`Metadata.Fab`. The consumer resolves `(fab, name)`:

- no fab on the message → nothing changes, recorded (FR-006);
- fab present, no such variable in it → nothing changes, recorded with the fab
  **and the variable name** (FR-005).

The second is the case a rule in one fab pointing at another fab's variable
produces. It must not share a log message with "malformed input" — #1252 hid
for a release behind exactly that kind of shared silence, and spec 013's remedy
was a distinct message naming the offending value.
