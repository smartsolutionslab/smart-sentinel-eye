# ADR-0176: The #1714 latency bracket is partial, and §IV records it as such

**Status:** Accepted
**Date:** 2026-10-12
**Amends:** Constitution §IV (The Latency Budget Is Sacred)
**Relates to:** ADR-015, ADR-0117, ADR-0118, ADR-0129, #1714, #1940

## Context

#1714 asked whether the 800ms event-arrival-to-overlay-rendered SLO has
ever been watched end to end. It has not — but spec 108's phase-6 work
produced a genuine, repeatable measurement of two of the six §IV legs
together: **event → overlay state** and **overlay composite + render**,
which sum to 250ms of the 800ms budget.

Recording this into §IV was blocked on #1940: whether "readable in the
Aspire dev sink" discharges §VII's dashboard obligation at all, since
without that answer it was unclear whether a measurement read from the
dev sink could even count as the kind of figure §IV's Measured column
expects. ADR-0117's addendum (2026-10-12) resolves #1940 in favour of
ADR-0118's reading: readable in the dev sink does discharge it, given no
production deployment exists yet. That clears the way to record this
bracket.

**The bracket is not the SLO.** The remaining four legs — camera → SFU,
SFU → kiosk decode, presentation buffer, and the arithmetic headroom —
are not measured together with this pair, and per ADR-0129, the overlay
is no longer frame-synced to the picture at all, so the two halves of
the budget are not strictly serial in the way the original 800ms SLO
implied. Writing the bracket into §IV as though it closed the SLO would
be exactly the kind of discharge-nobody-earned this table has had to
correct before (a leg recorded measured before anyone read its figure).

## Decision

**Record the bracket on the "Event → overlay state" row, with its scope
stated inline, not as a claim that the 800ms path has been watched end
to end.** The row's Measured column changes from "recorded, not yet
readable" to the actual bracket (82–234ms dev / 57.6–75ms CI), with an
explicit note that this covers only this leg plus "overlay composite +
render" together — 250ms of the 800ms budget — and that the other four
legs remain unmeasured as a single, continuous path.

**#1714 itself stays open.** This ADR discharges the recording half of
what #1714 asked for; it does not close the issue, because nobody has
yet watched a wall end to end and inter-display synchronisation remains
unbuilt (ADR-0128) — neither of which this bracket touches.

## Consequences

**Positive:** §IV stops under-describing what spec 108 actually proved,
without over-claiming what it didn't.

**Negative:** the table now carries a row whose Measured column is a
bracket with an explicit partial-scope caveat rather than a clean
yes/no — slightly more to read, in exchange for not being wrong.

## Alternatives Considered

**Wait for a fuller measurement before touching §IV.** Rejected for now:
spec 108's bracket is real and repeatable, and holding it unrecorded
until every leg can be measured together has no fixed endpoint — the
presentation buffer alone took until spec 045 to build. Recording the
partial bracket now, scoped honestly, costs less than leaving the table
silent about a real measurement that exists.

**Mark the whole row "yes" without the partial-scope caveat.** Rejected:
this is precisely the over-claim §IV's own prose already warns against.
