# Data Model: An event is never accepted until it is stored

**Feature**: `020-durable-ingest-ack` | **Date**: 2026-08-18

## No schema change

No table gains a column. No entity gains a field. No migration is written.

This is the second feature in a row with nothing to migrate, and for the same
kind of reason as spec 019: the defect is not in what is stored but in **when
the system claims to have stored it**. That claim has no representation in the
database at all — it lives in the moment an acknowledgement is sent.

## What actually changes: the lifetime of an acknowledgement

```text
Today

  broker delivery ──▶ parse ──▶ channel.Write ──▶ ACK ✅ ──▶ (loop) ──▶ INSERT
                                                   ▲                      │
                                        promise made here          may fail here
                                                                          │
                                                                    envelope dropped

  HTTP request ─────▶ parse ──▶ channel.Write ──▶ 202 ✅ ──▶ (loop) ──▶ INSERT
                                                   ▲                      │
                                        promise made here          may fail here
```

```text
After

  broker delivery ──▶ parse ──▶ channel.Write ──────────▶ (loop) ──▶ INSERT ──▶ ACK ✅
                                                                        │
                                                                     may fail
                                                                        │
                                                          not acknowledged ──▶ redelivered

  HTTP request ─────▶ parse ──▶ INSERT ──▶ 201 ✅
                                   │
                                may fail
                                   │
                                 5xx ──▶ caller knows
```

The promise moves to the right of the write on both paths. Everything else in
the picture is unchanged.

## The in-memory buffer stops being a hole without becoming durable

The 5 000-slot channel survives untouched, and this is the part worth stating
plainly because it looks like an omission.

An envelope sitting in that channel is, after this change, **something nobody
has been promised**. The broker has not been acknowledged; the HTTP caller was
never routed through it. So losing the buffer to a crash loses nothing the
system claimed to have: the broker still holds its copy and redelivers.

The buffer did not need to become durable. It needed to stop holding promises.

## Entities

- **Event** — unchanged in every respect: same fields, same identifier, same
  partitioning, same fab.
- **Envelope in flight** — gains a **completion signal**: the means to report
  that this envelope is now stored, or is permanently unstorable. For a broker
  delivery that signal is the acknowledgement; for a direct submission it is
  the HTTP response, and such envelopes no longer enter the channel at all.
- **Dead letter** — unchanged in shape (spec 006, plus the fab from spec 018),
  newly used for a second reason: a delivery that could not be stored after a
  bounded number of attempts, rather than only one that could not be parsed.
  The distinction is in `error`, which already carries the reason.

## Reads and writes this introduces

| Operation | Where | Frequency |
|---|---|---|
| Batch insert of events | persistence loop | once per batch instead of once per event — **fewer** writes than today, not more |
| Dead-letter insert | persistence loop, poison escape only | rare; zero in a healthy system |
| Duplicate suppression by identifier | existing ingest handler | unchanged in code, **far more often** in practice |

The last row is the one to watch. Nothing about idempotency changes, but a path
that was exercised occasionally becomes one that runs after every outage and
every restart, which is why the plan asks for it to be proven rather than
trusted.

## Deliberately not modelled

- **A durable ingest queue.** It was one of the four options and was not
  chosen; the reason it is not needed is above.
- **A persisted retry counter.** The bound on attempts lives in memory and
  resets on restart, which costs a few extra attempts and avoids a durable
  write per failed attempt — on the path that is failing precisely because
  writes are failing.
- **Any record of "acknowledged".** There is deliberately no state saying an
  event was confirmed: the acknowledgement is an action, not a fact to store,
  and storing it would create a third thing that can disagree with the other
  two.
