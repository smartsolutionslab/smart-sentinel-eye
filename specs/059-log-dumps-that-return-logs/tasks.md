# Tasks: A log dump returns logs

**Spec**: `specs/059-log-dumps-that-return-logs/spec.md`
**Plan**: `specs/059-log-dumps-that-return-logs/plan.md`
**Issue**: #2053
**Branch**: `fix/2053-log-dumps-that-return-logs`

## Phase-3 declarations (ADR-0144)

| Declaration | Value |
|---|---|
| Engineer for phase 4b / 5 | **`infra-engineer`** (reviewer: `infra-reviewer`) |
| New ADR required? | **No** — see `plan.md`, "No new ADR is required" |
| Change colour (phase 4a) | **Behaviour-changing → red first** |
| Latency leg (§IV) | **N/A** — test harness only, no production path |
| Security review needed? | **No** — no trust boundary, no auth, no secret |

**Why behaviour-changing.** The scope includes the guard (US-2), and the guard
is new behaviour: it does not exist, it fails today, and it passes after the
fix. Had the scope been the four strings alone, the honest colour would have
been behaviour-preserving with nothing to observe red — the scope choice drove
the colour, not the other way round. ADR-0144's tie-break (*"ambiguity resolves
to behaviour-changing"*) points the same way.

**This is not a refactor-plus-fix pair.** ADR-0144 forbids mixing the two.
Nothing here changes shape while preserving behaviour; both tasks move in the
same direction — T001 states the rule, T002 satisfies it.

## Dependencies

```
T001 (guard, RED)  ──▶  T002 (four names, GREEN)  ──▶  T003 (verify)
```

Strictly sequential. **Nothing is `[P]`** — the change is two files and the
second exists to make the first pass. Marking either parallel would be false.

The usual foundational blockers (Shared.Kernel, Shared.Contracts, AppHost,
Aspire resources) are **not touched**, so this feature does not gate any other
branch. It touches no ADR-0109 contention file.

---

## User Story US-2 — the mismatch cannot silently come back

### [T001] [US-2] Write the log-tail coverage guard and observe it red

**Agent:** `test-writer` (phase 4a). **May not touch `AspireFixture.cs`.**

**File:** `tests/Architecture.Tests/LogTailCoverageTests.cs` (new)

Add one guard class, following the shape and the XML-doc convention of
`HandlerDeconstructionTests` / `GuardBanWiringTests`: a `<summary>` that says
what is guarded, **why a test rather than a compile error**, and the concrete
incident that motivated it (issue #2053 — seven call sites returning a
placeholder where a stack trace was needed).

Requirements, from `plan.md` "Guard mechanics":

- Locate the repo root by walking up to `SmartSentinelEye.slnx`.
- Scan `tests/Integration.Tests/**/*.cs`, excluding `obj/` and `bin/`.
- Collect every `RecentLogs(` call whose first argument is a string literal;
  skip non-literal arguments (AS-5).
- Parse `TailedResources` from `tests/Integration.Tests/Fixtures/AspireFixture.cs`.
- Fail if the parsed set is empty, or if zero call sites were found (AS-4).
- Fail listing each violation as `<path>:<line> asks for '<resource>'`, with `/`
  separators, ending by naming `AspireFixture.TailedResources`.
- Sentence-style underscore test name (ADR-0053), Shouldly assertions
  (ADR-0052).

**Done when:** `dotnet test tests/Architecture.Tests --filter LogTailCoverage`
**fails**, and the failure names all four resources — `overlay-designer`,
`layout-composition`, `stream-distribution`, `system-variables` — across all
seven call sites. **Capture that output verbatim**; it is the transported
artifact for phase 4b and the quote for the PR body (ADR-0139, ADR-0144).

**A guard that arrives green is a phase-4a failure**, not a shortcut.

---

## User Story US-1 — an engineer reading a CI failure gets the service's logs

### [T002] [US-1] Tail the four missing resources

**Agent:** `infra-engineer` (phase 4b). **May not edit `LogTailCoverageTests.cs`.**

**File:** `tests/Integration.Tests/Fixtures/AspireFixture.cs`

Add `"stream-distribution"`, `"overlay-designer"`, `"layout-composition"` and
`"system-variables"` to `TailedResources` (`:30`). All four are declared in
`src/AppHost/AppHost.cs` (lines 266, 302, 336, 344).

Extend the array's existing `<summary>` with one sentence recording **why the
list is what it is** — a resource earns a tail by having a `RecentLogs` call
site, and `LogTailCoverageTests` enforces that. The current doc explains why
tailing exists but not why these names; without that sentence the next reader
faces the same "is this complete?" question that produced #2053.

Line length after the addition exceeds what one line comfortably holds — format
as a multi-line collection expression. It remains a collection expression, so
the `dotnet_style_prefer_collection_expression` rule (warning, fails Release) is
satisfied.

**Done when:** T001's captured failure now passes, the test file unmodified, and
`dotnet build -c Release` is clean.

---

### [T003] [US-1] [US-2] Observe the dump end to end

**Agent:** `infra-engineer` or orchestrator (phase 5).

Execute `spec.md` "Independent end-to-end test procedure" steps 3 and 4:

- Force one `StreamFabScopingIntegrationTests` assertion to fail and confirm the
  message carries `stream-distribution` log lines rather than the placeholder.
  Revert the forced failure. **This is the only step that proves the feature**
  — T001 and T002 together prove the array and the call sites agree, which is
  not the same as proving a tail delivers.
- Record fixture `InitializeAsync` duration on `develop` and on the branch,
  second run of each (a first run after machine churn reads as a regression).
  Write both figures into the verification note whatever they say.

**Done when:** the verification note records the observed dump and both timing
figures.

---

## Phase-3 gate

- [x] Tasks atomic and ordered
- [x] Every task names its file and its done condition
- [x] Colour declared for phase 4a, with the reason it follows from the scope
- [x] Engineer and reviewer declared
- [ ] Issue #2053 on Project #13 — **already there**, status In Progress
  (verified 2026-09-03 via `gh issue view 2053`); no per-task issues, per the
  convention in force since spec 028
