# Spec 312 — The underscore the tests kept

**Issue:** [#2556](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2556)
— *Leading-underscore private fields across tests/*. Labels `tech-debt`, `agent:ready`;
Project #13, status **Todo** (verified 2026-10-07).
**Branch:** `chore/2556-underscore-private-fields-tests` (cut from `origin/develop` @ `3ebd6f4a`)
**Created:** 2026-10-07 · **Lane:** autonomous (ADR-0144)
**ADRs:** 0036 (smallest change; a refactor changes shape, not behaviour), 0091 (naming),
0139 (§Testing — behaviour-preserving changes are characterised, not red-first),
0109 (disjoint files), 0087 (each commit builds on its own), 0144 (lane).
**House rule applied:** CLAUDE.md "Private fields carry no leading underscore — `gate`, not
`_gate` … assign with `this.value = value;`" (`this.` permitted for exactly this).
**New ADR:** no. The rule already exists; this spec only applies it.

**Spec number.** `origin/develop` tops out at **310**; open PR #2760 claims **311**
(`specs/311-a-header-the-caller-chose`). This spec takes **312**. Re-check before opening the PR
(memory: *spec number: origin/develop isn't enough*).

## 0. Scope, as narrowed on the issue

- **In:** rename every leading-underscore private field under `tests/` (`_name` → `name`) and
  every reference to it, including comments that name the field.
- **Out:** any `.editorconfig` / analyzer / naming-rule change — that question is **#2764**, and
  this spec does not decide it. Also out: collection-expression or other style changes to the
  touched declarations (smallest change, ADR-0036).

## 1. The premise, re-checked on this tree

### 1.1 The real count — 114 fields in 40 files, not "~100"

The issue's search was single-line and unattributed, and called itself a lower bound. Re-searched
with every modifier combination, attributes, name-on-its-own-line and non-`private`
accessibilities:

| Check | Result |
|---|---|
| `private …  _x =`/`;` declarations, non-tuple types | 110 in 37 files |
| **tuple-typed** declarations (a `(` in the type defeated the issue's pattern) | **+4**: `_entries` ×3 (`CapturingLogger` in Automation, SystemVariables, StreamDistribution), `_byTrigger` (`InMemoryRuleCache`) |
| attributed / multi-line / name-on-own-line / `protected`/`internal`/`public` / implicit-private | **0** |
| **Total** | **114 fields, 40 files** |
| References (incl. declarations) in declaring files | 457 |
| References in a file that declares nothing | 2 — `AspireFixture.LogCapture.cs` (`_logTails`, a `partial`) |
| Comments naming a field | 3 — `AspireFixture.cs:250`, `:545` (`_logCts`); `AspireFixtureReportSelectionTests.cs:44` (`_exitCodes`) |

Groups: **builders** 11 files / 70 fields; **fakes & in-memory repositories** 26 files / 36 fields;
**integration fixtures** 3 files / 8 fields (+ `LogCapture.cs`, + one comment file).

### 1.2 Already done

`V1ResourceMapTests._map` (#2505) is already `map` on this tree. Not in scope.

### 1.3 The issue's "src/ has none" is wrong — out of scope, recorded

`src/Automation/Infrastructure/Cache/InMemoryRuleCache.cs:33` declares `_byTrigger` (5 references).
The original search missed it for the same reason it missed the four tuple-typed test fields: its
type contains `(`. Scope here is `tests/` as narrowed on the issue; this is recorded for **#2764**
(an enforcing analyzer would fail on it on day one) rather than silently widened.

### 1.4 No reflection or string reads a renamed name

No test calls `GetField("_…")` or `nameof(_…)`; the three `GetField` calls in `tests/` read
`TailedResources`, `elements` (on `Revision`, a `src` type) and private `const`s. String literals
containing `_` (`"_STALE"`, `"_underscore"`, `"_us"`…) and doc comments quoting `_logger` (a
`src` identifier) are **not** fields and must not be touched.

## 2. Collision hazards — the part a mechanical rename gets wrong

A case-sensitive scan of every declaring file for the bare (de-underscored) name. Every hit falls
in one of these classes; there are no others.

### H1 — SILENT: parameter self-assignment (25 sites)

`_x = x;` becomes `x = x;`: the parameter is assigned to itself and the field is never written.
**In Debug this compiles** (CS1717 is a warning; `TreatWarningsAsErrors` is Release-only,
`Directory.Build.props:17`), and a builder that ignores `WithFab(...)` keeps its default, so some
tests would still pass. Each must become `this.x = x;`.

| File | Sites |
|---|---|
| `AuditObservability.Application.Tests/TestData/AuditEventBuilder.cs` | `WithFab` (`fab`), `WithActor` (`actor`), `WithPayload` (`payload`) |
| `Automation.Domain.Tests/Rule/RuleBuilder.cs` | `WithAction` (`action`) |
| `EventIngestion.Domain.Tests/Event/EventBuilder.cs` | `WithIdentifier` (`identifier`), `WithSource` (`source`) |
| `EventIngestion.Domain.Tests/SourceMode/SourceModeBuilder.cs` | `WithSource`, `WithMode`, `DeclaredBy` |
| `Identity.Domain.Tests/RegisteredClient/RegisteredClientBuilder.cs` | `WithKind` (`kind`) |
| `LayoutComposition.Domain.Tests/Layout/Builders/LayoutBuilder.cs` | `WithFab`, `ForCamera`, `CreatedBy`, `WithGrid`, `WithTiles` |
| `OverlayDesigner.Domain.Tests/Overlay/Builders/OverlayBuilder.cs` | `WithLabels` (`elements`), `CreatedBy` |
| `StreamDistribution.Domain.Tests/Stream/Builders/StreamBuilder.cs` | `WithFab`, `ForCamera`, `WithSourceUrl` |
| `SystemVariables.Domain.Tests/Variable/Builders/VariableBuilder.cs` | `OfType` (`type`), `DefinedBy` |
| `AuditObservability.Application.Tests/Fakes/FakeClock.cs` | constructor (`now`) |
| `LayoutComposition.Application.Tests/Fakes/FakeCameraFabGuard.cs` | constructor (`permissive`) |
| `Integration.Tests/Identity/KeycloakAdminTokenProviderTests.cs` | constructor (`fixture`) |

### H2 — LOUD: parameter assigned through a factory (18 sites)

`_fab = FabIdentifier.From(fab);` becomes `fab = FabIdentifier.From(fab);` — a type mismatch,
compile error. Safe in that it cannot ship, but still needs `this.`: `RuleBuilder` (fab, name,
predicate), `CameraBuilder` (fab, name, url), `EventBuilder` (fab, device, kind, occurredAt),
`RegisteredEventTypeBuilder` (fab, kind), `RegisteredClientBuilder` (fab), `LayoutBuilder` (name,
overlay — `Option<…>.Some(overlay)`), `OverlayBuilder` (name), `VariableBuilder` (fab, name).

### H3 — LOUD: local initialised from the field it now shadows (1 site)

`LayoutBuilder.Build()` line 75: `IReadOnlyList<Tile> tiles = _tiles ?? …` becomes
`tiles = tiles ?? …` — CS0165. Write `this.tiles` on the right-hand side; line 77's `tiles`
already means the local and stays.

### H4 — benign shadowing, recorded so nobody "fixes" it

`AspireFixture`'s **static** helpers (`IsHealthy`, `ExitedNonZero`, `FormatTimeoutMessage`, …,
lines 657–990) take a parameter named `exitCodes`. After the rename it shadows the field, but a
static method could never see the instance field, so every binding is unchanged. The four
instance uses (486, 617, 1005, 1021) have no `exitCodes` local. Leave the parameters alone.

### H5 — text that looks like a field but is not

`"(app not built)"` (AspireFixture 759, 1002) and the string/comment tokens in §1.4. A regex over
`\b_\w+` would corrupt them. Rename **by declared name, per file**, never by pattern.

## 3. User story

**US1 (P1)** — As a maintainer reading any test file, I see private fields named the way the house
rule names them, so the rule has no tree-wide exception for #2764 to carve around.

### Acceptance scenarios

```gherkin
Scenario: no leading-underscore private field remains under tests/
  Given the branch after the sweep
  When I search tests/ for a private field declaration whose name starts with "_",
       including tuple-typed, attributed and multi-line declarations
  Then there are no matches

Scenario: behaviour is unchanged
  Given the pass/fail/skip counts of every affected test project captured on develop @ 3ebd6f4a
  When the same projects run on the branch with no test file's assertions edited
  Then every count is identical and nothing fails

Scenario: a self-assignment cannot slip through (the conflict case)
  Given a With-method rewritten as "fab = fab;"
  When the solution is built in Release
  Then the build fails on CS1717
  And the sweep is not complete until the Release build is clean

Scenario: non-field underscore text is untouched (the bad-input case)
  Given the string literals "_STALE", "_underscore", "_us" and the doc comment "RunInBackground(_logger)"
  When the sweep is complete
  Then each is byte-for-byte unchanged
```

Auth: N/A — test code only, no endpoint, no scope.

## 4. Independent end-to-end procedure

1. On develop, run each affected test project (plan §3) and record its totals.
2. Apply the sweep.
3. `dotnet build -c Release` of the solution — clean, zero warnings promoted.
4. Re-run step 1 on the branch; totals identical.
5. Run the two greps in plan §4 — both empty.
6. `git diff --stat` touches only `tests/**` and `specs/312-*`; `git diff -G'"_'` shows no change
   to a string literal.

`Integration.Tests` needs the Aspire stack; locally it is build-only plus
`AspireFixtureReportSelectionTests` (pure), and CI's integration job is the run that exercises the
fixture.

## 5. Locked choices

Hand rename per file (IDE or by-name replacement), `this.x = x;` where a parameter shares the name
(CLAUDE.md; `dotnet_style_qualification_for_field = false:silent`, so `this.` raises nothing).
No new types, no style changes.

## 6. Latency budget

**N/A** — test code only; no leg of constitution §IV is touched.
