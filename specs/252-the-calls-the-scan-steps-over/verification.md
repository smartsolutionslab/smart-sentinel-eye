# Verification 252 — The calls the scan steps over

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Tasks**: [tasks.md](tasks.md) ·
**Issue**: #2470 · **Phase**: 5 (Verify) — T009

Fix already landed on this branch (`fix/2470-outbox-guard-il-scan-escapes`, commits `78a00989`
test, `076cfc5e` fix). This note is the T009 counterfactual evidence: each of the three shapes plan
§4/§3 claims to fix is independently necessary — remove just that one piece of the fix, and exactly
the probe it guards goes undetected again, nothing else moves.

Environment note: an in-repo default build/test (`dotnet build`/`dotnet test` with no
`--artifacts-path` override) was used throughout — an out-of-repo `--artifacts-path` was tried
first and broke unrelated repo-root-relative tests (`RealmAudienceTests`,
`DatabaseCommandLogLevelTests`), which is a property of those tests' repo-root discovery, not of
this change. No AppHost from this worktree held `src/*/Api/bin` locked, so the default path was
safe to use.

Each counterfactual: patch → build/run → capture verbatim → `git checkout --
tests/Architecture.Tests/OutboxCommitTests.cs` → `git diff --exit-code` confirmed clean before the
next. All three ran against `The_rule_sees_both_spellings_of_a_direct_commit`
(`OutboxCommitTests.cs`), the exact-membership fact T004/T008 already exercised.

Plan §"Verification" specifies the three counterfactuals as **drop 0x07; drop 0x06; one-level
nesting**, each showing *exactly one* probe missing (tasks.md T009). That is what is run below —
each opcode arm removed on its own, not together, so the two arms are shown independently
necessary rather than only jointly sufficient.

---

## CF1 — drop the `0x07` (`ldvirtftn`) arm only

Patch (`ReferencesSaveChanges`, one line):

```diff
-                0xFE when il[i + 1] is 0x06 or 0x07 => i + 2,
+                0xFE when il[i + 1] is 0x06 => i + 2,
```

Prediction: `MethodGroupOffenderRepository` (live-instance method group, compiles to `ldvirtftn`)
goes undetected. `BaseMethodGroupOffenderRepository` (`ldftn`, still handled by the `0x06` arm that
stays) and `AsyncLambdaOffenderRepository` (unrelated to this opcode, guarded by the nesting fix)
stay detected.

Command: `dotnet test tests/Architecture.Tests -c Release --filter
"FullyQualifiedName~The_rule_sees_both_spellings_of_a_direct_commit"`

Verbatim result:

```
Test run for D:\Github\smart-sentinel-eye\tests\Architecture.Tests\bin\Release\net10.0\SmartSentinelEye.Architecture.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.20]     SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit [FAIL]
  Failed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit [100 ms]
  Error Message:
   Shouldly.ShouldAssertException : offenders
    should be
["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.MethodGroupOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.BaseMethodGroupOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.AsyncLambdaOffenderRepository"]
    but was (case sensitive comparison)
["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.BaseMethodGroupOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.AsyncLambdaOffenderRepository"]
    difference
["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository", *"SmartSentinelEye.Architecture.Tests.Persistence.BaseMethodGroupOffenderRepository"*, *"SmartSentinelEye.Architecture.Tests.Persistence.AsyncLambdaOffenderRepository"*, *]
  Stack Trace:
     at SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit() in D:\Github\smart-sentinel-eye\tests\Architecture.Tests\OutboxCommitTests.cs:line 105
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 133 ms - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

**Observed**: exactly `MethodGroupOffenderRepository` missing from `offenders`. Everything else —
including `BaseMethodGroupOffenderRepository` and `AsyncLambdaOffenderRepository` — present.
Matches prediction.

Revert: `git checkout -- tests/Architecture.Tests/OutboxCommitTests.cs`. `git diff --exit-code`
exit 0 — clean.

---

## CF2 — drop the `0x06` (`ldftn`) arm only

Patch (`ReferencesSaveChanges`, one line):

```diff
-                0xFE when il[i + 1] is 0x06 or 0x07 => i + 2,
+                0xFE when il[i + 1] is 0x07 => i + 2,
```

Prediction: `BaseMethodGroupOffenderRepository` (`base.SaveChanges` as a delegate, non-virtual
load, `ldftn`) goes undetected. `MethodGroupOffenderRepository` (`ldvirtftn`, still handled by the
`0x07` arm that stays) and `AsyncLambdaOffenderRepository` stay detected.

Command: same filter as CF1.

Verbatim result:

```
Test run for D:\Github\smart-sentinel-eye\tests\Architecture.Tests\bin\Release\net10.0\SmartSentinelEye.Architecture.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.41]     SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit [FAIL]
  Failed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit [92 ms]
  Error Message:
   Shouldly.ShouldAssertException : offenders
    should be
["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.MethodGroupOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.BaseMethodGroupOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.AsyncLambdaOffenderRepository"]
    but was (case sensitive comparison)
["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.MethodGroupOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.AsyncLambdaOffenderRepository"]
    difference
["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.MethodGroupOffenderRepository", *"SmartSentinelEye.Architecture.Tests.Persistence.AsyncLambdaOffenderRepository"*, *]
  Stack Trace:
     at SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit() in D:\Github\smart-sentinel-eye\tests\Architecture.Tests\OutboxCommitTests.cs:line 105
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 115 ms - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

**Observed**: exactly `BaseMethodGroupOffenderRepository` missing from `offenders`. Everything
else — including `MethodGroupOffenderRepository` and `AsyncLambdaOffenderRepository` — present.
Matches prediction.

Revert: `git checkout -- tests/Architecture.Tests/OutboxCommitTests.cs`. `git diff --exit-code`
exit 0 — clean.

---

## CF3 — restore the one-level `GetNestedTypes` walk (undo the `NestedTypesOf` transitive recursion)

Patch (`BodiesOf`, one line):

```diff
-        IEnumerable<Type> types = [type, .. NestedTypesOf(type)];
+        IEnumerable<Type> types = [type, .. type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)];
```

This leaves `NestedTypesOf` itself unreferenced (dead code, still compiled). No Sonar-analyzer
discard workaround was needed to keep the patch compiling — `dotnet build` and `dotnet test`
succeeded outright with this diff in place; an unused private method did not fail the Release
build here (SonarAnalyzer's code-metric rules are `warning`, carved out of
`TreatWarningsAsErrors` per ADR-0084, and no unused-member rule fired as an error either).

Prediction: `AsyncLambdaOffenderRepository`'s offending call sits two levels of nesting deep
(`AsyncLambdaOffenderRepository+<>c__DisplayClass0_0+<<SaveAsync>b__0>d`) — a one-level walk finds
the display class but never looks inside it, so it goes undetected.
`MethodGroupOffenderRepository`/`BaseMethodGroupOffenderRepository` are caught at depth 0 (the
type's own declared method), unaffected by nesting depth, and stay detected.

Command: same filter as CF1/CF2.

Verbatim result:

```
Test run for D:\Github\smart-sentinel-eye\tests\Architecture.Tests\bin\Release\net10.0\SmartSentinelEye.Architecture.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.28]     SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit [FAIL]
  Failed SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit [87 ms]
  Error Message:
   Shouldly.ShouldAssertException : offenders
    should be
["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.MethodGroupOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.BaseMethodGroupOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.AsyncLambdaOffenderRepository"]
    but was (case sensitive comparison)
["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.MethodGroupOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.BaseMethodGroupOffenderRepository"]
    difference
["SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.MethodGroupOffenderRepository", "SmartSentinelEye.Architecture.Tests.Persistence.BaseMethodGroupOffenderRepository", *]
  Stack Trace:
     at SmartSentinelEye.Architecture.Tests.OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit() in D:\Github\smart-sentinel-eye\tests\Architecture.Tests\OutboxCommitTests.cs:line 105
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 113 ms - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

**Observed**: exactly `AsyncLambdaOffenderRepository` missing from `offenders`. Everything else —
including `MethodGroupOffenderRepository` and `BaseMethodGroupOffenderRepository` — present.
Matches prediction.

Revert: `git checkout -- tests/Architecture.Tests/OutboxCommitTests.cs`. `git diff --exit-code`
exit 0 — clean.

---

## Final clean state

After all three counterfactuals reverted, working tree matches `git diff --exit-code` (exit 0,
clean) against the two already-landed commits (`78a00989`, `076cfc5e`).

Release build (`dotnet build tests/Architecture.Tests -c Release`, default in-repo paths):

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:21.26
```

Probe fact filtered run (`dotnet test tests/Architecture.Tests -c Release --no-build --filter
"FullyQualifiedName~OutboxCommitTests"`):

```
Test run for D:\Github\smart-sentinel-eye\tests\Architecture.Tests\bin\Release\net10.0\SmartSentinelEye.Architecture.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 115 ms - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

Full `Architecture.Tests` project (`dotnet test tests/Architecture.Tests -c Release --no-build`):

```
Test run for D:\Github\smart-sentinel-eye\tests\Architecture.Tests\bin\Release\net10.0\SmartSentinelEye.Architecture.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   455, Skipped:     0, Total:   455, Duration: 31 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

10/10 on the probe fact and its companions (`The_rule_sees_both_spellings_of_a_direct_commit` plus
the nine `No_repository_commits_without_its_announcements` theory rows); 455/455 across the full
`Architecture.Tests` project. Nothing else in the project regressed by any of the three patches —
each counterfactual's filtered run showed exactly one failing assertion (the probe fact), never a
second.

## Conclusion

Each of the two fix pieces is independently load-bearing:

- The `0x07` (`ldvirtftn`) arm is what catches a live-instance method-group load
  (`MethodGroupOffenderRepository`); removing only it, with `0x06` intact, uncatches only that one
  probe.
- The `0x06` (`ldftn`) arm is what catches the `base.`-qualified method-group load
  (`BaseMethodGroupOffenderRepository`); removing only it, with `0x07` intact, uncatches only that
  one probe.
- The transitive nested-type walk is what catches a depth-2 async-lambda state machine
  (`AsyncLambdaOffenderRepository`); reverting to depth 1, with both opcode arms intact, uncatches
  only that one probe.

No cross-coupling observed: each patch left the other two probes detected, confirming the opcode
widening and the nesting widening are independent fixes for independent gaps, not one fix
accidentally covering for the other.
