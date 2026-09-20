# Spec 192 — A form the masker cannot read

**Issue:** [#2278](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2278)
— *"`EndpointScopeDeclarationTests` has no masker-assumptions guard, and masking
now decides statement boundaries"*. Labels `tech-debt`, `agent:ready`, no
`agent:blocked`. **Already on Project #13**, status **Todo** — verified
2026-09-20 with
`gh project item-list 13 --owner smartsolutionslab --limit 2000`, which returned
the row `2278 Todo EndpointScopeDeclarationTests has no masker-assumptions
guard…`. **No `item-add` needed.** (`--limit 2000`, not the default 30: a filled
board reads as empty otherwise.)

**Spec number.** `git ls-tree -d origin/develop -- specs/` shows **190**
(`190-one-reader-the-next-guard-finds`) as the highest merged. Every remote
branch was swept after `git fetch --prune`:
`191-one-source-of-the-admin-credentials` exists on open PR #2465's branch,
nothing claims 192. This spec is **192**. The number one above develop's highest
is not automatically free — it has collided before — so it was checked rather
than assumed, and it is worth re-checking after PR #2465 merges.

**File contention: none.** The only open PR is #2465
(`2275-one-source-of-admin-credentials`), whose files are `specs/191-…/*` and
`tests/Integration.Tests/Identity/*`. No open branch touches
`tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`, `SourceMask.cs`,
`RouteChainReader.cs` or `SourceScanFixtures.cs`.

**ADRs referenced:**

- **ADR-0139** (rules that fail the build, not the review), and the
  constitution's §Testing as that ADR amends it. This spec adds an instrument of
  that ADR: a form that today would mis-read *silently* becomes a named build
  failure.
- **ADR-0144** (the autonomous lane and its two phase-4a colours). §6 picks
  **red** and says why the alternative was considered and rejected.
- **ADR-0036** (smallest possible change; no speculative generality; surface
  assumptions rather than burying them).
- **ADR-0037** (the seven phases and their gates).
- **ADR-0109** (the `[P]` disjoint-file rule — `tasks.md` §4).
- **ADR-0052 / 0053 / 0054** (xUnit + Shouldly; sentence-style test names with
  underscores; hand-written fixtures, no AutoFixture).
- **ADR-0084** (code metrics advisory, 300 LOC/file — `plan.md` §5).

**No new ADR is needed.** This decides nothing architectural. It adds one
assertion over a corpus, consuming a mechanism spec 190 (issue #2257) already
built, and changes no rule, no pinned count and no production assembly.

**Latency budget (§IV): N/A.** The entire diff lands under
`tests/Architecture.Tests/`. No production assembly changes, so none of the six
legs of `event arrival → overlay rendered` is touched and no leg's measurement
status moves.

---

## 1. What the issue says, and what is actually in the tree

The issue was filed **before #2257 (spec 190) merged**, so its file and line
references describe the pre-consolidation shape. Every claim below was
re-checked against `origin/develop` on 2026-09-20.

### 1.1 What moved

| The issue says | What is there now |
|---|---|
| `MaskLiterals` at `EndpointScopeDeclarationTests.cs:1892-1930` | Gone. `Masked()` (`EndpointScopeDeclarationTests.cs:1869-1870`) is `SourceMask.Apply(SourceMask.Apply(text, MaskStrictness.CommentsBlankedLiteralsIntact), MaskStrictness.LiteralInteriorsOnly)` |
| `StatementEnd` local to the guard | `RouteChainReader.StatementEnd(text, from, ChainEndSentinel.EndOfText, ChainLiteralHandling.AlreadyMasked)`; the depth counter is `AlreadyMaskedStatementEnd` (`RouteChainReader.cs:99-118`) |
| "five copies of `StatementEnd` now exist" | One, in `RouteChainReader`, parameterised on the two axes that differed |
| `ConcurrencyConflictDeclarationTests.cs:764` — the guard to mirror | `ConcurrencyConflictDeclarationTests.cs:757`, `The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask`, now reading its ban list from `SourceMask.UnhandledForms(MaskStrictness.CommentsOnlyLiteralsIntact)` |

### 1.2 What did **not** move: the gap is still open

`SourceMask.UnhandledForms(strictness)` (`SourceMask.cs:118-121`) returns
`RawStringLiteralOnlyUnhandled` — exactly one row, the three-quote delimiter with
the reason *"a raw string literal, whose delimiter is longer than one quote"* —
for **every strictness except** `CommentsOnlyLiteralsIntact`. Its own doc comment
calls it *"the one form every other strictness fails to read, named in prose by
each of their guards but never asserted until now"*, and "until now" is not yet
true. A repository-wide grep finds exactly one caller of `UnhandledForms`:

```
tests/Architecture.Tests/ConcurrencyConflictDeclarationTests.cs:765
    SourceMask.UnhandledForms(MaskStrictness.CommentsOnlyLiteralsIntact)
```

So the raw-string row is **data that nothing asserts**. That is the phase-6
observation the dispatch recorded, confirmed here by reading rather than
repeated.

`SourceScanCharacterisationTests.The_RawStringLiteral_fixture_masks_as_expected_under_every_strictness`
(`SourceScanCharacterisationTests.cs:183-196`) pins *how* each strictness
mis-masks a raw string. It says nothing about whether the corpus contains one,
and nothing about what the mis-mask then does to a statement boundary.

### 1.3 Therefore the fix is smaller than filed

The issue asks for a guard "in the shape `ConcurrencyConflictDeclarationTests.cs:764`
already uses". Post-#2257 that shape is about **a dozen lines of loop plus a
message**, because the ban list, the file enumeration, the raw-text reader and
the line resolver all already exist:

| Ingredient | Already exists |
|---|---|
| The forms to ban, each with a reason | `SourceMask.UnhandledForms(…)` (`SourceMask.cs:118`) |
| The corpus | `EndpointScopeDeclarationTests.ApiSourceFiles()` (`:1820`) |
| Raw (unmasked) file text, `\r` stripped | `ReadRepositoryFile(…)` (`:1861`) |
| `file:line` for an offset | `RouteChainReader.LineOf(…)` (`RouteChainReader.cs:255`) |

**No new detection logic is needed, and none should be written.** Nothing about
raw strings is taught to `SourceMask`; teaching it is a behaviour change and
`SourceMask.cs:11-13` already says it needs its own issue.

---

## 2. Why it matters: the hazard, constructed and measured

The issue argues the hazard. It was **constructed and run** on this tree rather
than argued again, because this repository has a recorded habit of guards whose
claim about themselves turns out to be false.

Probe (throwaway, since deleted) feeding a raw string through the guard's own
`Masked()` and `RouteChainReader.StatementEnd`:

```
source:
group.MapPost("/probe", Probe)
    .WithSummary("""see "foo( bar" now""")
    .ProducesProblem(StatusCodes.Status404NotFound);
group.MapPost("/other", Other)
    .RequireAuthorization(Scope.Sse.Cameras.Read)
    .ProducesProblem(StatusCodes.Status403Forbidden);

masked:
group.MapPost("      ", Probe)
    .WithSummary("""    "foo( bar"    """)
    .ProducesProblem(StatusCodes.Status404NotFound);
group.MapPost("      ", Other)
    .RequireAuthorization(Scope.Sse.Cameras.Read)
    .ProducesProblem(StatusCodes.Status403Forbidden);

END=262  LEN=262  SWALLOWED(RequireAuthorization inside the span)=True
```

`END == LEN` is `ChainEndSentinel.EndOfText` firing: **the chain's own semicolon
was never found**, and `/probe`'s span is the rest of the file.

**Why the `(` survived.** Stage two (`LiteralInteriorsOnly`) walks quotes in
pairs. In `"""A"B"C"""` it reads the first two quotes as an empty literal, then
`"A"` — blanking `A` — then walks **`B` as ordinary code**, then `"C"` — blanking
`C`. Content between the first and second interior quotes is handed to the depth
counter as if it were source. An arrangement with the bracket in `C`
(`"""see "here" ( now"""`, also run) blanks it and the boundary comes out
correct. **The form is unsafe, not uniformly broken** — exactly the kind of
hazard a corpus ban is for and a spot check is not.

### 2.1 Which direction fails, and why one direction is worse

`The_refusal_declarations_the_walk_finds_are_all_the_ones_there_are` (`:1113`,
"A15") cross-checks *one* thing — the count of `Status403Forbidden`
declarations, walked versus flat-swept — and in the arrangement above it would go
red, because `/other`'s declaration is counted twice. That is the loud direction,
and it is a backstop rather than a diagnosis: its message names a number, not a
file.

**Three readings have no cross-check at all**, because a span that runs long
takes the *next* mapping's calls as its own:

- `DeclaredAuthorization` / `FirstAuthorizationCall` (`:1438`, `:1477`) — the
  span's **first** `.RequireAuthorization(...)`. A mapping that enforces nothing
  inherits its neighbour's scope.
- `DeclaredSummary` (`:1495`) and
  `No_summary_names_a_scope_other_than_the_one_the_endpoint_enforces` (`:720`) —
  summary-versus-scope agreement, judged against a scope belonging to a
  different endpoint.
- `Every_scoped_endpoint_declares_the_refusal_its_scope_produces` (`:1020`) —
  per-mapping, and satisfied by the borrowed declaration.

The fully silent case is reachable: an endpoint whose chain carries **no**
`.RequireAuthorization`, and whose raw-string summary happens to name the scope
its *neighbour* enforces, is reported as correctly scoped and correctly
documented while being unauthenticated. That is the precise failure spec 070
exists to prevent, passing green.

### 2.2 The corpus is clean today

Re-measured on this tree, 2026-09-20:

```
$ grep -rln '"""'  --include=*.cs src/*/Api/   → (none)
$ grep -rln '@\$"' --include=*.cs src/*/Api/   → (none)
```

So this is a **gap, not a live defect**. Nothing in `src/` changes.

---

## 3. User stories

### US1 (P1) — the only story

**As** the next engineer to write an endpoint,
**I want** the build to fail, naming my file and line, if I use a string form
this guard's reader cannot mask,
**so that** the guard reports what my endpoint actually declares instead of what
its neighbour declares.

Independently shippable: one file, one slice's worth of assertions, observable
end to end by planting a raw string in an endpoint file and watching the build
name it.

There is no P2. The identical gap in three sibling guards is **out of scope** —
§7.

---

## 4. Acceptance scenarios (Gherkin)

```gherkin
Feature: the API corpus stays inside what the endpoint-scope reader can mask

  Scenario: happy path — the corpus uses only forms the reader handles
    Given src/*/Api contains no raw string literal
    When The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask runs
    Then it passes
    And it has asserted against a non-empty file list and a non-empty ban list

  Scenario: conflict — an endpoint file gains a raw string literal
    Given an endpoint under src/*/Api contains a three-quote delimiter
    When the guard runs
    Then it fails
    And the message names the file, the line, the form, and why the reader cannot read it

  Scenario: bad request — the ban list becomes empty
    Given SourceMask.UnhandledForms returns no rows for the strictnesses this reader applies
    When the guard runs
    Then it fails, rather than passing against nothing

  Scenario: bad request — the corpus becomes empty
    Given ApiSourceFiles() returns no files
    When the guard runs
    Then it fails, rather than passing against nothing

  Scenario: the ban is load-bearing, not decorative
    Given a synthetic chain whose .WithSummary is a raw string containing an unbalanced bracket
      between two of its interior quotes
    When the chain is masked by Masked() and walked by RouteChainReader.StatementEnd
    Then the span runs past its own semicolon and swallows the next mapping
    And that is recorded as the reason the ban exists

  Scenario: auth — not applicable
    Given this change adds no endpoint and no runtime code
    Then no scope, token or fab check is involved
```

Two of these are the ones this repository keeps re-learning: a guard must not
pass against an empty corpus, and a guard's claim about itself must be
constructed rather than argued.

---

## 5. Independent end-to-end test procedure

Not "the tests are green" — the behaviour observed:

1. `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~EndpointScopeDeclarationTests"`
   → green on the untouched tree.
2. Plant a raw string in a real endpoint file: in
   `src/CameraCatalog/Api/CameraEndpoints.cs`, change one `.WithSummary("…")` to
   the three-quote form.
3. Re-run. **Expected:** the new guard fails, and its message names
   `src/CameraCatalog/Api/CameraEndpoints.cs:<line> contains <the form> — a raw
   string literal, whose delimiter is longer than one quote`.
4. `git checkout -- src/CameraCatalog/Api/CameraEndpoints.cs`; re-run → green.
5. Quote steps 2-4's verbatim output in the PR body.

Step 3 is the verification note's content: the point is not that a test failed,
but that the failure **names its own cause and location**, which the pre-existing
A15 backstop does not.

---

## 6. Phase-4a colour: **RED**

Declared here, per ADR-0144, so it is not decided under pressure at phase 4.

This adds **new coverage**, not a behaviour-preserving reshape, and §Testing
binds new behaviour to an observed failure. The complication is that the new
corpus assertion cannot be observed red against the real corpus — §2.2 shows the
corpus is clean, so it is green on arrival.

**Resolution: red, discharged by counterfactual on the guard's own detection.**
The offender-collecting step is written as a small private helper over an
in-memory `(file, text)` sequence. The first test feeds it **synthetic text
containing the three-quote delimiter** and asserts it reports an offender naming
the form and the line, and feeds it clean text and asserts nothing is reported.
That test is written **before the helper exists**, so the project does not
compile and the missing member is named verbatim — that is the phase-4a red, and
it is the weakest form of red, so it is not the only one taken. The
**behavioural** red follows at phase 5 (§5 steps 2-3): a raw string planted in a
real endpoint file, and the new corpus guard observed failing and naming that
file and line. Both failures are quoted in the PR body; neither alone is the
evidence.

The real-corpus `[Fact]` is then the same helper pointed at `ApiSourceFiles()`.

**Considered and rejected: characterisation-green.** A new assertion that has
nothing to catch today is not a refactor, and treating it as one means shipping a
guard nobody ever watched fail — the exact shape of the guards this repository
has had to disprove. Ambiguity resolves to red (CLAUDE.md, ADR-0144), and this
is not even ambiguous once the detection step is testable in isolation.

**One companion test arrives green, and is labelled as such.** The §2 hazard
test (`A_raw_string_in_a_chain_runs_one_mapping_into_the_next`) characterises
*existing* `SourceMask` + `RouteChainReader` behaviour: it must pass unmodified,
and it is the thing that stops the new ban being decorative. It is declared green
here rather than discovered green at phase 4. If someone later teaches the masker
raw strings, that test goes red **correctly**, and its message must say so.

---

## 7. Deliberately out of scope

- **Teaching `SourceMask` raw strings.** `SourceMask.cs:11-13` already says this
  is a behaviour change needing its own issue. A ban is the cheap, honest move;
  support is not.
- **The same gap in the three sibling guards.** `PreconditionDeclarationTests`,
  `RouteValueRefusalDeclarationTests` and `StatusProducerDeclarationTests` all
  mask with `MaskStrictness.CommentsAndLiteralInteriors` and walk with
  `ChainLiteralHandling.AlreadyMasked` — **identical exposure, identical missing
  assertion**. A real finding of this spec, and worth a follow-up issue; not
  folded in, because #2278 names one guard and three more files would make the
  counterfactual evidence harder to read, not easier.
- **Extracting the assertion into shared code.** `SourceMask.cs:77-81` states the
  doctrine explicitly: the ban list is shared data, *"the assertion that the
  corpus is free of them stays in that guard's own file — which corpus must
  avoid them is the guard's business, not the masker's."* Follow it.
- **A stale name in a neighbouring comment.** `EndpointScopeDeclarationTests.cs:1246`
  still says *"MaskLiterals blanks literal interiors"*; `MaskLiterals` was
  deleted by #2257. It is the only stale masker name left in the file
  (`MaskComments` in `ConcurrencyConflictDeclarationTests.cs:1027` still exists).
  Left alone: a comment-only change carries its own phase-4a obligation and would
  dilute this slice's red evidence. Worth a one-line follow-up.
