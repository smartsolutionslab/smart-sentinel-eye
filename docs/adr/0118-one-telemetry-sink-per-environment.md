# ADR-0118: One telemetry sink per environment; the comparison phase is abandoned

**Status:** Accepted
**Date:** 2026-08-22
**Amends:** ADR-026 (observability stack), and constitution §VII's third bullet
**Relates to:** ADR-0117, #1707, #1681, spec 024, spec 025

## Context

ADR-026 committed to an OpenTelemetry Collector fanning OTLP to **both** the
Aspire dashboard and a Grafana stack (Prometheus, Loki, Tempo, Grafana,
Alertmanager) during the walking skeleton and first two features, with a single
sink chosen before v1 GA and an explicit sunset clause.

**None of it was built.** Twenty-five features later there is no collector, no
Prometheus, no Grafana, no Loki, no Tempo and no Alertmanager anywhere in the
AppHost. The comparison phase never started, so its sunset clause has nothing to
sunset and its decision point has never arrived.

What exists instead, and works: every service exports OTLP to the endpoint Aspire
injects, and the Aspire dashboard displays it. Spec 023 added Wolverine's trace
source and spec 024 added a metrics meter, both of which reach that dashboard.

Two consequences forced this decision rather than letting it drift further:

1. **§VII now binds implemented legs** (ADR-0117), and spec 025 discharged only
   half of it for the `event → overlay state` leg: the latency histogram is
   emitted but **cannot be read from outside the process that records it**. The
   dashboard shows metrics; nothing else does, and there is no programmatic
   readout. "Measured" currently means "the number exists somewhere".
2. **An ADR describing something that does not exist is worse than no ADR.** It
   reads as a commitment to anyone consulting it, and it has been quietly
   contradicted by every feature since the walking skeleton.

## Decision

**The comparison phase is abandoned. One sink per environment, chosen by
environment rather than by comparison.**

1. **Development and CI: the Aspire dashboard.** It is the sink today, it is
   fed by the OTLP exporter Aspire already injects, and it needs no new
   infrastructure. It is sufficient for a human answering "what happened", and
   it is what §VII's dashboard requirement is satisfied by in development.
2. **Production: deferred, and deliberately.** The Grafana stack is not built
   because nothing is deployed to production yet (ADR-024/025 put k3s + Helm
   ahead of it). The production sink is decided **when there is a production
   deployment to attach it to**, and that decision belongs with the deployment
   work rather than here.
3. **No dual-sink comparison.** It was a device for choosing between two
   options, and it cannot run: only one option exists. Choosing by environment
   makes the comparison unnecessary rather than merely unperformed.
4. **A readable metrics path is a requirement of production observability, not
   an optional extra.** §VII's "measured" must eventually mean "someone can
   consult it", and today it does not. This ADR records that gap rather than
   closing it.

## Consequences

**The documents stop lying.** ADR-026 described a stack that does not exist and a
phase that never began. §VII's third bullet described both. Both now describe
what is true.

**§VII becomes satisfiable in development and explicitly not in production.**
That is the honest state: a leg can be watched on a developer's dashboard and
cannot be watched by anyone operating a fab, because there is no fab deployment.
The constitution's §IV table already carries "recorded, not yet readable" for the
one measured leg, and that phrasing survives this ADR unchanged.

**The production decision is deferred, not dodged — and it now has a trigger.**
"Before v1 GA" was a date nobody was watching. "When there is a production
deployment" is an event someone will notice, because it cannot happen without
them.

**Grafana is not ruled out.** It remains the expected production choice for the
reasons ADR-026 gave: retention, alerting, and dashboards that outlive a process.
This ADR declines to commit to it before there is something to run it against.

**Something is lost.** ADR-026's comparison would have produced evidence about
which sink serves this system better, and abandoning it means that choice will be
made on reputation rather than trial. Accepted: a comparison that has not started
in twenty-five features is not going to start, and pretending otherwise has
already cost more than the comparison would have been worth.

## Alternatives Considered

**Enact ADR-026 as written.** Stand up the collector and the full Grafana stack
now, run both sinks, compare, choose. Rejected: it is a substantial
infrastructure addition serving a comparison whose outcome nobody is waiting on,
at a point where there is no production deployment to inform it. It would also
front-load operational cost onto a system that cannot yet be operated.

**Split it — instrument now, decide the sink later, leave ADR-026 standing.**
This is what has been happening by default, and it is the status quo that
produced an ADR contradicted by the repository. Rejected because governance says
conflicts are *"resolved by amending one of the two — never by ignoring it"*, and
leaving a Locked ADR describing an unbuilt stack is ignoring it.

**Commit to Grafana now and skip the comparison.** Coherent and possibly where
this lands. Rejected as premature: the choice depends on how the fabs are
operated — on-prem constraints, who is on call, what retention the customer
requires — and none of that is settled. Deferring with a trigger is better than
choosing without inputs.

**Delete ADR-026 entirely.** Rejected: it recorded real reasoning about why
observability needs a retention-capable sink eventually, and that reasoning
survives. An ADR is amended, not erased.
