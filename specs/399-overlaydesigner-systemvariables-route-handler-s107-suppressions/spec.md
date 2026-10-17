# Spec 399: the OverlayDesigner and SystemVariables route handlers carry ADR-0084's S107 suppression

**Feature Branch**: `chore/2973-overlaydesigner-systemvariables-s107-suppressions` (cut from `origin/develop` at `f950cb7c`)

**Created**: 2026-10-18

**Status**: Draft (phases 1-3). No `[NEEDS CLARIFICATION]` remains. One decision recorded for the
orchestrator (D-1, §4) and two marked assumptions (A-1, A-2).

**Input**: Issue [#2973](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2973), as narrowed by
its comments. The body (bundle injected services like spec 341) is **superseded**: the 2026-10-16 comments
proved S107 does not count `[FromServices]` parameters and that the handler hits are pinned by the
per-context route-table baselines; the helper hit (`OverlayElementParsing.ParseShapeElement`) was cleared by
PR #3003; the owner's 2026-10-17 comment re-enrolled the ten handler hits with the mechanism this spec
applies. Refs #2970 (the ADR-0084 amendment, merged as `c7a6e586`), spec 380 (#2971, the evidence).

**Prior art, copied exactly**: spec 398 (#2972, LayoutComposition, merged as `eb5b6fe4`) and spec 397
(#2971, EventIngestion, open PR #3037). Same attribute, same placement, same covering-test shape.

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation, observed
green**. Engineer: **backend-engineer**. Not security-sensitive: no executable line, signature, route,
policy or fab-resolution path changes (§3), so Phase 6 is `/code-review` only.

**Spec number.** 399, assigned by the orchestrator (396-398 taken; 400 is #2974's).

**ADRs.** **ADR-0084** (S107 and S104; the 2026-10-16 amendment, accepted 2026-10-17: a route handler's
parameters are its HTTP contract, mechanism a per-handler `[SuppressMessage]` with a justification),
ADR-0144 (lane; the one suppression the owner authorised; characterisation colour), ADR-0070 (Minimal
APIs), ADR-0036 (smallest change), ADR-0087 (each commit builds on its own).

**No new ADR.** The decision is made and accepted; this spec applies it to two more contexts.

## 1. Premise check - the re-measured hit list

Measured 2026-10-18 on `f950cb7c`, each Api project's build closure only (the full solution is too heavy
on the shared machine):

```sh
dotnet build src/OverlayDesigner/Api/SmartSentinelEye.OverlayDesigner.Api.csproj -c Release --no-incremental \
  "-flp:logfile=<scratch>/od.log;verbosity=normal;NoSummary"
dotnet build src/SystemVariables/Api/SmartSentinelEye.SystemVariables.Api.csproj -c Release --no-incremental \
  "-flp:logfile=<scratch>/sv.log;verbosity=normal;NoSummary"
grep -oE '[A-Za-z]:[^ (]+\([0-9]+,[0-9]+\): warning [A-Za-z]+[0-9]+: [^[]*' <scratch>/od.log \
  | tr '\\' '/' | sed -E 's#^.*/src/#src/#' | grep -v Migrations | sort -u
```

(Under Git Bash, set `MSYS_NO_PATHCONV=1` or use a `D:/...` path for the log, or MSBuild writes it to
`D:\c\Users\...`.)

**10 S107 hits under the two Api projects, all 10 mapped route handlers, 0 helpers.** Identical to the
owner's list on #2973 (the eleventh hit of the body, `ParseShapeElement`, stays cleared). The premise holds.

| # | File (line) | Method | Counted | Pinned in |
|---|---|---|---|---|
| H1 | `src/OverlayDesigner/Api/OverlayEndpoints.Commands.cs` (16) | `CreateDraft` | 5 | `OverlayDesignerRouteTableCharacterisationTests` |
| H2 | `OverlayEndpoints.Commands.cs` (80) | `Publish` | 5 | same |
| H3 | `OverlayEndpoints.Commands.cs` (122) | `Archive` | 5 | same |
| H4 | `OverlayEndpoints.Commands.cs` (197) | `EditDraft` | 5 | same |
| H5 | `OverlayEndpoints.Commands.cs` (250) | `Revert` | 5 | same |
| H6 | `src/SystemVariables/Api/SystemVariableEndpoints.Reads.cs` (15) | `GetOne` | 5 | `SystemVariablesRouteTableCharacterisationTests` |
| H7 | `SystemVariableEndpoints.Reads.cs` (56) | `List` | 5 | same |
| H8 | `src/SystemVariables/Api/SystemVariableEndpoints.cs` (173) | `Define` | 6 | same |
| H9 | `SystemVariableEndpoints.cs` (263) | `SetValue` | 6 | same |
| H10 | `SystemVariableEndpoints.cs` (310) | `Archive` | 5 | same |

Every one is `private static async Task<IResult>` and is passed to a `Map*` call by
`MapOverlayEndpoints` / `MapSystemVariableEndpoints` (the only mapping entry points; `Program.cs`). Not
hits, so no attribute: OverlayDesigner `GetOne`, `List`, `BranchDraft`; SystemVariables `GetSnapshot`,
`ResolveText`.

**Justification truth, per context.** The fixed text says the parameters are "pinned by the route-table
characterisation baseline". For both contexts this is **true**: `tests/Architecture.Tests/OverlayDesignerRouteTableCharacterisationTests.cs`
and `SystemVariablesRouteTableCharacterisationTests.cs` (spec 366, captured on `39b0ef64`) render every
endpoint through `RouteTableRendering.Render`, whose `handler Name(param: Type [Attributes], ...)` line pins
each handler's full parameter list; all ten H-rows appear in them by name. (AuditObservability, which has
no such file, is not in this issue.)

**The rest of each closure, which must be identical after (SC-2).** OverlayDesigner closure, 6 lines:
S107 `OverlayDesigner/Domain/Overlay/OverlayElement.cs:132`, S107
`ServiceDefaults/Authentication/ClientCredentialsTokenProvider.cs:88`, S107
`ServiceDefaults/Authorization/FabResolution.cs:49`, S107 `ServiceDefaults/Idempotency/IdempotencyScope.cs:46`,
S107 and S138 `ServiceDefaults/WolverineDefaults.cs:40`. SystemVariables closure, 8 lines: the same five
ServiceDefaults lines, plus S107 `SystemVariables/Application/EventHandlers/VariableArchivedDomainEventHandler.cs:21`,
`.../VariableValueChangedDomainEventHandler.cs:24`, `SystemVariables/Domain/Variable/Variable.cs:55`. The
Domain/Application hits are outside the amendment and stay advisory.

## 2. User story

### US1 (P1) - the S107 count for these two contexts' handlers measures nothing anyone can act on

As the maintainer reading ADR-0084's S107 count, I want the ten OverlayDesigner and SystemVariables route
handlers to carry the sanctioned suppression, so that the count stops including HTTP contracts the
amendment declared out of scope, while every pinned contract stays exactly as it is.

**Independent test**: the two Release closure builds report 0 S107 under the two Api projects and the
other lines of §1 unchanged; both route-table baselines and the six source-scanning guards pass with
`git diff f950cb7c -- tests/` empty.

```gherkin
Scenario: the ten handlers are no longer counted
  Given both Api closures are built in Release with --no-incremental
  When the S107 warnings under src/OverlayDesigner/Api and src/SystemVariables/Api are listed
  Then there are none

Scenario: nothing else moved (conflict: a suppression that leaks, or a file pushed over S104)
  Given the same builds
  When every distinct warning in each closure is listed
  Then each list equals the corresponding §1 list exactly
  And no S104 warning appears on any SystemVariables or OverlayDesigner Api file

Scenario: the HTTP contract is unchanged (the attribute reaches no metadata)
  When OverlayDesignerRouteTableCharacterisationTests and SystemVariablesRouteTableCharacterisationTests run
  Then both pass unmodified

Scenario: the source-scanning guards still read the handlers (bad shape: an attribute the readers mis-parse)
  When ConcurrencyConflict-, EndpointScope-, IdempotentKeyReuse-, Precondition-, RouteValueRefusal-
       and StatusProducerDeclarationTests run
  Then every one passes unmodified

Scenario: a helper is not exempted (scope of the exemption)
  Given a method in these files that is not passed to a Map call
  Then it carries no S107 suppression
```

## 3. Why this is behaviour-preserving

- **Not emitted.** `SuppressMessageAttribute` is `[Conditional("CODE_ANALYSIS")]`. Both measured Release
  compile lines define `TRACE;RELEASE;NET;NET10_0;...` and not `CODE_ANALYSIS`, and no `.props`, `.targets`
  or `.csproj` in the repository defines it. Roslyn honours the attribute at analysis time; the IL carries
  nothing, so the minimal-API builder copies nothing into endpoint metadata. Were it emitted,
  `RouteTableRendering` would print `other System.Diagnostics.CodeAnalysis.SuppressMessageAttribute` under
  each handler and both baselines would fail: they are discriminating detectors here, not tautologies.
  `UnconditionalSuppressMessageAttribute` is **not** used (it is emitted).
- **Source readers.** `RouteChainReader.MethodBodies` finds a declaration with
  `(?<!\w)(?:private|public|internal|protected)[^;{}()\n]*?\bName\s*\(` on the masked source; the attribute
  sits on the preceding lines and the regex forbids `\n` and `(` between modifier and name, so it is
  invisible to it. Handlers are resolved by class name across **all files of the Api project**
  (`ProjectOf`), not by file, which is what makes D-1's move safe for the guards.
- **Masker.** `ConcurrencyConflictDeclarationTests` forbids `@"`, `"""`, `\"` and `'"'` in `src/*/Api`; the
  justification uses plain `"..."` literals, each closed on its own line, with no bracket, brace,
  parenthesis, semicolon, quote or backslash inside.
- **Excuse vocabulary.** The guards' `..._offers_no_way_to_excuse_...` rows scan the guards' own sources,
  not `src/`.
- **No line pins.** No file under `tests/` names `OverlayEndpoints.Commands.cs`, `SystemVariableEndpoints*.cs`
  or a line in them.

## 4. Decision D-1 - S104 on `SystemVariableEndpoints.cs` (architect's call, orchestrator to confirm)

`SystemVariableEndpoints.cs` is at **299 code lines** (non-blank, non-comment; the same count that reproduces
spec 398's 269/295 figures), against S104's `maximumFileLocThreshold` of 300 (`SonarLint.xml`). Three
five-line attributes plus one `using` add 16, taking it to **315: a new S104 warning**, which breaks the
owner's own characterisation clause ("every other warning is unchanged"). No form of the fixed attribute
avoids it (even three one-line attributes and the `using` give 303).

Options considered:

| | Option | Verdict |
|---|---|---|
| (a) | **Move** the write handlers and their private helpers verbatim from `SystemVariableEndpoints.cs` into a new partial `SystemVariableEndpoints.Writes.cs`, in a commit of its own before the suppressions | **Chosen.** Exactly the precedent `36abc23a` (the same file's reads moved to `.Reads.cs`, for the same limit). A member move between partials of one class changes no IL, no metadata, no nested-type name (`SystemVariableEndpoints+DefineVariableServices` stays as pinned); the guards resolve by class within the project. Measured after: main file 128 code lines; `.Writes.cs` 177 after the move, 193 after the three attributes. |
| (b) | Accept one new advisory S104 | Rejected: contradicts the owner's "every other warning unchanged". Would need the owner's word. |
| (c) | Suppress S104 too, or shorten the fixed text | **Forbidden**: outside the amendment (a weakened gate under ADR-0144) / breaks the one-grep count. |

The moved range is lines 173-379 at `f950cb7c`: `Define`, `ParseDefinition`, `CreateExecution`,
`DefineVariableServices`, `SetValue`, `Archive`, `ResolveWriteFabAsync`. The `Map*` methods and the two
constants (`DefineEndpoint`, `MaximumTextLength`) stay. Usings follow the code (each file keeps exactly the
ones it uses; IDE0005 clean). Name `.Writes.cs` pairs with the existing `.Reads.cs`, as EventIngestion's
`EventsEndpoints.Reads.cs`/`.Writes.cs` do.

If the orchestrator prefers (b), drop T010 and record the new S104 in the PR body as owner-accepted; nothing
else in this spec changes.

## 5. Success criteria

- **SC-1**: S107 hits under `src/OverlayDesigner/Api` and `src/SystemVariables/Api` go from 10 to 0.
- **SC-2**: each closure's distinct warning list is otherwise identical to §1 (OverlayDesigner 6 lines,
  SystemVariables 8 lines). Line numbers in `SystemVariableEndpoints*.cs` are not part of any §1 line, so
  D-1's move cannot disturb the comparison; no S104 appears.
- **SC-3**: `git diff f950cb7c -- tests/` is empty and the characterisation set (tasks T001) passes before
  and after, at each of the two production commits.
- **SC-4**: exactly 10 occurrences of `"S107:Methods should not have too many parameters"` across
  `src/OverlayDesigner/Api` and `src/SystemVariables/Api`, one per row of §1, in the four files
  `OverlayEndpoints.Commands.cs` (5), `SystemVariableEndpoints.Reads.cs` (2), `SystemVariableEndpoints.Writes.cs` (3),
  and none elsewhere in the diff.
- **SC-5** (D-1): the move commit's diff is a pure relocation - `git diff --color-moved=plain` (or
  `--color-moved-ws=ignore-all-space`) shows the 207 moved lines as moved, and the only other changes are the
  new file's header (usings, namespace, `public static partial class SystemVariableEndpoints {`, `}`) and
  the `using` lines IDE0005 requires.

## 6. Locked choices and the prescribed form

Byte-identical to specs 397/398 (A-1), so one grep counts them across contexts:

```csharp
    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> CreateDraft(
```

- Directly above the `private static async Task<IResult>` line, below any doc comment, never on the same
  line as the declaration. (None of the ten carries a doc comment today.)
- `using System.Diagnostics.CodeAnalysis;` in each of the three attribute-bearing files, in sorted position
  (first, before `using System.Security.Claims;`). Not implicit, not `global using`.
- Not `.editorconfig`, not `#pragma`, not a file-glob section, not a shared `const`.

**Latency budget**: N/A - no runtime change; nothing on the event-to-overlay path.

## 7. Assumptions

- **A-1**: the justification wording is the one fixed by the orchestrator and used by specs 397/398. It
  is true for both contexts here (§1).
- **A-2**: Roslyn matches the suppression on the ID before the colon; the category is documentation. Proven
  on develop by spec 398 (`eb5b6fe4`). If a hit remains after T011, the form is wrong: stop, do not try
  another mechanism.

## 8. Out of scope (what the lane must not take)

- Any handler signature, `*Services` record, route, OpenAPI metadata, summary or policy; any body edit in
  the moved members.
- Any file under `tests/` - an assertion that would need editing is a **block**.
- The non-hit handlers (§1) and every helper, including `ParseDefinition`, `CreateExecution`,
  `ResolveWriteFabAsync`, `ResolveReadFabsAsync` (Identity's `ResolveReadFabsAsync` is #2975, not this).
- The Domain/Application/ServiceDefaults S107 and the S138 in either closure; other contexts (#2971, #2972,
  #2974, #2975).
- `.editorconfig`, `SonarLint.xml`, `Directory.Build.props`, csproj files, ADRs, the constitution, CLAUDE.md;
  any suppression other than the ten in §6.
- The PR closes #2973 (`Closes #2973`): all ten remaining hits are covered and the eleventh is already
  cleared.
