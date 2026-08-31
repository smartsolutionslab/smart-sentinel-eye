# Quickstart — 053 where the audit milliseconds go

How to reproduce the number everyone quotes, how to take the breakdown, and what
none of it establishes.

---

## Reproducing the figure that started this

The existing measurement lives in
`tests/Integration.Tests/AuditObservability/NFR001_AuditIngestLatencyTests.cs`.
It is excluded from CI by `Category=Measurement` and stays that way: it fails,
and the budget is deliberately left at the requirement's 50 ms rather than tuned
to whatever the stack produces.

It drives value changes through the system-variables API and reads
`received_at - occurred_at` out of the audit store in SQL.

**Run it against a run-mode stack, not the fixture.** The fixture's figures were
seconds; run mode's are tens to hundreds of milliseconds, and the difference is
not the thing being studied here.

---

## Taking the breakdown

1. **Bound the clocks first.** It gates everything: an attribution over skewed
   clocks is a confident, specific, wrong answer. Ask the shared database its
   time from each participating process and compare with the process's own — one
   Postgres server serves all nine databases, so it is a common reference.
2. **Turn the apparatus on**, run at ~100 ev/s, three times.
3. **Run once with it off** at the same shape, so the apparatus' own cost is a
   measurement rather than an assertion.
4. **Read the parts beside the total** in one query. They come from one row, so
   they cannot drift apart.

---

## What "done" looks like

| Story | Done when | Not done merely because |
|---|---|---|
| **US2** | the offset between stamping processes is measured, with its residual | the processes share a machine |
| **US1** | every part carries a figure, they sum to the total, and each is marked inside or outside the requirement | there is a total and an intuition |
| **US3** | someone who has not seen this can state where the time goes and how far to trust it | the numbers exist in a session |

---

## What a complete run will still not establish

- **Whether the requirement is achievable.** That is the question this informs.
  Answering it here would be the same mistake as moving the budget.
- **What a fourth improvement should be.** Even if the breakdown makes one
  obvious.
- **Anything about production.** There is no production deployment (ADR-0130),
  and the recorded escape hatch — "audit gets its own pod and database node" —
  remains untested because there is nothing to test it on.
- **That these figures hold on other hardware.** They describe this stack.

---

## Environment notes that have already cost time here

- **The development trace view is poor at finding past traces.** Anything that
  depends on hunting history rather than provoking a specific event should be
  treated with suspicion — it is why the breakdown is carried on the row rather
  than read from spans.
- **Fixture and run mode differ by orders of magnitude** for this pipeline. Say
  which one produced a figure, every time.
- **The generator does not hit a rate exactly.** Prior records use bands —
  "99–113 ev/s" — for that reason. Report the achieved rate, not the intended one.
- **A first run after machine churn looks like a regression.** Run it twice
  before believing a change.
