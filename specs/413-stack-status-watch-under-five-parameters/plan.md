# Plan 413: the stack-status watch loop under five parameters

**Spec**: [spec.md](./spec.md). **Issue**: #3028.

## 1. Where

Composition root (`src/AppHost`), not a bounded context: no Domain/Application layers, no
entities, no messaging, no `Shared.Contracts` change. One file: `src/AppHost/StackStatusReport.cs`.
Boundary rules unaffected (no project reference added).

## 2. The change

### 2.1 The grouping record

Nested in `StackStatusReport`, directly above the existing
`private readonly record struct ResourceStatusLine` (`StackStatusReport.cs:322`), mirroring it:

```csharp
/// <summary>The report the watch loop keeps current: where it is written, which resources it names, and each instance's last state.</summary>
private readonly record struct StatusReportTarget(
    string Path,
    HashSet<string> ExpectedNames,
    Dictionary<string, ResourceStatusLine> Instances);
```

Why these three: they are the report's own state, built together in `Attach` and only read
together. `notifications` is the event source, `logger` is the fault sink, and
`cancellationToken` must stay last and separate (ADR-0049) — so the signature falls to four, the
limit, with the smallest grouping that gets there.

### 2.2 Signature, first body line, call site

```csharp
private static async Task WatchAndWriteAsync(
    ResourceNotificationService notifications,
    StatusReportTarget report,
    ILogger logger,
    CancellationToken cancellationToken)
{
    var (path, expectedNames, instances) = report;

    try
    ...   // unchanged, byte for byte, through the closing brace
```

In `Attach`, hoist the construction into a local immediately after `var logger = ...;` and before
`app.Services.GetRequiredService<IDistributedApplicationEventing>()`, so the lambda stays one line:

```csharp
var report = new StatusReportTarget(path, expectedNames, instances);
...
_ = WatchAndWriteAsync(app.ResourceNotifications, report, logger, cancellationToken);
```

Semantics are identical: the struct holds the same three references (the same `Dictionary` the
seed `WriteReport` just used), and `AfterResourcesCreatedEvent` fires once. `path` is non-null by
flow analysis after the `IsNullOrWhiteSpace` early return, so the `string` field compiles without
`!`.

**Transposition safety.** The three fields have three distinct types, so swapping two at the
construction site or in the deconstruction is CS1503/CS0029, not a silent behaviour change.

**Doc comment.** The method's existing `<summary>` is kept unchanged.

## 3. Forbidden

Any body line of `WatchAndWriteAsync` other than the signature and one deconstruction line plus its
blank line; `AppHost.cs`; `Attach` / `ExpectedResourceNames` signatures; `FoldWorstPerName`,
`Severity`, `WriteReport`; any suppression, `.editorconfig`, `SonarLint.xml`,
`Directory.Build.props`, csproj; visibility changes; ADRs, constitution, CLAUDE.md. After C0, no
file under `tests/`.

## 4. Characterisation (Phase 4a, green before, unmodified after)

### 4.1 Preferred: in-process, through public API (spike A-1)

New `tests/Integration.Tests/StackStatusReportWatchCharacterisationTests.cs`,
`[Trait("Category", "FixtureLogic")]` (Docker-free, runs locally and in CI's fixture-logic step).
Per test: temp path; `DistributedApplicationTestingBuilder.Create()` with
`AddContainer("watched", "image")` and `AddParameter("Secret", secret: true)`; `BuildAsync()`;
`StackStatusReport.Attach(app, path)`; publish `AfterResourcesCreatedEvent(app.Services, model)`
via `IDistributedApplicationEventing.PublishAsync`; drive `ResourceNotificationService`; poll the
file (bounded, e.g. 10 s) until the expected content or fail. Cases:

1. Seed: after `Attach`, before any event, line `watched\t(not reported)\t`, no `Secret` line.
2. Happy: `PublishUpdateAsync(watched, s => s with { State = KnownResourceStates.Running })` →
   `watched\tRunning\t`.
3. Exit code: `State = Finished, ExitCode = 3` → `watched\tFinished\t3`.
4. Filter: an update on `Secret` → no `Secret` line ever appears (assert after case 2's write is
   observed, so the absence is checked on a file the loop has already rewritten).
5. Null/whitespace path → no file created.

Header line is asserted by prefix (`# stack-status v1 `) only — its timestamp is not
deterministic. Assertions name literal expected lines (an-assertion-must-not-check-its-own-input).

**Counterfactuals** (each run on a scratch edit, observed red, reverted; reported verbatim):
CF1 delete the in-loop `WriteReport(...)` call → case 2 red; CF2 drop the
`expectedNames.Contains` `continue` → case 4 red; CF3 replace `ExitCode?.ToString(...)` with
`string.Empty` → case 3 red. One that stays green is fixed in the test before C0 is committed.

Shard filters are **inclusion** lists, and `AppHostStackStatusTests` is listed in
`tests/Integration.Tests/ci-shards/shard-3.filter` despite also running in the fixture-logic step.
Append `|FullyQualifiedName~SmartSentinelEye.Integration.Tests.StackStatusReportWatchCharacterisationTests.`
to `shard-3.filter` in C0; a class in no shard fails deterministically.

### 4.2 Fallback if A-1 fails (no new test)

The spike's failure is quoted verbatim in the PR. Evidence a reviewer should accept, together:

1. **Textual identity.** Hash the method body from `try` through its closing `}` on base and tip
   (`sed -n '/^        try$/,/^    }$/p'` scoped to `WatchAndWriteAsync`, `sha256sum`); equal
   hashes. Counterfactual: change one character in the scratch copy, observe a different hash.
2. **Type-checked binding.** Release build 0 errors (distinct field types make transposition a
   compile error).
3. **Behaviour, on CI.** The e2e job's "Wait for the stack to be ready" step green in all four
   shards on the base commit's run and on the PR tip's run; cite both run URLs. (Step green ⇒ the
   loop rewrote every `(not reported)` seed, spec §1.) Download the job logs before any re-run.
4. Existing guards green unmodified: `AppHostStackStatusTests`,
   `AppHostExtractionBoundaryTests`, the AppHost.cs text guards (Architecture.Tests).

If neither 4.1 nor a green base-commit e2e run can be established, **block** (`agent:blocked`)
rather than ship; do not add a production seam in this issue — that is a separate owner decision.

## 5. Verification (Phase 5)

- `dotnet build src/AppHost -c Release --no-incremental`: sorted-unique Sonar list before/after;
  only `StackStatusReport.cs(201,49) S107` removed; 0 errors. Stop any running AppHost first.
- `dotnet format --verify-no-changes` on the AppHost project.
- `git diff --stat <C0> -- tests/` empty; `git diff --stat origin/develop -- src/AppHost/AppHost.cs`
  empty.
- §4.1 suite (or §4.2 evidence) quoted verbatim in the PR.
