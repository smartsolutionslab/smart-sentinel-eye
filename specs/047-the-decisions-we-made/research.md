# Phase 0 Research: the audit method, validated

**Feature**: `047-the-decisions-we-made` · **Spec**: [spec.md](./spec.md)

Phase 0 here is **not** the audit — the audit is the deliverable. It is the
method the audit will use, tested against the rows most likely to break it.

---

## The problem the method has to solve

A decision can fail to describe the system in several different ways, and
collapsing them loses the information that makes the audit worth doing:

- The thing exists, under another name, doing roughly the job.
- The thing exists, doing a *different* job than described.
- The thing does not exist and nothing does its job.
- The thing is a future intention, correctly not built yet.
- The claim is about deployment or hardware and **cannot be settled from this
  repository at all**.

**A single "does it exist?" grep answers none of these.** It answers "is this
string present", which is a different question — and the reconnaissance nearly
mistook one for the other.

## The verdict vocabulary

Four verdicts (FR-002), each with a rule for when it applies:

| Verdict | Applies when |
|---|---|
| **Holds** | Every claim in the row is true of the repository today. |
| **Diverges** | The job is done, differently or under another name, than the row says. **The row is wrong; the system may be right.** |
| **Not built** | Neither the named thing nor anything doing its job exists. |
| **Unverifiable here** | The claim is about deployment, hardware or network topology this repository cannot show either way. |

**"Diverges" and "not built" are the pair that matters**, and telling them apart
is the whole difficulty. Only the second search distinguishes them: the first
looks for the *name*, the second for the *job*.

## The two-search rule

For every claim:

1. **Search for the name.** The component, interface, technology as written.
2. **If absent, search for the job**, on different terms — what would the code
   look like if someone did this work and called it something else?

A verdict of **not built** requires *both* to fail. This is a standing rule in
this repo, learned from a case where a near-miss result felt like having looked.

---

## Validation: the hardest case

Decisions **005, 012 and 013** all assign work to **StreamKeeper**. The name
appears nowhere in the code, so a careless audit records the same verdict three
times. **All three verdicts differ.**

### 005 — camera protocols and vendor adapters → **Diverges (partly)**

> *RTSP + ONVIF (Profile S/T) on day one. Adapter pattern in StreamKeeper for
> vendor-specific drivers (Axis VAPIX, Hikvision, Bosch, …)*

- **RTSP: holds.** It is how MediaMTX pulls from cameras.
- **ONVIF: not built.** No occurrence anywhere in the code.
- **Vendor adapters: not built.** No `vapix`, `hikvision` or `bosch` anywhere,
  and no adapter seam standing in for them.

One row, three claims, and they do not share a verdict — so **the unit of audit
is the claim, not the row**. That is the single most important thing this
validation established.

### 012 — SFU scaling and the coordinator → **Not built**

> *horizontal shard-by-camera. Coordinator service (Raft/etcd-class consistency)
> owns cam→SFU ownership map. Failover under 5 s.*

Second search found nothing doing the job: no sharding, no ownership map, no
coordinator, no failover logic. The only `failover` in the AppHost is an
unrelated comment about a timeout. The system runs **one** SFU (plus a dev-only
simulator), so there is nothing to shard between and the decision is not
contradicted so much as **unreached**.

### 013 — network topology → **Unverifiable here**

> *cameras on isolated OT VLAN. StreamKeeper is dual-NIC and the only bridge to
> the IT VLAN.*

A statement about fab network deployment. Nothing in this repository can confirm
or refute it, and **calling it "not built" would be a false verdict** — it may be
exactly how a fab is wired. This is why the fourth verdict exists.

### What the validation proves

The vocabulary survives its hardest case, and the audit unit is **the claim**.
Had the method been "one verdict per decision", these three rows would have been
recorded identically and two of the three records would have been wrong.

---

## A second validation, on the opposite failure

**016 — nine bounded contexts.** Counted: exactly nine context projects, matching
the names listed. **Holds.**

Recorded because FR-004 requires it and because an audit that only produces
failures cannot be distinguished from one that stopped at the interesting rows.
It also calibrates the result: the reconnaissance found real problems, and it
found rows that are simply right.

---

## Decisions the plan takes from this

**D1. The unit of audit is the claim, not the decision.** A row is verdicted per
claim, and the row's overall status is the worst of them, with the detail kept.

**D2. Evidence is recorded as the command and its result**, not as a conclusion.
"No occurrence of `hikvision` in `src/`" is checkable by the next reader;
"vendor adapters are missing" is not.

**D3. Absence needs two searches**, and the second one is recorded too —
otherwise a reader cannot tell a thorough "not built" from a lazy one.

**D4. "Unverifiable here" is a real verdict, not a failure to finish.** Rows
about fab hardware and network topology get it, and the audit says why rather
than straining to guess.

**D5. §IX is audited on the same terms**, with one addition: its rows are checked
against **accepted ADRs** as well as code, because that is how its observability
row went stale — no code changed, an ADR did.

---

## Alternatives considered

| Option | Why not |
|---|---|
| One verdict per decision | Validated as wrong: 005's three claims have three verdicts. |
| Binary true/false | Cannot express "built differently" or "cannot be checked here", which between them cover most of the interesting rows. |
| Audit by reading the ADRs against each other | How the current state arose. Four documents agreed with each other and none had been checked against code (spec 040). |
| Automate the whole audit | Most claims are prose about intent. A grep cannot decide whether MediaMTX plus `StreamDistribution` constitutes "StreamKeeper". The guard (FR-010) automates *keeping* the answer, not finding it. |
