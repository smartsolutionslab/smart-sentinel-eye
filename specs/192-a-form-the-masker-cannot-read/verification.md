# Verification 192 — A form the masker cannot read (#2278)

**Latency: N/A.** Test-infrastructure guard; no production assembly changed,
no leg of constitution §IV's event→overlay path touched.

## Summary

`EndpointScopeDeclarationTests` had no equivalent of
`ConcurrencyConflictDeclarationTests.cs`'s corpus assertion proving
`src/*/Api` only uses source forms its masker can correctly handle. Without
it, a multi-line raw string (`"""`) could silently shift a statement
boundary and misattribute one mapping's declarations to its neighbour —
fail-*green*, the dangerous direction: the over-crediting guard passes
silently while the missing declaration is never reported at all. This was a
gap, not a live defect (`grep -rln '"""' src/*/Api/` finds nothing today).

## Phase 4a — compile-failure red (T004), quoted verbatim

```
D:\Github\sse-2278\tests\Architecture.Tests\EndpointScopeDeclarationTests.cs(1350,30): error CS0103: The name 'FormsThisReaderCannotMask' does not exist in the current context [D:\Github\sse-2278\tests\Architecture.Tests\SmartSentinelEye.Architecture.Tests.csproj]
D:\Github\sse-2278\tests\Architecture.Tests\EndpointScopeDeclarationTests.cs(1391,30): error CS0103: The name 'FormsThisReaderCannotMask' does not exist in the current context [D:\Github\sse-2278\tests\Architecture.Tests\SmartSentinelEye.Architecture.Tests.csproj]
```

This is the weakest form of red (a compile failure naming a not-yet-existing
member), and spec.md says so plainly. The real behavioral red is below.

## The companion characterisation test — proving the hazard is real today

`A_raw_string_in_a_chain_runs_one_mapping_into_the_next` characterises
*existing* `SourceMask`/`RouteChainReader` behaviour and must pass
unmodified — it is what stops the new corpus ban from being a rule nobody
can show matters. Captured green in isolation before the corpus ban existed
(`Passed! - Failed: 0, Passed: 1`), and independently re-derived by the
phase-6 reviewer from first principles in a scratch project:

```
--- RAW:     end=262 len=262 fellThrough=True  swallowsRequireAuthorization=True  swallows403=True   (first ';' at 125)
--- CONTROL: end=123 len=260 fellThrough=False swallowsRequireAuthorization=False swallows403=False (first ';' at 123)
```

The identical chain shape with an ordinary escaped string stops at its own
semicolon (123); the raw-string version steps past it and falls through to
`EndOfText` at 262. Strengthened after phase 6's review with a direct
`end.ShouldBeGreaterThan(masked.IndexOf(';'))` assertion, so the test now
pins the raw string as the specific cause rather than only asserting the
sentinel value and two downstream symptoms that were logically implied by
it.

## Phase 5 — behavioral red, quoted verbatim (T008)

Planted `.WithSummary("""see "foo( bar" now""")` in
`src/CameraCatalog/Api/CameraEndpoints.cs:45`, ran the full
`EndpointScopeDeclarationTests` suite (91 facts):

```
Failed SmartSentinelEye.Architecture.Tests.EndpointScopeDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask
  Shouldly.ShouldAssertException : offenders
    should be empty but had
1
    item and was
["src/CameraCatalog/Api/CameraEndpoints.cs:45 contains """ — a raw string literal, whose delimiter is longer than one quote"]
```

Only the new guard failed — the other 90 facts, including the pre-existing
`A15` walked-vs-swept backstop, stayed green. That backstop catches a
double-count but only by naming a number; the new guard is the only
mechanism that names a file and a line. Independently reproduced by the
phase-6 reviewer, byte-for-byte the same failure. Reverted with
`git checkout -- src/CameraCatalog/Api/CameraEndpoints.cs`; confirmed via
`git hash-object` (identical to the pre-plant hash) and
`git diff origin/develop --numstat` (back to the four files this PR
actually touches) that the file carries zero trace of the plant.

## `FormsThisReaderCannotMask` — no new detection logic

Reuses only `SourceMask.UnhandledForms(MaskStrictness)` (built by
#2257/spec 190) for both strictnesses this guard's two-stage masker
actually composes (`CommentsBlankedLiteralsIntact`, `LiteralInteriorsOnly`)
plus `RouteChainReader.LineOf` for the line number. No regex, no new
quote-walking, no new understanding of what forms are unmaskable — that
stays entirely in `SourceMask.cs`, unmodified by this change. Confirmed
character-for-character the same shape as
`ConcurrencyConflictDeclarationTests`'s own original version.

**Fixed after phase 6 review**: the ban list was originally constructed
twice — once inside `FormsThisReaderCannotMask` for detection, once inside
the corpus test itself purely to assert non-vacuity — reading the same two
`UnhandledForms` calls independently. Extracted both into one
`BannedForms()` method so the detection loop and its own non-vacuity check
can never come to describe different sets if a future edit touches one
copy and not the other.

## Non-vacuity, confirmed not trivially satisfied

`SourceMask.UnhandledForms` returns exactly one row —
`("\"\"\"", "a raw string literal, whose delimiter is longer than one
quote")` — for both strictnesses this guard uses. The ban list is
non-empty by construction, and the real corpus is genuinely clean today
(0 hits for `"""`, `@"`, `\"` under `src/*/Api`), so the guard has
something to check and nothing to currently report.

## Self-scan — no tripwire tripped

`EndpointScopeDeclarationTests`'s own self-scan
(`The_guard_offers_no_way_to_excuse_an_endpoint`, 29 theory cases across
three classes) stayed green throughout. Grepped the new code for all ten
banned words (`allowlist`, `whitelist`, `skiplist`, `baseline`, `exempt`,
`waiver`, `waived`, `knownViolation`, `suppress`,
`#pragma warning disable`) in identifiers and comments — zero hits.

## Full suite and build

- `dotnet test tests/Architecture.Tests` — **432/432 passed**, independently
  re-run by the orchestrator after phase 4b and again after the phase-6
  fix round.
- `dotnet build tests/Architecture.Tests -c Release` — **0 errors, 0
  warnings** (this file introduces no new advisory-metric findings).
- `git diff origin/develop --numstat` — four files, all additive: the three
  spec docs and `EndpointScopeDeclarationTests.cs`. `SourceMask.cs` and
  `RouteChainReader.cs` are untouched — this guard consumes shared code,
  it does not extend it.

## Follow-ups filed, not silently dropped

- **#2467** — the three sibling guards (`PreconditionDeclarationTests`,
  `RouteValueRefusalDeclarationTests`, `StatusProducerDeclarationTests`)
  have the identical exposure and the identical missing assertion.
  Deliberately not folded into this PR (would have diluted the
  counterfactual evidence for the one guard named here). On Project #13.
- **F002, recorded but not filed separately** — `EndpointScopeDeclarationTests.cs`'s
  own G-series assertion messages still reference `MaskLiterals` by name
  in one place (a pre-existing Shouldly message this PR does not own);
  `SourceMask.cs` itself also still says "WithoutComments then MaskLiterals"
  in a provenance comment. Left alone here since a comment-only fix carries
  its own phase-4a obligation and touching it would muddy this slice's red;
  whoever picks it up should correct both mentions, not just the first one
  found.
