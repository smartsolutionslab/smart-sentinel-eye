# Research: Every journey has a beginning, not just the ones from the plant floor

**Feature**: `027-trace-background-publishers` · **Phase 0** · 2026-08-22

The checklist named two things to look at rather than reason about. Both were
looked at. **The first changes the design**; the second is partially observed and
its remaining gap is named rather than closed by argument.

---

## Finding 1 — the loop is wrong for a second reason, not just the obvious one

`StreamHealthWatcher.PollOnceAsync` calls the command handler for **every camera,
every poll**, whether or not anything changed:

```csharp
foreach ((Guid cameraGuid, MediaMtxPath path, StreamState state) in streams)
{
    ...
    await handler.HandleAsync(command, cancellationToken);
}
```

The change detection is not in the loop. It is in the aggregate, and it guards
each raise:

```csharp
if (previous != StreamState.Healthy)
{
    Raise(new StreamHealthChangedDomainEvent(...));
}
```

*(`Stream.cs`, three sites — healthy, degraded, offline.)*

**So a journey started in the loop would be created for every camera on every
poll**, the overwhelming majority of which changed nothing. That fails FR-006 and
SC-005 outright — not by merging journeys, but by inventing them for work that
did not happen.

The spec called the loop tempting because it merges. It is worse than that: **the
loop is wrong for two independent reasons here**, and only one of them was
anticipated.

### Where it goes instead

`StreamHealthChangedDomainEventHandler` — the same seam spec 026 used, for the
same reasons and one extra:

| | in the loop | in the domain event handler |
|---|---|---|
| One journey per camera | merged into one per poll | ✔ falls out of the dispatcher |
| Only when something changed | ✘ every camera, every poll | ✔ the aggregate already guards it |
| Matches the worked example | ✘ | ✔ `EventIngestedDomainEventHandler` |

**Nothing needs to be added to make FR-006 and SC-005 hold.** They hold because
the aggregate already decides what is worth announcing, and the handler runs
once per announcement. The tests assert that rather than establish it.

---

## Finding 2 — audit retention is a different shape and needs the same rule

`AuditRetentionHostedService` has no domain event handler to put anything in. It
loops over chunks and publishes inline:

```csharp
foreach (AuditChunk chunk in chunks)
{
    await ArchiveAndDropAsync(deps, chunk, cancellationToken);
}
```

and inside that, per chunk: archive, build `AuditChunkArchivedV1`, publish, commit.

So here the journey **does** go inside the loop — around one chunk's work, in
`ArchiveAndDropAsync`. That is not a contradiction of Finding 1: the rule is *one
journey per announcement*, and here one iteration is one announcement. In the
watcher one iteration is usually **no** announcement, which is exactly why the
same rule puts the journey somewhere else.

**This is the call site that gets skipped.** Someone copying spec 026's change
looks for a domain event handler, does not find one, and moves on. The two sites
look different and follow the same rule, which is the opposite of the pattern
most people would infer from 026 alone.

There is already a guard worth noticing: `ArchiveAndDropAsync` wraps its work in
`try`, so the failure path FR-004 needs is a place that exists rather than one to
be introduced.

---

## Finding 3 — the nine "fine" publishers, and what is still inferred

The spec classifies nine publishers as needing nothing because a request or a
message already establishes their cause. **That was inference, and the checklist
flagged it.** What is now observed:

- **Message-driven — observed directly.** Spec 026's trace
  `195d91230e630d835afd39ffc1132890`: audit-observability's and
  layout-composition's receive spans are children of automation's *receive* span.
  A handler's publish does inherit the message being handled.
- **HTTP-driven — observed one layer short.** Spec 026's trace
  `ed21f2fcfe87ff241cfbe4817062ce8b`: a `POST /rules/` Server span in automation
  with two Keycloak Client spans as **children**. So an ASP.NET Core request does
  establish an ambient activity, and work done during the request attaches to it.

**What is not yet observed is a `send` span specifically attaching to a Server
span**, because no HTTP-driven publish happened to be captured. The mechanism is
the same one — whatever is in progress at the moment of publishing — and it is
now seen under both hosting models, but the last inch is inference.

**Left as inference deliberately, and cheap to close**: the verification walk
boots the stack anyway, so registering one camera and looking at the trace costs
nothing extra. It is a task, not an assumption, and **nothing in this feature
depends on the answer** — if an HTTP publish turned out to be an orphan too, that
would be a new finding and a new issue, not a change to these two call sites.

---

## What this means for the plan

1. **Watcher: the domain event handler.** Per-announcement and only-on-change
   both fall out of existing structure; the tests assert the structure rather
   than defend it.
2. **Retention: inside the loop, per chunk.** Same rule, opposite-looking place.
   Say so in the code, because the asymmetry is the thing a reader will trip on.
3. **Failure marking at both**, using the handle spec 026's review added. The
   retention site already has the `try` to hang it on.
4. **Close the HTTP inference during verification**, as a task with an owner, not
   a footnote.

**No new machinery.** `IJourneyOrigin` is registered for every context already.
If this grows past two call sites and their tests, the diagnosis is wrong.
