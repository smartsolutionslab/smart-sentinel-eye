# Contract — the comparison

What this feature promises about the figures it produces. **The comparison is the
deliverable**, so these are the clauses that make two numbers legitimately
belong in one table — and the clauses that stop them appearing to.

---

## C1 — The run shape is identical by construction, not by agreement

Both the fixture run and the run-mode run read one shape: generator, warm-up
count, measured count, writer count, target rate, tolerance.

**Satisfied when** changing the shape for one run changes it for the other,
because there is one definition. **Violated by** two constants that happen to
match, or by a sentence asserting they match.

---

## C2 — Every run states the conditions it ran under

Environment, endpoint actually connected to, intended and achieved rate, logging
level, measurement-switch state, rows measured and rows missing stamps.

**Satisfied when** a reader can attribute a figure to a stack without asking
anyone. **Violated by** a breakdown whose provenance is in someone's memory of
which shell they ran it in.

The conditions block is emitted **before** any assertion that can fail, so a
refused run still reports what it was refused for.

---

## C3 — A run that cannot meet its conditions reports, it does not publish

If the clocks cannot be bounded, the rate is missed, the logging is verbose, rows
are missing stamps, or the parts do not cover each row — the run says so and does
**not** present the breakdown as a measurement.

**"We could not tell" is a result.** Satisfied when each of those refusals is
observable as an outcome rather than a log line. Violated by a run that prints
numbers and a warning and lets the reader choose.

---

## C4 — The driver never starts a stack

It targets a stack it did not create. Absent or unreachable configuration is a
**refusal naming what could not be reached**, never a fallback.

**Satisfied when** the run fails with an address in the message and no stack was
booted. **Violated by** any path that boots one — which would reproduce the exact
defect this feature exists to remove, while reporting success.

This is the single most important clause here. A silent fallback produces a figure
labelled "run mode" that is not.

---

## C5 — Both spans, and the unestablished ones named

The observed span and the requirement's span, the latter as a floor and a ceiling
rather than a figure it cannot support.

**The write leg and the requirement span's floor remain not established** — run
mode has the same host/container clock split as the fixture. They appear in the
same table as the established figures, marked, not in a footnote.

**Violated by** quietly dropping them, which would make the table look stronger
than the evidence.

---

## C6 — Three runs, and no effect size from fewer

At least three runs, spread reported.

**And the asymmetry is reported with it**: at Debug the logging is the bottleneck
so the figure reproduces; at Warning the machine is, so it does not. A single pair
can land anywhere from 2.1× to 3.0× with nothing having changed — this repository
has already published an overstated figure for exactly that reason.

**Violated by** averaging the spread away, or quoting one pair as the effect.

---

## C7 — The record states what was measured and stops

No recommendation. No proposed lever. No change to NFR-001's budget.

**Satisfied when** a reviewer can find no sentence of the form "and therefore we
should". **Violated by** a conclusion — however well-supported — because that
decision belongs to whoever holds the requirement, and the pull toward taking it
is what produced two conclusions that skipped this measurement.

A reviewer should push back if the record does otherwise.

---

## C8 — What no clause here can promise

**That the driver reached run mode.** An endpoint is an endpoint; nothing in the
run can distinguish the intended stack from another answering on that address.

C2 makes it *checkable* by a human — the reported address against the stack they
started, and a persistent store growing by exactly the measured count. It does not
make it *proved*, and this contract does not pretend otherwise.
