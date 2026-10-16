# Plan 383: OverlayDesigner's shape-element parsing under five parameters

**Spec**: [spec.md](./spec.md). **Issue**: #2973. **Base**: `81f68a62`.

## 1. Context and layers

OverlayDesigner, **Api layer only**, one file: `src/OverlayDesigner/Api/OverlayElementParsing.cs`.
No Domain, Application, Infrastructure, Shared.Kernel, Shared.Contracts, ServiceDefaults or AppHost
change. No entity, value object, invariant, domain or integration event, or messaging change.
Boundary: the new record references only `NormalizedPosition`, `NormalizedSize` and `OverlayColor`
from `OverlayDesigner.Domain.Overlay`, already imported by the file. SystemVariables: **no change**
(all five hits blocked, spec D-1).

**Latency budget (constitution §IV).** N/A: overlay authoring is off the event-to-overlay path.

**Constitution §II** does not apply: it binds domain models' state (ADR-0139/0140), and this is a
private Api-layer helper type. It would be moot anyway: all three fields are already value objects.

## 2. Shape

### 2.1 H1 `OverlayElementParsing.ParseShapeElement` (5 -> 3)

A record struct, nested in `OverlayElementParsing` after `ParseShapeElement`'s helpers (mirroring the
Domain's own `private readonly record struct ElementParts` in `OverlayElement.cs`, which groups the
same values for the same reason):

```csharp
/// <summary>
/// Where an element sits, how big it is and what colour it is drawn in: the three parsed values
/// every <see cref="OverlayElement"/> factory takes, in that order.
/// </summary>
private readonly record struct ElementAppearance(
    NormalizedPosition Position,
    NormalizedSize Size,
    OverlayColor Color);
```

Signature and the first body line:

```csharp
private static OverlayElement ParseShapeElement(
    ElementKind kind,
    ElementRequest request,
    ElementAppearance appearance)
{
    var (position, size, color) = appearance;

    RequireNoTextFields(kind, request.Text, request.FontSizePx);

    return kind == ElementKind.Box
        ? OverlayElement.Box(position, size, color)
        : OverlayElement.Ellipse(position, size, color);
}
```

The locals take the old parameter names, so everything after the deconstruction is **textually
unchanged**. The deconstruction comes first (house rule: destructure the input first), which is
behaviour-neutral: deconstructing a struct of three already-built references cannot throw.

**Call site** (`ParseElement`, line 28; the only change there):

```csharp
return kind == ElementKind.Text
    ? ParseTextElement(request, position, size, color)
    : ParseShapeElement(kind, request, new ElementAppearance(position, size, color));
```

The arguments to `new ElementAppearance(...)` are in field order and of three distinct types, so a
swap does not compile. Lines 21-24 (the four parses, in that order) are **not touched**: they hold
the only same-typed (`decimal`) pairs on this path.

The doc comment on `ParseShapeElement` keeps its text (it names no parameter). No `Ensure.That` is
added (private helper, not a trust boundary; ADR-0036/ADR-0105). `ParseTextElement` keeps its four
parameters.

**Metrics after.** `ParseShapeElement` grows from 7 to 9 body lines (S138 limit 30); cyclomatic
complexity unchanged (1 ternary); `ParseElement` unchanged in branching; file grows by ~10 lines
(117 -> ~127, S104 limit 300).

### 2.2 What is not touched

H2-H11 and every other handler signature; `OverlayEndpoints*.cs`; `Requests/ElementRequest.cs`;
`ParseElement` lines 17-26; `ParseColor`, `ParseKind`, `RequireNoTextFields`, `ParseTextElement`;
every SystemVariables file.

## 3. Boundary and guard check

See spec §5 D-2. Summary: the only guards that read signatures literally are the two route-table
baselines, which read route handlers only, none of which changes. The declaration guards scan every
Api `*.cs` for mappings and resolve handler bodies by method-group name; `OverlayElementParsing.cs`
contains neither a mapping nor a handler. No file is added or renamed under `src/*/Api`.

## 4. Covering tests and commands (Phase 4a characterisation, observed green before; unmodified after)

All commands from `D:\Github\sse-2973`. Stop any running AppHost first (MSB3027).

**T000a adds (test-writer, observed green on unchanged code, commit `C0`)**, into the existing
`tests/Architecture.Tests/OverlayDraftEndpointCharacterisationTests.cs` (no new class, no host
change; the existing `Post`/`Patch`/`Existing` helpers and `host.Overlays` mock suffice; add an
`Element` overload or a second builder taking all eight wire fields; the existing `Element(...)`
helper and every existing fact stay byte-identical):

1. **Box, every value in its own place** - create and edit: X 0.11, Y 0.12, W 0.23, H 0.34, colour
   `#12345678`. Assert, on the element captured from `overlays.Add` (create) or
   `existing.Revisions.Single().Elements` (edit): `Kind == Box`, `Position.X`, `Position.Y`,
   `Size.Width`, `Size.Height`, `Color`, `Text is null`, each as its **own** assertion.
2. **Ellipse, every value in its own place** - create and edit, distinct values (e.g. X 0.41,
   Y 0.52, W 0.13, H 0.24, `#ABCDEF`).
3. **Mixed set** `[Box(a), Ellipse(b)]` - create: two elements, in order, each kind and all five
   values per element; all ten decimals pairwise distinct, colours distinct.
4. **Transparent shape colour** `#00000000` on a Box - create: 201, persisted colour equal.
5. **Shape with text** - Box and Ellipse, create and edit: 400, exact body observed
   (`InvalidInput("text must be absent for a Box element. (Parameter 'text')")` expected shape),
   `host.Overlays.VerifyNoOtherCalls()` on create; on edit, no `SaveAsync`.
6. **Shape with fontSizePx** - Box and Ellipse, create and edit: 400 naming `fontSizePx`, exact body.
7. **Shape with both** - Box, create: 400 names `text` (first check wins).
8. **Precedence** - Box with text and normalizedX 1.5: 400 names `normalizedX`; Box with text and
   colour `red`: 400 names `color` (exact bodies as observed).

A `[Theory]` over (route, kind) is fine for 5-6. Each new fact must be seen **green on unchanged
code** before commit.

**Counterfactuals** (run once each, reported with the red output, never committed; restore and
rebuild `--no-incremental` after each, a restored file keeps its old timestamp):
(1) swap `OverlayElement.Box` and `OverlayElement.Ellipse` in `ParseShapeElement`: cases 1-3 red;
(2) swap `request.NormalizedX`/`request.NormalizedY` on line 21: cases 1-3 red;
(3) swap `request.NormalizedWidth`/`request.NormalizedHeight` on line 22: cases 1-3 red;
(4) delete the `RequireNoTextFields` call: cases 5-7 red;
(5) swap the two `if` blocks in `RequireNoTextFields`: case 7 red.
A counterfactual that stays green means the test does not pin what it claims: fix the test before
`C0`, not after.

**In-process (no Aspire; the primary safety net):**

```sh
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~OverlayDraftEndpointCharacterisationTests|FullyQualifiedName~OverlayDesignerRouteTableCharacterisationTests|FullyQualifiedName~SystemVariablesRouteTableCharacterisationTests"
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~IdempotentKeyReuseDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~HandlerDeconstructionTests|FullyQualifiedName~BoundaryTests"
dotnet test tests/Architecture.Tests -c Release
dotnet test tests/OverlayDesigner.Domain.Tests -c Release
dotnet test tests/OverlayDesigner.Application.Tests -c Release
```

Each `--filter` run must report a non-zero test count (a filter matching nothing exits 0, #2289).
Before T000a the first run counts 22 + 1 + 1 test methods; after it, those plus the new ones.

**Aspire fixture (CI's shard 2 is authoritative; run locally only if the stack is free, one machine,
one stack):**

```sh
dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~SmartSentinelEye.Integration.Tests.OverlayDesigner.OverlayElementValidationIntegrationTests|FullyQualifiedName~SmartSentinelEye.Integration.Tests.OverlayDesigner.OverlayRevisionLifecycleIntegrationTests|FullyQualifiedName~SmartSentinelEye.Integration.Tests.OverlayDesigner.OverlayGeometryValidationIntegrationTests"
```

No new test class is added, so no shard-filter entry is owed.

**Gate measurement** (spec §1 command, then):

```sh
grep -oE '[A-Za-z]:[^ (>]+\([0-9]+,[0-9]+\): warning S[0-9]+' metrics.log | sort -u | grep -E 'src.(OverlayDesigner|SystemVariables).Api'
git diff --stat C0 -- tests/                                         # must be empty
git diff 81f68a62 -- src/OverlayDesigner/Api/OverlayElementParsing.cs   # signature + 2 lines + call + record
```

Before: 11 S107 in scope (spec §1), no other Sonar rule. After: 10 (H2-H11), **no S138, S104, S1541
or S134** in either Api project, no new warning of any rule; solution-wide S107 109.

## 5. Risks

- **A value transposed while the call is reshaped.** Mitigated by distinct-valued characterisation
  of every component (T000a cases 1-3) and counterfactuals (2)-(3); the record itself makes its own
  swaps a compile error.
- **The shape text guard moved or reordered.** Cases 5-8 and counterfactuals (4)-(5).
- **An existing assertion edited to fit.** `git diff --stat C0 -- tests/` must be empty; an
  assertion that would need editing is a block, reported back.
- **Spec-number race.** Re-check `specs/` on `origin/develop` and open PR heads before the PR (382 is
  in flight as PR #3002).
