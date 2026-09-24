# Tasks — Spec 243, the level the wrapper adds

**Spec:** `specs/243-the-level-the-wrapper-adds/spec.md` ·
**Plan:** `specs/243-the-level-the-wrapper-adds/plan.md` ·
**Issue:** #2496 · **Branch:** `fix/2496-buildcontext-dead-letter` · **Base:** `origin/develop` @ `a54b11d0`

**Phase-4a colour: RED overall, one characterisation-bound story.**

- **US1 (defects 1 + 2) — RED.** Behaviour-changing. A US1 test that arrives green is a phase-4 failure.
- **US2 (defect 3) — CHARACTERISATION.** Behaviour-preserving. Covering tests observed **green on HEAD before any production edit**, and must pass **unmodified** after. An assertion that has to be edited is evidence the behaviour moved: block, don't adjust.

Ambiguity resolves to red (CLAUDE.md §*Phase 4a has two colours*); where a task below could be read either way, it is red. Why one issue carries two colours without the "refactor + bug fix is two issues" hazard: spec §*Why one issue, two colours*.

`test-writer` writes and runs every test in T001–T003 and returns the **verbatim** output; the engineer receives it as its brief and **may not edit the tests to pass**. Both outputs (green characterisation, red US1) are quoted in the PR body.

**No new ADR, no constitution amendment, no task issues.** Phase-3 gate = the feature's issue on Project #13: #2496 is open, on *Smart Sentinel Eye*, labelled `agent:ready`, no `agent:blocked` (verified 2026-09-24). `/speckit-taskstoissues` is **not** to be run.

**Engineer:** `backend-engineer` (Automation Application layer only).

## Parallelism (ADR-0109)

**No `[P]` tasks.** Every test task edits `tests/Automation.Application.Tests/EventHandlers/FabEventIngestedV1HandlerTests.cs`; every implementation task edits `src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs`. One file per side, so the work is serial by construction. **No foundational blocker** — nothing in `Shared.Kernel`, `Shared.Contracts`, `AppHost` or any Aspire resource changes.

**Order is load-bearing** — it is what lets each colour's evidence be captured independently:

```
T001 (char., green on HEAD) → T002 + T003 (red on HEAD) → T004 (US2 impl: T001 green, T002/T003 still red)
  → T005 (US1 impl: all green) → T006 (verify) 
```

T004 before T005 proves the disposal restructure alone changes no behaviour: the characterisation stays green **and** the US1 reds stay red for their original reason. If T004 turns any US1 test green, or any characterisation test red, stop — the restructure moved behaviour.

---

## US2 (P2) — The evaluation context's document is returned to the pool

### Phase 4a — CHARACTERISATION (`test-writer`), first

- [ ] **T001 [US2]** In `tests/Automation.Application.Tests/EventHandlers/FabEventIngestedV1HandlerTests.cs`, add `A_string_read_from_the_payload_is_published_intact`: rule predicate `$.payload.cycleTime <= 30`, value expression `$.payload.station`, event payload `{"cycleTime":27,"station":"station-4-east"}` → exactly one `SystemVariableValueRequestedV1` with `Value == "station-4-east"`. Reuse `ActiveSetVariableRule` / `PlcCycleStart` (extend `PlcCycleStart` with an optional `payload` parameter defaulting to the current `{"cycleTime":27}`, so existing call sites are unchanged). Run the **whole file** on HEAD `a54b11d0` with no production change: **all green**, output returned verbatim. This plus the existing 10 methods (12 cases) is the characterisation set.
  *Done when:* file green on HEAD, verbatim output captured.

---

## US1 (P1) — An unreadable payload costs one evaluation, not a dead-letter; a legal one is always evaluated

### Phase 4a — RED (`test-writer`)

- [ ] **T002 [US1]** Same file. Add, with fixtures built programmatically (`Nested(int depth) => new string('[', depth) + "1" + new string(']', depth)`, depth as a named value, never a literal blob):
  - `A_payload_nested_to_the_depth_ingestion_accepts_is_evaluated` — depth 64, rule predicate `$.source == "plc"`, action `SetVariableValue("oeeLine1", "1")` → no throw; one `SystemVariableValueRequestedV1("oeeLine1", "1")`; no log entries. **Red on HEAD with `JsonReaderException` escaping `Handle`.**
  - `A_payload_one_level_deeper_than_ingestion_accepts_is_logged_and_skipped` — depth 65 → no throw; nothing published; one `Warning` whose exception is a `JsonException`. Red on HEAD.
  - `A_payload_that_is_not_json_is_logged_and_skipped` — `[Theory]` over `""`, `"   "`, `"{"`, `"not json"`, `"{\"a\":1}}"` → no throw; nothing published; exactly one `Warning`, exception `ShouldBeAssignableTo<JsonException>()`, rendered message contains the event identifier. Red on HEAD.
  - `A_null_payload_is_logged_and_skipped` — `Payload: null!` (contract violation) → same assertions. Red on HEAD.
  *Done when:* each fails on HEAD **because an exception escapes `Handle`** — quote it. A compile failure is not an acceptable red: assert against `CapturingLogger.Entries` and `FakeEventBus.Published`, never against a not-yet-existing `Log` member.

- [ ] **T003 [US1]** Same file, after T002. Add:
  - `An_unparseable_payload_is_logged_distinctly_from_both_fab_failures` — same `Guid` on three events (fab `""`; fab `"NotAFab"`; fab `"munich"` with payload `"not json"`), one `CapturingLogger` each; the three single rendered messages are pairwise different. (Same-identifier discipline as the existing `An_absent_fab_is_logged_distinctly_from_one_that_will_not_parse`, or the test passes on the identifier alone.) Red on HEAD (the third handler throws / logs nothing).
  - `An_unparseable_payload_log_carries_its_length_not_its_text` — payload `"not json"` padded with `x` to 60 000 characters → rendered message does **not** contain the payload text (assert on a distinctive substring, e.g. the first 200 characters) and **does** contain `"60000"`. Red on HEAD.
  *Done when:* red on HEAD for the stated reason; verbatim output returned together with T002's.

### Phase 4b — implementation (`backend-engineer`)

- [ ] **T004 [US2]** `src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs`: rename `BuildContext` → `ParseContext`, return the `JsonDocument` instead of an `EvaluationContext`; in `Handle`, `using (document) { effects = evaluator.Evaluate(…, new EvaluationContext(document.RootElement)); }`, with the publish loop **outside** the `using` (plan §*Defect 3*). Update `ParseContext`'s doc comment to say the caller owns the document. **No `MaxDepth`, no `catch` yet.** Run the file: T001 set **green, unmodified**; T002/T003 **still red, same failure**. Quote both.
  *Done when:* that exact split is observed.

- [ ] **T005 [US1]** Same file + `src/Automation/Application/Log.cs`:
  1. `ParseContext` parses with `JsonDocumentOptions { MaxDepth = PayloadMaximumDepth + 1 }`, `PayloadMaximumDepth = 64`, a static readonly options field, and a *why* comment naming whose limit it mirrors (EventIngestion's `Payload`, System.Text.Json's default) and that the `+ 1` is the wrapper object. No reference to `EventIngestion` (cross-context).
  2. In `Handle`, a `try` around **only** the `ParseContext` call, `catch (JsonException exception)` → `logger.SkippedEventWithUnparseablePayload(exception, eventIdentifier, payloadLength)` → `return`. `Evaluate` stays outside the `try`.
  3. Read the payload via the existing deconstruction (replace the `_` at the `Payload` position with a local named `payload` — `HandlerDeconstructionTests` checks the name). The length expression must be null-safe (`payload?.Length ?? 0`) or the guard throws on the input it guards.
  4. `Log.cs`: add `SkippedEventWithUnparseablePayload(this ILogger logger, Exception exception, Guid @event, int payloadLength)`, `Warning`, a message distinct from both fab messages, e.g. *"Ingested event {Event} carried a payload of {PayloadLength} characters that is not valid JSON; no rule evaluated."* XML doc says why it names the length and not the value (contrast with `SkippedEventWithUnparseableFab`).
  *Done when:* whole file green, **no test edited since T001–T003 returned**; `dotnet build -c Release` clean (analyzers are errors in Release; collection expressions, no `_` fields, `Ensure.That` unchanged); `tests/Architecture.Tests` green (boundary + `HandlerDeconstructionTests`).

---

## Cross-cutting

- [ ] **T006 [US1][US2]** Phase-5 verification note, `specs/243-the-level-the-wrapper-adds/verification.md`, per spec §*Independent end-to-end test procedure*: observation at the Automation queue via the RabbitMQ management API **or** the explicit fallback label *"not observed on the stack — no ingress can produce this input (spec §Reachability today)"*. Must say **latent**, not "reproduced through ingress". Re-run the allocation measurement twice against the built handler path (≈ −24.6 KB/event at 10 KB payload, ≈ −65.8 KB/event at 60 KB) or state it was not re-run; do **not** cite `AelInterpreterBenchmarkTests` as evidence — it does not exercise this method. Record every figure in the note, not only in the report (memory: *self-review catches contradictions, never omissions*).
  *Done when:* note committed; its claims match what was observed.

## Commit plan (ADR-0030, ADR-0086 — no `Co-Authored-By`)

Each commit builds on its own (rebase-merge lands them individually):

1. `docs(243): specify, plan and task the context parse's dead-letter guard` — these three files.
2. `test(automation): characterise the fab-event handler's published values` — T001.
3. `test(automation): pin unreadable and 64-deep payloads to log-and-skip and evaluate` — T002 + T003. **This commit leaves the test run red by design** (it still compiles — the reds are assertion failures, not compile errors); say so in the commit body. If the orchestrator's per-commit rule requires green tests rather than a green build, fold it into commit 5 and keep the red output in the PR body instead.
4. `refactor(automation): dispose the fab-event handler's evaluation document` — T004.
5. `fix(automation): evaluate legal deep payloads and skip unreadable ones instead of dead-lettering` — T005.
6. `docs(243): record phase-5 verification for #2496` — T006.
