# Verification — Spec 190 (issue #2257)

Written at phase 6's findings-fix pass, after the phase-6 `backend-reviewer`
found this file missing (tasks.md T35) despite T19, T28 and T35 all pointing
at it. Everything below was re-run by the agent that wrote this file, not
copied from an earlier report — every figure and every counterfactual output
is what that re-run actually printed.

## 1. The baseline figures, corrected

tasks.md and spec.md originally recorded the pre-change whole-project
`Architecture.Tests` baseline as **189**. That was wrong. The real figure is
**414**.

```
Passed!  - Failed:     0, Passed:   414, Skipped:     0, Total:   414, Duration: 14 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

Measured by building a detached worktree at `origin/develop` (commit
`f256aaf7`) and running:

```sh
dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj -c Release --nologo
```

**189 was a measurement error in the original whole-project figure as first
recorded, not a real prior state of the project.** The six guards' own
subtotal was always right:

```
Passed!  - Failed:     0, Passed:   199, Skipped:     0, Total:   199, Duration: 1 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

(filtered to `PreconditionDeclarationTests|RouteValueRefusalDeclarationTests|StatusProducerDeclarationTests|ConcurrencyConflictDeclarationTests|EndpointScopeDeclarationTests|PaginatedConsumerTests`,
run against `origin/develop` and unchanged on this branch — see §2).

**Confirmed independently three times now**: phase 4a's `test-writer`,
the phase-6 `backend-reviewer`, and this findings-fix pass all measured 414
against `origin/develop` separately. `spec.md` §1.1 and `tasks.md` (the
phase-4a-colour declarations section) have been corrected to state 414, note
the six-guard subtotal of 199, and record 189 as the original error rather
than a real prior state.

## 2. The six guards, byte-identical before and after

On this branch, after the findings-fix pass (the `HandlerBodyFor` redesign,
the self-scan extension, the doc-comment corrections, the `RegexOptions`
revert):

```
Passed!  - Failed:     0, Passed:   199, Skipped:     0, Total:   199, Duration: 1 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

Same 199 as `origin/develop`. No `[Fact]`/`[Theory]` body, pinned count or
expected-message string in any of the six guard files changed — see §3 for
why the `HandlerBodyFor` redesign does not count as such a change, and §5 for
the mechanical proof.

The whole-project total on this branch is **429** (414 the six guards
contributed to, plus the fifteen permanent M1 fixture-golden facts
`SourceScanCharacterisationTests.cs` added — M2's throwaway frozen-copy
comparison was deleted at T29 and contributes nothing here; see
`tools/capture-surface.md`).

```
Passed!  - Failed:     0, Passed:   429, Skipped:     0, Total:   429, Duration: 3 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

## 3. `HandlerBodyFor` — the three-message shape, restored

Phase-6 found that `RouteChainReader.HandlerBodyFor` had collapsed
`PreconditionDeclarationTests`' and `RouteValueRefusalDeclarationTests`'
three distinct failure messages (unreadable argument / zero candidates / N
candidates) into one generic string, and dropped `MethodGroupName` +
`Ellipsis` from `PreconditionDeclarationTests` entirely — losing the
file:line pairs for the duplicate-declaration case. That is a change to what
a guard reports, which the spec forbids even though nothing in the real
corpus currently reaches any of the three failure paths.

**Fix.** `HandlerBodyFor` now returns a `HandlerResolution` record —
`(HandlerBody? Body, bool IsBareMethodGroupName, IReadOnlyList<HandlerBody>
Candidates)` — instead of a bare `HandlerBody?`. It still does the shared
resolution mechanics only; each guard reconstructs its own original wording
from the record's fields:

- `!IsBareMethodGroupName` → `"the handler argument '{Ellipsis(...)}' is not
  a bare method-group name"` (`Ellipsis` restored to both guards).
- `Body is null && Candidates.Count == 0` → `"'{Class}.{Handler}' resolves to
  no method declaration in {project}"`.
- `Body is null && Candidates.Count > 1` → `"'{Class}.{Handler}' resolves to
  {n} method declarations ({file}:{line}, ...)"`, with the same
  `file:line` pairs as before (`Candidates` carries the full `HandlerBody`
  list, not just a count).

Both guards' `Resolve` methods were edited to match — this touches only the
non-`Should*` computation of `offenders`/the failure string passed into
`Unreadable(...)`; the `[Fact]`/`[Theory]` bodies of the tests that assert
these shapes are untouched (there are none exercising the three paths today,
per the class docs — the corpus does not reach them).

**Verified.** Since the real corpus does not reach any of the three paths,
byte-identical G4-style test output can't distinguish the fix from the bug it
replaces by itself. A throwaway xUnit fixture (constructed input, run once,
deleted before commit — not part of the final diff) called
`RouteChainReader.HandlerBodyFor` directly against three constructed inputs
and confirmed each shape is reachable and distinguishable:

```
Passed SmartSentinelEye.Architecture.Tests.HandlerResolutionProbe.Resolves_to_zero [4 ms]
Passed SmartSentinelEye.Architecture.Tests.HandlerResolutionProbe.Not_a_bare_method_group_name [< 1 ms]
Passed SmartSentinelEye.Architecture.Tests.HandlerResolutionProbe.Resolves_to_two [11 ms]

CANDIDATE src/Foo/Api/FooEndpoints.cs:3
CANDIDATE src/Foo/Api/FooEndpoints.cs:7
```

— `IsBareMethodGroupName` is `false` only for the unreadable-argument case;
`Body` is `null` with an empty `Candidates` for zero matches; `Body` is
`null` with a populated, correctly-`File`/`Line`-tagged `Candidates` for the
ambiguous case. The message-formatting code in both guards was also
line-by-line diffed against `origin/develop`'s original `Resolve` bodies and
is textually identical apart from reading through `resolution.Body` /
`resolution.Candidates` instead of local variables of the same shape.

## 4. The self-scan extension — additive, not an assertion change

Phase-6 found that after this consolidation, nothing scans
`RepositorySource.cs`, `SourceMask.cs` or `RouteChainReader.cs` themselves —
an exemption added to any of the three shared files would pass all six
guards' own "no way to excuse" self-scans silently.

Of the six guards, only two actually have a self-scan built on
`RepositorySource.ExecutableLines` — `EndpointScopeDeclarationTests` and
`PaginatedConsumerTests` (confirmed by grep: `RouteValueRefusalDeclarationTests`
and `ConcurrencyConflictDeclarationTests` have never had a "no way to excuse"
self-scan, on `origin/develop` or on this branch; `StatusProducerDeclarationTests`
has one but built on `SourceMask.Apply` directly, a different mechanism the
finding did not ask to change).

**Fix.** Both guards gained a `SharedSourceScanFiles` array naming the three
shared files, and their `offenders` computation now sweeps
`SharedSourceScanFiles.Prepend(GuardSource)` instead of `GuardSource` alone:

```csharp
string[] offenders = SharedSourceScanFiles.Prepend(GuardSource)
    .SelectMany(file => RepositorySource.ExecutableLines(ReadRepositoryFile(file)))
    .Where(line => line.Contains(mechanism, StringComparison.OrdinalIgnoreCase))
    .ToArray();
```

**Additive, confirmed.** The `offenders.ShouldBeEmpty(...)` call — the
assertion itself, including its message — is byte-for-byte unchanged in both
files; only what `offenders` is computed *from* changed, from one file to
four. The mechanical gate (§5) confirms no `Should*` line in any of the six
guard files changed.

## 5. The mechanical gate

```sh
git diff -U0 origin/develop -- \
  tests/Architecture.Tests/PreconditionDeclarationTests.cs \
  tests/Architecture.Tests/RouteValueRefusalDeclarationTests.cs \
  tests/Architecture.Tests/StatusProducerDeclarationTests.cs \
  tests/Architecture.Tests/ConcurrencyConflictDeclarationTests.cs \
  tests/Architecture.Tests/EndpointScopeDeclarationTests.cs \
  tests/Architecture.Tests/PaginatedConsumerTests.cs \
  | grep -cE 'Should[A-Z]'
```

Result: **0**. Confirmed three times now — after the original extraction
(phase 4b), after the orchestrator's own fixture fix, and again now, after
this findings-fix pass (which touches `PreconditionDeclarationTests.cs` and
`RouteValueRefusalDeclarationTests.cs` for finding 1, and all six guard files
except `PreconditionDeclarationTests.cs`/`RouteValueRefusalDeclarationTests.cs`'s
overlap for finding 2 — `EndpointScopeDeclarationTests.cs` and
`PaginatedConsumerTests.cs`). No pinned count, expected-message string or
`Should*` line changed in any of the six.

## 6. T27 — is the group-of-a-mapping resolver extractable?

**No.** Recorded in `RouteChainReader.cs`'s class doc (not re-derived here,
since it was already checked and written down during the phase 4b
extraction): `EndpointScopeDeclarationTests` resolves a group from any
`var`/`RouteGroupBuilder` local declaration whose statement contains a
`MapGroup` call, refuses a name declared twice, and separately registers
routes mapped outside the readable chain shapes.
`StatusProducerDeclarationTests` resolves a group with one regex anchored to
`<variable> = <builder>.MapGroup("…")` and does none of that bookkeeping.
Different shapes, different failure modes — both stay local to their own
guard, per plan.md §3.3's conditional extraction clause.

## 7. AS-4 — counterfactual, re-run, verbatim

**Claim.** Collapsing `CommentsOnlyLiteralsIntact` to the stronger
`CommentsAndLiteralInteriors` mode should fail
`ConcurrencyConflictDeclarationTests`' route-classification register (blanked
route text can't be classified), while `The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask`
(the `UnmaskableLiteralForms`-adjacent corpus assertion, which reads raw,
unmasked text) should still pass.

**Change made** (temporary, reverted immediately after capture — confirmed
by `git diff` showing no residual change to `SourceMask.cs`):

```csharp
// tests/Architecture.Tests/SourceMask.cs, SourceMask.Apply
MaskStrictness.CommentsOnlyLiteralsIntact => CommentsAndLiteralInteriorsMask(text), // was: CommentsOnlyLiteralsIntactMask(text)
```

**Run** (`dotnet test … -c Debug --filter FullyQualifiedName~ConcurrencyConflictDeclarationTests`
— Debug only for this throwaway run, to avoid the pre-existing-unused-method
analyzer error `-c Release` would raise on the now-orphaned
`CommentsOnlyLiteralsIntactMask`; that analyzer question is orthogonal to the
counterfactual itself).

**Verbatim failure** (the register assertion):

```
[xUnit.net 00:00:01.16]     SmartSentinelEye.Architecture.Tests.ConcurrencyConflictDeclarationTests.Every_mutating_route_sits_in_exactly_one_of_the_two_pinned_sets [FAIL]
[xUnit.net 00:00:01.16]       Shouldly.ShouldAssertException : unclassified
[xUnit.net 00:00:01.16]           should be empty but had
[xUnit.net 00:00:01.16]       35
[xUnit.net 00:00:01.16]           items and was
[xUnit.net 00:00:01.16]       ["src/Automation/Api/RulesEndpoints.cs:41 Automation POST        ", "src/Automation/Api/RulesEndpoints.cs:50 Automation POST                      ", ... 35 entries total ...]
[xUnit.net 00:00:01.16]
[xUnit.net 00:00:01.16]       Additional Info:
[xUnit.net 00:00:01.16]           35 mutating endpoint(s) appear in neither pinned set:
[xUnit.net 00:00:01.16]       src/Automation/Api/RulesEndpoints.cs:41 Automation POST
[xUnit.net 00:00:01.16]       ... (35 lines) ...
[xUnit.net 00:00:01.16]       Classify each one here, in tests/Architecture.Tests/ConcurrencyConflictDeclarationTests.cs, by answering: can any of the four mechanisms answer 409 on this route — a command handler returning an ApiError whose Status is Conflict, EF's affected-row check on an UPDATE or DELETE, a unique index written through, or IdempotentRequest finding an earlier call with the same key still running? If any can, add the route to CanAnswerConflict with the mechanism and declare StatusCodes.Status409Conflict on its chain. If none can, add it to CannotAnswerConflict with the reason that clears all four. This guard is a register and cannot answer that question for you — but it will not let it go unanswered.
```

(Full 35-item list preserved in the agent's working output; every one of the
33 route-classification mappings' route text and 2 already-classified routes'
route text came back blank once `CommentsOnlyLiteralsIntact` also blanked
literal interiors, so the register lookup by route text failed to match any
pinned entry.)

**Verbatim pass** (the corpus assertion, same run):

```
  Passed SmartSentinelEye.Architecture.Tests.ConcurrencyConflictDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask [52 ms]
```

Full result line for the run: `Total tests: 13, Passed: 12, Failed: 1` —
exactly the one register assertion, as the claim predicted.

**Reverted.** `git diff tests/Architecture.Tests/SourceMask.cs` against the
committed state shows no difference after reverting the one-line change.

## 8. AS-5 — counterfactual, re-run, verbatim

**Claim.** Passing `ChainEndSentinel.NotFound` where
`EndpointScopeDeclarationTests` expects `ChainEndSentinel.EndOfText` should
throw `ArgumentOutOfRangeException` naming the `length` parameter, at a
slice-bound call site.

**Not naturally reproducible against the real corpus** — confirmed again
here: every real chain under `src/*/Api` has a terminating semicolon today,
so `StatementEnd` never actually returns the `NotFound` sentinel (-1) on
`EndpointScopeDeclarationTests`' three real call sites, whichever sentinel
they're given. Simply editing those three call sites from `EndOfText` to
`NotFound` and re-running the guard proves nothing — it stays green either
way, for the wrong reason (the branch is never taken).

**Constructed instead** (throwaway xUnit fixture, run once, deleted before
commit — not part of the final diff): a string with no top-level terminating
semicolon, fed straight through the shared mechanism the guard's call sites
use.

```csharp
string masked = "no terminating semicolon here";
int end = RouteChainReader.StatementEnd(masked, 0, ChainEndSentinel.NotFound, ChainLiteralHandling.AlreadyMasked);
// end == -1
string slice = masked[0..end]; // masked[0..(-1)] — the throwing line
```

**Verbatim output:**

```
PARAM: length
MESSAGE: length ('-1') must be a non-negative value. (Parameter 'length')
Actual value was -1.
```

Test result: `Passed SmartSentinelEye.Architecture.Tests.AS5Probe.NotFound_sentinel_used_as_a_slice_bound_throws_naming_length [14 ms]`.

Confirms the class doc's claim exactly: `ArgumentOutOfRangeException`, naming
`length` (not the index), because `masked[x..(-1)]` lowers to
`Substring(x, -1)` and `Substring`'s negative-length guard is what fires.

**Removed.** The throwaway fixture file was deleted after capture;
`git status` shows no trace of it.

## 9. Honesty block (per T35)

The fixture corpus's completeness (does `SourceScanFixtures.cs` actually
cover every literal/comment shape the three masker strictnesses and five
chain-reader copies need to agree on?) is **unverifiable by construction** —
M1 only proves the shared code matches its own pinned fixtures, not that the
fixtures are exhaustive. M2's frozen-copy sweep over the real corpus was the
backstop for that gap, comparing old and new against every real file rather
than a curated fixture list, and **M2 is now deleted** (T29, once every guard
was repointed and had nothing left to compare against). A shape that exists
in the real corpus but in none of the fixtures and was never exercised by M2
while it still ran would not be caught by anything that runs today. This is
the same residual `tools/capture-surface.md` already names for a reviewer
who wants to reconstruct the before/after diff by hand.
