# Plan 240 — A grid nobody sent

**Spec**: [spec.md](./spec.md) · **Issue**: #2480 · **Engineer**: `backend-engineer`

## 1. Scope and placement

- **Bounded context**: LayoutComposition. **Layer**: Api only.
- **Production file**: `src/LayoutComposition/Api/LayoutEndpoints.Commands.cs` — nothing else.
- **Not touched**: `Requests/CreateLayoutRequest.cs`, `Requests/EditDraftRequest.cs` (shapes and
  nullability unchanged), Domain (`GridDimensions`, `Layout` — #2309 already guards the domain side),
  Application, Infrastructure, `Shared.Kernel`, `Shared.Contracts`, AppHost, JSON options, any other
  context.
- **Entities / value objects / invariants**: none new. `GridDimensions.From(rows, cols)` keeps owning
  the `>= 1` invariant; this change only guarantees it is reached with a non-null request.
- **Messaging**: none. The refusal precedes every command dispatch, so no domain or integration event
  is raised or suppressed differently.
- **Boundary rules**: unaffected — no new reference, nothing crosses a context.

## 2. Why the existing catch is the right place

Both handlers already have one validation block whose only job is "turn every malformed body member
into `400 LAYOUT_INVALID_INPUT`": `try { …From(...) } catch (ArgumentException ex)`. An omitted
`tiles` already reaches it through `ParseTiles`' `Ensure.That(requests).IsNotNull()`
(`ArgumentNullException` ⊂ `ArgumentException`). The defect is that `grid` and a tile element are
dereferenced **without** such a guard, so they throw `NullReferenceException`, which the catch does
not see.

The fix therefore adds the same guard the sibling already uses, **inside** the `try`. It must be
inside: OverlayDesigner shows the failure mode of the guard outside it (§6) — an
`ArgumentNullException` thrown before the `try` is as much a 500 as the NRE it replaces, because no
registered `IExceptionHandler` handles `ArgumentException`.

Rejected alternatives:

- **Explicit `if (body.Grid is null) return Results.Problem(…)` before the `try`** — works, but
  duplicates the problem construction a third and fourth time per handler and diverges from how
  `tiles` is already handled. One helper serves both endpoints.
- **Catch `NullReferenceException`** — swallows programming errors anywhere in the block; a review
  blocker under ADR-0036 ("no drive-by error handling").
- **Make `Grid` nullable (`GridRequest?`)** — changes the DTO contract and the OpenAPI document for
  no behavioural gain over the guard.
- **`RespectNullableAnnotations` globally** — spec §2; an ADR-level decision, and §6 shows the class
  is too narrow to justify it.

## 3. The change

```csharp
// both sites, inside the existing try
grid = ParseGrid(body.Grid);

private static GridDimensions ParseGrid(GridRequest grid)
{
    Ensure.That(grid).IsNotNull();
    return GridDimensions.From(grid.Rows, grid.Cols);
}

private static List<Tile> ParseTiles(IReadOnlyList<TileRequest> requests)
{
    Ensure.That(requests).IsNotNull();
    return requests
        .Select(tile =>
        {
            Ensure.That(tile).IsNotNull();
            return new Tile(
                CameraIdentifier.From(tile.CameraIdentifier),
                tile.OverlayIdentifier is { } overlayId
                    ? Option<OverlayIdentifier>.Some(OverlayIdentifier.From(overlayId))
                    : Option<OverlayIdentifier>.None,
                GridPosition.From(tile.Row, tile.Col));
        })
        .ToList();
}
```

`Ensure.That` takes the parameter name from `[CallerArgumentExpression]`, so the details read
`Value cannot be null. (Parameter 'grid')` and `… (Parameter 'tile')` — the lambda parameter is
renamed `request` → `tile` precisely so the detail names the wire concept (spec FR-003, A2).
`ParseGrid` placement: next to `ParseTiles` at the bottom of the partial class. Both call sites keep
their statement order, so the validation order of FR-004 holds without further edits.

## 4. Tests (phase 4a — RED)

New file `tests/Integration.Tests/LayoutComposition/AbsentLayoutBodyMembersAreRefusedIntegrationTests.cs`,
Aspire fixture (ADR-0103), modelled on `EventIngestion/MissingPayloadIsRefusedIntegrationTests.cs`
(spec 173, the same defect class). Bodies are anonymous objects / raw JSON so `grid` can be omitted.
Operator `op-dresden@dresden.test` (single fab; no `?fabId=` needed). Helpers for a camera and a
draft: reuse the patterns in `LayoutFabScopingIntegrationTests` / `EditRevisionIntegrationTests`
(`RegisterCameraAsync("dresden")`, create → `ETag` → `If-Match`); copy locally, do not refactor
the existing suites.

| # | test | today | after |
|---|---|---|---|
| a | `POST /layouts` omitting `grid` → 400, title `LAYOUT_INVALID_INPUT`, detail ∋ `grid` (case-insensitive); `GET /layouts` does not list the name | **500** | 400 |
| b | `PATCH /layouts/{id}/revisions/1` (real draft, valid `If-Match`) omitting `grid` → same; `GET` shows grid/tiles unchanged | **500** | 400 |
| c | `POST /layouts` with valid grid, `"tiles":[null]` → 400, detail ∋ `tile` | **500** | 400 |
| d | `PATCH …/revisions/1` with valid grid, `"tiles":[null]` → same | **500** | 400 |
| e | `POST /layouts` with `grid` omitted and `Idempotency-Key: K` → 400; same key with a valid body → **201** | red at step 1 (500) — *then* green guard for FR-007 | 400 then 201 |
| f | `POST /layouts` with `{"rows":0,"cols":2}` → 400, detail **equals** `rows must be >= 1; got 0. (Parameter 'rows')` | green | green (FR-005 guard) |
| g | anonymous `POST /layouts` omitting `grid` → 401 | green | green (auth guard) |

Note on e: its first assertion is red today for the same reason as a, so it is **not** an
independent guard of FR-007 until a is fixed; that is acceptable — it guards that the fix does not
start recording keys for refused bodies. f and g are the green guards.

Red must be **the status assertion** (`500` observed, `400` expected), quoted verbatim — not a
compile error or a fixture boot failure.

## 5. Counterfactual (phase 4b)

After green: revert only the `Ensure.That(tile)` line and re-run — c and d must go red with 500
while a, b stay green. This proves the element guard is load-bearing and that the tests for a/b and
c/d are not asserting the same thing twice. Restore.

## 6. Repo-wide exposure audit (the issue's "check before deciding")

Every `[FromBody]` DTO in `src/*/Api` (18 bound types; no endpoint binds a body any other way and none
deserialises by hand) was traced member by member for a null that could escape as a 500. Result:

| context | site | exposed | why |
|---|---|---|---|
| LayoutComposition | `grid` at `LayoutEndpoints.Commands.cs:143`, `:354` | **yes** — this spec | NRE inside `catch (ArgumentException)` |
| LayoutComposition | `tiles[i] = null` at `:443` | **yes** — this spec | NRE in `ParseTiles` lambda |
| OverlayDesigner | `label` at `OverlayEndpoints.Commands.cs:25` (create) and `:199` (edit) | **yes — not in scope** | `Ensure.That(body.Label).IsNotNull()` sits **before** the `try`; its `ArgumentNullException` is handled by nothing → 500 (read from code, not reproduced) |
| every other context (CameraCatalog, SystemVariables, Automation, EventIngestion, Identity, StreamDistribution) | all body members | no | null reaches a VO `.From` / `IsNullOrWhiteSpace` / `??` / switch default inside a caught block, or the member is `required` (binder 400) |

Separately, not a 500: `RegisterDeviceRequest.DeviceIdentifier` null/empty is **accepted** (client id
`"plc-"`), a validation gap in Identity, not this defect class.

**Conclusion**: the class is two contexts, six sites, each fixable locally with the pattern of §3.
That does not justify a repo-wide serializer policy (spec §2's costs apply to every endpoint to fix
six). **Recommended follow-ups (not this spec)**: one issue for OverlayDesigner's `label` (move the
guard inside the `try`), one for Identity's empty `deviceIdentifier`. Neither needs an ADR.

## 7. Constitution / ADR alignment

- §II (no primitives on the domain model): untouched — Api layer only.
- §IV latency: N/A.
- ADR-0105: guards via `Ensure.That`; no `ArgumentNullException.ThrowIfNull`, no bare throw.
- ADR-0036: one file, no new abstraction; the helper exists because two call sites need it.
- ADR-0084 metrics: `ParseTiles` stays well under 30 LOC; handler bodies shrink by nothing and grow
  by nothing.
- ADR-0139 / ADR-0144: behaviour-changing → red first, verbatim output quoted in the PR.
