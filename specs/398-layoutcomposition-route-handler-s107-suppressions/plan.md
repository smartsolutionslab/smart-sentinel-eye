# Plan 398: the LayoutComposition route handlers carry ADR-0084's S107 suppression

**Spec**: [spec.md](./spec.md). **Issue**: #2972.

## Constitution and ADR check

| Check | Result |
|---|---|
| ADR-0084 (2026-10-16 amendment, accepted) | Applies the adopted mechanism verbatim: per-handler `[SuppressMessage]` for S107 with a justification. Handlers only; helpers stay subject. |
| ADR-0144 (no weakened gate) | The owner authorised this suppression on #2972 (2026-10-17). No threshold, analyzer, `.editorconfig` or test changes. |
| §Testing / phase 4a | Behaviour-preserving: characterisation observed green before, unmodified after. |
| §II (no primitives on the domain model) | Not touched — Api layer, attributes only. |
| No cross-context references | None added. |
| §IV latency | N/A. |

## Bounded context and layers

LayoutComposition, **Api layer only**. Four files, attribute + `using` only:

| File | Handlers |
|---|---|
| `src/LayoutComposition/Api/LayoutEndpoints.Commands.cs` | `CreateDraft`, `Publish`, `Archive`, `EditDraft`, `Revert` |
| `src/LayoutComposition/Api/LayoutEndpoints.Queries.cs` | `GetOne` |
| `src/LayoutComposition/Api/WallEndpoints.Commands.cs` | `CreateWall`, `EditScenes`, `Switch` |
| `src/LayoutComposition/Api/WallEndpoints.Queries.cs` | `GetOne` |

No entities, value objects, invariants, messages or migrations. No Shared.Kernel / Shared.Contracts /
AppHost change, so there is no foundational task.

## Design

The attribute is `[Conditional("CODE_ANALYSIS")]` and the build does not define that symbol, so it is
analysis-time only and absent from IL (spec §3). The covering evidence is therefore:

1. **Contract** — `LayoutCompositionRouteTableCharacterisationTests` renders every endpoint's metadata,
   including method attributes copied in by the minimal-API builder; if the attribute were emitted it
   would print `other System.Diagnostics.CodeAnalysis.SuppressMessageAttribute` and fail. It is the
   counterfactual-capable guard for "the HTTP contract did not move".
2. **Source readers** — the six declaration guards in `tests/Architecture.Tests`; the form in spec §5 is
   chosen so their regexes and masker read it correctly (spec §3).
3. **Analyzer** — the scoped Release build (spec §1): S107 under the Api goes 10 → 0, everything else
   identical.

A cheap extra observation for phase 5 (not a committed test): after the build,
`grep -c SuppressMessageAttribute src/LayoutComposition/Api/bin/Release/net10.0/SmartSentinelEye.LayoutComposition.Api.dll`
prints 0, confirming the attribute type is not even referenced from the assembly's metadata.

## Commits (each builds on its own, ADR-0087)

1. `docs(specs): spec 398 - ...` (this artifact set).
2. `chore(2972): suppress S107 on the ten LayoutComposition route handlers (ADR-0084 amendment)` — the
   four files. One commit; the four edits are the same change and none builds differently alone.

No test commit: the characterisation set already exists and is run, not written (tasks T001).

## Risks

| Risk | Mitigation |
|---|---|
| Attribute emitted after all (someone defines `CODE_ANALYSIS`) | Route-table baseline fails loudly; that is a block, not an edit. |
| `LayoutEndpoints.Commands.cs` crosses S104's 300 LOC | Five 5-line attributes: 269 -> 295 code lines (physical 309 -> 335), 5 lines of headroom, no S104 reported; SC-2 compares the whole warning list. |
| Wording diverges from sibling specs (#2971 spec 397) | Spec A-1: orchestrator aligns before merge. |
| Measurement noise from a stale build | `--no-incremental`; run before/after on the same command. |
