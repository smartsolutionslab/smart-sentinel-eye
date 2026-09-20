# Plan 190 — One reader the next guard finds

**Spec:** `specs/190-one-reader-the-next-guard-finds/spec.md`
**Issue:** [#2257](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2257)

---

## 1. Bounded context and layers

**None.** No bounded context, no domain model, no aggregate, no persistence, no
HTTP surface, no messaging. The whole diff lands inside one test project:

```
tests/Architecture.Tests/
  RepositorySource.cs          (new — US1)
  SourceMask.cs                (new — US2)
  RouteChainReader.cs          (new — US3)
  SourceScanFixtures.cs        (new — phase 4a, the fixture corpus)
  SourceScanCharacterisationTests.cs  (new — phase 4a; the frozen copies in it
                                       are deleted by the last task of US3)
  PreconditionDeclarationTests.cs     (edited — call sites only)
  ConcurrencyConflictDeclarationTests.cs
  EndpointScopeDeclarationTests.cs
  RouteValueRefusalDeclarationTests.cs
  StatusProducerDeclarationTests.cs
  PaginatedConsumerTests.cs
```

That is stated rather than skipped because the phase-2 template asks for layers,
and answering "Application/Infrastructure" here would send a reviewer looking
for a handler that does not exist. §4 (entities) and §5 (messaging) record the
same.

**No csproj change.** The fixture corpus is in-file raw string literals, not
`EmbeddedResource` or `None` items — one fewer thing to get wrong on a case
-sensitive CI filesystem, and the fixtures are reviewable in the diff that uses
them.

**Boundary rules (ADR-0027/0044): untouched.** Nothing here references a bounded
context. `BoundaryTests` and `NetArchTest` are unaffected; the three new types
are `internal` to `SmartSentinelEye.Architecture.Tests`.

---

## 2. The shape of the change

Three new files, six edited, in dependency order. Every row is
behaviour-preserving.

| # | File | Change | Story |
|---|---|---|---|
| 1 | `SourceScanFixtures.cs` | **new** — the fixture corpus, §6 | 4a |
| 2 | `SourceScanCharacterisationTests.cs` | **new** — AS-2 + AS-3, §7 | 4a |
| 3 | `RepositorySource.cs` | **new** — `Root`, `RelativePath`, `ExecutableLines`, `StringLiteral` | US1 |
| 4–9 | the six guards | delete `RepositoryRoot`/`Relative`(`Path`); call `RepositorySource` | US1 |
| 10 | `SourceMask.cs` | **new** — `Apply(text, MaskStrictness)` | US2 |
| 11–15 | guards 1–5 | delete the masker helpers; call `SourceMask.Apply` | US2 |
| 16 | `RouteChainReader.cs` | **new** — the primitives + the handler-body resolver | US3 |
| 17–21 | guards 1–5 | delete the reader helpers; call `RouteChainReader` | US3 |
| 22 | `SourceScanCharacterisationTests.cs` | delete the frozen copies and the AS-3 sweep; keep AS-2 | US3 |

Guard 6 (`PaginatedConsumerTests`) appears in US1 only. It has no masker and no
chain reader (spec §1.2).

---

## 3. The shared API, exactly

Written out here so phase 4 implements a design that was reviewed, not one it
invents. Every signature below is derived from code actually read, and every
XML-doc obligation is load-bearing.

### 3.1 `RepositorySource` — US1

```csharp
namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// The repository as a source-scanning guard sees it. One copy of what stood in
/// 31 files under tests/ on 2026-09-20 (28 in Architecture.Tests, 3 in
/// Integration.Tests); this spec migrates the six guards issue #2257 names and
/// records the rest.
/// </summary>
internal static class RepositorySource
{
    /// <summary>
    /// The directory holding SmartSentinelEye.slnx, walking up from
    /// AppContext.BaseDirectory. Byte-identical to the six copies it replaces.
    /// </summary>
    internal static DirectoryInfo Root();

    /// <summary>
    /// Forward slashes throughout. Path.GetRelativePath returns the PLATFORM
    /// separator, so a filter or an expected string written with a backslash is
    /// green on a Windows developer machine and red on Linux CI — the worst
    /// direction for a guard to break, because it passes exactly where nobody
    /// looks. This repository has been bitten by it.
    /// </summary>
    internal static string RelativePath(DirectoryInfo root, string file);

    /// <summary>
    /// Lines that are neither commentary nor attribute metadata, with the
    /// content of every string literal removed — what is left is code that could
    /// carry a mechanism, rather than the prose it prints. Used by the
    /// GuardSource self-scans. Byte-identical in EndpointScopeDeclarationTests
    /// and PaginatedConsumerTests today; the other four guards' self-scans are
    /// shaped differently and keep their own.
    /// </summary>
    internal static IEnumerable<string> ExecutableLines(string source);
}
```

`Read`/`ReadRepositoryFile` is **not** extracted: three variants exist
(`\r`-stripping, existence-asserting, raw) and picking one changes behaviour.
Recorded, not done.

### 3.2 `SourceMask` — US2

The centre of the spec. **One entry point, one required enum argument, no
default value anywhere.**

```csharp
/// <summary>
/// What a masker blanks. There is no default: the three behaviours below
/// disagree on real forms (a verbatim string, an escaped quote, a quote written
/// as a char literal), and picking one by inheritance is how a guard silently
/// widens or narrows what it sees. State the choice at the call site.
///
/// <para>NONE of the three understands a raw string literal (three quotes).
/// Teaching one is a behaviour change and needs its own issue.</para>
/// </summary>
internal enum MaskStrictness
{
    /// <summary>
    /// Comments blanked; literal CONTENT LEFT INTACT. Steps over a plain "…"
    /// only so a // inside one is not read as a comment. Does NOT understand
    /// @"…", a backslash escape, or a char literal — the caller must assert
    /// those forms are absent from its corpus, as
    /// ConcurrencyConflictDeclarationTests does
    /// (The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask).
    ///
    /// <para>Used by ConcurrencyConflictDeclarationTests (spec 075), which reads
    /// route literals and MapGroup prefixes straight off the masked text. Giving
    /// it a stronger mode blanks the very content it reads.</para>
    /// </summary>
    CommentsOnlyLiteralsIntact,

    /// <summary>
    /// Comments blanked; literals SKIPPED WHOLE, content intact. Understands
    /// @"…" (doubled-quote escape), '…' and backslash escapes.
    ///
    /// <para>Stage one of EndpointScopeDeclarationTests' two-stage reader (specs
    /// 070/085). That guard needs both stages: structure is searched on the
    /// fully masked text, and literal contents — a scope constant, a summary
    /// sentence — are read from this one at the same offsets.</para>
    /// </summary>
    CommentsBlankedLiteralsIntact,

    /// <summary>
    /// Literal INTERIORS blanked, delimiters and length kept. Comments are NOT
    /// touched — this is stage two, applied to
    /// <see cref="CommentsBlankedLiteralsIntact"/>'s output.
    /// Used by EndpointScopeDeclarationTests only.
    /// </summary>
    LiteralInteriorsOnly,

    /// <summary>
    /// One pass: comments blanked AND literal interiors blanked, delimiters and
    /// length kept so every offset still names the same character. Understands
    /// @"…", '…' and backslash escapes.
    ///
    /// <para>Used by PreconditionDeclarationTests (072),
    /// RouteValueRefusalDeclarationTests (091) and
    /// StatusProducerDeclarationTests (130), whose three copies were
    /// byte-identical.</para>
    ///
    /// <para>This is NOT the same as applying
    /// <see cref="LiteralInteriorsOnly"/> after
    /// <see cref="CommentsBlankedLiteralsIntact"/>: the two were written
    /// separately, walk literals differently, and are kept separate for that
    /// reason rather than because anyone has proved they diverge.</para>
    /// </summary>
    CommentsAndLiteralInteriors,
}

internal static class SourceMask
{
    /// <summary>
    /// The text with <paramref name="strictness"/> applied. Length is always
    /// preserved and newlines are always kept, so every index, offset and line
    /// number still refers to the real file.
    /// </summary>
    internal static string Apply(string text, MaskStrictness strictness);

    /// <summary>
    /// The literal forms <paramref name="strictness"/> cannot read, each with a
    /// one-line reason. Shared as DATA so the next guard inherits the list; the
    /// assertion stays in the guard that owns the corpus, because which corpus
    /// must be free of them is the guard's business, not the masker's.
    /// </summary>
    internal static IReadOnlyList<(string Form, string Why)> UnhandledForms(
        MaskStrictness strictness);
}
```

**`UnhandledForms` is the piece that stops this drifting again.**
`ConcurrencyConflictDeclarationTests.UnmaskableLiteralForms` moves into it
verbatim for `CommentsOnlyLiteralsIntact`; the other three get the raw-string row
they already document in prose but never asserted. The guards' own assertions are
unchanged — they iterate the same shape from a different place.

### 3.3 `RouteChainReader` — US3

```csharp
/// <summary>
/// What the walk returns when a statement has no terminating semicolon.
/// There is no default. The two answers are not interchangeable.
/// </summary>
internal enum ChainEndSentinel
{
    /// <summary>
    /// -1. PreconditionDeclarationTests, RouteValueRefusalDeclarationTests,
    /// StatusProducerDeclarationTests and ConcurrencyConflictDeclarationTests —
    /// every one of their call sites checks for it.
    /// </summary>
    NotFound,

    /// <summary>
    /// masked.Length. EndpointScopeDeclarationTests only, and deliberately: its
    /// three call sites use the result DIRECTLY as a slice bound, and
    /// masked[x..(-1)] lowers to Substring(x, -1), which throws naming the
    /// 'length' parameter, not the index. masked[x..masked.Length] is a valid
    /// identity slice. Do not "fix" this to NotFound to match the siblings;
    /// that reintroduces the throw at every call site the moment a chain has no
    /// trailing semicolon.
    /// </summary>
    EndOfText,
}

/// <summary>Whether the walk must step over literals itself.</summary>
internal enum ChainLiteralHandling
{
    /// <summary>
    /// The text is already literal-masked, so a bracket inside a literal cannot
    /// exist. Pairs with MaskStrictness.CommentsAndLiteralInteriors and with
    /// EndpointScope's stage two.
    /// </summary>
    AlreadyMasked,

    /// <summary>
    /// The text carries live literals — it pairs with
    /// MaskStrictness.CommentsOnlyLiteralsIntact. String AND char literals are
    /// stepped over while bracket depth is counted.
    ///
    /// <para>Both halves were real holes, reached separately: an unbalanced '('
    /// inside a WithSummary("…"), and one written as the char literal '('. Each
    /// left depth permanently positive, so the chain ran past its own ';' into
    /// the next mapping and inherited whatever that one declared — a route could
    /// be made to look compliant by borrowing its neighbour's 409. The char
    /// literal was found only after the string fix was believed to have closed
    /// it. Issue #2183.</para>
    /// </summary>
    StepOverStringAndCharLiterals,
}

internal static class RouteChainReader
{
    /// <summary>
    /// The index of the semicolon ending the statement that starts at
    /// <paramref name="from"/>, ignoring semicolons nested inside brackets — a
    /// chain may carry a lambda or a collection initialiser.
    /// </summary>
    internal static int StatementEnd(
        string text, int from, ChainEndSentinel sentinel, ChainLiteralHandling literals);

    /// <summary>The index of the delimiter matching the one at
    /// <paramref name="openIndex"/>, or -1.</summary>
    internal static int Balanced(string text, int openIndex, char open, char close);

    /// <summary>The half-open spans of the top-level arguments between
    /// <paramref name="from"/> and <paramref name="close"/>.</summary>
    internal static List<(int Start, int End)> SplitArguments(string masked, int from, int close);

    internal static string Unquote(string value);

    internal static int LineOf(string text, int index);

    /// <summary>
    /// The body of the handler a mapping names: a bare method group resolved
    /// across every file declaring that partial class within the same
    /// src/&lt;Context&gt;/Api project, or the lambda written inline. A name
    /// qualified by another type, or one resolving to none or two declarations,
    /// returns null — nothing resolves to a pass by default.
    ///
    /// <para>Byte-identical in PreconditionDeclarationTests (072) and
    /// RouteValueRefusalDeclarationTests (091) today; the two differed only in
    /// one word of one doc comment.</para>
    /// </summary>
    internal static HandlerBody? HandlerBodyFor(
        string containingClass, string handlerArgument, string file,
        IReadOnlyList<ClassSpan> classes, IReadOnlyDictionary<string, string> masked);

    internal sealed record HandlerBody(string File, int Line, int BodyStart, string Body);
    internal sealed record ClassSpan(string File, string Name, int Start, int End);
}
```

**What is deliberately NOT here**, and why — this is the boundary between
plumbing and rule, and the issue forbids crossing it:

- **No `Read(...)` façade returning a `Surface`.** Each guard's mapping regex is
  part of its rule (`ConcurrencyConflictDeclarationTests` matches only
  `Post|Put|Patch|Delete`; `StatusProducerDeclarationTests` captures the
  receiver). A single `Read` would have to take both the regex and the
  consequent, and the consequent is the assertion.
- **No `AntecedentScope` enum.** The three styles (chain / chain + group /
  handler body) are the *guards'* compositions over these primitives, not a
  parameter to one function. Chain-only is `StatementEnd` at the `.MapX(` index;
  handler-body is `HandlerBodyFor`; chain-plus-group is each guard's own group
  walk. Adding a mode enum that no shared function consumes would be a knob for
  a need that does not exist (ADR-0036).
- **No group resolver, unless it proves identical.** `EndpointScope` and
  `StatusProducer` both find the `MapGroup` a receiver was bound to.
  **Unverified whether they are byte-identical.** US3's task list makes the
  extraction conditional on that check and records the answer either way.

---

## 4. Entities, value objects, invariants

**None.** No domain model is touched. The three records above
(`HandlerBody`, `ClassSpan`, and each guard's own `RouteMapping`/`RouteGroup`)
are test-local data carriers, not domain models — constitution §II's primitive
ban binds domain models, and `PrimitiveBoundaryTests` walks `src/`, not `tests/`.

## 5. Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` change,
no Wolverine handler, no queue.

---

## 6. The fixture corpus

`SourceScanFixtures.cs` — in-file raw string literals, one `internal const
string` per case, each named for what it exercises. Minimum set, from spec AS-2:

| Fixture | Exercises | The three strictnesses… |
|---|---|---|
| `LineComment` | `// .ProducesProblem(Status409Conflict)` inside a chain | agree |
| `BlockComment` | `/* … */` spanning lines, newlines preserved | agree |
| `SemicolonInSummary` | `.WithSummary("does x; then y")` | agree in effect, differ in output |
| `UnbalancedParenInSummary` | `.WithSummary("see foo(")` | **differ** |
| `VerbatimString` | `@"a ""quoted"" path"` | **differ** |
| `EscapedQuote` | `"he said \"no\""` | **differ** |
| `QuoteAsCharLiteral` | `Split('"')` | **differ** |
| `OpenParenAsCharLiteral` | `IndexOf('(')` | **differ** (chain reader) |
| `RawStringLiteral` | `"""…"""` | **all three mis-read it** — asserted, so the shared blind spot is on the record |
| `StatementBodiedLambdaInChain` | `.AddEndpointFilter(async (c, n) => { int p = 1; return await n(c); })` | chain reader |
| `ExpressionBodiedHandler` | `private static IResult Get(…) => …;` | `HandlerBodyFor` |
| `HandlerInSecondPartialFile` | two `ClassSpan`s, one class | `HandlerBodyFor` |
| `MapGroupChain` | `var g = app.MapGroup("/x").RequireAuthorization();` | group walk |

**Raw-string delimiter trap.** `RawStringLiteral` cannot be written inside a
three-quote raw string — it needs a four-quote (or longer) delimiter. Phase 4a
must use `""""` and a reviewer must not "simplify" it back.

**These fixtures are also the documentation the issue asks for.** The table of
disagreements in `spec.md` §1.3 is prose; `SourceScanFixtures` is the executable
version, and it is what the seventh guard's author reads.

---

## 7. The characterisation harness

`SourceScanCharacterisationTests.cs`, written at **phase 4a**, before any
extraction, and observed **green** (ADR-0144).

**Two mechanisms, because one would not be enough.**

**M1 — the fixture golden test (permanent).** For every fixture × every
strictness, the expected output is written out **literally** in the test, not
hashed. A hash mismatch tells a reader nothing; a literal expectation shows them
exactly which character moved. Small enough to be readable because the fixtures
are small. It stays in the tree forever and is the artefact the next author
reads.

Plus the two assertions spec AS-2 requires and that a naive version would omit:
the three strictnesses must be shown to **disagree** on `VerbatimString`,
`EscapedQuote` and `QuoteAsCharLiteral`. A test that only asserted agreement
would pass against one collapsed masker.

**M2 — the real-corpus char-for-char sweep (transitional).** The file carries a
**frozen verbatim copy** of each pre-refactor masker (`MaskAsSpec072Wrote`,
`MaskAsSpec075Wrote`, `BlankCommentsAsSpec070Wrote`,
`BlankLiteralsAsSpec070Wrote`) and each pre-refactor `StatementEnd`. For every
`.cs` under `src/*/Api`, the shared implementation must return a char-for-char
identical string and an identical index; the first mismatch is reported with the
file, the offset and **both characters**.

M2 is deleted by US3's last task, together with the frozen copies. That is
deliberate: a permanent frozen copy of the thing you just deduplicated is the
defect this spec is closing. M2's job is to hold the migration, and the
migration ends.

**Why not a pinned hash of the real corpus as a permanent test?** Because it goes
red every time anybody edits an endpoint file, which is weekly. The before/after
hashes are captured as a **one-off** and recorded in `verification.md` (phase 5),
where they prove the claim without becoming a tax.

**M3 — the six guards' own output.** Captured with
`--logger "console;verbosity=detailed"` before and after, diffed modulo
durations, and **quoted in the PR body**. This is the phase-4a evidence ADR-0144
requires: the characterisation is the existing suite, and its output is the
proof.

---

## 8. Migration order and why

**One commit per story.** ADR-0087 rebase-merges, so each commit lands
individually on `develop` and must build and pass **on its own**, not merely at
the tip. Verify per commit.

1. **4a** — fixtures + characterisation (green).
2. **US1** — `RepositorySource`, six guards repointed. Smallest, zero-risk,
   proves the file layout and the `internal` visibility before anything
   interesting rides on it.
3. **US2** — `SourceMask`, five guards repointed, `UnmaskableLiteralForms`
   folded into `UnhandledForms`.
4. **US3** — `RouteChainReader`, five guards repointed, M2 and the frozen copies
   deleted.

**Per guard, the edit is always the same three steps**: add the call, delete the
private helper, run the guard's own class and diff its output. Never delete
first — a deleted helper and a mistyped call site fail together and the failure
names the wrong thing.

**Guard order within a story: hardest first.** `ConcurrencyConflictDeclaration
Tests` (the only `CommentsOnlyLiteralsIntact` + `StepOverStringAndCharLiterals`
user) and `EndpointScopeDeclarationTests` (the only `EndOfText` user) go first.
If either cannot be repointed without an assertion moving, the story stops there
and the three easy ones are not already half-migrated around it.

---

## 9. Risks

| Risk | Why it is real here | Control |
|---|---|---|
| A shared masker is collapsed to one behaviour by a later author | It is the exact failure the issue predicts, and "they're all nearly the same" is true today | No default on `MaskStrictness`; `UnhandledForms` per mode; the disagreement assertions in M1; the AS-4 counterfactual run once and recorded |
| `EndOfText` is "fixed" to `NotFound` | The existing doc already begs a reader not to, in prose nobody has tested | The AS-5 counterfactual, constructed once, with the `ArgumentOutOfRangeException` message quoted in `verification.md` |
| An assertion is edited to make a guard pass | The single most likely way this change does harm | Step 4 of the spec's §4 procedure: zero `Should*` lines in the diff, checked mechanically; phase 6 blocks on it |
| The fixture corpus misses a divergent form | Unverifiable by construction (spec A4) | M2's char-for-char sweep of the real corpus |
| `dotnet test` output diff is noisy | Durations, ordering | Compare the sorted `Passed`/`Failed` test-name lines, not the raw log |
| A new file trips an analyzer in Release | `TreatWarningsAsErrors`, collection expressions at `warning` | `dotnet build -c Release` is its own task, not folded into the test run |
| Release analyzer flake | Known: S125 has failed on a file outside the diff and passed on re-run at the same SHA | Re-run once before treating it as this branch's fault; record if it happens |

---

## 10. What this plan does not decide

- Whether the group-of-a-mapping resolver is extractable (§3.3). US3 checks and
  records; it is not assumed either way.
- Whether the remaining 25 root-finder copies get swept. Out of scope (spec §7),
  needs its own issue.
- Anything about what the six guards *assert*. Untouched, by construction.
