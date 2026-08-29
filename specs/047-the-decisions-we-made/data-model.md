# Data Model: the audit record

**Feature**: `047-the-decisions-we-made` · **Plan**: [plan.md](./plan.md)

**No persistence, no code types.** This is the shape of the record in
`audit.md`, written down because FR-002 requires a vocabulary and because an
audit whose terms drift mid-way is worth less than no audit.

---

## 1. `Claim` — the unit of audit

**Not the decision. The claim.** Validated in [research.md](./research.md):
decision 005 makes three claims (RTSP, ONVIF, vendor adapters) and they take
three different verdicts. Auditing per row would have recorded one answer and
been wrong twice.

| Field | Meaning |
|---|---|
| `decision` | 001–027, or a §IX row |
| `claim` | One assertion, quoted from the original text |
| `verdict` | One of the four below |
| `evidence` | What was run, and what came back |
| `disposition` | Where a non-holding claim goes next (§3) |

A decision's overall status is **the worst verdict among its claims**, with the
per-claim detail kept. Collapsing to the worst *and discarding the detail* is the
failure this model exists to prevent.

---

## 2. `Verdict` — four values, and the rule for each

| Verdict | Applies when | Example from the reconnaissance |
|---|---|---|
| **Holds** | Every part of the claim is true today | 016 — nine bounded contexts, and there are exactly nine |
| **Diverges** | The job is done, but differently or under another name | 019 — the language is AEL, not CEL |
| **Not built** | Neither the named thing nor anything doing its job exists | 012 — no shard, no coordinator, no ownership map |
| **Unverifiable here** | The claim is about deployment, hardware or topology this repository cannot settle | 013 — the OT/IT VLAN split |

**The pair that matters is *diverges* vs *not built*.** One says the decision is
stale; the other says the work is absent. They lead to opposite dispositions —
an ADR versus an issue — so getting them the wrong way round produces the wrong
follow-up work, not merely an untidy note.

**"Unverifiable here" is a verdict, not a gap.** Recording 013 as *not built*
would be a false statement about a fab we cannot see. Refusing to guess is the
correct answer, and the model makes room for it so nobody is tempted to round it
into one of the other three.

---

## 3. `Disposition` — where a non-holding claim goes

Every claim that does not hold ends in **exactly one** of two places (FR-009):

| Disposition | Meaning | Chosen when |
|---|---|---|
| **Legitimise** | An ADR amends the decision to match the system | The system is right and the decision is stale |
| **Correct** | An issue proposes changing the system | The decision is right and the system has not caught up |

**Neither is a default.** Where the answer is not obvious, the claim becomes an
**issue** — the cheaper mistake, because an issue that turns out unnecessary is
closed, while an ADR that legitimises a mistake makes it policy.

**"Recorded and left alone" is not a disposition.** A note that does neither is
how the current situation arose: four documents describing a system nobody had
checked, with nothing obliging anyone to act.

---

## 4. `Evidence` — a command and its result

Recorded so the next reader can re-run it, never as a conclusion.

- ✅ `grep -ril "hikvision\|vapix\|bosch" src/ → no matches`
- ❌ *"vendor adapters are missing"*

**Absence carries two searches**, both recorded (research D3): one for the
**name**, one for the **job**. A verdict of *not built* backed by a single grep
is indistinguishable from a lazy one, and the record must let a reader tell.

**Why the command and not the conclusion.** The claim being audited is itself a
conclusion somebody wrote down without evidence, and this feature exists because
nobody could check it. Recording conclusions would reproduce the original defect
in a new document.
