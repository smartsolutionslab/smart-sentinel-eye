# Spec 174 — Plan

**Issue**: #2209 · **Branch**: `fix/2209-a-limit-the-build-can-see` · **Phase**: 2 (Plan)

**Spec**: `specs/174-a-limit-the-build-can-see/spec.md`

---

## Bounded context and layers

**None.** This change has no bounded context, no layer, no entity, no value
object, no invariant, no domain event, no integration event, no message, no
migration, no Aspire resource, no HTTP surface and no NetArchTest boundary
implication. It is build configuration and documentation.

The boundary rules of ADR-0027 and the `Shared.Contracts`-only rule are
**unaffected and untouched** — stated explicitly because the plan template asks
and the honest answer is "not applicable", not silence.

| Concern | This change |
|---|---|
| Cross-context project references | None added; NetArchTest untouched |
| `Shared.Contracts` | Untouched |
| Domain → integration event | None |
| Persistence / migrations | None |
| Aspire AppHost | Not touched. **AppHost is production code and therefore now in scope for the five rules** — see "AppHost is not exempt", below |

---

## The two-file mechanism, and why neither file is sufficient

SonarAnalyzer splits a parameterised rule across two inputs, and this repository
has neither today:

| Input | What it controls | File |
|---|---|---|
| `dotnet_diagnostic.<ID>.severity` | **Whether the rule runs**, and at what severity | `.editorconfig` |
| `<Rule><Key>…<Parameters>` | **The threshold value** the rule compares against | `SonarLint.xml`, wired as an `AdditionalFiles` item |

So:

- **S107 and S134 are off in the analyzer itself.** Without the `.editorconfig`
  severity line they will not fire *at any threshold*, and a `SonarLint.xml`
  entry for them is inert. This is proved, not assumed — `Event.Ingest`'s 8
  parameters exceed S107's own default of 7 and produce nothing today.
- **S104, S138 and S1541 are on**, at 1000 / 80 / 10. Without `SonarLint.xml`
  they stay at those numbers, two of which are far looser than ADR-0084's.

**Neither file alone delivers FR-1.** A reviewer reading only one of them will
conclude the change is half-done; both files therefore carry a comment pointing
at the other.

---

## Designed content — `.editorconfig`

**Placement: immediately after the existing `[*.cs]` block, *before* the first
`[**/Migrations/*.cs]` section.** Ordering is load-bearing in EditorConfig: later
sections win on overlapping keys, and the Migrations sections must stay later so
their `generated_code = true` keeps generated EF files out of S104 (AS-5). No
key here collides with `[src/AppHost/**.cs]`.

Rule IDs are listed in **ADR-0084's table order** (S104, S138, S107, S1541,
S134), which is also the order of the existing test `NoWarn` list in
`Directory.Build.props` — so the two lists diff against each other by eye.

```ini
# ADR-0084 (as amended 2026-09-17, spec 174 / #2209): the five code-metric
# limits are ADVISORY — `warning`, never `error`. Until this change they were
# configured nowhere and fired on nothing, while the ADR, CLAUDE.md and two
# agent briefs all called them CI-enforced.
#
# Severity here, thresholds in SonarLint.xml, and NEITHER FILE IS SUFFICIENT
# ALONE: S107 and S134 are disabled in the analyzer itself, so these lines are
# what switch them on at all; S104, S138 and S1541 are on already at 1000 / 80 /
# 10, so it is SonarLint.xml that tightens them to ADR-0084's numbers.
#
# Scoped to src/ deliberately, NOT [*.cs]. EditorConfig severity OUTRANKS a
# project's NoWarn, so a blanket section would re-enable all five inside tests/
# and silently revoke the test exemption that ADR-0084 grants and that
# Directory.Build.props still believes it has. tests/ gets the opposite
# instruction below, in the file that wins.
[src/**.cs]
dotnet_diagnostic.S104.severity = warning
dotnet_diagnostic.S138.severity = warning
dotnet_diagnostic.S107.severity = warning
dotnet_diagnostic.S1541.severity = warning
dotnet_diagnostic.S134.severity = warning

# ADR-0084: "Test projects exempt — long, narrative test methods are valuable."
# Restated here, not only as NoWarn in Directory.Build.props, because the NoWarn
# is not the deciding input: editorconfig severity beats it. The exemption has to
# be written where it wins.
[tests/**.cs]
dotnet_diagnostic.S104.severity = none
dotnet_diagnostic.S138.severity = none
dotnet_diagnostic.S107.severity = none
dotnet_diagnostic.S1541.severity = none
dotnet_diagnostic.S134.severity = none
```

The `[src/**.cs]` glob shape is not invented here — `[src/AppHost/**.cs]` already
exists in this file and demonstrably works.

---

## Designed content — `SonarLint.xml` (new, repository root)

**The filename must be exactly `SonarLint.xml`** — SonarAnalyzer matches the
additional file by name, the same way `BannedApiAnalyzers` matches
`BannedSymbols.txt` by prefix (a constraint `Directory.Build.props` already
documents for that analyzer). Root placement mirrors the root `BannedSymbols.txt`.

```xml
<?xml version="1.0" encoding="utf-8"?>
<!--
  ADR-0084's five code-metric thresholds. THRESHOLDS ONLY: whether a rule runs
  at all is set in .editorconfig (dotnet_diagnostic.<ID>.severity), and neither
  file does the job alone. Wired in as an AdditionalFile for non-test projects
  by Directory.Build.props; SonarAnalyzer matches this file by its exact name.

  All five are written out, including S1541 and S134 whose values already equal
  SonarAnalyzer 10.33.0.1635's own defaults. Two reasons:

    - A partial statement of a rule is how this repository's records drift. §II
      was summarised by three examples while 35 violations accumulated, then
      enumerated as nine types that omitted `short`. A file that lists three of
      ADR-0084's five limits invites the same reading.
    - A default belongs to the analyzer, which is upgraded. An unpinned
      threshold would let a SonarAnalyzer release move a documented limit
      without a diff in this repository.

  The cost is eight lines that change nothing today. Phase 4 confirms they
  change nothing today, by measuring S1541 and S134 at the counts they had
  before this file existed.
-->
<AnalysisInput>
  <Rules>
    <Rule>
      <Key>S104</Key>
      <Parameters>
        <Parameter>
          <Key>maximumFileLocThreshold</Key>
          <Value>300</Value>
        </Parameter>
      </Parameters>
    </Rule>
    <Rule>
      <Key>S138</Key>
      <Parameters>
        <Parameter>
          <Key>max</Key>
          <Value>30</Value>
        </Parameter>
      </Parameters>
    </Rule>
    <Rule>
      <Key>S107</Key>
      <Parameters>
        <Parameter>
          <Key>max</Key>
          <Value>4</Value>
        </Parameter>
      </Parameters>
    </Rule>
    <Rule>
      <Key>S1541</Key>
      <Parameters>
        <Parameter>
          <Key>maximumFunctionComplexityThreshold</Key>
          <Value>10</Value>
        </Parameter>
      </Parameters>
    </Rule>
    <Rule>
      <Key>S134</Key>
      <Parameters>
        <Parameter>
          <Key>maximumNestingLevel</Key>
          <Value>3</Value>
        </Parameter>
      </Parameters>
    </Rule>
  </Rules>
</AnalysisInput>
```

### The S1541 / S134 question, answered

The brief asked whether the two rules already sitting at ADR-0084's number need
an explicit entry. **They get one.** The reasoning is in the comment above and
rests on two things this repository has already been bitten by: a partial
statement of a rule drifts into a wrong one, and an analyzer default is not ours
to rely on across upgrades. The alternative — omit them and rely on the default —
is cheaper by eight lines and leaves the file an incomplete record of the very
decision it exists to implement. **The cost of being wrong is asymmetric**, which
is the same argument CLAUDE.md makes for passing `--base develop`.

It is also *verifiable that they are inert*: their measured counts must not move.
Phase 4 records S1541 = 7 and S134 = 2 (or reports otherwise), which is a real
check that the file is being read as a threshold source and not, say, being
ignored entirely.

---

## Designed content — `Directory.Build.props`

Three edits, all in the file's existing idiom.

### 1. Wire the additional file (new `ItemGroup`, next to the two existing ones)

```xml
  <!-- ADR-0084: the five code-metric thresholds. SonarAnalyzer reads rule
       parameters from an AdditionalFile named exactly SonarLint.xml; the
       severities live in .editorconfig, and neither input works without the
       other. Non-test projects only — ADR-0084 exempts tests, and that
       exemption is ALSO spelled out in .editorconfig, because editorconfig
       severity outranks the NoWarn below. -->
  <ItemGroup Condition="'$(IsTestProject)' != 'true'">
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)SonarLint.xml" />
  </ItemGroup>
```

### 2. The Release carve-out (inside the existing production `PropertyGroup`)

```xml
    <!-- ADR-0084 (as amended 2026-09-17): the five metric limits are ADVISORY.
         TreatWarningsAsErrors is on in Release above, and CI builds Release, so
         severity `warning` alone would turn the recorded baseline into that many
         build ERRORS on the first push. This carve-out is how "warning, not
         error" is expressed under ADR-0034's Release policy.

         It is PERMANENT, not a migration aid. Deleting it does not tighten the
         limits; it breaks the build. If these limits are ever to become blocking,
         that is an ADR-0084 amendment plus a cleanup of the baseline, not the
         removal of this line. -->
    <WarningsNotAsErrors>$(WarningsNotAsErrors);S104;S138;S107;S1541;S134</WarningsNotAsErrors>
```

The `$(WarningsNotAsErrors);` prefix mirrors the `$(NoWarn);…` idiom already used
twice in this file.

### 3. Correct the two false comments in this file

- The `ItemGroup` header `<!-- ADR-0084: code-metric limits enforced via
  SonarAnalyzer. Looser in tests. -->` — "enforced" is the untrue word, and this
  `ItemGroup` is the `PackageReference` block, which configures nothing.
- The production `PropertyGroup`'s `<!-- Production code: hard limits per
  ADR-0084. -->` and its `S104 file too long, S138 …` line, which read as though
  the block sets limits. It sets none; it sets unrelated suppressions.
- The test `PropertyGroup`'s `<!-- Tests: relax the metric rules … -->`, which
  reads as relaxing rules that were on. It should say the `NoWarn` is the
  belt to `.editorconfig`'s braces, and that the editorconfig entry is the one
  that decides.

---

## AppHost is not exempt, and that is deliberate

`src/AppHost` is production code by `IsTestProject`, so the five rules now apply
to it. ADR-0084 grants it no exemption, and this plan invents none — inventing
one would be deciding something the owner did not decide. `.editorconfig` already
relaxes *braces* for AppHost (ADR-0105's composition-root carve-out); metrics are
a separate question and stay in the baseline.

If AppHost's `Program.cs` turns out to dominate the S104 count, that is a finding
to record in the ADR's baseline note — **not** a reason to add a carve-out in
this spec.

---

## Designed content — the ADR-0084 amendment

Two corrections to the existing body (the false sentence, the false bullet),
plus an appended amendment block. **The existing "Decision" table of five limits
and their values does not change** — the numbers were never the defect.

### Correction A — the Decision preamble

Replace:

> Adopt the same limits via **SonarAnalyzer.CSharp**, configured in
> `Directory.Build.props`:

with:

> Adopt the same limits via **SonarAnalyzer.CSharp**. Severities are set in
> `.editorconfig` (scoped to `src/`), thresholds in the root `SonarLint.xml`
> wired in as an `AdditionalFiles` item by `Directory.Build.props`. See the
> **2026-09-17 amendment** below: from 2026-05-25 until that date this sentence
> read "configured in `Directory.Build.props`" and **no threshold was configured
> anywhere** — the limits fired on nothing for the whole period.

### Correction B — the enforcement bullet

Replace:

> - All rules are warnings by default; `TreatWarningsAsErrors=true` in
>   Release config makes them PR blockers via ADR-0033 CI.

with:

> - **The five limits are advisory, not blocking** (amended 2026-09-17). They are
>   configured at `warning` and carved out of Release's
>   `TreatWarningsAsErrors` by an explicit `WarningsNotAsErrors`. They appear in
>   the build log and in the IDE; they do not fail CI. This is a deliberate
>   choice given the baseline recorded below, and reversing it means clearing
>   that baseline first, not deleting the carve-out.

### The appended amendment block

```markdown
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
| S104 | ≤ 300 LOC/file | _<measured>_ |
| S138 | ≤ 30 LOC/method | _<measured>_ |
| S107 | ≤ 4 parameters | _<measured>_ |
| S1541 | cyclomatic complexity ≤ 10 | _<measured>_ |
| S134 | nesting depth ≤ 3 | _<measured>_ |
| **Total** | | **_<measured>_** |

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
```

---

## Documentation corrections — the full sweep

CLAUDE.md warns that *"a partial summary is how §II drifted twice"*. The sweep
below is therefore exhaustive by construction: every match of `0084`, `300 LOC`,
`code.metric`, `cyclomatic` and `nesting` across `*.md`, `.claude/`, `*.props`
and `*.editorconfig` was enumerated, and each one is either corrected or listed
as deliberately unchanged.

| # | File:line | Current text (abridged) | Disposition |
|---|---|---|---|
| 1 | `docs/adr/0084-…:14` | "configured in `Directory.Build.props`" | **Correct** — Correction A |
| 2 | `docs/adr/0084-…:26-27` | "…makes them PR blockers via ADR-0033 CI" | **Correct** — Correction B |
| 3 | `docs/adr/0084-…` (end) | — | **Append** the amendment block |
| 4 | `CLAUDE.md:489` | `\| Code metrics \| Max 300 LOC/file, … \| 0084 \|` | **Correct** — see below |
| 5 | `CONTRIBUTING.md:213` | "**Code metric limits** enforced by SonarAnalyzer (ADR-0084)" | **Correct** — "advisory (warning, not build-failing)" |
| 6 | `CONTRIBUTING.md:215` | "Max LOC per method: **30** backend / **50** frontend (S138)" | **Correct** — the frontend 50 is ESLint's, not S138's, and is not configured |
| 7 | `CONTRIBUTING.md:207-208` | "Warnings are errors in `Release` builds." | **Correct** — add the five-rule exception; the sentence is otherwise still true |
| 8 | `.claude/agents/backend-engineer.md:18` | "**Quality gates (CI-enforced):** … SonarAnalyzer limits …" | **Correct** — move the five out of the CI-enforced list and label them advisory |
| 9 | `.claude/agents/backend-reviewer.md:15` | "**Quality:** … SonarAnalyzer limits …" | **Correct** — same |
| 10 | `docs/design/scenario-simulator-m2.md:533` | "House ADRs that bind here: … ≤300 LOC / ≤30 LOC method (ADR-0084)" | **Correct** — "advisory" |
| 11 | `Directory.Build.props` (3 comments) | "enforced via SonarAnalyzer", "hard limits per ADR-0084", "Tests: relax the metric rules" | **Correct** — edit 3 above |
| 12 | `docs/adr/0034-code-style.md:3` | "extended by … ADR-0084: SonarAnalyzer metrics" | **Unchanged** — a pointer, not a claim about enforcement |
| 13 | `docs/adr/0091-no-shortcuts.md:66` | "hard limits in ADR-0084 keep individual lines bounded" | **Unchanged** — reasoning about why long names are affordable; not an enforcement claim. Listed so the sweep is complete |
| 14 | `docs/adr/0112-multi-tile-layouts.md:12` | "ADR-0084 (code metrics)" | **Unchanged** — a citation |
| 15 | `.specify/memory/constitution.md` | *(no match)* | **Nothing to correct.** Verified: the constitution never mentions ADR-0084 or these limits. **The constitution is not touched by this spec** |

### The CLAUDE.md row, designed

Replace line 489 with:

```markdown
| Code metrics | **Advisory, not enforced** — `warning`, carved out of Release's `TreatWarningsAsErrors`: 300 LOC/file, 30 LOC/method, 4 params, complexity ≤ 10, depth ≤ 3 (SonarAnalyzer). Configured nowhere until 2026-09-17; baseline and re-measurement command in the ADR | 0084 |
```

This mirrors the `Option<T>` row's honesty — it says *advisory*, says when the
gap was closed, and points at where the number lives rather than restating it
(a restated count is the second failure mode §II demonstrated).

**No new CLAUDE.md prose section is added.** The `Option<T>` rule earns a bullet
in §House rules because it is a *coding* rule an engineer applies by hand; these
five are reported by the build, and a bullet restating five numbers is exactly
the partial summary that drifts. The stack-table row plus the ADR is the whole
record.

---

## What phase 4 must not do

- **Not fix a single violation.** `git diff --stat` must show no `.cs` change.
- **Not add a suppression** for any of the five, anywhere, for any file —
  including AppHost.
- **Not touch `.specify/memory/constitution.md`.** It says nothing about these
  limits; an edit there would be inventing a claim.
- **Not touch S3776**, or any rule outside the five.
- **Not fix the two `CONTRIBUTING.md` `var` / `dotnet format` defects.** File
  them; leave them.
- **Not weaken a gate to reach green.** If the design does not work, stop
  (SC-7).
