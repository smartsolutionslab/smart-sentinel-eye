# Spec 255 — The assemblies the guard never loads

**Issue:** [#2586](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2586)
— *"The outbox-commit guard's widened candidate filter still doesn't scan
\*.Api assemblies"*. Labels `bug`, `agent:ready`. On Project #13, status **Todo**
(`gh issue view 2586 --json projectItems`, 2026-09-25). No `item-add` needed.

**Spec number.** `origin/develop`'s highest is 252. 253 is claimed on disk in the
main worktree; **254 is claimed three times** — `origin/test/2450-mqtt-credential-history`
(`254-the-credential-the-fake-never-kept`), and uncommitted in `sse-2570`
(`254-the-race-the-catch-mislabels`) and `sse-2589` (`254-the-copies-248-left-behind`).
That collision is not this spec's to resolve, but the orchestrator should know.
This spec is **255**; no worktree or remote claims it. Re-check before the PR.

**File contention.** No open PR touches `tests/Architecture.Tests/OutboxCommitTests.cs`
(checked by file list over `gh pr list --state open`). #2588 edits
`ITransactionalCommit.cs` doc only — no overlap.

**ADRs referenced:** ADR-0088 (Postgres outbox — why the rule exists); ADR-0139
and constitution §Testing (new behaviour observed red first); ADR-0144 (autonomous
lane, phase-4a colour); ADR-0036 (smallest change); ADR-0037 (phases);
ADR-0052/0053 (xUnit + Shouldly, sentence-style names).

**No new ADR.** Build-time test guard only. It extends the instrument spec 247
(#2469) and spec 252 (#2470) already reviewed; no production code, contract,
constitution clause or runtime behaviour changes.

**Latency budget (§IV): N/A.** Every changed file is under `tests/`.

**Colour: red** (behaviour-changing — the guard's scanned scope grows).

---

## 1. The issue's premise, re-checked on this tree (`0d349ce8`, 2026-09-25)

| Claim | Found |
|---|---|
| The guard scans only the nine `*.Infrastructure` assemblies | **True.** `OutboxCommitTests.PersistenceAssemblies` |
| `*.Api` can name the concrete `XxxDbContext` | **True.** Each of the nine `*.Api` DLLs carries an AssemblyRef to its sibling `*.Infrastructure` (metadata, not just csproj) |
| Only one direct-commit call site exists in `src/` | **True.** `StreamFabAttributionService.cs:81`, already exempt (spec 247) |

### 1.1 Measured: what the widened scan reports

Scratch edit of `PersistenceAssemblies` adding **30 − 9 = 21** assemblies — the nine
`*.Api`, the nine `*.Application`, plus `ServiceDefaults`, `MigrationRunner` and
`ApiGateway` as a wider control — then
`dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~OutboxCommitTests"`
(SDK 10.0.401):

```
Total tests: 32
     Passed: 32
```

Every one of the 30 `Nothing_commits_without_its_announcements` cases green, the
spec-193/247/252 probe fact green, the stale-entry fact green. **Zero offenders in
any added assembly.** The scratch edit was reverted (`git status` clean).

### 1.2 Which assemblies can commit at all — measured from metadata

A direct commit leaves a MemberRef into EF Core, and naming a concrete context needs
a reference to the assembly that defines it. Grepping each built DLL's metadata
(`tests/Architecture.Tests/bin/Debug/net10.0`):

| Assembly group | AssemblyRef `Microsoft.EntityFrameworkCore` | AssemblyRef a `*.Infrastructure` |
|---|---|---|
| 9 × `*.Infrastructure` (scanned today) | yes | — |
| 9 × `*.Api` | **no** | **yes** (own sibling) |
| 9 × `*.Application` | **yes** (query sources use EF's async LINQ) | no |
| `ServiceDefaults` | yes | no |
| `MigrationRunner` | yes | yes (all nine) |
| 9 × `*.Domain`, `Shared.*`, `ApiGateway` | no | no |

So the population that *can* hold a direct commit is exactly: Infrastructure ×9,
Api ×9, Application ×9, ServiceDefaults, MigrationRunner — 29 assemblies. The guard
reads 9 of them.

### 1.3 Application: plausible, so included

The constitution's layering says Application depends on abstractions, and its
query-source doc comments say "Application stays EF-Core-free". The metadata says
otherwise: all nine Application projects reference the `Microsoft.EntityFrameworkCore`
package. Application cannot name `XxxDbContext`, but it can call
`SaveChangesAsync` on a base-typed `DbContext` (a parameter, a
`GetRequiredService<DbContext>()`), and that compiles. The layering rule is a
convention, not a wall — which is the same shape of proxy spec 247 removed from the
candidate filter. Measured clean, so including it costs one line each.

### 1.4 ServiceDefaults and MigrationRunner: in, by the same criterion

Not named in the issue, but they meet the same test (§1.2) and measure clean.
`ServiceDefaults` hosts the seam itself (`OutboxTransactionalCommit`, which calls
Wolverine's `SaveChangesAndFlushMessagesAsync` — rescued by the declaring-type check,
as the green run shows) and `IdempotencyStore<TDbContext>`. Leaving them out would
need a named exclusion with a reason; there is none, so they go in.

---

## 2. User stories

### US1 (P1) — The guard reads every assembly that can commit

As the maintainer relying on the outbox guard, I want it to scan every project
assembly that can reach a `DbContext`, so that a direct commit in an endpoint, an
Application service or a shared host component fails the build exactly as one in a
repository does.

```gherkin
Scenario: a direct commit in an Api assembly is reported
  Given a type in SmartSentinelEye.CameraCatalog.Api that resolves CameraCatalogDbContext
    and awaits SaveChangesAsync on it
  When the architecture tests run
  Then Nothing_commits_without_its_announcements fails for "SmartSentinelEye.CameraCatalog.Api"
    naming that type

Scenario: today's tree stays green
  Given no direct commit outside StreamFabAttributionService
  When the architecture tests run
  Then every case of Nothing_commits_without_its_announcements passes

Scenario: the permitted exception still resolves
  Given StreamFabAttributionService in PermittedDirectCommits
  Then Every_permitted_direct_commit_still_commits_directly passes
```

### US2 (P1) — The scanned list cannot fall behind the code

As the same maintainer, I want a new assembly that gains the ability to commit to
fail the build until it is scanned, so the list does not rot the way the filter did.

```gherkin
Scenario: an assembly that can reach a DbContext but is not scanned
  Given a SmartSentinelEye assembly in the test output that references
    Microsoft.EntityFrameworkCore or a SmartSentinelEye.*.Infrastructure assembly
  And it is not in the scanned list
  When the architecture tests run
  Then Every_assembly_that_can_reach_a_DbContext_is_scanned fails naming it

Scenario: an assembly that cannot commit is not demanded
  Given SmartSentinelEye.CameraCatalog.Domain (no EF, no Infrastructure reference)
  Then the completeness fact does not require it to be scanned
```

(Bad-request / auth scenarios: N/A — no endpoint, no caller.)

## 3. Independent end-to-end procedure

1. On the branch tip, run the filtered command in §1.1 — all green, 29 theory cases.
2. Counterfactual A (US1): add a throwaway class to `src/CameraCatalog/Api/` that
   takes `CameraCatalogDbContext` and awaits `SaveChangesAsync`; re-run — the
   `CameraCatalog.Api` case fails naming it. On `develop` the same plant is green
   (the gap). Revert; `git diff` empty.
3. Counterfactual B (US2): delete one `*.Api` entry from the list; re-run — the
   completeness fact fails naming it. Restore.

## 4. Out of scope

- The IL scan's own blind spots (interface dispatch, `ExecuteSql*`, reflection) —
  recorded in `ReferencesSaveChanges`' remarks (spec 252).
- Assemblies Architecture.Tests does not reference: a new bounded context must be
  added to its csproj, as for every other boundary test in the project.
- Any file under `src/`.
