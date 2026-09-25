# Spec 249 — The form three readers assume away

**Issue:** [#2467](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2467)
— *"PreconditionDeclarationTests, RouteValueRefusalDeclarationTests and
StatusProducerDeclarationTests have no masker-assumptions guard either"*. Labels
`tech-debt`, `agent:ready`, no `agent:blocked`. **Already on Project #13**, status
**In Progress** — verified 2026-09-25 by `content.url` against
`gh project item-list 13 --owner smartsolutionslab --limit 2000`. No `item-add`
needed.

**Spec number.** Highest on `origin/develop` is 237; open PR branches hold up to
247 (#2587); an uncommitted sibling worktree holds
`248-the-copies-the-realm-never-sees`. Every local and remote branch and every
`D:/Github/*/specs/` directory was swept on 2026-09-25 after `git fetch`: nothing
claims 249. **Re-check before opening the PR** (tasks T010).

**File contention: none.** No open PR touches
`tests/Architecture.Tests/{PreconditionDeclarationTests,RouteValueRefusalDeclarationTests,StatusProducerDeclarationTests,SourceScanCharacterisationTests}.cs`
(checked against every open PR's file list, 2026-09-25).

**ADRs referenced:**

- **ADR-0139** — rules that fail the build, not the review; constitution
  §Testing as amended. A form that today mis-reads *silently* becomes a named
  build failure in three more guards.
- **ADR-0144** — autonomous lane; §6 declares the phase-4a colour.
- **ADR-0036** — smallest change; no speculative generality; §7 names the
  consolidation this spec deliberately does not do.
- **ADR-0037** — phases and gates.
- **ADR-0109** — `[P]` disjoint-file rule (`tasks.md` §4).
- **ADR-0052 / 0053 / 0054** — xUnit + Shouldly; sentence-style names;
  hand-written fixtures.
- **ADR-0084** — code metrics advisory (300 LOC/file) — `plan.md` §5.

**No new ADR is needed.** This decides nothing. It applies, three more times, the
pattern spec 192 (#2278) established for `EndpointScopeDeclarationTests` and spec
075 established for `ConcurrencyConflictDeclarationTests`, consuming a mechanism
spec 190 (#2257) already built. No rule, pinned count or production assembly
changes.

**Latency budget (§IV): N/A.** The whole diff is under `tests/Architecture.Tests/`.
No production assembly changes; no leg of `event arrival → overlay rendered` is
touched and no leg's measurement status moves.

---

## 1. The issue's claims, re-checked against the tree (2026-09-25)

| Claim | Verified |
|---|---|
| `PreconditionDeclarationTests` masks with `CommentsAndLiteralInteriors` | Yes — `Read()`, `PreconditionDeclarationTests.cs:702` |
| …and walks with `ChainLiteralHandling.AlreadyMasked` | Yes — `Mappings()`, `:776-777`, sentinel `ChainEndSentinel.NotFound` |
| `RouteValueRefusalDeclarationTests` — same shape | Yes — mask `:484`; walks `:554` (`RouteGroups`) and `:589` (`Mappings`), both `NotFound` + `AlreadyMasked` |
| `StatusProducerDeclarationTests` — same shape | Yes — mask `:634`; walks `:679` (`RouteGroups`) and `:714` (`Mappings`), both `NotFound` + `AlreadyMasked` |
| None calls `SourceMask.UnhandledForms` | Yes — repository grep finds exactly three references: the call in `ConcurrencyConflictDeclarationTests.cs:765`, the `BannedForms()` pair in `EndpointScopeDeclarationTests.cs:2045-2046`, and a `<see cref>` at `ConcurrencyConflictDeclarationTests.cs:882`. None in the three guards. |
| The mechanism exists | Yes — `SourceMask.UnhandledForms(MaskStrictness)`, `SourceMask.cs:118-121`. For `CommentsAndLiteralInteriors` it returns `RawStringLiteralOnlyUnhandled`: one row, the three-quote delimiter, *"a raw string literal, whose delimiter is longer than one quote"*. |

All three read the **same corpus** as spec 192's guard: `ApiSourceFiles(root)` —
every `*.cs` under `src/*/Api`, `obj/` and `bin/` excluded — at
`PreconditionDeclarationTests.cs:719`, `RouteValueRefusalDeclarationTests.cs:502`,
`StatusProducerDeclarationTests.cs:649`. Handler resolution
(`Resolve(mapping, classes, masked)`) reads the same masked dictionary, so there
is no second corpus to guard.

Two of the three already **state the assumption in prose and never assert it**:
the class docs at `PreconditionDeclarationTests.cs:159-162` and
`RouteValueRefusalDeclarationTests.cs:170-172` say *"A raw string literal (three
quotes), of which there are none in these directories, would be masked wrongly."*
This spec turns that sentence into an assertion.

**The fix needs no new detection logic**, and none may be written. `SourceMask`
is not taught raw strings (`SourceMask.cs:10-12`: that is a behaviour change with
its own issue).

---

## 2. Why it matters: the hazard in this pipeline

The one-pass `CommentsAndLiteralInteriors` mask walks quotes in pairs, exactly as
spec 192 §2 found for the two-stage mask. `SourceScanCharacterisationTests.cs:194-195`
already pins it: `"""raw "quoted" text"""` masks to `"""    "quoted"     """` —
the content between the first and second interior quotes (`quoted`) is **left as
code**. An unbalanced `(` there reaches `RouteChainReader.AlreadyMaskedStatementEnd`
(`RouteChainReader.cs:99-120`), whose depth never returns to zero, so with
`ChainEndSentinel.NotFound` it returns `-1`, and every caller then takes
`masked[call.Index..]` — **the chain is the rest of the file**.

What each guard does with a swallowed span:

- **Precondition** — `Declares428` / `DeclaresMalformed` are `Contains` / regex
  over the chain: an `If-Match` endpoint that declares no 428 or 400 borrows a
  later mapping's. Silent.
- **RouteValueRefusal** — the chain's `DeclaredStatus` matches include every later
  mapping's `ProducesProblem`: a route whose handler can answer 400 is credited
  with a neighbour's 400. Silent. A `MapGroup` statement span swallowing the file
  can also make `No_route_group_declares_a_400…` fail pointing at the wrong
  thing.
- **StatusProducer** — the challenge declaration (401) and the
  authorized-or-anonymous `Classify(chain, group)` are both read off the chain: an
  unauthorized mapping can be classified by its neighbour's `RequireAuthorization`.
  `The_challenge_declarations_the_walk_finds_are_all_the_ones_there_are` is a
  count backstop that would go red — loudly, but naming a number, not a file.

Same class of failure spec 192 closed: silent, in the passing direction.

### 2.1 The corpus is clean today

```
$ rg '"""' --glob '*/Api/**/*.cs' src/   → (none)      # 2026-09-25, this tree
```

A **gap, not a live defect**. Nothing under `src/` changes.

---

## 3. User stories

### US1 (P1) — the only story

**As** the next engineer to write an endpoint,
**I want** each of the three sibling guards to fail, naming my file and line, if
I use a string form its reader cannot mask,
**so that** they judge what my endpoint declares, not what its neighbour declares.

Independently shippable and observable end to end: plant one raw string in one
endpoint file and watch all three guards (and spec 192's) name it.

One story, not three: the three edits are the same edit, share one verification
procedure and one plant, and none is useful to ship without the others — the
issue exists precisely because 192 shipped one of four. They remain
parallelisable at task level (`tasks.md` §4).

---

## 4. Acceptance scenarios (Gherkin)

```gherkin
Feature: the API corpus stays inside what each CommentsAndLiteralInteriors reader can mask

  Scenario Outline: happy path — the corpus uses only forms the reader handles
    Given src/*/Api contains no raw string literal
    When <guard>.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask runs
    Then it passes
    And it has asserted a non-empty file list and a non-empty ban list first

    Examples:
      | guard                             |
      | PreconditionDeclarationTests      |
      | RouteValueRefusalDeclarationTests |
      | StatusProducerDeclarationTests    |

  Scenario: conflict — an endpoint file gains a raw string literal
    Given an endpoint file under src/*/Api contains a three-quote delimiter
    When the three guards run
    Then each fails
    And each message names the file, the line, the form, and why this reader cannot read it

  Scenario: detection is testable in isolation
    Given two in-memory (file, text) pairs, one containing a three-quote delimiter and one clean
    When each guard's FormsThisReaderCannotMask is given them
    Then exactly one offender is reported, naming the offending file and line together, the form and the reason
    And the clean pair is not reported

  Scenario: bad request — the ban list becomes empty
    Given SourceMask.UnhandledForms(CommentsAndLiteralInteriors) returns no rows
    When a guard's corpus fact runs
    Then it fails, rather than passing against nothing

  Scenario: bad request — the corpus becomes empty
    Given ApiSourceFiles returns no files
    When a guard's corpus fact runs
    Then it fails, rather than passing against nothing

  Scenario: the ban is load-bearing, not decorative
    Given a synthetic two-mapping chain whose first .WithSummary is a raw string
      with an unbalanced bracket between two of its interior quotes
    When it is masked with CommentsAndLiteralInteriors and walked with
      RouteChainReader.StatementEnd(…, NotFound, AlreadyMasked)
    Then the first chain's end is not found (-1)
    And that is recorded as the reason the ban exists

  Scenario: auth — not applicable
    Given this change adds no endpoint and no runtime code
    Then no scope, token or fab check is involved
```

---

## 5. Independent end-to-end test procedure

1. `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests"`
   → green on the changed tree.
2. Plant: in `src/CameraCatalog/Api/CameraEndpoints.cs`, change one
   `.WithSummary("…")` to the three-quote form.
3. Re-run step 1's filter plus `FullyQualifiedName~EndpointScopeDeclarationTests`.
   **Expected:** each of the three new corpus facts fails with
   `src/CameraCatalog/Api/CameraEndpoints.cs:<line> contains """ — a raw string
   literal, whose delimiter is longer than one quote`, and spec 192's fact fails
   with the same line. Other assertions may also go red — record which, verbatim;
   that is the evidence the misreading is real rather than argued.
4. `git checkout -- src/CameraCatalog/Api/CameraEndpoints.cs`; re-run → green.
5. Quote steps 2-4 verbatim in `verification.md` and the PR body.

The observation is not that a test failed but that **each** failure names its own
cause and location.

---

## 6. Phase-4a colour: **RED**

New coverage, not a reshape — §Testing binds it to an observed failure. As in
spec 192 §6, the corpus facts **cannot** be observed red against the real corpus
(§2.1: clean), so they arrive green.

**Red is discharged in two forms, both quoted in the PR:**

1. **Phase 4a (compile red).** Per guard, a detection test
   `A_planted_unmaskable_form_is_reported_with_its_file_and_line` is written
   first, calling that guard's not-yet-existing `FormsThisReaderCannotMask`. The
   test project does not compile and names the three missing members verbatim.
   Weakest form of red; not the only one.
2. **Phase 5 (behavioural red).** §5 steps 2-3: a raw string planted in a real
   endpoint file, each new corpus fact observed failing and naming it.

**Considered and rejected: characterisation-green.** A new assertion with nothing
to catch today is not a refactor; shipping it unwatched is the shape of the
guards this repository has had to disprove. Ambiguity resolves to red.

**One companion test arrives green, declared here.** The §2 hazard test in
`SourceScanCharacterisationTests` characterises *existing* `SourceMask` +
`RouteChainReader` behaviour for this pipeline and must pass unmodified — it is
what makes the three bans load-bearing. If `SourceMask` later learns raw strings,
it goes red **correctly**; its message must say to remove the bans, not edit the
assertion.

---

## 7. Deliberately out of scope

- **Teaching `SourceMask` raw strings** — behaviour change, own issue
  (`SourceMask.cs:10-12`).
- **Consolidating the offender loop.** After this spec the same dozen-line loop
  exists in five guards. Hoisting it into shared code would touch
  `ConcurrencyConflictDeclarationTests` and `EndpointScopeDeclarationTests` — a
  behaviour-preserving refactor with its own characterisation obligation, which
  must not ride in a red slice (ADR-0036, CLAUDE.md phase-4a colours). The
  doctrine at `SourceMask.cs:78-82` also keeps the *assertion* in each guard's own
  file. Follow-up F001.
- **Updating the class-doc prose** at `PreconditionDeclarationTests.cs:159-162`
  and `RouteValueRefusalDeclarationTests.cs:170-172`. They stay true; the
  engineer may append "asserted by …" in the same edit if it reads naturally, but
  must not reword the existing sentence.
