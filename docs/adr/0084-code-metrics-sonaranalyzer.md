# ADR-0084: Code Metric Limits via SonarAnalyzer

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney enforces hard code-metric limits via analyzers: 300 LOC/file,
30 LOC/method, 4 params max, cyclomatic complexity ≤ 10, nesting
depth ≤ 3. Smart Sentinel Eye had no equivalent automation.

## Decision

Adopt the same limits via **SonarAnalyzer.CSharp**. Severities are set in
`.editorconfig` (scoped to `src/`), thresholds in the root `SonarLint.xml`
wired in as an `AdditionalFiles` item by `Directory.Build.props`. See the
**2026-09-17 amendment** below: from 2026-05-25 until that date this sentence
read "configured in `Directory.Build.props`" and **no threshold was configured
anywhere** — the limits fired on nothing for the whole period.

| Limit | Value | Sonar rule |
|---|---|---|
| Max LOC per file | 300 | S104 |
| Max LOC per method (backend) | 30 | S138 |
| Max parameters | 4 | S107 |
| Cyclomatic complexity | 10 | S1541 |
| Nesting depth | 3 | S134 |
| Frontend: max LOC per method | 50 | (ESLint complexity / max-lines-per-function) |

- **The five limits are advisory, not blocking** (amended 2026-09-17). They are
  configured at `warning` and carved out of Release's
  `TreatWarningsAsErrors` by an explicit `WarningsNotAsErrors`. They appear in
  the build log and in the IDE; they do not fail CI. This is a deliberate
  choice given the baseline recorded below, and reversing it means clearing
  that baseline first, not deleting the carve-out.
- **Test projects exempt** — long, narrative test methods are
  valuable; `S104;S138;S107;S1541;S134` plus xUnit-specific
  conventions are suppressed via `NoWarn` for paths under `tests/`.

## Consequences

- **Positive:** quantitative quality bar; reviewers don't count lines
  by hand.
- **Positive:** aligns with Yumney.
- **Negative:** SonarAnalyzer is one more package to track for
  upgrades.
- **Negative:** some legitimate code (Wolverine config blocks, EF
  configuration methods) brushes against the limits and requires
  per-instance refactoring or a documented `[SuppressMessage]`.

## Alternatives Considered

- **Relaxed limits (500 LOC / 50 LOC / 5 params / 12 / 4)** —
  considered; rejected to stay aligned with Yumney.
- **No automated limits** — drift over time.
- **Stricter limits (200 / 20 / 3 / 8 / 2)** — too aggressive for
  Wolverine handler shapes and Marten projection callbacks.

---

## Amendment — 2026-09-17 (spec 174, issue #2209): configured at last, and advisory

**What was wrong.** From this ADR's acceptance on 2026-05-25 until 2026-09-17,
none of the five limits was configured anywhere. `Directory.Build.props` named
the rule IDs in a comment and suppressed them for tests; no severity and no
threshold existed in any file. Two of the five (S107, S134) are disabled in
SonarAnalyzer by default and so could not fire at all; the other three ran at the
analyzer's own looser numbers (1000 / 80 / 10) rather than this ADR's. Meanwhile
this ADR, `CLAUDE.md`, `CONTRIBUTING.md`, two `.claude/agents/` briefs and a
design document all described the limits as CI-enforced, and reviewers cited them
as such — phase 6 of spec 103 checked a method against the 30-line limit before
discovering the limit did not exist.

**What changed.** The five rules are now configured, in two places because
SonarAnalyzer needs two:

| Input | File |
|---|---|
| Severity — `warning` under `src/`, `none` under `tests/` | `.editorconfig` |
| Thresholds — 300 / 30 / 4 / 10 / 3 | `SonarLint.xml` (root), wired as `AdditionalFiles` for non-test projects |
| Release carve-out so `warning` stays a warning | `WarningsNotAsErrors` in `Directory.Build.props` |

**They are advisory.** `warning`, never `error`; the build does not fail on them.
The test exemption is unchanged and is now stated in `.editorconfig` as well as
in `NoWarn`, because editorconfig severity outranks `NoWarn` and the exemption
had to be written where it wins.

**Baseline at the date of this amendment.** Recorded because an advisory rule
nobody measures is the exact shape of every rule this repository has had to
correct — §II twice, the Phase 3 board gate, §IV's leg table, ADR-0048, and this
ADR. **Production code only; tests are exempt and excluded.**

| Rule | Limit | Violations |
|---|---|---|
| S104 | ≤ 300 LOC/file | 5 |
| S138 | ≤ 30 LOC/method | 116 |
| S107 | ≤ 4 parameters | 84 |
| S1541 | cyclomatic complexity ≤ 10 | 7 |
| S134 | nesting depth ≤ 3 | 2 |
| **Total** | | **214** |

Measured 2026-09-17 against `SmartSentinelEye.slnx` in Release, `--no-incremental`,
0 build errors. Matches the figure expected from the prior read-only
investigation exactly — no discrepancy to report. S1541 and S134 landed at the
same counts (7, 2) they had before `SonarLint.xml` existed, confirming those two
entries are read as thresholds rather than silently ignored (they already sat at
the analyzer's own defaults, which equal ADR-0084's numbers). No `S104` hit named
a file under a `Migrations/` directory, so generated code stayed out of the count.

Re-measure the same way before claiming the figure still holds:

```sh
dotnet build SmartSentinelEye.slnx -c Release --no-incremental \
  -flp:logfile=metrics.log\;verbosity=normal\;NoSummary
grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' metrics.log \
  | sort -u | grep -oE 'S104|S107|S138|S1541|S134' | sort | uniq -c
```

`sort -u` on file, line and rule is what makes the count a count: MSBuild prints
a warning once per project that compiles the file, and a file shared by two
projects would otherwise be counted twice. `--no-incremental` is required — a
warm build skips the projects it would have warned about, and the number comes
back too low. **A build-based count needs the build to finish**; if MSBuild stops
at a failing project the number is a floor, not a total.

**Frontend row unchanged and still unenforced.** This ADR's sixth row (frontend,
50 LOC/method, ESLint `max-lines-per-function`) was not measured and is not
configured by this amendment. It is named here so the correction is not mistaken
for a complete one.

**Why advisory rather than blocking or dropped.** Issue #2209 offered three
honest endings and the repository owner chose this one. Blocking would require
clearing the baseline first — a large refactor with its own characterisation
obligation, mixed into a configuration change. Dropping the limits would discard
a bar that has been shaping review decisions for four months. Advisory makes the
build answer "does this pass ADR-0084?" today, and leaves tightening as a
decision someone can make on evidence.

---

## Amendment — 2026-10-15: S107 does not apply to `[LoggerMessage]` methods

**Status of this amendment: accepted, 2026-10-17, by the repository owner.**
Drafted by the autonomous lane, which may not amend an ADR (ADR-0144).

**What the measurement showed.** The 2026-09-17 baseline put S107 at 84. After
the per-context refactors (#2887–#2895, #2929–#2933, #2951–#2952, #2962–#2963)
S104 is 0 and S138 is 1 (`WolverineDefaults`, #2953), but S107 stands at **108**
in production code (Release, `--no-incremental`, `sort -u`, migrations
excluded, re-measured 2026-10-16 after #2971, #2973 and #2975 each cleared a
helper hit; the first measurement, 2026-10-15, read 113). By location:

| Where | S107 hits |
|---|---|
| Api layer — endpoint and handler methods | 45 |
| `Log.cs` — `[LoggerMessage]` methods | 31 |
| Other — ScenarioSimulator, ServiceDefaults, AppHost | 13 |
| Domain — constructors and factories | 12 |
| Application — handlers | 7 |

**Decision.** The 31 `Log.cs` hits are not a design smell. A `[LoggerMessage]`
extension method carries `this ILogger`, usually an `Exception`, and then the
fields of the message; ADR-0050 requires those to be separate, named,
structured parameters so the sink can index them. Bundling them into a record
would defeat the source generator and the structured fields it exists to
produce. S107 is therefore switched off for files named `Log.cs`, in
`.editorconfig` after the `src/` section so that it wins.

**What this does not do.** It does not touch the other 77. The 45 Api-layer
hits were first described as fixable by the precedent of spec 341
(`IngestManualServices`: injected services bundled into one record bound with
`[AsParameters]`). **They are not**: S107 does not count `[FromServices]`
parameters, and route-handler request parameters are pinned by the route-table
baselines (specs 380/382/383, issue #2971). A second exemption — minimal-API
handler request parameters — is therefore the subject of the second amendment below (options
on #2971); it is not part of this one and is not in `.editorconfig`. The
Domain, Application and "other" hits (32) are
per-case design decisions and remain advisory warnings; none is exempted.

**Consequences.** The S107 count falls by 31 with no code change (108 to 77), so it now
measures something a person could act on. The exemption is by file name: a
`[LoggerMessage]` method placed outside a file called `Log.cs` is still
counted, which at the date of this amendment is none (verified by searching
`src/` for `[LoggerMessage` outside `Log.cs`).

---

## Amendment — 2026-10-16: S107 does not apply to a minimal-API route handler's request parameters

**Status of this amendment: accepted, 2026-10-17, by the repository owner.**
Drafted by the autonomous lane, which may not amend an ADR (ADR-0144).
It is independent of the 2026-10-15 amendment above: accepting either one did
not accept the other, and the owner accepted both.

**What the evidence is.**

- *S107 does not count `[FromServices]` parameters.* Proved on `develop`:
  `GetEvent` has six parameters, two of them `[FromServices]`, and is not a hit;
  removing `[FromServices]` from one of them produced a new hit; the analyzer's
  message for the 12-parameter `IngestWebhook` reads "Method has 5". The spec 341
  pattern (bundle the injected services into a record bound with `[AsParameters]`)
  therefore removes nothing from the count.
- *What S107 does count is the handler's request surface:* route values, query
  values, body, request, user and `CancellationToken`. Bundling those changes the
  handler signature, the minimal-API binding and, unless the record members keep
  their camelCase names, the generated OpenAPI document and the Development 400
  `detail` text. The #2974 scratch harness measured it: a PascalCase record changes
  OpenAPI; PascalCase members with `[FromQuery(Name = ...)]` or `init` properties
  keep OpenAPI but change nine Development 400 bodies; a positional record with
  camelCase members keeps everything identical but deliberately breaks the
  member-naming convention.
- *Every examined context pins every handler's full parameter list* in a
  per-context `*RouteTableCharacterisationTests` baseline (EventIngestion,
  Identity, CameraCatalog, Automation, LayoutComposition, OverlayDesigner,
  SystemVariables; AuditObservability has none). A reshape is an edit to a
  characterisation assertion, which the lane treats as evidence that behaviour
  moved (CLAUDE.md, phase 4a).

**Current numbers** (origin/develop plus the first amendment's `.editorconfig`
section, Release `--no-incremental`, `sort -u`, migrations excluded; the log
prints each warning twice; 0 errors):

| Measure | Count |
|---|---|
| S107 total without either amendment | 108 |
| `Log.cs` hits (first amendment) | 31 |
| S107 after the first amendment | 77 |
| Route-handler hits | 45 |
| Helper hits remaining in the Api layer | 0 |
| Other (Domain 12, Application 7, ScenarioSimulator, ServiceDefaults, AppHost 13) | 32 |

Route-handler hits by context: LayoutComposition 10, EventIngestion 10,
Automation 6, SystemVariables 5, OverlayDesigner 5, CameraCatalog 4,
Identity 3, AuditObservability 2. The helpers in those files that were genuinely
fixable have been fixed (#2971, #2973, #2975); each remaining hit is a
`private static async Task<IResult>` route handler.

**Decision.** S107 does not apply to the route-handler methods themselves. A
handler's parameters are the HTTP contract, not a design choice the author can
bundle away, and the contract is pinned by tests on purpose. Helpers in the same
files stay subject to S107.

**Recommended mechanism, and its cost.** A `[SuppressMessage]` for S107 with a
justification on each of the 45 handlers: it is the narrowest option, it names
exactly what is exempt, and it moves with the method. The cost is one attribute
per handler and one more on every new handler with more than four parameters.
The alternatives are rejected for the reasons that follow. A `[src/**/*Endpoints*.cs]`
severity section is too broad, because a helper added to such a file would be
exempt without anyone deciding so; it is exact today only because the helper
hits are 0. Leaving the 45 as advisory warnings is the honest fallback: S107 is
carved out of `TreatWarningsAsErrors`, so nothing fails, and the cost is that
the count stays at 32 above a floor nobody can reach. The recommended mechanism is the one adopted;
applying it is code work on the issues named below, and nothing is added to `.editorconfig` by this
amendment.

**Consequences.** Once the suppressions are applied, S107 falls from 77 to 32 (to 108 to 32 across both
amendments). It closes #2971, #2972 and #2974, and the handler hits of #2973 and
#2975. It does not close the Domain, Application and other hits (32), which are
per-case design decisions and remain advisory. A handler that is genuinely too
wide (many query values with no shared meaning) is no longer flagged by S107 and
is left to review.
