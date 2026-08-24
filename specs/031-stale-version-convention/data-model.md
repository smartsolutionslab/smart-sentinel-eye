# Data Model: One way to say a version is stale

**Feature**: `031-stale-version-convention` · 2026-08-24

No persisted state, no migration, no aggregate change. The model here is the
**refusal vocabulary** — what the system says when a write is refused, and which
part of that carries the meaning.

---

## A refusal

Every refusal in this project is an `ApiError`:

```csharp
public abstract record ApiError(string Code, string Message, HttpStatusCode Status);
```

Three parts, and this feature is about which one a client may key on.

| Part | Who reads it | Authoritative? |
|---|---|---|
| `Code` | clients, deciding what to tell the operator | **yes, after this feature** |
| `Message` | the operator, when nothing more specific is known | no |
| `Status` | HTTP intermediaries, and clients today | **no** |

### Why the status cannot carry the meaning

Both statuses in play are overloaded, in both directions:

| Status | Means |
|---|---|
| `409 Conflict` | a stale version (16 sites), a name collision, and a terminal-state refusal |
| `412 Precondition Failed` | a stale version (1 site), and an upsert precondition that was wrong about existence (2 sites, Identity) |

Neither identifies a lost update. This is not a defect in either choice — both
are defensible readings of HTTP — it is why the code has to be the discriminator.

---

## The convention

**A refusal because the caller's version is no longer current MUST carry a code
ending `_STALE`.**

That is the whole rule. Not the status, which stays free to be whichever the
context judges correct.

### After this feature

| Context | Code | Status |
|---|---|---|
| Automation | `RULE_STALE` | 409 |
| LayoutComposition | `LAYOUT_REVISION_STALE` | 409 |
| OverlayDesigner | `OVERLAY_REVISION_STALE` | 409 |
| SystemVariables | `VARIABLE_STALE` | 409 |
| EventIngestion | `WEBHOOK_INTEGRATION_STALE` | 409 |
| Identity | `WEBHOOK_CLIENT_STALE` | 409 |
| **CameraCatalog** | **`CAMERA_VERSION_STALE`** *(was `CAMERA_VERSION_MISMATCH`)* | **412**, unchanged |

Seven contexts, one code convention, two statuses — and the two statuses no
longer matter to anything a client decides.

---

## What a client derives from it

| Question | Answered by |
|---|---|
| Was this a lost update? | the code ends `_STALE` |
| Is this thing finished, rather than contested? | a named code — today only `CAMERA_RETIRED` |
| Anything else? | the server's `Message`, surfaced as-is |

**The terminal case keeps a name list, and that is a known limit.** There is one
such code today. Inventing a `*_TERMINAL` suffix for a population of one would
be speculative generality; it is recorded in the plan rather than solved.

---

## Explicitly not modelled

- **No change to the concurrency mechanism.** Versions, `If-Match` and
  no-retry-on-conflict are ADR-0113's and work.
- **No change to the sixteen existing sites.** Their codes and statuses are
  already correct under this convention.
- **No new status.** 412 stays where it is, because it is the more correct
  reading and because making it irrelevant is cheaper than standardising it.
