# ADR-0119: The Code, Not the Status, Says a Version Is Stale — amends ADR-0113

**Status:** **Accepted** (amends ADR-0113)
**Date:** 2026-08-24
**Supersedes:** —
**Superseded by:** —

## Context

ADR-0113 established two-layer optimistic concurrency: an aggregate carries a
`Version`, a mutating request echoes it in `If-Match`, and a conflict is
refused rather than retried. That mechanism works.

**It says nothing about how the refusal is spelled**, and that omission let two
conventions grow without either contradicting the ADR.

### Two spellings

By 2026-08-24, seven contexts could refuse a write because the caller's version
had moved:

| Contexts | Code | Status | Declaration sites |
|---|---|---|---|
| Automation, LayoutComposition, OverlayDesigner, SystemVariables, EventIngestion, Identity | `*_STALE` | `409 Conflict` | **16** |
| CameraCatalog (spec 029) | `CAMERA_VERSION_MISMATCH` | `412 Precondition Failed` | 1 |

Six followed a convention by imitation. The seventh, written months later, did
not — and nothing detected it, because nothing had ever written the convention
down.

### Both statuses are overloaded

This is what makes it more than untidiness. Neither status identifies a lost
update:

| Status | Also carries |
|---|---|
| `409` | name collisions (`LAYOUT_NAME_TAKEN`, `OVERLAY_NAME_TAKEN`), and a terminal-state refusal (`CAMERA_RETIRED`) |
| `412` | upsert preconditions that were wrong about *existence* (`WEBHOOK_CLIENT_ALREADY_EXISTS`, `WEBHOOK_CLIENT_NOT_FOUND`, Identity) |

### What it cost

`apps/shared/src/api/problemDetail.ts` decides what an operator is told. Its
`isStaleConflict` tested status `409` **and** a `_STALE` code suffix — and its
own comment already stated the right rule:

> *"anything that changes the **advice** has to key on the code rather than the
> status"*

It only half-applied it. So a stale camera refusal fell through to the generic
fallback and told the operator to **try again**, which resubmits unchanged and
replays their edit over the winner's. `LayoutEditorDialog` documents that
exact advice as wrong.

**A system that detects a lost update and then advises the action that causes
one has spent the mechanism and kept the bug.**

### An eighth site, which no context declares

The architecture test written for this ADR failed on its first run, against a
site this analysis had missed:

| Site | Was | Layer |
|---|---|---|
| `src/ServiceDefaults/Persistence/ConcurrencyConflictExceptionHandler.cs` | `AGGREGATE_VERSION_CONFLICT` (`409`) | ADR-0113 **Layer 2** |

This is the *true database race* — two transactions overlapping — converted
from EF Core's `DbUpdateConcurrencyException` by an `IExceptionHandler`
registered in `ServiceDefaults`. It is not declared by any context, and it
applies to **every mutating endpoint in every context**.

Because it did not end `_STALE`, no client recognised it as a lost update
either. So the defect was never confined to CameraCatalog: an operator losing
the rarer, realer race was told to *try again* everywhere in the product.

The convention had two independent violators, one of them global, and the count
of contexts following it correctly was never the right measure. **This is the
argument for the architecture test in one paragraph** — the survey that produced
the table above was done carefully and was still wrong, because it looked where
concurrency errors are *declared* and this one is *handled*.

## Decision

**A refusal because the caller's version is no longer current MUST carry an
error code ending `_STALE`.**

**The HTTP status is not authoritative and MUST NOT be used to identify one.** A
context may answer `409` or `412`; both are defensible readings of HTTP and
neither affects what an operator is told.

Four consequences follow directly:

1. `CAMERA_VERSION_MISMATCH` is renamed **`CAMERA_VERSION_STALE`**. Its status
   stays `412`.
2. The shared client predicate becomes `problemCode(error)?.endsWith('_STALE')`
   — the status test is deleted, not extended.
3. The convention is enforced by an architecture test, so the next context
   cannot miss it the way CameraCatalog did — and it earned that place
   immediately, by finding consequence 4 on its first run.
4. `AGGREGATE_VERSION_CONFLICT` is renamed **`AGGREGATE_VERSION_STALE`**. Its
   status stays `409`. This one is the shared Layer-2 handler, so the rename
   corrects the advice for every mutating endpoint in the product at once.

## Consequences

**Easier.** One rule to follow, and it is checkable. A new context gets the
right operator-facing advice by naming its code correctly, with no change to
shared code. A client can recognise a lost update without knowing which status
a given context chose.

**Harder.** The status now carries less meaning than an HTTP-literate reader
might expect: a `412` and a `409` may mean the same thing. That is deliberate,
and it is why this ADR exists rather than a comment.

**Unchanged.** The sixteen existing declaration sites, ADR-0113's mechanism, and
every operator-facing message outside CameraCatalog.

**A limit accepted, not solved.** Terminal-state refusals — `CAMERA_RETIRED`
today — are still recognised by name rather than by convention. There is exactly
one, and inventing a `*_TERMINAL` suffix for a population of one would be
speculative generality. When there is a second, this ADR should be revisited.

## Alternatives Considered

### Standardise the sixteen onto `412` instead

**This is the more HTTP-correct end state, and it was rejected.**

RFC 9110 §15.5.13 specifies `412 Precondition Failed` for exactly a failed
`If-Match`. By that reading CameraCatalog is right and the other six deviate —
so "make the outlier match the majority" makes the *newest and most correct*
endpoint worse.

It was rejected on cost: changing sixteen declaration sites across six contexts
is a breaking contract change to earn nothing an operator can see. Renaming one
code is a rename, and it is cheapest now — that code has never had a consumer
outside this repository.

**Making the status irrelevant gets the benefit without the breakage.** Both
spellings stay legal; only the code has to conform. A future reader who
rediscovers the correctness argument should know it was seen and priced, not
missed.

### Standardise the one onto `409` to match the majority

Consistent, and it would make the newest endpoint less correct for no gain once
the status no longer decides anything. Rejected.

### Key on both the code and the status

The status quo. It is wrong for a `412` stale refusal and wrong for a `409`
terminal one — and being wrong in both directions is what a two-part test buys
when one of its parts is unreliable.

### Document the convention without enforcing it

Six contexts already followed it by imitation and the seventh did not. A
convention that depends on noticing is the thing that failed here.

## Implementation Notes

The architecture test reads **source files** rather than reflecting over
assemblies, because `ApiError` takes its code as a constructor argument:

```csharp
public abstract record ApiError(string Code, string Message, HttpStatusCode Status);
```

so the value exists only on an instance, and building every error record would
mean supplying arguments for each. `HandlerDeconstructionTests` already reads
source for a comparable reason.

Specified as `031-stale-version-convention`, from issue #1857, which was found
by spec 030's Phase 0 research while working out how the management app should
turn spec 029's refusals into words.
