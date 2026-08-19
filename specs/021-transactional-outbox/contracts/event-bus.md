# Contract: the publish seam

**Feature**: `021-transactional-outbox` · **Phase 1** · 2026-08-19

No wire contract changes. Nothing in `Shared.Contracts` gains, loses or versions
a field, and no consumer needs to be redeployed in step. The contract that
changes is the **internal** one between a write and the announcement it makes —
and it changes in a way the type system cannot express, which is why it is
written down here.

---

## `IEventBus.PublishAsync` — same signature, different promise

```csharp
Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
    where TEvent : notnull;
```

| | before | after |
|---|---|---|
| when the message leaves | immediately, on the call | when the surrounding write commits |
| if the call succeeds | the broker has it, or will very shortly | it is captured, and will be delivered iff the write commits |
| if the write then fails | **the message is already gone** | the message is discarded with the write |
| if the broker is unreachable | the call throws; the message is lost | the call succeeds; delivery is retried durably |
| if the process dies after the call | the message is lost | the message survives |

**The signature is identical in both columns.** A caller cannot tell which it is
holding, which is precisely how the defect survived a year and an ADR that
claimed otherwise. Two consequences worth stating rather than discovering:

- **`PublishAsync` no longer throwing is not proof of delivery.** It never
  really was, but afterwards it is explicitly not: it means captured. Code that
  treats a successful publish as "the other contexts know" was wrong before and
  is still wrong.
- **A publish outside a write has no transaction to join.** It is captured and
  flushed at the end of the scope. Anything publishing without an accompanying
  write is outside this feature's guarantee and should be looked at on its own.

---

## The obligation the seam places on its callers

The guarantee holds only if the publish and the write share a unit of work.
Three rules, all enforceable:

1. **Publish before committing.** A message captured after the commit is a
   message outside the transaction, which is the defect.
2. **Commit through the outbox.** `SaveChangesAndFlushMessagesAsync`, not
   `SaveChangesAsync` — the latter writes the domain rows and leaves the
   messages behind. Enforced by an architecture rule (FR-007), because this is
   the mistake a new repository makes by default.
3. **A handler on this path publishes and does nothing else.** Handlers now run
   before the commit, so a handler that writes elsewhere or has an external side
   effect would act on a write that may still be rolled back. Eleven of the
   twelve publish only; the twelfth reads (research.md R2).

Rule 3 is a constraint on domain-event handlers that did not exist before and
cannot be checked mechanically. It is stated here so a reviewer of a *new*
handler knows to ask.

---

## What a caller sees — unchanged (FR-013)

| Path | before | after |
|---|---|---|
| `POST /events/manual` | 201 with a resolving `Location`, or 503 | identical |
| webhook ingest | 201, or 503 | identical |
| every other write endpoint | unchanged | unchanged |
| broker unreachable during a write | **write succeeds, announcement silently lost** | write succeeds, announcement delivered later |

The last row is the whole feature, and it is invisible from outside except that
things stop going missing. That is worth noting for the verification phase: no
observable success-path behaviour changes, so a test that only exercises the
happy path will pass identically before and after and prove nothing.

---

## Failure semantics

| Failure | Result |
|---|---|
| the domain write fails | transaction rolls back; **no message is delivered** (FR-001, SC-003) |
| a domain-event handler throws | the transaction aborts; the caller is told the write failed. Different from before, where the row survived — acceptable only because these handlers publish and nothing else |
| the flush fails after a successful commit | rows and messages are committed; delivery is retried from the outbox. Nothing is lost |
| the process dies between commit and flush | the messages are in the outbox; the recovery agent picks them up (FR-003, SC-002) |
| a message can never be delivered | retried, then dead-lettered durably and countably (FR-010) |

The third and fourth rows are the ones that did not previously have an answer.
They are now the same answer, which is the point.
