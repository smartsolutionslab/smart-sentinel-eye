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

**This is not a separate tool, and the M2 half described below no longer
exists.** `SourceScanCharacterisationTests.cs` had two regions:

- **M1 — the fixture golden test, permanent.** Every fixture in
  `SourceScanFixtures.cs` masked under every strictness and read by every
  chain-reader shape, asserted against a pinned expected output. This is what
  remains today, and it is the ongoing regression guard for `SourceMask` and
  `RouteChainReader` — not a before/after diff, a fixture corpus with a known
  answer per fixture.
- **M2 — the frozen-copy comparison, throwaway, deleted at T29.** Before the
  extraction existed, this region held a frozen, verbatim copy of each
  guard's own masker/chain-reader code and swept `src/*/Api` comparing them
  char-for-char (`AssertIdenticalAcrossRealCorpus` and named facts around
  it) — the actual before/after proof AS-1 and AS-3 asked for, run once
  while both the old per-guard copies and the new shared ones existed
  side by side. T29 deleted the whole region, frozen copies included, once
  every guard was repointed at `SourceMask` / `RouteChainReader` and had
  nothing left to compare against.

So a reviewer today runs the **M1 fixture facts** to convince themselves the
shared reader still does what every fixture says it should; there is no
standing M2 sweep to run because there is nothing left for it to compare —
the "before" side (the frozen per-guard copies) was the thing being deleted.
A reviewer who wants an actual before/after diff against `origin/develop`
reconstructs it by hand: check out `origin/develop` in a second worktree, run
the loop below there and on this branch, and diff the two outputs — the
frozen copies this file's loop borrows are still recoverable from
`origin/develop`'s `SourceScanCharacterisationTests.cs` even though this
branch no longer carries them.

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
shared surface (plan.md §3.3 — "no `Read(...)` façade"). The (now-deleted) M2
facts did this correctly *inside* the test project, using each guard's own
mapping shape (a generic `.Map[A-Za-z]*\(` for the four already-masked-input
guards, `.(Post|Put|Patch|Delete)\(` for the literal-aware one) — that is
where the before/after proof for the chain-end walks belonged while it still
existed, and where a reviewer reconstructing it against `origin/develop`
should put it again, not in a second standalone script that would have to
duplicate the same judgement call.

## Why this file exists rather than only the code

Spec.md §4 step 2 is written as an instruction to "a reviewer who has read
none of the above." Pointing that reviewer at test method names inside a
1,300-line characterisation file is not self-explanatory without this map;
this file is that map, not a second implementation.
