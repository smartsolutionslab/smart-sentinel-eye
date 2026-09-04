# Spec 068 — A brief that cites checkable things

**Issue:** #2058 · **Branch:** `test/2058-a-brief-that-cites-checkable-things`
**Phase:** 1 (Specify) · **Date:** 2026-09-04
**ADRs:** ADR-0144 (autonomous lane; briefs are the whole context), ADR-0037
(phased workflow), ADR-0130 (a record nobody checks drifts), ADR-0052 / ADR-0103
(xUnit + Shouldly, no Docker), ADR-0065 (coverage gate), ADR-0109 (parallel
markers).

## The decision, already made

Issue #2058 lays out four options and declines to choose. The maintainer adopted
**(1) a guard test** and **(3) an ADR obligation**, accepting the residual.

**This spec is (1) only.** (3) is filed as **#2081** and needs an ADR, which
ADR-0144 bars the autonomous lane from writing. Nothing here proposes, amends or
implies an ADR.

## Why

`.claude/agents/*.md` are the instructions subagents work from. Under ADR-0144
they are the **only** context a subagent has: no CLAUDE.md breadth to catch a
contradiction, no human reading over its shoulder, and — in the autonomous lane —
no reviewer outside the same set of briefs. Nothing checks them against the repo
they describe.

Four errors surfaced on 2026-09-03, all found by *using* the briefs, not by
reading them. Two were mechanical (a CI key that does not exist; a capability
described as working that has never been run) and two were semantic (NRT recorded
as disabled eleven days after ADR-0141 enabled it).

This is the same defect ADR-0130 named in the founding decisions and in §IV's leg
table: **a record nobody checks against the thing it describes.** The remedy that
worked there was a consistency guard, and it is the remedy here.

## Scope

**In:** a build-failing guard over `.claude/agents/*.md` **and**
`.claude/commands/*.md`, checking three classes of mechanically decidable claim —
cited ADR numbers, quoted repository paths, and CI job facts.

**Out:** semantic claims (below); the ADR obligation (#2081); any change to brief
*content* beyond what the guard requires in order to read a claim; any new ADR.

### Why commands are in scope

The issue notes `.claude/commands/*.md` have the same property and were never
audited. They are the same file format, consumed the same way, and the sweep is
the same glob. Measured today they carry **14 ADR citation lines and 13
slash-bearing spans**, including one fully repo-rooted path
(`docs/adr/0144-an-autonomous-delivery-lane.md`) and one template
(`specs/NNN-x/spec.md`) that the guard finds unresolvable. Excluding them would
leave a corpus with identical failure modes outside the guard for no saving — the
marginal cost is one directory in the enumeration.

## What the guard provably cannot catch

Stated first, because the honest limit is what makes the rest trustworthy.

- **Semantic claims.** "NRT is disabled", "the publisher has never been run",
  "the two apps do not share a client" — each is a statement about how the system
  behaves or about what has happened, not about a token that does or does not
  exist. **Two of the four known errors are out of reach by construction**, and
  no amount of parsing brings them in. #2081 is the half that addresses those, by
  putting the obligation on the change that knows it is changing the thing.
- **A correct citation attached to a wrong claim.** `ADR-0048` exists, so
  "NRT disabled (ADR-0048)" passes every arm of this guard. The guard checks that
  a citation *resolves*, never that it *supports*.
- **Unanchored prose.** A path whose first segment is not a real top-level entry
  is not recognised as a claim at all (FR-002). That is the deliberate price of
  having no allow-list: `Commands/`, `App.tsx` and `camera-catalog/cameras` are
  not checked, and neither is a genuinely wrong path written the same way.
- **Free-text CI keys.** Narrowed deliberately to job facts — see FR-004.

## User stories

### US-1 (P1) — A wrong brief fails the build

*As a subagent spawned into the autonomous lane, I am given a brief whose
checkable claims have been verified against the repository, so that a citation I
cannot see the source of is one I can still rely on.*

This is the whole slice. It is independently shippable: one guard file, no
product code, observable end to end by running one test project.

**There is no P2.** A second story would be the semantic half, which is #2081.

## Functional requirements

**FR-001 — ADR citations resolve.** Every `ADR-NNNN` token in the corpus names a
decision that exists. The register is **derived from source, in two parts**: the
`NNNN` prefixes of `docs/adr/*.md`, **union** the decision-row numbers parsed from
the table in `docs/adr/0000-initial-decisions.md`.

> The second part is not optional. ADRs **0001–0027 have no file** — they are 27
> rows in the founding document. A file-only check is red on arrival for
> ADR-0007, ADR-0024 and ADR-0026, all three of which are correct citations. A
> guard that fails on correct work gets deleted within a month, taking the
> protection with it.

Recognised spellings: `ADR-NNNN`, and the `adr/NNNN-slug` form used once in
`.claude/commands/next-issue.md`.

**FR-002 — Quoted repository paths exist.** A path claim is an inline-code span
containing `/` whose **first segment is an existing top-level entry of the
repository**. Every such span must resolve: to a file, to a directory, or — if it
carries a glob metacharacter — to **at least one** matching entry.

> The anchor set is enumerated from the repository root, not declared. It is both
> the recogniser and the entire false-positive story (FR-005).

**FR-003 — Enumerated CI jobs match `ci.yml`.** Where a brief block names
`.github/workflows/ci.yml` and enumerates jobs, the set of job names it lists must
**equal** the set of `jobs:` keys in `ci.yml` — in both directions, so a job added
to CI that no brief learned about is as red as a job a brief invents.

**FR-004 — Job attribute claims agree with `ci.yml`.** For each job a brief
enumerates, a claim that it is `continue-on-error` / non-blocking, or that it
`needs` another job, must agree with `ci.yml`. Both directions, per job.

> **Deliberately narrowed to job attributes**, not free-text config keys. The
> worked reason is live: `infra-reviewer.md:19` uses the words "a
> `continue-on-error` masking a real failure" as a *review heuristic* — a
> hypothetical naming no job. Any check of the form "a key named near `ci.yml`
> must appear in `ci.yml`" turns that correct sentence red; any check of the
> inverse form turns `infra-engineer.md:12`'s correct *negative* claim ("there is
> no `continue-on-error` anywhere in the file") red instead. Binding the claim to
> a **named job** removes the polarity problem entirely, and it is the shape the
> known error actually had.

**FR-005 — No allow-list, and none is needed.** The guard ships with no exemption
list, and its own executable lines are scanned for the vocabulary of one
(`allowlist`, `baseline`, `exempt`, `waiver`, `suppress`, `#pragma warning
disable`, …), following `PaginatedConsumerTests`.

The two legitimate cases are expressed structurally rather than excused:

- **A class of file** is named with a glob — `apps/shared/src/api/*.api.ts`,
  `docs/adr/*` — and checked as "matches at least one entry". Stricter than
  silence, not weaker.
- **A file that does not exist yet**, or an illustrative path, is named
  unanchored: the first segment is not a top-level entry, so it is not a claim.
  Prose about hypothetical files is naturally unanchored; prose about real files
  is naturally anchored. A brief that repo-roots a nonexistent path is making a
  false claim, which is the thing being guarded.

A departure from FR-001–FR-004 is a **blocked outcome a human accepts in
writing**, not a line added to this guard. ADR-0144 forbids the lane from reaching
green by weakening a gate.

**FR-006 — The corpus is provably non-empty, per item.** Every `*.md` under
`.claude/agents/` and `.claude/commands/` must appear in the scanned set —
asserted **per file**, naming the ones missing, not as an aggregate count and not
by asserting that the directory exists. A separate floor asserts the total
recognised-claim count is not a rounding error — **one floor per claim class**,
because a single total lets two of the three recognisers die in silence while the
largest one carries the sum over the line on its own.

> Today (measured 2026-09-05, by the guard's own recognisers, with the leading
> slash trimmed before anchoring): 13 brief files; **80 ADR citation
> sites carrying 104 citation claims** across 54 distinct decision numbers; **37
> anchored path spans** (22 distinct spellings, 33 distinct per file); **11 CI job
> claims** — 4 jobs in each of the two enumerating bullets plus the 3 sentences
> describing `e2e` outside an enumeration. **152 claims in total.** Each floor is
> half its class, rounded down.
>
> The earlier figures in this paragraph — "39 distinct ADR numbers over 80
> citation sites, 21 anchored path spans" — counted **citation heads** rather than
> claims (`ADR-0038/0046/0066/…` is one head and seven claims) and used a
> span-distinctness rule the guard does not apply. They are corrected here rather
> than left standing: a record nobody checks against the thing it describes is the
> defect this whole spec exists to fix, and it does not get an exemption for being
> the spec's own paragraph.

**FR-007 — Unrecognised shapes are reported, not skipped.** A token that looks
like a claim but does not parse fails the build naming the file, the line and the
text. Specifically: an ADR-like token that is not a recognised citation spelling,
and a brief block that names `ci.yml` and enumerates jobs but whose enumeration
cannot be read. Silently skipping unparseable input is how a guard goes quiet.

## Acceptance scenarios

The four required categories, mapped honestly. **Auth is N/A** — this is a test
over repository files with no trust boundary, no caller and no scope. The nearest
analogue is FR-005's self-scan, given its own scenario below rather than dressed
up as authentication.

### Happy — a corpus whose claims all resolve

```gherkin
Given every ADR number cited in .claude/agents and .claude/commands names either
      a docs/adr/NNNN-*.md file or a decision row in 0000-initial-decisions.md
  And every anchored path span resolves to an entry or matches at least one
  And every enumerated CI job set equals ci.yml's jobs, with agreeing attributes
 When the architecture test project runs
 Then the guard passes
  And it reports having read all 13 brief files
```

### Conflict — the record disagrees with the repository

```gherkin
Given .claude/agents/infra-reviewer.md describes the `integration` job as
      `continue-on-error`
  And .github/workflows/ci.yml contains no continue-on-error key at all
 When the guard runs
 Then it fails
  And the message names the file, the job, the claimed attribute and the reality
  And it says the job blocks, so a flake there is not cheap
```

```gherkin
Given a brief cites ADR-0199
  And neither docs/adr/0199-*.md nor a decision row 199 exists
 When the guard runs
 Then it fails naming the citing file and line
  And the message states that ADRs below 0028 are rows in 0000-initial-decisions.md,
      so the author checks the right register before adding a file
```

```gherkin
Given a brief quotes `src/app/auth.ts`
  And no such path exists at the repository root
 When the guard runs
 Then it fails naming the span
  And the message offers the glob form as the fix for a per-app path
```

### Bad request — a shape the guard cannot read

```gherkin
Given a brief block names .github/workflows/ci.yml and says "jobs" but lists them
      in a form the enumeration parser does not recognise
 When the guard runs
 Then it fails naming that block, its file and its line
  And the message says the parser must be taught the shape, not that the block is wrong
```

```gherkin
Given a brief writes a citation as "ADR 141" or "ADR-141"
 When the guard runs
 Then it fails naming the token
  And the message states the recognised spellings
```

### No soft edge — the FR-005 analogue of an auth check

```gherkin
Given the guard's own source is scanned, string literals and comments excluded
 When an executable line names allowlist, baseline, exempt, waiver or suppress
 Then the guard fails on itself
  And the message states that a departure is a blocked outcome a human accepts in
      writing (ADR-0144), not a line added here
```

### Coverage does not go quiet

```gherkin
Given a brief file is added under .claude/agents/
   Or the agents directory is restructured so the sweep stops matching
 When the guard runs
 Then it names the files that are present but unscanned
  And it fails rather than passing on a smaller corpus
```

## Independent end-to-end test procedure

No Aspire stack, no Docker, no network. From the repository root:

1. `dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj`
   — expect green, and the corpus arm reporting 13 files.
2. Append `Per ADR-0199, do the thing.` to `.claude/agents/architect.md`. Re-run:
   **red**, naming `architect.md` and the token.
3. Revert. Change `infra-engineer.md`'s CI bullet to name a job `backendd`.
   Re-run: **red**, naming the job-set difference in both directions.
4. Revert. Add `` `src/DoesNotExist/Thing.cs` `` to any brief. Re-run: **red**,
   naming the span.
5. Add `` `Some/Illustrative/Thing.cs` `` (unanchored). Re-run: **green** — the
   documented recall limit, observed rather than asserted.
6. `git status` clean at the end.

## Phase 4a — how red is obtained

**Behaviour-changing → red** (constitution §Testing, ADR-0144). Two of the arms
are red on arrival against real defects; the rest need a demonstration.

**Red on arrival — four live findings, verified 2026-09-04 on `d0faa47`:**

1. **`.claude/agents/infra-reviewer.md:18`** — "`integration` (needs backend,
   `continue-on-error`)". `grep -c continue-on-error .github/workflows/ci.yml`
   returns **0**, and there is no occurrence anywhere under `.github/`. **#2055
   corrected this exact claim in `infra-engineer.md` and left the identical claim
   in `infra-reviewer.md`** — the git log for `infra-reviewer.md` does not contain
   commit `09688a9`. This is the issue's own error class, still live, in the brief
   belonging to the reviewer whose job is to catch it. It is also the single best
   argument for the guard: the fix was applied to one of two files carrying the
   same sentence, and nothing noticed.
2. **`.claude/agents/frontend-engineer.md:11`** — `` `src/app/auth.ts` ``. The
   real paths are `apps/kiosk-web/src/app/auth.ts` and
   `apps/management-web/src/app/auth.ts`.
3. **`.claude/commands/next-issue.md:67`** — `` `specs/NNN-x/spec.md` ``, a
   template spelling that resolves to nothing.
4. **`.claude/agents/architect.md:11`** — `` `specs/NNN-x/` ``, the same
   placeholder in the brief that *writes* the specs. Recorded here because this
   paragraph said "three" while the run that produced the red named four and the
   fix commit is titled "the four claims" — a count in the artefact a later reader
   opens first, disagreeing with the run it describes, is this spec's own subject.

Findings 2, 3 and 4 are fixed by making the spelling a glob
(`apps/*/src/app/auth.ts`, `specs/*/spec.md`) — a brief edit the guard requires in
order to read the claim, and nothing more. Finding 1 is fixed by correcting the
false claim.

**Demonstrated red — the ADR arm.** FR-001 is green on arrival, so its red is
obtained as #1982 did: a **temporary, uncommitted** reversion. Precisely:

- Edit `.claude/agents/backend-engineer.md`, changing one existing `ADR-0105`
  citation to `ADR-0199`. Run the project. Capture the **verbatim** failure.
- `git checkout -- .claude/agents/backend-engineer.md`, and confirm `git status`
  is clean before committing anything.

**The reversion must not be committed.** Reverting a brief is safer than #1982's
case — no product code is involved and the file is not executed — but the rule
stands unchanged: committing the demonstration re-ships the defect, and a defect
re-shipped to prove a test works is still a defect on `develop`.

The captured output is quoted in the PR body. It is the only form of this evidence
a later reader can check.

## Latency budget

**N/A.** A build-time architecture test touching no runtime path, no leg of the
event→overlay budget, and no product assembly. It runs in the `backend` CI job,
which is minutes, not the 30-minute Docker job.

## Non-functional

- Runs with **no Docker and no network** (ADR-0103) — file reads only.
- Adds no product code, so the ADR-0065 coverage gate is unaffected.
- Every regex carries a match timeout, as the precedents do.
- Paths reported with `/` throughout: `Path.GetRelativePath` returns the platform
  separator, and a backslash literal is green on Windows and red on Linux CI.

## Assumptions, marked

- **A1.** The founding-document register is the 27 decision rows matching the
  table's `| NNN |` shape. Verified: 27 rows, 001–027; files resume at 0028.
- **A2.** No brief legitimately needs to name a repo-rooted path that does not
  exist. Verified against the corpus as it stood on `d0faa47`: **34 anchored spans
  by the guard's own recogniser**, 3 unresolvable, all three genuine defects, all
  three since corrected. The "21" this assumption used to quote was a
  distinct-spelling count, which is not the unit assertion 3 reports; with the
  leading slash trimmed the recogniser now reads **37** spans, all resolving. If a
  case appears that is genuinely legitimate, it is a blocked outcome for a human
  under FR-005, not an allow-list entry.
- **A3.** The `[src/AppHost/**.cs]` editorconfig section header in
  `infra-engineer.md` is not recognised as a path claim, because the span begins
  `[`. Accepted as a recall limit rather than special-cased.
