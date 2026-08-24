# Contract: the refusal vocabulary

**Feature**: `031-stale-version-convention` · 2026-08-24

A convention rather than an endpoint. What changes on the wire is one string;
what changes for a client is which part of a refusal it may key on.

---

## The rule

**A refusal because the caller's version is no longer current carries a code
ending `_STALE`.**

Nothing about the status. A context may answer `409` or `412` — both are
defensible readings of HTTP, and after this feature neither affects what an
operator is told.

## The one wire change

| | Before | After |
|---|---|---|
| Code | `CAMERA_VERSION_MISMATCH` | **`CAMERA_VERSION_STALE`** |
| Status | `412 Precondition Failed` | **unchanged** |
| Message | unchanged | unchanged |

**No other code moves.** The sixteen existing `*_STALE` declaration sites across
six contexts are untouched — that is the whole cost argument for changing the
outlier rather than the majority.

### Is this a breaking change?

For an API consumer keying on `CAMERA_VERSION_MISMATCH`, yes. There are none:
the endpoint shipped in spec 029 and its only client is the management app,
which spec 030 is still building. Renaming it now is the cheapest this will ever
be — the code has never had a consumer outside this repository.

The status is unchanged, so anything keying on `412` is unaffected.

## A correction, not only a rename

`specs/029-camera-read-edit/contracts/cameras-api.md` documents this refusal as:

| Status | Title |
|---|---|
| 412 | `PRECONDITION_FAILED` |

**That code does not exist.** There is no `PRECONDITION_FAILED` anywhere in
`src/`; the implementation has always answered `CAMERA_VERSION_MISMATCH`. A
client written against that contract would have keyed on a string that never
arrives.

Nothing broke because nothing reads the contract mechanically — which is exactly
how it drifted, and a second instance of the problem this feature exists to fix:
a written convention and a running one disagreeing. Corrected to the value the
implementation actually returns.

## What a client may rely on

| Question | May key on | May **not** key on |
|---|---|---|
| Was this a lost update? | the code ending `_STALE` | the status — both are overloaded |
| Is this thing terminal? | the specific code | the status — `CAMERA_RETIRED` is a 409, same as a lost update |
| Anything else | the server's message, surfaced as-is | — |

## Enforced, not just written

A source-scanning architecture test fails the build if a context names a version
conflict any other way — `*_VERSION_MISMATCH`, `*_VERSION_CONFLICT`, and so on.

This is the difference between a convention and a note. Six contexts followed it
by imitation and the seventh did not, which is what produced this feature; a
test is what stops the eighth.

The check reads source rather than reflecting over assemblies because `ApiError`
takes its code as a constructor argument, so the value exists only on an
instance. `HandlerDeconstructionTests` already reads source for a comparable
reason.

## Not in this contract

- **The concurrency mechanism.** Versions, `If-Match`, no retry on conflict —
  ADR-0113's, unchanged and working.
- **Standardising the statuses.** Deliberately refused: 412 is the more correct
  reading, and making the status irrelevant is cheaper than making six contexts
  agree on one.
- **A convention for terminal-state refusals.** One such code exists. A suffix
  for a population of one would be speculative generality.
