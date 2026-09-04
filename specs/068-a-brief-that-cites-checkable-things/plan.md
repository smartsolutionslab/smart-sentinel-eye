# Plan — Spec 068, a brief that cites checkable things

**Phase:** 2 (Plan) · **Spec:** `spec.md` · **Issue:** #2058

## Shape of the change

**No bounded context, no layers, no domain, no messaging, no persistence.** This
is a build-time architecture guard: one test class that reads repository files and
compares two derived registers. The usual plan headings do not apply, and saying
so is better than inventing a context for them.

| Heading | This feature |
|---|---|
| Bounded context | None. `tests/Architecture.Tests`. |
| Entities / value objects | None. Two private records inside the guard. |
| Domain → integration event | None. No runtime code. |
| Boundary rules | Unchanged. Adds no project reference. |
| Persistence | None. |
| Latency leg | N/A — no runtime path. |

## Where it lives, and why

**`tests/Architecture.Tests/AgentBriefClaimTests.cs`** — one file, one class,
self-contained.

- `Architecture.Tests` is where this repository already reads its own files as
  data: `GuardBanWiringTests`, `FoundingDecisionRecordTests`,
  `LogTailCoverageTests`, `PaginatedConsumerTests`. All four duplicate a small
  `RepositoryRoot()` walker rather than sharing a base class. **Mirror that.**
  Introducing a shared helper is a refactor of four existing guards and belongs to
  a different issue.
- **One file, not three.** `PaginatedConsumerTests.cs` is 643 lines and
  `BoundaryTests.cs` 435, both green today, so ADR-0084's 300-LOC limit is not
  enforced on this project. Cohesion wins: the three claim classes share the
  corpus enumeration and the block splitter, and splitting them would force
  exactly the shared-helper file the point above rejects.

### No `FixtureLogic` trait — and this is not the #2013 omission repeating

That trait exists for one purpose: pulling the Docker-free classes **out of
`Integration.Tests`** into the fast `backend` job, because `coverage-check.ps1`
excludes that project by name and CI otherwise ran them only in the 30-minute
`integration` job.

`Architecture.Tests` is not excluded. `scripts/coverage-check.ps1:71-72`
enumerates every `tests/**/*.csproj` and filters out
`SmartSentinelEye.Integration.Tests.csproj` alone, so this guard already runs
unfiltered in the `backend` job's "Unit + architecture tests + coverage gate"
step. **Adding the trait would do nothing here**, and a trait that selects nothing
is a worse defect than the omission it imitates.

## The two registers, both derived

Nothing in this guard is a hand-maintained list of expectations. That is lesson 3
from #1982, and it is what separates this from a guard that proves a document was
written.

### Register A — decisions that exist

```
AdrRegister = { NNNN : docs/adr/NNNN-*.md exists }
            ∪ { NNN  : "| NNN |" is a row of the table in 0000-initial-decisions.md }
```

The union is load-bearing. Files begin at `0028`; decisions `001`–`027` exist only
as 27 rows in the founding document. A file-only register calls `ADR-0007`,
`ADR-0024` and `ADR-0026` — all correct citations, all live in today's briefs —
errors. The first draft of this guard must not be the thing that fails on correct
work.

### Register B — paths that exist

```
Anchors = the entry names of the repository root, enumerated at run time
```

`src`, `apps`, `tests`, `docs`, `scripts`, `deploy`, `e2e`, `specs`, `.github`,
`.specify`, `.claude`, `global.json`, `Directory.Packages.props`, … — enumerated,
never written down. A restructure that renames a top-level directory changes the
recogniser automatically, and a brief still naming the old one goes red, which is
the correct outcome.

## Recognising a claim in prose — the crux

The briefs are Markdown, not structured data. **Under-recognition is the silent
failure**: a claim the guard does not parse is a claim it does not check, and it
looks exactly like compliance. Three recognisers, each with its own failure story.

### 1. ADR citations — high recall, near-zero false positives

Two accepted spellings, `ADR-NNNN` and `adr/NNNN-slug`. Recall is checked rather
than assumed: a second, looser pattern (`ADR` followed by optional separator and
1–4 digits) finds every ADR-shaped token, and any token it finds that the strict
pattern did **not** is reported as unparseable (FR-007). So `ADR-141` and `ADR 141`
fail loudly rather than slipping past the strict pattern into silence.

This is the arm with the best recall and the least ambiguity, and it is also the
arm the maintainer's own errors did not exercise. Cheap and worth having.

### 2. Repository paths — anchoring is both recogniser and false-positive story

A candidate is any inline-code span containing `/`. It is a **claim** only if its
first segment is in `Anchors`. That single rule disposes of every false positive
in today's corpus without an allow-list:

| Span | Verdict | Why |
|---|---|---|
| `.github/workflows/ci.yml` | claim, resolves | anchored, exists |
| `apps/shared/src/api/*.api.ts` | claim, resolves | glob, ≥1 match |
| `docs/adr/*` | claim, resolves | glob, ≥1 match |
| `/cameras`, `/speckit-plan` | not a claim | first segment empty — a route or a slash-command |
| `camera-catalog/cameras` | not a claim | not a top-level entry — an HTTP route |
| `Commands/`, `Events/`, `Handlers/` | not a claim | folder *conventions*, not repo paths |
| `${VITE_API_GATEWAY_URL}/<context>` | not a claim | not a top-level entry |
| `[src/AppHost/**.cs]` | not a claim | begins `[` — an editorconfig section (assumption A3) |
| `src/app/auth.ts` | claim, **fails** | anchored at `src`, does not exist |
| `specs/NNN-x/spec.md` | claim, **fails** | anchored at `specs`, resolves to nothing |

Globs are evaluated as "matches at least one entry" — stricter than skipping, and
it is how a brief legitimately names a *class* of file. The last two rows are the
live defects from spec.md, and both are fixed by writing the glob the prose
already means.

### 3. CI job facts — bound to a named job, which removes polarity

A **CI block** is a Markdown block (bullet item, with continuations, or paragraph)
that names `ci.yml` or `.github/workflows/ci.yml`. Within a CI block that also
contains the word "jobs", the enumerated job names are the inline-code spans
matching `^[a-z][a-z0-9_-]*$`.

Two comparisons, both against `ci.yml` parsed at run time:

- **Set equality** with the `jobs:` keys — `backend`, `frontend`, `integration`,
  `e2e` — in both directions.
- **Per-job attributes.** For each enumerated job, the text between that job's
  span and the next job's span is its attribute clause. Within it,
  `continue-on-error` / "non-blocking" must agree with whether `ci.yml` gives that
  job a `continue-on-error:` key, and a `needs` claim must agree with that job's
  `needs:` list.

**Why not check config keys generally.** Two sentences in the current briefs make
the general form unworkable in opposite directions:

- `infra-reviewer.md:19` — "a `continue-on-error` masking a real failure". A
  hypothetical, in a CI block, naming no job. "Every key named near `ci.yml` must
  appear in `ci.yml`" makes this correct sentence red.
- `infra-engineer.md:12` — "there is no `continue-on-error` anywhere in the file".
  A correct *negative* claim. The inverse rule makes **this** red.

Reading polarity from prose would need a negation vocabulary applied per sentence
— brittle, and wrong the first time someone writes "not without". Binding the
claim to a named job sidesteps it entirely: neither sentence above names a job in
an attribute position, so neither is a claim, and the error that actually occurred
(`integration` … `continue-on-error`) is caught precisely. The narrowing is
recorded in FR-004 and in spec.md's limits section so it is a known bound rather
than a discovered gap.

**Unparseable CI blocks are reported** (FR-007): a block naming `ci.yml`, saying
"jobs", from which no job name parses, fails naming the file and line.

## Assertion inventory

| # | Assertion | Kind | Red on arrival |
|---|---|---|---|
| A1 | Every ADR citation resolves against Register A | Theory, per file | no — needs the demonstration |
| A2 | Every ADR-shaped token parses as a citation | Fact | no |
| A3 | Every anchored path span resolves | Theory, per file | **yes** — 2 findings |
| A4 | Enumerated job set equals `ci.yml`'s jobs | Fact | no |
| A5 | Per-job attribute claims agree with `ci.yml` | Fact | **yes** — 1 finding |
| A6 | Every CI block that enumerates jobs parses | Fact | no |
| A7 | Every `.md` under both directories is scanned (**per file**) | Fact | no |
| A8 | Total recognised claims ≥ floor | Fact | no |
| A9 | The guard's own code names no allow-list mechanism | Theory | no |

**A7 is per item, not aggregate** — lesson 1 from #1982, whose first version
checked that a directory existed and would have passed with the directory
emptied. A7 enumerates both directories and asserts each file appears in the
scanned set, naming the ones that do not. **A8 is its companion**: every file can
be scanned and the claim count still collapse to nothing if a recogniser breaks.

## Failure messages

Every message names the file, the line, the exact text, and what to do — the
register that was consulted, and the fix. Specifically, the ADR message must state
that decisions below 0028 are rows in `0000-initial-decisions.md`, so nobody
"fixes" a correct citation by creating a duplicate file; and the path message must
offer the glob form, because that is the fix for the two live findings.

## Boundary and convention compliance

- No project reference added; NetArchTest boundaries untouched.
- xUnit + Shouldly (ADR-0052); no Moq, no fixtures, no Docker (ADR-0103).
- Sentence-style test names with underscores (ADR-0053).
- `Ensure.That` is for argument guards in product code (ADR-0105) and does not
  apply to a test's private helpers; the precedents use plain assertions.
- Collection expressions with explicit types — `string[] offenders = [...]`
  (`dotnet_style_prefer_collection_expression`, warning, fails Release).
- No leading underscore on private fields.
- Regexes carry `TimeSpan` match timeouts, as all four precedents do.

## Risks

- **R1 — recall is unprovable.** No assertion can show the recognisers see every
  claim; A2, A6 and A8 narrow the gap by making *unparseable* input loud, but a
  claim written in a shape nobody imagined is still invisible. Recorded in
  spec.md's limits, not papered over.
- **R2 — the guard becomes an obstacle.** Mitigated by A3 accepting globs and by
  anchoring, so ordinary prose is not a claim. If it obstructs anyway, it is
  removed by a human with a stated reason, not quietly weakened (FR-005).
- **R3 — brief edits collide with concurrent work.** `.claude/agents/*.md` is not
  on ADR-0109's contention list, but another lane could be editing a brief. The
  three edits are one line each and confined to files this slice must touch.
