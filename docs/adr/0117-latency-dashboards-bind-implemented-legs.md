# ADR-0117: Latency-budget dashboards bind implemented legs

**Status:** Accepted
**Date:** 2026-08-21
**Amends:** Constitution §VII (Observability Is Non-Negotiable)
**Relates to:** ADR-015 (latency budget), ADR-026 (observability stack), #1681, #1714

## Context

Constitution §VII says:

> Latency-budget dashboards (per ADR-015) are mandatory. **A leg without a
> dashboard cannot ship.**

Spec 024 (#1681) set out to satisfy that rule and found it cannot be satisfied as
written. ADR-015 names six legs. Three of them **are not implemented**:

| Leg | Budget | State |
|---|---|---|
| SFU → kiosk decode | 120 ms | `apps/kiosk-web` has no `<video>`, no `MediaStream`, no `RTCPeerConnection` |
| Presentation buffer (PTP) | 200 ms | PTP is named in ADR-014 and listed in spec 002 as out of scope, a "future-add" |
| Overlay composite + render | 50 ms | overlays render, but over nothing — no video beneath them |

A dashboard for a leg that does not exist would display an empty panel. Worse, an
empty panel is indistinguishable from a healthy one unless something explicitly
says otherwise — a failure mode this codebase has met three times (a green suite
that never ran, a `401` that printed like an empty list, a board add that printed
like a success).

The rule as written therefore has two readings, and both are bad:

- **Literal** — the streaming legs cannot ship until they can be watched, which
  blocks work on the grounds that it has not been done.
- **Ignored** — the rule is quietly not applied, which is what happened: six
  features shipped past it without discussion.

The second is what actually occurred, and it is the more damaging, because a rule
nobody enforces still reads as enforcement to anyone consulting the document.

## Decision

**§VII's dashboard requirement binds every leg that is implemented.**

Precisely:

1. A leg whose code path exists MUST have a latency measurement and a dashboard
   showing it against its ADR-015 budget before further work ships on that leg.
2. A leg whose code path does **not** exist is not exempt — it is **not yet
   subject**. The requirement attaches when the leg does.
3. Whichever spec implements a previously-unbuilt leg carries its measurement
   and dashboard as part of that work. It does not become a follow-up.
4. The set of legs and their state MUST be recorded where the budget is, so that
   "not yet subject" is a visible claim rather than an absence.

## Consequences

**Makes honest what was already true.** The rule was not being applied to unbuilt
legs; now it says so, rather than being contradicted in silence.

**Moves the obligation to where the work is.** Implementing a leg now includes
making it watchable. That is the cheapest moment to do it: spec 024 found that
retrofitting `event → overlay state` needs a timestamp propagated through
`Shared.Contracts`, which would have cost nothing had it been designed in.

**Does not weaken the rule for anything that exists.** `camera → SFU` and
`event → overlay state` are implemented and remain fully bound. Spec 024 made the
first readable; the second is not yet, which is now a live obligation instead of
an ambiguity.

**Costs a tracking burden.** Someone must keep the record of leg states current,
and a stale record is worse than none — it would report a leg as unbuilt after it
had been built, exempting it from the rule by clerical error. Point 4 keeps the
record beside the budget so the two are read together.

**Does not address #1714.** That three of six legs are unbuilt remains a fact
about the product. This ADR stops the constitution from misdescribing it; it does
not change it.

## Alternatives Considered

**Leave §VII as written and treat unbuilt legs as blocking.** Coherent, and the
strictest reading. Rejected: it would halt the streaming path on the grounds that
the streaming path has not been built, and no one has been applying it that way
for six features, so adopting it now would be a new policy dressed as enforcement
of an old one.

**Leave §VII as written and accept the gap informally.** Rejected outright. It is
the status quo, and the status quo is a constitution that says something the
project does not do. Governance says conflicts are *"resolved by amending one of
the two — never by ignoring it"*.

**Weaken the rule to "dashboards are recommended".** Rejected: the rule earned its
strength. The one implemented leg that lacked measurement hid a twelve-second
breach of a 200 ms budget (#1655) until a test written for another purpose
happened to time it. The problem is the rule's *scope*, not its force.

**Amend ADR-015 to drop the unbuilt legs from the budget.** Rejected: the budget
is a design target for the finished system, and removing legs because they are
unfinished would make the 800 ms SLO arithmetic that no longer describes the
product's intent.
