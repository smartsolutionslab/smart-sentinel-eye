# Plan 192 — A form the masker cannot read

Implements `spec.md`. Issue [#2278](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2278).

## 1. Bounded context and layers

**None.** This is test infrastructure. No bounded context, no Domain, no
Application, no Infrastructure, no Api, no entity, no value object, no domain
event, no integration event, no migration, no Aspire resource, no scope, no
endpoint.

The whole diff is **one file**:

```
tests/Architecture.Tests/EndpointScopeDeclarationTests.cs
```

Explicitly **not** touched: `SourceMask.cs`, `RouteChainReader.cs`,
`SourceScanFixtures.cs`, `SourceScanCharacterisationTests.cs`, `RepositorySource.cs`,
anything under `src/`.

Boundary rules (no cross-context project references; communication only via
`Shared.Contracts`) are unaffected — nothing here references a context.

## 2. What is added

Three members in `EndpointScopeDeclarationTests`, in this order in the file:
the two `[Fact]`s next to the existing counterfactual
`A_statement_bodied_lambda_does_not_end_the_chain_before_its_own_authorization`
(`:1207`), the private helper down in the "reading the chain" helper region
beside `Masked()` (`:1869`).

### 2.1 `The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask` — `[Fact]`

The real-corpus assertion. Name deliberately identical to
`ConcurrencyConflictDeclarationTests.cs:757`: the two guards assert the same
*kind* of fact about the same directory tree for two different readers, and
someone grepping the name should find both.

Body:

1. Build the offender list by calling the helper (§2.3) over
   `ApiSourceFiles().Select(file => (file, ReadRepositoryFile(file)))`.
2. Assert the corpus is non-empty **before** asserting it is clean.
3. Assert the ban list is non-empty.
4. `offenders.ShouldBeEmpty(<message>)`.

Steps 2 and 3 exist because an assertion that passes against nothing is this
repository's most-repeated defect, and both inputs are external to this file:
`ApiSourceFiles()` depends on the directory layout, `UnhandledForms` on
`SourceMask`. `EndpointSourceFiles()` (`:1832`) and A15's
`swept.ShouldBeGreaterThan(0)` (`:1123`) are the two local precedents; mirror
them rather than inventing a third shape.

### 2.2 `A_raw_string_in_a_chain_runs_one_mapping_into_the_next` — `[Fact]`

The counterfactual that makes the ban load-bearing. **Characterisation of
existing behaviour — it arrives green** (spec §6), and it is what stops the ban
above from being a rule nobody can show matters.

Source text: the probe from spec §2, inline as a `const string` built by escaped
concatenation, exactly like the neighbouring counterfactual at `:1210-1216`.

> **Delimiter trap — do not "simplify".** The fixture's own content contains a
> three-quote delimiter. Written as a three-quote raw string in this file it
> closes early; it would need four quotes (`SourceScanFixtures.cs:17-23` records
> the same trap). Escaped concatenation sidesteps it and matches the
> neighbour. Do not convert it.

Asserts, in this order:

1. `RouteChainReader.StatementEnd(Masked(source), 0, ChainEndSentinel.EndOfText, ChainLiteralHandling.AlreadyMasked)`
   returns `masked.Length` — the `EndOfText` sentinel, i.e. the chain's own `;`
   was never found. **Assert the sentinel by name, not by the literal 262**: a
   character added to the fixture must not turn this red for the wrong reason.
2. The span contains `RequireAuthorization` — the next mapping's authorization,
   which `FirstAuthorizationCall` would hand to this mapping.
3. The span contains `Status403Forbidden` — the next mapping's refusal
   declaration, double-counted by A15.

Each message must say **what this proves about the guard above**, not merely
what the walk did: that the ban is the only thing standing between this form and
a mapping being credited with its neighbour's scope. And the first message must
say that if this test goes **red** because the masker learned raw strings, the
correct response is to delete the ban, not to edit the assertion.

### 2.3 The offender-collecting helper — `private static`

```
private static string[] FormsThisReaderCannotMask(IEnumerable<(string File, string Text)> sources)
```

Returns one `file:line contains <form> — <why>` line per (file, form) whose
first occurrence is found, mirroring `ConcurrencyConflictDeclarationTests.cs:762-772`
(first occurrence only, via `IndexOf`; `RouteChainReader.LineOf` for the line).

It takes a **sequence of `(File, Text)` pairs rather than reading files itself.**
That is the whole reason the helper exists: it makes the detection testable
against synthetic text, which is what discharges the red (spec §6). It is not
speculative generality — without it there is no way to observe this change fail.

Reads **raw** text, never `Masked(...)`. Masking is what destroys the form being
looked for.

## 3. Which strictnesses the ban list is read for

The reader is a **two-stage composition** (`Masked()`, `:1869-1870`):
`CommentsBlankedLiteralsIntact` then `LiteralInteriorsOnly`. The helper therefore
iterates **both** and takes forms distinct by `Form`:

```
MaskStrictness[] stages = [MaskStrictness.CommentsBlankedLiteralsIntact, MaskStrictness.LiteralInteriorsOnly];
```

Today both return the same single row, so the offender list is identical either
way. Naming both anyway is the honest spelling: asserting against one stage is a
claim about half the reader, and `SourceMask.UnhandledForms` is a `switch` whose
rows can diverge per strictness — it already does, for
`CommentsOnlyLiteralsIntact`. Cost is one array and a `Distinct`.

Deliberately **not** `CommentsOnlyLiteralsIntact` or
`CommentsAndLiteralInteriors`: this guard applies neither, and banning `@"` or
`\"` from `src/*/Api` on behalf of a reader that handles them would be a rule
invented here rather than a fact about this reader.

## 4. The failure message

Written for this reader, **not copied from the sibling.**
`ConcurrencyConflictDeclarationTests.cs:776-783` describes a single-pass masker
that "treats an unescaped double quote as opening a literal that ends at the next
one on the same line" — true of `CommentsOnlyLiteralsIntact` and false of this
guard's two stages. Copying it would put a wrong explanation behind a correct
failure, which is worse than no explanation.

It must say:

- **What broke:** the form is one `Masked()` cannot read.
- **Why it matters here specifically:** since #2183, `RouteChainReader.StatementEnd`
  decides where a chain ends by counting brackets on the masked text. A bracket
  the mask fails to blank keeps depth positive, the span runs past its own `;`,
  and the mapping is credited with the **next** mapping's
  `.RequireAuthorization` and `.ProducesProblem`.
- **Which direction that fails in:** silently, in the passing direction, for
  authorization and summary agreement. A15's walked-versus-swept arithmetic
  catches only the 403 count, and reports a number rather than a file.
- **What to do:** keep the form out of `src/*/Api`, or teach `SourceMask` the
  form — which is a behaviour change with its own issue
  (`SourceMask.cs:11-13`), not something to do inside a failing build.

## 5. Constraints and conventions

| Rule | How this complies |
|---|---|
| ADR-0053, sentence-style test names with underscores | Both `[Fact]` names are sentences |
| ADR-0052, xUnit + Shouldly | `ShouldBeEmpty` / `ShouldNotBeEmpty` / `ShouldBe` / `ShouldContain` with a reason string on every assertion |
| ADR-0054, hand-written fixtures | The counterfactual source is an inline `const string`; no AutoFixture |
| ADR-0091, no shortcuts or aliases | `FormsThisReaderCannotMask`, not `BadForms`; no `Src`, `Decl`, `Repo` |
| Collections: explicit type + collection expression | `List<string> offenders = [];`, `MaskStrictness[] stages = [...]` |
| No leading underscore on private fields | No new fields |
| `var` allowed | Fine where the right-hand side names the type |
| ADR-0084 code metrics | `EndpointScopeDeclarationTests.cs` is **2085 lines** today and already emits S104 (300 LOC/file). This adds ~80. Advisory, carved out of Release's `TreatWarningsAsErrors`; recorded, not fixed here — splitting a 2000-line guard is its own change |
| ADR-0144, no weakening a gate to reach green | Nothing is deleted, lowered, suppressed or narrowed. Purely additive |

### 5.1 The guard's own self-scan

`The_guard_offers_no_way_to_excuse_an_endpoint` (`:1174`) scans
`RepositorySource.ExecutableLines` of `GuardSource` plus `SharedSourceScanFiles`
for ten words: `allowlist`, `whitelist`, `skiplist`, `baseline`, `exempt`,
`waiver`, `waived`, `knownViolation`, `suppress`, `#pragma warning disable`.

`ExecutableLines` strips string-literal content, so message prose is safe — but
**identifiers and comments are not.** None of the three new members may use any
of those words in code. This is a real tripwire, not a hypothetical: "exempt" and
"baseline" are natural words for what a ban list is, and would fail the build in
a way whose message points at the wrong rule.

### 5.2 Nothing else scans the new fixture

The new counterfactual puts a three-quote sequence into
`EndpointScopeDeclarationTests.cs`. Checked: the new corpus guard reads
`src/*/Api` only; the self-scan above looks for ten specific words; no other
`tests/Architecture.Tests` guard greps this file for quote forms. Task T5 runs
the whole `Architecture.Tests` project anyway, because "checked and reasoned" is
not the same as "ran".

## 6. Messaging

None. No domain event, no integration event, no `Shared.Contracts` change, no
Wolverine handler, no outbox.

## 7. Risk

| Risk | Mitigation |
|---|---|
| The ban is decorative — nobody can show the form actually breaks anything | §2.2's counterfactual, already run in spec §2 and reproduced as a permanent test |
| The assertion passes against an empty corpus or an empty ban list | §2.1 steps 2-3 |
| The message explains the wrong masker | §4 — written fresh, sibling's copy explicitly rejected |
| The helper's fixture closes its own raw-string delimiter early | §2.2's trap note; escaped concatenation |
| A word in the new code trips the guard's own self-scan | §5.1 |
| The spec number collides once PR #2465 merges | Re-check `specs/` before the PR is opened (T6) |
