# ADR-0154: Readiness is liveness, by choice

**Status:** **Accepted**
**Date:** 2026-09-17
**Amends:** Constitution §Availability
**Supersedes:** —
**Superseded by:** —

## Context

Issue #2125 observes that `GET /health` on every service in this system
returns `200` under every condition the code can produce. The check set has
exactly two members and neither can yield `Unhealthy`, so readiness that never
fails is arithmetically identical to no readiness probe at all.

Re-verified at `develop@a4a36ea1`, against the running Aspire stack, the four
outcomes are:

| Path | Mechanism | Result | HTTP |
|---|---|---|---|
| Empty outbox | `OutboxBacklogHealthCheck` returns `Healthy` | `Healthy` | `200` |
| Backlog over threshold | `OutboxBacklogHealthCheck` returns `Degraded` | `Degraded` | `200` |
| Database unreachable | `OutboxBacklogHealthCheck`'s unreachable catch returns `Healthy` | `Healthy` | `200` |
| Check throws (any other cause) | Registration's `failureStatus: Degraded` (`WolverineDefaults.cs:170`) | `Degraded` | `200` |

`HealthCheckResult.Unhealthy` appears nowhere in `src/` outside comments, and
no `ResultStatusCodes` override exists, so `/health` is a `200` under every
condition the code can produce today. `self` (`Extensions.cs:129`) returns
`Healthy()` unconditionally and is the only other registered check.

**This property is not a defect being fixed here. It is a choice being
recorded.** Three documents point at `#2125` — an open issue — as the
explanation for this behaviour: the constitution's §Availability (line 452),
ADR-0153 (which cites #2125 as an open finding), and spec 078 (`spec.md:184`,
*"until #2125 settles what readiness should mean here"*). An open issue reads
as "known defect, unfixed." What is missing is not a fix; it is a record that
a decision was made, by whom, and why.

**The decision recorded here was made by the repository owner**, in the
session that commissioned spec 172, choosing option 1 of the two #2125 names
(see Alternatives Considered for option 2). Writing this ADR is *implementing*
that decision, not making it — ADR-0144 reserves decisions for a human, and
this document exists because the autonomous lane may not make this call itself
and the decision therefore needs its authorship on the record.

### Two independent reasons, not one

**The availability argument.** An external dependency such as Postgres is
shared by every replica of a service. Failing readiness on it evicts all of
them at once, for a condition none of them caused, and takes with it the HTTP
surface an operator would use to find out what is happening. ADR-0153 records
that every service in this system runs at exactly one instance today — so
"drain every replica" and "drain the service" are currently the same sentence,
but the argument does not depend on that: even at N replicas, a shared
dependency failing is not evidence that *this* replica cannot serve.

**The exposure argument (spec 078).** Spec 078 mapped `/health` and `/alive`
in Production and found the exposure acceptable specifically *because* the
check set is two members and neither name encodes a dependency
(`spec.md:144-152`). `/health` is forwarded unauthenticated through the
gateway's `/{context}/{**catch-all}` routes to all nine context services
(`src/ApiGateway/appsettings.json`), so a check named e.g. `postgres` would put
a dependency name one `ResponseWriter` away from an anonymous caller. A reader
who keeps only one of these two reasons and drops the other will reintroduce
the problem from the side they dropped — the availability argument alone does
not justify *never naming a dependency*, and the exposure argument alone does
not justify *never failing on one*.

### A gap found while implementing this record

Spec 172, which produced this ADR, also drove the unreachable-database path
under test for the first time (`tests/ServiceDefaults.Tests/OutboxBacklogHealthCheckTests.cs`).
The first run was **red**: EF Core's Npgsql execution strategy
(`NpgsqlExecutionStrategy`) wraps any connection failure — a refused connection
and a timed-out one alike, since `NpgsqlException.IsTransient` matches an inner
`SocketException` or `TimeoutException` identically — in a plain
`InvalidOperationException`, which is not a `DbException` and which
`OutboxBacklogHealthCheck`'s original `catch (DbException ex) when
(IsUnreachable(ex))` could not catch. The explicit `Healthy` this ADR describes
was, for a real connection failure, dead code; the framework's own
uncaught-exception handling was reporting `Degraded` instead (still `200`, so
the row 4 outcome above, not a new one — the externally observable contract
held throughout).

`OutboxBacklogHealthCheck.cs` was widened, as part of landing this ADR, to
also catch that wrapped shape, so the deliberate row-3 path in the table above
fires for the failure it is meant to describe rather than falling through to
row 4 by accident. This is completing the decision this ADR records, not
making a new one: both rows already mapped to `200`, and the fix makes the
*mechanism* match the *rationale* uniformly instead of by coincidence for one
shape of connection failure and not the other.

## Decision

1. **`/health` is a liveness and routing signal.** It answers `200` unless the
   process cannot serve at all. It is not, and will not become, a readiness
   signal for shared infrastructure.

2. **No check registered on `/health` may name or reach an external
   dependency.** This is both the availability argument and the exposure
   argument above, and both must hold together: a check is barred from failing
   readiness on a shared dependency *and* barred from naming one in a form an
   unauthenticated caller could read back.

3. **A shared-dependency outage is reported through telemetry and logs, not
   through readiness.** `OutboxBacklogHealthCheck` logs the outage
   (`logger.OutboxBacklogConcerning` and the `Degraded` description string) and
   records it in the health check's own `data` dictionary, which never reaches
   the wire. ADR-0118 means that path currently has no destination in
   Production — the Aspire dashboard is the only sink, and it is a
   development/CI sink. **This ADR records that as a known, undischarged
   consequence, with its own issue (#2125 remains open for this, narrowed —
   see Consequences), not as something this decision settles.**

4. **What would reverse this decision:** a per-replica dependency — one a
   single pod can lose while its siblings keep serving — or a deployment
   topology where evicting one replica is not evicting the whole service.
   Neither exists today: ADR-0153 holds every service at one instance, and no
   Deployment, Service, Ingress, chart or Kubernetes publisher exists anywhere
   in this repository. When either changes, the calculus in this ADR should be
   revisited, not silently outrun by it.

## Consequences

**Positive.**

- The property #2125 observed — `/health` cannot fail on a database outage —
  now has a name, a reason, and an owner, reachable by grepping `docs/adr` for
  "readiness" instead of by finding an open issue by accident.
- The mechanism now matches the rationale for both shapes of connection
  failure a real outage can produce (§Context, "A gap found while
  implementing this record"), verified by a test that failed before the fix
  and passes, unmodified, after it.
- `ADR-0153`'s and spec 078's citations of `#2125` remain accurate as a record
  of what was known when they were written (spec 164 §2's precedent); this ADR
  is the forward pointer for anyone arriving later.

**Negative, and stated rather than hidden.**

- **A pod that cannot serve a single request stays in rotation** if the only
  thing wrong with it is that Postgres is unreachable. That is the trade this
  ADR makes deliberately: it is better than evicting every replica of every
  service for one shared outage, but it is a real cost an operator feels,
  applied per replica.
- **The backlog signal reaches nobody in Production.** ADR-0118's one-sink
  decision means the `Degraded`/unreachable data this check records has no
  destination once out of development/CI. This ADR does not discharge that; it
  is recorded as a consequence with its own open work, not solved here.
- **Nothing guards a future check that violates clause 2.** A tenth check
  registered with the framework's default `failureStatus` (`Unhealthy`), or
  named after a dependency, would violate this ADR and nothing in CI catches
  it today. Filed as a follow-up (see the issue linked from spec 172's tasks,
  T007) rather than solved here — the shape that would work is an integration
  assertion over a booted host's `IOptions<HealthCheckServiceOptions>`, which
  belongs with the Aspire fixture, not a unit test.
- **The `InvalidOperationException`-wrapping behaviour that produced the gap
  in §Context is Npgsql/EF-provider-specific and was not audited across the
  other eight contexts' own `catch (DbException ...)` sites.** Flagged as its
  own follow-up rather than solved here; this ADR's fix is scoped to
  `OutboxBacklogHealthCheck`, the only place `#2125` named.

## Alternatives Considered

**Option 2 — add a `ready`-tagged reachability check.** Declined by the
repository owner. Three costs, all named in #2125 and none avoidable:

1. It reopens spec 078's exposure analysis — a check that reaches Postgres to
   answer readiness is, definitionally, a check whose behaviour (if not
   necessarily its name) encodes a dependency, undermining the reasoning that
   made mapping `/health` in Production acceptable.
2. It needs a timeout of its own, or it hangs the probe — trading "never
   fails" for "sometimes hangs," which is worse for an orchestrator than a
   readiness probe that always answers.
3. It drains every replica at once for a condition none of them caused — the
   availability argument in reverse: a shared-dependency outage would become a
   total outage of the HTTP surface an operator needs to diagnose it.

**A doc comment at the check registration sites only, no ADR.** Rejected: a
comment is where the decision *acts*, not where a reader *looks*. Three
documents already cite `#2125` by number; a comment cannot be the target of
those citations, and the comment that used to carry this reasoning
(`OutboxBacklogHealthCheck.cs`, before commit `588e1e5e`) is precisely what
went stale and stood uncorrected for months.

**Amend ADR-0106 instead of writing a new ADR.** Rejected. ADRs in this
repository are records of a moment, superseded rather than rewritten
(`_template.md:3,6`). ADR-0106 is about the API gateway; the "≥ 2 replicas
with health checks" sentence at issue is one bullet in its Consequences, and
readiness semantics generally are not that ADR's subject.

## Implementation Notes

- `src/ServiceDefaults/OutboxBacklogHealthCheck.cs` — the unreachable-database
  catch now handles both the direct `DbException` shape and the
  `InvalidOperationException`-wrapped shape EF Core's Npgsql execution
  strategy actually produces, both routed through a single `Unreachable(...)`
  helper so the reported description cannot drift from what this ADR says it
  means, the way it drifted before.
- `src/ServiceDefaults/WolverineDefaults.cs` and `src/ServiceDefaults/Extensions.cs`
  carry pointer citations to this ADR at their respective health-check
  registration and mapping sites.
- `.specify/memory/constitution.md:452` cites this ADR alongside `#2125` — a
  citation swap; the sentence's claim is unchanged.
