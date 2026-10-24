# Plan 444: A route is bound to the group it is mapped on

**Spec:** [spec.md](spec.md) · **Tasks:** [tasks.md](tasks.md) · **Issue:** #3101

## Slices

One slice, US1. Test code only: `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`. No `src/` change.

## Bounded context and layers

None — an architecture guard. No Domain/Application/Infrastructure/Api code, no messaging, no persistence, no
AppHost resource, no contract. Boundary rules unaffected (Architecture.Tests already references every Api project).
Latency: N/A.

## Entities / invariants (of the reader)

- `RouteGroup` (existing record): one per group local; names are already unique per file (duplicate declaration →
  Unreadable, `:1473-1488`).
- **New: `GroupHelper`** (private record in the same file): `Method`, `Parameter`, body span `(From, To)`.
  Recognised shape only: `\b(?<method>\w+)\s*\(\s*RouteGroupBuilder\s+(?<parameter>\w+)\s*\)\s*(?<body>\{|=>)` on the
  masked text. Body span: `{` → `RouteChainReader.Balanced(masked, open, '{', '}')`; `=>` →
  `RouteChainReader.StatementEnd(...)`. This matches all 65 helpers in the 13 helper-using files (the four LayoutComposition /
  StreamDistribution files use `=>`).
- **New: parameter spans.** Every method parameter list in the file that declares a route-builder type
  (`RouteBuilderDeclaration` matches not followed by `=`) yields `(name, enclosing body span)`. Used only for FR-001:
  a receiver that is such a parameter, inside that body, and *not* a recognised `GroupHelper` parameter, binds to no
  group. Invariant: **a parameter name is never looked up in the group dictionary.**
- **Call-site resolution** for a `GroupHelper` `h`: every `\b{h.Method}\s*\(` in the masked text outside `h`'s own
  declaration; for each, `RouteChainReader.SplitArguments` on its argument span. Result:
  - 0 calls → Unreadable "helper '{Method}' is never called in this file, so the group its '{Parameter}' parameter
    receives cannot be established";
  - any call with ≠1 argument, or an argument that is not a bare identifier naming a key of `groups` → Unreadable
    naming the argument text;
  - calls naming ≥2 distinct groups → Unreadable naming them;
  - otherwise → that `RouteGroup` (its prefix *and* its authorization; the prefix matters for the route string the
    register and messages use).
  A group whose own entry is the duplicate-declaration Unreadable stays Unreadable (unchanged).

## Where the change lands

1. **Seam (behaviour-preserving, test-writer, T001):** split `Read(string file)` into
   `Read(string file) => Read(file, ReadRepositoryFile(file))` and `Read(string file, string source)`. Nothing else.
   The 106 cases are its characterisation.
2. **Binding (engineer, T004):** in `Read(file, source)`, after `groups` is built, compute helpers and parameter
   spans; pass a resolver to `ReadMapping` replacing `groups.TryGetValue(receiver, out var group)` (`:1535`) with:
   receiver is a `GroupHelper` parameter enclosing the site → call-site resolution; receiver is another parameter
   enclosing the site → `group = null` (existing `ReceiverProblem` text applies, or own chain); else → today's lookup.
   `ReceiverProblem` gains the helper-specific details above via the Unreadable `EnforcedAuthorization` it returns.
3. **Doc (engineer, T005):** replace `:218-222`'s "No such indirection exists today" with the truth (in-file helpers
   are universal and are resolved at the call site; cross-file helpers are not read and are Unreadable), and rewrite
   `:282-301` to state call-site binding, keeping the duplicate-declaration paragraph and its reviewer history.
   Methods stay within ADR-0084's advisory limits where practical (extract `ResolveHelperGroup`).

`OutsideTheChainSites`/`RouteBuilderNames` (`:1760-1804`) use names only to decide whether a bare `.Map(` receiver
is a route builder, not to credit a scope — unchanged.

## Phase 4a — colours (declared here, ADR-0144)

- **Characterisation (green first, T000):** run the class (106 cases) and the nine
  `*RouteTableCharacterisationTests` at `99516703`; quote the totals. After T001 (seam) and after T004 (binding) the
  same cases pass **unmodified** — no existing `[Fact]`/`[Theory]` body or expected value is edited. The doc-comment
  edits of T005 are prose, not assertions.
- **Red (T002):** new facts fed fixtures through `Read(file, source)`, one per Gherkin scenario: happy (green both
  before and after — it is the control, labelled as such in the PR), conflict-mismatch, conflict-two-groups,
  bad-argument (two cases: inline `MapGroup` expression; non-group identifier), never-called, parameter-not-name-bound.
  The five non-control facts must be **observed red** against the unchanged reader, failure quoted. A non-control fact
  arriving green is a phase-4 failure. Facts assert on `EndpointMapping.Kind`, `.ScopeConstant`, `.Route` and
  `.Problem` (contains the helper name) — not on the fixture text (an assertion must not check its own input).
- **Does the hardening turn the existing suite red?** No: premise 3 measured every helper's call argument equal to
  its parameter name. If T004 turns any existing case red, that is a **real misbinding in `src/`** — stop and report
  it with the verbatim failure; do not adjust the binding to pass, do not edit `src/` in this spec.

### Fixtures

Raw string literals in the test class (ADR-0054; `SourceScanFixtures` precedent), compiled as strings only. Scope
constants must be real (`Scope.Sse.Cameras.Write`/`Read`) so A3's catalogue resolves them. Fixture file names are
synthetic (`"fixture/HelperEndpoints.cs"`); `Read(file, source)` never touches disk for them.

### Counterfactuals (T006; each run, failure quoted, reverted)

1. Spec procedure step 2 (`MapGetCameraAddress(reads)`) → red mismatch.
2. Step 3 (second call with `reads`) → red Unreadable.
3. Step 4 (call deleted) → red Unreadable + count pin.
4. Revert T004's parameter guard alone (keep call-site resolution) and rename a helper's parameter in a fixture to
   collide with a group local in a multi-parameter method → the parameter-not-name-bound fact goes red.

## Why no ADR

The guard's documented contract is already "the group a mapping's receiver was assigned from" (`:282`); binding a
parameter by its name is a reader defect against that contract, not a decision. No locked choice moves, no rule is
added to or removed from the codebase's obligations, and no `src/` shape is banned — option (b) of the issue (ban
helpers whose parameter is named like another group) was rejected because it would ban the shape of all 65 helpers
and force a `src/` rename sweep for no runtime gain. Option (c) (accept, rely on baselines) is recorded in spec
premise 5 as the reason this is tech debt rather than a security defect, but leaves the guard asserting falsehoods.

## Risks

- **R1 — regex method detection over-matches** (e.g. a lambda or local function). Mitigation: only the exact
  single-`RouteGroupBuilder`-parameter shape is a helper; any other parameter-bound receiver binds to *nothing*, which
  fails red, never green.
- **R2 — call-site regex matches the declaration itself or a `nameof`.** Exclude the declaration span; `nameof(MapX)`
  has a non-group argument → Unreadable → red, visible.
- **R3 — `Read` is called per theory case and per fact; extra regex passes cost milliseconds.** Accept.
