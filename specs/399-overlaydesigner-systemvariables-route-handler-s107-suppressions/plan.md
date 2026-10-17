# Plan 399: the OverlayDesigner and SystemVariables route handlers carry ADR-0084's S107 suppression

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2973.

## Constitution and ADR check

| Check | Result |
|---|---|
| ADR-0084 (2026-10-16 amendment, accepted) | Applies the adopted mechanism verbatim: per-handler `[SuppressMessage]` for S107 with a justification. Handlers only; helpers stay subject. S104 is respected, not suppressed (spec D-1). |
| ADR-0144 (no weakened gate) | The owner authorised this suppression on #2973 (2026-10-17). No threshold, analyzer, `.editorconfig` or test changes. |
| §Testing / phase 4a | Behaviour-preserving: characterisation observed green before, unmodified after, at each production commit. |
| §II (no primitives on the domain model) | Not touched - Api layer only. |
| No cross-context references | None added; each change stays inside one Api project. |
| §IV latency | N/A. |

## Bounded contexts and layers

OverlayDesigner and SystemVariables, **Api layer only**. No entity, value object, invariant, domain or
integration event, migration, Shared.Kernel / Shared.Contracts / AppHost change, so there is no
foundational task.

| File | Change |
|---|---|
| `src/OverlayDesigner/Api/OverlayEndpoints.Commands.cs` | attribute on `CreateDraft`, `Publish`, `Archive`, `EditDraft`, `Revert`; `using` |
| `src/SystemVariables/Api/SystemVariableEndpoints.Reads.cs` | attribute on `GetOne`, `List`; `using` |
| `src/SystemVariables/Api/SystemVariableEndpoints.cs` | D-1: members at lines 173-379 removed (moved); usings trimmed to what remains |
| `src/SystemVariables/Api/SystemVariableEndpoints.Writes.cs` (new) | D-1: the moved members verbatim; then attribute on `Define`, `SetValue`, `Archive`; `using` |

## Design

1. **Contract** - `OverlayDesignerRouteTableCharacterisationTests` and
   `SystemVariablesRouteTableCharacterisationTests` render every endpoint's metadata, including any method
   attribute the minimal-API builder copies in, and every handler's parameter list. They are the
   counterfactual-capable guard for both production commits: an emitted attribute, a moved nested type, or
   a changed parameter would each fail them.
2. **Behaviour** - `OverlayDraftEndpointCharacterisationTests` and
   `SystemVariableDefineEndpointCharacterisationTests` pin status/problem-body/refusal order on the write
   handlers, which D-1 relocates.
3. **Source readers** - the six declaration guards in `tests/Architecture.Tests` plus
   `SourceScanCharacterisationTests`. `RouteChainReader` resolves a handler by class name across the Api
   project's files, so the move is invisible to it; the attribute form is the one spec 398 already proved
   against the masker.
4. **Analyzer** - the two scoped Release builds (spec §1): S107 under the two Api projects 10 -> 0,
   everything else identical, no S104.

Phase-5 extra (not a committed test): `grep -c SuppressMessageAttribute` on each built Api DLL prints 0.

## Commits (each builds and passes on its own, ADR-0087)

1. `docs(specs): spec 399 - ...` (this artifact set).
2. `refactor(system-variables): move the variable write handlers to SystemVariableEndpoints.Writes`
   (D-1; mirrors `36abc23a`). The characterisation set must pass at this commit.
3. `chore(2973): suppress S107 on the ten OverlayDesigner and SystemVariables route handlers per ADR-0084`
   - the three attribute-bearing files.

No test commit: the characterisation set exists and is run, not written.

## Risks

| Risk | Mitigation |
|---|---|
| Attribute emitted after all (someone defines `CODE_ANALYSIS`) | Both route-table baselines fail loudly; a block, not an edit. |
| Move is not pure (a body edit slips in) | SC-5: `git diff --color-moved` on commit 2; reviewer checks it. |
| S104 crossed anyway | Measured: main file ~135, `.Writes.cs` ~190, `OverlayEndpoints.Commands.cs` 252 -> 278, `.Reads.cs` 165 -> 176 code lines. SC-2 compares the whole list. |
| Orchestrator picks D-1 option (b) | Drop T010; one new advisory S104 recorded in the PR as owner-accepted. |
| Stale build noise | `--no-incremental`; identical before/after commands; stop any AppHost first (MSB3027). |
