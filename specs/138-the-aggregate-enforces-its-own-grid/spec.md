# Spec 138 — The `Layout` aggregate enforces its own grid invariants

**Issue:** [#2187](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2187)
**Branch:** `fix/2187-the-aggregate-enforces-its-own-grid`
**Status:** Phase 1 — Specify
**ADRs:** ADR-0112 §2 (the grid invariants, *"enforced inside the aggregate"*),
ADR-0047 (Result for expected failures, exceptions for programmer error),
ADR-0105/0059 (`Ensure.That` argument guards), ADR-0144 (autonomous lane),
ADR-0139 (new behaviour starts red). Extends spec 010 (multi-tile layouts).
**Latency budget (§IV):** **N/A.** Nothing on the `event arrival → overlay
rendered` path changes. The added check is an O(n≤4) list scan on the
LayoutComposition **write** path (management API), which is not one of the six
legs. See §6.

---

## 1. Problem

`Layout.ValidateGrid` is the single source of truth for the four spec-010 grid
invariants (≥1 tile, ≤ the max-tiles ceiling, in-bounds, no duplicate position).
It has **zero callers inside the domain**. Both write paths go straight past it:

- `Layout.CreateDraft` (`Layout.cs:95-120`) calls `Revision.NewDraft` directly.
- `Layout.EditDraft` (`Layout.cs:157-165`) calls `target.ReplaceTiles` directly.

Every actual call site is in the Application layer —
`CreateLayoutDraftCommandHandler.cs:23` and `EditDraftRevisionCommandHandler.cs:22`.

Four doc comments tell the next reader the opposite:

| File:line | Claim |
|---|---|
| `GridPosition.cs:9-11` | *"the upper bound … is validated by the owning `Layout` aggregate"* |
| `Revision.cs:16-17` | *"The grid invariants are validated by the owning aggregate before the tile set is set here."* |
| `Revision.cs:108-109` | *"The aggregate validates the grid invariants before calling this"* |
| `Layout.cs:16-19` | *"`ValidateGrid` is the single source of truth … command handlers call it"* — honest, but describes a contract the aggregate does not hold |

And `Layout.cs:93` / `Layout.cs:154` say the reverse of `Revision.cs`:
*"The grid + tiles must already be valid (`ValidateGrid`)"* — i.e. the caller's
job. **One bounded context, two contradictory contracts, one file apart.**

**Consequence.** The aggregate will today accept and persist a revision with zero
tiles, two tiles at the same position, a tile at `(5,5)` on a 2×2 grid, or an
oversized grid. Nothing fails; the malformed revision is written, published, and
first appears as a **silently dropped tile on the wall** — the kiosk renderer
drops out-of-bounds tiles defensively on the same false premise
(`apps/kiosk-web/src/features/cell/CellPage.tsx:473-477`: *"the aggregate already
enforces in-bounds; this keeps the renderer total"*). Two layers each believe the
other checked.

This also sits against constitution §II and ADR-0112 §2, which states the
invariants are *"enforced inside the aggregate"* — the decision was made; the
code does not implement it.

## 2. What was verified against the tree (2026-09-13, `fix/2187-…` at `4a0bae62`)

Every claim below was re-checked rather than taken from the issue.

- ✅ `ValidateGrid` has no caller in `src/*/Domain`. `git grep -n ValidateGrid -- src`
  returns the declaration (`Layout.cs:66`), three doc references, and the two
  Application handlers. Confirmed.
- ✅ `Layout.cs:93` and `:154` contradict `Revision.cs:16` and `:108`. Confirmed verbatim.
- ✅ Domain tests exercise `ValidateGrid` as a standalone static across all six
  outcomes (`LayoutTests.cs:249-311`) and **never** assert that `CreateDraft` or
  `EditDraft` reject anything. Confirmed.
- ✅ **Write paths into `Revision`'s tile set — the full set is three, not two:**
  | Path | Reaches tiles via | Caller-supplied input? | Guard needed? |
  |---|---|---|---|
  | `Layout.CreateDraft` | `Revision.NewDraft` | **yes** (handler → HTTP body) | **yes** |
  | `Layout.EditDraft` | `Revision.ReplaceTiles` | **yes** (handler → HTTP body) | **yes** |
  | `Layout.BranchDraft` | `Revision.Branch` → `Revision.NewDraft` | **no** — copies `baseRevision.Grid` / `.Tiles` | **no**, see §5 |
  | EF materialisation | `private Revision()` + property setters | n/a (read) | **no** — a guard here would fail a *read* |
- ✅ `Revision.ReplaceTiles` is reachable from **exactly one** place:
  `Layout.cs:163`. It is `internal` and has no other caller in `src` or `tests`.
- ✅ `Revision.NewDraft` / `Revision.Branch` are `internal`; the only `src`
  callers are `Layout.cs:117` and `Layout.cs:144`.
- ✅ `Layout.CreateDraft` / `EditDraft` have **no `src` caller outside the two
  handlers**. `ScenarioSimulator` seeds over HTTP
  (`Seeding/LayoutCompositionClient.cs`), so it routes through the handlers.
- ✅ **No existing test passes an invalid grid through `CreateDraft`/`EditDraft`.**
  All 13 call sites in `tests/` (`LayoutBuilder.cs:77`, `LayoutGuardTests.cs:25/38/62`,
  `LayoutTests.cs:175/245/329/360/435/460`, `LayoutChainArchivalTests.cs:136`,
  `LayoutRevisionStateMachineTests.cs:108`,
  `Integration.Tests/LayoutComposition/LayoutNameUniquenessIntegrationTests.cs:87`)
  supply a valid 1×1 or in-bounds 2×2 grid. **No existing test flips red.**
- ✅ **No `DomainException` / `InvariantViolationException` convention exists.**
  `git grep "class .*Exception" -- src` returns nine types, none of them a domain
  invariant type: `AelParseException`, two Keycloak client exceptions,
  `FabAuthorizationException`, `UnattributableOperatorException`, and four
  `IExceptionHandler` implementations. Nothing to reuse but the BCL.
- ✅ **The existing domain convention for "must never be reachable" is
  `InvalidOperationException`**, used at four sites in this very aggregate
  (`Layout.cs:139`, `Layout.cs:293`, `Revision.cs:88`, `:99`, `:116`) and stated
  in `Layout.cs:20-21`: *"Illegal state transitions keep throwing
  `InvalidOperationException` (programmer error)."*
- ✅ `Ensure.That` **cannot** express this check: `EnsuredObject<T>` offers only
  `IsNotNull()` (`Ensure.cs:152-206`). `Satisfies` exists on `EnsuredString` and
  `EnsuredValue<T>` only. Using `Ensure` would mean adding a member to
  `Shared.Kernel` — out of scope and speculative generality.

## 3. The design decision — how the aggregate reports the violation

**Chosen: throw `InvalidOperationException` from `CreateDraft` and `EditDraft`
when `ValidateGrid` returns `Some`, with a message naming the violation. The two
Application handlers are untouched.**

### Why this, and not a signature change

- **ADR-0112 §2 already prescribes exactly this split**, verbatim: *"Invariants
  enforced inside the aggregate (`Ensure.That`, `Result<T,Error>` **at the command
  boundary**)."* Two tiers were the decision. The handlers own the `Result`; the
  aggregate owns the throw. The fix restores ADR-0112 §2 rather than revisiting it.
- **ADR-0047 puts it in the right category.** *"Expected business failures use
  `Result<T, Error>` … Exceptions are reserved for programmer errors."* An
  operator submitting a duplicate tile is an expected business failure and still
  gets `LAYOUT_GRID_DUPLICATE_POSITION` / `400` from the handler — unchanged. A
  violation *arriving at the aggregate* means a caller skipped a check the
  contract requires: programmer error, by ADR-0047's own definition.
- **It reuses the convention already in this file.** Five throw sites in
  `Layout`/`Revision` use `InvalidOperationException` for exactly this role, and
  `Layout.cs:20-21` documents it as the aggregate's rule. Nothing is invented.
- **It is unreachable through the existing handlers**, which validate first — so
  no HTTP behaviour changes for any live caller. It is a backstop for the
  population that grows: a saga, an import, a migration backfill, a bulk-clone
  command, a future handler.
- **Option 2 (`Result<Layout, GridViolation>` / `Result<Unit, GridViolation>`)
  is rejected.** It changes both handler call sites, forces every future caller
  to unwrap a failure that the handler has already ruled out one line earlier,
  and converts a programmer error into a value that can be ignored — which is the
  defect this spec exists to fix. It also contradicts ADR-0047's category split
  and ADR-0112 §2's explicit "at the command boundary" placement.
- **A new `GridViolationException` is rejected.** No such convention exists
  (§2), and a bespoke type buys nothing over the one the aggregate already
  throws. Constitution §IX / Karpathy: no speculative generality.

### Why no new ADR is needed

The architectural question — *where do the grid invariants live, and in what
form do they surface* — was **answered by ADR-0112 §2 in 2026-06-04** and has not
been reopened. ADR-0047 already fixes the exception-vs-`Result` category rule
repo-wide. This spec chooses no new mechanism, introduces no new type, adds no
new layer contract, and changes no public signature. It makes the code do what
two accepted ADRs already say. **That is an implementation defect, not an
architectural decision — ADR-0144's prohibition on the autonomous lane making
architectural decisions is not engaged.**

### The HTTP error surface does not move

| Path | Before | After |
|---|---|---|
| `POST /layouts` with a duplicate-position tile set | handler returns `LAYOUT_GRID_DUPLICATE_POSITION` / `400` | **unchanged** |
| `PATCH /layouts/{id}/revisions/{n}` with an out-of-bounds tile | handler returns `LAYOUT_GRID_OUT_OF_BOUNDS` / `400` | **unchanged** |
| A future caller that skips the handler check | silently persists a malformed revision | `InvalidOperationException` → Wolverine/ASP.NET → `500` |

Double validation is intentional and cheap (≤4 tiles). The handler's check is
primary and owns the mapping; the aggregate's is the backstop.

## 4. User stories

### US1 (P1) — The aggregate cannot be put into an invalid grid state

*As the LayoutComposition domain, I refuse a grid + tile set that violates a
spec-010 invariant, whichever caller hands it to me, so that a malformed revision
cannot be persisted, published, or silently half-rendered on the wall.*

This is the whole slice: one bounded context, one aggregate, two methods, no
schema change, no contract change, no frontend change. It is independently
shippable and independently observable (§7).

### US2 (P1, same slice) — The domain's doc comments and its behaviour agree

*As the next reader of `LayoutComposition.Domain`, every statement about who
validates the grid is true.* `Layout.cs:93` and `:154` are corrected;
`GridViolation.cs`'s doc records the two-tier arrangement explicitly so the
"`Result`, not a thrown exception" line is not read as forbidding the backstop.

*(US2 is not separable — shipping the guard without correcting `Layout.cs:93/154`
would leave the same one-file contradiction pointing the other way.)*

## 5. `BranchDraft` — deliberately **not** guarded

`BranchDraft` is a third write path into `Revision`, and it is **excluded on
purpose**. The reasons, in order:

1. **It takes no caller-supplied grid or tiles.** Its arguments are `by` and
   `clock`; the grid and tiles come from `baseRevision.Grid` / `.Tiles` —
   the aggregate's own already-persisted state. There is no trust boundary here.
2. **The invariant holds by induction once US1 lands.** Every revision is created
   by `CreateDraft` (guarded), edited by `EditDraft` (guarded), or copied by
   `BranchDraft` from one of those. A guard would be unreachable by construction.
3. **A guard there could strand a chain.** If a malformed row *did* exist,
   throwing in `BranchDraft` turns a currently-working branch operation into a
   `500` with no recovery path — trading a dropped tile for an unusable layout.
4. **No malformed row is known to exist.** ADR-0112 §3's migration backfilled
   every pre-grid revision as a single tile at `(0,0)` on a 1×1 grid — valid by
   construction — and every revision since was written through a handler that
   validates.

Recorded here rather than left implicit, because "are there other write paths"
is the question this class of defect is found by.

**Repairing pre-existing malformed rows is explicitly out of scope** — none are
known to exist, and a repair path with no population to repair is speculative
generality.

## 6. Latency-budget impact (constitution §IV)

**N/A — no leg is touched.** The change adds one `ValidateGrid` call
(`tiles.Count`, one `Any`, one `Distinct` over ≤ 4 elements) to two methods on
the LayoutComposition **write** path, reached by `POST /layouts` and
`PATCH /layouts/{id}/revisions/{n}` from management-web. That path is not the
`event arrival → overlay rendered` path and is not one of the six legs in §IV.
No §VII dashboard obligation attaches.

The defect it fixes is a *correctness* failure that presents as a missing tile,
not a latency failure.

## 7. Acceptance scenarios (Gherkin)

### Happy path — a valid grid is still accepted

```gherkin
Scenario: CreateDraft accepts a valid full 2x2 wall
  Given four tiles at (0,0), (0,1), (1,0) and (1,1)
  When Layout.CreateDraft is called with a 2x2 grid and those tiles
  Then a Layout is returned with one Draft revision carrying four tiles

Scenario: CreateDraft accepts a sparse grid
  Given one tile at (0,0)
  When Layout.CreateDraft is called with a 2x2 grid and that tile
  Then a Layout is returned with one Draft revision carrying one tile

Scenario: EditDraft accepts a valid replacement tile set
  Given a Layout whose revision 1 is a Draft
  When EditDraft replaces its tiles with two in-bounds tiles on a 2x2 grid
  Then the revision carries two tiles and no new revision is spawned
```

### Invariant violation — the four refusals (the red tests)

```gherkin
Scenario Outline: CreateDraft refuses a tile set that violates a grid invariant
  When Layout.CreateDraft is called with <grid> and <tiles>
  Then it throws InvalidOperationException
  And the message names <violation>
  And no Layout is returned

  Examples:
    | grid | tiles                                  | violation         |
    | 1x1  | (none)                                 | Empty             |
    | 2x2  | two tiles both at (0,0)                | DuplicatePosition |
    | 1x1  | one tile at (0,1)                      | OutOfBounds       |
    | 3x3  | one tile at (0,0)                      | TooLarge          |

Scenario Outline: EditDraft refuses a tile set that violates a grid invariant
  Given a Layout whose revision 1 is a Draft
  When EditDraft is called with <grid> and <tiles>
  Then it throws InvalidOperationException
  And the revision's existing grid and tile set are unchanged

  Examples:
    | grid | tiles                                  | violation         |
    | 1x1  | (none)                                 | Empty             |
    | 2x2  | two tiles both at (0,0)                | DuplicatePosition |
    | 1x1  | one tile at (0,1)                      | OutOfBounds       |
    | 3x3  | one tile at (0,0)                      | TooLarge          |
```

### Bad request — the handler's surface is unchanged

```gherkin
Scenario: The HTTP error shape for an operator's bad grid does not move
  Given an authenticated operator with sse.layouts.write in fab "munich"
  When they POST /layouts with two tiles at the same position
  Then the response is 400
  And the error code is LAYOUT_GRID_DUPLICATE_POSITION
  And no InvalidOperationException is thrown
  # the handler validates first; the aggregate's guard is never reached

Scenario: The edit path's error shape does not move either
  When they PATCH /layouts/{id}/revisions/1 with an out-of-bounds tile
  Then the response is 400 with LAYOUT_GRID_OUT_OF_BOUNDS
```

### Argument guards still win the ordering

```gherkin
Scenario: A null argument still throws ArgumentNullException, not InvalidOperationException
  When Layout.CreateDraft is called with a null name and a valid grid
  Then it throws ArgumentNullException
  # the Ensure.That guards run before the grid check
```

### Conflict / state — the existing guard still wins

```gherkin
Scenario: Editing a Published revision still reports the state error, not a grid error
  Given a Layout whose revision 1 is Published
  When EditDraft is called with a VALID grid and tiles
  Then it throws InvalidOperationException naming the Published state
  # ReplaceTiles' Draft guard is unchanged

Scenario: Editing a Published revision with an INVALID grid fails on the grid first
  Given a Layout whose revision 1 is Published
  When EditDraft is called with an empty tile set
  Then it throws InvalidOperationException naming the violation
  # the grid check precedes RequireRevision/ReplaceTiles — deliberate: the
  # argument is bad regardless of which revision it targets
```

### Auth — unchanged, and asserted so

```gherkin
Scenario: Authorization is untouched
  Given an unauthenticated caller
  When they POST /layouts
  Then the response is 401
  # no endpoint, scope, or guard is modified by this spec
```

### BranchDraft — still copies without a new refusal

```gherkin
Scenario: BranchDraft is unaffected
  Given a Layout with a Published revision carrying a valid 2x2 tile set
  When BranchDraft is called
  Then a new Draft revision is returned carrying the same grid and tiles
  And nothing throws
```

## 8. Independent end-to-end test procedure

Runnable by a reviewer with no knowledge of the diff.

1. **Prove the backstop exists (the point of the change).**
   ```
   dotnet test tests/LayoutComposition.Domain.Tests --filter "FullyQualifiedName~LayoutGridInvariant"
   ```
   Expect 8 passing facts — the four violations × the two write methods.
   On `origin/develop` these same tests fail (the aggregate accepts each one).

2. **Prove the HTTP surface did not move.** Boot the Aspire stack, mint an admin
   token, and POST a duplicate-position tile set:
   ```
   POST /layouts  { "grid": {"rows":2,"cols":2},
                    "tiles": [{"cameraIdentifier":"…","row":0,"col":0},
                              {"cameraIdentifier":"…","row":0,"col":0}] }
   ```
   Expect **`400` with `LAYOUT_GRID_DUPLICATE_POSITION`** — *not* `500`. A `500`
   means the handler check was removed, which this spec forbids.

3. **Prove the happy path still works end to end.** Create a valid 2×2 wall over
   HTTP, publish it, and confirm four tiles render on the kiosk. Any tile
   disappearing means the guard is over-tight.

4. **Prove the docs now agree.** `grep -n "must already be valid" src/LayoutComposition/Domain/Layout/Layout.cs`
   returns nothing.

## 9. Locked tech choices

| Concern | Choice | Authority |
|---|---|---|
| Where the invariant lives | Inside the `Layout` aggregate | ADR-0112 §2 |
| How the aggregate reports it | `throw new InvalidOperationException` | ADR-0047, `Layout.cs:20-21` |
| How the operator sees it | `Result<T, Error>` → `LAYOUT_GRID_*` / `400`, at the command boundary, **unchanged** | ADR-0047, ADR-0089, ADR-0112 §2 |
| Validation source of truth | `Layout.ValidateGrid` — reused, not duplicated | ADR-0112 §2 |
| Argument guards | `Ensure.That(x).IsNotNull()`, ordered before the grid check | ADR-0105 |
| Test framework | xUnit + Shouldly, `LayoutBuilder` fluent builder | ADR-0052, ADR-0054 |
| Test naming | Sentence-style with underscores | ADR-0053 |
| Phase 4a colour | **Red** — behaviour-changing | ADR-0139, ADR-0144 |

## 10. Out of scope

- Removing or altering the handlers' `ValidateGrid` call (would change the HTTP
  error shape — explicitly excluded by the issue).
- Changing `CreateDraft` / `EditDraft` signatures (§3).
- Guarding `BranchDraft` (§5).
- Repairing hypothetical pre-existing malformed rows (§5).
- The kiosk's defensive out-of-bounds drop (`CellPage.tsx:473-477`) — its comment
  becomes **true** after this change; the defensive code stays, because a renderer
  that is total is still the right renderer.
- Any `OverlayDesigner` change. It has its own `CreateDraft`/`EditDraft`/`Branch`
  with a `Label` payload and no grid — no equivalent defect.

## 11. Assumptions and unresolved items

- **Assumption (stated, unverified by measurement):** no malformed revision
  exists in any live database. Reasoned from ADR-0112 §3's 1×1 backfill plus the
  handlers having validated since spec 010, not from a query against production —
  **there is no production deployment** (ADR-0118). Dev volumes are not surveyed.
  If one did exist, the effect is confined to `BranchDraft` (§5) and to reads;
  the new guards only refuse *new* writes.
- **No `[NEEDS CLARIFICATION]` items remain.**
