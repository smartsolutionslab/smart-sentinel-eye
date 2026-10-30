# Spec 467 — A Postgres connection to `localhost` goes to the loopback address

**Feature Branch**: `fix/3186-postgres-localhost-to-loopback`
**Created**: 2026-10-30
**Status**: Draft
**Issue:** #3186 (feature-level issue; maintainer decision in its comment: **option 1**).
**ADRs:** ADR-0125 (the Postgres connection budget — `PostgresConnectionBudget` is the single place
every context's Postgres connection string is shaped; this spec adds one rewrite to that place),
ADR-0067 (MigrationRunner — the most visible consumer, it registers all nine persistence modules),
ADR-0105 (`Ensure.That` guards, already present on both entry points), ADR-0139 / ADR-0144 (red first
for behaviour-changing work; autonomous lane).
**No new ADR.** The maintainer chose option 1 on the issue; it lives inside the mechanism ADR-0125
already establishes ("every persistence module and `AddWolverineForContext` goes through this"). It
adds no knob, no resource, no dependency.
**Latency budget:** see §Latency below — no §IV leg's production budget changes.
**Phase 4a colour:** behaviour-changing → **red first**.

## Problem

Aspire injects `Host=localhost;Port=<mapped>` into every Postgres connection string. On the measured
Windows 11 / Docker Desktop machine, each **new physical** Npgsql connection to `localhost` costs
~2.1 s; the same connection to `127.0.0.1` costs ~80 ms (issue #3186 evidence 1; SSL mode
irrelevant). The suspected cause — `localhost` resolving to `::1` first, and the IPv6 attempt
stalling — is a hypothesis, not established; this spec does not depend on it.

Most visible at the AppHost warm boot: nine sequential EF migrators each pay ~2.1–2.4 s on their first
connection, ~19.4 s of a 37 s `migrations` run. Every long-running service receives the same string, so
it plausibly pays the same cost whenever a pool grows (not measured — issue §"Not established").

## Scope

**In:** `PostgresConnectionBudget.Bounded` (and therefore `GetBoundedPostgresConnectionString`) rewrites
the Postgres host to `127.0.0.1` when, and only when, the host value is exactly `localhost`
(case-insensitive).

**Out (explicitly, per the issue comment):**
- Option 2 — fixing IPv6 forwarding in Docker Desktop / Windows.
- Option 3 — running the nine EF migrators concurrently (file separately if wanted).
- Any connection string that does not pass through `Bounded` (see plan §Bypasses — reported, not fixed).
- Any other host spelling: `127.0.0.1`, `::1`, `[::1]`, a DNS name, `localhost.localdomain`,
  a multi-host list (`localhost,db2`), a host with an inline port (`localhost:5432`). All untouched.
- AppHost's `Host=localhost` emission itself — Aspire owns it.

## User Scenarios & Testing

### User Story 1 (P1) — A context connecting to `localhost` connects to `127.0.0.1`

As an operator booting the stack (and as every service whose pool grows), I want a Postgres connection
string that names `localhost` to reach the server over `127.0.0.1`, so a new physical connection does
not stall ~2 s on the `localhost` resolution path.

**Why P1:** it is the whole issue; one slice.

**Independent test:** unit tests on `PostgresConnectionBudget.Bounded` (below), then the warm-boot
re-timing in §Verification.

**Acceptance scenarios**

```gherkin
Scenario: localhost is rewritten (happy path)
  Given the connection string "Host=localhost;Port=54321;Database=audit-db;Username=postgres;Password=pw"
  When it is bounded
  Then its host is "127.0.0.1"
  And its port, database, username and password are unchanged
  And its maximum pool size is PostgresConnectionBudget.MaxPoolSize

Scenario: the match is case-insensitive
  Given a connection string whose host is "LocalHost"
  When it is bounded
  Then its host is "127.0.0.1"

Scenario: the Server synonym names the same keyword
  Given the connection string "Server=localhost;Database=audit-db;Username=postgres"
  When it is bounded
  Then its host is "127.0.0.1"

Scenario: an explicit pool size still wins, and localhost is still rewritten (escape-hatch branch)
  Given the connection string "Host=localhost;Database=audit-db;Username=postgres;Maximum Pool Size=7"
  When it is bounded
  Then its maximum pool size is 7
  And its host is "127.0.0.1"

Scenario Outline: any other host is left exactly as written (conflict / non-match)
  Given a connection string whose host is "<host>"
  When it is bounded
  Then its host is "<host>"

  Examples:
    | host                  |
    | db.internal           |
    | 127.0.0.1             |
    | ::1                   |
    | localhost.localdomain |
    | localhost,db2         |
    | mylocalhost           |

Scenario: an explicit pool size with a non-localhost host returns the string verbatim
  Given the connection string "Host=db.internal;Database=audit-db;Username=postgres;Maximum Pool Size=7"
  When it is bounded
  Then the result is identical, character for character, to the input

Scenario: the configuration entry point applies the same rewrite
  Given configuration "ConnectionStrings:audit-db" = "Host=localhost;Database=audit-db;Username=postgres"
  When GetBoundedPostgresConnectionString(builder, "audit-db") is called
  Then the host of the result is "127.0.0.1"

Scenario: bad request — blank or null connection string is still refused
  Given a connection string that is "", "   " or null
  When it is bounded
  Then an ArgumentException is thrown   # existing tests, unchanged
```

**Auth:** N/A — no endpoint, no caller identity; the change is in-process connection-string shaping.

### Edge cases

- Host value with surrounding whitespace (`Host= localhost `): Npgsql's builder trims on parse; whatever
  `NpgsqlConnectionStringBuilder.Host` reports is what is compared. Not asserted separately.
- `localhost` in a different keyword (e.g. a password that happens to be `localhost`): untouched — only
  the host is compared.
- A Postgres server that listens **only** on `::1`: would become unreachable via `localhost`. Accepted:
  Docker Desktop and Aspire-published ports bind IPv4; production hosts are DNS names (k3s), not
  `localhost`. Recorded as an assumption.

## Requirements

- **FR-001** `Bounded` sets the host to `127.0.0.1` when `NpgsqlConnectionStringBuilder.Host` equals
  `localhost` under `StringComparison.OrdinalIgnoreCase`.
- **FR-002** Any other host value is left unchanged.
- **FR-003** The rewrite applies on both branches of `Bounded` — cap applied, and explicit pool size
  honoured. The explicit-pool-size branch still returns the input **verbatim** when no rewrite is needed.
- **FR-004** All other connection-string parameters survive (existing
  `Applying_the_cap_keeps_the_rest_of_the_connection_string` stays green unmodified).
- **FR-005** No new configuration key, option or public member. `127.0.0.1` and `localhost` are private
  constants of `PostgresConnectionBudget`.
- **FR-006** The class/method XML docs say why the rewrite exists (the measured ~2.1 s vs ~80 ms) and
  that it is limited to the literal `localhost`.

## Success criteria

- **SC-001** New unit tests for FR-001/FR-003 observed red on unmodified `develop`, failure quoted in the PR.
- **SC-002** All existing `PostgresConnectionBudgetTests`, `BoundedConnectionStringCharacterisationTests`
  and `PostgresPoolBoundTests` pass unmodified.
- **SC-003** (phase 5, measured twice before and twice after) warm AppHost boot, `migrations` start →
  exit, drops from ~37 s toward < ~20 s; the per-migrator "Opening connection" → "Opened connection"
  span drops from ~2.1 s to ~0.1 s. If it does not, the hypothesis is wrong and that is reported, not
  tuned around.
- **SC-004** (phase 5) one service's EF connection string, as resolved at runtime, carries
  `Host=127.0.0.1` — observed on the running system, not inferred from the test.

## Latency (constitution §IV)

**No §IV leg's production budget changes.** Production (k3s) connection strings name a Service DNS
host, never `localhost`, so FR-002 leaves them byte-identical in content.

On a developer / CI stack the change is on the data path of every context, so it reaches the
**Event → overlay state (≤ 200 ms)** leg wherever that leg touches Postgres (EF writes, the Wolverine
Postgres outbox). Today, any request or message that forces a pool to open a **new physical connection**
on the measured machine pays ~2.1 s — ten times that leg's whole budget — and after this change pays
~80 ms. Pooled connections (the steady state) open in 0–2 ms either way and are unaffected. So the
direction is strictly an improvement, the effect is confined to pool growth, and the leg's dev/CI
measurement bracket (82–234 ms / 57.6–75 ms) is not expected to move because those runs measure warm
pools. No §VII dashboard obligation is added: no leg is newly built.

## Assumptions

- **A1** The slow-`localhost` effect is at least machine-specific (only one Windows machine measured;
  the issue's "premise to confirm" — CI/second machine — is not a precondition, because the rewrite is
  harmless where `localhost` is already fast: it saves a name resolution).
- **A2** `Server=` is the same Npgsql keyword as `Host=` (Npgsql synonym); treating it identically is the
  literal reading of "only the Postgres Host keyword", not a widening.
- **A3** IPv4 loopback reaches every Postgres this code is pointed at in dev and CI.
