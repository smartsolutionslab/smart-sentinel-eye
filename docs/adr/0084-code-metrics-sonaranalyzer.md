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
