# Plan 405: the configured-sources read shares ReadPagesAsync

**Spec**: [spec.md](./spec.md). **Issue**: #3021, item 1.

## 1. Context and layers

Bounded context **StreamDistribution**, **Infrastructure** layer only. The changes stay inside
`src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs`. No Domain, Application, Api
or `Shared.Contracts` change. `IRtspGateway` (Domain port) and its doc comments are unchanged. No
entity, value object, invariant, domain event or integration event is touched. No cross-context
reference appears. NetArchTest boundaries are unaffected.

| File | Change |
|---|---|
| `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs` | `ListConfiguredPathSourcesAsync` body replaced by a `ReadPagesAsync` call plus a two-case switch (§2). `private const int MaxConfiguredSourcePages = 1000;` deleted. The comment above the method reworded (spec A2). Nothing else |
| `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayConfiguredSourcesFoldCharacterisationTests.cs` | **new**, phase 4a only (C1-C6) |

## 2. The fold

It mirrors `ListConfiguredPathsAsync` (L92-119) exactly. Only the result container, the page
callback and the two logger methods differ:

```csharp
public async Task<IReadOnlyDictionary<MediaMtxPath, string>> ListConfiguredPathSourcesAsync(
    CancellationToken cancellationToken)
{
    // <reworded comment: same paginated read as ListConfiguredPathsAsync, via ReadPagesAsync,
    //  keeping each item's `source` verbatim; the repair compares it ordinally with the database.>
    Dictionary<MediaMtxPath, string> sources = [];

    var read = await ReadPagesAsync(
        "/v3/config/paths/list",
        payload => AddPageToSources(payload, sources),
        cancellationToken);

    switch (read.End)
    {
        case PageReadEnd.PageCountUnreadable:
            logger.MediaMtxConfiguredSourcesPageCountUnreadable(read.Page);
            break;
        case PageReadEnd.PageCapReached:
            logger.MediaMtxConfiguredSourcesPageCapReached(MaxListPages);
            break;
    }

    return sources;
}
```

Load-bearing details. Each maps to a row of spec §2:

- **The Sources logger methods, not the Paths ones** (rows 9, 11, 15). C1 and C2 catch the swap.
- **`read.Page`** for the unreadable case. `ReadPagesAsync` returns the page it stopped on, which is
  the value the old loop logged. C1 cuts at page 2 to tell the two apart from a constant 0.
- **`MaxListPages`** for the cap argument (both are 1000, so the rendered text is identical).
- **`/v3/config/paths/list` literal** (row 1). C3 pins the full URI.
- The old loop's "absent pageCount reads as one page" behaviour is already commented inside
  `ReadPagesAsync`. Do not duplicate that comment.
- No `default:` arm. `ListConfiguredPathsAsync` has none and the analyzers accept it. `Complete`
  needs no action. Do not add an `UnreachableException` arm: that would be new behaviour for an
  impossible value, and a pattern this method's sibling does not use.

`MaxConfiguredSourcePages` has no other reference (`git grep` on `b7001237`: L20, L168, L199 only).
Delete it, or IDE0051 flags it.

## 3. Messaging

None. No domain or integration event is published, consumed or reshaped.

## 4. Verification of "behaviour-preserving"

1. Phase 4a: C1-C6 written on `b7001237` and observed **green**, together with every existing
   covering test (tasks T001).
2. Phase 4b: the fold (T010).
3. Phase 5: the same command, `tests/` byte-unmodified against the 4a commit (T020). Plus three
   structural checks: `git grep -n MaxConfiguredSourcePages src` is empty, exactly one
   `for (var page = 0;` remains in the gateway, and `ReadPagesAsync`'s body is unchanged
   (`git diff` of that hunk is empty).
4. Counterfactual (prove the new guards bite, without committing it): temporarily swap the
   unreadable-case call to `MediaMtxConfiguredPathsPageCountUnreadable` and see C1 go red. Swap the
   cap call and see C2 go red. Change `itemsPerPage=100` in the scratch copy and see C3 go red. Revert.
   Quote the three reds in the PR. This shows C1-C3 are the guards the fold needs, not assertions
   that cannot fail.

## 5. Constitution and ADR alignment

- §II (no primitives on domain models): no domain model touched.
- §IV latency: N/A (control plane, spec §7).
- §Testing: characterisation, green before and after, assertions unmodified (ADR-0139, ADR-0144).
- ADR-0084: no new suppression. The method shrinks. #3029's S138 on `GetAllPathsHealthAsync` is
  unaffected (spec §7).
- ADR-0087: one `test(3021)` commit (C1-C6, green on unchanged code), then one `refactor(3021)`
  commit (the fold). Each builds and passes on its own.
- **ADR needed? No.** This implements spec 391 plan §3's recorded follow-up. It makes no decision.

## 6. Build hygiene

Build and test only `tests/StreamDistribution.Infrastructure.Tests` (Release). Do not build the
whole solution twice. The C: drive is nearly full, so use no extra worktrees, and stop any running
AppHost first. No Aspire boot, no integration shard: the integration tests that name
`ListConfiguredPathSourcesAsync` all use fake gateways and cannot observe this change.
