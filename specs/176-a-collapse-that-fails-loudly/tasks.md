# Spec 176 — Tasks

**Phase:** 3 (Tasks) · **Date:** 2026-09-17 · **Issue:** #2214

## Declarations

**Engineer:** **`backend-engineer`, alone — no `test-writer` / engineer split.**
ADR-0144's phase-4 split exists so an engineer cannot edit a test to make *its
implementation* pass. **There is no implementation here.** 4b is empty: the only
`src/` edit is a doc comment that cannot change any test's outcome, and T003
proves it by hash. Meanwhile the evidence this slice is made of — the
counterfactual — requires mutating `src/`, running, predicting, observing and
reverting, and its entire value is the **prediction-beside-observation pairing**.
Split across an agent boundary, the agent that writes the prediction is not the
one that reads the result, which is the "summary of a summary" failure ADR-0144
names as its own reason for existing. Spec 105 — same shape, same seam, the
template #2214 points at — declared `backend-engineer` alone for the same reason.

**Option chosen:** **1 + option 2's consumer coverage as a unit test + one
written qualification where option 3 would go.** The Aspire integration test is
declined. Full reasoning in `plan.md` § *The decision*; the short form is that
the counterfactual requirement would multiply an Aspire boot by three, ADR-0103
leaves no cheaper integration path, a red integration run has too many causes to
carry a one-bit signal, and the machine's single Aspire stack (pid 3312) is
already running.

**Phase 4a colour:** **characterisation — observed GREEN against current
production code, RED only under the injected counterfactual.** Stated per test
in `plan.md`'s table. This is *not* the red-then-green sequencing of a bug fix.
**A red first run is a finding, not progress** — it would mean production
already collapses the pair. Report it and stop.

**New ADR needed:** **No.** No design decision is taken. The property being
pinned is existing behaviour the kiosk was already built against.

**Files phase 4 may touch — exhaustive:**

| File | Permitted change |
|---|---|
| `tests/Automation.Infrastructure.Tests/Cache/InMemoryRuleCacheTests.cs` | add one helper + one `[Fact]` |
| `tests/LayoutComposition.Application.Tests/EventHandlers/OverlayHighlightRequestedV1HandlerTests.cs` | add one `[Fact]` |
| `src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs` | **doc comment only**, proved by hash |
| `specs/176-a-collapse-that-fails-loudly/*` | evidence appended |

**Temporarily, then reverted before any commit** — C1 in
`src/Automation/Infrastructure/Cache/InMemoryRuleCache.cs`, C2 in
`src/LayoutComposition/Application/EventHandlers/OverlayHighlightRequestedV1Handler.cs`.
**Neither may appear in a commit.**

**Anything else — stop and report.** In particular: **do not add a dedupe.**
Nothing is asking for one. If a test cannot be made green without production
logic, that is a finding about the premise, not a licence to write the logic.

## User story US1 — a wall that still gets both windows

`[P]` markers are real: T001, T002 and T003 own three files in two bounded
contexts with no shared state (ADR-0109). They are marked so an orchestrator
*could* fan them out; one `backend-engineer` doing all three in order is equally
correct and is the expected shape at this size.

- [ ] **T001 [P] [US1]** Add
  `Two_rules_highlighting_the_same_overlay_both_stay_in_the_bucket` to
  `tests/Automation.Infrastructure.Tests/Cache/InMemoryRuleCacheTests.cs`,
  after `A_bucket_is_ordered_by_CreatedAt_so_the_last_write_wins` — it is the
  highlight sibling of that ordering test and reads as one thought with it.

  The file's existing `ActiveRule` helper already takes **five** parameters, and
  ADR-0084 caps a method at four, so **add a separate helper** rather than a
  sixth parameter:

  ```csharp
  private static RuleAggregate HighlightRule(
      string name, Guid overlay, int durationMs, int minutesLate = 0) =>
      // same RuleBuilder shape as ActiveRule, fab fixed to "munich", plus
      // .WithAction(RuleAction.HighlightOverlay.From(overlay, durationMs))
  ```

  It must `Build()` **and** `Publish(builder.Clock)` exactly as `ActiveRule`
  does — a Draft never enters the cache
  (`A_rule_that_is_not_Active_never_enters_the_cache`), so an unpublished rule
  would make the test vacuously pass with an empty bucket.

  The test: one `Guid overlay = Guid.CreateVersion7();` shared by both rules;
  `HighlightRule("highlight-a", overlay, 5_000)` and
  `HighlightRule("highlight-b", overlay, 12_000, minutesLate: 5)` — **distinct
  names**, because `Upsert` removes by rule id and two rules must be two rules.
  Both durations sit inside `HighlightDuration`'s 500–60000 range
  (`src/Automation/Domain/Rule/HighlightDuration.cs:21-22`).

  Assert on what `LookupActive(FabIdentifier.From("munich"), "plc",
  "PlcCycleStart")` returned, not on what was upserted: `Count.ShouldBe(2)`;
  index like the sibling does, `bucket[0].Action.ShouldBeOfType<RuleAction.HighlightOverlay>()`
  with `.Overlay.Value.ShouldBe(overlay)` and `.Duration.Value.ShouldBe(5_000)`,
  and `bucket[1]` the same with `12_000`. **Order is asserted, not incidental** —
  the cache sorts by `CreatedAt` because `RuleEvaluator` emits in that order
  (FR-012).

  Carry a short comment in the file's existing register: the two windows differ
  deliberately so the kiosk's later-expiry OR
  (`CellPage.test.tsx`, *"Scenario 3: overlapping highlights on the same overlay
  survive until the later expiry"*) has something to discriminate; the cache
  keeps both rather than picking (#2214). **Name the test, not a line number** —
  the issue's own `:491` citation had already drifted to `:564` by the time this
  spec checked it.

- [ ] **T002 [P] [US1]** Add
  `Two_highlights_of_one_event_on_one_overlay_are_both_broadcast` to
  `tests/LayoutComposition.Application.Tests/EventHandlers/OverlayHighlightRequestedV1HandlerTests.cs`,
  after `Calls_broadcaster_OverlayHighlightedAsync_with_the_overlay_and_duration`.

  **One** `FakeLayoutLifecycleBroadcaster`, **one** handler instance, built the
  way the three neighbours build theirs (`new RecordingLatencyBudget()`,
  `NullLogger<OverlayHighlightRequestedV1Handler>.Instance`). One instance, not
  two: it reddens under a process-wide dedupe *and* under an instance-scoped one,
  which is the broader net (`plan.md` § C2).

  Two `await handler.Handle(...)` calls with messages that are indistinguishable
  the way production makes them indistinguishable: the **same** `overlay`, the
  **same** `Guid causingEvent`, the same `Moment`, and durations `5_000` then
  `12_000`.

  **Do not reuse `MetadataFor` / `TestMetadata` for both messages.** That helper
  hard-codes `EventIdentifier` `00000000-0000-0000-0000-0000000000aa`, so two
  calls yield metadata that is equal — whereas in production the two frames each
  carry a **fresh** `Guid.CreateVersion7()` (`FabEventIngestedV1Handler:92`,
  `:101`). Build the two metadata inline —
  `new EventMetadata(Guid.CreateVersion7(), <occurredAt>, "munich", null)` — so
  the arrangement reproduces the one field that genuinely distinguishes the pair.
  A test that made them identical would be asserting against a message production
  never sends.

  Assert `broadcaster.Highlighted.Count.ShouldBe(2)`, both
  `.Overlay.ShouldBe(overlay)`, and the durations in order `[5_000, 12_000]`.
  **Do not** use `ShouldHaveSingleItem` — the three neighbours do, and that is
  precisely the assertion shape this test exists to complement.

  Carry a comment saying what is pinned: these two frames share their overlay
  **and** their `CausingEventIdentifier` by design, so a consumer keyed on that
  pair would collapse them; the correct redelivery key is
  `Metadata.EventIdentifier` (#2214).

  **`A_highlight_with_no_fab_is_not_broadcast` is not touched and must stay
  green** — the new test carries a fab.

- [ ] **T003 [P] [US1]** Qualify the dedupe invitation in
  `src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs`. Add
  one `<para>` immediately after the existing paragraph at `:22-27` (the one
  that says consumers can dedup against Wolverine outbox redelivery), saying:
  the key is the contract's own `Metadata.EventIdentifier`, a fresh
  `Guid.CreateVersion7()` per published effect (`:92`, `:101`); it is **not**
  `(OverlayIdentifier, CausingEventIdentifier)`, because two rules on one overlay
  produce two highlights from one event that share both of those and differ only
  in duration — collapsing them means the kiosk never receives the second window
  to OR (#2214).

  This is the one place a written acceptance belongs, because it *is* the
  paragraph a future dedupe author reads. Keep it to the why; no test names, no
  restatement of what the code does.

  **Characterise it as comment-only rather than asserting it.** Two independent
  checks, because "I only touched a comment" is the claim, not the proof.

  *Exact* — every changed line must be a doc-comment line, so this prints
  nothing:

  ```sh
  git diff -U0 -- src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs \
    | grep -E '^[+-]' | grep -vE '^(\+\+\+|---)' | grep -vE '^[+-][[:space:]]*///'
  ```

  *Corroborating* — strip doc comments, squeeze whitespace, hash; before and
  after must match:

  ```sh
  sed -E 's:^[[:space:]]*///.*$::' \
    src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs \
    | tr -d '[:space:]' | sha256sum
  ```

  **Prove the hash is not vacuous before trusting it** (it is a `sed`, not a
  compiler): change one non-comment character in the file, re-run, confirm the
  hash **differs**, then undo. A check that cannot fail proves nothing — the
  repository has five of those on record from one week.

  Quote the empty diff-filter output, both hashes, and the counterfactual hash in
  the PR body. If either check disagrees, something other than a comment moved:
  revert and report.

- [ ] **T004 [US1]** Evidence. Depends on T001, T002, T003.

  1. Run `dotnet test` for `Automation.Infrastructure.Tests` and
     `LayoutComposition.Application.Tests`, plus `Automation.Application.Tests`
     (it holds spec 105's two tests, which C1's prediction is about).
     **Observe green** and quote verbatim output. A red run here is a finding —
     stop and report, do not proceed to the counterfactual.
  2. Apply **C1** (`plan.md`) to `InMemoryRuleCache.cs`. Re-run all three
     projects. Expect: T001's test **red**; the other nine cache tests **green**;
     **spec 105's two tests green** — that last is #2214's central claim and the
     reason the counterfactual is worth running at all. Quote. **Revert** and
     confirm with `git diff --stat src/`.
  3. Apply **C2** (`plan.md`) to `OverlayHighlightRequestedV1Handler.cs`. Re-run.
     Expect: T002's test **red**; the three existing tests in that file
     **green**; T001's test **green**. Quote. **Revert** and confirm.
  4. State in the report, in these words, that the integration suites were **not
     run** under either counterfactual and that their staying green is
     **reasoning, not observation** (they need the Aspire boot this plan
     declines, and pid 3312 holds the stack).
  5. `git status` must show zero `src/` changes beyond T003's comment.

  **If a prediction misses, record the actual result beside the prediction and
  say the prediction was wrong.** Do not retro-fit the prediction, and do not
  adjust a test to restore one.

## Dependencies

```
T001 [P] ─┐
T002 [P] ─┼─→ T004
T003 [P] ─┘
```

No foundational task. Nothing in `Shared.Kernel`, `Shared.Contracts`, `AppHost`
or any Aspire resource is touched, so there is nothing for an orchestrator to
fan out behind — the three `[P]` tasks are the whole parallel surface.

## Deliberately not covered

**The bus itself.** Nothing here asserts that Wolverine and the Postgres outbox
deliver two `OverlayHighlightRequestedV1` as two rather than one. That is
transport behaviour, ADR-0088's territory, and #2214 names neither it nor a
dedupe hook there. Recorded so a later reader meets this boundary rather than
discovers it — and so that if the bus ever *is* suspected, no one mistakes this
spec for having cleared it.

## Gate (phase 3)

Tasks atomic; **#2214 is already on Project #13, status Todo** (confirmed by
`gh issue view 2214`: `projects: Smart Sentinel Eye (Todo)`), so no
`gh project item-add` is required. Feature-level issue only —
`/speckit-taskstoissues` is **not** run (CLAUDE.md: per-task issues stopped
after spec 028).
