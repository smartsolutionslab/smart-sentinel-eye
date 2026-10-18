# Plan 401: the CameraCatalog and Identity route handlers carry ADR-0084's S107 suppression

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2975.

No open decision (spec §6). The plan is spec 400's minus both of its decisions: no new baseline, no
partial-file move.

## Constitution and ADR check

| Check | Result |
|---|---|
| ADR-0084 (2026-10-16 amendment, accepted) | Applies the adopted mechanism verbatim to the 7 handler hits; helpers stay subject (none is a hit). |
| ADR-0144 (no weakened gate) | The owner authorised this suppression on #2975 (comment of 2026-10-17). No threshold, analyzer, `.editorconfig` or test change. |
| §Testing / phase 4a | Behaviour-preserving: existing characterisation observed green before, unmodified after. |
| §II (no primitives on the domain model) | Not touched — Api layer only. |
| No cross-context references | None added. |
| Security-sensitive context (Identity; fab resolution in both) | No executable line changes; `security-reviewer` still runs in phase 6 (spec §3). |
| §IV latency | N/A. |

## Bounded contexts and layers

CameraCatalog and Identity, **Api layer only**. No test file is added or changed.

| File | Change |
|---|---|
| `src/CameraCatalog/Api/CameraEndpoints.cs` | `using` at line 1; attribute on `Register`, `Patch`. 282 -> 293 code lines. |
| `src/CameraCatalog/Api/CameraEndpoints.Reads.cs` | `using` at line 1; attribute on `Get`, `List`. 100 -> 111. |
| `src/Identity/Api/DevicesEndpoints.cs` | `using` at line 1; attribute on `Register`. 202 -> 208. |
| `src/Identity/Api/KiosksEndpoints.cs` | `using` at line 1; attribute on `Enroll`. 199 -> 205. |
| `src/Identity/Api/WebhookRotationEndpoints.cs` | `using` at line 1; attribute on `Rotate`. 156 -> 162. |

No entities, value objects, invariants, messages or migrations. No Shared.Kernel / Shared.Contracts /
AppHost change, so there is no foundational task.

## Design

### The attributes

Spec §5, verbatim, on the 7 rows of spec §1, directly above each `private static async Task<IResult> Name(`
line (each name is unique as such a declaration within its file; the probe checked). Five lines, four-space
class-member indent, CRLF like the rest of each file.

### Covering evidence

1. **Contract** — `CameraCatalogRouteTableCharacterisationTests` (shared `RouteTableRendering`) and
   `IdentityRouteTableCharacterisationTests` (own renderer via `IdentityEndpointHost`). Both print each
   handler's name and full parameter list and every metadata item; an emitted attribute would appear as
   `other System.Diagnostics.CodeAnalysis.SuppressMessageAttribute` and fail them.
2. **Behaviour around the touched handlers** — `CameraRegisterEndpointCharacterisationTests`,
   `CameraPatchEndpointCharacterisationTests`, `DeviceAndKioskEndpointCharacterisationTests`,
   `WebhookRotationEndpointCharacterisationTests` (drive the mapped delegates in process).
3. **Source readers** — the seven declaration guards (spec §3).
4. **Analyzer** — the two scoped Release builds (spec §1): S107 7 -> 0, remainder identical, no S104.

Phase 5 extra observation: `grep -c -a SuppressMessageAttribute` on both built Api DLLs prints 0 (the probe
already observed it).

## Commits (each builds and passes on its own, ADR-0087)

1. `docs(specs): spec 401 - ...` (this artifact set).
2. `chore(2975): suppress S107 on the seven CameraCatalog and Identity route handlers per ADR-0084` — the
   five files.

One `src/` commit: there is no move to precede it and no new test to land first. Characterisation lane,
so no red/green pair.

## Risks

| Risk | Mitigation |
|---|---|
| Attribute emitted after all (`CODE_ANALYSIS` defined somewhere) | Both baselines fail loudly; that is a block, not an edit. Probe observed 0 in both DLLs. |
| `CameraEndpoints.cs` crosses S104 (7 lines of headroom) | Probe measured 293 and the build reported no S104; SC-2 compares the whole list. Any extra line beyond the prescribed form (a blank line, a comment) eats into it, so none is added. |
| Wording drifts from specs 397-400 | Copied byte for byte; SC-4 greps the rule-ID line; T030 diffs the attribute block against `LayoutEndpoints.Commands.cs`. |
| Attribute lands on a non-hit (`Retire`, `Disable`, `List` in Identity) | SC-4 counts exactly 4 + 3; review checks names. |
| Stale build measurement | `--no-incremental`; before and after on the same command; native `C:/` logfile path with `MSYS_NO_PATHCONV=1`. |
