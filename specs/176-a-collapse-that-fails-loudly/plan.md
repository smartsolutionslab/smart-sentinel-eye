# Spec 176 — Plan

**Phase:** 2 (Plan) · **Date:** 2026-09-17 · **Issue:** #2214

## The decision — which of the issue's three options, and why

The issue offers three and says *"whichever is judged the right seam"*. The
judgement here is **option 1, plus option 2's consumer coverage relocated to a
unit test, plus a one-paragraph written qualification where option 3 would have
gone.** Option 2 as filed — an Aspire integration test — is **declined**, with
reasons specific to this repository rather than a general preference.

### Option 1 — taken, unchanged

A production-cache test with a highlight action in it. It closes the issue's
first location at the exact level named, and it repairs the odder fact the issue
found: the suite whose own doc comment claims to be *"the only thing that fails
if the shipped cache is wrong"* has never exercised a highlight. It is a unit
test in an existing file with an existing builder.

### Option 2 — consumer coverage taken, the Aspire boot declined

The issue's stated benefit of option 2 is that it *"covers both directions at
once"*. That benefit is real, and it is obtainable without the boot, because the
consumer seam the issue actually names is a **handler with three injected
collaborators and no state**:

```csharp
public sealed class OverlayHighlightRequestedV1Handler(
    ILayoutLifecycleBroadcaster broadcaster,
    ILatencyBudget latency,
    ILogger<OverlayHighlightRequestedV1Handler> logger)
```

`tests/LayoutComposition.Application.Tests/EventHandlers/OverlayHighlightRequestedV1HandlerTests.cs`
already constructs it three times over `FakeLayoutLifecycleBroadcaster` and
`RecordingLatencyBudget`. Two `Handle` calls on one instance, asserting two
recorded notifications, pins *"a consumer keyed on
`(OverlayIdentifier, CausingEventIdentifier)` collapses this pair"* directly —
the same claim, at the same seam, deterministically, in milliseconds.

**Why the Aspire boot is not worth its cost here, concretely:**

- **The counterfactual multiplies it by three.** The issue's non-negotiable
  requirement is that the test be observed red under an injected dedupe. For an
  integration test that means boot → green, mutate → boot → red, revert → boot →
  green. ADR-0103 forbids Testcontainers and makes `AspireFixture` the only
  integration path, so there is no cheaper variant to fall back on; CLAUDE.md
  prices the Docker-integration and full-stack jobs at twenty-plus minutes.
- **An integration test under counterfactual is the wrong instrument for a
  one-bit signal.** A red integration run has many causes — SignalR connect
  timing, rule seeding order, cache warm-up, machine churn. The repository has
  already learned that *"the first run after machine churn looks exactly like a
  regression; run it twice."* The evidence this issue needs is *"red because of
  the dedupe and nothing else"*, and an instrument with that many failure modes
  cannot carry it.
- **The machine has one Aspire stack, and it is running (pid 3312).** One
  machine, one stack: a second boot produces `FailedToStart` that reads exactly
  like a code defect. Delivering this issue would mean either contending with
  that stack or stopping it, and the brief says not to stop it.
- **It would not be more specific.** The two unit tests between them touch both
  named locations — the shipped cache at one end, the consumer handler at the
  other. What the integration test would add over them is the **bus itself**,
  which #2214 does not name and where no dedupe hook is documented.

**The residue is named rather than left implicit:** nothing here asserts that
Wolverine/the outbox delivers two `OverlayHighlightRequestedV1` as two. That is
transport behaviour, not a documented dedupe seam, and it is out of this slice's
scope. It is recorded in `tasks.md` under *Deliberately not covered* so a later
reader meets it rather than discovers it.

### Option 3 — not used as a substitute for either test, used once where it is the only instrument

A written acceptance instead of a test is declined for both named locations:
both are cheaply testable, and the issue is explicit that the value is *failing
loudly*, which prose cannot do.

But there is one thing a test genuinely cannot say, and it is the thing a future
dedupe author most needs: **which key a correct redelivery dedupe uses.**
`FabEventIngestedV1Handler:22-27` currently invites a dedupe without qualifying
it, and that paragraph is, by construction, the text read by the person about to
write one. T003 adds one paragraph beside it saying the key is
`Metadata.EventIdentifier` — unique per published event, `Guid.CreateVersion7()`
per effect at `:92` and `:101` — and **not** `(OverlayIdentifier,
CausingEventIdentifier)`, because two rules on one overlay share both of those
by design.

That is where the written acceptance goes: **in the paragraph that invites the
mistake**, not in an unrelated file, and citing `#2214` in the repository's
existing house style (`#1252`, `#1397`, `#2151` all appear as comment citations
in these same files). It is a comment-only edit, and T003 proves that by hashing
the comment-stripped source.

## Bounded contexts and layers

Two contexts, one layer each, **tests only** — plus one doc comment.

| File | Context · layer | What it pins | New? |
|---|---|---|---|
| `tests/Automation.Infrastructure.Tests/Cache/InMemoryRuleCacheTests.cs` | Automation · Infrastructure tests | the **shipped** cache keeps two rules that name one overlay | +1 test, +1 helper |
| `tests/LayoutComposition.Application.Tests/EventHandlers/OverlayHighlightRequestedV1HandlerTests.cs` | LayoutComposition · Application tests | the consumer broadcasts both frames of an indistinguishable pair | +1 test |
| `src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs` | Automation · Application | doc comment only — names the correct dedupe key | +1 `<para>` |

No Domain change, no Infrastructure behaviour, no Api, no contract, no
migration, no Aspire resource, no `Shared.Kernel` or `Shared.Contracts` edit.
There is **no foundational task**: nothing blocks anything else, so there is
nothing for an orchestrator to fan out behind.

## Entities, value objects, invariants

Nothing new, and the invariant being documented is the **absence** of one:
`OverlayIdentifier` (`src/Automation/Domain/Rule/OverlayIdentifier.cs:33`,
`readonly record struct … : IStronglyTypedId<Guid>`) carries no uniqueness
constraint, and none is added. `HighlightDuration` bounds durations to
500–60000 ms (`HighlightDuration.cs:21-22`); both test durations (5000, 12000)
sit inside it, and they differ **on purpose** — equal durations would leave the
kiosk's later-expiry OR nothing to discriminate and would let a mutation that
emits one effect twice pass unnoticed (spec 105's reasoning, reused).

`CompiledRule.Action` is public (`CompiledRule.cs:32`), so the cache test can
assert on the action the bucket handed back rather than on the rule it was given
— the assertion reads the cache's output, not its input.

## Messaging — domain to integration event, unchanged

`RuleAction.HighlightOverlay` → `RuleActionEffect.HighlightOverlay` →
`OverlayHighlightRequestedV1` → `OverlayHighlightedNotification` → the
`/hubs/layouts` SignalR frame the kiosk ORs. One per effect, each with its own
`Metadata.EventIdentifier` and a **shared** `CausingEventIdentifier`. Spec 105
pinned the first two arrows. This slice pins the store in front of arrow one
(the cache) and arrow three (the consumer).

## Boundary rules

No cross-context project reference is added. `Automation.Infrastructure.Tests`
already references `Automation.Domain.Tests` for `RuleBuilder` and
`Automation.Application` for `CompiledRule`;
`LayoutComposition.Application.Tests` already references its own fakes and
`Shared.Contracts`. NetArchTest is unaffected. Neither file is on ADR-0109's
contention list (`ci.yml`, `Directory.Packages.props`, `global.json`).

ADR-0084: `InMemoryRuleCacheTests` is 166 lines and gains roughly 25;
`OverlayHighlightRequestedV1HandlerTests` is 80 and gains roughly 25 — both stay
far under 300. The new cache helper takes **four** parameters, which is the
limit, so it is a separate helper rather than a sixth parameter on the existing
five-parameter `ActiveRule`.

## Phase 4a colour — characterisation, observed GREEN, red only under counterfactual

**Behaviour-preserving.** Both new tests describe behaviour that exists at
`55d29fe4`, and the issue is explicit that *nothing is broken*. There is no red
available and none is to be manufactured: a compile error is not a red test
(spec 061, `24e6fc4c`), and inventing one would mean adding the dedupe the issue
forbids.

So, precisely, **per test**:

| Test | Against current `develop` | Under its counterfactual |
|---|---|---|
| `Two_rules_highlighting_the_same_overlay_both_stay_in_the_bucket` | **GREEN**, first run | **RED** under C1 |
| `Two_highlights_of_one_event_on_one_overlay_are_both_broadcast` | **GREEN**, first run | **RED** under C2 |

This is **not** the red-then-green sequencing of a bug fix. A red first run here
would mean the production code already collapses the pair, which would be a
finding, not progress — report it, do not proceed.

### C1 — a dedupe in the shipped rule cache

In `src/Automation/Infrastructure/Cache/InMemoryRuleCache.cs`, inside the `lock`
in `Upsert` (`:64-69`), between the `RemoveAll` and the `Add`:

```csharp
lock (gate)
{
    bucket.RemoveAll(compiledRule => compiledRule.Identifier == rule.Id);

    // C1 (spec 176) — counterfactual only, never committed.
    if (compiled.Action is RuleAction.HighlightOverlay highlight &&
        bucket.Any(existing => existing.Action is RuleAction.HighlightOverlay other
            && other.Overlay == highlight.Overlay))
    {
        return;
    }

    bucket.Add(compiled);
    bucket.Sort((left, right) => left.CreatedAt.CompareTo(right.CreatedAt));
}
```

This is the issue's own wording made executable: *"don't register a second rule
for an overlay already highlighted."*

**Written prediction.**

- `InMemoryRuleCacheTests.Two_rules_highlighting_the_same_overlay_both_stay_in_the_bucket`
  — **RED**, bucket count 1, expected 2.
- **All nine existing `InMemoryRuleCacheTests` GREEN.** Every one builds rules
  through `RuleBuilder`'s default `SetVariableValue` action, so the
  `is RuleAction.HighlightOverlay` pattern is false and the guard never fires.
- **Spec 105's two tests GREEN** —
  `RuleEvaluatorTests.Two_highlight_actions_on_the_same_overlay_both_yield_an_effect`
  and
  `FabEventIngestedV1HandlerTests.Two_highlight_actions_on_the_same_overlay_both_publish`.
  They construct `Automation.Application.Tests.Fakes.InMemoryRuleCache`, a
  different class that C1 does not touch. **This prediction is the point of the
  whole exercise**: observing it is what converts #2214's central claim from an
  argument into a measurement, and it is the one prediction whose failure would
  be good news.
- **Everything else in the solution GREEN.** `HighlightOverlay.From` occurs
  **14 times across `tests/`**, in exactly five files — `RuleActionTests`
  (domain construction), `CreateRuleCommandHandlerTests`, `RuleQueryHandlerTests`,
  `RuleEvaluatorTests` and `FabEventIngestedV1HandlerTests`. **None of the five
  is in `Automation.Infrastructure.Tests`**, so none of them reaches the class C1
  mutates; the last two reach only the fake. Re-counted against `55d29fe4`
  rather than carried over — spec 105 recorded eleven, and the figure moved.
  `FirstPublishPerTypeTests` opens a fresh `Guid.NewGuid()` per round and is
  `[Trait("Category", "Measurement")]` besides.

### C2 — a dedupe at the consumer, on the pair the contract makes indistinguishable

In `src/LayoutComposition/Application/EventHandlers/OverlayHighlightRequestedV1Handler.cs`,
add a **static** seen-set and gate the broadcast at `:41`:

```csharp
// C2 (spec 176) — counterfactual only, never committed.
private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid, Guid), byte> Seen = new();
...
if (!Seen.TryAdd((overlayIdentifier, causingEventIdentifier), 0))
{
    return;
}

await broadcaster.OverlayHighlightedAsync(...);
```

**Static on purpose, and the reason shapes the test.** Wolverine resolves a
handler per message, so a per-instance seen-set would not dedupe anything in
production — the only dedupe shape that *works* against outbox redelivery is
process-wide or store-backed. C2 is therefore the realistic mutation, and the
new test calls `Handle` twice on **one** handler instance, which is the strictly
broader net: it reddens under a process-wide dedupe **and** under an
instance-scoped one.

**Written prediction.**

- `OverlayHighlightRequestedV1HandlerTests.Two_highlights_of_one_event_on_one_overlay_are_both_broadcast`
  — **RED**, one recorded notification, expected 2.
- **The three existing tests in that file GREEN.** Each handles exactly one
  message, and each builds its overlay from a fresh `Guid.CreateVersion7()`, so
  no two share a `(overlay, causing)` key even across a static set.
- **The cache test GREEN.** It never reaches LayoutComposition — which is the
  argument for two tests rather than one: neither counterfactual reddens the
  other layer's test, so neither test subsumes the other.
- **Everything else GREEN**, with one caveat stated rather than assumed: the
  integration suites (`Integration.Tests/Automation/*`) are **not run** under the
  counterfactual, because they need the Aspire boot this plan declines. They
  publish single-overlay highlights with distinct identifiers per round, so C2
  would not redden them, but that is **reasoning, not observation**, and the
  phase-4 report must say so in those words.

### What the counterfactual does not prove

Neither mutation is a bug anyone is likely to write by accident; they are probes.
What they establish is narrower and sufficient: that the two new assertions are
load-bearing rather than restatements of coverage the eleven existing tests in
the two files already provide.

### If a prediction misses

Record the actual result **beside** the prediction and say the prediction was
wrong. Do not retro-fit the prediction to the observation, and do not adjust a
test to restore a prediction — spec 104's commit `08782b7c` exists because that
distinction was worth keeping. In particular, if an existing test reddens under
C1 or C2, that is the finding.
