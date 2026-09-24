# Plan 232 — The bucket one wall empties

**Spec**: [spec.md](./spec.md) · **Issue**: #2221 · **Phase**: 2 (Plan)
**Engineer**: `infra-engineer` · **Reviewer**: `infra-reviewer`

## 1. Shape

No bounded context, no domain model, no messaging, no contract. Three files change, one is new:

| File | Change | Story |
|---|---|---|
| `e2e/kiosk-shows-a-label-over-video.spec.ts` | Record each iteration's submit status; append it to the "never painted" refusal | US1 |
| `tests/Integration.Tests/AppHostGatewayRateBudgetTests.cs` (new) | Model test: run mode → override present at the plan's value; `E2ETests=true` → absent | US2 |
| `src/AppHost/AppHost.cs` | `apiGateway.WithEnvironment("RateLimiting__PermitLimit", …)` under `isRunMode && !isE2ETests`, with the derivation as a comment | US2 |

Boundary rules are unaffected: nothing crosses a context; `Shared.Contracts` untouched; the
NetArchTest suite has nothing new to see.

## 2. The value, and why it is not a hide

**Derivation.** Per four-tile live wall, ≈ 246 gateway requests/min at rest (spec §2 table). A
local run at the default worker count on an 8-logical-core machine is 4 workers
(Playwright: half the logical cores); a 16-core developer machine is 8. Budget for **12
concurrent four-tile walls** (8 workers, some specs holding two pages — the span test holds a
kiosk and an operator console; `wall-withdrawal` holds two screens) ≈ 2 950/min, ×2 margin ≈
**6 000 per minute, per replica** — without relying on the two replicas' round-robin halving it,
because DCP's balancing is not a guarantee the override should rest on. `Window` is left at the
1-minute default: the fix is the size of the bucket, not the shape of the window.

**Why run mode, and why not the fixture.** The dev stack and CI's Playwright job boot the same
run-mode shape (`AppHostE2ESwitchTests` pins that CI passes no `E2ETests`). The integration
fixture (`E2ETests=true`) must keep 100/min: `GatewayRateLimitIntegrationTests` exhausts it. The
gate is therefore `isRunMode && !isE2ETests`, the same conjunction the AppHost already uses for
the frontends.

**Why this is not `workers: 1` in disguise.** `workers: 1` removes concurrency to stay under a
budget the harness's topology cannot respect: a dozen simulated screens on one machine are one
source IP, which no production screen is *by design* (whether it is *in fact*, behind an Ingress,
is the production finding, spec §3). The override resizes the dev stack's bucket to the
dev stack's topology, leaves the production default at 100, and leaves every spec running
concurrently. The comment in `AppHost.cs` names the production issue so that, when that issue
changes the kiosk's spend or the partition key, the override is revisited rather than
fossilised. **What it gives up**, stated so the reviewer can weigh it: the dev stack no longer
reproduces a 429 from a single wall. That signal was only ever visible as a CI flake nobody read;
the production issue is where it gets a real test.

## 3. US1 — the refusal names the submit's status

Mirror the existing pairing (`:1306-1319`): the same `requestfinished` listener already filters
to `PUT …/system-variables/{name}/value` and keys by the body's `value`. Add a second map
`submitStatuses: Map<string, number>` filled from `(await request.response())?.status()` in that
listener — **plus** a `requestfailed` listener recording a failure for the same predicate, so a
network error is named too. At the `painted.t1 === null` refusal (`:1360-1366`) append
`submit answered <status>` / `submit failed: <errorText>` / `submit response unseen` after a
bounded wait reusing `settleSubmitRoundTrip`'s poll shape (do not add a new helper if the
existing one can be generalised by its value type — ADR-0036).

Do not change: any `expect`, `ITERATIONS`, budgets, the sample print format, the record written
by `writeRenderLegRecord`. Note `responseEnd < 0` returns early today — the status must be
recorded **before** that early return, since a 429 body may still time normally but a failed
request has no timing.

## 4. US2 — the override and its guard

**Test first** (`AppHostGatewayRateBudgetTests`): build the model with
`DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(args)` using
the `RunModeArguments` / `FixtureArguments` pair already defined in `AppHostReplicaCountTests`
(copy, do not extract — ADR-0109 contention; those files are not edited), find `api-gateway`,
and read its environment through the resource's environment-callback annotations (the engineer
chooses the Aspire accessor available at the pinned version — memory: a plan's SDK claim needs
checking against the pinned DLL). Two facts: run mode → `RateLimiting__PermitLimit == "6000"`;
fixture → key absent. `[Trait("Category", "FixtureLogic")]` like its siblings (builds the model,
starts nothing, safe beside a live stack). Failure messages carry the derivation's pointer, the
way `WhepAuthorizeCeilingTests` does, so a later change is told where 6 000 came from.

**Then** the one `WithEnvironment` call, placed after the `WithReplicas(2)` block and gated on
`isRunMode && !isE2ETests`.

## 5. Stop gate between US1 and US2

T002 must show the refusal naming **429**. Anything else — a 409 `VARIABLE_STALE`, a 5xx, a 2xx
with still no mutation — means §2's diagnosis is wrong; the lane stops, labels `agent:blocked`
with the verbatim refusal, and US2 is not applied. Also recorded by T002: whether
`--project=kiosk` alone fails at the default worker count (the spec's prediction).

**Stack availability.** A local run needs the host ports free. On 2026-09-24 another worktree's
persistent containers (`*-2d55740f`, e.g. Postgres on `127.0.0.1:16304`) blocked this worktree's
boot with `port is already allocated`. Stopping them is a user decision (memory: stopping a
shared stack needs the user). **Fallback that needs no local stack:** push US1 alone; CI's
`kiosk-*` shard refuses on the first attempt in ~7 of 9 runs today, and with US1 the refusal
names the status. One CI run is likely, not certain, to show it — re-run once if it does not.

## 6. Verification (phase 5)

Spec §8 steps 1-4. Evidence quoted in the PR: the pre-fix refusal naming 429 (local or CI log),
the model test's red, two post-fix local runs at the default worker count (33 passed each), and
the PR's CI shard log showing one span attempt with no refusal.

## 7. Risks

- **The override masks the production finding in dev.** Mitigated by the comment naming the
  separate issue; accepted in writing in §2.
- **6 000 is still too small on a larger machine.** A 32-core machine runs 16 workers; the margin
  covers ~24 walls. If it trips, the US1 refusal now says `429` immediately, which is the point
  of doing US1 first.
- **Round-robin across the two replicas changes** (#2283 returns the gateway to one replica): the
  derivation already assumes no halving, so no change is needed.
