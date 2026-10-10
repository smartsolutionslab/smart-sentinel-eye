# Spec 334 — The naming rule the build never read

**Issue:** [#2764](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2764)
— *Decide whether to add an analyzer/naming rule for the leading-underscore private-field
convention*. Labels `tech-debt`, `agent:ready`; Project #13, status **Todo** (verified by GraphQL
2026-10-10).
**Branch:** `fix/2764-leading-underscore-analyzer` (cut from `origin/develop` @ `b97afeaa`)
**Created:** 2026-10-10 · **Lane:** autonomous (ADR-0144)
**ADRs:** 0034 (code style enforcement; `TreatWarningsAsErrors` in Release), 0084 (as amended by
spec 174 — the precedent for an *advisory* rule: `warning` + a `WarningsNotAsErrors` carve-out),
0091 (naming), 0036 (smallest change), 0139 (§Testing — red for new behaviour, characterisation
for refactors), 0087 (each commit builds on its own), 0144 (lane).
**House rule enforced:** CLAUDE.md "Private fields carry no leading underscore — `gate`, not
`_gate`".
**Decision implemented (owner, 2026-10-08, on the issue):** *add an analyzer/naming rule for the
leading-underscore private-field convention, as a warning (advisory, not build-breaking).* This
spec transcribes that decision; it does not re-open whether to enforce or at what severity.
**New ADR:** no (see §7).

**Spec number.** `origin/develop` tops out at **329**. Remote branches claim **330**
(`the-scope-the-broker-never-read`) and **331** (`the-probe-that-reports-nothing-changed`); the
local worktree `sse-2297` holds an uncommitted **333** (`the-lane-the-flag-was-never-named-for`),
which skipped 332 for a reason not recorded anywhere visible. **332 is left unclaimed on purpose**
— a gap costs nothing, a collision costs a rename. Re-check before opening the PR (memory: *spec
number: origin/develop isn't enough*).

## 0. Scope

- **In:** make the existing `.editorconfig` private-field naming rule actually report in
  `dotnet build` (including CI's Release build) as an advisory **warning**; narrow the rule so it
  claims only what the house rule claims; bring the tree to zero hits so the first warning anyone
  sees is a real regression; update the two documents that describe the rule.
- **Out:** a `dotnet format` CI gate (#2175); the two `error WHITESPACE` findings in #2175; any
  custom Roslyn analyzer; any other naming rule; any change to severity of other rules.

## 1. The premise, re-checked on this tree

The issue says "nothing enforces it". That is **half right, and the wrong half is the important
one**: the rule is already written down, and the build ignores it.

### 1.1 The rule already exists in `.editorconfig`

`.editorconfig:108-118` (`origin/develop` @ `b97afeaa`), introduced in `9b3a0f17`
("style: allow var, require collection expressions, drop the field underscore"):

```ini
dotnet_naming_rule.private_fields_are_camel_case.severity = warning
dotnet_naming_rule.private_fields_are_camel_case.symbols = private_fields
dotnet_naming_rule.private_fields_are_camel_case.style = camel_case_no_prefix
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.camel_case_no_prefix.capitalization = camel_case
```

`Directory.Build.props:16` sets `EnforceCodeStyleInBuild=true`. So the configuration *looks*
enforced. It is not.

### 1.2 The build does not report IDE1006 at all — measured

A naming rule's own `dotnet_naming_rule.<name>.severity` is honoured by the IDE and by
`dotnet format`, but **not by the compiler during `dotnet build`**. The build reports IDE1006 only
when the diagnostic itself is given a severity: `dotnet_diagnostic.IDE1006.severity = warning`.

Measured in a scratch project carrying this repository's `[*.cs]` section verbatim, SDK 10.0.401
(the `global.json` pin), with a `private readonly int _underscored`:

| `.editorconfig` | `dotnet build` IDE1006 |
|---|---|
| as on develop | **0** (only CS0649/IDE0044 on an unrelated probe field) |
| + `dotnet_diagnostic.IDE1006.severity = warning` | `_underscored` **and** every PascalCase `const` / `static readonly` |

This is the mechanism behind **#2174**'s "build and `dotnet format` disagree — 252 vs 0": the two
tools read different keys.

### 1.3 The rule as written is wider than the house rule — measured

`applicable_kinds = field` with no `required_modifiers` claims every private `const` and
`private static readonly`, which this repository spells in PascalCase. On develop,
`dotnet format style tests/Architecture.Tests/… --verify-no-changes --diagnostics IDE1006` reports
**350** IDE1006, **all** of the form "The first word, '…', must begin with a lower case character"
— not one is an underscore. Turning the build on with the rule as written would bury the signal
this spec exists to create under hundreds of false positives.

### 1.4 With the rule narrowed, the whole solution has exactly two hits — measured

Temporary `.editorconfig` = develop + `dotnet_diagnostic.IDE1006.severity = warning` + two
higher-specificity rules (`const` → PascalCase; `static readonly` → PascalCase), full
`dotnet build SmartSentinelEye.slnx -c Debug` on a fresh worktree (every project compiled, exit 0,
0 errors):

| File | Hit |
|---|---|
| `src/Automation/Infrastructure/Cache/InMemoryRuleCache.cs:33` | `_byTrigger` — "Prefix '_' is not expected" (5 references in the file). Recorded for this issue by spec 312 §1.3 |
| `src/AppHost/StackStatusReport.cs:34` | `private const string notReported` — "must begin with upper case" (3 references) |

Nothing under `tests/` — #2556 / spec 312 (closed 2026-10-07) cleaned all 114. The interface rule
(`begins_with_i`), which also becomes build-visible, has **0** hits. The same narrowed config takes
`dotnet format`'s IDE1006 on `Architecture.Tests` from **350 to 0** (exit 0).

### 1.5 Release would turn the rule into an error

`TreatWarningsAsErrors` is on in Release (`Directory.Build.props:17`) and CI builds Release. A
`warning` alone would fail CI on the two hits above and on every future one — the opposite of the
owner's "advisory, not build-breaking". ADR-0084's carve-out (`WarningsNotAsErrors`) is the
repository's established way to say "warning, not error" under that policy. Today it exists only
in the **production** `PropertyGroup` (`Directory.Build.props:100`); the private-field rule has no
test exemption (#2556 cleaned tests/ for exactly that reason), so its carve-out must bind **all**
projects.

### 1.6 #2556 sequencing — resolved

#2556 is **closed (completed) 2026-10-07**; its sweep is on develop. There is no pre-existing flood
to sequence around: the measured residue is the two `src/` hits in §1.4, which this spec fixes.

## 2. Assumption made explicit

**[ASSUMPTION A1] Narrowing the rule (§1.3) is implementation, not a new policy.** The owner
decided to enforce "no leading underscore on private fields". The rule as written also demands
camelCase `const`s, which contradicts how the repository (and C#) spells every one of them; the
measurement shows a single `const` and no `static readonly` written camelCase. Adding two
higher-specificity PascalCase rules makes the rule say what the house rule says. This overlaps
**#2174** (which asks exactly this naming question) — after merge, #2174's naming half is resolved
and should be closed or narrowed to whatever remains; #2175 (the format gate) is unblocked for its
naming half. The alternative — turn the build on with the rule unnarrowed — is rejected: hundreds
of advisory warnings no one reads are the same as no rule.

Scope of A1 is deliberately minimal: `private const` and `private static readonly` only. Plain
`private static` (mutable) fields stay under the camelCase rule (0 hits measured).

## 3. User story

**US1 (P1)** — As a maintainer, when anyone adds a private field named `_x` (in `src/` or
`tests/`), `dotnet build` — locally and in CI's Release build — prints an IDE1006 warning naming
the field, and the build still succeeds; so the convention cannot silently drift back, without
the rule ever blocking a merge.

### Acceptance scenarios

```gherkin
Scenario: a leading-underscore private field warns in Release (happy path)
  Given a production project containing "private readonly int _probeValue"
  When it is built with "dotnet build -c Release"
  Then the output contains "warning IDE1006" naming "_probeValue"
  And the exit code is 0

Scenario: the same holds in a test project (no test exemption)
  Given a test project containing "private readonly int _probeValue"
  When it is built with "dotnet build -c Release"
  Then the output contains "warning IDE1006" naming "_probeValue"
  And the exit code is 0

Scenario: PascalCase constants and static readonly fields stay silent (the false-positive / conflict case)
  Given "private const int ProbeLimit" and "private static readonly int ProbeShared" in the same file
  When the project is built
  Then no IDE1006 names either of them

Scenario: before this change the rule is invisible to the build (the red)
  Given the same probe on the unchanged tree
  When the project is built in Release
  Then no IDE1006 is reported at all

Scenario: the tree starts at zero (bad-input residue)
  Given the branch after this change
  When "dotnet build SmartSentinelEye.slnx -c Release" runs
  Then it succeeds with zero IDE1006 warnings
```

Auth: N/A — build configuration; no endpoint, no scope, no fab.

## 4. Independent end-to-end procedure

1. On the unchanged tree, add the transient probe (tasks T001/T002) to `src/Shared.Kernel` and
   `tests/Shared.Kernel.Tests`; `dotnet build <project> -c Release --no-incremental` each — **zero**
   IDE1006 (red).
2. Apply the configuration change.
3. Rebuild both — IDE1006 on `_probeValue` only, none on `ProbeLimit` / `ProbeShared`, exit 0.
4. Delete the probes; rename the two residual fields.
5. `dotnet build SmartSentinelEye.slnx -c Release` — exit 0, `grep -c IDE1006` = 0.
6. Informational (not a gate — #2175's territory): `dotnet format style
   tests/Architecture.Tests/… --verify-no-changes --diagnostics IDE1006` → 0 (was 350).

## 5. Success criteria

- **SC-1** The probe's `_probeValue` produces IDE1006 in a Release build of a production project
  and of a test project.
- **SC-2** Both builds exit 0 (proves the `WarningsNotAsErrors` carve-out).
- **SC-3** No IDE1006 on PascalCase `const` / `static readonly` (proves the narrowing).
- **SC-4** Full-solution Release build: 0 IDE1006, exit 0.
- **SC-5** No probe file is committed.
- **SC-6** Existing suites covering the two renamed files pass unmodified.

## 6. Locked choices

- **Mechanism:** `.editorconfig` naming rules + `dotnet_diagnostic.IDE1006.severity`, read by the
  built-in Roslyn code-style analyzer under the existing `EnforceCodeStyleInBuild=true`. No custom
  analyzer, no new package.
- **Severity:** `warning`, carved out of Release's `TreatWarningsAsErrors` via
  `WarningsNotAsErrors` for every project (ADR-0084 pattern).
- **Evidence:** a build counterfactual on the compiler's output (spec 174's method), **not** an
  architecture test that greps `.editorconfig` — such a test proves the design was written down,
  not that it holds, and would check its own input.

## 7. ADR

**None needed.** The convention exists (CLAUDE.md; `.editorconfig` since `9b3a0f17`); the
enforcement and its severity were decided by the owner on the issue; the advisory mechanism is
ADR-0084's established pattern. The decision is recorded where the convention is documented
(CLAUDE.md house rule, CONTRIBUTING.md code-style section) with a pointer to #2764. ADR-0034 is
silent on naming rules and is not amended.

## 8. Latency budget

**N/A** — build configuration and two private renames off the event→overlay path. (The
`InMemoryRuleCache` rename touches a type on the 200 ms event→overlay-state leg, but a private
field rename changes no executed code.)
