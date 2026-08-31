# Contract — what the attribution must say

This feature's only interface is **a document someone reads to decide what to do
about NFR-001**. So the contract is on the record, not on an API — and it is a
contract because the failure mode here is a documentation failure, which this
project has had several of.

---

## C1. The breakdown

| Must | |
|---|---|
| **Name every part** | broker hop, work on arrival, and the write, at minimum |
| **Give each a figure** | at the rate the requirement names |
| **Sum to the total** | and report whatever they do not account for as an **unattributed remainder** |
| **Mark each part** | inside or outside the requirement's own span |

**Must not**: distribute a remainder across the parts it might belong to; report
a part without saying which span it belongs to.

---

## C2. Both spans

| Must | |
|---|---|
| Report the requirement's span | broker hand-over → row committed |
| Report the observed span | the one every prior figure quotes |
| Account for the difference | at the front and at the back separately |

**Must not**: use one figure for both. Three ADRs did, and at 1.7× off a budget
that difference may be the answer.

---

## C3. The clocks

| Must | |
|---|---|
| State the measured offset | between the stamping processes |
| State its residual | the round trip's own uncertainty |
| Declare the attribution **not established** | if the offset cannot be bounded under 10 ms |

**Must not**: assert the clocks agree because the processes share a machine.
That assumption is what the story exists to test.

---

## C4. The runs

| Must | |
|---|---|
| At least three | one run is an anecdote |
| Intended **and achieved** rate | a run at 60 ev/s answers a different question |
| The spread between runs | two of six prior runs spiked by an order of magnitude |
| The apparatus' own cost | measured with it off and on, not argued |

---

## C5. What the record must not contain

| Must not | Why |
|---|---|
| A changed budget | a passing number obtained by moving the line reports the requirement as met when it is not |
| A recommendation | whether the requirement moves, or a lever follows, is a decision this work informs and does not take |
| A claim about production | there is no production deployment |
| A claim that the requirement is or is not achievable | that is the question the record exists to inform |

**C5 is the load-bearing one.** A breakdown is persuasive, and the pull towards
"and therefore we should…" is exactly what produced two recorded conclusions
that skipped this measurement entirely.
