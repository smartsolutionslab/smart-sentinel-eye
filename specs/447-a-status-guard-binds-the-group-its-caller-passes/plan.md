# Plan 447: A status guard binds the group its caller passes

**Spec:** [spec.md](spec.md) · **Tasks:** [tasks.md](tasks.md) · **Issue:** #3127

## Slices

One slice: US1 (code, `tests/Architecture.Tests/StatusProducerDeclarationTests.cs`) and US2 (comment-only,
`tests/Architecture.Tests/RouteValueRefusalDeclarationTests.cs`). Test code only; no `src/` change.

## Bounded context and layers

None — architecture guards. No Domain/Application/Infrastructure/Api code, messaging, persistence, AppHost resource
or contract. Boundary rules unaffected. Latency: N/A.

## Port, not extract — the smallest diff

`EndpointScopeDeclarationTests`' binding (spec 444) is coupled to that file's own types: its `RouteGroup` carries an
`EnforcedAuthorization`, an unresolved helper becomes an *Unreadable* `RouteGroup` with problem text, and
`GroupBinding` also carries `GroupScope`/`IsRedeclared` block-scoping added in 444's review. Extracting a shared binder
means re-plumbing that 2820-line file and re-characterising its cases for no behaviour gain there. StatusProducer needs
only "which group, or none". RouteValueRefusal needs nothing (spec premise 3). So:

- **StatusProducer:** port the name-level half of 444's rule as private members (~90 lines), mirroring 444's names so
  a later extraction is mechanical: `GroupHelperDeclaration`, `RouteBuilderParameter`, `ParameterListEnd`, `BodyAfter`,
  `ResolveHelperGroup` (returns `RouteGroup?`), and a private `ParameterScope(Name, From, To, RouteGroup? Bound)`.
  Regexes copied verbatim from `EndpointScopeDeclarationTests.cs:480-511` (`RouteBuilderDeclaration`,
  `GroupHelperDeclaration`, `EqualsAssignment`).
- **RouteValueRefusal:** doc-only on `PrefixOf`.

Extraction stays possible later; with two copies and one positional third (`ConcurrencyConflict`), it is not yet
earning its diff. No follow-up issue is filed for it.

## The reader's change (StatusProducer)

- `Mappings(file, text, masked, groups)` gains, before its loop, `var parameters = ParameterScopes(masked,
  groupsInFile)` where `groupsInFile` = `groups` filtered to `file`, keyed by `Variable` (first wins — today's
  `FirstOrDefault` semantics, premise 6 left alone).
- `ParameterScopes`: for each `GroupHelperDeclaration` match → body span via `BodyAfter`, `Bound` =
  `ResolveHelperGroup` (every `\b{method}\b` outside the declaration span must be a call with exactly one bare-identifier
  argument naming a key of `groupsInFile`; 0 calls, a non-call reference, a non-group argument or ≥2 distinct groups →
  `null`). For each `RouteBuilderParameter` match not followed by `=` (`EqualsAssignment`) and not already a helper
  parameter → `ParameterScope(name, from, to, null)`.
- Group lookup (`:866-868`) becomes: innermost `ParameterScope` with `Name == receiver` enclosing `call.Index` →
  its `Bound`; otherwise today's by-name lookup. **Invariant: a parameter name is never looked up by name.**
- `Classify` and its precedence are unchanged; an unresolved helper reaches it as `group: null` → `Unclassified` →
  L5 red. L5's message gains one clause naming "a helper whose group could not be established from its in-file call
  sites (called never, with a non-group argument, or with two groups)". No new `RouteMapping` field.
- Class doc `:62-70`: "the `MapGroup` statement whose bound variable is the mapping's receiver" → the group bound to the
  receiver, where a helper's parameter takes the group its in-file callers pass (spec 444's rule); unresolved → none.
  L5 doc `:425-429` already says "the reader stops binding a mapping to its group … they become unclassified" — keep.

## RouteValueRefusal (US2)

`PrefixOf` doc (`:741-744`) gains: binds by receiver name, so a helper parameter gets the namesake local's prefix; it
words G1's message only, no assertion reads it; before any verdict depends on it, port call-site binding (spec 444/447).
Comment-only — characterised by hashing comment-stripped code before/after (memory rule), not by asserting prose.

## Phase 4a — colours (declared here, ADR-0144)

- **Characterisation (green first, T000):** run `StatusProducerDeclarationTests` (33) and
  `RouteValueRefusalDeclarationTests` (21) at `9b8b33a3`; quote totals; record the comment-stripped SHA-256 of
  `RouteValueRefusalDeclarationTests.cs`. After T003/T004 the same 54 cases pass **unmodified** — no existing
  `[Fact]`/`[Theory]` body or expected value edited.
- **Red (T001):** new `[Fact]`s in `StatusProducerDeclarationTests`, fed raw-string fixtures through the existing
  private `RouteGroups(file, text, masked)` and `Mappings(file, text, masked, groups)` — already a seam, no refactor
  needed. Masking via `SourceMask.Apply(text, MaskStrictness.CommentsAndLiteralInteriors)` exactly as `Read` does.
  One fact per Gherkin scenario: control (green before and after, labelled control), mismatch, two-groups,
  non-group-argument, never-called, multi-parameter. **The five non-control facts must be observed red** against the
  unchanged reader, failure quoted. Assert on `RouteMapping.Kind` and `FullRoute` (computed by the reader) — never on
  fixture text. A non-control fact arriving green is a phase-4 failure.
- **Planted-violation facts per guard (the issue's demand):** StatusProducer — spec procedure step 2 (red after, green
  before, both quoted). RouteValueRefusal — premise 3 already observed it green **and correct**; there is no red to
  produce because no verdict reads the binding. That is reported as the finding, not papered over with a contrived test.
- **If an existing case goes red after T003, it is a real misbinding in `src/`** — stop, report the verbatim failure;
  do not adjust the binding, do not edit `src/` in this spec.

### Fixtures

Raw string literals in the class (ADR-0054). Each contains a `static class` with an entry method declaring the groups
and calls, and the helpers. The authorized group uses bare `.RequireAuthorization()` (StatusProducer's antecedent does
not read the scope). Synthetic file name `"fixture/HelperEndpoints.cs"`; nothing reads disk.

### Counterfactuals (T005; each run, failure quoted, reverted)

1. Spec procedure step 2 (`MapListEvents(writes)`) → L5 red.
2. Spec procedure step 3 (second call with `writes`) → L5 red.
3. Revert only the "parameter is never name-bound" fallthrough (let a non-helper parameter fall back to by-name) →
   the multi-parameter fact goes red.

## Why no ADR

StatusProducer's own class doc already states the antecedent as the `MapGroup` the mapping "was written on" (`:30`,
`:246`); name binding is a reader defect against that. No locked choice moves; no `src/` shape is banned; the rule
ported is spec 444's, already accepted without an ADR.

## Risks

- **R1 — two copies of the binder drift.** Accepted; names mirrored, both cite spec 444 in their doc.
- **R2 — helper regex over/under-matches.** Any unrecognised parameter receiver binds to nothing → L5 red, never
  green (same failure direction as 444's R1).
- **R3 — `FirstOrDefault` duplicate-name binding.** Unchanged and out of scope (spec premise 6).
