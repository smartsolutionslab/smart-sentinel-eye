# Capturing the derived surface (spec.md §4, step 2)

Written at phase 4a, before any extraction exists. This is the harness spec.md
§4 step 2 refers to — what a reviewer runs on `origin/develop` and again on
this branch to confirm the two are byte-identical.

## What "the surface" is

For every `.cs` file under `src/*/Api`:

- the masked text under each of the three masker behaviours (spec.md §1.3:
  `CommentsOnlyLiteralsIntact`, the two-stage `CommentsBlankedLiteralsIntact` →
  `LiteralInteriorsOnly`, and `CommentsAndLiteralInteriors`);
- the chain-end index each of the five `StatementEnd` copies returns at every
  mapping-call site in that file.

## Where it actually lives

**This is not a separate tool.** The comparison is `SourceScanCharacterisationTests.cs`'s
M2 region:

- `AssertIdenticalAcrossRealCorpus` sweeps every file under `src/*/Api` and
  compares two functions' output char-for-char, reporting the file, the
  offset and both differing characters on the first mismatch — exactly AS-3's
  requirement, not a hash.
- `The_three_byte_identical_A_maskers_already_agree_on_every_api_source_file`
  and `The_three_byte_identical_StatementEnd_walks_already_agree_at_every_mapping_call_in_the_real_corpus`
  run that sweep today, before any extraction, comparing the frozen
  per-guard copies against each other.
- `The_two_stage_masker_preserves_length_on_every_api_source_file`,
  `The_literal_aware_StatementEnd_walk_finds_a_terminator_for_every_mutating_mapping_in_the_real_corpus`
  and `The_end_of_text_StatementEnd_walk_never_returns_the_not_found_sentinel_on_the_real_corpus`
  are the smoke checks for the three shapes that have no identical sibling to
  compare against yet.

Phase 4b's T13 extends this region to compare each frozen copy against
`SourceMask.Apply` / `RouteChainReader.StatementEnd` once those exist — at
that point the sweep is comparing *old* against *new*, which is the actual
before/after proof AS-1 and AS-3 ask for. T29 deletes the whole region,
frozen copies included, once every guard is repointed.

## Running it as a standalone before/after diff, if a reviewer wants a file

The xUnit facts above are pass/fail, not a printed digest — sufficient for
CI, but if a reviewer wants an actual before-file and after-file to diff
(rather than trusting a green run twice), the loop below reproduces the
masker half using the same frozen bodies. It is not part of the test project
(it never needs to compile once the sources it names are deleted at T29), and
it is deliberately dumb: same repository-root walk, same `\r`-stripping, one
line per file per strictness.

```csharp
// Paste into a scratch console app (dotnet run --project) with the three
// frozen masker methods copied in from SourceScanCharacterisationTests.cs
// (MaskAsSpec072Wrote, MaskAsSpec075Wrote, BlankCommentsAsSpec070Wrote,
// BlankLiteralsAsSpec070Wrote). Run once on origin/develop, once on the
// branch under review; `sort` both outputs; diff.
using System.Security.Cryptography;
using System.Text;

DirectoryInfo root = RepositoryRoot(); // same walk as the guards
foreach (string file in ApiSourceFiles(root)) // src/*/Api/**/*.cs, minus obj/bin
{
    string text = File.ReadAllText(Path.Combine(root.FullName, file))
        .Replace("\r", string.Empty, StringComparison.Ordinal);

    string a = MaskAsSpec072Wrote(text);
    string c = MaskAsSpec075Wrote(text);
    string b = BlankLiteralsAsSpec070Wrote(BlankCommentsAsSpec070Wrote(text));

    Console.WriteLine($"{file}\tA\t{Digest(a)}");
    Console.WriteLine($"{file}\tC\t{Digest(c)}");
    Console.WriteLine($"{file}\tB\t{Digest(b)}");
}

static string Digest(string s) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
```

The chain-end half is not reproduced as a standalone loop here: it needs an
anchor (a mapping-call site) to ask `StatementEnd` about, and which regex
finds an anchor is exactly the guard-specific rule spec 190 keeps out of the
shared surface (plan.md §3.3 — "no `Read(...)` façade"). The M2 facts already
do this correctly *inside* the test project, using each guard's own mapping
shape (a generic `.Map[A-Za-z]*\(` for the four already-masked-input guards,
`.(Post|Put|Patch|Delete)\(` for the literal-aware one) — that is where the
before/after proof for the chain-end walks belongs, not in a second
standalone script that would have to duplicate the same judgement call.

## Why this file exists rather than only the code

Spec.md §4 step 2 is written as an instruction to "a reviewer who has read
none of the above." Pointing that reviewer at test method names inside a
1,300-line characterisation file is not self-explanatory without this map;
this file is that map, not a second implementation.
