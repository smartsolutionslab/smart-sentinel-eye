# Plan 407: the simulator's seeding helpers under five parameters

**Spec**: [spec.md](./spec.md). **Issue**: #3027. **Base**: `c8c3773c`.

## 1. Context and layers

ScenarioSimulator (dev tooling; not a bounded context, no Domain/Application split). Two files:
`src/ScenarioSimulator/Seeding/LayoutCompositionClient.cs` and
`src/ScenarioSimulator/Seeding/ScenarioSeeder.cs`. No Shared.Kernel, Shared.Contracts,
ServiceDefaults or AppHost change. No entity, value object, invariant, domain or integration event,
or messaging change. Boundary: the new record structs reference only types already in their files
(`TileBody`, `AssetDefinition`), so no `using` and no project reference changes.

**Latency (§IV): N/A.** **§II: N/A** (private helpers, dev tooling).

## 2. Shape

All three layout groupings go in the private-records block at the end of `LayoutCompositionClient`
(beside `EditDraftBody` … `LayoutRevisionItem`), each with a one-line `<summary>`. Locals take the
old parameter names, so everything after the deconstruction line is textually unchanged (spec 383
pattern). Deconstructing a struct of already-built values cannot throw, so moving it first is
behaviour-neutral.

### 2.1 H1 `RecoverAsync` (6 -> 3) and H2 `RetileAsync` (7 -> 4)

```csharp
/// <summary>The wall the scenario asks for: its name, grid and tiles, carried as one argument.</summary>
private readonly record struct DesiredWall(
    string Name,
    int Rows,
    int Cols,
    IReadOnlyList<TileBody> Tiles);
```

```csharp
private async Task RecoverAsync(
    DesiredWall wall,
    string token,
    CancellationToken cancellationToken)
{
    var (name, rows, cols, tiles) = wall;

    var existing = await ReadBackAsync(name, token, cancellationToken);
    ...                                                   // unchanged
    await RetileAsync(wall, existing, token, cancellationToken);   // the only other changed line
}

private async Task RetileAsync(
    DesiredWall wall,
    LayoutListItem existing,
    string token,
    CancellationToken cancellationToken)
{
    var (name, rows, cols, tiles) = wall;

    var layout = existing.LayoutIdentifier;
    ...                                                   // unchanged except the PublishAsync call (2.2)
}
```

`RetileAsync` receives the same `wall` value rather than a reconstructed one: no second
construction site, so no second chance to transpose `rows`/`cols`.

**Call site** (`EnsureWallAsync`, :63; its only change, spec D-3):

```csharp
await RecoverAsync(new DesiredWall(name, rows, cols, tileBodies), token, cancellationToken);
```

`rows`/`cols` are adjacent `int`s here, so a swap compiles: G1's characterisation (plan §4, cases
1-3) is what catches it. `RetileAsync`'s doc comment is unchanged (it names no parameter).

### 2.2 H3 `PublishAsync` (5 -> 4)

```csharp
/// <summary>One revision of one layout chain, as the publish route addresses it.</summary>
private readonly record struct RevisionAddress(Guid Layout, int Revision);
```

```csharp
private Task PublishAsync(
    Guid layout, int expectedVersion, string token, CancellationToken cancellationToken) =>
    PublishAsync(new RevisionAddress(layout, FirstRevision), expectedVersion, token, cancellationToken);

private async Task PublishAsync(
    RevisionAddress address,
    int expectedVersion,
    string token,
    CancellationToken cancellationToken)
{
    var (layout, revision) = address;

    using HttpRequestMessage publish = ...                // unchanged
}
```

`RetileAsync`'s call (:168) becomes
`await PublishAsync(new RevisionAddress(layout, revision), afterEdit.Version, token, cancellationToken);`.
`Guid` and `int` cannot swap inside the record, and `expectedVersion` is no longer beside
`revision` (spec D-2). Overload resolution is unambiguous (`Guid` vs `RevisionAddress` first
parameter). The four-parameter overload keeps its signature and was never a hit; its wrapped
line may exceed the file's usual width, in which case it takes one argument per line (house rule).

### 2.3 H4 `SeedReactionsAsync` (6 -> 4)

Nested at the end of `ScenarioSeeder`, after `ReportUnreachableTriggers`:

```csharp
/// <summary>The asset whose reactions are being seeded: its scenario, its definition and the overlay seeded for it, if any.</summary>
private readonly record struct SeededAsset(string Scenario, AssetDefinition Asset, Guid? Overlay);
```

```csharp
private async Task SeedReactionsAsync(
    SeededAsset seededAsset,
    HashSet<string> seededVariables,
    IReadOnlyDictionary<string, ClipManifestLoadResult> manifestsByClip,
    CancellationToken cancellationToken)
{
    var (scenario, asset, overlay) = seededAsset;

    if (asset.Reactions.Count == 0)                       // unchanged from here
```

**Call sites** (`SeedOverlayAndRuleAsync`):

```csharp
// :185
await SeedReactionsAsync(new SeededAsset(scenario, asset, Overlay: null), seededVariables, manifestsByClip, cancellationToken);
// :204
await SeedReactionsAsync(new SeededAsset(scenario, asset, overlay), seededVariables, manifestsByClip, cancellationToken);
```

If a call exceeds the line width, it is wrapped with every argument on its own line (house rule).
The three fields are three distinct types, so every swap is a compile error. The doc comment's
`<paramref name="seededVariables"/>` still resolves (that parameter keeps its name); the
`<paramref name="asset"/>` on `SeedVariablesAsync`'s comment refers to *that* method's parameter
and is unaffected.

### 2.4 Metrics after

Method bodies grow by 2 lines each (deconstruction + blank); none is near S138's 30 (largest,
`RetileAsync`, ~26 -> ~28 lines: the engineer confirms in the after-build). Cyclomatic complexity
and nesting unchanged. File code lines (non-blank, non-comment): `ScenarioSeeder.cs` ~253 -> ~260,
`LayoutCompositionClient.cs` ~167 -> ~180, both under S104's 300. No `using` added or removed.

## 3. Boundary and guard check

- `HandlerDeconstructionTests` scans only `public … Handle/HandleAsync` signatures: none here.
- `PrimitiveBoundaryTests` binds domain models: none here.
- `SimulatorRuntimeBindingTests` reflects over `Program`'s static methods: untouched.
- No file added or renamed under `src/`.

## 4. Covering tests and commands (Phase 4a characterisation, observed green before; unmodified after)

All commands from `D:\Github\sse-3027`. Stop any running AppHost first (MSB3027).

**T000a adds (test-writer, observed green on unchanged code, commit `C0`)** one new class,
`tests/ScenarioSimulator.Tests/LayoutCompositionClientCharacterisationTests.cs` (spec D-1). It
builds the real `LayoutCompositionClient` + real `KeycloakTokenProvider` exactly as
`LayoutCompositionClientTests.SeedAsync` does (token `stub-token`), but with its **own** recording
handler capturing method, path-and-query, `If-Match`, `Authorization` and body (read inside
`SendAsync`, since requests are disposed), a `RecordingLogger<LayoutCompositionClient>`, and a
parameterised desired wall (rows, cols, tiles). Every numeric value distinct where it can be:
desired grid **rows 2, cols 3**; two tiles `(c1, o1, 0, 0)` and `(c2, o2, 1, 2)`; draft response
revision **5**; read-back versions **7, 8, 9**.

1. **Non-square re-tile, values in place** (G1, G2). Published revision 2x3 with other tiles.
   Assert the 7-call sequence; the PATCH path `/revisions/5`; the PATCH body's `grid.rows == 2`,
   `grid.cols == 3`, and for each tile `cameraIdentifier`, `overlayIdentifier`, `row`, `col`, in
   order, each its own assertion; publish path `/revisions/5/publish` with `If-Match "9"`.
2. **Orientation-only mismatch is re-tiled** (G1). Published revision **3x2**, tiles identical to
   desired: the 7-call sequence happens.
3. **Same orientation, same tiles, left alone** (G1, G4). Published **2x3**, identical tiles:
   exactly `POST /layouts`, `GET /layouts`; one Information entry
   `"Wall layout 'rolling-mill' already exists and needs no publishing; skipping (idempotent)."`.
4. **Bearer on every call** (G3). On case 1's run and on a stranded-draft run (version 4): every
   captured request's `Authorization` is `Bearer stub-token`.
5. **Branch log lines** (G4), exact text and level as observed: stranded draft ->
   `"Wall layout 'rolling-mill' ({id}) was left in Draft; published it."`; re-tile ->
   `"… had different tiles; re-tiled and published revision 5."`; missing revisions -> the Warning
   `"… came back with no revisions; cannot tell whether it still needs publishing."`. Assert each
   message appears **exactly once**.
6. **Archived chain left alone** (G5). Revisions `[{1, "Archived"}]`: only `POST`, `GET`; one
   `WallAlreadyExists` line.

**Counterfactuals** (run once each on a scratch edit, red output reported verbatim, never
committed; restore and rebuild `--no-incremental` after each, a restored file keeps its old
timestamp):

| # | Edit (production, unchanged code) | Must go red |
|---|---|---|
| CF1 | swap `rows`/`cols` in `RetileAsync`'s `new GridBody(rows, cols)` | case 1 |
| CF2 | swap `rows`/`cols` in `published.Matches(rows, cols, tiles)` | cases 2 and 3 |
| CF3 | swap `rows`/`cols` in `EnsureWallAsync`'s `RecoverAsync(name, rows, cols, …)` call | cases 1-3 |
| CF4 | in `RetileAsync`, build the PATCH request's bearer from `name` instead of `token` | case 4 |
| CF5 | in `RetileAsync`, log `FirstRevision` instead of `revision` | case 5 |
| CF6 | swap `expectedVersion`/`revision` arguments in `RetileAsync`'s `PublishAsync` call | case 1 (and existing `Each_re_tiling_step_sends_the_version_it_just_read`) |
| CF7 | `ScenarioSeeder.cs:204` pass `overlay: null` | `ScenarioSeederReactionTests.A_declared_HighlightOverlay_reaction_…` |
| CF8 | `ScenarioSeeder.cs:204` pass `new HashSet<string>()` for `seededVariables` | `ScenarioSeederVariableTests.A_successfully_seeded_variable_lets_its_SetVariableValue_reaction_…` |
| CF9 | in `SeedReactionsAsync`, pass `asset.Key` instead of `scenario` to `ReactionRuleSeed.From` | any rule-name assertion (`rolling-mill-…`) |

A counterfactual that stays green means the test does not pin what it claims: fix the test before
`C0`, not after. CF7-CF9 need no new test; they prove the existing seeder coverage.

**In-process commands** (no Aspire):

```sh
dotnet test tests/ScenarioSimulator.Tests -c Release --filter "FullyQualifiedName~LayoutCompositionClient"
dotnet test tests/ScenarioSimulator.Tests -c Release --filter "FullyQualifiedName~ScenarioSeeder|FullyQualifiedName~ScenarioStoryCheckTests|FullyQualifiedName~CameraRegisteredSimHandler"
dotnet test tests/ScenarioSimulator.Tests -c Release
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~BoundaryTests|FullyQualifiedName~HandlerDeconstructionTests"
```

Each `--filter` run must report a **non-zero** count (a filter matching nothing exits 0, #2289).
Before T000a: the first matches the 10 `LayoutCompositionClientTests` facts; the second at least
30 seeder + 9 story-check + the `CameraRegisteredSimHandler*` facts. After T000a the first adds
the new class's tests.

No Aspire-fixture run is owed: no integration test drives the simulator's seeding helpers, and
none is added.

**Gate measurement** (spec §1 command, then):

```sh
grep -oE '[A-Za-z]:[^ (>]+\([0-9]+,[0-9]+\): warning S[0-9]+' s107-3027.log | sort -u > after.txt
diff before.txt after.txt                                   # exactly H1-H4 removed
git diff --stat C0 -- tests/                                 # must be empty
git diff c8c3773c -- src/ScenarioSimulator/Seeding/        # signatures + deconstructions + 5 call sites + 3 records
```

Before: 8 S107 under `src/ScenarioSimulator` (spec §1), no other Sonar rule there; 5 more in the
closure (`ServiceDefaults`: 4 S107, 1 S138). After: H5-H8 only; ServiceDefaults' 5 unchanged.
Solution-wide S107 (issue: 108 on `86d32918`) drops by exactly 4; the orchestrator measures it at
T020 only if disk allows (C: is nearly full), otherwise the closure diff is the evidence.

## 5. Risks

- **`rows`/`cols` transposed at the new construction site.** Cases 1-3 and CF1-CF3.
- **`expectedVersion`/`revision` transposed.** Designed out by D-2; CF6 proves it would be caught
  today anyway.
- **An existing assertion edited to fit.** `git diff --stat C0 -- tests/` must be empty; a needed
  edit is a block.
- **Spec-number race.** 406/408 claimed in parallel; re-check before the PR.
