# Plan 185 — A filter that cannot select nothing

**Spec:** `specs/185-a-filter-that-cannot-select-nothing/spec.md`
**Issue:** [#2289](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2289)

---

## 1. Bounded context and layers

**None.** This feature touches no bounded context, no Domain, Application,
Infrastructure or Api layer, and no production assembly. It lives entirely in
the repository's own build and gate machinery:

| File | Role |
|---|---|
| `.github/workflows/ci.yml` | the gate that under-reports (ADR-0033) |
| `tests/Architecture.Tests/IntegrationTestSelectionTests.cs` | the guard that reads only half its subject |

No cross-context project reference is added, no `Shared.Contracts` message, no
value object, no aggregate. NetArchTest's boundary rules are unaffected because
nothing under `src/` moves.

---

## 2. US1 — the workflow change

### 2.1 What lands

Both filtered `dotnet test` invocations gain a final continuation line:

```yaml
            --blame-hang --blame-hang-dump-type mini --blame-hang-timeout 3min \
            -- RunConfiguration.TreatNoTestsAsError=true
```

`--` is load-bearing: everything after it is passed to VSTest as a run-setting
override. Without it the token is an unrecognised `dotnet test` argument. The
reader in §3 therefore checks for the separator as well as the setting — a
`RunConfiguration.…=true` that never reached VSTest is inert, silent, and
exactly the family of defect this spec closes.

### 2.2 The comment that goes with it

One short block above each, saying **why**, not what. The workflow's existing
comments are the house style here — they explain reasoning and cite issues. The
new one says: a filter that matches nothing exits 0, so this step's green would
otherwise mean "nothing was attempted" (#2289), and it names the measured exit
codes so the next reader does not have to re-derive them.

Not repeated twice in full. The cheap step carries the reasoning; the
integration step carries a one-line back-reference plus the honest note from
spec §1.5 that an exclusion filter is the less exposed of the two.

### 2.3 What deliberately does not change

- **No `--logger trx` added to the cheap step.** It would only exist to be
  parsed, and nothing parses it (spec §2).
- **No `--results-directory` override.** The upload globs at `ci.yml:294-322`
  are documented as having been got wrong once already, against real hang
  output; moving the results directory would move the `.dmp` and the
  `Sequence*.xml` with it.
- **No change to `timeout-minutes`, `needs:`, `continue-on-error`, or job
  names.** `AgentBriefClaimTests` asserts the agent briefs against exactly those
  properties, and this feature has no reason to disturb them.
- **No `permissions:` change and no new action.** Recorded because it is the
  discriminator for whether `security-reviewer` is warranted (`tasks.md` §6).

---

## 3. US2 — the workflow reader

### 3.1 Shape

A small line-oriented reader inside `IntegrationTestSelectionTests`, in the same
style as the three existing precedents (`AgentBriefClaimTests.WorkflowJobs()`,
`AppHostE2ESwitchTests`, `AppHostStackStatusTests`): **read the text, do not add
a YAML parser** (spec A-3, ADR-0036).

```csharp
private sealed record TestInvocation(int Line, string Command)
{
    public bool IsFiltered => …;   // the joined command contains --filter
    public bool FailsOnNoTests => …; // a standalone `--` followed by the setting
}
```

Construction, per `ci.yml` line:

1. A line whose trimmed text starts with `dotnet test` opens an invocation and
   records its **1-based** line number.
2. While the line just consumed ends with a trailing `\`, the next line is
   appended (trimmed) to the same invocation.
3. Otherwise the invocation closes.

Two projections:

- `Filter(invocation)` — the value of `--filter "…"`, by regex.
- `Categories(filter)` — every `Category` name the filter mentions, from
  `Category\s*!?=\s*(?<name>[A-Za-z0-9_]+)`. This is spelling-agnostic about
  `=` versus `!=` on purpose: both an inclusion and an exclusion are a claim
  that the name exists on the test side.

### 3.2 What the derived set replaces

`CategoryDeclaration` stops being a regex literal naming four categories and
becomes a regex **built at first use** from `Categories(...)` over every
filtered invocation in `ci.yml`:

```csharp
$@"^[ \t]*\[\s*Trait\(\s*""Category""\s*,\s*""({string.Join("|", names.Select(Regex.Escape))})""\s*\)\s*\]"
```

`Regex.Escape` is not decoration: the names come from a file outside this
assembly, and an unescaped one would silently widen the pattern.

`CheapStep` and `ExcludeStep` are **deleted**. Every message that cited them
cites the invocation's real, freshly-read line number instead — so the citation
cannot be stale, because there is nothing left to go stale (spec SC-007).

### 3.3 The four new facts

**F1 — `Every_filtered_test_step_in_the_workflow_fails_when_it_selects_nothing`.**
For each `IsFiltered` invocation, `FailsOnNoTests` must hold. The message names
the line number, the filter, and the exact flag to add. **This is the red**
(spec AS-3).

**F2 — `The_workflow_reader_finds_every_dotnet_test_invocation`.**
The reader's invocation count must equal a raw count of lines whose trimmed text
starts with `dotnet test`, and at least one invocation must be filtered. This is
the reader's own positive control and the answer to AS-6: a reader whose
continuation logic breaks reports fewer invocations than the file contains and
fails here, rather than silently finding nothing and letting F1 pass over an
empty set. It asserts **no fixed number** — nothing here goes stale when a step
is added.

**F3 — `Every_category_the_workflow_filters_on_is_declared_by_a_test_class`.**
Each derived name must appear on at least one scanned class. This is AS-4: a
category `ci.yml` names and no test carries selects nothing, and is caught at
build time. Its converse — a class declaring a name `ci.yml` does not filter on
— is already covered by the existing `Every_integration_test_class_declares_…`
fact, because such a class no longer matches the derived set (AS-5).

**F4 — `A_filter_the_workflow_does_not_contain_yields_no_categories`**, plus its
positive twin over synthetic workflow text. Feeds the reader a literal string
rather than the repository's file, in exactly the way the existing eight facts
feed `Describe(path, source)` synthetic sources. Covers:

- a filtered invocation split across continuation lines is read as one command;
- `RunConfiguration.TreatNoTestsAsError=true` **without** a preceding standalone
  `--` does not count as passing (§2.1);
- an unfiltered `dotnet test` is not asked for the flag;
- `Category!=X&Category!=Y` yields both `X` and `Y`.

### 3.4 What must not change

The eight existing facts keep their **assertions** verbatim:

1. `Every_integration_test_class_declares_where_it_runs`
2. `A_doc_comment_naming_the_collection_attribute_is_not_a_declaration`
3. `A_commented_out_declaration_is_not_a_declaration`
4. `A_string_literal_containing_block_comment_syntax_does_not_erase_the_population`
5. `Naming_the_fixture_in_code_is_not_a_declaration`
6. `A_misspelled_category_value_is_not_a_declaration`
7. `A_file_with_no_test_methods_is_not_asked_to_declare`
8. `Either_declaration_satisfies_the_guard`

They are the characterisation half of this change: the guard's verdict on the
real tree must be identical before and after, because only the *source* of the
category set moves, not the set itself. An assertion that has to be edited is
evidence the verdict moved — **block, do not adjust** (ADR-0144).

**One narrow exemption, stated so it is not mistaken for drift:** fact 6's and
`Explain`'s assertion *messages* quote `CheapStep`/`ExcludeStep`, which cease to
exist. Updating message text to cite the freshly-read line numbers is not an
assertion change. The `ShouldBeTrue` / `ShouldBeEmpty` calls and their subjects
stay exactly as they are.

---

## 4. Entities, value objects, invariants

None. No domain model is touched, so constitution §II does not engage and
`PrimitiveBoundaryTests` sees no new surface. The one record introduced
(`TestInvocation`) is test-local parsing state in `Architecture.Tests`, not a
domain type — the same category as `TestFile`, which this class already has, and
outside §II's stated scope.

---

## 5. Messaging

None. No domain event, no integration event, no `Shared.Contracts` change, no
Wolverine handler. Nothing here runs at runtime.

---

## 6. Boundary rules

- No cross-context project reference is created.
- `Architecture.Tests` continues to read `tests/Integration.Tests` **from disk**
  rather than referencing it. The existing class documents why: a project
  reference would drag the Aspire hosting and DCP graph into a project that runs
  in seconds with no Docker, defeating the very separation the guard exists to
  protect. Reading `ci.yml` from disk is the same move, one file further out.
- The reader resolves paths from `RepositoryRoot()` — the existing walk up to
  `SmartSentinelEye.slnx` — and normalises separators to `/`. Not optional:
  `Path.GetRelativePath` returns the platform separator, and a backslash literal
  in an expected string is green on Windows and red on Linux CI. This repository
  has been bitten by exactly that, and the existing class already carries the
  fix in `Relative(...)`.

---

## 7. Risks, and what mitigates each

| Risk | Mitigation |
|---|---|
| The flag behaves differently on the ubuntu runner | Spec A-1; phase 5 quotes the measured counterfactual, and the first post-merge CI run is read for a non-zero total |
| The reader finds nothing and F1 passes vacuously | F2 — count parity against a raw `dotnet test` line count, with no frozen number |
| The derived category set comes back empty and the built regex credits everything | F2 requires at least one filtered invocation; F3 requires every derived name to be declared, which an empty set satisfies vacuously — so F2's non-empty check is the load-bearing one and must be written as such |
| A future step uses a YAML folded block or flow scalar the reader cannot see | F2's parity check fails, because the raw `dotnet test` count still includes it |
| Someone "fixes" a red F1 by deleting the `--filter` | The filter is what selects the Docker-free slice; deleting it runs the whole suite without Docker and fails loudly. Recorded, not guarded |
| The two flags are added but the reader is never written | US1 and US2 ship in one PR; F1 is the red that gates 4b |

---

## 8. What this plan does not decide

- Whether a repository-wide `.runsettings` should eventually turn the flag on
  for every `dotnet test`. Out of scope (spec §3); if it is ever wanted, it is a
  separate issue with a separate justification.
- Whether the four category names are the right ones, or whether
  `Measurement`/`Disruptive`/`Maintenance` should still be excluded from CI.
  That is spec 020's decision and is untouched here.
- Whether the e2e job needs an analogous "did anything run" check. Playwright
  has its own mechanisms and no `--filter` appears in that job; not investigated,
  not claimed either way.
