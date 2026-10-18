# Spec 413: the stack-status watch loop under five parameters

**Feature Branch**: `refactor/3028-stack-status-report-s107` (cut from `origin/develop` at `00a66722`)

**Created**: 2026-10-18

**Status**: Specified, planned and tasked (phases 1-3). No `[NEEDS CLARIFICATION]` remains; one
assumption (A-1) is a timeboxed spike with a defined fallback, not an open question.

**Input**: Issue [#3028](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3028) (body +
two comments: the owner's 2026-10-18 enrolment note and the lane pick-up). Template: spec 383 (a
nested `private readonly record struct` groups a private helper's parameters; one deconstruction
line, locals keep the old names, the body is textually unchanged). Siblings: 380, 407.

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation,
observed green**. Engineer: **infra-engineer** (AppHost). **Not security-sensitive**: the report's
fixed three-field line shape (the actual secret guarantee, `StackStatusReport.cs:22-30`) is not
touched; Phase 6 runs `/code-review` only.

**Spec number.** 413. `origin/develop` ends at 410; 411 and 412 are claimed by parallel architects.
Re-check `specs/` on `origin/develop` and open PR heads before the PR is opened.

**ADRs.** **ADR-0084** (S107, at most 4 parameters; advisory warning; the 2026-10-16 amendment
exempts minimal-API *route handler* request parameters only, so a private non-route method gets
the refactor, never a suppression), ADR-0049 (`CancellationToken` stays the last parameter),
ADR-0144 (lane; characterisation colour; no new suppression), ADR-0087 (each commit builds on its
own), ADR-0091 (no shortcuts in names), ADR-0103 (Aspire testing builder, no Testcontainers),
ADR-0036 (smallest change).

**No new ADR.** The cure is a refactor, not a suppression. Had it been a suppression, this spec
would block instead.

**Latency budget (§IV):** N/A. `StackStatusReport` is CI/dev boot tooling in the composition
root; it is on no leg of the event-to-overlay path.

## 1. Problem and premise check

Measured 2026-10-18 on `00a66722`: `dotnet build src/AppHost -c Release --no-incremental` (the
AppHost project closure, 0 errors). Under `src/AppHost/` exactly **one** Sonar hit:

```
src\AppHost\StackStatusReport.cs(201,49): warning S107: Method has 6 parameters, which is greater than the 4 authorized.
```

`WatchAndWriteAsync(ResourceNotificationService notifications, string path, HashSet<string>
expectedNames, Dictionary<string, ResourceStatusLine> instances, ILogger logger, CancellationToken
cancellationToken)`: `private static`, **one caller** (the `AfterResourcesCreatedEvent` lambda in
`Attach`, `StackStatusReport.cs:181`, same file). No reflection, no `InternalsVisibleTo` in
`src/AppHost`. `AppHost.cs:624` calls the public `Attach` only and is **not** touched.

**Premise holds for the hit. It does not hold for the issue's covering-test claim**, and the
correction matters for the characterisation:

- `tests/Integration.Tests/AppHostStackStatusTests.cs` does **not** boot the stack. It is
  `[Trait("Category", "FixtureLogic")]`, builds the model only (`CreateAsync`, "starts no
  resources, costs no containers"), and runs in CI's Docker-free "fixture logic tests" step. It
  covers `ExpectedResourceNames` and the `ci.yml` boot line, **not** `WatchAndWriteAsync`. It is
  runnable locally with Docker down.
- `tests/Architecture.Tests/AppHostExtractionBoundaryTests.cs` reads files as text; it covers the
  file set, not this method.
- **No test executes `WatchAndWriteAsync`.** Its only behavioural exerciser is CI's e2e job:
  "Boot the Aspire stack" passes `StackStatusFile=` and "Wait for the stack to be ready" runs
  `scripts/wait-for-e2e-stack.sh`, which reads the file. `Attach` seeds every line at
  `(not reported)`; only the watch loop rewrites them; the gate's `not_started_resources` lets
  through only `Running`, `Finished`/0 and `migrations` `Finished|Exited`. So a green gate step is
  a discriminating observation that the loop ran and wrote every expected resource's state.

## 2. User story

### US1 (P1): the watch loop takes at most four parameters and writes exactly as before

A maintainer reading AppHost's S107 list sees nothing. The status file is seeded, rewritten on
every resource event, folded worst-state-per-name, and closed with `# watch ended: ...` on a fault,
exactly as before.

**Independent test.** Plan §4's characterisation green before and unmodified after; Release build
S107 diff; the e2e job's gate step green on the tip.

#### Acceptance scenarios

```gherkin
Scenario: the hit is gone and nothing else moved
  Given the sorted-unique Sonar warning list of the AppHost Release --no-incremental build on 00a66722
  When the refactor lands
  Then StackStatusReport.cs(201,*) S107 is absent
  And every other line of that list is unchanged
  And the build has 0 errors

Scenario: a watched resource's state reaches the file (happy path)
  Given a status file seeded by Attach with every expected resource at "(not reported)"
  When a resource event reports an expected resource as Running
  Then the file's line for that resource reads "<name>\tRunning\t"

Scenario: an event for a resource outside the expected set is ignored (conflict)
  Given a ParameterResource or other unexpected name
  When it publishes an event
  Then the file gains no line for it

Scenario: no switch, no file (bad request)
  Given Attach is called with a null or whitespace path
  Then no file is written and no watch is started

Scenario: auth
  Not applicable: the AppHost process has no caller identity; the file's three-field shape,
  which keeps secrets out, is not touched.
```

## 3. Out of scope

`AppHost.cs` (27 guards read it as text, #2898); `Attach`'s and `ExpectedResourceNames`'
signatures; the file format, the `# watch ended` line, cancellation handling, `FoldWorstPerName`,
`Severity`, `WriteReport`; the existing `ArgumentNullException.ThrowIfNull(app)` (AppHost is
ADR-0105's stated exception); any new production seam (`internal`, `InternalsVisibleTo`).

## 4. Assumptions

- **A-1 (spike, timeboxed 30 min).** An in-process characterisation can drive the *real* loop
  through public API only: build an empty `DistributedApplicationTestingBuilder.Create()` app with
  one container resource and one parameter, `BuildAsync()` without `StartAsync()`, call
  `StackStatusReport.Attach(app, tempPath)`, publish `AfterResourcesCreatedEvent` by hand through
  `IDistributedApplicationEventing`, then `ResourceNotificationService.PublishUpdateAsync`. Risk:
  `Attach`'s own remarks record (from `AspireFixture.cs:305`) that `WatchAsync` before host start
  "has nothing to watch and completes". If the spike shows that, or that `BuildAsync` needs DCP or
  Docker, **fall back to plan §4.2** (no new test; evidence is the textual-identity proof plus the
  CI e2e gate on base and tip). The fallback is honest because every parameter has a distinct
  type, so a transposed construction is a compile error, and the body is byte-identical.
- **A-2.** S107 counts the `CancellationToken` (proven in spec 407 §1); the target is therefore at
  most four including it.
