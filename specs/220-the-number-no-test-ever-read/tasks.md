# Tasks: The number no test ever read

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Spec
number**: 220 · **Branch**: `2515-the-number-no-test-ever-read` · **Issue**:
#2515

**Phase-4a colour**: characterisation, **observed green** — plus a **mandatory
counterfactual** (T006). See spec §*Phase-4a colour*.

---

## Parallelism, stated once

**There is none worth having, and saying so is the useful answer.** Every task
below writes to or reads the same single file,
`tests/Architecture.Tests/WhepAuthorizeCeilingTests.cs`. ADR-0109 marks `[P]`
for disjoint file ownership; nothing here is disjoint, so **no task carries
`[P]`**. The orchestrator should dispatch this as one sequential slice to one
agent. A fan-out would produce three agents contending for one file — the exact
shape the `[P]` marker exists to prevent.

The two foundational reads (T001, T002) are cheap and block everything, as
usual. There is no `Shared.Kernel`/`Shared.Contracts`/AppHost/Aspire work at
all.

---

## User Story 1 — A halved ceiling fails the build (P1)

*The whole shippable slice. Closes #2515 on its own.*

### `[T001]` `[US1]` Confirm the two premises before writing anything

Two things this whole spec rests on, both cheap, both to be **observed rather
than assumed** — this repository has delivered work against stale premises
before.

1. **The value still matches.** Read
   `src/StreamDistribution/Api/appsettings.json` on the branch tip and confirm
   `"PermitLimit": 2000` and `"Window": "00:01:00"`. Recorded on
   `origin/develop` at `8ce9b5fa` as matching.
   **If it does not match: STOP.** Do not adjust the expected value. That is the
   more urgent finding in spec §*Phase-4a colour* — the shipped ceiling has
   drifted from its derivation and nobody noticed. Report it and park.
2. **`ConfigurationBuilder` is reachable.** Confirm
   `Microsoft.Extensions.Configuration` / `…Configuration.Json` resolve in
   `tests/Architecture.Tests` without a new `PackageReference` —
   `DatabaseCommandLogLevelTests.cs:2` uses them today. If a package reference
   *is* needed, add it to the `.csproj` only (`Directory.Packages.props` is
   centrally versioned; follow the existing pattern rather than pinning a
   version inline) and say so in the PR.

**Depends on**: nothing. **Blocks**: everything.

**Done when**: both confirmed in writing, with the observed values quoted.

---

### `[T002]` `[US1]` Read the three precedents

No code yet. Read, in this order:

- `tests/Architecture.Tests/DatabaseCommandLogLevelTests.cs` — the file-binding
  shape to mirror (`:125-154`), the "a passing guard that checks nothing"
  argument (`:76-81`), and the private `record` for what was read (`:182`).
- `tests/Architecture.Tests/RepositorySource.cs` — `Root()` and
  `RelativePath()`. **Use these.** NFR-003: no 32nd copy of the root walk.
- `tests/Architecture.Tests/ContainerImagePinTests.cs` — the regex-with-timeout
  shape for T007, and the "bans a category, never a value" paragraph that plan
  §2 answers head-on.

**Depends on**: T001. **Blocks**: T003.

**Done when**: the implementer can state which existing helper each piece of the
new file will use.

---

### `[T003]` `[US1]` Create the class with its doc comment

Create `tests/Architecture.Tests/WhepAuthorizeCeilingTests.cs`. Class
`WhepAuthorizeCeilingTests`, namespace `SmartSentinelEye.Architecture.Tests`.

Write the **doc comment first**, and make it carry:

- what is guarded (`WhepAuthorizeRateLimiting:PermitLimit` / `:Window` in the
  one shipped file) and the issue/spec numbers (#2515, spec 220, following spec
  208 / #2284);
- the derivation in one sentence — ≈800 POST/min worst computed minute, 2.5×
  margin — with the citation
  `specs/208-a-ceiling-the-hook-never-had/spec.md` §*Sizing the ceiling*;
- **why this guard pins a value** when `ContainerImagePinTests` and
  `DatabaseCommandLogLevelTests` both say not to — the paragraph drafted in plan
  §2, in the implementer's own words. This is the part a future reader needs and
  the part a reviewer will look for;
- what is **not** governed: `AppHost.cs`'s `isE2ETests` override (50/00:00:10,
  correct as written) and `appsettings.Development.json`;
- that the file is read from disk rather than through the referenced Api
  assembly, and why (`ContainerImagePinTests:31-36`).

Then the constants: the expected `PermitLimit` (2000), the expected `Window`
(`TimeSpan.FromMinutes(1)`), the relative path
`src/StreamDistribution/Api/appsettings.json`, and the two configuration keys —
each key string spelled **exactly** as `Program.cs:22,24` spells it.

**Depends on**: T002. **Blocks**: T004.

**Done when**: the file compiles with no `[Fact]` yet.

---

### `[T004]` `[US1]` Bind the shipped file and assert the pair

Add the reader and the first `[Fact]`.

Reader: `ConfigurationBuilder().AddJsonFile(<absolute path>, optional: false)
.Build()`, then `GetValue<int?>` and `GetValue<TimeSpan?>` with the same key
strings and generic arguments as `Program.cs:21-24` (FR-003). Wrap the read so a
present-but-unparseable value (`"two thousand"`) surfaces as "present but not
readable as a number" rather than a raw binder exception (FR-004, spec AS7).

The `[Fact]`, sentence-style (ADR-0053), e.g.
`The_shipped_whep_authorize_ceiling_is_the_number_spec_208_derived`:

1. `ShouldNotBeNull` on each, with a message distinguishing *absent section*,
   *absent key* and *unreadable value* (FR-004; spec AS4, AS5, AS7);
2. `permitLimit.ShouldBe(2000, Derivation)`;
3. `window.ShouldBe(TimeSpan.FromMinutes(1), Derivation)` — **compared as
   `TimeSpan`, never as a string** (FR-002, spec AS3).

`Derivation` is a message builder carrying observed value, expected value,
≈800/min, 2.5×, and the spec citation (FR-005). Build it so the *observed* value
appears — an assertion message that cannot change when the subject changes is
one this repository has had to correct five times in a week.

House rules: private fields with no leading underscore; `List<T> x = [];` for
any collection; no drive-by error handling beyond the one binder translation
justified in plan §2.

**Depends on**: T003. **Blocks**: T005.

**Done when**: `dotnet test tests/Architecture.Tests --filter
"FullyQualifiedName~WhepAuthorizeCeiling"` is **green**, and the output is
captured verbatim for the PR (this is the characterisation capture — ADR-0144).

---

### `[T005]` `[US1]` Confirm the whole suite is still green

Run the full `tests/Architecture.Tests` suite, not just the filter. Specifically
confirm nothing else began failing — spec AS6: the `AppHost.cs` `isE2ETests`
override must remain legal, and no existing guard should see the new file as
in-scope for its own scan (several guards in this directory scan
`tests/Architecture.Tests/*.cs`, e.g. the `GuardSource` self-scans — a new file
in that tree can enter an existing guard's corpus).

**Depends on**: T004. **Blocks**: T006.

**Done when**: full-suite green, output captured.

---

### `[T006]` `[US1]` **Prove it by counterfactual** — the load-bearing task

Green-on-arrival proves the number matches. It does **not** prove the guard can
see a number that does not. Construct exactly what the issue describes:

```sh
# a legal, non-zero, wrong value
#   src/StreamDistribution/Api/appsettings.json: "PermitLimit": 2000 -> 200
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~WhepAuthorizeCeiling"
```

**Expect red**, with a message naming `200`, `2000`, the ≈800/min derivation and
the spec citation. **Capture that output verbatim** — it is the evidence quoted
in the PR body, and it is what makes this guard worth having.

Then repeat for the window (`"00:01:00"` → `"00:10:00"`, spec AS3) and for the
deleted section (spec AS4). Three counterfactuals, three red outputs.

Restore with `git checkout -- src/StreamDistribution/Api/appsettings.json` and
re-run to confirm green. **If it is still red after the revert**, the restored
file kept its old timestamp and MSBuild skipped the rebuild — touch the file or
build `--no-incremental` before concluding anything.

**`git status` must be clean of `src/` changes when this task ends.** The whole
premise of this spec is that no production file is edited; a counterfactual left
in the tree would ship the very defect it was demonstrating.

**Depends on**: T005. **Blocks**: T007 (and is the gate for shipping US1 alone).

**Done when**: three red outputs captured, tree restored, green re-confirmed,
`git diff src/` empty.

---

## User Story 2 — The code fallback agrees with the shipped file (P2)

*Severable. If dropped, US1 ships whole and #2515 is closed.*

### `[T007]` `[US2]` Assert `Program.cs`'s fallbacks agree with the bound values

A second `[Fact]` on the same class. Read
`src/StreamDistribution/Api/Program.cs` as **text** (top-level statements give
nothing reflectable) and match the two `??` fallbacks, each regex anchored on
its configuration key string and carrying an explicit `TimeSpan` timeout
(`ContainerImagePinTests` precedent):

- `WhepAuthorizeRateLimiting:PermitLimit"\)\s*\?\?\s*(\d+)`
- `WhepAuthorizeRateLimiting:Window"\)\s*\?\?\s*TimeSpan\.From(\w+)\((\d+)\)`

**Assert the match count first** (exactly one each) and fail on zero with a
message saying the scan matched nothing and must be repointed — FR-007, spec US2
AS3. A source scan that silently stops matching is the standard way this kind of
guard rots green.

Then assert each captured value equals the value bound from `appsettings.json`
in T004 — *agreement between the two carriers*, not two independent literals, so
a future legitimate change to the ceiling has one place to fail rather than two
to keep in step.

Add to the class doc comment what a literal scan **cannot** see: a fallback
whose value comes from a `const`, a different overload, or a helper method.

**Depends on**: T006. **Blocks**: T008.

**Done when**: green, output captured.

---

### `[T008]` `[US2]` Counterfactual for the fallback

Change **only** `Program.cs`'s `?? 2000` to `?? 200`, leaving `appsettings.json`
untouched. Expect:

- T004's `[Fact]` **still green** — which is the proof that `Program.cs` is
  genuinely a second, independently-drifting carrier and that US2 is not
  redundant;
- T007's `[Fact]` **red**, naming both carriers and both values.

Then remove the digit-literal fallback without changing its effective value —
`?? 2000` → `?? int.Parse("2000")` — and confirm T007 fails on the match count
rather than passing (spec US2 AS3). **Not `?? 2000` deleted outright**: T004's
line assigns `GetValue<int?>(...) ?? <literal>` to a non-nullable `int`, so
dropping the `??` clause leaves an `int?` assigned to `int` — a build error
(CS0266), not a red test.

Restore, re-run, confirm green, `git diff src/` empty.

**Depends on**: T007. **Blocks**: T009.

**Done when**: both outputs captured, tree restored.

---

## Wrap-up

### `[T009]` Self-review against the spec's requirement table

Walk FR-001 … FR-007 and NFR-001 … NFR-004 and confirm each is satisfiable by
pointing at a line in the new file. Specifically confirm:

- **NFR-003**: no new `RepositoryRoot()` walk — `RepositorySource.Root()` is
  used.
- **NFR-001**: no `AspireFixture`, no Docker, no network; the filtered run takes
  seconds.
- **NFR-002**: `Directory.Packages.props` untouched (or, if T001 found
  otherwise, the one-line csproj change is called out).
- **FR-005**: the failure message teaches the derivation, it does not merely
  state a mismatch.
- **Scope**: `git diff --stat` shows `tests/Architecture.Tests/WhepAuthorizeCeilingTests.cs`
  and the two `specs/220-…` artifacts, and nothing under `src/`.

**Depends on**: T008 (or T006 if US2 is dropped).

**Done when**: each item confirmed or its deviation written down.

---

### `[T010]` Housekeeping — board and commits

- Commits: Conventional Commits, **no `Co-Authored-By: Claude` footer**
  (ADR-0030, ADR-0086 — the session attribution reminder does not override the
  repo's ADR; the PR body still gets the Claude Code line).
- PR base `--base develop`, **never `main`** (ADR-0028), with `--base develop`
  passed explicitly.
- The **feature issue** #2515 is on Project #13 (added at phase 3, see below).
  No per-task issues — the repo stopped creating those after spec 028.

---

## Dependency graph

```
T001 ──> T002 ──> T003 ──> T004 ──> T005 ──> T006 ══ US1 shippable here ══>
                                                  └─> T007 ──> T008 ──> T009 ──> T010
```

No `[P]`. One agent, one file, in order.

---

## Phase-3 gate

- [x] Tasks are atomic and each names its own "done when".
- [x] `[P]` markers considered and deliberately absent, with the reason stated.
- [x] Feature issue #2515 on Project #13 — see the phase-3 report.
