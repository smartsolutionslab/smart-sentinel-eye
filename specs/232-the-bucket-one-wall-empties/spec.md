# Spec 232 — The bucket one wall empties

**Issue**: #2221 · **Branch**: `fix/2221-e2e-worker-contention` · **Phase**: 1 (Specify)
**Date**: 2026-09-24 · **Base**: `396c4fa7` (branch cut from `origin/develop`)
**Context**: test harness + dev/CI stack composition — `e2e/kiosk-shows-a-label-over-video.spec.ts`,
`src/AppHost/AppHost.cs`, one new AppHost model test. No bounded context, no domain, no
`apps/` change, no `src/ApiGateway` change.
**Engineer**: `infra-engineer` · **Reviewer**: `infra-reviewer`
**Lane**: autonomous (ADR-0144)
**Phase 4a colour**: **RED** (§7). The span test goes from refusing to passing, and the AppHost
model gains a value it did not have.
**ADRs**: ADR-0106 (the gateway owns rate limiting — `per-fab` policy, source-IP fallback),
ADR-0153 (api-gateway's two-replica exception; #2283 owns its fate), ADR-0108 (browser e2e runs
against a live Aspire stack), ADR-0109 (disjoint files), ADR-0036 (smallest change),
ADR-0139 (new behaviour observed red), ADR-0144 (the lane)
**Constitution**: §IV — **N/A to the budget itself**: no leg's code path changes. The span test
that *measures* two legs (event → overlay state, composite + render) is the victim, and after
this change it produces a figure on its first attempt instead of a refusal.
**New ADR needed**: **No** (§9). The production-shaped finding (§3) may need an ADR-0106
amendment; that belongs to the separate issue, not here.

---

## 1. The issue's premise, re-checked — two of its three claims do not hold

| Claim in #2221 | Finding |
|---|---|
| "It is a realtime-channel / wall-spec concurrency interaction … the pair" | **Not the pair. The request rate.** The wall specs share nothing with the span test: they sign in as `wall-munich`, not `operator`; they never touch `readLiveVideoWall()`'s variable, overlay or layout; the realtime hub fans out per fab group (`LayoutLifecycleHub.cs:45-51`, `SignalRLayoutLifecycleBroadcaster.cs:112-123`) with no per-subscriber state a second screen could steal. What the wall specs add is **more gateway traffic from the same source IP** (§2). |
| "CI … can never fail" / "`workers: 1` is CI-only" | **False. CI fails it on the first attempt in 7 of the 9 develop runs since `b8c14eb9`, at `workers: 1`,** and `retries: 2` hides it (table below). One run failed outright. |
| "Fails sooner on parent `9956f7d6`" | Consistent with a rate cause — how far the loop gets depends on where in the limiter's fixed window it starts. Both commits post-date `b8c14eb9`. |

### CI evidence (e2e shard that runs `kiosk-*`, `origin/develop`, `workers: 1`)

Read from `gh run view --job <id> --log`, grepping `[span] REFUSED` and the attempt count.

| Run | Head | Attempts that reached the loop | First-attempt refusal | Result line |
|---|---|---|---|---|
| 35781904723 … 35883494479 (5 runs) | before `b8c14eb9` | 1 | none | 21 passed |
| 35894668870 | `b8c14eb9` | 2 | iteration 5, **0 label mutation(s)** | 3 flaky |
| 35918329596 | | 2 | iteration 4, 0 mutations | 2 flaky |
| 35920524319 | `859426c8` | 2 | iterations 8 and 7, 0 mutations | **1 failed** |
| 35925050239 | | 1 | none | 1 flaky |
| 35932737838 | | 2 | iteration 7, 0 mutations | 1 flaky |
| 35934330075 | | 2 | iteration 7, 0 mutations | 1 flaky |
| 35936415099 | | 2 | iteration 9, 0 mutations | 1 flaky |
| 35940399208 | | 1 | none | 1 flaky |

**Onset is exactly `b8c14eb9` — "widen the fixture wall to the domain's four-tile ceiling"
(spec 225 US2).** Five clean runs before it; seven first-attempt refusals in nine runs after it.
Nothing about concurrency changed at that commit. The number of live tiles did, from one to four.

## 2. Root cause — the gateway's per-partition budget, emptied by the wall's own traffic

**Diagnosed from source and CI logs; the 429 itself is not yet observed** (a local stack could
not be booted — another worktree's persistent containers hold the host ports, e.g. Postgres
`127.0.0.1:16304`). Confirming it is therefore this spec's first task (T001–T002) and a **stop
gate**: if the refusal names anything other than 429, the fix in US2 is not applied.

**The budget.** `src/ApiGateway/Program.cs:29-44`, `appsettings.json:20-24`: a fixed-window
limiter, `PermitLimit` 100 per 1-minute window, `QueueLimit` 0, on every REST route. Partition
key (`Program.cs:95-105`): the `X-Fab` header, else `ip:{RemoteIpAddress}`. **No client sends
`X-Fab`** (`grep -rn X-Fab apps/` — zero hits), so every browser in an e2e run — kiosk, wall,
operator console — is one partition, `ip:127.0.0.1`. The gateway runs two replicas in run mode
(`AppHost.cs:655`), each with its own in-process limiter, so the effective ceiling is ~200/min.

**The spend.** A mounted kiosk tile calls the gateway on timers, regardless of anyone acting:

| Source | Cadence | Per tile per min |
|---|---|---|
| `useGetStreamQuery(…, { pollingInterval: 5000 })` — `CameraViewer.tsx:113-115` | 5 s | 12 |
| `receive_to_decoded` POST — `CameraViewer.tsx:29, 213-241` → `kioskLatency.ts:110` | 5 s, live only | 12 |
| `presentation_buffer` POST — `CameraViewer.tsx:49, 263-327` (kiosk passes `onLagMeasured`, `CellPage.tsx:208`) | 2 s, live only | 30 |
| `wall_skew` POST — `useWallAlignment.ts:35, 134-205` | 2 s, per wall | 30 / wall |

**One four-tile live wall ≈ 4 × 54 + 30 ≈ 246 requests/min** — over the ~200/min ceiling by
itself. The one-tile wall before `b8c14eb9` spent ≈ 84/min, well under it.

**The victim.** The span test (`kiosk-shows-a-label-over-video.spec.ts:1195`) submits a variable
value through the operator console — a `PUT …/system-variables/{name}/value` through the same
gateway, same partition. Once a replica's window is spent, that PUT is refused with 429, the
value never changes, no `ResolvedOverlayTextChanged` is pushed, and the kiosk observer records
**0 label mutations** — exactly the refusal text (`:1360-1366`). The kiosk's label path itself
(SignalR, ADR-0106 "realtime stays direct") never touches the gateway, which is why the symptom
is *nothing at all* rather than *slow*.

**Why local default workers makes it deterministic.** Every other concurrent kiosk or wall page
adds its own tiles' polling and telemetry to the same partition (wall specs open the first
published layout; the picker lists the seeded walls). At `workers: 1` the budget is exceeded
intermittently; at the local default it is exceeded continuously. **The prediction this makes,
and T002 checks: `--project=kiosk` alone at the default worker count also fails** — it is the
count of concurrent screens, not the kiosk/wall pair.

## 3. The production-shaped finding — out of scope here, reported for a separate issue

The harness did not invent this. **One four-tile wall, doing nothing but showing video, spends
more than the gateway's default per-partition budget**, and because browsers never send `X-Fab`
the partition is the source IP: every screen and console behind one egress address — a fab NAT,
or a k3s Ingress with no forwarded-headers handling, where `RemoteIpAddress` is the ingress pod —
shares one bucket. When it empties, operator writes are refused with 429 alongside the
telemetry. This is distinct from #2283 (the partition key is caller-chosen and the limiter is
per-replica), though it shares its root in `ResolveFabPartition`. It is **not fixed here**
(orchestrator instruction; ADR-0144 forbids the lane making the ADR-0106 call).

## 4. User stories

### US1 (P1) — A span refusal names why the value never arrived

As an engineer reading a red span test, when an iteration refuses because the value never
painted, the refusal says what the operator's submit was answered with (e.g. `submit answered
429`) — so "0 label mutation(s)" is never again the whole story.

### US2 (P1) — The span test measures on its first attempt, at any local worker count

As an engineer running `pnpm exec playwright test --project=kiosk --project=wall` locally at the
default worker count — or CI running it at one — the span test's submits are not refused by the
dev stack's gateway budget, so it produces a figure instead of a refusal, without `workers: 1`,
without skipping a spec, and without touching the production default.

**US2 is conditional on US1's evidence** (§2 stop gate).

## 5. Acceptance scenarios

### US1

```gherkin
Scenario: a refused iteration names the submit's status              # conflict / diagnosis
  Given the span test's operator submit for value "SPANn" was answered 429
  And the kiosk saw no label mutation within the iteration's budget
  Then the refusal reads "... (0 label mutation(s) were seen; submit answered 429)"

Scenario: a submit that never finished says so                        # bad request / unseen
  Given no response for "SPANn" was recorded before the refusal
  Then the refusal reads "...; submit response unseen"

Scenario: a passing run is unchanged                                   # happy
  Given every iteration painted
  Then no refusal is produced and every printed sample line is byte-identical in shape to today's
```

### US2

```gherkin
Scenario: the dev/CI stack's gateway budget covers its simulated screens   # happy
  Given the AppHost is composed in run mode without E2ETests
  Then api-gateway's environment carries RateLimiting__PermitLimit = the value in plan §2

Scenario: the integration fixture keeps the production default            # conflict guard
  Given the AppHost is composed with E2ETests=true
  Then api-gateway's environment carries no RateLimiting__PermitLimit override
  And the gateway rate-limit integration tests still exercise 100/min

Scenario: the production default is untouched                              # bad-request guard
  Then src/ApiGateway/appsettings.json still reads PermitLimit 100, Window 00:01:00
```

Auth: **N/A** — no scope, token or policy changes. The override widens a *rate* on a dev stack;
authorization on every route is unchanged.

## 6. Functional requirements

- **FR-001** The span test records the HTTP status of each iteration's `PUT …/value`, keyed by
  the value the body carried (the existing `requestfinished` listener's pairing rule,
  `:1306-1319`), and appends it to the "never painted" refusal. No assertion, threshold or
  sample line changes.
- **FR-002** In run mode **without** `E2ETests`, the AppHost sets `RateLimiting__PermitLimit`
  on `api-gateway` to the value derived in plan §2, beside a comment stating the derivation and
  naming the production issue (§3) as the reason it may be removed.
- **FR-003** Under `E2ETests=true` no override is set; `appsettings.json` is not edited;
  `src/ApiGateway/Program.cs` is not edited; `WithReplicas(2)` is not touched (#2283's call).
- **FR-004** No change to `playwright.config.ts`'s `workers`, `retries` or project graph; no
  spec skipped, serialised or tagged.

## 7. Phase 4a colour — RED

- **US1**: the observed red is the refusal *before* FR-001 lacking any status (quoted from a
  run), and after FR-001 naming one. It is observed on the unfixed stack, which is also the
  root-cause confirmation (T002).
- **US2**: a new AppHost model test asserting FR-002/FR-003 fails on `396c4fa7` (no override
  exists) — observed red, quoted verbatim. The e2e span test is the behavioural red: refusing
  at the default worker count before, measuring after; it must pass **unmodified** by US2.

## 8. Independent end-to-end test procedure

Requires a stack slot: stop any other worktree's persistent stack first (a user action — see
the report). Then, from this worktree:

1. **Before US2** (after US1): `pnpm exec playwright test --project=kiosk --project=wall` at the
   default workers → the span test refuses; the refusal names `submit answered 429`. Repeat with
   `--project=kiosk` alone → same (confirms "the count, not the pair"). Record both verbatim.
2. **After US2**: restart the AppHost (env is read at boot), rerun both commands **twice**
   (memory: the first run after churn lies) → span test passes; all 33 tests pass.
3. `--workers=1` → still passes (no regression of the one configuration that used to pass).
4. CI on the PR: the `kiosk-*` shard's log shows `[span] iteration 0:` **once** (no retry) and
   no `[span] REFUSED`.

## 9. Latency budget

**N/A** — no leg's code path changes. The span figure this test prints is read, not altered;
a first-attempt figure replaces a retry's, which is if anything a more honest sample.

## 10. ADR check

No new decision. ADR-0106 already puts limits in configuration "tuned … at deploy time"; a dev
stack's composition setting one is the same move the AppHost already makes for
`WhepAuthorizeRateLimiting__*` under `isE2ETests` (`AppHost.cs:438-443`). The *production*
budget question (§3) may amend ADR-0106 — flagged to the separate issue.

## 11. Out of scope

- Any change to the kiosk's telemetry cadence, polling, batching, or the gateway partition key
  (the production finding, §3; #2283).
- `wall-survives-a-lockout.spec.ts`'s own, already documented, local-parallel hazard: it locks
  `wall-munich` while sibling `wall-*` specs sign in as it (its header, "CI-only guarantee").
  Not the failure #2221 reports (that is 0 label mutations, not a sign-in refusal); worth its own
  issue if local wall runs keep tripping it.
- Replacing `retries: 2` in CI, or making CI surface first-attempt refusals (#2077's summary
  already counts flakes).
