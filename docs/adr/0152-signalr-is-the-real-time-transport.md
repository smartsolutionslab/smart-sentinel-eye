# ADR-0152: SignalR is the real-time transport

**Status:** **Accepted**
**Date:** 2026-09-16
**Supersedes:** ADR-0076 (replaceable real-time transport)
**Superseded by:** —

## Context

ADR-0076 decided that "the real-time transport is replaceable behind an
abstraction", specified **native WebSocket** for v1, named **Server-Sent Events**
as the v2 candidate, and listed SignalR under rejected alternatives:

> **SignalR** — auto-reconnect, hubs, groups — but heavier and couples to the
> framework's protocol envelope.

The repository ships SignalR. This ADR resolves that, and it resolves it in
SignalR's favour — not to ratify a fait accompli, but because the evidence
gathered for issue #2400 says ADR-0076 was wrong about the thing it cared most
about.

### How the divergence happened

Not silently, and not over a long period. ADR-0076 landed on **2026-05-25**, the
repository's first day. SignalR landed **two days later**, in `82ae74e9`
("kiosk-web + SignalR force-disconnect"). The divergence is **3.7 months**, not
the fifteen that issue #2400 and `surface.pin.ts` state — that figure is an
error inherited from a commit message and is corrected by this ADR's
implementation.

The decision path is recorded, and it is instructive:

- `specs/003-layout-composition/spec.md:467-470` — phase-1 Q&A "rejected polling
  and SSE in favour of ADR-0076 v1's WebSocket… **plan.md picks the .NET
  implementation (SignalR vs raw `Microsoft.AspNetCore.WebSockets`)**". The
  choice was knowingly deferred.
- `specs/003-layout-composition/plan.md:54` then picks SignalR, **citing
  ADR-0076 as its authority** — the ADR that rejects it.
- `plan.md:56` notices a deviation, but against ADR-0070 (minimal APIs), not
  ADR-0076.

**Nowhere does any artefact state a reason to prefer SignalR over raw
WebSockets, or acknowledge that ADR-0076 rejected it.** The likeliest reading —
inference, flagged as such — is that the plan's author read ADR-0076 as
"WebSocket transport", treated SignalR as an implementation of WebSocket
transport, and did not read the Alternatives section.

### The abstraction was never built, on either side

- `src/Realtime.Abstractions/` contains **zero `.cs` files** — a csproj listed in
  `SmartSentinelEye.slnx` and referenced by no other project. `IRealtimeChannel`
  and `IRealtimeTransport` have never existed as code.
- `apps/shared/src/realtime/index.ts` declares `RealtimeClient` with **zero
  implementors and zero consumers**; its `"./realtime"` export is imported
  nowhere.

So the replaceability ADR-0076 specified was not eroded. It was never had: the
interfaces were scaffolded on day one and the transport arrived two days later
without them.

What *does* exist is a narrower, real seam — `ILayoutLifecycleBroadcaster`, six
domain-shaped methods with one implementor. Spec 003 claimed it made a transport
swap "a single-class change". That is half true and worth stating precisely: it
covers the **send** half only. Connection lifecycle, group membership, the auth
handshake and the entire client sit outside it. `SignalRLayoutLifecycleBroadcaster`
is 152 lines of the ~1,200 a swap would touch.

### The finding that decides it

ADR-0076's stated purpose was that a v2 SSE transport could drop in. Nothing in
this repository constrains SignalR's transport negotiation — no
`HttpTransportType`, no `skipNegotiation`, and `AddSignalR()` is called with no
options. So **the shipped client already negotiates across WebSockets, SSE and
long polling, and falls back to SSE automatically when a proxy blocks the
upgrade.**

A native-WebSocket v1 built to ADR-0076's letter would have **no fallback at
all** until someone hand-wrote `SseRealtimeChannel` and `SseRealtimeClient` —
the work the abstraction existed to enable, and which in 3.7 months nobody did.

The two properties ADR-0076's rejection clause *credited* SignalR with —
auto-reconnect and groups — are the two this system now depends on:

- **Groups are load-bearing.** Every one of the six broadcasts is
  `Clients.Group(...)`; `Clients.All` appears nowhere. ADR-0145's fab isolation
  rests on it.
- **Auto-reconnect is load-bearing and has been extended**, with a custom retry
  ladder plus a hand-written loop for the two cases SignalR does not cover
  (initial-connect failure, `onclose`).

These are different properties with the same example, and the distinction is
what ADR-0076 missed: it wanted "SSE as an independently-authored channel behind
an interface"; SignalR provides "SSE as a negotiated fallback inside one
protocol". The second is weaker in principle and is the one that actually
exists.

### What is genuinely used, and what is not

Used: groups, automatic reconnection, typed hubs, the access-token factory,
negotiate. **Not used:** streaming, client→server invocation (the hub has no
server methods beyond `OnConnectedAsync`), MessagePack, backpressure options, a
backplane. It is a group-addressed, one-way, JSON server→client pipe with
resilient reconnect.

## Decision

Accepted 2026-09-16. Issue #2400 tracks the implementation; nothing in this ADR
changes runtime behaviour on its own.

**The status of the evidence, recorded because this ADR's central claim is
inferred rather than observed.** That SignalR falls back to SSE when a proxy
blocks a WebSocket upgrade is derived from configuration — no
`HttpTransportType`, no `skipNegotiation`, `AddSignalR()` with no options, and
the client package's own transport enum. **Nobody has watched it happen on a
network that blocks the upgrade**, and no such network exists in this repository
to test against (`deploy/` holds two Mosquitto files and no chart, Service or
Ingress). If that fallback ever needs to be relied upon rather than cited, it
should be observed first. The rest of the Context — the group and reconnect
dependencies, the empty abstractions, the decision path through spec 003 — is
read directly from code and history.

### 1. SignalR is the real-time transport. ADR-0076 is superseded

Not "tolerated pending replacement". The rejection in ADR-0076 rested on
"heavier" and "couples to the framework's protocol envelope"; the coupling is
real and is accepted here as the price of the two properties the system depends
on and of the fallback ADR-0076 wanted and would not have had.

### 2. The dead abstractions are retired

`src/Realtime.Abstractions/` (and its `.slnx` entry) and
`apps/shared/src/realtime/index.ts` (and its `"./realtime"` export) describe a
design that was never built and that this ADR abandons. Keeping them implies a
seam that does not exist.

This unblocks spec 162 §4 option (B), which was deferred precisely because
retiring them needed an ADR.

`ILayoutLifecycleBroadcaster` **stays** — it is a real seam with a real
implementor, and it is the thing a future transport change would actually pivot
on.

### 3. The replaceability claim is restated to what is true

The system has **transport negotiation within SignalR** (WebSockets → SSE → long
polling), not **transport replaceability behind an interface**. Documents
asserting the latter are corrected, not reinterpreted: `CLAUDE.md`'s stack
table, `CONTRIBUTING.md`'s description of interfaces that do not exist,
`.claude/agents/frontend-engineer.md`, and `ILayoutLifecycleBroadcaster`'s own
doc comment.

### 4. The scale-out defect is recorded, because neither ADR records it

The shipped fan-out is in-process `IHubContext` from Application event handlers,
with **no backplane** and a single-instance `layout-composition`. **A second
replica would silently drop frames for clients attached to the other instance.**

ADR-0076's design — a per-connection RabbitMQ subscription per kiosk — would not
have had this property. That is a genuine point against the implementation that
shipped, it is unrelated to SignalR-versus-WebSocket (a backplane fixes it), and
it is the one place where ADR-0076's rejected design was better. It gets its own
issue rather than a sentence here.

## Consequences

**A 3.7-month-old contradiction between the decision of record and the code is
closed**, in the direction the evidence supports rather than the direction the
older document happens to state.

**The v2-SSE story changes shape and gets weaker.** Today SSE is a negotiated
fallback we did not choose and do not test. Authoring an independent SSE channel
now means leaving SignalR, which is a larger change than ADR-0076 imagined. If
that property is wanted deliberately, it needs its own ADR and its own cost.

**Framework coupling is accepted explicitly.** `@microsoft/signalr` version
bumps are a live risk the e2e suite already exercises, and the client's reconnect
behaviour depends on SignalR's keepalive and timeout semantics to fire `onclose`
on a half-open socket.

**Retiring the abstractions removes an aspiration from the codebase.** A reader
will no longer find an interface implying a seam that does not exist — which is
the honest state — but also no longer finds the aspiration written anywhere
except this ADR.

**Nothing changes at runtime.** No production behaviour is altered by accepting
this; it is a record catching up with the code, plus deletions of unused
declarations.

## Alternatives Considered

**Rebuild v1 as a native WebSocket, honouring ADR-0076.** Priced rather than
dismissed: **~1,100-1,400 lines across ~22 files**, of which ~600 is new
hand-written connection-lifecycle infrastructure that SignalR currently supplies
— a thread-safe connection registry replacing groups, per-connection send
serialisation (`WebSocket.SendAsync` is not concurrency-safe), heartbeat and
timeout in both directions, a client reconnect state machine, token refresh on
reconnect, and envelope dispatch. There is **no in-repo precedent**:
`UseWebSockets`, `AcceptWebSocketAsync` and `System.Net.WebSockets` appear
nowhere in `src/` or `tests/`.

The dominant cost is not the production code — it is the **test corpus**: nine
integration files, 2,434 lines, 53 references, all using the off-the-shelf typed
`SignalR.Client`, which a raw-WS server replaces with a hand-rolled test client.
Plus three e2e specs that reason about negotiate-and-transport being two
requests.

Rejected because it spends that to *lose* the SSE fallback, re-implement
auto-reconnect and groups by hand, and deliver a property — interface-level
replaceability — that nothing has needed in 3.7 months and that the original
abstraction never actually provided.

**Keep both documents and let the drift stand.** Rejected: this repository has
corrected the same defect four times (§II twice, the Phase 3 board gate, §IV's
leg table), every time because a record nobody checked was assumed to match
reality. A decision of record that the code contradicts is worse than either
outcome, because it makes every future reader's premise wrong.

**Supersede ADR-0076 but keep the abstractions "for when we need them".**
Rejected: they have had zero implementors since the day they were written. An
interface with no implementor does not preserve optionality, it advertises a
seam that a reader will believe in — and spec 162 already found two dead tests
guarding exactly that fiction.

## Implementation Notes

- Do **not** implement before this ADR is accepted. Issue #2400 tracks it.
- **Correct the "fifteen months" figure** wherever it appears — issue #2400's
  body, `apps/shared/src/realtime/surface.pin.ts:33`, and the commit message
  lineage it came from. `client.spec.ts`'s history is 2026-05-25 → 2026-09-16.
- Retiring `apps/shared/src/realtime/index.ts` removes the subject of spec 163's
  `surface.pin.ts`. Retire the pin in the same change — a pin whose subject is
  gone is precisely the dead-assertion class specs 159-163 spent a week removing.
- `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs:401-407` registers
  `/hubs/layouts` as the sole route mapped outside the readable shapes, and
  `BoundaryTests.cs:147-155` asserts the gateway depends on no SignalR. Both
  name SignalR deliberately and both **stay**; this ADR makes them correct rather
  than incidental.
- `LayoutLifecycleHub.cs:8` claims "the kiosk (and management-web) connects";
  `management-web` does not, and its `/hubs` Vite proxy is dead configuration.
  Correct both while in the area.
- **The push hop is on §IV's `Event → overlay state` leg (200 ms), and no
  instrument measures it in isolation** — the only figures are 555 ms and 758 ms
  server-side end-to-end against a 5,000 ms regression ceiling, from a test that
  says in its own source that it cannot read the budget. Neither this ADR nor
  ADR-0076 has a latency argument available, and neither should pretend to one.
