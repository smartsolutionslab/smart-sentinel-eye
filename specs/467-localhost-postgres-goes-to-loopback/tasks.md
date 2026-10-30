# Tasks 467 — A Postgres connection to `localhost` goes to the loopback address

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3186 (feature-level issue, on Project #13).
Engineer: **backend**. Phase 4a colour: **behaviour-changing → red first**.

No foundational task and no parallelism: one production file, one test file, strictly sequential.
No migration, no AppHost change, no `Shared.Contracts` change, no frontend, no shard-filter change
(`ServiceDefaults.Tests` is a unit project).

## User story 1 (P1) — a context connecting to `localhost` connects to `127.0.0.1` (FR-001…FR-006)

- [ ] **T001 [US1]** (test-writer) Extend `tests/ServiceDefaults.Tests/Persistence/PostgresConnectionBudgetTests.cs`
  (class `PostgresConnectionBudgetTests`) with the tests listed in plan §5:
  - expected red: `Localhost_is_rewritten_to_the_ipv4_loopback_address`, `The_localhost_match_ignores_case`,
    `The_server_synonym_for_host_is_rewritten_too`,
    `An_explicit_pool_size_keeps_its_value_and_localhost_is_still_rewritten`,
    `The_configured_connection_string_is_rewritten_too` (via `GetBoundedPostgresConnectionString`);
  - expected green on arrival (non-match guards): `A_host_other_than_localhost_is_left_as_written`
    (Theory: `db.internal`, `127.0.0.1`, `::1`, `localhost.localdomain`, `localhost,db2`, `mylocalhost`),
    `An_explicit_pool_size_with_another_host_returns_the_string_verbatim`.
  Assert the host against the literal `"127.0.0.1"` via `NpgsqlConnectionStringBuilder`. Run the
  `ServiceDefaults.Tests` project; **quote the red output verbatim**, naming which tests were red and which
  arrived green and why.
- [ ] **T002 [US1]** (backend) `src/ServiceDefaults/Persistence/PostgresConnectionBudget.cs`: implement plan §4
  in `Bounded` — private `Localhost`/`Loopback` constants, case-insensitive exact match on
  `NpgsqlConnectionStringBuilder.Host`, rewrite on both branches, verbatim return preserved for
  explicit-cap + non-localhost. Update the class and `Bounded` XML summaries (FR-006). No public surface,
  no config knob. Depends on T001 (red observed). T001's tests must pass **unmodified**.
- [ ] **T003 [US1]** (backend) Run `ServiceDefaults.Tests` and `Architecture.Tests` (incl.
  `BoundedConnectionStringCharacterisationTests`, `PostgresPoolBoundTests`); confirm all existing tests pass
  unmodified; `dotnet format --verify-no-changes` on the touched files; Release build clean. Depends on T002.

## Phase 5 — verification (not code)

- [ ] **T004** (verify) Warm AppHost boot timed twice before (develop) and twice after: `migrations`
  start → exit and one migrator's first-connection span; observe one running service connecting with host
  `127.0.0.1` (e.g. `server.address` on an Npgsql span in the Aspire dashboard). Write the
  `verification.md` note with the figures as observed and the §IV statement from spec §Latency.
  Depends on T003.

## Out of scope — note for follow-up (do not implement)

- Integration-test harness bypass (`tests/Integration.Tests/Fixtures/AspireFixture.Db.cs` and ~20 tests
  opening connections from `GetConnectionStringAsync`) — plan §3.
- Option 3 (concurrent migrators) — the issue comment says file separately if wanted.
