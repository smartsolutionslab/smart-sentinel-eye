# Plan 255 — The assemblies the guard never loads

**Spec**: [spec.md](spec.md) · **Issue**: #2586 · **Phase**: 2 (Plan)

One file changes: `tests/Architecture.Tests/OutboxCommitTests.cs`. No `src/`, no
Shared.Kernel/Contracts, no AppHost. Bounded context: none — this is the
architecture-test project, which references every context by design.

## 1. Decision

**Widen the scanned set from 9 to 29 assemblies** — Infrastructure ×9 (today), Api ×9,
Application ×9, `SmartSentinelEye.ServiceDefaults`, `SmartSentinelEye.MigrationRunner`
— and **add a completeness fact** that derives, independently of the list, which
assemblies can reach a `DbContext` and fails if any is unscanned.

**No offender surfaced** (spec §1.1: 32/32 green with Api, Application,
ServiceDefaults, MigrationRunner and ApiGateway added). No triage, no new
`PermittedDirectCommits` entry, no `src/` change.

**Not added:** `*.Domain`, `Shared.*`, `ApiGateway` — no EF and no Infrastructure
reference in their metadata (spec §1.2), so a direct commit there does not compile.
The completeness fact enforces that reasoning rather than this plan restating it.

### Rejected: widen the list only

The smallest edit, and it would close #2586 as filed. Rejected because it
repeats the defect: the list is a hand-maintained proxy for "assemblies that can
commit", exactly as the namespace/name filter was a proxy for "types that commit"
(spec 247). It also has no honest red — every added case arrives green, since no
offender exists — so ADR-0139's red-first would have to rest on a counterfactual
alone. The completeness fact gives a red that the widening turns green.

### Rejected: derive `Assemblies()` from the metadata instead of listing

Self-maintaining, but an empty or wrong derivation makes the guard vacuous and
silent. A static list plus a fact that checks it keeps the scanned set readable in
review and makes a derivation bug fail loudly instead.

## 2. The completeness fact (phase 4a)

Name: `Every_assembly_that_can_reach_a_DbContext_is_scanned`.

- **Candidates:** every `SmartSentinelEye.*.dll` in `AppContext.BaseDirectory`,
  excluding this test assembly (`typeof(OutboxCommitTests).Assembly` — it references
  EF for its probe `DbContext` and is deliberately never scanned; see the probe
  fact's doc). Read names via `AssemblyName.GetAssemblyName` and
  `Assembly.Load(name).GetReferencedAssemblies()`.
- **Can reach a DbContext** ⇔ its referenced-assembly names include
  `Microsoft.EntityFrameworkCore`, **or** any name matching
  `SmartSentinelEye.*.Infrastructure`. The second clause is the issue's threat
  model exactly: `*.Api` carries no EF reference today (spec §1.2) and would be
  missed by the first clause alone.
- **Assert:** the set of reaching assemblies minus the scanned list is empty,
  `ShouldBeEmpty` with a message naming each missing assembly and telling the
  reader to add it to the scanned list (or, if it genuinely cannot commit, to
  explain why here — not to narrow the criterion).
- **Guard against vacuity** in the same fact: the reaching set must contain
  `SmartSentinelEye.CameraCatalog.Infrastructure` (a known member). A directory
  probe that finds nothing would otherwise pass. That is an independent fact about
  the build output, not a restatement of the list.

**Expected red** on today's tree (from spec §1.2) — exactly these 20, no more, no
fewer:

- `SmartSentinelEye.{CameraCatalog,StreamDistribution,LayoutComposition,SystemVariables,EventIngestion,OverlayDesigner,Automation,Identity,AuditObservability}.Api`
- the same nine `.Application`
- `SmartSentinelEye.ServiceDefaults`, `SmartSentinelEye.MigrationRunner`

Anything else in the list (a Domain, `ApiGateway`, `Shared.*`) means the criterion
is wrong — stop and report. Fewer than 20 means the probe misses assemblies — stop.

Doc comment: spec 255 / #2586; the metadata argument (a direct commit needs a
MemberRef into EF or a reference to the assembly defining the concrete context);
the known limit — only assemblies Architecture.Tests references are in the
output folder, so a new context must be added to the csproj, as for every other
boundary test.

## 3. The widening (phase 4b)

- Add the 20 names to the list. No per-entry comments — the completeness fact
  documents why they are there.
- **Rename `PersistenceAssemblies` → `ScannedAssemblies`** (field, the two uses in
  `Every_permitted_direct_commit_still_commits_directly` and `Assemblies()`, and
  the message text "the scanned PersistenceAssemblies"). It now holds endpoints and
  hosts; the old name would mislead the next reader about what is scanned. Local to
  this file.
- Class-doc: one sentence that the scanned set is every assembly that can reach a
  `DbContext`, held by the completeness fact (spec 255).
- Engineer may not edit the fact from §2.

## 4. Behaviour of the existing facts after the change

- `Nothing_commits_without_its_announcements`: 9 → 29 cases, all green (measured).
- `Every_permitted_direct_commit_still_commits_directly`: unchanged, green
  (`StreamDistribution.Infrastructure` still scanned).
- `The_rule_sees_both_spellings_of_a_direct_commit`: unaffected (test assembly).
- Runtime: in the scratch run the slowest single case took 9 s (first-load cost,
  cases run in parallel); record the filtered run's wall time in the phase-4b report.

## 5. Constitution / ADR check

§Testing and ADR-0139: red observed first (§2's expected list). ADR-0144: two
agents, test-writer then engineer, engineer may not touch the fact. ADR-0036:
one file. ADR-0052/0053: Shouldly, sentence-style name. No ADR needed — no
architectural decision is made; the rule (ADR-0088) is unchanged, only the reach
of its instrument.
