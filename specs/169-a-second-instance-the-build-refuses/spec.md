# Spec 169 — A second instance the build refuses

**Issue:** #2404
**Branch:** `chore/2404-a-second-instance-the-build-refuses`
**Decisions of record:** **ADR-0153** (one instance per service, until a service
earns otherwise — `Accepted` 2026-09-16, merged to `develop`; this spec
implements its clause 1 and accommodates its clause 2). Secondary: **ADR-0152**
(SignalR is the real-time transport — §4 records the defect #2404 was filed
for), **ADR-0106** (single YARP gateway; the `WithReplicas(2)` this spec must
accommodate), **ADR-0103** (Aspire fixture, no Testcontainers), **ADR-0037**
(phases), **ADR-0144** (autonomous lane), **ADR-0086** (no `Co-Authored-By`).
**Constitution:** §Availability (already amended by ADR-0153's acceptance — see
§2.6); §IV latency budget — checked and **N/A**, see §7.

---

## 1. What this issue is, and what it is not

**The issue title is not the scope.** #2404 is titled *"SignalR fan-out is
single-instance with no backplane — a second replica silently drops frames"*
and asks for a backplane. **ADR-0153 clause 5 answers that with "no
backplane"**, deliberately, with reasons: a backplane buys horizontal scaling
for one context out of five that need it, adds a second datastore or bespoke
relay code against two standing ADRs on operational surface (ADR-0071,
ADR-0088), and — decisively — **cannot be verified**, because no lane runs two
replicas of any affected context, so a backplane wired backwards would pass the
entire suite.

ADR-0153 was accepted on the strength of that reasoning and records that
**#2404 tracks its implementation**. So this issue is now the ADR's
implementation slice, not its original question.

What the ADR leaves to be built:

| Clause | Content | This slice? |
|---|---|---|
| 1 | "No service in this system runs more than one instance." Wants **a guard, not a comment**. | **Yes** — this is the slice. |
| 2 | `api-gateway` is the known-broken exception at `WithReplicas(2)`; #2283 owns its fate. | **Accommodated, not decided.** |
| 3 | The two-instance harness. "The substantial piece of work here… sizing it is its own slice." | **No** — §3.2 says why, and re-scopes it. |
| 4 | §Availability amended. | **Already done** — landed with acceptance (§2.6). |
| 5 | No backplane for `LayoutComposition`. | **Nothing to build.** |

---

## 2. The premise, verified

Everything below was read at the tip of `develop` (`ed15de67`) on 2026-09-16,
not taken from the ADR. **One of the ADR's claims does not survive the check
(§2.5), and one of its table rows names the wrong mechanism (§2.3).**

### 2.1 `api-gateway` is still the only `WithReplicas(>1)` — HELD

`WithReplicas` appears **once** in the entire repository:

- `src/AppHost/AppHost.cs` — `apiGateway.WithReplicas(2);`, inside
  `if (!isE2ETests)`, under the comment block beginning
  `// HA (#1005): run >= 2 gateway replicas so the single REST front door is not a`.

**Cited by text, deliberately.** ADR-0153 on `develop` cites this as
`AppHost.cs:534-540`; the comment is actually at line 558 and the call at 563.
The citation was already stale when written, which is the defect **PR #2417**
(open, `docs/adr-0153-anchor-the-apphost-citation`) exists to fix. This spec
cites by text throughout and never by line number.

### 2.2 The five contexts and their per-instance state — HELD

Every row of ADR-0153's table was re-read against the code. All citations land:

- **LayoutComposition** — `AddSignalR()` bare, no chained configuration, in
  `LayoutCompositionInfrastructureModule.cs`. `AddSignalR` appears exactly once
  in the repo; **zero** hits for `AddStackExchangeRedis`, `AddAzureSignalR` or
  `backplane` anywhere; no Redis package in `Directory.Packages.props`. The
  broadcaster is a singleton wrapping in-process `IHubContext`. Wolverine's
  queue name is module-prefix + event type (`WolverineDefaults.cs`), so
  replicas are competing consumers.
- **Automation** — `AddSingleton<IRuleCache, InMemoryRuleCache>()`. The class's
  own doc comment still says *"For v1 we run one Automation instance per fab;
  once we scale to multiple instances…"*.
- **SystemVariables** — `AddSingleton<IReverseIndex, InMemoryReverseIndex>()`,
  three `ConcurrentDictionary` fields, seeded per process.
- **EventIngestion** — `ClientId` defaults to the constant `"event-ingestion"`
  and `MosquittoConnectionFactory` builds options with
  `.WithClientId(opts.ClientId)` and `.WithCleanSession(false)`. Two replicas
  presenting one id evict each other. Hard-blocked, as stated.
- **StreamDistribution** — `StreamHealthWatcher`'s `degradedSince` dictionary is
  per-process; `MediaMtxReconciler` and `StreamFabAttributionService` are both
  `IHostedService` startup mutators, registered unconditionally. **The delete is
  real**: the reconciler calls `gateway.RemovePathAsync(path, …)` inside an
  orphan loop whose `expected` set is *this process's* database read.

**One finding the ADR understates.** `StreamHealthWatcher`'s split clock is
worse than drift: the 5-minute `OfflineAfter` decision needs *continuous*
accumulated observation, so two instances each holding half the observations may
mean the Offline transition **never fires at all**, rather than firing late.

### 2.3 The Identity row names the wrong mechanism — MOVED (minor)

ADR-0153 groups **AuditObservability** and **Identity** as *"Timer-driven hosted
services with no leader election; each runs twice."*

- `AuditRetentionHostedService` — **correct**: `BackgroundService`,
  `PeriodicTimer`, loop, no leader election.
- `KioskPrivilegeSweepHostedService` — **not timer-driven**. It is a plain
  `IHostedService` whose `StartAsync` calls `SweepOnceAsync` once, with
  `StopAsync` returning `Task.CompletedTask`. It is a **one-shot startup
  mutator**, the same shape as StreamDistribution's two — and its own doc
  comment says so.

The conclusion ("each runs twice") survives; the mechanism label does not. This
changes nothing about clause 1 or this slice. It is recorded in §9 as a
docs-only correction to route elsewhere.

### 2.4 `OverlayDesigner` and `CameraCatalog` hold no per-instance state — HELD

Both trees searched for `AddSingleton`, `AddHostedService`, `IHostedService`,
`BackgroundService`, `PeriodicTimer`, `ConcurrentDictionary`, `IMemoryCache`
and static mutable fields. The only singletons are `IClock`/`SystemClock`,
`TimeProvider.System` (stateless) and `IMigrator` (resolved only by the separate
MigrationRunner process). No hosted services, no timers, no caches. Both
contexts only *publish* integration events and have no inbound Wolverine
listener, so the competing-consumer mechanism does not reach them.

### 2.5 **"No lane runs two replicas of anything" is FALSE** — MOVED

This is the premise correction, and it is load-bearing for clause 3.

ADR-0153 clause 3 justifies itself with: *"**no lane in this repository runs two
replicas of anything**, so a backplane wired backwards would pass the entire
suite."* The Consequences section repeats the framing, and the ADR's Context
says the two-replica mode is *"disabled under e2e so the tests resolve a single
endpoint."*

**The Playwright end-to-end lane runs `api-gateway` at two replicas today.**

The chain, each link read directly:

1. `AppHost.cs` gates the call on `if (!isE2ETests)`.
2. `isE2ETests` is `bool.TryParse(builder.Configuration["E2ETests"], …)` — it is
   **false unless something sets `E2ETests`**.
3. The e2e job boots with
   `dotnet run --project src/AppHost/… -c Release --no-build -- ScenarioSimulator=false`.
   It passes `ScenarioSimulator=false` and **nothing else**.
4. `E2ETests` appears nowhere in `.github/workflows/*.yml` except two explanatory
   comments, and in no `scripts/*.sh`. It is set as neither an argument nor an
   environment variable for that job.

So `isE2ETests` is `false` in the e2e lane and `WithReplicas(2)` applies.

**Where the ADR's error came from.** The `E2ETests` flag does not mean "the
Playwright end-to-end job" — it means *"this is the integration fixture"*.
`AppHostE2ESwitchTests` documents that trap explicitly and at length, because
the two switches are deliberately different: `E2ETests` also removes the three
Vite apps the Playwright suite drives. The AppHost comment's phrase *"Kept to
one instance under E2E tests"* reads as the Playwright lane and means the
integration lane. ADR-0153 inherited the ambiguity.

**What is actually true**, and is the sharper statement:

- No lane runs two replicas of any of the **five affected contexts**. (Holds.)
- No lane **observes or asserts** anything about multi-instance behaviour.
  (Holds — see §2.7.)
- The gateway's two replicas **do** run in CI, in the lane with the richest
  fixtures, and #2283's rate-limit split is live there with nothing watching it.

This does not weaken ADR-0153's decision — a backplane still could not be
verified, because the gateway is not LayoutComposition. It does change clause
3's cost estimate: the harness has a foothold the ADR believed absent.

### 2.6 §Availability was already amended — clause 4 is discharged

`.specify/memory/constitution.md` §Availability already carries the ADR-0153
amendment: **"One instance per service (ADR-0153)"**, the five-context list, the
`api-gateway` exception with #2283, and the zero-downtime clause explicitly
demoted to *"an aspiration, not a discharged requirement"*. It landed with the
ADR's acceptance. **Nothing to do here**, and no constitution edit is in scope.

This also partly discharges the ADR's note that *"each context in the table
deserves its own issue, or one issue with the table in it, so the list does not
live only in this ADR"* — the list now lives in the constitution as well.

### 2.7 Nothing in the repo asserts a replica count today — HELD

Repo-wide, `WithReplicas` / `ReplicaAnnotation` / `Replicas` appear only in
`AppHost.cs`, ADR-0153, spec 078, and two agent briefs. **No test in `tests/`
asserts a replica count, or "one instance", in any spelling.** Clause 1 is
today enforced by nothing at all.

---

## 3. Scope

### 3.1 In scope — clause 1's guard, and only that

One guard that fails the build when any service other than the recorded
exception is composed at more than one instance, plus the pointer from the
composition root to it.

### 3.2 Out of scope — clause 3's harness, and why

**Decision: clause 3's two-instance harness is a follow-up, not this slice.**
Four reasons, in order of weight:

1. **The ADR says so.** *"Sizing it is its own slice."* Sizing is design work
   with an unknown answer; this slice has a known one.
2. **It is not independently shippable here.** The harness needs a way to run
   two instances of a chosen context, route to both, and observe divergence —
   and for `LayoutComposition` specifically it needs two SignalR endpoints and a
   client on each. That is several slices, and §2.5 has just changed its
   starting assumptions.
3. **The ordering is the right way round.** The guard *enforces* one instance;
   the harness is what would *license an exception to it*. You do not need the
   harness to enforce the rule — you need it to lift the rule. Shipping the
   guard first costs the harness nothing and protects the interval.
4. **The guard is what makes the harness necessary rather than optional.**
   Today, adding a replica is silent. After this slice it fails the build with a
   message naming clause 3. The guard is the tripwire that routes the next
   person to the harness instead of past it.

### 3.3 Explicitly not in scope

- **A SignalR backplane.** ADR-0153 clause 5. Not a judgement call.
- **The `api-gateway` replica count.** ADR-0153 clause 2: choosing between a
  shared rate-limiter store and returning to one replica is **#2283's job**. The
  guard must pin the exception as it stands, and must not bless, revert or
  argue it. *If this slice starts deciding the gateway's fate, stop — that is a
  decision, and the autonomous lane may not make it.*
- **Any change to the five contexts' per-instance state.** No leader election,
  no shared cache, no idempotent mutators.
- **Any ADR or constitution edit** (§2.6; ADR-0144 forbids it, and PR #2417 is
  already open on `docs/adr/0153-*.md` — a second editor would conflict).
- **`deploy/`.** See §5.3.

---

## 4. User stories and acceptance scenarios

### US-1 (P1) — Adding a second instance fails the build

*As the engineer who reaches for `WithReplicas` during an incident, I am
refused at build time and told why, instead of silently corrupting five
contexts.*

This is the whole slice. It is independently shippable and observable end to
end today.

```gherkin
Scenario: the composition root as it stands passes
  Given the AppHost application model composed in run mode
  When the replica guard reads every resource's replica count
  Then api-gateway is composed at exactly 2 instances
  And no other resource is composed at more than 1 instance
  And the guard passes

Scenario: a second instance of an affected context is refused
  Given an engineer adds WithReplicas(2) to layout-composition
  When the replica guard runs
  Then the guard fails
  And the failure message names "layout-composition" and its count
  And the message cites ADR-0153 clause 3 as the route to an exception

Scenario: the refusal reads the model, not the source text
  Given an engineer sets a replica count through an indirection
        that no source-text search for "WithReplicas" would match
  When the replica guard runs
  Then the guard still fails and still names the resource

Scenario: the recorded exception is pinned to its count, not merely permitted
  Given an engineer raises api-gateway from 2 replicas to 3
  When the replica guard runs
  Then the guard fails
  And the message states that #2283 owns the gateway's replica count

Scenario: the integration lane still composes every service at one instance
  Given the AppHost application model composed with E2ETests=true
  When the replica guard reads every resource's replica count
  Then no resource is composed at more than 1 instance
  And api-gateway is present and composed at exactly 1

Scenario: a guard that observed nothing is a failure, not a pass
  Given the composed model contains no resources, or api-gateway is absent
  When the replica guard runs
  Then the guard fails, reporting that the scan is broken rather than passing
```

**Auth / bad-request scenarios are N/A.** This slice adds no endpoint, no
message, no domain type and no trust boundary. It is one test class over the
composition root.

---

## 5. The guard: what it can and cannot observe

The ADR sets this slice its central problem and names the trap:

> Clause 1 wants a **guard, not a comment** — but note a startup assertion that
> reads a configured replica count proves only that the configuration was read.
> That is the weakest class of guard in this repo's own catalogue, and it is
> worth saying so where the guard is written.

### 5.1 The three candidates, judged

**(A) A source-text scan over `AppHost.cs`** asserting no `WithReplicas(n>1)`
outside the exception.

*Catches:* the literal call, written literally, in the file scanned.
*Misses:* every semantically-equivalent spelling — a helper or extension method,
a loop over resources, `WithReplicas(int.Parse(…))`, a value bound from
configuration, the call made from any other file. It asserts that a **character
sequence is absent from a file**, which is precisely the class this repository
has disproved by counterfactual repeatedly. **Rejected.**

**(B) A test over the composed Aspire application model.** Build the model with
`DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(args)`
and read each resource's replica count.

*Catches:* **any** spelling that results in a replica count above one, because it
reads the *output* of the composition root rather than its text. Indirection,
loops, config binding and other files are all covered — the annotation is on the
resource however it got there. It can pin the exception by name *and* count, and
it can assert both lanes (run mode and `E2ETests=true`) because composing does
not start anything.
*Costs:* none worth the name. **`CreateAsync` builds the model without starting
any resource** — no containers, no Docker. `AppHostE2ESwitchTests` already does
exactly this and says so: *"Builds the application model only… so this costs no
containers and is safe beside a live `aspire run`."* It runs in CI's existing
**Docker-free `Category=FixtureLogic` step**, in seconds.
*Mechanism:* `ReplicaAnnotation` (public, `Replicas` property) with the
supported accessor `ResourceExtensions.GetReplicaCount(IResource)`, which
"Defaults to `1` if no `ReplicaAnnotation` is found" — both confirmed present in
`Aspire.Hosting` 13.5.3, the pinned version. **Recommended.**

**(C) A check against the *running* Aspire model** (resource snapshots in the
integration suite).

*Sounds strictly stronger — it observes what was built rather than written.* It
is in fact **strictly weaker here**, for a mechanical reason: the integration
fixture boots with `E2ETests=true`, and under that flag the gateway is pinned to
one replica. **The one real replica count in the system is invisible in the only
lane that could run this check.** It would cost the thirty-minute Docker job to
observe a model in which nothing is above one — a test that cannot see its own
subject. The lane that *does* boot two gateway replicas is the Playwright e2e
job (§2.5), which boots the AppHost as a process and offers no in-test hook into
the model. **Rejected**, and the reason is worth recording: it is the
`// HA (#1005)` e2e pin, which the ADR already names as the obstacle clause 3
must solve.

### 5.2 Why (B) is not "asserting the artefact", stated honestly

The distinction is real but it has a limit, and both halves must be said.

**Why it is stronger than a grep:** a source scan asserts a property of a
*file*. This asserts a property of the *object graph the composition root
produced by being executed*. `WithReplicas` is the only supported way to attach
a `ReplicaAnnotation`, and `GetReplicaCount` is the accessor the orchestrator
itself consults. The guard reads the same thing the runtime reads.

**Where it stops:** it observes the model AppHost **describes**, not processes
that **ran**. It trusts that DCP honours the annotation — warranted, it is
Aspire's contract, but it is trust. It proves nothing about whether one instance
is *correct*; it says nothing about the five contexts' per-instance state. That
is clause 3's harness, and this is precisely why clause 3 is a separate slice
rather than a nicety.

**The honest position:** the guard observes the composition root's output, which
is the **sole in-repo authority on replica counts today**. Strictly stronger
than a grep; strictly weaker than running two instances and counting them.

### 5.3 What it cannot observe, and what follows

- **Replica counts set outside the Aspire model** — a Helm `replicas:`, a
  `kubectl scale`, a Compose `deploy.replicas`, a second `dotnet run`. **Today
  this set is empty**: ADR-0153 established, and §2 re-confirmed, that no
  Deployment, Service, Ingress, chart or Kubernetes publisher exists anywhere.
  The guard's coverage is therefore total today and **ends at the AppHost
  boundary the moment deployment artefacts exist.** This is exactly ADR-0153's
  recorded consequence that clause 1 is *"only as strong as the absent
  deployment artefacts make it — which is to say, currently absolute and
  eventually not."*
- **A deliberate runtime scale action.** Out of reach of any build-time guard.

**A scan over `deploy/` is deliberately not added now.** It would be a
file-content scan over a directory containing two Mosquitto files and no chart —
a guard whose subject does not exist, which passes by matching nothing. This
repository's own rule, written into `IntegrationTestSelectionTests`, is that *"a
source-scanning guard that matches nothing passes, and a passing guard that
checks nothing is indistinguishable from one that holds."* **The obligation is
therefore attached to whichever spec first introduces a Deployment or chart**,
and §9 routes it to an issue so it is not lost.

### 5.4 The recorded exception is also the guard's liveness witness

A negative assertion ("nothing above one") passes vacuously if the mechanism is
broken — an empty model, a failed compose, or a `GetReplicaCount` that always
returned 1 would all read as green.

So the guard asserts `api-gateway` **is present and is at exactly 2** in run
mode. That is a positive assertion that can only pass if the accessor can
actually observe a count above one. **The known-broken exception doubles as the
proof that the guard can see what it claims to police** — which is the cheapest
possible answer to "an assertion must not check its own input".

**Forward note for #2283.** If the gateway returns to one replica, the guard
loses this witness and its run-mode assertion becomes purely negative. Whoever
closes #2283 must then either add a synthetic two-replica resource to a
throwaway model or accept the phase-4 counterfactual record as the standing
evidence. Flagged here so it is not discovered by accident.

---

## 6. Independent end-to-end test procedure — the counterfactual

**A guard whose failure mode nobody has constructed is not a guard.** The guard
will be green against today's code, because clause 1 holds today. Green is
therefore *not* evidence. The evidence is the constructed red, and phase 4a's
red obligation is discharged by these runs, each with **verbatim output quoted
in the PR body**.

All four are run by hand, each edit reverted before the next. None requires
Docker.

| # | Construct | Expected |
|---|---|---|
| CF-1 | Add `.WithReplicas(2)` to `layoutComposition` in `AppHost.cs` | Guard **red**; message names `layout-composition` and cites clause 3 |
| CF-2 | Set a replica count on `automation` through an indirection a source search for `WithReplicas` on that line would miss — e.g. a local `static void ScaleOut(IResourceBuilder<ProjectResource> r) => r.WithReplicas(3);` applied to it | Guard **red**, naming `automation` |
| CF-3 | Change `apiGateway.WithReplicas(2)` to `WithReplicas(3)` | Guard **red**; message states #2283 owns the gateway's count |
| CF-4 | Remove the `if (!isE2ETests)` gate so the gateway gets 2 replicas in both lanes | The **E2E-lane** test red, naming `api-gateway` |

**CF-2 is the one that matters** and must not be skipped: it is the only run that
distinguishes the recommended guard from the source-text scan §5.1 rejected. If
CF-2 comes back green, the guard has been built as a grep and the slice has
failed.

**CF-4 doubles as a record.** It is the assertion that pins the integration
lane's single endpoint — the `// HA (#1005)` behaviour ADR-0153 names as the
obstacle clause 3's harness must solve. Seeing it go red is the proof that
whoever builds that harness will be stopped here and made to change it
deliberately.

Phase 5 verification is the CI run: the guard must be observed executing in the
**Docker-free `Category=FixtureLogic` step**, not merely passing somewhere. A
guard that silently ran only in the thirty-minute Docker job is the exact
omission `IntegrationTestSelectionTests` exists to close (§8).

---

## 7. Latency budget

**N/A — no leg affected.** Constitution §IV checked explicitly, as required.

This slice adds one test class and at most a comment. It introduces no runtime
code, no message, no endpoint and no allocation on any path. It does not touch
`event arrival → overlay rendered` or any of its six legs. Nothing ships to a
running process.

Note that ADR-0153 also declined a latency argument, for the separate reason
that the figures usually quoted for `event → overlay state` are cold n=1 samples
of a sub-span, and §IV records that leg as *recorded, not yet readable*. This
spec neither relies on nor disturbs that.

---

## 8. Constraints the implementation must satisfy

1. **The test class must carry `[Trait("Category", "FixtureLogic")]`.**
   Not optional and not cosmetic. `IntegrationTestSelectionTests` fails the
   build for any class under `tests/Integration.Tests` carrying neither that
   trait nor `[Collection(AspireCollection.Name)]`, and only the four exact
   spellings it knows are credited — `"FixtureLogick"` would satisfy a naive
   reader and select nothing. Without the trait the guard's verdict is deferred
   to the thirty-minute Docker job.
2. **It must not take `[Collection(AspireCollection.Name)]`** — that would drag
   in the fixture's live stack and defeat the point.
3. **It must compose only.** `CreateAsync` and read the model; never `BuildAsync`
   or `StartAsync`. An Aspire stack is running on this machine (pid 3312) and
   the guard must be safe beside it, as `AppHostE2ESwitchTests` is.
4. **Both lanes asserted** — run-mode arguments and `E2ETests=true` arguments,
   passed exactly as `AppHostE2ESwitchTests` and `AspireFixture` pass them.
5. **A population gate.** Assert the model is non-empty and that `api-gateway` is
   present, so a broken scan fails instead of passing (§5.4).
6. **Cite `AppHost.cs` by text, never by line number** (§2.1).
7. **Failure messages must route the reader**: name the resource and its count,
   name ADR-0153 clause 3 as the only route to an exception, and for the gateway
   name #2283.
8. **Mirror the existing pattern.** `AppHostE2ESwitchTests` is the template for
   argument arrays, the repository-root walk and `Path.Combine` over literal
   separators. Do not invent a new one.

---

## 9. Findings routed elsewhere — not this slice

Each is real, each was found while verifying §2, and none belongs in a slice
that implements clause 1.

| Finding | Where it goes |
|---|---|
| **ADR-0153 clause 3's "no lane runs two replicas of anything" is false** — the Playwright e2e lane runs `api-gateway` at 2 (§2.5) | A `docs(adr)` correction. **Ideally folded into PR #2417**, which is open and already editing this file; a second editor conflicts. Not the autonomous lane's to write. |
| **ADR-0153's Identity row names the wrong mechanism** — one-shot `IHostedService`, not timer-driven (§2.3) | Same correction, same PR. |
| **`AppHost.cs`'s "under E2E tests" is ambiguous** — it means the integration fixture, and misled ADR-0153 (§2.5) | Candidate for the comment task (T003) if it can be fixed in one clause; otherwise its own chore. |
| **`StreamHealthWatcher`'s split clock may prevent Offline entirely**, not merely delay it (§2.2) | Add to whichever issue carries StreamDistribution's row of the ADR table. |
| **`SignalRLayoutLifecycleBroadcaster` cites FR-012 where it means FR-008** | ADR-0153 already calls this *"a one-line fix and unrelated to this decision."* Its own chore. |
| **A `deploy/` replica scan** (§5.3) | Attaches to the first spec that introduces a Deployment or chart. |
| **Per-context issues for the ADR's table** | Partly discharged: the list now lives in constitution §Availability too (§2.6). Remaining value is one issue per context for the eventual fix. |

---

## 10. Declarations (ADR-0144)

- **Phase 4 engineer role:** **`infra-engineer`.** The subject is the Aspire
  composition root, a test in the AppHost-model lane, and CI job selection —
  squarely that brief. No bounded-context code is touched.
- **Phase 4a colour:** **RED — behaviour-changing.** This adds new behaviour to
  the build (a refusal that did not exist). The guard is green against today's
  correct code, so **green is not the evidence** — the red is the four
  counterfactuals in §6, each observed failing and quoted verbatim in the PR
  body. CF-2 is mandatory; without it the guard is a grep that has not been
  caught being one.
- **New ADR needed?** **No.** ADR-0153 is accepted and decides every question
  this slice implements: the rule (clause 1), the exception and explicitly that
  its fate is not ours to choose (clause 2), and no backplane (clause 5). The
  guard's mechanism is an implementation choice reusing a pattern already in the
  repo. **Stop and escalate if** the work drifts into choosing the gateway's
  replica count (#2283), designing clause 3's harness, or editing ADR-0153 or the
  constitution — each is a decision, and the autonomous lane may not make one.
- **Files phase 4 may touch:**
  - `tests/Integration.Tests/AppHostReplicaCountTests.cs` — **new**, the guard.
  - `src/AppHost/AppHost.cs` — **the `// HA (#1005)` comment block only**, to
    record that the replica count is a guarded ADR-0153 clause-2 exception and
    name the guard. No behaviour change; `WithReplicas(2)` itself is untouched.
  - `specs/169-a-second-instance-the-build-refuses/*` — these artefacts.
  - **Nothing else.** In particular: no ADR, no constitution, no `deploy/`, no
    bounded-context source, no `ci.yml` (the `Category=FixtureLogic` step already
    selects the new class by trait — that is why the trait is mandatory).
