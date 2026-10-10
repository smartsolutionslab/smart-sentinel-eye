# Plan 334 — The naming rule the build never read

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2764

## 1. Shape

No bounded context, domain model, messaging or contract. Build configuration
(`.editorconfig`, `Directory.Build.props`), two private-field renames in `src/`, and two
documentation lines. NetArchTest boundaries unaffected.

Change classes, declared separately (constitution §Testing, ADR-0139):

| Part | Class | Phase 4a colour |
|---|---|---|
| Build reports IDE1006 for the private-field rule | **behaviour-changing** (the build gains a diagnostic it never emitted) | **RED** — build counterfactual |
| Rename `_byTrigger`, `notReported` | behaviour-preserving | **characterisation, observed green** |

The red is a compiler diagnostic, not something xUnit can observe, so — as in spec 174 — it is
produced and quoted as a **build counterfactual** with a transient probe file.

## 2. The configuration change, exactly

### 2.1 `.editorconfig` — in the `[*.cs]` section, replacing lines 108-118

```ini
# Private fields are plain camelCase — no leading underscore. Most types here
# use primary constructors, so an explicit field is already the exception; when
# one is needed it reads as an ordinary name.
#
# The naming rule's own `.severity` is read by the IDE and `dotnet format`, but
# NOT by `dotnet build`: the compiler reports IDE1006 only when the diagnostic
# itself has a severity. Without the dotnet_diagnostic line below this rule was
# invisible to every build, CI included (#2764, #2174). Advisory: Release's
# TreatWarningsAsErrors is carved out for IDE1006 in Directory.Build.props.
dotnet_diagnostic.IDE1006.severity = warning

dotnet_naming_rule.private_fields_are_camel_case.severity = warning
dotnet_naming_rule.private_fields_are_camel_case.symbols = private_fields
dotnet_naming_rule.private_fields_are_camel_case.style = camel_case_no_prefix

dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private

dotnet_naming_style.camel_case_no_prefix.capitalization = camel_case

# The rule above would otherwise claim every private const and static readonly,
# which C# — and this repository — spells in PascalCase. These two are more
# specific (they name modifiers), so Roslyn applies them first.
dotnet_naming_rule.private_constants_are_pascal_case.severity = warning
dotnet_naming_rule.private_constants_are_pascal_case.symbols = private_constants
dotnet_naming_rule.private_constants_are_pascal_case.style = pascal_case

dotnet_naming_symbols.private_constants.applicable_kinds = field
dotnet_naming_symbols.private_constants.applicable_accessibilities = private
dotnet_naming_symbols.private_constants.required_modifiers = const

dotnet_naming_rule.private_static_readonly_fields_are_pascal_case.severity = warning
dotnet_naming_rule.private_static_readonly_fields_are_pascal_case.symbols = private_static_readonly_fields
dotnet_naming_rule.private_static_readonly_fields_are_pascal_case.style = pascal_case

dotnet_naming_symbols.private_static_readonly_fields.applicable_kinds = field
dotnet_naming_symbols.private_static_readonly_fields.applicable_accessibilities = private
dotnet_naming_symbols.private_static_readonly_fields.required_modifiers = static, readonly

dotnet_naming_style.pascal_case.capitalization = pascal_case
```

Comment wording is the engineer's to tighten; the **keys and values are fixed** — they are the
exact set measured in spec §1.4 (style renamed from the scratch `pascal_case_style` to
`pascal_case`; a style name is a free identifier).

**Ordering:** specificity, not file order, decides precedence in current Roslyn (modifiers beat
none) — confirmed by the scratch measurement where the PascalCase rules sat *after* the camelCase
rule and still won. Keep them in `[*.cs]`; do not put them in `[src/**.cs]` (the rule binds tests
too).

**`dotnet_diagnostic.IDE1006.severity` also makes the interface `begins_with_i` rule
build-visible.** Measured: 0 hits. Intended, recorded.

**Generated code:** `[**/Migrations/*.cs]` sets `generated_code = true`; IDE analyzers skip it.
Measured: 0 migration hits.

### 2.2 `Directory.Build.props` — the carve-out binds every project

Add to the **first, unconditional** `PropertyGroup`, directly after the `TreatWarningsAsErrors`
line (17), with a comment in the same voice as the ADR-0084 one at line 89-99:

```xml
<WarningsNotAsErrors>$(WarningsNotAsErrors);IDE1006</WarningsNotAsErrors>
```

Not in the production-only `PropertyGroup` at line 100: the private-field rule has no test
exemption, and a test project in Release would otherwise turn a stray `_x` into a build error. The
existing production line `$(WarningsNotAsErrors);S104;…` appends to it, so the two compose.

**Permanent, like ADR-0084's.** Deleting it does not make naming blocking; it breaks Release on the
next violation. Making it blocking is a new owner decision.

## 3. Residue — two renames (behaviour-preserving)

| File | Change | References | Covering tests |
|---|---|---|---|
| `src/Automation/Infrastructure/Cache/InMemoryRuleCache.cs` | `_byTrigger` → `byTrigger` | 5, all in-file | `tests/Automation.Infrastructure.Tests/Cache/InMemoryRuleCacheTests.cs` (the whole project) |
| `src/AppHost/StackStatusReport.cs` | `notReported` → `NotReported` (`const`) | 3, all in-file (34, 167, 226) | `tests/Integration.Tests/AppHostStackStatusTests.cs` — needs the Aspire stack; locally **build-only**, CI's integration job runs it |

Hazard check for `byTrigger`: no parameter, local or member named `byTrigger` in
`InMemoryRuleCache.cs` (engineer re-verifies with a case-sensitive grep before renaming; a
collision would need `this.`). `NotReported` — no other member of `StackStatusReport` by that name
(re-verify). Both are private and file-local: no reflection reads them (`GetField` / `nameof`
checked by spec 312 for tests; re-grep `src`).

`tests/Automation.Application.Tests/Fakes/InMemoryRuleCache.cs` is a *different* type (a fake,
already renamed by spec 312) — do not touch.

## 4. Commit layout (each builds and passes on its own — ADR-0087)

Order matters: the residue renames go **first**, so no commit leaves Release with an IDE1006 even
as a warning, and the enabling commit lands on a tree already at zero.

1. `refactor(2764): drop the leading underscore from InMemoryRuleCache's index field`
2. `refactor(2764): spell StackStatusReport's private constant in PascalCase`
3. `build(2764): report the private-field naming rule in dotnet build, as an advisory warning`
   — `.editorconfig` + `Directory.Build.props` together (**indivisible**: either alone either
   fires as Release errors once a violation appears, or does nothing).
4. `docs(2764): record that the private-field rule is build-visible and advisory`
   — CLAUDE.md house-rule bullet (~line 399) gains one sentence: *enforced as an advisory IDE1006
   warning (`.editorconfig` + `WarningsNotAsErrors`), not build-breaking (#2764)*;
   CONTRIBUTING.md §Code style (~line 206-211) names IDE1006 alongside ADR-0084's rules as the
   second `WarningsNotAsErrors` carve-out. Two sentences each, no more.

Spec artifacts land in their own leading `docs(334)` commit per house practice.

## 5. Evidence plan (phase 4a / 5)

**Probe** — `src/Shared.Kernel/NamingProbe.cs` and `tests/Shared.Kernel.Tests/NamingProbe.cs`
(namespace per folder: `SmartSentinelEye.Shared.Kernel` / `SmartSentinelEye.Shared.Kernel.Tests`),
each exactly:

```csharp
public sealed class NamingProbe
{
    private const int ProbeLimit = 1;
    private static readonly int ProbeShared = 2;
    private readonly int _probeValue = 3;

    public int Total() => ProbeLimit + ProbeShared + _probeValue;
}
```

Designed so that nothing **but** the naming rule can fire in Release: `public` (no CA1812), every
field read (no IDE0051/CS0169), `_probeValue` already `readonly` (no IDE0044), instance member used
(no CA1822), file-scoped namespace matching folder (no IDE0130/IDE0161). If the **before** run
shows any diagnostic, the probe is confounded — fix the probe, not the config.

| Run | Expected |
|---|---|
| before, `src/Shared.Kernel` Release | exit 0, **0** IDE1006 — the red |
| before, `tests/Shared.Kernel.Tests` Release | exit 0, **0** IDE1006 |
| after, both | exactly **1** IDE1006 each, on `_probeValue` ("Prefix '_' is not expected"), **exit 0** |

Exit 0 after is not incidental — it is the only proof of the `WarningsNotAsErrors` carve-out
(SC-2), and the likeliest thing to be wrong.

Build with `--no-incremental` every time: an `.editorconfig` change is a compiler input, but a stale
`obj/` has fooled this repository before (memory: *a restored file keeps its old timestamp*).

**Solution measurement** (SC-4): `dotnet build SmartSentinelEye.slnx -c Release` → exit 0,
`grep -c "warning IDE1006"` = 0. Needs the build to finish (memory: *a build-based count needs the
build to finish*); if an Aspire stack is running from this worktree, ask the orchestrator before
stopping it (MSB3027 looks like a broken build).

## 6. Interaction with #2174 / #2175 (not fixed here)

- **#2174** — its naming question is answered by §2.1. Its whitespace part is #2175's. After
  merge: comment on #2174 with the before/after `dotnet format` IDE1006 count (350 → 0 on
  `Architecture.Tests`) and recommend closing or narrowing it. Do not close it from the lane.
- **#2175** — a `dotnet format --verify-no-changes` gate is still not added; the naming blocker is
  gone, the two `error WHITESPACE` remain. Comment, do not fix.

## 7. Risks

- **SDK drift.** Behaviour measured on SDK 10.0.401 (the `global.json` pin, `rollForward:
  latestPatch`). A future SDK honouring the naming rule's own severity at build would make the
  `dotnet_diagnostic` line redundant, not wrong.
- **Rider/VS.** The IDE already showed these as warnings; nothing changes for editors except that
  PascalCase `const`s stop being flagged — a reduction in noise.
- **CI logs.** Warnings in Release CI are visible but not gating — the owner's choice. Nothing
  counts them; a later spec may add a count if drift is observed.
