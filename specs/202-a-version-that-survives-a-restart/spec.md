# Spec 202 — A version that survives a restart

- **Issue:** #2426 (`bug`, `agent:ready`, Project #13 → Todo — verified 2026-09-21)
- **Branch:** `2426-overlay-version-restart`
- **ADRs:** ADR-0037 (workflow), ADR-0145 (§1 — the version high-water mark the
  kiosk keeps), ADR-0113 (versions as a concurrency vocabulary), ADR-0102
  (integration-event envelope), ADR-0139 / constitution §Testing (red first),
  ADR-0084 (code metrics), ADR-0088 (Wolverine per-module transactions),
  ADR-0105 (guards), ADR-0103 (Aspire fixture, no Testcontainers).
  Constitution §IV (latency budget — `event → overlay state`).
- **Explicitly *not*:** ADR-0153 / #2423. See "What this is not" below.

## Problem

`ResolvedOverlayTextChangedV1.Version` is documented on the wire as *"a
monotonic per-overlay counter the kiosk uses to discard out-of-order frames"*
(`src/Shared.Contracts/SystemVariables/ResolvedOverlayTextChangedV1.cs:20`).
It is not monotonic. It is a per-process `ConcurrentDictionary` that starts
again at 1 every time the SystemVariables host starts.

The kiosk trusts the documented property as a strict drop filter. So the first
real change after an ordinary redeploy is published as version 1, the kiosk
compares it against the mark it still holds from before the redeploy, and drops
it — and goes on dropping every subsequent change until that many further
changes to *that one overlay* have accumulated. For a slow-moving variable that
is not a delay, it is an indefinite freeze: a green live-updates badge over a
stale plant-floor figure.

This is live today, at one replica, on the system's most latency-sensitive path.

### Premise check — the issue's claims, re-verified at HEAD `20ecb430`

| Claim | Verdict |
|---|---|
| `InMemoryReverseIndex.cs:24` holds `ConcurrentDictionary<Guid, long> versionByOverlay` | **Confirmed**, verbatim |
| `:72-76` — `NextVersionFor` starts at 1 and increments; `CurrentVersionFor` returns 0 when unknown | **Confirmed**, verbatim |
| `ReverseIndexSeederHostedService` restores label references but no version | **Confirmed** — `:95` calls only `UpsertOverlayReferences(id, text)` |
| The kiosk drop filter is `message.version <= (versions.get(...) ?? 0)` | **Confirmed**, but at `CellPage.tsx:269`, not `:262`. The line moved; the code is identical |
| `GetOverlaySnapshotQueryHandler` reads `CurrentVersionFor`, also reset | **Confirmed** at `:41` |
| No persistence of `versionByOverlay` exists anywhere | **Confirmed** — `grep -rn 'NextVersionFor\|CurrentVersionFor\|versionByOverlay' src/ tests/` returns hits only for the interface, the singleton, an in-memory test fake, and three call sites. No table, no column, no migration, no cache |
| A SystemVariables restart does not disconnect the kiosk, so `onReconnected` never fires | **Confirmed** — the hub is LayoutComposition's (`SignalRLayoutLifecycleBroadcaster`), a different process |

**Two findings the issue did not have, and both of them change the answer.**

**Finding A — the REST leg of this defect is latent, not live.** The kiosk never
reads `snapshot.version`. `ResolvedOverlaySnapshot.version` is declared in
`apps/shared/src/api/systemVariables.api.ts` and consumed by nobody: the only
`.version` reads in kiosk source are `message.version` at `CellPage.tsx:269`,
`:270` and `:287` — all three the *push*. The tile reads `snapshot?.resolvedText`
and nothing else (`CellPage.tsx:484`). So `CurrentVersionFor` returning 0 after a
restart harms nothing *today*, and it is a loaded gun: the server publishes a
number it documents as meaningful, and the first consumer to believe it inherits
the bug. It is in scope for that reason — not because a wall is stale because
of it.

**Finding B — the snapshot handler reads its version after resolving its text,
which is the wrong order.** `GetOverlaySnapshotQueryHandler.cs:40-41` resolves
`resolvedText` and *then* reads the version. A push committing between those two
lines produces a snapshot carrying older text stamped with the newer push's
version — the kiosk then drops that push as not-newer and the tile is frozen on
text the server has already superseded. Same failure mode, different cause,
one line apart. Fixed here because the fix is an ordering swap, and leaving it
would mean shipping a version the client is newly told to trust while it can
still be wrong.

### What this is not

**ADR-0153 and #2423 do not cover this.** ADR-0153 is about *two instances
diverging*; its SystemVariables row cites `InMemoryReverseIndex.cs:14,22-24` —
the label-reference dictionaries and the per-process seeder — and its clause 3
obligation is triggered by *raising a replica count*. The word "restart" does not
appear in it as a failure mode. #2423 is the standing backlog record for that
table and is explicitly not actionable until clause 3's two-instance harness
exists. This spec fixes a single-replica, restart-triggered defect that would
still be present if ADR-0153 were fully discharged tomorrow. Conversely, this
spec does **not** discharge SystemVariables' row in that table: the label cache
at `:14,22-24` stays per-instance and stays listed.

## Locked tech choices

Nothing new is chosen. Everything below already exists in this context.

| Concern | Choice | Precedent |
|---|---|---|
| Durable store | PostgreSQL via `SystemVariablesDbContext` | ADR-0000 row 009 |
| Access shape | Application-layer interface, Infrastructure impl, raw parameterised SQL | `IVariableValueRequestDedupStore` / `VariableValueRequestDedupStore.cs` — same context, same folder |
| Atomicity | one `INSERT … ON CONFLICT … DO UPDATE … RETURNING` statement | `VariableValueRequestDedupStore.cs:26-31` uses `INSERT … ON CONFLICT DO NOTHING` |
| Migration | EF Core migration in `SystemVariables/Infrastructure/Persistence/Migrations` | five already there |
| Guards | `Ensure.That(x).IsNotNull()` | ADR-0105 |
| Async | `CancellationToken` last, no `ConfigureAwait` | ADR-0049 |
| Tests | xUnit + Shouldly; integration via `AspireFixture`, real resource restart | ADR-0052, ADR-0103; `RestartLosesNothingIntegrationTests.cs` |

**No ADR is required and none is written here.** The wire contract already
promises monotonicity; this makes the implementation meet the promise it already
documents. No new architectural primitive is introduced — in particular *not* a
Postgres sequence, for which the repo has no precedent (`grep -rn 'nextval\|HasSequence' src/`
→ zero hits) and which would be an architectural choice this lane may not make.
If a reviewer judges a durable counter table to be ADR-class, that is ADR-0155
and a blocked outcome, not a silent decision taken here.

## User stories

### US1 (P1) — A wall keeps updating after SystemVariables redeploys

**As** a fab operator watching a kiosk wall,
**I want** an overlay's live text to keep updating after the SystemVariables
service is redeployed,
**so that** the figure on the wall is the figure in the plant, and a working
badge means a working wall.

This is the whole slice. One bounded context (SystemVariables), no frontend
change, no wire-contract change, and observable end to end in one sitting:
change a variable, watch the wall, restart the service, change it again, watch
the wall again.

There is no US2. Splitting the push path from the REST path would ship a
half-fix over one shared store, and the REST half is the one with no live
consumer to notice it was left broken.

## Acceptance scenarios

SC-1 and SC-2 are the reason this spec exists.

### SC-1 — the push path survives a restart (happy)

```gherkin
Given overlay X references variable {{throughput}}
  And a kiosk is attached and has received pushes for X up to version N
 When the system-variables service is restarted
  And an operator sets {{throughput}} to a new value
 Then the ResolvedOverlayTextChangedV1 published for X carries a Version
      strictly greater than N
  And the kiosk's drop filter accepts the frame
  And the tile renders the new value
```

### SC-2 — the REST snapshot path survives a restart (happy)

```gherkin
Given overlay X has been pushed up to version N
 When the system-variables service is restarted
  And a client GETs /system-variables/snapshot?overlayIdentifier=X&fabId=<fab>
 Then the response's version is not lower than N
```

### SC-3 — the cutover deploy is not itself an instance of the bug

```gherkin
Given a kiosk holds an in-memory mark of N for overlay X, issued by the
      old per-process counter before this change was deployed
 When this change is deployed and an operator changes a referenced variable
 Then the first Version issued for X exceeds any value the old counter could
      have reached in a process lifetime
  And the kiosk accepts that first frame without a page reload
```

*Rationale, because an unexplained floor is exactly the kind of thing that
rots:* a durable counter starting from zero on an empty table reproduces #2426
once, on the very deploy that fixes it — and a kiosk is a browser, so nothing
reloads it when a server restarts. The first version an overlay is ever issued
must therefore start above the reach of the counter being retired.

### SC-4 — concurrent changes to one overlay get distinct, increasing versions (conflict)

```gherkin
Given two variables {{a}} and {{b}} are both referenced by overlay X
 When both are changed concurrently, in two separate requests
 Then the two ResolvedOverlayTextChangedV1 frames for X carry two different
      versions
  And neither is lower than any version previously issued for X
```

### SC-5 — an unknown overlay is still a 404 (bad request)

```gherkin
Given an overlay identifier the reverse index does not hold
 When a client GETs /system-variables/snapshot for it
 Then the response is 404
  And no counter row is created for it
```

### SC-6 — the snapshot still requires its scope (auth)

```gherkin
Given a caller whose token lacks sse.variables.read
 When it GETs /system-variables/snapshot
 Then the response is 401 or 403
  And no version is disclosed
```

### SC-7 — a version is never newer than the text it stamps (Finding B)

```gherkin
Given a snapshot request for overlay X
 When a variable referenced by X changes while that request is in flight
 Then the version the snapshot returns is not greater than the version of the
      push carrying that change
  And the kiosk therefore accepts the push rather than dropping it as stale
```

## Independent end-to-end test procedure

Runnable by a person against the Aspire stack, without reading the test suite.
This is what phase 5 walks.

1. Boot the AppHost. Open a kiosk wall on a layout with a tile whose overlay
   label embeds `{{name}}` for some defined variable.
2. In management-web, change that variable's value **three times**, a few
   seconds apart. Confirm the wall's tile changes each time. (This puts the
   overlay's version at 3 or more, and the kiosk's mark with it.)
3. In the Aspire dashboard, **restart the `system-variables` resource** and wait
   for it to go healthy.
4. Change the variable once more.
5. **Expected:** the tile updates. **Today it does not** — that is the bug, and
   step 5 is the whole verification.
6. `curl` the snapshot with a token carrying `sse.variables.read`:
   `GET /system-variables/snapshot?overlayIdentifier=<X>&fabId=<fab>`.
   **Expected:** `version` is at least 4 — not 0, and not 1.
7. Repeat steps 3–5 once more. The second restart must behave like the first: a
   counter that survives one restart by accident is not a counter that survives
   restarts.

## Latency budget

**Leg affected: `event → overlay state` (RabbitMQ + projection), budget ≤ 200 ms**
(constitution §IV). The change is on it, and the impact is real and must not be
buried:

- **What is added.** The push fan-out gains **one** database statement per
  variable change — not one per affected overlay. A single
  `INSERT … ON CONFLICT DO UPDATE … RETURNING` over the whole set of affected
  overlay identifiers returns every new version in one round trip, so the added
  cost is constant in the fan-out width.
- **Against what.** That handler already performs a `SELECT` per sibling
  placeholder (`VariableValueChangedDomainEventHandler.cs:124`) and an outbox
  `INSERT` per published event (ADR-0088), on the same pooled connection inside
  the same ambient transaction. One further statement is sub-millisecond against
  a 200 ms leg — but "sub-millisecond" is a prediction, and this spec does not
  accept predictions as evidence.
- **How it is discharged.** `tests/Integration.Tests/SystemVariables/NFR_VariableResolutionLatencyTests.cs`
  is re-run **twice** (the first run after machine churn reads exactly like a
  regression) and **its printed median is quoted in the PR body**, before and
  after. Its own 800 ms assertion is four times the budget and would not catch a
  regression of this size on its own — the figure is the evidence, not the green
  tick.
- **The REST snapshot path is not on this leg.** It is the opening-label fetch a
  tile makes when it attaches, not the event path. Its added cost is nil in any
  case: the version read it already performs becomes a DB read instead of a
  dictionary lookup, and moves one line earlier.

**§VII dashboard obligation, stated rather than quietly skipped.** `event →
overlay state` is an implemented leg, so it is subject to §VII's dashboard rule
(ADR-0117), and §IV records it as *"recorded, not yet readable"* (#1707), with
the Dashboard column `no` for every leg and the question of what satisfies it
explicitly unsettled (#1940). This spec does not discharge that and does not
claim to; it is a standing, tracked condition that predates this work by many
specs, and flagging it is the honest handling.

## Out of scope

- **Re-keying the counter on `(fab, overlay)`.** `CellPage.tsx:222-239` records
  this as a latent hazard and spec 067 already carries it as a follow-up. Doing
  it here would activate ADR-0145 §1's suppression scenario in the same change
  that fixes a different suppression bug. The key stays the overlay alone.
- **Any kiosk change.** The client's filter is correct given a server that does
  not regress. See plan.md §2, "Why not the client".
- **Highlight frames.** `OverlayHighlightedNotification` carries no version at
  all (ADR-0145 notes this); giving it one is new behaviour, not this bug.
- **ADR-0153 / #2423.** Unchanged, still open, still listing this context.
- **Garbage-collecting counter rows for archived overlays.** Deliberate — see
  plan.md §4. Deleting a row resets that overlay to zero and reintroduces #2426
  for any kiosk still holding a mark.

## File contention

Single context. Nothing here is touched by PR #2493 (spec 201, ServiceDefaults
idempotency) or by any other open branch.

- `src/SystemVariables/Application/Resolution/IReverseIndex.cs`
- `src/SystemVariables/Application/Resolution/IOverlayTextVersions.cs` *(new)*
- `src/SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs`
- `src/SystemVariables/Application/EventHandlers/VariableArchivedDomainEventHandler.cs`
- `src/SystemVariables/Application/Queries/Handlers/GetOverlaySnapshotQueryHandler.cs`
- `src/SystemVariables/Infrastructure/Resolution/InMemoryReverseIndex.cs`
- `src/SystemVariables/Infrastructure/Persistence/OverlayTextVersionStore.cs` *(new)*
- `src/SystemVariables/Infrastructure/Persistence/Migrations/*` *(new)*
- `src/SystemVariables/Infrastructure/SystemVariablesPersistenceModule.cs`
- `tests/SystemVariables.Application.Tests/**`, `tests/SystemVariables.Infrastructure.Tests/**`,
  `tests/Integration.Tests/SystemVariables/**`

## Success criteria

1. The end-to-end procedure above completes with the tile updating at step 5 and
   the snapshot reporting at least 4 at step 6 — twice.
2. An integration test restarts the real `system-variables` Aspire resource and
   proves both SC-1 and SC-2. It is **observed red** first, and the failure is
   quoted in the PR.
3. No version state remains in any process-lifetime field in SystemVariables.
4. `NFR_VariableResolutionLatencyTests`' median is quoted before and after, from
   two runs each.
5. Domain ≥ 90%, Application ≥ 80% coverage gates hold (ADR-0065).

## Assumptions, marked

- **A1.** The counter's key stays `overlay`, not `(fab, overlay)` — matching
  today's behaviour exactly. Marked because `CellPage.tsx:222-239` argues the
  current key is itself a latent problem; this spec preserves it deliberately.
- **A2.** A floor for a first-ever version is sufficient for the cutover
  (SC-3). It assumes no single SystemVariables process has ever issued more
  than the floor's worth of pushes for one overlay. plan.md §3 states the figure
  and why it cannot have been reached.
- **A3.** Retaining a counter row for an archived overlay is correct. Marked
  because `VariableResidueCleanupTests` shows this repo otherwise cleans up
  after itself; here the residue is the fix.
