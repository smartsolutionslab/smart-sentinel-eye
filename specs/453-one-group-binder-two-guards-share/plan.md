# Plan 453: One group binder the two guards share

**Spec:** [spec.md](spec.md) · **Tasks:** [tasks.md](tasks.md) · **Issue:** #3133

## Slices

One slice, US1. Test code only (`tests/Architecture.Tests/`); no `src/` change.

## Bounded context and layers

None — architecture guards. No Domain/Application/Infrastructure/Api code, messaging, persistence, AppHost resource
or contract; boundary rules unaffected. Latency: N/A.

## The shared file — `tests/Architecture.Tests/RouteGroupBinding.cs`

`internal`, beside `RouteChainReader.cs` and `SourceMask.cs` (spec 190's shape). Built on `RouteChainReader.Balanced`,
`StatementEnd(…, ChainEndSentinel.EndOfText, ChainLiteralHandling.AlreadyMasked)` and `SplitArguments` exactly as
both copies call them today.

```csharp
internal static class RouteGroupBinding
{
    // Moved verbatim from EndpointScopeDeclarationTests.cs:480-512 (== StatusProducer :1095-1108).
    internal static readonly Regex RouteBuilderDeclaration;   // ES's RouteBuilderNames (:2265) keeps using it
    private static readonly Regex GroupHelperDeclaration;
    private static readonly Regex EqualsAssignment;

    internal static GroupBindings Read(
        string masked,
        IReadOnlySet<string> groupNames,                       // ordinal; the caller's dictionary keys
        IEnumerable<GroupDeclarationSite> declarations);
}

internal sealed record GroupDeclarationSite(string Name, int BlockAnchor, int DeclarationFrom, int DeclarationTo);

internal sealed class GroupBindings      // holds ParameterScope / GroupScope lists + masked
{
    internal GroupBindingOutcome Resolve(string receiver, int index);
}

internal abstract record GroupBindingOutcome
{
    internal sealed record Bound(string GroupName) : GroupBindingOutcome;
    internal sealed record NotBound : GroupBindingOutcome;
    internal sealed record HelperUnresolved(string Method, string Parameter, string Reason) : GroupBindingOutcome;
}
```

Moved in, unchanged in logic: `ParameterScopes`, `EnclosingBlock`, `ParameterListEnd`, `BodyAfter`, the helper resolver
(444's version, returning `Bound(name)` or `HelperUnresolved(method, parameter, reason)` instead of a `RouteGroup`),
`Resolve` and `IsRedeclared` (reading `masked`). The four reason fragments are **moved, not retyped** — byte-equal to
`EndpointScopeDeclarationTests.cs:1971, 1979, 1987, 1989`. Private records `ParameterScope(Name, From, To,
GroupBindingOutcome Bound)` and `GroupScope(Name, From, To, DeclarationFrom, DeclarationTo)`.

`Resolve` keeps today's order: innermost parameter scope containing `index` (ties by span length) → its outcome
(`NotBound` for a non-helper route-builder parameter); else a visible, un-redeclared group scope → `Bound(receiver)`
**only if `groupNames` contains it**, else `NotBound` (both copies' `GetValueOrDefault` returns `null` there); else
`NotBound`. A helper's `Bound` is produced only after the same `groupNames.Contains(argument)` check as today.

Names follow ADR-0091 (no `Id`/`Repo`-style shortcuts); private fields carry no underscore; `var` and collection
expressions as the analyzers require.

## Absorbing the differences — no verdict moves

| Diff | Where it stays | ES (444) | SP (447) |
|---|---|---|---|
| D1 failed helper | caller's adapter | `HelperUnresolved(m, p, r)` → `new RouteGroup(p, "", new EnforcedAuthorization(Unreadable, $"the helper '{m}' maps on its '{p}' parameter, which takes the group its caller passes, and {r}"))` — today's text, today's fields | any non-`Bound` → `null` → `Classify` → Unclassified, prefix `""` |
| D2 scope source | caller's discovery | `Read` adds `new GroupDeclarationSite(name, declaration.Index, declaration.Index, end)` where it now calls `EnclosingBlock` + `scopes.Add` (`:1747-1748`) | `GroupScopes` keeps its `GroupDeclaration` loop + walk-back, renamed `GroupDeclarationSites`, yielding `(variable, declaration.Index, typeStart, end)` — anchor and span exactly as now |
| D3 duplicates | caller's dictionary | `Bound(n)` → `groups[n]`, which is already the Unreadable duplicate entry | `Bound(n)` → `groupsInFile[n]`, the first (`TryAdd`) |

Name-then-lookup equals today's object binding because both copies resolve against the **final** dictionary (ES builds
`groups` completely before `ParameterScopes`; SP builds `groupsInFile` before `ParameterScopes`). `BlockAnchor` exists
only so SP's block is computed from the variable index, as now, while its declaration span starts at the type token.

**Stop condition (not reached):** had any difference needed the core to branch per guard, it would change a verdict
in one of them — none does.

## Edits by file (the allowed diff)

- **`RouteGroupBinding.cs`** — new.
- **`EndpointScopeDeclarationTests.cs`** — delete `RouteBuilderDeclaration`, `GroupHelperDeclaration`, `EqualsAssignment`,
  `ParameterScopes`, `EnclosingBlock`, `ParameterListEnd`, `BodyAfter`, `ResolveHelperGroup`, records `ParameterScope`,
  `GroupScope`, `GroupBinding`; in `Read`, collect `GroupDeclarationSite`s and call `RouteGroupBinding.Read(masked, new
  HashSet<string>(groups.Keys, StringComparer.Ordinal), sites)`; `ReadMapping` takes the resolved `RouteGroup?` (or the
  bindings plus a private `GroupOf` adapter) instead of `GroupBinding`; `RouteBuilderNames` reads
  `RouteGroupBinding.RouteBuilderDeclaration`; **`SharedSourceScanFiles` gains `"tests/Architecture.Tests/RouteGroupBinding.cs"`**
  — the one edit inside a test's data, and it *keeps* the self-scan's coverage of the moved lines (spec premise 5;
  counterfactual C3). No `[Fact]`/`[Theory]` member is touched.
- **`StatusProducerDeclarationTests.cs`** — delete `RouteBuilderParameter`, `GroupHelperDeclaration`, `EqualsAssignment`,
  `BoundGroup`, `IsRedeclared`, `ParameterScopes`, `EnclosingBlock`, `ParameterListEnd`, `BodyAfter`,
  `ResolveHelperGroup`, records `ParameterScope`, `GroupScope`; `GroupScopes` → `GroupDeclarationSites`; `Mappings` calls
  `RouteGroupBinding.Read(...)` and maps `Bound(n)` → `groupsInFile[n]`, else `null`. `BoundGroup`'s "two known
  differences" doc moves to the call site, reworded to say they now live in this guard's discovery and dictionary. The
  `RouteGroups`/`Mappings` signatures (the fixture seam) do not change.
- **`RouteChainReader.cs`** — comment only: the "group-of-a-mapping resolver is NOT here" paragraph points at
  `RouteGroupBinding` and says what is shared (binding) and what is not (discovery, dictionary, rendering).

## Phase 4a — characterisation, captured green first

No new test is written; the net exists (spec premise 4) and covers the binder from both sides. Captured **before** the
first change, at the base commit, and re-run after every commit:

- **Run:** `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~RouteTableCharacterisationTests" --logger trx`
  → 214 passed. Keep the sorted test-name list; it must be identical after.
- **Unmodified, proved mechanically** (memory: hash the code, not the prose): a scratch script (scratchpad, not
  committed) strips comments, splits each guard file into class members, and SHA-256s each. After the change, every
  member hash is equal except the members listed above as deleted or edited. Every `[Fact]`/`[Theory]` member, every
  fixture constant and every `InlineData` falls in the "equal" set.

## Counterfactuals (scratch edits, quoted red, reverted)

- **C1** — shared helper resolver binds the first of two groups: SP `A_helper_called_with_two_different_groups_binds_no_group`
  **and** ES `A_helper_called_with_two_different_groups_is_unreadable_and_names_the_helper_and_both_groups` go red.
  Both red proves both guards route through the one copy.
- **C2** — drop `!IsRedeclared(...)` from `Resolve`: SP `An_out_variable_is_never_credited_…` and ES
  `An_out_variable_is_never_bound_…` go red.
- **C3** — plant the word `waiver` in executable code of `RouteGroupBinding.cs`: ES
  `The_guard_offers_no_way_to_excuse_an_endpoint(waiver)` goes red (the scan list entry is live).

## Commits (ADR-0030; each builds and leaves the net green — ADR-0087)

1. `refactor(3133): extract RouteGroupBinding from EndpointScopeDeclarationTests` — new file + ES edits + scan-list entry.
2. `refactor(3133): StatusProducerDeclarationTests binds through RouteGroupBinding` — SP edits.
3. `docs(3133): point RouteChainReader at the shared group binder` — comment-only, code hash unchanged.

## Why no ADR

Spec 190 already established "shared guard machinery lives in internal files beside the guards"; this applies it to
one more piece. No decision about architecture, runtime, tooling or process is made, and no gate moves.

## Risks

- **Message drift** in ES's Unreadable text: guarded by the byte-equal reason check and ES `:1491-1520`'s `ShouldContain`s.
- **Regex `Compiled` options** differ only on SP's own `GroupDeclaration`/`MappingCall`, which stay in SP.
- Stale line references in comments elsewhere (`SourceScanCharacterisationTests.cs:57`,
  `RealmImportMirrorTests.IntegrationTestCopies.cs:115`) point at ES line numbers that shift; they cite masker /
  pattern precedent, not the binder. Left as-is (smallest change); a reviewer may note them.
