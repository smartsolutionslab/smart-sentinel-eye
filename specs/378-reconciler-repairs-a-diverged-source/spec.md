# Spec 378: The reconciler repairs a MediaMTX path whose source has diverged

**Feature Branch**: `fix/2960-reconciler-repairs-source-mismatch` (cut from `origin/develop` at `68a927ac`)

**Created**: 2026-10-16

**Status**: Implemented. No `[NEEDS CLARIFICATION]` remains. The owner's decision on
#2960 settles *what* (compare and repair; do not serialise repoints). Two judgement calls the
decision presupposed but did not make are marked **[ASSUMPTION]** in §5: the sweep **cadence**
(the reconciler is startup-only today, so "the next sweep" needs a period) and the
**integration-lane override** that keeps spec 099's outage technique valid. Both are reversible
configuration values, not new architecture; the owner may change either at review.

**Input**: Issue [#2960](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2960),
*A retried MediaMTX patch can land after a newer repoint and leave the wrong source*. Found by the
security review of #2958.

**Owner decision (2026-10-15, on #2960), recorded here and not re-opened:** make the reconciler
compare sources and repair a mismatch; do **not** serialise repoints. `MediaMtxReconciler`
additionally reads each path's configured source and, when it differs from the database's current
source for that stream, patches it back. Only the database's current value is ever written, so a
stale retry can never win for longer than one sweep. The same mechanism covers the HTTP-retry race,
the outbox-retry race on the MediaMTX side, and the `add/` follow-up `GET` that confirms existence
but not source.

**Out of scope, filed separately:** the outbox retry of an older `CameraAddressChangedV1` also
rewrites the **aggregate** back to the older address (DB says A while CameraCatalog says B). This
spec makes MediaMTX converge on the database; it cannot make the database right. Filed as
**#2991** (tech-debt, not `agent:ready`, on Project #13).

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (a path whose source differs from the
database is now re-pointed; nothing re-pointed it before): phase 4a is **red** (ADR-0139). An
**adversarial pass is required** (`test-adversary`): the failure mode is a race.

**Spec number.** 378. Checked 2026-10-16 against `origin/develop` (highest: 376), every remote
head, every local branch head (`fix/2977-redact-camera-url-in-audit` holds 377, unmerged) and the
open PR branches: nothing claims 378. Re-check before the PR is opened.

**ADRs.** ADR-0171 (every MediaMTX control-API call authorizes through `/streams/authorize`; why a
gateway call may not run before Kestrel binds, and why per-path probing is ruled out), ADR-0143
(the MediaMTX gateway opts back in to `RetryEveryMethod()` — the retry that opens the race),
ADR-0050 (logging), ADR-0103 (no in-memory provider / Testcontainers: the DB read is separated from
the decision so it can be unit-tested), ADR-0105 (`Ensure.That`), ADR-0139 / ADR-0144 (red first;
the lane implements, it does not decide), ADR-0036 (smallest change). Specs 099, 309, 318, 351,
373 are prior art and are cited where they bind. **No new ADR is needed**: the owner made the one
decision there was to make; the cadence is a configuration value of the mechanism it chose.

**Latency budget.** N/A. The sweep is control-plane (a background reconcile against MediaMTX's
config API), not on any constitution §IV leg. A repair *does* restart the path's source pull in
MediaMTX, which a viewer sees as a brief reconnect — that is the wrong picture being replaced by
the right one, not a regression of the steady-state path.

---

## 1. Premise, verified (not assumed)

**MediaMTX exposes the configured source.** The pinned image is `bluenviron/mediamtx:1.21.0-ffmpeg`
(`src/AppHost/AppHost.cs:183`). Read at tag `v1.21.0` (Docker is down locally; read from the
upstream source at that exact tag):

- `internal/api/api_config_paths.go` — `onConfigPathsList` and `onConfigPathsGet` serialise
  `conf.Path` after `conf.Redact(...)`. `GET /v3/config/paths/list` returns
  `{ itemCount, pageCount, items: [conf.Path...] }`; each item carries `"name"` and `"source"`.
- `internal/conf/path.go:222` — `Source string \`json:"source"\``.
- `internal/conf/redact.go` — `Redact` touches only `AuthInternalUsers[].Pass`, `PublishPass` and
  `ReadPass` (defaults and per path). **`source` is not redacted.**
- `internal/conf/path.go:486-600` (`validate`) — the source is checked by **case-sensitive prefix**
  (`strings.HasPrefix(pconf.Source, "rtsp://")`) and `validateURL` is called for its error only; the
  parsed URL is discarded. **The source is stored and returned verbatim**: no normalisation of case,
  default port, trailing slash or credentials.

**Consequence for comparison: ordinal string equality, no normalisation.** Every writer of a
canonical `cam-{guid}` path's source writes the database's own string: `ProvisionStreamCommandHandler`
(`stream.SourceUrl.Value`), `MediaMtxReconciler`'s re-add (`sourceUrl.Value`), and
`RepointStreamCommandHandler` (`rtspSourceUrl`, which `StreamSourceUrl.From` accepted unchanged —
`StringValueObject` stores the value as given). So after one repair MediaMTX echoes exactly the
database string, and the next sweep sees equality: **no patch storm**. Normalising would be wrong,
not merely unnecessary — `rtsp://cam/a` and `rtsp://CAM/a` are different strings MediaMTX dials
differently, and a normaliser would hide exactly the divergence this spec repairs.

**The reconciler is one-shot today.** `MediaMtxReconciler` (`Infrastructure/Reconciler/MediaMtxReconciler.cs`)
runs `ReconcileOnceAsync` once, deferred to `ApplicationStarted` (ADR-0171 ordering), and never
again. It lists paths (`ListConfiguredPathsAsync` reads `name` only and discards `source`), removes
orphans, re-adds missing paths, and **never compares sources**. The only periodic loop in the
context is `StreamHealthWatcher` (2 s), which reads the **runtime** list (`/v3/paths/list`), whose
`source` is a `{type, id}` descriptor, not the configured URL — so it cannot do this comparison
without a second endpoint. The issue's description is accurate.

**The race, as found.** `RepointStreamCommandHandler.cs:54` saves the aggregate then calls
`MediaMtxRtspGateway.RepointPathAsync` (`PATCH /v3/config/paths/patch/{name}`, `:64`), whose client
retries every method (ADR-0143). A first attempt that times out client-side may still be applied
server-side after a newer repoint's PATCH: MediaMTX points at A while the database says B, and
nothing ever corrects it. The `add/` 400 branch (`MediaMtxRtspGateway.cs:40-48`) succeeds on an
existing path without checking its source — the same divergence, entered through provisioning.

**Why it matters (stream attribution).** The WHEP hook authorizes a viewer by path → stream → fab.
A path pulling the wrong camera shows that camera's picture under another camera's tile and
authorization — possibly another fab's camera. The bound this spec buys is "at most one sweep".

## 2. User stories

### US1 (P1) — A diverged source is repaired by the next sweep

As an operator, when a stale retry leaves a camera's SFU path pulling a different address than the
one StreamDistribution has on record, the system puts the recorded address back within one sweep,
so a tile never shows another camera's picture for longer than that.

**Independent end-to-end test.** With the integration stack up: provision a stream (DB row with
source `S_db`), point its MediaMTX path at `S_other` directly through the control API, run one
reconcile/sweep pass, then `GET /v3/config/paths/get/cam-{camera}` and observe `source == S_db`.
Run the pass again and observe **no** PATCH was issued (the source is already equal).

```gherkin
Feature: The reconciler repairs a diverged MediaMTX source

  Scenario: Happy path — a diverged source is put back
    Given a non-retired stream whose persisted source is "rtsp://10.0.8.1/h264"
    And its MediaMTX path is configured with source "rtsp://10.0.8.2/h264"
    When a reconcile sweep runs
    Then the path's configured source is "rtsp://10.0.8.1/h264"
    And one Information line "Reconciler repaired MediaMTX path cam-… source rtsp://10.0.8.2 -> rtsp://10.0.8.1." is logged

  Scenario: Converged — an equal source is left alone (no patch storm)
    Given a stream whose MediaMTX source equals its persisted source byte for byte
    When two consecutive sweeps run
    Then no PATCH is sent

  Scenario: Ordinal, not normalised — a case or trailing-slash difference is a divergence
    Given a persisted source "rtsp://cam-a/h264" and a MediaMTX source "rtsp://CAM-A/h264/"
    When a sweep runs
    Then the path is patched to "rtsp://cam-a/h264" exactly once
    And the following sweep sends no PATCH

  Scenario: Conflict — a repoint commits while the sweep is repairing
    Given the sweep read the persisted source "A" and MediaMTX reports "X"
    And a repoint commits "B" and its own PATCH lands before the sweep's PATCH of "A"
    When the sweep's PATCH of "A" completes
    Then the sweep re-reads the committed source, finds "B", and patches "B"
    And the path ends the sweep on "B", the database's current value

  Scenario: Conflict — a repoint commits between the MediaMTX read and the database read
    Given the sweep reads MediaMTX ("A") before it reads the database
    And a repoint commits "B" in between
    When the sweep compares
    Then it writes "B" (the value it read from the database), never "A"

  Scenario: Conflict — a stale retry lands after a sweep
    Given a sweep has converged the path on "B"
    And a delayed retry of an older PATCH then sets "A"
    When the next sweep runs
    Then the path is back on "B"

  Scenario: Bad request — MediaMTX refuses the repair
    Given MediaMTX answers the repair PATCH with a non-success status
    When a sweep runs
    Then a Warning naming the path is logged, the other paths are still compared, and the sweep does not throw

  Scenario: Scope — paths the sweep does not own are untouched
    Given a canonical path with no database row, a retired stream's leftover path, a non-canonical path, and a stream whose path is absent from MediaMTX
    When a sweep runs
    Then none of them is patched, added or removed by the sweep

  Scenario: Auth — the sweep's control-API calls authorize like every other gateway call
    Given the sweep is hosted in StreamDistribution
    When the host starts
    Then no sweep gateway call is made before ApplicationStarted (ADR-0171)
    And every sweep call carries the gateway client's Basic credential (no new client, no new credential)
```

### US2 (P1, same slice) — Sweep cadence is periodic and bounded

The repair runs at startup (as the final step of the existing startup pass) and then every
`MediaMtxSourceRepair:Interval` (default **10 s**, §5) for the life of the host, stopping cleanly on
shutdown. Not separately shippable from US1 — without it the bound is "until the next restart".

## 3. Functional requirements

- **FR-001** `IRtspGateway` gains a read of every canonical path's **configured source**, from
  `GET /v3/config/paths/list` (paginated exactly as `ListConfiguredPathsAsync` is). The value is
  returned verbatim; a canonical item whose `source` is absent or not a string is omitted (it cannot
  be compared, so it is not repaired).
- **FR-002** A repair pass reads **MediaMTX first, then the database** (non-retired streams: path,
  source). For each path present in both whose sources differ **ordinally**, it patches the path to
  the database's value through the existing `RepointPathAsync`.
- **FR-003** After each repair PATCH returns, the pass re-reads that stream's **committed** source
  and state. If the stream is now retired, or the path is gone, it stops. If the committed source
  differs from the value just written, it patches the committed value **once more** and stops — it
  never loops. (The check is on the *source*, not the aggregate version: health reports bump the
  version every few seconds, and a version check would re-patch spuriously.)
- **FR-004** The pass writes **only** the database's value, never a value read from MediaMTX.
- **FR-005** The pass never adds or removes a path; orphan removal and re-add stay the startup
  pass's, unchanged.
- **FR-006** `MediaMtxReconciler.ReconcileOnceAsync` runs the repair pass as its **last** step. Its
  existing removal and re-add behaviour, log lines and summary line are unchanged (C7, spec 351).
- **FR-007** A hosted sweep runs the repair pass every `MediaMtxSourceRepair:Interval` after
  `ApplicationStarted`, never before it (ADR-0171), and stops on `ApplicationStopping`. A failed pass
  is logged at Warning and the next tick tries again; it never takes the host down.
  `MediaMtxSourceRepair:Interval` is validated at startup to lie between **1 s** and 4294967294 ms.
  The ceiling is `PeriodicTimer`'s range. The 1 s floor exists because a shorter interval makes
  passes run back to back and exhausts the shared MediaMTX control-API rate partition (2000/min),
  which also serves health, provisioning, repoint and retire. The binding is registered at the top
  level of `AddStreamDistributionInfrastructure`, directly after `BindStreamFabAttribution`, not
  inside it.
- **FR-008** Every new log line that names a source renders it with `UrlRedaction.ToOrigin`
  (spec 373). No new log line carries a full URL, path segment or query.
- **FR-009** A per-path repair failure (any exception except cancellation of the caller's token) is logged at Warning with the path
  and does not abort the pass.
- **FR-010** No change to `Shared.Contracts`, to any other context, to `StreamSourceUrl`, to
  `AddPathAsync`'s behaviour, or to `RepointStreamCommandHandler`.

## 4. Non-goals

- Serialising repoints per camera (rejected by the owner).
- The aggregate-rewrite half of the outbox race (#2991).
- Making orphan removal or re-add periodic.
- Detecting that a camera *itself* serves the wrong picture (only configuration divergence).

## 5. Judgement calls

- **[ASSUMPTION] Cadence: 10 s default.** The owner's bound ("no longer than one sweep") requires a
  recurring sweep that does not exist. Cost per tick: one `config/paths/list` call per 100 paths
  (3 at the 250-camera target) through the rate-limited `api` partition (2000/min production) —
  18 calls/min against the health sweep's 90. 10 s keeps the wrong-camera window to the same order
  as the 2-8 s retry window that opens it. This is the smallest option consistent with the decision:
  it adds a loop, not a mechanism. Not folded into `StreamHealthWatcher`: that loop reads the runtime
  list, which has no configured source, so it would need a second endpoint every 2 s.
- **[ASSUMPTION] Integration lane: `MediaMtxSourceRepair__Interval=49.17:02:47.2940000`.** Spec 099's
  `StreamHealthTransitionTests` and `UnresolvedFabAuditRowIntegrationTests` provoke an outage by
  patching a MediaMTX path away from its recorded source — exactly the divergence this spec now
  repairs. At 10 s the sweep would race their 15 s budgets and flake; the value is the largest interval `PeriodicTimer` accepts (~49.7 days), because any finite interval shorter than a shard can tick inside those outage windows. The lane override (gated
  `isE2ETests`, precedent: `AuditObservability__Retention__TickInterval`) keeps that technique
  valid; the repair itself is exercised in the integration lane by calling the pass directly, and
  the cadence by a unit test. The Playwright e2e boot (`dotnet run`) keeps the production default.
  Alternative rejected: re-engineering spec 099's outage provocation needs a second controllable
  RTSP source in the AppHost — a larger change than this spec.
- **Hosted as a sibling, not inside `MediaMtxReconciler`'s lifecycle.** A loop inside the existing
  deferred pass would change its pinned lifecycle (`MediaMtxReconcilerStartupOrderingTests`: "runs
  once", the startup-failure log) and its constructor (four call sites in tests). The owner's
  "MediaMtxReconciler additionally reads" is honoured by FR-006; the periodic half lives beside it
  in `Infrastructure/Reconciler/`.

## 6. Acceptance

1. Every Gherkin scenario in §2 has a test, and each new-behaviour test was observed red first.
2. C7 (`MediaMtxReconcilerPassCharacterisationTests`) passes with **no assertion edited**.
3. `MediaMtxReconcilerStartupOrderingTests`, `MediaMtxReconcilerIntegrationTests`,
   `StreamHealthTransitionTests` and `UnresolvedFabAuditRowIntegrationTests` pass unmodified.
4. `security-reviewer` has reviewed the change (stream attribution).
