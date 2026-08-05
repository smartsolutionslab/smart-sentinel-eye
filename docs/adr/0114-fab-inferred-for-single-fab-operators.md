# ADR-0114: A Rule's Fab Is Inferred for Single-Fab Operators

**Status:** **Accepted** (amended 2026-08-05 — scope extended to the
SystemVariables endpoints, see below)
**Date:** 2026-08-03
**Supersedes:** —
**Superseded by:** —

## Context

Spec 013 gives Automation rules a fab. Every rule endpoint therefore needs
to know which fab a request concerns, so the fab guard has something to
check and reads can be narrowed.

The obvious answer is the one every other fab-scoped endpoint already uses:
take `fabId` on the request and guard it. `IFabAuthorizationGuard` states
this outright in its own documentation:

> Multi-fab users (e.g. a regional admin assigned to both `/fabs/munich` and
> `/fabs/berlin`) are supported: the guard only checks that the requested
> `fabId` is present in the caller's group list, so each per-fab API call
> passes independently. **There is no implicit "current fab" — the caller
> picks per request.**

That position was never recorded in an ADR. A search of `docs/adr/` at the
time of writing finds no decision record asserting it; it exists only as
that XML comment, added alongside the guard in spec 008.

Meanwhile the deployment reality is one live fab. Requiring `fabId` on every
rule authored by an operator who belongs to exactly one fab is a parameter
whose value is never in doubt, on the most frequently used write in the
context.

## Decision

**When an operator is assigned to exactly one fab and does not name one, the
rule's fab is inferred from their group membership.**

Concretely, on the rule endpoints:

| Caller assigned to | `fabId` supplied | Outcome |
|---|---|---|
| exactly one fab | omitted | **inferred** |
| exactly one fab | that fab | accepted |
| several fabs | omitted | `400 RULE_FAB_REQUIRED` |
| several fabs | one of theirs | accepted |
| any | a fab they lack | `403 RESOURCE_FAB_NOT_AUTHORIZED` |
| no fabs | anything | `403` |

Inference never widens access. It applies only after the caller's group
membership has been read from a validated token, and it can only ever select
a fab the caller is already entitled to. A multi-fab caller is refused rather
than guessed at.

This ADR **narrows** the "no implicit current fab" position to the endpoints
that still hold it, and the guard's comment is corrected to point here rather
than continue asserting something the Automation endpoints contradict.

## Consequences

**Easier.** A single-fab operator — every operator in the current deployment
— authors a rule with no fab parameter. The management UI does not need a fab
picker for the common case, and no existing rule-authoring call site has to
change to keep working.

**Harder.** There are now two ways a rule's fab is established, and a reader
of `RulesEndpoints` must look at the resolution step rather than the request
shape to know which applied. The refusal for multi-fab callers is a behaviour
that only appears once a second fab exists, so it is the case least likely to
be exercised in practice and most likely to regress; spec 013 T035 and T036
exist to pin it.

**Constrained.** Inference is deliberately confined to the endpoints named in
this decision — originally Automation's rule endpoints, extended to
SystemVariables by the 2026-08-05 amendment below. It is not a general
licence, and extending it further is a new decision — not an application of
this one. Every other endpoint continues to require `fabId` explicitly.

**A risk worth naming.** Inference makes it possible to write an endpoint that
*looks* fab-scoped while never consulting the caller's claim, because the
absence of a `fabId` parameter is no longer a signal that something is
missing. The mitigation is that the guard call is what enforces access, not
the parameter — and it is applied to every rule endpoint including the
non-mutating dry-run.

## Alternatives Considered

**Require `fabId` on every request.** The documented position, and what
Identity's device/kiosk registration and webhook rotation already do.
Rejected for the authoring path only, because it puts a mandatory parameter
with exactly one possible value on the most common write in the context. It
remains the rule everywhere else.

**Infer for multi-fab callers too, by picking one.** Rejected outright. Any
selection rule — first alphabetically, first in the claim, most recently used
— silently places a rule in a fab the operator did not choose, and the
failure is invisible until someone notices automation running in the wrong
plant.

**A per-session "current fab" the operator selects once.** Rejected as the
larger change it is: it introduces session state the system does not have,
and it is the concept the guard's original comment was written to avoid. If
multi-fab operation becomes common, this is worth revisiting as its own
decision rather than arriving by accretion.

**Amend an existing ADR instead of writing this one.** There is nothing to
amend — no ADR asserts the position being narrowed. Attaching the deviation
to an unrelated ADR (0007 or 0008, on Keycloak) would bury it where no
reviewer would look.

## Implementation Notes

- The claim-reading helper is `ServiceDefaults.Authorization.FabClaims`,
  promoted from a private copy in `AuditEndpoints` so Automation is not the
  third place claim parsing is written by hand.
- Enumeration deliberately does **not** live on `IFabAuthorizationGuard`.
  That interface answers one question — may this caller touch fab X — and
  widening it would grow every implementation and test double with a method
  most callers never use.
- The fab check runs **before** the `If-Match` precondition (ADR-0113), so a
  caller cannot distinguish "not yours" from "does not exist" by the
  difference between a 403 and a 409.

## Amendment (2026-08-05): scope extended to SystemVariables

The decision is unchanged — inferred for a single-fab operator, refused for a
multi-fab one who names none, 403 for a fab they do not hold. Only its scope
widens: the SystemVariables endpoints resolve a fab the same way, using the
same `FabResolution` and `FabClaims` helpers unchanged.

**Why an amendment rather than a new ADR.** The original reasoning was not
wrong, so superseding it would misrepresent what happened. What the original
did was refuse to grant a licence it had no evidence for — "extending it
elsewhere is a new decision" — and this is that decision, made when a second
context needed it. Recording it here keeps the constraint and its one
extension in the same place, which is where a reviewer checking whether a
third context may infer will look.

**What made it necessary.** Spec 014 fab-scopes system variables (#1310). Its
endpoints need a fab for exactly the reasons the rule endpoints did, and an
operator who is never asked which fab a rule belongs to but is asked for a
variable would be meeting two rules for one concept.

**What has not changed.** The risk named above — that an endpoint can look
fab-scoped while never consulting the caller's claim — applies equally here,
and the mitigation is the same: the guard call enforces access, not the
presence of a parameter. Spec 014 applies it to all five SystemVariables
endpoints including the non-mutating snapshot read.

**Still not a general licence.** A third context wanting inference is a third
decision. The list of endpoints this covers is exhaustive and is stated in the
Consequences above.
