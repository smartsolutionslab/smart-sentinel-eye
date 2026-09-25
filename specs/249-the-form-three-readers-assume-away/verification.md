# Verification — spec 249, issue #2467

Per `tasks.md` T009 and `spec.md` §5, quoting every output verbatim. Re-run
on this tree 2026-09-25 (the artifact was missing from the commit that
implemented the guards; commands below reproduce what that commit's own
message summarised, against the current, unmodified tree).

**Framing, read this before the evidence below.** This slice has *two*
separate claims and *two* separate pieces of evidence — they must not be
conflated:

1. **The ban fires — detection mechanics work end to end on a real file.**
   Proven by the compile-red (§1) and the planted-corpus red→revert-green
   (§3) below. The plant used (`.WithSummary("""…""")`, content otherwise
   unchanged) has no brackets or inner quotes between its first two interior
   quotes, so wrapping it in triple-quotes produces a masked result with the
   *same* correct statement boundary as before masking. It proves file
   enumeration, CRLF stripping, `RouteChainReader.LineOf`'s line numbers and
   the message wording all correctly fire on a real file — **not** that a
   statement boundary actually shifted or that a declaration was
   misattributed to a neighbour.
2. **The hazard itself is real — a statement boundary can silently
   shift/misattribute declarations to a neighbour.** Proven separately, by
   §2 below: `SourceScanCharacterisationTests`' pre-declared premise-check
   (T001), which plants an *unbalanced-bracket* raw string and shows the
   `CommentsAndLiteralInteriors` mask + `RouteChainReader.StatementEnd`
   chain genuinely runs to the end of the file. This is what makes the ban
   load-bearing rather than decorative.

Do not read §3 as demonstrating the hazard — it demonstrates the guard.

---

## 1. Phase 4a compile red (T005)

The phase-4a gate artifact ADR-0139/ADR-0144 require: proves the tests came
before the helpers, not that they can fail at runtime. Reproduced by
temporarily removing `BannedForms()`/`FormsThisReaderCannotMask(...)` from
all three guard files (the tests that call them left untouched) and
building:

```
$ dotnet build tests/Architecture.Tests -c Release --no-incremental
...
D:\Github\sse-2526\tests\Architecture.Tests\StatusProducerDeclarationTests.cs(639,30): error CS0103: The name 'FormsThisReaderCannotMask' does not exist in the current context [D:\Github\sse-2526\tests\Architecture.Tests\SmartSentinelEye.Architecture.Tests.csproj]
D:\Github\sse-2526\tests\Architecture.Tests\StatusProducerDeclarationTests.cs(675,9): error CS0103: The name 'BannedForms' does not exist in the current context [D:\Github\sse-2526\tests\Architecture.Tests\SmartSentinelEye.Architecture.Tests.csproj]
D:\Github\sse-2526\tests\Architecture.Tests\StatusProducerDeclarationTests.cs(679,30): error CS0103: The name 'FormsThisReaderCannotMask' does not exist in the current context [D:\Github\sse-2526\tests\Architecture.Tests\SmartSentinelEye.Architecture.Tests.csproj]
D:\Github\sse-2526\tests\Architecture.Tests\RouteValueRefusalDeclarationTests.cs(484,30): error CS0103: The name 'FormsThisReaderCannotMask' does not exist in the current context [D:\Github\sse-2526\tests\Architecture.Tests\SmartSentinelEye.Architecture.Tests.csproj]
D:\Github\sse-2526\tests\Architecture.Tests\RouteValueRefusalDeclarationTests.cs(520,9): error CS0103: The name 'BannedForms' does not exist in the current context [D:\Github\sse-2526\tests\Architecture.Tests\SmartSentinelEye.Architecture.Tests.csproj]
D:\Github\sse-2526\tests\Architecture.Tests\RouteValueRefusalDeclarationTests.cs(524,30): error CS0103: The name 'FormsThisReaderCannotMask' does not exist in the current context [D:\Github\sse-2526\tests\Architecture.Tests\SmartSentinelEye.Architecture.Tests.csproj]
D:\Github\sse-2526\tests\Architecture.Tests\PreconditionDeclarationTests.cs(696,30): error CS0103: The name 'FormsThisReaderCannotMask' does not exist in the current context [D:\Github\sse-2526\tests\Architecture.Tests\SmartSentinelEye.Architecture.Tests.csproj]
D:\Github\sse-2526\tests\Architecture.Tests\PreconditionDeclarationTests.cs(732,9): error CS0103: The name 'BannedForms' does not exist in the current context [D:\Github\sse-2526\tests\Architecture.Tests\SmartSentinelEye.Architecture.Tests.csproj]
D:\Github\sse-2526\tests\Architecture.Tests\PreconditionDeclarationTests.cs(736,30): error CS0103: The name 'FormsThisReaderCannotMask' does not exist in the current context [D:\Github\sse-2526\tests\Architecture.Tests\SmartSentinelEye.Architecture.Tests.csproj]
    9 Error(s)
```

Nine `CS0103` errors — three per guard file (`FormsThisReaderCannotMask`
referenced twice, `BannedForms` once), matching `tasks.md` T005's
expectation and `9201606d`'s commit message ("failed to compile (CS0103)
until now"). The temporary removal was reverted with `git checkout --` on
the three test files before any further step; no test file's content
differs from the committed tree.

---

## 2. T001 — the hazard premise-check, green on its first run

This is the evidence that the underlying hazard — a statement boundary
silently shifting/misattributing declarations to a neighbour — is real in
this codebase's masking/walking pipeline. Declared green in advance by the
architect (spec §6): "the corpus facts **cannot** be observed red against
the real corpus… so they arrive green," and separately, T001 plants an
unbalanced-bracket raw string
(`"""see "foo( bar" now"""`) against a synthetic two-mapping fixture and
asserts the chain end is `-1` (not found) — showing the walk runs off the
first mapping's own statement into the rest of the chain.

```
$ dotnet test tests/Architecture.Tests -c Release --no-restore --filter "FullyQualifiedName~SourceScanCharacterisationTests.A_raw_string_runs_a_CommentsAndLiteralInteriors_chain_to_the_end_of_the_file"
...
Test run for D:\Github\sse-2526\tests\Architecture.Tests\bin\Release\net10.0\SmartSentinelEye.Architecture.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 34 ms - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

Passed on its first run — this confirms the premise, not a red-then-green
pair. Per spec §6, this test "must pass unmodified" going forward; if
`SourceMask` later learns raw strings, it is expected to go red, and its
own message says to remove the bans rather than edit the assertion.

---

## 3. Phase 5 — the planted-corpus plant/red/revert (T009, spec §5 steps 2-4)

**What this proves and what it does not.** The plant below shows detection
mechanics fire end to end on a real file — enumeration, masking, line
numbering, message wording — and that #2278's existing
`EndpointScopeDeclarationTests` guard fires on the same plant too. It does
**not** demonstrate the boundary-shift hazard itself: the planted text
(`Register a camera in the resolved fab. Omit fabId when you belong to
exactly one fab; ` wrapped in `"""…"""`, content otherwise unchanged) has no
brackets or inner quotes between its first two interior quotes, so the
masked result keeps the *same* correct statement boundary as before masking
— §2 above is what proves the hazard is real.

### Step 1 — filter green before the plant

```
$ dotnet build tests/Architecture.Tests -c Release --no-incremental
...
Build succeeded.
    0 Error(s)
```

(confirmed separately below at step 4, run against the identical,
unplanted tree)

### Step 2 — plant

`src/CameraCatalog/Api/CameraEndpoints.cs:45`, `.WithSummary("""...""")`
in place of the plain string, content otherwise unchanged:

```diff
             .WithSummary(
-                "Register a camera in the resolved fab. Omit fabId when you belong to exactly one fab; "
+                """Register a camera in the resolved fab. Omit fabId when you belong to exactly one fab; """
                 + "name it when you belong to several (ADR-0114). Required scope: sse.cameras.write")
```

### Step 3 — re-run step 1's filter plus `EndpointScopeDeclarationTests`

```
$ dotnet test tests/Architecture.Tests -c Release --no-build --filter "FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests|FullyQualifiedName~EndpointScopeDeclarationTests"
...
[xUnit.net 00:00:02.17]     SmartSentinelEye.Architecture.Tests.RouteValueRefusalDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask [FAIL]
[xUnit.net 00:00:02.18]     SmartSentinelEye.Architecture.Tests.StatusProducerDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask [FAIL]
[xUnit.net 00:00:02.18]     SmartSentinelEye.Architecture.Tests.PreconditionDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask [FAIL]
  Failed SmartSentinelEye.Architecture.Tests.PreconditionDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask [488 ms]
  Error Message:
   Shouldly.ShouldAssertException : offenders
    should be empty but had
1
    item and was
["src/CameraCatalog/Api/CameraEndpoints.cs:45 contains """ — a raw string literal, whose delimiter is longer than one quote"]

Additional Info:
    1 source file(s) under src/*/Api use a literal form this reader's masker does not handle:
src/CameraCatalog/Api/CameraEndpoints.cs:45 contains """ — a raw string literal, whose delimiter is longer than one quote
This reader masks in one pass — SourceMask.Apply(…, CommentsAndLiteralInteriors) — that walks a literal's quotes in pairs, so the text between a raw string's first two interior quotes is left as code. An unbalanced bracket there keeps RouteChainReader.StatementEnd's depth positive past the chain's own semicolon; with ChainEndSentinel.NotFound the chain becomes the rest of the file, and this mapping is credited with every later mapping's 428 and 400 declarations. Keep the form out of src/*/Api, or teach SourceMask to read it — a behaviour change with its own issue, not something to do inside a failing build.
  Stack Trace:
     at SmartSentinelEye.Architecture.Tests.PreconditionDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask() in D:\Github\sse-2526\tests\Architecture.Tests\PreconditionDeclarationTests.cs:line 737
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed SmartSentinelEye.Architecture.Tests.StatusProducerDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask [487 ms]
  Error Message:
   Shouldly.ShouldAssertException : offenders
    should be empty but had
1
    item and was
["src/CameraCatalog/Api/CameraEndpoints.cs:45 contains """ — a raw string literal, whose delimiter is longer than one quote"]

Additional Info:
    1 source file(s) under src/*/Api use a literal form this reader's masker does not handle:
src/CameraCatalog/Api/CameraEndpoints.cs:45 contains """ — a raw string literal, whose delimiter is longer than one quote
This reader masks in one pass — SourceMask.Apply(…, CommentsAndLiteralInteriors) — that walks a literal's quotes in pairs, so the text between a raw string's first two interior quotes is left as code. An unbalanced bracket there keeps RouteChainReader.StatementEnd's depth positive past the chain's own semicolon; with ChainEndSentinel.NotFound the chain becomes the rest of the file, and this mapping is credited with every later mapping's 401 challenge declaration and authorized/anonymous classification. Keep the form out of src/*/Api, or teach SourceMask to read it — a behaviour change with its own issue, not something to do inside a failing build.
  Stack Trace:
     at SmartSentinelEye.Architecture.Tests.StatusProducerDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask() in D:\Github\sse-2526\tests\Architecture.Tests\StatusProducerDeclarationTests.cs:line 678
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed SmartSentinelEye.Architecture.Tests.RouteValueRefusalDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask [542 ms]
  Error Message:
   Shouldly.ShouldAssertException : offenders
    should be empty but had
1
    item and was
["src/CameraCatalog/Api/CameraEndpoints.cs:45 contains """ — a raw string literal, whose delimiter is longer than one quote"]

Additional Info:
    1 source file(s) under src/*/Api use a literal form this reader's masker does not handle:
src/CameraCatalog/Api/CameraEndpoints.cs:45 contains """ — a raw string literal, whose delimiter is longer than one quote
This reader masks in one pass — SourceMask.Apply(…, CommentsAndLiteralInteriors) — that walks a literal's quotes in pairs, so the text between a raw string's first two interior quotes is left as code. An unbalanced bracket there keeps RouteChainReader.StatementEnd's depth positive past the chain's own semicolon; with ChainEndSentinel.NotFound the chain becomes the rest of the file, and this mapping is credited with every later mapping's 400 refusal declaration. Keep the form out of src/*/Api, or teach SourceMask to read it — a behaviour change with its own issue, not something to do inside a failing build.
  Stack Trace:
     at SmartSentinelEye.Architecture.Tests.RouteValueRefusalDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask() in D:\Github\sse-2526\tests\Architecture.Tests\RouteValueRefusalDeclarationTests.cs:line 525
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:02.74]     SmartSentinelEye.Architecture.Tests.EndpointScopeDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask [FAIL]
  Failed SmartSentinelEye.Architecture.Tests.EndpointScopeDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask [81 ms]
  Error Message:
   Shouldly.ShouldAssertException : offenders
    should be empty but had
1
    item and was
["src/CameraCatalog/Api/CameraEndpoints.cs:45 contains """ — a raw string literal, whose delimiter is longer than one quote"]

Additional Info:
    1 source file(s) under src/*/Api use a literal form this reader's masker does not handle:
src/CameraCatalog/Api/CameraEndpoints.cs:45 contains """ — a raw string literal, whose delimiter is longer than one quote
This reader is a two-stage composition — CommentsBlankedLiteralsIntact then LiteralInteriorsOnly, see Masked() — that walks a literal's quotes in pairs, so an unbalanced bracket inside a raw string's interior keeps RouteChainReader.StatementEnd's depth counter positive past the chain's own semicolon: the span runs into the next mapping, and that mapping is credited with its neighbour's RequireAuthorization and ProducesProblem declarations. Since issue 2183 that bracket-depth walk is how this reader finds a chain's end at all. Keep the form out of src/*/Api, or teach SourceMask to read it — a behaviour change with its own issue, not something to do inside a failing build.
  Stack Trace:
     at SmartSentinelEye.Architecture.Tests.EndpointScopeDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask() in D:\Github\sse-2526\tests\Architecture.Tests\EndpointScopeDeclarationTests.cs:line 1396
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     4, Passed:   173, Skipped:     0, Total:   177, Duration: 2 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

All four corpus facts (the three new ones plus #2278's) fail, each naming
`src/CameraCatalog/Api/CameraEndpoints.cs:45`, the `"""` form, and why that
guard's reader cannot read it. No other assertion went red.

### Step 4 — revert; re-run green

```
$ git checkout -- src/CameraCatalog/Api/CameraEndpoints.cs
$ git diff --stat -- src/
(empty)
$ dotnet build tests/Architecture.Tests -c Release --no-incremental
...
Build succeeded.
    0 Error(s)
$ dotnet test tests/Architecture.Tests -c Release --no-build --filter "FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests|FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~SourceScanCharacterisationTests"
...
Test run for D:\Github\sse-2526\tests\Architecture.Tests\bin\Release\net10.0\SmartSentinelEye.Architecture.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   193, Skipped:     0, Total:   193, Duration: 2 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

---

## 4. Full `Architecture.Tests` project (plan §5.2)

```
$ dotnet test tests/Architecture.Tests -c Release --no-build
...
Test run for D:\Github\sse-2526\tests\Architecture.Tests\bin\Release\net10.0\SmartSentinelEye.Architecture.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   462, Skipped:     0, Total:   462, Duration: 32 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

---

## 5. Solution build

```
$ dotnet build -c Release --no-incremental
...
    218 Warning(s)
    0 Error(s)
```

All warnings are the pre-existing SonarAnalyzer advisory metrics (ADR-0084)
in files this slice does not touch. `git diff --stat -- src/` is empty
throughout this verification pass.
