# Spec 176 — A collapse that fails loudly

**Issue:** #2214 · **Branch:** `test/2214-a-collapse-that-fails-loudly`
**Base:** `55d29fe4` (`origin/develop`) · **Phase:** 1 (Specify) · **Date:** 2026-09-17
**Predecessor:** spec 105 (#729), which closed the **producer** half of the same
property. This slice closes the two places downstream that can still collapse
the pair.
**ADRs:** ADR-0037 (phases and gates), ADR-0052 (xUnit + Shouldly),
ADR-0053 (sentence-style test names), ADR-0054 (hand-written builders),
ADR-0084 (300 LOC/file, 4 params/method — both new tests stay inside),
ADR-0103 (integration tests are Aspire-only — the reason option 2 is priced the
way it is), ADR-0109 (disjoint files; the two test files sit in two contexts),
ADR-0139 / ADR-0144 (two obligations; the architect declares the colour).
**Constitution:** §Testing (two obligations).
**§IV latency:** **N/A.** No production behaviour changes. The only `src/` edit
is a doc comment, and T003 proves the compiled code is byte-identical. The
`event → overlay state` leg is *read about* here, never touched.
**New ADR needed:** **No.** No architectural decision is taken, and none is
implied: the property being pinned — *no dedupe by overlay* — is behaviour the
kiosk was already built against, not a new one.

## Not in scope, stated first because it is the point

**Do not add a dedupe. Nothing is asking for one.** Both gaps are latent: today
the production cache appends and the consumer broadcasts unconditionally, and
that is correct. This slice exists so that *if* a dedupe is added later it
**fails loudly** instead of silently changing what a wall shows. A task that
adds dedupe logic to either layer is out of scope at every phase; if the tests
cannot be made green without one, stop and report.

## Premise check — every claim in #2214, verified by content

The issue is from spec 105's phase 6 and was verified line by line before any
artefact was written.

### Confirmed

- **Production cache, `src/Automation/Infrastructure/Cache/InMemoryRuleCache.cs:63-69`.**
  Exactly as filed. `Upsert` does `GetOrAdd` → `RemoveAll(by rule id)` → `Add` →
  `Sort(by CreatedAt)` inside the lock. Nothing inspects `Action`, so nothing
  can collapse two rules that name one overlay — and lines 63-69 are precisely
  where a guard that did would be written.
- **`InMemoryRuleCacheTests` has zero highlight coverage.** A *case-insensitive*
  grep for `overlay|highlight` across
  `tests/Automation.Infrastructure.Tests/Cache/InMemoryRuleCacheTests.cs`
  returns **no match** (exit 1). The file has **nine** `[Fact]`s, matching the
  issue's count; every one of them uses `RuleBuilder`'s default action, which is
  `RuleAction.SetVariableValue.From("oeeLine1", …)`
  (`tests/Automation.Domain.Tests/Rule/RuleBuilder.cs:24-25`). The one suite
  whose own doc comment says *"This suite is the only thing that fails if the
  shipped cache is wrong"* (`:19-20`) therefore has no highlight in it at all.
- **Spec 105's tests run against a test-side fake.**
  `tests/Automation.Application.Tests/Fakes/InMemoryRuleCache.cs:36` is a
  second, name-identical `InMemoryRuleCache` that reimplements the production one
  line for line, and it is the class `RuleEvaluatorTests` and
  `FabEventIngestedV1HandlerTests` construct
  (`using SmartSentinelEye.Automation.Application.Tests.Fakes;`). Its own doc
  comment warns about exactly this class of divergence (`:16-21`). So a dedupe in
  *production* leaves spec 105's two tests green — the issue's central claim,
  and **C1's written prediction turns it into an observation** rather than an
  argument.
- **Consumer dedupe hook, `src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs:23-26`.**
  Verbatim: the two downstream V1 contracts *"both carry the
  `CausingEventIdentifier` so consumers can dedup against Wolverine outbox
  redelivery."*
- **`OverlayHighlightRequestedV1Handler.cs:41` broadcasts unconditionally.**
  Confirmed: after the no-fab guard (`:34-39`) the handler calls
  `broadcaster.OverlayHighlightedAsync(...)` with no seen-set, no store, no state
  of any kind. Nothing is broken.
- **The pair really is indistinguishable by `(overlay, causing event)`.**
  `FabEventIngestedV1Handler:96-103` publishes one `OverlayHighlightRequestedV1`
  per effect, each with `eventIdentifier` as `CausingEventIdentifier` and its
  **own** `Metadata: new EventMetadata(Guid.CreateVersion7(), …)`. Two highlight
  rules on one overlay therefore share the overlay *and* the causing identifier
  and differ only in `DurationMs` and `Metadata.EventIdentifier`. A consumer
  keyed on `(OverlayIdentifier, CausingEventIdentifier)` collapses them exactly.
- **The kiosk behaviour that pays for it still exists and still asserts what the
  issue describes.** `apps/kiosk-web/src/features/cell/CellPage.test.tsx`,
  *"Scenario 3: overlapping highlights on the same overlay survive until the
  later expiry"* — two `onOverlayHighlightChanged` callbacks on `ovl-x`, the
  second landing 500 ms in with a fresh 1000 ms window, asserting the tile is
  still lit at t=1000 and dark at t=1500. Unchanged, and it discriminates purely
  on duration.
- **The case is reachable.** `OverlayIdentifier`
  (`src/Automation/Domain/Rule/OverlayIdentifier.cs:33`) carries no uniqueness
  constraint; rules are unique by name per fab; the cache buckets by
  `(fab, source, kind)` and appends.

### One correction — a line number, drifted

The issue cites `CellPage.test.tsx:491`. Line 491 is now *"Does not flag a tile
while its overlay is still loading"*; **Scenario 3 is at `:564`**. The citation
was correct when spec 105 wrote it and the file grew underneath it — spec 105's
own `plan.md` and `tasks.md` carry the same stale `:491`.

**Consequence for this slice:** every reference in these artefacts is by **test
name**, not line number. A line number is a citation that rots silently, which
is the same defect class the issue itself is about.

## User story US1 (P1) — a wall that still gets both windows

As the kiosk, when two published rules in my fab both highlight overlay X on one
plant-floor event, I receive **two** `OverlayHighlightRequestedV1` frames — one
per rule — so my later-expiry OR has something to OR. Spec 105 pinned that the
producer *emits* both. This story pins that the two layers between the producer
and me **do not collapse them**, at the two seams where a plausible future
change would.

There is one story because there is one property, observed at two seams. It is
independently shippable: two test files, no production behaviour, no migration,
no contract.

### Acceptance scenarios

**Happy — the shipped cache keeps both rules.**

```gherkin
Given two Active rules in fab "munich" on trigger ("plc", "PlcCycleStart")
  And rule "highlight-a" highlights overlay X for 5000 ms, created at T
  And rule "highlight-b" highlights the same overlay X for 12000 ms, created at T+5min
When the production InMemoryRuleCache is asked for ("munich", "plc", "PlcCycleStart")
Then two compiled rules come back
  And both carry a HighlightOverlay action naming overlay X
  And their durations are 5000 then 12000, in createdAt order
```

**Happy — the consumer broadcasts both frames.**

```gherkin
Given two OverlayHighlightRequestedV1 produced by one plant-floor event
  And both name overlay X, carry fab "munich", and share one CausingEventIdentifier
  And their durations are 5000 ms and 12000 ms
When OverlayHighlightRequestedV1Handler handles each in turn
Then the broadcaster receives two OverlayHighlightedNotification
  And both name overlay X
  And their durations are 5000 then 12000
```

**Conflict — is the happy path.** Two rules competing for one overlay *is* the
scenario. The system's resolution is deliberately "emit both, let the consumer OR
by expiry", mirroring the variable sibling
(`RuleEvaluatorTests.Conflict_two_rules_writing_the_same_variable_emit_both_in_createdAt_order`),
which resolves its conflict the same way — both in `createdAt` order. There is no
losing rule to assert about.

**Bad request — none applies, and the nearest thing must not regress.** These are
in-process seams: a cache lookup and a Wolverine subscriber. There is no caller,
no request body, no validation boundary. The closest existing case is
`A_highlight_with_no_fab_is_not_broadcast` (#1397) — a frame that cannot be
addressed is dropped. **The new consumer test must not weaken it**: it carries a
fab, and that test stays untouched and green.

**Auth — N/A, and that is a property of the seam, not an omission.** Neither seam
is reachable from HTTP. Both sit behind `FabEventIngestedV1`, whose own ingestion
boundary is where scope is checked; no `RequireScope`, no principal and no
`Idempotency-Key` is in scope here.

### Independent end-to-end test procedure

Not a live-stack procedure — see *Locked choices* for why an Aspire boot is
declined. The independent check is the **counterfactual**, and it is the whole
evidence of this slice, because the failure mode the issue names is *a test that
passes against a layer that was never exercised*:

1. Observe both new tests **green** against unmodified `develop`, and quote the
   run. Green is the correct colour: nothing is broken today.
2. Apply **C1** (`plan.md`) — a dedupe in the production cache. Observe the new
   cache test **red**; observe spec 105's two tests and all nine existing cache
   tests **green**. That second half is #2214's claim, converted from assertion
   to observation. Quote. Revert.
3. Apply **C2** (`plan.md`) — a dedupe at the consumer keyed on
   `(OverlayIdentifier, CausingEventIdentifier)`. Observe the new consumer test
   **red** and the three existing consumer tests **green**. Quote. Revert.
4. Confirm `git status` shows no `src/` change except T003's doc comment, and
   that T003's comment-stripped hash of that file is unchanged.

Both mutations are **probes, not predictions of failure** — the framing spec 105
used. What they establish is narrower and sufficient: that the two new assertions
are load-bearing rather than restatements of coverage that already exists.

## Locked choices

xUnit + Shouldly (ADR-0052); the existing `RuleBuilder`,
`FakeLayoutLifecycleBroadcaster` and `RecordingLatencyBudget` fakes (ADR-0054) —
**no new fake, no new builder method**; sentence-style names (ADR-0053).

**No Aspire boot** (the issue's option 2). Reasoning is in `plan.md` under *The
decision*; in one line: the counterfactual requirement would multiply an Aspire
boot by three, ADR-0103 leaves no cheaper integration path, and the machine
already has one Aspire stack running (pid 3312) that a second boot would collide
with.

**No new ADR**, and no production behaviour change.
