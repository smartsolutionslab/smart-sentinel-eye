# Plan — Spec 128

## Phase 4a colour: RED (behaviour-changing), with one honest exception

| Gap | Red | Green |
|---|---|---|
| 1 | `AppHostMigrationGateTests` — build the **run-mode** application model and assert each of the nine context services carries a `WaitAnnotation` on `migrations` of the completion kind with expected exit code 0. Fails on today's tree, where that annotation exists only under `E2ETests=true`. Plus a **real run-mode boot** with migrations forced to fail, observed through the Aspire dashboard resource list. | The `WaitForCompletion` loop moves out of `if (isE2ETests)`. |
| 2 | `scripts/wait-for-e2e-stack.test.mjs` — runs the real script under stubbed `curl` and `docker` on `PATH`. The stubs describe a stack whose ports serve and whose gateway answers 401 but whose databases carry **no applied migrations**. Today's script exits 0 against that. | The script grows a migrations probe and exits 1 with a named error. |
| 3 | **None, honestly.** `if: failure()` versus `if: always()` is a GitHub Actions runtime decision. A test that reads the YAML and asserts the string would pass whether or not the step ever runs — it proves the file says something. The evidence is the observed run instead (spec.md, Gap 3): in one cancelled job, the `always()` siblings ran and the `failure()` step was skipped. | `if: failure() || cancelled()`. |

## Gap 1 — how migrations are made to fail on demand

No product code is edited to produce the failure. `migrations` mints a
Keycloak token as the `migration-runner` client to read `/fabs` (spec 019,
FR-011: an unreachable realm makes the run fail). The secret is an Aspire
parameter with a dev default, so the AppHost is booted with

```
dotnet run --project src/AppHost -c Release --no-build -- \
  Parameters:MigrationRunnerClientSecret=deliberately-wrong ScenarioSimulator=false
```

which leaves every other resource untouched and gives `migrations` a real,
non-zero exit. Resource states are read from the Aspire dashboard.

## Gap 2 — what the probe asks

`docker exec` into the `postgres` container (`--filter name=^postgres`, which
does not match `pgadmin`), then for each of the nine databases:

```sh
PGPASSWORD="$POSTGRES_PASSWORD" psql -U "$POSTGRES_USER" -d "$db" \
  -tAc 'select count(*) from "__EFMigrationsHistory"'
```

The password is read from the container's own environment, so no credential
enters the script. A database that does not exist, a table that does not
exist, a count of zero, or a container that cannot be found are all *not
ready*; after the bound they are a loud failure (FR-003).

Placed **first**, before the port waits: it is the cheapest question and the
one whose failure explains the others.

## Gap 3 — why `failure() || cancelled()` and not `always()`

`always()` would also print 300 lines of AppHost log on every green run.
`failure() || cancelled()` names the two cases the step exists for and keeps
a passing job quiet. `cancelled()` is what a job-level `timeout-minutes`
expiry sets, which is the case that lost run `33623647778`'s log.

## Contention (ADR-0109)

Two contention files are touched — `src/AppHost/AppHost.cs` and
`.github/workflows/ci.yml`. Both changes are small and local (one `if` block
moved, one `if:` expression). This branch is the single owner of both for
this batch.

## Order

1. Tests (4a), observed red.
2. `ci.yml` guard (gap 3) — independent, no test depends on it.
3. `wait-for-e2e-stack.sh` probe (gap 2) — turns its `.mjs` test green.
4. `AppHost.cs` edge (gap 1) — turns `AppHostMigrationGateTests` green.
5. `dotnet build -c Release`, the two suites, the run-mode boot.

Each commit builds on its own (ADR-0087 rebase-merge lands them singly).
