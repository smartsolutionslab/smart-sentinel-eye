# Quickstart: every journey has a beginning

**Feature**: `027-trace-background-publishers` · 2026-08-22

Manual in places on purpose: SC-001, SC-002 and SC-007 are about **a person
reading the dashboard**, and no test substitutes for that (spec 026 established
there is no cross-service test to write).

---

## Before

1. `dotnet run --project src/AppHost`
2. Aspire dashboard → **Traces**

**A camera going unhealthy.** The stack's camera simulator provides real streams;
stopping one makes the watcher notice. Or wait — probes fail on their own often
enough.

**Expect, today:** the `StreamHealthChangedV1` publish is a **trace root**
containing only `stream-distribution` spans, and audit-observability's receive of
it is a **separate root**. Two ends, unconnected — the same shape spec 026's
verification note records for ingestion.

**Write down both trace IDs.** They are the comparison.

**Audit retention** runs on a timer; its `AuditChunkArchivedV1` publish is a root
the same way.

---

## After

Same walk.

1. The observation should now be the **root of one trace** containing the
   downstream receive — the shape spec 026's trace `a44f7abc…` shows for
   ingestion.
2. From the downstream record, reach the observation **without** using timestamps.
3. Same for an archived chunk.

---

## The four things most likely to be wrong

**One journey for the whole sweep.** Cheaper, joins the trace, and looks correct
from the downstream end. **Check two cameras that changed in the same poll are
two traces**, not that some trace joined up.

**A journey for every camera, every poll.** The subtler half, and the reason the
code is in the domain event handler rather than the loop: `PollOnceAsync` calls
the command handler unconditionally, and only the aggregate knows whether
anything changed. **Look for traces named after an observation that observed no
change** — there should be none. A dashboard full of them is this failure.

**Retention skipped.** It publishes inline rather than through a domain event
handler, so it does not look like the thing spec 026 fixed. Confirm an archived
chunk joins up too, separately.

**A failed announcement reading as a quiet success.** Both sites must mark
failure. Without it, a publish that could not be made looks exactly like a
healthy one nothing subscribed to — same name, no children, no error.

---

## The measurements

**Twice**, per FR-007 and the lesson recorded from spec 026 — a single run after
machine churn looks exactly like a regression.

```sh
dotnet test tests/Integration.Tests --filter "Category=Measurement"
```

Plus the two figures this feature actually touches: **health poll cadence** and
**retention run duration**, before and after. If there is no clean before, make
one the way spec 026 did — `git revert -n <commit>`, measure, `git reset --hard`.

---

## The one inference to close while you are here

Nine publishers are classified as needing nothing because an HTTP request or a
message already gives them a cause. Message-driven is observed directly. **HTTP
is observed one layer short** — a request span with children, but no publish
captured under one.

The stack is up: **register a camera through the API and look at the trace.** If
the `send` sits under the `POST` span, the classification is confirmed. If it
does not, that is a new finding and a new issue, and nothing in this feature
changes.

---

## Recording it

`verification.md` gets both walks with screenshots, the before/after trace IDs,
both measurements, the sweep check stated explicitly — and **the full publisher
survey as a table** (FR-009, SC-008). Finding the orphans was the expensive part
of this feature; the table is what stops the next person repeating it.
