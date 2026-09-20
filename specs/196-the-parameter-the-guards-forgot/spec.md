# Spec 196 — The parameter the guards forgot

**Issue:** [#2309](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2309)
(`bug`, `agent:ready`; Project #13, status Todo — verified with `--limit 2000`)
**Branch:** `2309-guard-grid-against-null`
**Phase:** 1 (Specify) — ADR-0037
**ADRs:** ADR-0105 (primary), ADR-0139, ADR-0144, ADR-0112, ADR-0059, ADR-0047, ADR-0036, ADR-0052
**Constitution:** §II (value objects), §Testing ("New behaviour", "Behaviour-preserving refactors")

---

## Summary

`GridDimensions grid` is the **only unguarded reference-type parameter in
`src/LayoutComposition/Domain/Layout/Layout.cs`**. Every other one — `fab`,
`name`, `tiles`, `clock` — carries `Ensure.That(...).IsNotNull()` per ADR-0105.
`grid` carries none, on any of the three public methods that take it, and
`ValidateGrid` dereferences it (`grid.Rows`) two statements in.

A null `grid` therefore leaves the aggregate through one of two undignified
exits instead of the `ArgumentNullException` ADR-0105 mandates:

| input | today | after |
|---|---|---|
| null `grid`, **non-empty** tiles | `NullReferenceException` at `ValidateGrid` (`grid.Rows`) | `ArgumentNullException`, `ParamName = "grid"` |
| null `grid`, **empty** tiles | `InvalidOperationException: "Grid  with 0 tile(s) violates Empty."` | `ArgumentNullException`, `ParamName = "grid"` |

The fix is three lines, and it introduces nothing: it makes `grid` follow the
guard pattern `tiles` already follows in the same file.

### What was verified before this spec was written

Every claim in #2309 was checked against the tree at `75ee9545`, not taken on
trust. Two were confirmed, one needed restating, and one new finding changes the
shape of the fix.

**1. Confirmed — the guards omit `grid`.** `Layout.cs:129-132` (`CreateDraft`)
guards `fab`, `name`, `tiles`, `clock`; `Layout.cs:190-191` (`EditDraft`) guards
`tiles`, `clock`. Neither names `grid`. `ValidateGrid` (`:70`) guards `tiles` at
`:72` and not `grid`, then reads `grid.Rows` at `:78`.

**2. Confirmed empirically — the degenerate message renders exactly as
reported.** Reproduced standalone on .NET 10 with the same record shape and the
same interpolation:

```
[Grid  with 0 tile(s) violates Empty.]
length=36
```

Two spaces after `Grid`, no exception from the interpolation itself.

**The issue's explanation of the mechanism is slightly off, and the correction
matters.** It reads as though something in the repo's message-building path
avoids calling `ToString()`. Nothing in the repo does anything of the kind.
`RequireValidGrid` (`:103-110`) interpolates unconditionally:

```csharp
$"Grid {grid} with {tiles.Count} tile(s) violates {violation.Value}."
```

The blank comes from the BCL: `DefaultInterpolatedStringHandler.AppendFormatted<T>`
tests `value is IFormattable` (false for `null`), then appends `value?.ToString()`
only when non-null. So `GridDimensions.ToString()` is simply never reached, and
**no code in this repository is choosing to be careful here** — which is
precisely why the message is degenerate rather than a crash. Recorded because
"the message-builder null-checks" would have been a wrong lesson to carry
forward.

**3. New finding — `grid` is alone.** A sweep of every parameter in `Layout.cs`
against its declared type:

| type | kind | guarded? |
|---|---|---|
| `FabIdentifier`, `LayoutName`, `CreatedAt`, `Tile`, `GridDimensions` | `sealed record` (reference) | all but `GridDimensions` |
| `IReadOnlyList<Tile>`, `IClock` | reference | yes, everywhere |
| `OperatorIdentifier`, `LayoutRevisionNumber`, `CameraIdentifier` | `readonly record struct` | **cannot be null** — `Ensure.That<T>` is `where T : class` |

So the file is not sloppy about guards: it is complete except for this one
parameter. `createdBy`, `by` and `number` look unguarded and are not holes —
they are value types. `grid` is the single omission, and it is also the only
unguarded parameter the methods **dereference**, which is why it is the only one
that produces a raw NRE.

**4. New finding — `tiles` is guarded in three places, `grid` in none.**
`Ensure.That(tiles).IsNotNull()` appears in `CreateDraft`, `EditDraft` **and**
`ValidateGrid`. The redundancy is the file's own convention (each public entry
point validates its own arguments — `LayoutGuardTests`' docstring states it as a
rule). The issue asks for two lines. **This spec delivers three**, because
`ValidateGrid` is public, is called directly by both command handlers, and is
the actual dereference site — leaving it out would preserve, one level down, the
exact asymmetry this issue is about. See §Locked technical choices for the
argument and the trim-back option.

**5. Confirmed — pre-existing, not a #2187 regression.** Before spec 138 / #2187
the NRE came from `Revision.NewDraft`, which reads `grid.Rows, grid.Cols` at
`Revision.cs:59`. #2187 moved the site, not the category.

---

## User stories

### US-1 (P1) — A null grid is refused at the boundary, by name

**As** a developer calling `Layout.CreateDraft` / `Layout.EditDraft` /
`Layout.ValidateGrid`,
**I want** a null `grid` to raise `ArgumentNullException` naming `grid`,
**so that** the failure says which argument was wrong at the method I called,
instead of an NRE from inside validation or a message with a hole in it.

This is the whole spec. There is no US-2: every other candidate below is
out of scope with a reason.

### Why one slice, and why it is not split further

Three lines in one file. The slicing rule asks for the smallest independently
shippable vertical; this is at the floor. Splitting `CreateDraft` from
`EditDraft` would produce two branches colliding on `Layout.cs` for a saving of
one line each (ADR-0109 marks `[P]` only for disjoint files).

---

## Acceptance scenarios

### AS-1 (US-1) — bad request: null grid with tiles present

```gherkin
Given a caller invoking Layout.CreateDraft with a valid fab, name, tile set,
      operator and clock
  And grid passed as null
When CreateDraft is called
Then an ArgumentNullException is thrown
  And its ParamName is "grid"
  And no NullReferenceException is thrown
```

Today this fails: the exception is a `NullReferenceException` from
`ValidateGrid` (`grid.Rows`). That failure is the phase-4a red.

### AS-2 (US-1) — bad request: null grid with an empty tile set

```gherkin
Given a caller invoking Layout.EditDraft on an existing draft
  And grid passed as null
  And an empty tile set
When EditDraft is called
Then an ArgumentNullException is thrown
  And its ParamName is "grid"
  And the message "Grid  with 0 tile(s) violates Empty." is not produced
```

This is the degenerate-message path. It is the more interesting red of the two,
because today it does **not** crash — it reports a grid violation about a grid
that does not exist, and a caller reading that message would go looking for the
wrong bug.

### AS-3 (US-1) — bad request: the public validator refuses it too

```gherkin
Given a command handler calling Layout.ValidateGrid directly, as both
      CreateLayoutDraft and EditDraftRevision do before invoking the aggregate
  And grid passed as null
When ValidateGrid is called
Then an ArgumentNullException is thrown
  And its ParamName is "grid"
  And no Option<GridViolation> is returned
```

### AS-4 (US-1) — conflict of faults: the earlier guard still wins

```gherkin
Given CreateDraft called with BOTH a null name and a null grid
When CreateDraft is called
Then the ArgumentNullException names "name", not "grid"
```

Guard order follows parameter order (`fab`, `name`, `grid`, `tiles`, `clock`),
so the new guard is inserted **between `name` and `tiles`** and cannot displace
an existing one. The sibling test
`CreateDraft_with_a_null_name_and_an_invalid_grid_still_throws_ArgumentNullException`
(`LayoutGridInvariantTests.cs:232-247`) already asserts the adjacent ordering
claim and must pass unmodified.

### AS-5 (US-1) — happy: every valid caller is untouched

```gherkin
Given the LayoutComposition domain and application test suites as they stand
When they run after the change
Then every test passes unmodified
  And no assertion has been edited
```

The behaviour-preserving half of constitution §Testing, over the population the
new guard cannot reach. If any existing assertion has to move, the change is
wrong — block, do not adjust.

### AS-6 (US-1) — the grid violations still work

```gherkin
Given a non-null grid and a tile set violating one of the four spec-010
      invariants (Empty, TooLarge, OutOfBounds, DuplicatePosition)
When CreateDraft or EditDraft is called
Then the InvalidOperationException still names the violation
  And the message still renders the grid as "RxC"
```

ADR-0112 §2's four invariants and their two-tier treatment (operator-facing
`Result` in the handler, programmer-error throw in the aggregate) are unchanged
by this spec. `LayoutGridInvariantTests` is the evidence.

### Auth / scope

**N/A.** The change is confined to the Domain layer of one bounded context. No
endpoint, no token, no `sse.*` scope, no fab authorization, no trust boundary.
The methods are already only reachable through handlers that authorize first.

---

## Independent end-to-end test procedure

A reviewer can reproduce every claim without reading the diff. From the worktree
root:

1. **Baseline, before any change.**
   `dotnet test tests/LayoutComposition.Domain.Tests/SmartSentinelEye.LayoutComposition.Domain.Tests.csproj`
   → all green. This is the characterisation capture for AS-5.
2. **Observe today's behaviour directly.** Add the three tests of AS-1/2/3 to
   `tests/LayoutComposition.Domain.Tests/Layout/LayoutGuardTests.cs` and run
   them. Expect: two failures reporting `NullReferenceException` where
   `ArgumentNullException` was expected, and one reporting the literal string
   `Grid  with 0 tile(s) violates Empty.` Keep the verbatim output.
3. **Apply the three guard lines** to `Layout.cs`.
4. **Re-run the same filter** → the three tests pass.
5. **Re-run the whole domain + application suites** → green, with no test file
   other than `LayoutGuardTests.cs` modified:
   `git diff --name-only HEAD -- tests/` must list exactly that one file.

No Docker, no Aspire fixture, no database — the Layout aggregate is pure domain
with a hand-written `TestClock` (ADR-0052, ADR-0054). ADR-0103 does not apply
because there is nothing to integrate against.

---

## Locked technical choices

| Concern | Choice | Authority |
|---|---|---|
| Guard idiom | `Ensure.That(grid).IsNotNull();` | ADR-0105, ADR-0059 |
| Exception type | `ArgumentNullException`, `ParamName` from `[CallerArgumentExpression]` | ADR-0105 |
| Guard placement | at the top of each public method, in parameter order | `Layout.cs` as it stands |
| Guard sites | `CreateDraft`, `EditDraft`, **and `ValidateGrid`** | see below |
| Test project | `LayoutComposition.Domain.Tests`, file `LayoutGuardTests.cs` | ADR-0052 |
| Assertions | xUnit + Shouldly; `Should.Throw<T>()` | ADR-0052 |
| Test naming | sentence-style with underscores | ADR-0053 |
| Production surface | `Layout.cs` only; no signature, no new type, no ADR | ADR-0036 |

### Why `ValidateGrid` gets the third line

The issue scopes the fix to two lines. Three is the right number, and the
argument is the file's own convention rather than preference:

- **`ValidateGrid` is public** (`Layout.cs:70`) and is not a helper nobody calls
  — both `CreateLayoutDraftCommandHandler` and `EditDraftRevisionCommandHandler`
  call it directly to map a violation to a `LAYOUT_GRID_*` 400 (ADR-0047,
  ADR-0112). A handler path that skips the aggregate would keep the unguarded
  dereference.
- **It already guards `tiles` and not `grid`.** Fixing only the two aggregate
  methods reproduces the exact asymmetry this issue reports, one frame down.
- **It is the dereference site.** `grid.Rows` is at `:78`. A guard on the line
  that reads the value is the one a reader finds when they follow the NRE.
- **Redundancy is the established pattern here, not a smell.** `tiles` is
  guarded three times for the same reason; `LayoutGuardTests`' docstring states
  it as the rule ("each aggregate behaviour validates its arguments at the top
  before doing any work").

**Trim-back option, if a reviewer prefers the issue's literal two lines:** drop
the `ValidateGrid` guard and delete AS-3 and its test. The aggregate paths are
still fixed — `RequireValidGrid` forwards `grid`, so
`[CallerArgumentExpression]` would still report `"grid"`. What is lost is the
direct-handler path and the symmetry with `tiles`. Recorded so the choice is
visible at the gate instead of buried in a diff.

### What this spec does **not** do

- **No architecture test for guard coverage.** Nothing mechanical could have
  caught this hole: `GuardBanWiringTests` checks that the *ban* on
  `ArgumentNullException.ThrowIfNull` is wired, not that every reference-type
  parameter is guarded. ADR-0139's principle ("rules that fail the build, not
  the review") points at such a test, and it would be a genuine improvement —
  but it would sweep every Domain and Application method in nine contexts, must
  encode at least four legitimate exemptions, and is a decision rather than a
  bug fix. **Recommended as a separate issue** (see §Out of scope). Adding it
  here would be exactly the drive-by widening whose refusal created #2309.
- **A missing `grid` in the request body answers 500, not 400** (*recommended
  follow-up, finding 2 — found while verifying this spec's phase-5 claim*).
  `CreateLayoutRequest.Grid` is a non-nullable `GridRequest`
  (`src/LayoutComposition/Api/Requests/CreateLayoutRequest.cs:11`), but nothing
  makes System.Text.Json enforce that: `RespectNullableAnnotations` is opt-in
  since .NET 9 and is configured nowhere in this repository — there is no
  `ConfigureHttpJsonOptions` call at all. A body omitting `"grid"` therefore
  deserialises to `Grid = null`, and both endpoints then evaluate
  `GridDimensions.From(body.Grid.Rows, body.Grid.Cols)`
  (`LayoutEndpoints.Commands.cs:143` and `:354`) **inside a `try` that catches
  only `ArgumentException`** (`:146`). The `NullReferenceException` escapes the
  catch and the caller gets a 500 where every sibling malformed-input case
  returns `400 LAYOUT_INVALID_INPUT`.

  **Not fixed here, and deliberately not**: it is the Api layer, it is a
  *trust-boundary* validation rather than an argument precondition (CLAUDE.md:
  "validate at trust boundaries only"; ADR-0105's guards are for programmer
  error), and its fix is a 400 problem response, not an `Ensure` guard. Folding
  it in would be exactly the drive-by widening whose refusal created #2309.
  **Filed as [#2480](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2480).**

  **Marked as unreproduced.** This is read out of the code, not observed against
  a running endpoint. The follow-up issue should start by sending
  `POST /layouts` without `grid` and recording the status.

- **No change to `RequireValidGrid`'s message.** Once `grid` cannot be null, the
  interpolation cannot render blank, so the degenerate message is fixed by
  removal of its cause rather than by defensive formatting. Adding a null-safe
  format string would be dead code the moment the guard lands.

---

## Phase-4a colour — **RED**, and the issue's own framing is wrong

#2309 declares "characterisation colour (behaviour-preserving on every valid
caller; only a null `grid` changes from one exception type/site to another
well-formed one)". The premise is true. The conclusion does not follow, and
ADR-0144 decides it the other way.

**1. "Behaviour-preserving on every valid caller" proves too much.** A guard
exists *for invalid callers*. By this standard every guard addition in the
repository is characterisation, and the one population a guard changes is the
one that would never be covered. The only observable effect of this change is on
the exact input a characterisation net declines to test.

**2. Something observable moves, and it is the deliverable.** The exception type
at a public API boundary is behaviour: `NullReferenceException` becomes
`ArgumentNullException`, and on the empty-tiles path an `InvalidOperationException`
carrying a misleading message becomes an `ArgumentNullException`. ADR-0105 says
of the original migration that "no `catch`/test that depended on the type or
message changes" — precisely because the types matched. Here they do not. A
caller catching `InvalidOperationException` around `EditDraft` today catches the
null-grid-plus-empty-tiles case and after this change does not.

**3. Characterisation cannot even express the deliverable.** Its contract is
that the covering tests pass **unmodified** before and after. A test asserting
today's `NullReferenceException` would have to be edited to assert
`ArgumentNullException` — and this repository's own deciding test is that an
assertion which has to be edited is evidence the behaviour moved. Writing it as
characterisation would mean encoding the NRE as the safety net, which is the
refactor-that-is-also-a-bug-fix trap ADR-0144 names.

**4. A real red is available for free.** Unlike spec 194, no counterfactual
mutation of correct production code is needed. Three tests, written against the
tree as it stands, fail deterministically today and pass after three lines.
When honest red is one `dotnet test` away, choosing green is choosing weaker
evidence for no saving.

**5. ADR-0144's tie-break points the same way.** "Ambiguity resolves to red —
that path fails loudly, the other passes quietly."

**Both obligations still hold, over different populations.** The ~20 existing
`LayoutComposition.Domain.Tests` and the application-layer handler tests are the
**characterisation net**: captured green at T001, must pass **unmodified**
afterwards (AS-5, AS-6). The three new tests are **red-first** (AS-1, AS-2,
AS-3). That is not a compromise between the colours; it is what constitution
§Testing asks for when a change adds behaviour to a covered path.

**The verbatim red is quoted in the PR body.** Specifically including the
current literal message `Grid  with 0 tile(s) violates Empty.` — it is the only
record anyone will ever have of it, and a later reader cannot reproduce it once
the guard lands.

---

## Latency budget impact

**N/A.** No leg of `event arrival → overlay rendered ≤ 800 ms` is touched.

The reasoning, since LayoutComposition does sit near the path: the three guards
are on `CreateDraft`, `EditDraft` and `ValidateGrid` — the **admin write** paths
reached from `POST`/`PATCH` layout endpoints, not from the event fan-out. The
*event* → overlay leg runs through `LayoutRevisionPublishedDomainEvent` and the
SignalR broadcasters, none of which this spec touches. The added cost on the
write path is one reference comparison per call, on a path already doing a
database round trip. §VII's dashboard obligation for implemented legs (ADR-0117)
is unaffected because no leg changes.

---

## Out of scope

- **A guard-coverage architecture test** (*recommended follow-up, finding 1*).
  Argued above. **Recommended as a
  follow-up issue**, with this spec as the motivating example: nine contexts,
  needs exemptions for `Shared.Contracts`, `AppHost`, generated migrations and
  value types, and is a decision rather than a fix. The lane may not write the
  ADR it would need (ADR-0144).
- **`RequireValidGrid`'s message format.** Fixed by cause removal, not by
  formatting.
- **`Revision.NewDraft`'s `grid.Rows` read** (`Revision.cs:59`). It is `internal`
  and reached only through `CreateDraft`/`BranchDraft`, both of which are guarded
  after this change. Guarding it too would be defence at a non-boundary.
- **Any other file in LayoutComposition.** The sweep in §3 above found no second
  unguarded reference-type parameter in `Layout.cs`; other aggregates were not
  swept and are not this issue's business.
- **The `2309-guard-grid-against-null` branch name lacking a `fix/` prefix.**
  Inherited from the worktree as handed over; noted, not corrected here.

---

## Assumptions and flags

- **Marked guess — none load-bearing.** Every factual claim above was read out
  of the tree or reproduced; the one inference is that no *external* caller
  outside this repository catches `InvalidOperationException` around `EditDraft`
  for the null-grid case. The Domain assembly is not published as a package and
  `BoundaryTests` confines its callers to this solution, so the blast radius is
  the repository.
- **Flagged for the gate, not blocking:** the two-versus-three-line choice in
  §Locked technical choices. The spec commits to three with reasons; the
  trim-back is one deletion if reviewed otherwise.

## Gate — phase 1

No `[NEEDS CLARIFICATION]` remains. Hand back for review before phase 2.
