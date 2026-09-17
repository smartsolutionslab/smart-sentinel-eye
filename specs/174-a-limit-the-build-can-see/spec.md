# Spec 174 — A limit the build can see

**Issue**: #2209 · **Branch**: `fix/2209-a-limit-the-build-can-see` · **Phase**: 1 (Specify)

**ADRs**: **ADR-0084** (the code-metric limits — *amended by this spec*), ADR-0034
(analyzers enforced; warnings are errors in Release — the property this change has
to work around), ADR-0064 (line length 160; the other half of the style budget),
ADR-0033 (CI is the gate), ADR-0037 (phased workflow), ADR-0144 (autonomous lane;
phase-4a colour), ADR-0036 (smallest possible change; no speculative generality).

**A new ADR is not needed. ADR-0084 is amended, and that amendment is explicitly
authorized.** The repository owner made the decision in this session, choosing
ending **2** of the three the issue offers: configure all five rules at
`warning`, and amend ADR-0084 to record that the limits are **advisory** and to
record the measured baseline. Phase 4 is *implementing* a decision, not making
one. **Nothing downstream should treat the ADR edit or the CLAUDE.md edits as the
lane overstepping ADR-0144's "may not write an ADR" rule** — that rule bars the
lane from *deciding*; the decision is already made and recorded here.

Phase 4 still stops and reports if it discovers the chosen ending cannot be
implemented as designed (see **SC-7**).

---

## Why

ADR-0084 says five code-metric limits are *"configured in
`Directory.Build.props`"*. They are not configured anywhere:

| Where a threshold could live | State on `develop` @ `934c1d10` |
|---|---|
| `Directory.Build.props` | Names the five rule IDs **in a comment only** (`<!-- S104 file too long, S138 method too long, … -->`). No severity, no threshold. |
| `.editorconfig` | No `dotnet_diagnostic.S104/S107/S138/S1541/S134` key anywhere. |
| A ruleset / `SonarLint.xml` | **No such file exists in the repository** (`find . -name SonarLint.xml` → nothing). |
| Tests' `NoWarn` | `S104;S138;S107;S1541;S134` **are** listed — suppressing five rules that were never switched on. |

So the only trace of ADR-0084 in the build is the suppression of rules that do
not fire, and `Directory.Build.props`'s comment header — `ADR-0084: code-metric
limits enforced via SonarAnalyzer` — is false in its operative word.

**"Does this change pass ADR-0084?" is not answerable by the build, for every PR
in this repository.** Reviewers have been citing the limits as build-enforced;
phase 6 of spec 103 checked a method against the 30-line limit on that assumption
before finding the limit does not fire.

This is the repo's recurring defect shape — **a record nobody checked against
what was actually happening**. §II drifted twice, §IV recorded a leg as unbuilt
after it was built, the Phase 3 board gate was documented and ignored for sixteen
specs. This is that, in the analyzer configuration.

---

## Premise check against `develop` (`934c1d10`)

Every claim below was re-verified on this branch's base. The violation counts
come from a prior read-only investigation against the pinned analyzer
(**SonarAnalyzer.CSharp 10.33.0.1635**, `Directory.Packages.props:10`); they are
carried forward as the *expected* figure and **re-measured in phase 4**, not
asserted (see FR-5).

| Claim | State | Evidence |
|---|---|---|
| ADR-0084 claims `Directory.Build.props` configuration | **Holds** | `docs/adr/0084-code-metrics-sonaranalyzer.md:14` — "configured in `Directory.Build.props`" |
| No threshold is configured anywhere | **Holds** | table above |
| `Event.Ingest` takes 8 parameters, unsuppressed, unwarned | **Holds — re-read, counted** | `src/EventIngestion/Domain/Event/Event.cs:38-47`: `identifier, fab, source, device, kind, occurredAt, payload, clock` = **8** |
| `TreatWarningsAsErrors` is on in Release | **Holds** | `Directory.Build.props:19` — `Condition="'$(Configuration)' == 'Release'"` |
| CI builds Release | **Holds** | `.github/workflows/ci.yml:52`, `:192`, `:446` — `dotnet build … -c Release` |
| S107 is **not** enabled by default | **Holds — proved, not assumed** | Its default threshold is 7. `Event.Ingest` has 8. The Release build on `develop` is green with zero S107. A rule enabled at 7 would fire on 8. Therefore it is off. |
| S134 is **not** enabled by default | **Holds — same proof** | Its default threshold is 3, which *is* ADR-0084's number, and 2 violations exist. A rule enabled at 3 would fire on them. The green Release build proves it is off. |
| Test projects are meant to stay exempt | **Holds** | `Directory.Build.props` — `<PropertyGroup Condition="'$(IsTestProject)' == 'true'"><NoWarn>…S104;S138;S107;S1541;S134…` and ADR-0084 "Test projects exempt" |
| Production C# lives only under `src/`, tests only under `tests/` | **Holds** | 43 production `.csproj` under `src/`, 29 test `.csproj` under `tests/`, none elsewhere |
| `#2209` is on Project #13 | **Holds** | `gh issue view 2209` → `projects: Smart Sentinel Eye (Todo)`; no `item-add` needed |

### Expected violation counts (to be re-measured, not trusted)

| ADR-0084 limit | Rule | Analyzer default | ADR threshold | Parameter key | On by default? | Expected hits |
|---|---|---|---|---|---|---|
| ≤ 300 LOC/file | **S104** | 1000 | 300 | `maximumFileLocThreshold` | yes | 5 |
| ≤ 4 parameters | **S107** | 7 | 4 | `max` | **no** | 84 |
| ≤ 30 LOC/method | **S138** | 80 | 30 | `max` | yes | 116 |
| Cyclomatic complexity ≤ 10 | **S1541** | 10 | 10 (= default) | `maximumFunctionComplexityThreshold` | yes | 7 |
| Nesting depth ≤ 3 | **S134** | 3 | 3 (= default) | `maximumNestingLevel` | **no** | 2 |

**Expected total: 214**, production code only.

**S3776 (Cognitive Complexity) is a different rule from S1541 (Cyclomatic
Complexity) and is not one of ADR-0084's five.** It is already active via
`AnalysisMode=Recommended` at its own unrelated default and is **out of scope** —
do not fold it in, do not change it, do not count it.

---

## The chosen ending, and what "advisory" has to mean mechanically

`warning` alone does **not** produce an advisory rule in this repository, because
`TreatWarningsAsErrors` is on in Release and CI builds Release. Switching the
five on at `warning` with nothing else would turn ~214 warnings into ~214 **build
errors** and CI would go red on `develop` at the first push.

So "advisory" requires a second, deliberate, **permanent** piece:
`WarningsNotAsErrors` naming exactly these five rule IDs. That is not a
workaround for a temporary state — it is the mechanism by which the owner's
decision ("warning, not error") is expressed under ADR-0034's Release policy, and
it must be commented as such so a later reader does not "tidy" it away and
discover 214 errors.

**`suggestion` was considered and rejected**: a `suggestion` diagnostic is
invisible in `dotnet build` output, so the limits would remain unanswerable by
the build — which is the whole defect. `warning` puts them in the build log and
in the IDE, where a reviewer and an engineer can both see them.

---

## User stories

### US-1 (P1) — An engineer can see, from the build, whether their code meets ADR-0084

**As** an engineer writing a method in `src/`,
**I want** the build to tell me when I cross one of ADR-0084's five limits,
**so that** "does this pass ADR-0084?" is a question the build answers rather
than a question a reviewer guesses at.

This is the whole slice. It ships independently: the rules fire, the build still
succeeds, the documentation says what is true. **No production `.cs` file is
changed and no violation is fixed** — the 214 are a recorded backlog, not this
spec's work.

### Out of scope (explicitly, so phase 4 does not drift into it)

- **Fixing any of the ~214 violations.** Not one. A metric fix is a refactor with
  its own characterisation obligation (constitution §Testing) and its own issue.
- **Adding any `[SuppressMessage]` or `#pragma`** for these rules.
- **Changing S3776**, or any rule outside the five.
- **The frontend half of ADR-0084** (ESLint `max-lines-per-function` 50). The
  issue, the measurement and the decision are all about the C# five. The ADR
  amendment states the frontend row's status honestly rather than silently
  extending the claim to it (FR-4c).
- **Two unrelated documentation defects found while checking** — recorded here so
  they are not lost, and **not fixed by this spec** (smallest possible change,
  ADR-0036):
  - `CONTRIBUTING.md:227-231` still says the house style is "explicit types
    (**never** `var`)" and that `.editorconfig` sets `csharp_style_var_* = false`.
    Both are false: the keys are `true:silent` and CLAUDE.md says "`var` is
    allowed anywhere."
  - `CONTRIBUTING.md:233-235` says "CI verifies with `--verify-no-changes`".
    There is no `dotnet format` step in any workflow (`grep -n 'dotnet format'
    .github/workflows/*.yml` returns nothing), and there is no `.husky/pre-commit`.
  - **Phase 4 must file these as one issue and must not fix them here.**

---

## Acceptance scenarios (Gherkin)

### AS-1 — Happy path: a non-compliant construct in production code now warns

```gherkin
Given a production file under src/ containing a method with 8 parameters
  and a method whose body is 48 lines
When dotnet build is run in Release over that project
Then the build output contains a warning S107 naming that file and method
  And the build output contains a warning S138 naming that file and method
```

### AS-2 — The counterfactual: the same construct was silent before

```gherkin
Given the same production file, on the base commit 934c1d10
  and the .editorconfig and SonarLint.xml changes are NOT applied
When dotnet build is run in Release over that project
Then the build output contains no S107 and no S138 warning for that file
  And the build succeeds
```

### AS-3 — Advisory, not blocking: Release still succeeds

```gherkin
Given the five rules are configured at warning
  and roughly 214 violations exist in src/
When dotnet build SmartSentinelEye.slnx -c Release is run
Then the process exit code is 0
  And no S104, S107, S138, S1541 or S134 diagnostic is reported as an error
```

### AS-4 — The test exemption survives (the conflict case)

`.editorconfig` severity **outranks** a project's `NoWarn`. A naive
`[*.cs]` section would therefore re-enable all five inside `tests/`, silently
revoking ADR-0084's test exemption — which is the opposite of the decision.

```gherkin
Given a test file under tests/ containing a method with 8 parameters
  and a method whose body is 48 lines
When dotnet build is run in Release over that test project
Then the build output contains no S107 and no S138 warning for that file
```

### AS-5 — Bad input: generated code stays out of the count

```gherkin
Given EF Core migration files under src/**/Migrations/ exceeding 300 lines
When the measurement command of FR-5 is run
Then no S104 warning names a file under a Migrations/ directory
  And the total across the five rules is within a factor of two of 214
```

*(The second clause is a leak detector, not a precise assertion: if generated
code starts being analysed, the total jumps by an order of magnitude and the
figure recorded in the ADR would be a lie.)*

### AS-6 — Auth / permission: N/A

This change touches build configuration and documentation only. It adds no
endpoint, no scope, no token, no trust boundary. **No auth scenario applies.**

---

## Functional requirements

- **FR-1** — All five rules (S104, S107, S138, S1541, S134) are enabled at
  severity `warning` for C# under `src/`, and **explicitly not enabled** for C#
  under `tests/`, in `.editorconfig`, in this repository's existing section style.
- **FR-2** — All five thresholds are set to ADR-0084's numbers in a
  `SonarLint.xml` wired as `AdditionalFiles` for non-test projects, mirroring the
  existing `BannedSymbols.txt` scoping in `Directory.Build.props`. **All five are
  written explicitly, including the two that equal the analyzer default** — see
  plan.md for the reasoning.
- **FR-3** — `WarningsNotAsErrors` names exactly these five rule IDs, permanently
  and with a comment stating why, so the Release build stays green.
- **FR-4** — Every place this repository's documentation claims these limits are
  enforced is corrected to say **advisory**:
  - **(a)** `docs/adr/0084-code-metrics-sonaranalyzer.md` — the false
    `Directory.Build.props` sentence, the "PR blockers" bullet, and an amendment
    block recording *advisory*, the measured baseline, its date, and a
    reproduction command.
  - **(b)** `CLAUDE.md:489` — the stack-table row.
  - **(c)** `CONTRIBUTING.md:213` — "**Code metric limits** enforced by
    SonarAnalyzer"; and the frontend sub-bullet, whose ESLint status must be
    stated honestly rather than left implying parity.
  - **(d)** `.claude/agents/backend-engineer.md:18` and
    `.claude/agents/backend-reviewer.md:15` — both list the limits under
    "**Quality gates (CI-enforced)**". These are **load-bearing when no human is
    watching** (CLAUDE.md says so in as many words, citing the two briefs that
    said "NRT disabled" for eleven days after ADR-0141 enabled it).
  - **(e)** `docs/design/scenario-simulator-m2.md:533` — "House ADRs that bind
    here: … ≤300 LOC / ≤30 LOC method (ADR-0084)". Lower stakes; corrected for
    completeness because a partial sweep is how this class of drift returns.
  - **(f)** `Directory.Build.props`'s own comment header — `ADR-0084:
    code-metric limits enforced via SonarAnalyzer` — and the test `NoWarn`
    comment, which currently reads as relaxing rules that were on.
- **FR-5** — The ADR amendment carries a **reproduction command** that a later
  reader can run to re-measure the baseline the same way, mirroring in form the
  `Option<T>` baseline entry in CLAUDE.md. Phase 4 runs it and records **the
  number it observes**. If that number is not 214, the observed number is what
  goes in the ADR, and the discrepancy is reported — the figure is a measurement,
  not a target.
- **FR-6** — No `.cs` file under `src/` or `tests/` is modified. The probe files
  of the counterfactual are created and deleted within phase 4a and **never
  committed**.

---

## Independent end-to-end test procedure

Runnable by a reviewer with nothing but a clone. **The Aspire stack is not
needed** (confirmed: this change touches `.editorconfig`, one XML file,
`Directory.Build.props` and Markdown — no runtime resource, no service, no
container). The running stack at **pid 3312 must not be stopped**; a `dotnet
build` of the whole solution while it runs will fail with MSB3027 on locked
service binaries, so the reviewer **builds specific projects, not the solution**,
or runs the solution build after the stack is down.

1. `git checkout 934c1d10` (the base).
2. Create `src/Shared.Kernel/MetricProbe.cs` with an 8-parameter method and a
   48-line method (content in tasks.md, T001).
3. `dotnet build src/Shared.Kernel/SmartSentinelEye.Shared.Kernel.csproj -c Release`
   → **succeeds, zero S107, zero S138.** This is the defect, reproduced.
4. `git checkout fix/2209-a-limit-the-build-can-see` (keeping the probe).
5. Rebuild the same project → **S107 and S138 warnings naming `MetricProbe.cs`,
   exit code 0.**
6. Move the probe to `tests/Shared.Kernel.Tests/MetricProbe.cs`, rebuild that
   project → **no S107, no S138.** The exemption holds.
7. Delete both probes.
8. `grep -n "advisory" docs/adr/0084-code-metrics-sonaranalyzer.md CLAUDE.md` →
   both say advisory.

---

## Success criteria

- **SC-1** — A production method with 8 parameters produces S107; a 48-line
  production method produces S138. Both observed, both quoted in the PR.
- **SC-2** — The same two constructs produced **nothing** on the base commit.
  Observed and quoted, in the same PR, as the counterfactual pair.
- **SC-3** — `dotnet build SmartSentinelEye.slnx -c Release` exits **0** with the
  five rules on. CI green.
- **SC-4** — The same two constructs inside `tests/` produce nothing. Observed.
- **SC-5** — Every one of FR-4's locations reads *advisory*. Verified by `grep`,
  listed in the PR.
- **SC-6** — `git diff --stat` shows **no `.cs` file** changed and **no file
  added under `src/` or `tests/`**.
- **SC-7** — If any of SC-1..SC-4 cannot be achieved — in particular if
  `SonarLint.xml` parameters turn out not to be read, or if `WarningsNotAsErrors`
  does not neutralise `TreatWarningsAsErrors` for these IDs — phase 4 **stops and
  reports** with the verbatim output. It does not switch to `suggestion`, does not
  disable `TreatWarningsAsErrors`, and does not narrow the rule set to make the
  build pass. Each of those would be weakening a gate to reach green, which
  ADR-0144 forbids outright.

---

## Locked tech choices

| Concern | Choice | Source |
|---|---|---|
| Analyzer | SonarAnalyzer.CSharp **10.33.0.1635**, already pinned | `Directory.Packages.props:10`, ADR-0084 |
| Severity mechanism | `.editorconfig` `dotnet_diagnostic.<ID>.severity` | existing repo pattern (`RS0030`, `IDE0305`, `S2068`) |
| Threshold mechanism | `SonarLint.xml` as `AdditionalFiles` | the only mechanism SonarAnalyzer offers for rule parameters |
| Scoping to production | `Condition="'$(IsTestProject)' != 'true'"` in `Directory.Build.props` | existing `BannedSymbols.txt` pattern, same file |
| Release-error carve-out | `WarningsNotAsErrors` | the only per-rule carve-out MSBuild offers under `TreatWarningsAsErrors` |

---

## Latency budget impact

**N/A.** No leg of constitution §IV is touched. This change alters compile-time
diagnostics and documentation; it adds no runtime code, changes no runtime code,
and cannot move `event arrival → overlay rendered`. §VII's dashboard rule does not
attach.
