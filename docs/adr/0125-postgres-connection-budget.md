# ADR-0125: The Postgres connection budget is stated, not discovered

**Status:** Accepted
**Date:** 2026-08-28
**Relates to:** [ADR-0009](./0000-initial-decisions.md) (PostgreSQL as
default persistence), [ADR-0088](./0088-wolverine-configuration-defaults.md)
(Postgres-backed outbox per context), [ADR-0124](./0124-parallel-listeners-where-order-does-not-matter.md)
(parallel listeners are paid for out of this budget)

## Context

Nine bounded contexts share one Postgres. Each long-running service opens
**two** pools against it — one for its EF `DbContext`, one for
Wolverine's message store. They carry the same connection string but are
separate `NpgsqlDataSource` instances, so they do not share a pool.

Npgsql's default `Maximum Pool Size` is **100**. Nothing in the codebase
set it. So the platform's potential demand was 9 × 2 × 100 = **1 800
connections** against a server allowing 100, and the only reason it
worked was that the pools never all grew at once.

They had grown far enough. Measured on a run-mode stack **at idle,
before any load**: **97 of 100 connections held**, all `idle`, spread
7–16 per database.

**What that produces is the reason this is an ADR and not a config
tweak.** When the budget runs out, the service refused a connection is
whichever one asks next — not the one that took them. Driving audit
ingest at 100 ev/s took the cluster over, and the failure surfaced as:

```
Npgsql.PostgresException (0x80004005): 53300: sorry, too many clients already
  … RequestPath: /system-variables/{name}/value
```

A 500 on an unrelated context's write path, with a stack trace pointing
at that context's own `DbContext` and nothing anywhere naming the cause.
It also silently corrupted a measurement: a change that cut audit latency
by an order of magnitude looked like it had barely helped, because the
run was connection-starved rather than throughput-bound.

## Decision

**Bound the pools; state the arithmetic; check it.**

`PostgresConnectionBudget` in `ServiceDefaults` is the single place the
numbers live:

| | |
|---|---|
| `MaxPoolSize` | 20 per pool |
| `PoolsPerService` | 2 (EF `DbContext` + Wolverine message store) |
| `FixedConnectionsPerDatabase` | 2 (unpooled — see below) |
| `Services` | 9 |
| `ServiceCeiling` | 378 |
| `ReservedForToolingAndOperators` | 100 |
| `ServerMaxConnections` | 500 |

`MaxPoolSize` is sized against observation, not taste: the heaviest
consumer is AuditObservability, whose four listeners plus HTTP peaked at
22 connections across both pools at 100 ev/s — about 11 per pool. Twenty
clears that by ~80% while keeping the total inside the server. A cap is
not a reservation; a pool only grows under demand, so this costs nothing
until it is needed.

### The pool count was measured, and reading the code would have got it wrong

Setting the cap temporarily to **3** and loading the stack put every
database at exactly **6** pooled connections, which is what confirms
`PoolsPerService = 2` rather than the two call sites merely implying it.

The two under load sat at **8**, and the extra pair is the part no
amount of reading would have produced:

| connection | what it is |
|---|---|
| `wolverine-advisory-lock:WolverineEnvelopeStorage` | a dedicated connection Wolverine keeps open for its advisory lock, outside any pool |
| `TimescaleDB Background Worker Scheduler` | server-side, one per database carrying the extension — not a client connection, but it occupies a slot all the same |

Eighteen slots across the platform. Small in absolute terms, and exactly
the kind of omission that turns a budget which "adds up" into one that
does not. `ServiceCeiling` counts them.

The reserve is a **named number rather than a fraction of the server**,
because what it covers is a list — the `MigrationRunner`'s nine
registrations, pgAdmin, an operator's `psql`, Postgres'
`superuser_reserved_connections` — and because the moment it matters most
is a saturation incident, when the person diagnosing it still has to be
able to connect.

Every persistence module and `AddWolverineForContext` reads its
connection string through
`builder.GetBoundedPostgresConnectionString(name)` rather than from
configuration directly. That call also replaced nine identical
read-and-throw pairs, so the bound arrived as a simplification rather
than as an extra step to remember.

An explicit `Maximum Pool Size` already in a connection string **wins**.
That is the escape hatch: a deployment needing a different cap says so
in its connection string rather than requiring a new knob here.

### Three guards, because each catches a different way this breaks

- **`PostgresConnectionBudgetTests`** — the arithmetic. `ServiceCeiling`
  plus the reserve fits inside `ServerMaxConnections`, and the ceiling
  provably counts the unpooled connections. A tenth context therefore
  fails a test rather than a production write.
- **`PostgresPoolBoundTests`** — the call sites. Reads the IL of every
  Infrastructure assembly and fails on any direct
  `GetConnectionString("…-db")`, with no exemption list, for the same
  reason `OutboxCommitTests` has none. Verified by breaking a module and
  watching it fail, not merely by watching it pass.
- **`PostgresConnectionBudgetIntegrationTests`** — the deployed reality.
  Asks the *running* server what it allows. `AppHost` cannot reference
  `ServiceDefaults` (an Aspire project reference exposes no assembly), so
  `max_connections` is written out there; this is what holds the two
  together, and it is the stronger check anyway because it also catches
  the container ignoring the argument.

### What is deliberately not decided

**Whether production gives each context its own Postgres or keeps one
shared instance.** There is no production deployment yet and no Postgres
chart in `deploy/helm`, so the question has no answer to record —
inventing one here would be the speculative generality this repo avoids.
The numbers above describe the dev and CI stack, which is where they are
enforced. If production splits the databases, `Services` drops to 1 per
instance and the ceiling stops binding; if it keeps one, this arithmetic
is the production arithmetic and the server sizing follows from it.

`MigrationRunner` is excluded from `Services` although it registers all
nine persistence modules: it migrates sequentially and exits, so at most
one of its pools is ever active, and it is finished before the services
take load. The reserved quarter covers it.

## Consequences

- **Positive:** no single context can consume the shared budget, so the
  cross-context failure above cannot recur by growth alone.
- **Positive:** the sum is known at composition time and enforced, so
  adding a context surfaces the arithmetic instead of deferring it.
- **Positive:** nine duplicated read-and-throw pairs became one call.
- **Negative:** a service that genuinely needs more than 20 connections
  per pool now queues instead of opening more. That is the intended
  trade — queuing is visible as latency, whereas exhaustion is visible
  as somebody else's exception — but it means a cap set too low presents
  as slow rather than as a cap, which is its own kind of quiet.
- **Negative:** `ServerMaxConnections` is written in two places, held
  together by an integration test rather than by the compiler.

## Alternatives Considered

- **Raise `max_connections` and leave the pools unbounded.** This was
  the first move (400, in ADR-0124's PR) and it is what "chase the
  limit" looks like: it buys room without bounding demand, so the same
  failure returns one context later.
- **Cap per context, individually tuned.** More knobs, nine places to
  get wrong, and the evidence says one number clears the heaviest
  consumer with room to spare.
- **Share one pool between EF and Wolverine.** Would halve the demand,
  but Wolverine builds its own `NpgsqlDataSource` and threading one
  through is a fight with two frameworks' composition for a problem a
  cap solves outright.
- **A connection pooler (PgBouncer) in front of Postgres.** The
  general answer, and probably the right one if production keeps a
  shared instance. Rejected for now as infrastructure this stack does
  not otherwise need, and it would not have made the arithmetic visible
  — which is half of what went wrong.
