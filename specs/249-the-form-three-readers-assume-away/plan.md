# Plan 249 — The form three readers assume away

`spec.md` in this directory. Issue
[#2467](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2467).

## 1. Context and layers

**No bounded context.** Test-only change in `tests/Architecture.Tests/` (the
NetArchTest / source-scanning guard project). No Domain, Application,
Infrastructure or Api layer is touched; no entity, value object, domain event or
integration event is introduced; no `Shared.Contracts` or `Shared.Kernel` change;
no AppHost resource. Boundary rules are unaffected — nothing here references a
bounded context's assembly.

**Files edited (four, disjoint):**

| File | Adds |
|---|---|
| `tests/Architecture.Tests/PreconditionDeclarationTests.cs` | corpus `[Fact]`, detection `[Fact]`, `FormsThisReaderCannotMask` helper |
| `tests/Architecture.Tests/RouteValueRefusalDeclarationTests.cs` | same three members |
| `tests/Architecture.Tests/StatusProducerDeclarationTests.cs` | same three members |
| `tests/Architecture.Tests/SourceScanCharacterisationTests.cs` | one hazard characterisation `[Fact]` |

**Read, never edited:** `SourceMask.cs`, `RouteChainReader.cs`,
`RepositorySource.cs`, `EndpointScopeDeclarationTests.cs`,
`ConcurrencyConflictDeclarationTests.cs`, anything under `src/`.

## 2. Design — per guard (identical in all three)

### 2.1 The helper

Mirror `EndpointScopeDeclarationTests.FormsThisReaderCannotMask`
(`EndpointScopeDeclarationTests.cs:2052-2070`), with one simplification: this
reader applies **one** strictness, so there is no `BannedForms()` union and no
`DistinctBy` — the ban list is `SourceMask.UnhandledForms(MaskStrictness.CommentsAndLiteralInteriors)`
directly, read once in a single private member (e.g. a `private static
IReadOnlyList<(string Form, string Why)> BannedForms() => SourceMask.UnhandledForms(MaskStrictness.CommentsAndLiteralInteriors);`)
so the helper and the non-vacuity assertion cannot describe different sets.

```csharp
private static string[] FormsThisReaderCannotMask(IEnumerable<(string File, string Text)> sources)
```

- Reads the **raw** text as given — never the masked text, because masking
  destroys the form being looked for.
- One `"{file}:{line} contains {form} — {why}"` entry per (file, form); first
  occurrence via `IndexOf(form, StringComparison.Ordinal)`; line via
  `RouteChainReader.LineOf(text, at)`.
- Returns `[.. offenders]` (collection-expression house rule).

The strictness named at the call site must be `CommentsAndLiteralInteriors` —
the one each guard's `Read()` applies. Not `CommentsOnlyLiteralsIntact` (a
different, larger list, for a reader these guards do not use).

### 2.2 The detection test

`A_planted_unmaskable_form_is_reported_with_its_file_and_line` — mirror
`EndpointScopeDeclarationTests.cs:1337-1372`: two in-memory pairs, one with the
three-quote delimiter on line 2, one clean; assert exactly one offender; assert it
contains `"{file}:2"` (file and line together), the form, and the reason text
`"raw string literal"`.

**Fixture text is built by escaped concatenation, never a raw string** — a raw
string in the guard's own source would itself be the trap (StatusProducer's L8
self-scan masks its own file with `CommentsAndLiteralInteriors`, `:595`).

### 2.3 The corpus fact

`The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask` —
same name as the two precedents, so a grep for the name finds every guard.

1. `files = ApiSourceFiles(RepositorySource.Root())`; `files.ShouldNotBeEmpty(…)`.
2. `BannedForms().ShouldNotBeEmpty(…)`.
3. Build `(file, text)` pairs with the reading the guard's own `Read()` uses —
   `File.ReadAllText(Path.Combine(root.FullName, file)).Replace("\r", string.Empty, StringComparison.Ordinal)`
   (StatusProducer may use its existing `ReadRepositoryFile`, `:767`).
4. `offenders.ShouldBeEmpty(message)`.

Both non-vacuity assertions come **before** the emptiness assertion.

### 2.4 The failure message — written per guard, not copied

The message must explain **this** reader, not spec 192's two-stage reader and not
`ConcurrencyConflictDeclarationTests`' single-pass comments-only mask (whose
explanation is false here). Common core:

> This reader masks in one pass — `SourceMask.Apply(…, CommentsAndLiteralInteriors)`
> — that walks a literal's quotes in pairs, so the text between a raw string's
> first two interior quotes is left as code. An unbalanced bracket there keeps
> `RouteChainReader.StatementEnd`'s depth positive past the chain's own
> semicolon; with `ChainEndSentinel.NotFound` the chain becomes the rest of the
> file, and this mapping is credited with every later mapping's declarations.
> Keep the form out of `src/*/Api`, or teach `SourceMask` to read it — a
> behaviour change with its own issue, not something to do inside a failing build.

Plus one guard-specific clause naming what is borrowed: the **428 / 400** for
Precondition; the **400** refusal for RouteValueRefusal; the **401 challenge and
the authorized/anonymous classification** for StatusProducer.

## 3. Design — the shared hazard characterisation

In `SourceScanCharacterisationTests.cs`, one `[Fact]`, e.g.
`A_raw_string_runs_a_CommentsAndLiteralInteriors_chain_to_the_end_of_the_file`:

- Inline escaped-concatenation fixture, two mappings; the first's
  `.WithSummary` is `"""see "foo( bar" now"""` (bracket between interior quotes
  1 and 2 — spec 192 §2 showed a bracket after the second interior quote is
  blanked and the boundary comes out right, so placement matters).
- `masked = SourceMask.Apply(source, MaskStrictness.CommentsAndLiteralInteriors)`;
  `end = RouteChainReader.StatementEnd(masked, 0, ChainEndSentinel.NotFound, ChainLiteralHandling.AlreadyMasked)`.
- Assert `end.ShouldBe(-1, …)` — the first chain's end is never found, which is
  what makes each guard's `masked[call.Index..]` fallback swallow the file. The
  message says: if this goes red because `SourceMask` learned raw strings, delete
  the three bans (and spec 192's), do not edit this assertion.

**Why one shared test, not three.** The hazard lives in two shared primitives
with identical arguments in all three guards (spec §1 table); three copies would
characterise the same call three times. The guard-specific consequence is
explained in each guard's message (§2.4), and observed for real at phase 5.

**Must be captured GREEN before any helper exists** (characterisation, spec §6).
If it arrives red, stop: the spec's premise about this pipeline is wrong, and the
slice needs re-planning, not a different fixture.

## 4. Messaging / events

None. No domain event, no integration event, no RabbitMQ message.

## 5. Risks and checks

### 5.1 Self-scans that read the guard's own source

- `PreconditionDeclarationTests.The_guard_offers_no_way_to_excuse_an_endpoint`
  (`:635-661`) scans raw lines (skipping `//` and `[` lines, stripping string
  literals) for `allowlist, whitelist, skiplist, baseline, exempt, waiver, waived,
  knownViolation, suppress, #pragma warning disable`.
- `StatusProducerDeclarationTests` L8 (`:583-605`) scans its own masked source for
  the same list minus `baseline`.

No new identifier, and no code outside a string literal or comment, in any of the
three files may contain those words. `BannedForms` is safe; `Baseline…` is not.

### 5.2 Other scans of `tests/Architecture.Tests`

The fixtures add `\"\"\"` sequences to four test files. Run the **whole**
`Architecture.Tests` project, not a filter (tasks T009) — "reasoned" is not
"ran".

### 5.3 Size (ADR-0084, advisory)

Current sizes: Precondition 897, RouteValueRefusal 714, StatusProducer 817 LOC —
all already over the advisory 300; each grows by ~70. Advisory only; not a
reason to split in this slice.

### 5.4 Pinned counts

None affected. `The_precondition_corpus_is_seventeen_endpoints_across_seven_files`,
`The_endpoint_file_glob_still_finds_twelve_files` and StatusProducer's census
count `src/` mappings and handler registrations, not test methods.
`Every_mechanism_the_census_calls_derived_names_a_guard_that_exists` checks named
methods exist; adding methods cannot break it.

## 6. Constitution / ADR alignment

- §Testing / ADR-0139, ADR-0144: red declared (spec §6), one characterisation
  declared green.
- ADR-0036: no new detection logic; no consolidation riding along (spec §7).
- ADR-0052/0053/0054: Shouldly, sentence-style names, hand-written fixtures.
- House rules: collection expressions; no leading-underscore fields; `var`
  optional; no drive-by comments beyond the doc comments on the new members,
  matching each file's existing style.
