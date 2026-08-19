# Verification: An integration event is never lost after its write commits

**T027** — [quickstart.md](./quickstart.md) walked. "Done" is the observations,
so they are here rather than a tick.

Observed on 2026-08-19 against the real Aspire stack.

**Read this first.** This feature is invisible when it works. Every happy-path
test in the repository passes identically before and after it, so nothing below
is a demonstration that something succeeded — each step breaks something on
purpose, and what is recorded is what broke.

## 0. The mechanism was already paid for, and unreached

The finding that shaped the whole feature. ADR-0088 mandates a Postgres outbox,
and `WolverineDefaults` genuinely configures it:

```csharp
opts.PersistMessagesWithPostgresql(postgresConnection, outboxSchema);
opts.UseEntityFrameworkCoreTransactions();
opts.Policies.AutoApplyTransactions();
```

`AutoApplyTransactions` enrols messages published **from inside a Wolverine
message handler**. None of the nine write paths is one — they are HTTP endpoints
and hosted services — so nothing was enrolled and every announcement left its
transaction immediately.

So no outbox was built. The work was reaching the one already running.

## 1. A write and its announcement share a fate (SC-001, SC-003)

`OutboxSharesTheWritesFateTests`, and the second case is the one that matters:

```
A_write_that_commits_leaves_nothing_owed        pending 0 after the write drains
A_write_that_cannot_commit_leaves_no_message_behind
    dropped events_hamburg — the write can no longer commit
    POST /events/manual -> 503
    stored: 0
    pending messages before: 0   after: 0
```

**Capturing the announcement early is only safe if the rollback discards it.**
If it did not, a message would sit in the outbox waiting to tell eight other
contexts about an event that does not exist — and unlike the defect being fixed,
there would be no row to reconcile against afterwards. A false announcement is
worse than a lost one.

The unit cases (`EventRepositoryOutboxTests`) assert the ordering itself, and
both fail against the previous commit-then-announce arrangement; I checked by
restoring it.

## 2. The gap is closed in every context (SC-004)

Nine repositories changed, one or two per bounded context.
`OutboxCoversEveryContextTests` asserts the per-context half in CameraCatalog —
that its writes go through **its own** schema, `wolverine_camera_catalog` — and
drains to zero.

The part that belongs to the shared seam rather than to any one context is
proven once, in EventIngestion. Proving it nine times would be re-testing the
same three lines.

`OutboxCommitTests` is what keeps it true: no repository may call
`SaveChangesAsync` directly. **The first version of that rule was wrong and
passed.** I broke a repository on purpose to check, and it stayed green —
`SaveChangesAsync` is declared on `DbContext` itself, so `IsSubclassOf` excluded
the only declaring type it ever has. `IsAssignableFrom` catches it, and breaking
the repository again now fails exactly one assembly by name. A guard nobody has
watched fail is not a guard.

## 3. Two publishes that had no write to ride on

The integration suite found this and it is the most useful thing it did: 225
passed, one failed — AuditObservability's retention sweep, *"the aged chunk was
not archived + dropped + announced within 30s"*.

The contract note had predicted exactly this and I had not swept for instances:

> A publish outside a write has no transaction to join… anything publishing
> without an accompanying write is outside this feature's guarantee and should
> be looked at on its own.

Two were:

- **`AuditRetentionHostedService`** announces an archived chunk and writes
  nothing through EF. Its message was captured with nothing to release it.
- **`RotateWebhookClientCommandHandler`** publishes *after* its save — the very
  defect this feature fixes, one layer above the repositories. Reordering is not
  available: the announcement carries a client id that only exists once Keycloak
  has answered, and the save-then-Keycloak order is load-bearing for reasons its
  own comment gives. So it flushes explicitly.

Automation's `FabEventIngestedV1Handler` is a Wolverine message handler and was
already covered. Checked, not assumed.

`IEventBus` now carries this on the interface, with both incidents named,
because the signature cannot express that publishing captures rather than sends
— which is why it kept catching people, including me, twice, inside the feature
written to fix it.

## 4. The backlog is visible, and FR-008 asked for something that does not exist

`OutboxBacklogIsVisibleTests` pins the tables and columns the health check reads,
and the reason it exists is a defect it found in my own code.

FR-008 asks for "how many, and how long the oldest has waited". Asking the
database what is actually there:

```
wolverine_event_ingestion: wolverine_outgoing_envelopes, wolverine_incoming_envelopes,
                           wolverine_dead_letters, wolverine_nodes, …
outgoing columns: id, owner_id, destination, deliver_by, body, attempts, message_type
```

**There is no enqueue timestamp.** The age of the oldest pending announcement is
not obtainable. I had guessed `execution_time` in both the check and its test;
the test failed honestly with `42703`, and **the check swallowed the identical
error and reported Healthy** — a backlog monitor that would have said "no
announcements are waiting" for ever, about an outbox nobody was watching.

Three corrections, all recorded rather than quietly made:

- the check reports `max(attempts)`, which answers what the age was a proxy for
  and answers it better — a message queued long ago and delivered first time is
  not a problem; one on its fifth attempt is. **This is a deviation from FR-008
  as written.**
- the swallow is narrowed to connection failures, where the database's own check
  owns the alarm. Anything with a SQLSTATE is this check being broken and now
  says so.
- the check stopped adding load to what it monitors. It called
  `OpenConnectionAsync` with no matching close, so a connection was held per
  readiness probe, across nine services, against the Postgres that Keycloak and
  every context share.

`wolverine_dead_letters` exists and is asserted, which is FR-010: a message that
can never be delivered is recorded durably and countably by Wolverine.

## 5. Throughput, latency and order (SC-005)

`IngestThroughputMeasurementTests`, the same harness spec 020 left, run against
both builds:

| | before (spec 020) | after |
|---|---|---|
| offered | 60 175 in 151.4 s = **398/s** | 60 534 in 150.7 s = **402/s** |
| stored | 60 175 of 60 175 | **60 534 of 60 534** |
| sustained end to end | 398/s, never behind | **393/s**, never behind |
| arrival→visible p50 | 164 ms | **146 ms** |
| p95 / p99 | 6 968 / 10 371 ms | **1 260 / 6 116 ms** |
| per-source order | 0 inversions / 40 sources | **0 inversions / 40 sources** |

FR-011 and FR-012 hold. The tail improved rather than degraded, which is
consistent with what the change does: it removes a synchronous broker hop from
the write path and replaces it with rows in a transaction that was already open.

The cost is real and is rows: a batch of 200 events now writes 200 event rows
and 200 outbox rows in one commit. They are short-lived and deleted on delivery.

**As with spec 020, this harness cannot reach the 5 000/s spec 006 sizes for** —
each publisher waits for its own acknowledgement, so ~400/s is what it can
offer. That figure is not established by this feature in either direction. What
is established is the comparison SC-005 asks for.

## 6. The suites

```
Domain / Application / Infrastructure / Architecture   all green
Integration        228 passed, 1 skipped, 0 failed
Coverage           all 20 gates pass (ADR-0065)
Build              0 warnings, 0 errors
```

Two notes on the runs themselves. The Aspire fixture failed to boot twice, both
times on a run started immediately after a previous one — 223 Polly timeouts in
under two minutes, which is a stack that never came up rather than 223 defects.
A re-run on a settled machine passed. And the MQTT `CONNECT→CONNACK` p50 breached
its 15 ms budget (17.58 ms) on the run before the health-check connection leak
was fixed, and passed after. That is consistent with the leak but not proof of
it.

## What this feature does not do

**It does not cover a publish with no accompanying write, automatically.** Such
a publish must flush itself. Two call sites do; a third added later will have to
know, and only the interface documentation tells them — this is the sharpest
edge the feature leaves behind.

**It does not deduplicate or order.** At-least-once, as FR-004 says. Consumers
already dedupe by identifier where it matters (spec 006 FR-002).

**It does not report the age of a pending announcement**, because the data does
not exist. See §4.

**SC-002 has no CI coverage.** `OutboxSurvivesAKillTests` carries
`Category=Disruptive` for the reason spec 020 recorded: the Aspire restart
command fails outright on the CI runner, and a test that goes red on a platform
limitation teaches people to ignore red CI. It is verified locally and by hand.
