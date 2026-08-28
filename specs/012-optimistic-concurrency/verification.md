# Verification: Optimistic concurrency, actually enforced

**Feature**: `012-optimistic-concurrency` · #1154 · observed **2026-08-28**

**Status: T059's three observations were made, and all three hold.** T059 says
*"'Done' is those observations, not a green compile"*, so this note records what
was run and what it proved, not that the build succeeded.

**There is no quickstart to follow.** Spec 012 predates that convention — it has
only `plan.md` and `tasks.md`, because it is *"remediation of an accepted ADR, not
a new capability"* (tasks.md). T059's scope is therefore its own sentence: the two
integration tests from T011 and T020 cited by name, plus the e2e run.

---

## 1. T011 — the EF wiring, not a mocked throw

`tests/Integration.Tests/LayoutComposition/AggregateVersionConflictIntegrationTests.cs`,
against real Postgres and the real `Layout` mapping. T011 insists on this
explicitly: *"**Not a mocked throw** — a mock proves nothing about the EF wiring,
which is the entire point of #1154."*

| Test | |
|---|---|
| `The_second_of_two_concurrent_writers_is_rejected` | **Passed** (1 s) |
| `The_first_writer_moves_the_version_off_its_loaded_value` | **Passed** (363 ms) |
| `Without_the_interceptor_both_writers_commit_and_one_update_is_lost` | **Passed** (275 ms) |

**The third is the one that makes this verification mean anything.** It is a
negative control: same schema, same concurrency-token mapping, but no version
bump — and it asserts the *broken* outcome, `reloaded.Version.ShouldBe(0)`, with
both writers committing and the first one's work silently gone. Its own doc says
this is *"`develop`'s behaviour before spec 012."*

Without it, a suite could pass on a build where the interceptor did nothing at
all. That is exactly the failure T059's "not a green compile" clause is written
against.

## 2. T020 — the ETag round trip, deterministic and without racing

`tests/Integration.Tests/LayoutComposition/LayoutETagIntegrationTests.cs`.

| Test | |
|---|---|
| `Reading_a_layout_returns_an_ETag_matching_the_version_in_the_body` | **Passed** (362 ms) |
| `The_list_endpoint_carries_a_version_on_every_chain` | **Passed** (567 ms) |
| `A_mutation_without_If_Match_is_refused_with_428` | **Passed** (827 ms) |
| `A_mutation_carrying_a_superseded_version_is_refused_with_409` | **Passed** (813 ms) |
| `The_same_mutation_succeeds_once_the_caller_re_reads` | **Passed** (15 s) |

The fourth is T020's own assertion — *"`GET` → mutate → mutate again with the
stale `ETag` → 409"*. The fifth matters just as much: it proves the refusal is
**recoverable**, not a dead end. A mechanism that refused the stale write and then
refused the corrected one too would satisfy T020's letter and be unusable.

**Both classes together: 8 passed, 0 failed, 24 s**, on the Aspire fixture
(`[Collection(AspireCollection.Name)]`) — a real stack, real Postgres, real HTTP.

## 3. The e2e run

`e2e/layouts.spec.ts` and `e2e/system-variables.spec.ts` (T054, T055), Chromium
against a run-mode stack: **8 passed, 52.8 s**.

The one T059 is really asking about:

```
[6/8] e2e\layouts.spec.ts:90:5 ›
      a second operator publishing the same revision is refused, not silently applied
```

Two browser contexts, both loading the list before either publishes, so both hold
the same version — the ordering *is* the test. The first publishes and wins; the
second is refused, and the assertion is on the conflict being **surfaced**:

```ts
await expect(alert).toContainText(/changed|reload|re-read/i);
await expect(alert).not.toContainText(/try again/i);
```

Its comment states why it is written that way: *"an assertion on the absence of a
conflict here would have passed on the broken build, so the test asserts the
conflict is surfaced."*

---

## What this note does not claim

**No quickstart checklist was followed**, because none exists for this spec. The
three observations T059 names are the whole of its scope, and they are all here.

**The frontend transport work (T060–T063) was not re-verified by hand.** It landed
in commits `8076a77` (layouts `If-Match`) and `839d0fb` (overlays `If-Match`), and
the e2e run above exercises the layouts half end to end. The overlays half rests
on `OverlayETagIntegrationTests`, which is structurally identical to T020's per
ADR-0104 but was not part of T059's named scope.

**A caution for whoever reads the layouts e2e as general assurance.** It proves
*layouts* surfaces a refused publish. It does not generalise: verifying spec 031
the same day found that Automation's rules page refuses a stale publish correctly
on the server and then tells the operator **nothing at all** (issue 1952). The
same conflict, the same shared client helpers, a different page — and only the
page with an e2e test covering the words actually shows them.
