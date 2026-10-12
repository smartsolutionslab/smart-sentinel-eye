# ADR-0177: AppHost resources get explicit health checks, applied incrementally

**Status:** Accepted
**Date:** 2026-10-12
**Amends:** none (implementation-pattern decision, not a constitution amendment)
**Relates to:** #2060, #2061, #2062, ADR-025 (Aspire AppHost), issue 1015

## Context

#2060 reported MediaMTX intermittently going `RuntimeUnhealthy` during a
cold Aspire boot and failing every integration test at once. A deeper
investigation (recorded on the issue) found the actual reported state
is a Docker container-runtime condition, not an application-health
one — a `WithHttpHealthCheck` would not have caught it, since there is
nothing to poll while the container itself cannot start. The two real
occurrences found in CI history had a different cause entirely
(MigrationRunner exiting non-zero, filed separately as #2062), and the
report's own failure to name its cause was filed as #2061.

What's left is a genuine, broader question the issue itself flagged as
needing a human decision: **none of AppHost's 12 resources declare an
explicit health check today.** Readiness is inferred from container-
runtime state (`Running`, `Unhealthy`, etc.), and `.WaitFor(...)` calls
between resources wait on that inferred state rather than on anything
the resource itself claims to mean by "ready." Three ad-hoc probes
already live in `AspireFixture` (`WaitForKeycloakRealmAsync`,
`WaitForMediaMtxAsync`, `WaitForServiceHealthAsync`) — a sign that this
gap has already been worked around once, outside the composition root.

## Decision

**AppHost resources that expose an HTTP endpoint get an explicit
`WithHttpHealthCheck` declaring what "ready" means for that resource,
rather than relying on inferred container-runtime state.**

Applied **incrementally, not as a one-shot migration of all 12
resources**:

- MediaMTX (the first instance) now checks its control-API endpoint.
- Other resources gain a health check as they're touched by future
  work, or as a boot-ordering issue surfaces for them specifically —
  not as a backlog sweep.

**The three ad-hoc fixture probes are not replaced by this.** They test
from outside the process, proving something is reachable over the
network the way a real caller would; `WithHttpHealthCheck` is readiness
as the composition root itself understands it, informing `.WaitFor`
ordering during boot. The two serve different purposes and both stay.

## Consequences

**Positive:** boot-ordering bugs involving a resource with a declared
health check become diagnosable by what the resource itself reports,
rather than by container-runtime state that (per #2060's finding) can
read `Running` even when something upstream has actually failed.

**Negative:** the 11 other resources remain without one until touched;
this ADR does not close that gap in one move, so a boot-ordering issue
on an untouched resource will still be diagnosed by the same inferred-
state shortfall #2060 hit, until that resource's turn comes.

## Alternatives Considered

**Migrate all 12 resources now.** Rejected: most have had no reported
boot-ordering issue, and writing 12 health checks speculatively is the
kind of sweep this repo's own practice argues against — a precedent
set deliberately, applied where the problem actually shows up.

**Do nothing — leave readiness inferred.** Rejected: this is the status
quo #2060 found misleading, and the three ad-hoc fixture probes are
already evidence that inferred readiness isn't sufficient everywhere.
