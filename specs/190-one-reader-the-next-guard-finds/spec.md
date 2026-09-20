# Spec 190 — One reader the next guard finds

**Issue:** [#2257](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2257)
— **already on Project #13** ("Smart Sentinel Eye"), status **Todo**, labels
`tech-debt`, `agent:ready`, no `agent:blocked`. Verified 2026-09-20 with
`gh project item-list 13 --owner smartsolutionslab --limit 2000`, which returned
the row `2257 Todo The source-scanning guards carry six copies…`. **No
`item-add` needed.** (`--limit 2000` rather than the default 30 — a filled board
reads as empty otherwise.)

**Spec number.** `git ls-tree -d origin/develop -- specs/` shows **188** as the
highest merged. Every remote branch was swept for a `specs/18x`–`specs/2xx`
directory: `189-a-cleanup-that-says-when-it-failed` exists on an unmerged
branch, nothing claims 190. This spec is **190**. The number one above develop's
highest is not automatically free — it has collided before — so it was checked
rather than assumed.

**ADRs referenced:**

- **ADR-0139** (rules that fail the build, not the review). The six files this
  spec touches are that ADR's instruments. The whole risk of this change is that
  an extraction quietly widens or narrows what one of them sees, which turns a
  build-failing rule back into a review convention without anybody noticing.
- **ADR-0144** (the autonomous lane and its two phase-4a colours). This spec is
  the **characterisation** colour, and the issue exists *because* spec 130
  refused to fold it into a behaviour-changing commit. Doing it here as a red is
  a category error.
- **ADR-0036** (smallest possible change; refactors change shape, not
  behaviour; no speculative generality; surface assumptions).
- **ADR-0037** (the seven phases and their gates).
- **ADR-0109** (the `[P]` disjoint-file rule).
- **ADR-0052 / 0053 / 0054** (xUnit + Shouldly + hand-written fixtures; sentence
  -style test names with underscores; no AutoFixture).
- **ADR-0091** (no shortcuts or aliases in names — the shared type and its
  members are spelled out).
- **ADR-0084** (code metrics advisory at 300 LOC/file — why this lands as three
  small files rather than one big one).

**No new ADR is needed.** This spec decides nothing architectural. It moves test
plumbing that already exists into one place and changes no rule, no assertion,
no pinned count, and no production assembly.

**Latency budget (§IV): N/A.** The entire diff lands under
`tests/Architecture.Tests/`. No production assembly changes, so none of the six
legs of `event arrival → overlay rendered` is touched and no leg's measurement
status moves.

---

## 1. The finding, re-checked on this tree

The issue says **six copies of `RepositoryRoot()`, the masker and the chain
reader**. Each of those three is a different number, and the spec is written
against what is actually in the tree at `origin/develop` on 2026-09-20 rather
than against the sentence.

### 1.1 The six guards, named

`specs/130-the-line-between-derivable-and-register/tasks.md:19-25` names them,
and that list is the authority — the issue body's "each one walks `src/*/Api`"
is true of five of the six and not of the sixth.

| # | File | Origin | Lines | Walks |
|---|---|---|---|---|
| 1 | `tests/Architecture.Tests/PreconditionDeclarationTests.cs` | spec 072 (#2088), extended by #2101 | 1199 | `src/*/Api` |
| 2 | `tests/Architecture.Tests/ConcurrencyConflictDeclarationTests.cs` | spec 075 (#2096) — the pinned register | 1325 | `src/*/Api` |
| 3 | `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs` | spec 070 (#2087), extended by spec 085 (#2113) | 2298 | `src/*/Api` |
| 4 | `tests/Architecture.Tests/RouteValueRefusalDeclarationTests.cs` | spec 091 (#2114) | 1019 | `src/*/Api` |
| 5 | `tests/Architecture.Tests/StatusProducerDeclarationTests.cs` | spec 130 (#2142) | 1026 | `src/*/Api` |
| 6 | `tests/Architecture.Tests/PaginatedConsumerTests.cs` | spec 065 (#1982) | 643 | **`apps/*/src`**, TypeScript |

**All six were run on this tree before anything was written: `Passed! - Failed:
0, Passed: 189` for the whole Architecture.Tests project (2026-09-20,
`dotnet test … --filter FullyQualifiedName~DeclarationTests|…EndpointScope…`
plus the full run).** No guard currently reports a violation, so the
characterisation baseline is "every assertion passes", not "these violations are
listed". That is a simplification worth stating, because it is the thing that
makes byte-identical output a usable proof rather than a wish.

### 1.2 The three pieces are three different counts

| Piece | Copies | Identical? |
|---|---|---|
| `RepositoryRoot()` | **6 of 6** | **Byte-identical.** MD5 `2109ee01…` on all six extractions. |
| The masker | **5, in 3 distinct behaviours** — guard 6 has none | **No.** §1.3. |
| The `Map…` chain reader | **5, in 2 distinct behaviours** — guard 6 has none | **No.** §1.4. |

**The correction matters.** `PaginatedConsumerTests` contributes the root finder
and a `RelativePath` helper; it has no masker in the sense the other five mean
(its `ExecutableLines` is a line-oriented TypeScript filter, §1.5) and no chain
reader at all. A plan that assumed six maskers would have gone looking for one
in a TypeScript sweep and either invented a unification that does not fit or
widened the slice to include a guard whose subject is a React hook.

**And the root finder is not a six-copy problem; it is a thirty-one-copy
problem.** `grep -c "while (candidate is not null && !File.Exists(Path.Combine(
candidate.FullName, \"SmartSentinelEye.slnx\")))"` matches in **31 files** under
`tests/` (28 in `Architecture.Tests`, 3 in `Integration.Tests`). This spec
migrates **six** of them — the six the issue owns — and records the other 25 as a
follow-up rather than sweeping them, because a 31-file diff for a
behaviour-preserving change buys nothing and collides with every branch in
flight. §7.

### 1.3 How the maskers actually differ

Read, not assumed. Three behaviours across five files:

**Behaviour A — `Mask(text)`, one pass, comments *and* literal interiors
blanked.** In `PreconditionDeclarationTests.cs:1031`,
`RouteValueRefusalDeclarationTests.cs:851`,
`StatusProducerDeclarationTests.cs:838` — with their four helpers
(`MaskBlockComment`, `MaskVerbatim`, `MaskLiteral`, `Next`, `Blank`)
**byte-identical in all three** (MD5 `3822dc1b…` over the 91-line block).
Handles `//`, `/* */`, `@"…"` (doubled-quote escape), `"…"` and `'…'`
(backslash escape, newline-terminated). **Does not handle raw string literals
(`"""`)**; all three class docs say so, and all three say there are none under
`src/*/Api`.

**Behaviour B — two stages, `EndpointScopeDeclarationTests.cs:1900` and
`:1964`.** `WithoutComments(source)` blanks comments and *skips literals whole,
content intact*. `MaskLiterals(text)` then blanks literal interiors.
`Masked(source) = MaskLiterals(WithoutComments(source))` (`:1888`). Both stages
share one `EndOfLiteral(text, start, verbatim)` (`:2007`). **The guard needs both
outputs**: structural searches run on the fully masked text, and literal
*contents* — the scope constant, the summary sentence — are then read from the
unmasked text at the very same offsets. Collapsing B to A would destroy stage
one, which is the half that lets the guard read a summary at all.

**Behaviour C — `MaskComments(text)`,
`ConcurrencyConflictDeclarationTests.cs:1169`.** Comments blanked; **literal
content left completely intact**, because this guard reads route literals and
`MapGroup` prefixes straight off the masked text (`PlainRouteLiteral`,
`GroupPrefix`). It steps over a plain `"…"` only so that a `//` inside one is not
mistaken for a comment. Its `EndOfStringLiteral` (`:1107`) **does not honour
backslash escapes** and it has **no `@"` and no `'…'` branch at all**.

**C is the weakest masker, and it is weak on purpose, with the weakness pinned
by an assertion.** `The_api_sources_use_only_the_string_and_comment_forms_this_
reader_can_mask` (`:772`) fails the build if any file under `src/*/Api` contains
`@"`, `"""`, `\"` or `'"'` — each with a one-line reason
(`UnmaskableLiteralForms`, `:220`). So the guard does not silently mis-read a
form it was never taught; it refuses the corpus that contains one.

**This is the trap the issue warns about, stated precisely:**

- **Collapsing C to A** (giving it verbatim, escape and char-literal handling)
  changes what `ConcurrencyConflictDeclarationTests` sees *the moment the corpus
  gains one of those forms*, and — worse — makes its own
  `The_api_sources_use_only…` assertion describe a masker that no longer exists.
  Today, with the corpus free of all four forms, A and C **happen** to agree.
  That coincidence is not the contract.
- **Collapsing A to C** silently widens all three A-guards: an `@"…"` containing
  `.ProducesProblem(StatusCodes.Status400BadRequest)` would start counting as a
  declaration.
- **Collapsing B to A** deletes stage one and with it the guard's ability to
  read any literal content.

**Therefore the shared masker exposes all three as named, mandatory choices with
no default.** §2, US2.

### 1.4 How the chain readers actually differ

Two independent axes, documented at `EndpointScopeDeclarationTests.cs:2054-2100`
— which already names all five copies and their line numbers, and is the reason
this was findable at all.

**Axis 1 — the not-found sentinel.**

| Copy | Returns when no terminating `;` |
|---|---|
| `PreconditionDeclarationTests.cs:941` | `-1` |
| `RouteValueRefusalDeclarationTests.cs:758` | `-1` |
| `StatusProducerDeclarationTests.cs:751` | `-1` |
| `ConcurrencyConflictDeclarationTests.cs:1064` | `-1` |
| `EndpointScopeDeclarationTests.cs:2103` | **`masked.Length`** |

EndpointScope's is deliberate and carries 22 lines of doc explaining why: its
three call sites use the result *directly* as a slice bound, and
`masked[x..(-1)]` lowers to `Substring(x, -1)`, which throws naming the *length*
parameter. `masked[x..masked.Length]` is a valid identity slice. The doc ends
**"Do not 'fix' this back to `-1` to match the siblings."** A shared reader that
picked one sentinel would either reintroduce that throw or force four call sites
to start null-checking a value that can no longer be `-1`.

**Axis 2 — literal awareness inside the walk.**
`ConcurrencyConflictDeclarationTests.cs:1064` steps over string **and char**
literals (`EndOfStringLiteral`, `EndOfCharLiteral`) while counting bracket depth.
The other four do not, and do not need to — their input is already
literal-masked. CC's input is not, because its masker is behaviour C. The class
doc records that this was the same hole reached twice: an unbalanced `(` inside a
`WithSummary("…")`, then a `'('` written as a char literal, each leaving depth
permanently positive so one chain ran into the next and **borrowed its
neighbour's 409 declaration**. Issue #2183. A shared reader without the
literal-aware mode reopens both.

**Axis 3 — how far out the antecedent is resolved.** This is the axis the issue
names, and it splits the five three ways:

| Style | Guards | What it reads |
|---|---|---|
| **Chain only** | `ConcurrencyConflictDeclarationTests` (075), `StatusProducerDeclarationTests` (130) consequent | `.MapX(` to the statement's `;` |
| **Chain + its `MapGroup`** | `EndpointScopeDeclarationTests` (#2113), `StatusProducerDeclarationTests` (130) access classification | also the `var g = x.MapGroup("…")` statement the receiver was bound to |
| **Handler body** | `PreconditionDeclarationTests` (072/#2101), `RouteValueRefusalDeclarationTests` (#2114) | one hop out: the bare method group resolved across every file declaring that partial class **in the same `src/<Context>/Api` project**, or the inline lambda |

**The handler-body resolver is byte-identical between guards 1 and 4** —
`MethodBodies` + `BodyAfter` (`:881`/`:914` and `:698`/`:731`) differ by exactly
one word in one doc comment ("a lambda" vs "a lambda or a collection
initialiser"). That is the largest genuinely-identical block after the masker.

**Also byte-identical across the subsets that have them:** `Balanced`
(guards 1, 4, 5), `SplitArguments` (1, 4, 5), `Unquote` (4, 5), `LineOf` (1, 2,
4, 5 — and a variant in 3), `Relative`/`RelativePath` (all six, same body and
in five cases the same doc comment about `Path.GetRelativePath` returning the
platform separator).

### 1.5 The seventh piece, found while looking

`ExecutableLines(source)` and the `StringLiteral` regex
`@"""(?:[^""\\\r\n]|\\.)*"""` exist **byte-identically** in
`EndpointScopeDeclarationTests.cs:2227`/`:510` and
`PaginatedConsumerTests.cs:610`/`:153` (the two differ only in where a `&&`
wraps), and the same regex again at `PreconditionDeclarationTests.cs:229`. It is
the "read the guard's own source, ignoring comments and the prose it prints"
filter behind every `GuardSource` self-scan — six files declare `GuardSource`.

It is **in scope** for the root-finder story only where it is already identical
(guards 3 and 6), and recorded rather than extracted elsewhere. Including it is
what makes `PaginatedConsumerTests` a sensible member of this slice at all: it
shares the root finder, `RelativePath` and `ExecutableLines`, and nothing else.

### 1.6 Why it keeps happening, and what actually fixes it

Each copy arrived legitimately — the sibling did not exist yet, or widening the
slice would have mixed a refactor into a behaviour change. The failure is not
that anyone copied; it is that **the differences between the copies are
invisible from any one of them**. `RouteValueRefusalDeclarationTests` says its
reader "is copied from `PreconditionDeclarationTests` deliberately rather than
reinvented (ADR-0036)" — correct, and still leaves the next author unable to see
that `ConcurrencyConflictDeclarationTests` needs a literal-aware walk or that
`EndpointScopeDeclarationTests` needs the other sentinel.

So the deliverable is not "one function". It is **one place where the three
choices are written down as choices**, with no default on any of them, so the
seventh guard's author has to pick rather than inherit whichever file they
opened first.

---

## 2. User stories

Prioritised, and **each is independently shippable**: every story ends with the
full Architecture.Tests suite green and every one of the six guards' assertions
byte-identical to what it is today.

### US1 (P1) — One repository-root finder, six call sites

**As** the author of the seventh source-scanning guard,
**I want** one `RepositoryRoot()` that the six existing guards already use,
**so that** I inherit it instead of copying whichever file I opened first.

This is the floor. It is the only genuinely identical piece (§1.2), it creates
the shared file that US2 and US3 extend, and it can merge on its own and close a
real part of the issue if US2 or US3 has to be deferred. It also carries
`RelativePath` (identical in all six) and `ExecutableLines` + `StringLiteral`
(identical in guards 3 and 6).

### US2 (P2) — One masker, three named strictnesses, no default

**As** that same author,
**I want** the masker to make me name which of the three behaviours I want,
with each one's blind spots written on it,
**so that** I cannot pick the weakest by accident and widen my own guard.

Requires US1 (the shared file exists). Independently shippable after it.

### US3 (P3) — One chain reader, with the sentinel and the literal-awareness as arguments

**As** that same author,
**I want** `StatementEnd`, `Balanced`, `SplitArguments`, `Unquote`, `LineOf` and
the handler-body resolver in one place, with the two axes that actually differ
expressed as required arguments,
**so that** a `Map…` overload changing shape is one edit, and so that the reason
EndpointScope returns `masked.Length` survives contact with the next reader.

Requires US2 (the masker modes are what the literal-awareness argument pairs
with). Independently shippable after it.

**If phase 4 loses confidence in US3, US1 + US2 is a complete, mergeable slice**
and US3 gets a follow-up issue. That is a stated outcome, not a failure — it is
preferable to a chain-reader unification nobody can prove.

---

## 3. Acceptance scenarios

Written as Gherkin. "The six guards" means the six files in §1.1.

### AS-1 — Happy path: the extraction is invisible to every guard

```gherkin
Given origin/develop at the commit this branch was cut from
  And the full Architecture.Tests suite passing, 189 of 189
When the six guards are repointed at the shared reader
Then the full Architecture.Tests suite still passes, 189 of 189
  And the per-test console output of the six guards' classes is byte-identical
      to the output captured before the change, modulo durations
  And no [Fact] or [Theory] method body in any of the six files has changed
  And no pinned count, expected-message string or assertion text has changed
```

### AS-2 — The masker's three behaviours are preserved exactly

```gherkin
Given the fixture corpus in SourceScanFixtures, which contains at least one
      example of every form the three maskers agree and disagree on:
      a line comment, a block comment, a verbatim string, a raw string literal,
      an escaped quote, a quote written as a char literal, a '(' written as a
      char literal, a semicolon inside a WithSummary, an unbalanced '(' inside
      a WithSummary, a statement-bodied lambda in a chain, an expression-bodied
      handler, a MapGroup, and a handler in a second partial file
When each fixture is masked with each of the three named strictnesses
Then the output equals the expected output written out literally in the test
  And the three strictnesses DISAGREE on at least the verbatim string,
      the escaped quote and the char-literal quote
  And that disagreement is asserted, not merely permitted
```

The last two lines are the point. A test that only asserted "all three agree on
the current corpus" would pass just as well against one collapsed masker, and
would have caught nothing.

### AS-3 — Char-for-char equality over the real corpus, during the refactor

```gherkin
Given a frozen verbatim copy of each pre-refactor masker and chain-end walk,
      kept in the characterisation file only while the migration is in progress
When each is run over every .cs file under src/*/Api
Then the shared implementation returns a char-for-char identical string,
      and an identical index at every offset the walk is asked about
  And the first mismatch is reported with the file, the offset, and both
      characters — not as a hash mismatch
```

### AS-4 — Conflict: a collapsed strictness fails loudly

```gherkin
Given the shared masker
When a caller omits the strictness argument
Then the code does not compile — there is no default parameter value
```

```gherkin
Given ConcurrencyConflictDeclarationTests repointed at the shared masker
When the shared masker's comments-only behaviour is changed to also blank
    literal interiors
Then The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask
    still passes, because it is a corpus assertion
  But the route-literal and MapGroup-prefix reads return blank
  And the guard's pinned register assertions fail
```

That second scenario is the counterfactual this spec requires phase 4 to
actually construct once (`MEMORY: prove a guard by counterfactual`). A shared
masker whose weakest mode is never shown to be load-bearing is a shared masker
somebody will collapse in six months.

### AS-5 — Conflict: the sentinel cannot be unified away

```gherkin
Given EndpointScopeDeclarationTests repointed at the shared chain reader
When the reader is asked for the NotFound (-1) sentinel instead of EndOfText
Then at least one of its three slice-bound call sites throws
     ArgumentOutOfRangeException naming the 'length' parameter
```

Also a constructed counterfactual, run once and recorded — the existing doc
comment asserts this consequence in prose and nobody has ever seen it.

### AS-6 — Bad request: the corpus grows a form no masker handles

```gherkin
Given a file under src/*/Api containing a raw string literal
When the Architecture.Tests suite runs
Then ConcurrencyConflictDeclarationTests fails by name, listing the file,
     the line and the reason
  And the shared masker's own XML doc names raw string literals as unhandled
     by all three strictnesses
```

Unchanged from today — listed to make explicit that this spec does **not** teach
any masker a new form. Teaching one is a behaviour change and belongs to a
different issue.

### AS-7 — Auth / scope

**Not applicable, and that is a finding, not an omission.** The diff touches no
`src/` assembly, no endpoint, no scope, no realm, no token, no trust boundary
and no secret. Nothing in it can be reached by a request. Phase 6 therefore runs
`/code-review` and **not** `/security-review`; §6 records that decision in
writing so a reviewer does not have to infer it.

---

## 4. Independent end-to-end test procedure

Runnable by a reviewer who has read none of the above. No Aspire stack, no
Docker, no browser — this is a test-project refactor and it verifies in seconds.

1. **Capture the before-state.** On `origin/develop`, from the repository root:

   ```sh
   dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj \
     --logger "console;verbosity=detailed" --nologo \
     > /tmp/arch-before.txt
   grep -E '^\s+(Passed|Failed)\s' /tmp/arch-before.txt | sort > /tmp/arch-before-tests.txt
   ```

   Expect `Passed! - Failed: 0, Passed: 189`.

2. **Capture the derived surface.** Still on `origin/develop`, run the
   one-off harness `specs/190-.../tools/capture-surface.md` describes (phase 4a
   writes it): for every `.cs` under `src/*/Api`, print
   `<relative path>\t<SHA-256 of the masked text>` for each of the three masker
   behaviours, and `<relative path>:<line>\t<verb>\t<route>\t<SHA-256 of the
   chain span>` for each of the five chain readers. Sort ordinally, save.

3. **Check out the branch and repeat both.** The two test-name files must be
   **byte-identical**. The two surface files must be **byte-identical**.

4. **Read the diff of the six guards.** `git diff origin/develop -- <the six
   files>` must show only: deleted private helpers, and call sites rewritten to
   the shared type. **Zero lines inside a `[Fact]` or `[Theory]` body.** Check
   mechanically:

   ```sh
   git diff -U0 origin/develop -- tests/Architecture.Tests/{Precondition,ConcurrencyConflict,EndpointScope,RouteValueRefusal,StatusProducer}DeclarationTests.cs \
     tests/Architecture.Tests/PaginatedConsumerTests.cs \
     | grep -E '^[+-]' | grep -vE '^[+-]{3}' | grep -cE 'Should[A-Z]'
   ```

   Must print `0`.

5. **Run the two counterfactuals** (AS-4 second scenario, AS-5). Each must fail
   in the way the scenario names. Revert both.

6. **Build Release.** `dotnet build -c Release` clean — `TreatWarningsAsErrors`
   is on and the collection-expression analyzer is at `warning`.

**Done means:** steps 3 and 4 produce identical files and a zero, steps 5 fail as
predicted, step 6 is clean. Not "it compiles", and not "the tests are green" —
green was already true before the change and is therefore no evidence at all.

---

## 5. Locked tech choices

| Concern | Choice | Source |
|---|---|---|
| Test framework | xUnit + Shouldly, already referenced by the project | ADR-0052 |
| Test naming | sentence-style with underscores | ADR-0053 |
| Fixtures | hand-written, in-file raw string literals; no AutoFixture, no new csproj items | ADR-0054 |
| Mocks | none — this is pure text processing over real files | — |
| Language | C# 13 / .NET 10; NRT on solution-wide | ADR-0141 |
| Collections | explicit type + collection expression (`List<string> offenders = [];`) | CLAUDE.md house rules, `dotnet_style_prefer_collection_expression` at `warning` |
| Private fields | no leading underscore | CLAUDE.md house rules |
| Guards | `Ensure.That(...)` is for production argument preconditions; test plumbing keeps the shape the six guards use today | ADR-0105 |
| Visibility | `internal static` types in `SmartSentinelEye.Architecture.Tests` — not `public`, nothing outside the test project may take a dependency | ADR-0036 (no speculative generality) |
| File size | three files, each well under the advisory 300 LOC | ADR-0084 |

---

## 6. Phase-4a colour and phase-4/6 roles

**Phase 4a colour: GREEN (characterisation).** ADR-0144: behaviour-preserving.
The covering suite is the six guards themselves; they are captured passing
*before* the change and must pass **unmodified** after. **An assertion that has
to be edited is evidence the behaviour moved — block, do not adjust.** This is
the colour the issue exists to obtain: spec 130 declined the extraction
precisely so it would not ride a red-first commit.

There is no red anywhere in this spec. Nothing about what any guard reports
changes.

- **Phase 4a — `test-writer`.** Writes the fixture corpus and the
  characterisation test, captures the six guards' verbatim baseline, and returns
  it. It does **not** perform the extraction.
- **Phase 4b — `backend-engineer`.** Justified in `tasks.md` §Roles.
- **Phase 5 — `/verify`**, using §4's procedure.
- **Phase 6 — `backend-reviewer`. No `security-review`** — AS-7.

---

## 7. Out of scope, recorded so it is not lost

- **The other 25 copies of the root finder** under `tests/` (§1.2). Mechanical
  once US1 lands, but a 31-file diff collides with every branch in flight and
  proves nothing the six do not. **Needs its own issue**, and that issue should
  quote the 31 figure and its command.
- **Teaching any masker raw string literals.** A behaviour change (AS-6).
- **Unifying the five guards' `Mappings()` / `RouteGroups()` composition.** Each
  guard's mapping regex is part of its *rule* — `ConcurrencyConflictDeclaration
  Tests` matches only `Post|Put|Patch|Delete`; `StatusProducerDeclarationTests`
  captures the receiver. Folding those into one `Read(...)` would unify
  assertions, which the issue forbids, and is speculative generality (ADR-0036).
  The shared surface stops at the primitives.
- **The group-of-a-mapping resolver.** Extract it in US3 **only if** guards 3
  and 5 prove byte-identical there; otherwise leave both and record why. Stated
  as a condition rather than a plan because it was not checked.
- **`ExecutableLines` in guards 1, 2, 4, 5.** Only guards 3 and 6 have the
  identical form; the others' `GuardSource` self-scans are shaped differently
  and are left alone.

## 8. Assumptions, marked

- **A1.** The six guards are green on this tree — **verified**, not assumed
  (§1.1).
- **A2.** No other branch touches the six files — **verified** 2026-09-20 by
  diffing every remote branch against `origin/develop` for those paths; zero
  hits. The only open PR is #2463 (`2274-client-delete-assertion`), which touches
  `Integration.Tests`/Identity.
- **A3.** `src/*/Api` contains no `@"`, `"""`, `\"` or `'"'` today — asserted by
  guard 2's own test, which passes. This spec relies on that assertion
  continuing to exist; it does not rely on the fact independently.
- **A4. Unverified.** That the *fixture* corpus in AS-2 is complete — that there
  is no form on which the three maskers disagree and which no fixture contains.
  AS-3's char-for-char sweep of the real corpus is the backstop, and it is why
  AS-3 exists in addition to AS-2 rather than instead of it.
