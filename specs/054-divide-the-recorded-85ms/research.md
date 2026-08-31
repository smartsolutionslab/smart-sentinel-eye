# Research — 054 divide the span the decision is waiting on

Phase 0. Everything below was probed against this repository and the running dev
stack, not reasoned from documentation.

---

## 0. Locked decisions: checked, and there is no conflict

Five were candidates. **None contradicts this feature**, and saying so is worth
recording because "we checked" is otherwise indistinguishable from "we didn't".

| ADR | Why it was a candidate | Finding |
|---|---|---|
| 0067 MigrationRunner | The measurement columns arrived by migration | Already applied and merged in spec 053. This feature adds no migration. |
| 0103 Aspire fixture, no Testcontainers | This feature deliberately does **not** use the fixture | No conflict. ADR-0103 governs how *integration tests* get a stack; it does not require every measurement to use one, and the fixture is unusable here for the reason that defines this feature. |
| 0118 one telemetry sink per environment | The run reads timings | No conflict. Nothing here adds a sink, exporter or dashboard; the figures come from SQL over the audit table, as spec 053's do. |
| 0130 no production deployment | "Why not measure production?" | Confirms the premise rather than conflicting. Production does not exist; run mode is what produced the recorded figure. |
| 0135 this measurement's record | It is the thing being extended | This feature closes a gap ADR-0135 names in its own Consequences. Extending it, not overturning it. |

**No amendment gate is triggered.**

---

## 1. Run mode's process topology

**Decision**: treat run mode as *host processes plus containerised infrastructure*,
identical in clock topology to the fixture.

**Evidence**: `src/AppHost/AppHost.cs` composes every service with `AddProject<…>`
— so each is a child .NET process on the host — while Postgres, Keycloak,
RabbitMQ, MediaMTX, Mosquitto and MinIO are containers.

**Consequences, both of which follow and neither of which is new work:**

- `occurred_at` (publisher) and `received_at` (audit consumer) are stamped by two
  **host processes reading one OS clock**. The front of the span — where spec
  053's finding lives — carries no cross-clock error. Spec 053 worried about this
  pair and was wrong to; the correction is already in ADR-0135.
- `written_at` is `clock_timestamp()` inside the **Postgres container**, so the
  write leg still subtracts a host stamp from a container stamp. **It remains not
  established, exactly as on the fixture.** Spec 054 does not close it (spec.md,
  Out of scope).

**Alternatives considered**: assuming run mode might be multi-machine and
designing for it. Rejected — it is measurably not, and designing for a topology
that does not exist is speculative generality. FR-008 still requires the run to
*establish* rather than assume this, which the clock probe already does.

---

## 2. There is no stable address

**Decision**: the operator supplies the endpoints; the run reports what it
actually connected to.

**Evidence**: every service uses `.WithHttpEndpoint()` with no port, so ports are
assigned per boot. The API gateway is the same and additionally runs ≥2 replicas.
`scripts/wait-for-e2e-stack.sh` resolves the gateway by scraping
`apps/shared/src/api/gateway.ts` off the Vite dev server on :5173.

**Rationale**: that scraping trick is a legitimate hack for a smoke check and a
bad foundation for a measurement — it depends on a frontend dev server being up,
which has nothing to do with the audit pipeline. Operator-supplied configuration
is honest: the address genuinely varies per boot, and pretending otherwise is how
a figure gets attributed to the wrong stack.

**Alternatives considered**:

- *Pin ports for the measured services in the AppHost.* Rejected: changes the
  composition root for every developer to serve one measurement, and a pinned port
  is one more thing that can collide.
- *Query the Aspire dashboard's resource API.* Rejected: couples a committed
  artefact to a dashboard whose surface is not a contract, and the dashboard is a
  dev-only sink (ADR-0118).
- *Discover via the MCP Aspire tools.* Rejected outright for a committed driver —
  those are an assistant's tools, not the repository's.

---

## 3. Target the service directly, not the gateway

**Decision**: the run-mode driver calls `system-variables` directly, as the
fixture run does.

**Evidence**: the fixture run uses `aspire.CreateAdminClientAsync("system-variables")`,
which resolves that resource's own endpoint. The gateway would add a proxy hop and
load-balancing across ≥2 replicas.

**Rationale**: FR-011 requires every difference between the two runs other than
the environment to be nil or named. A gateway hop is a difference in the path
being measured, and one that varies per request. Keeping the target identical
removes it rather than documenting it.

---

## 4. The driver's mechanism

**Decision**: **a committed xUnit test that carries no collection attribute** and
builds its own client and database context from operator-supplied configuration.

**Rationale**:

- The apparatus is already xUnit-resident. `IngestAttribution`, `ClockOffsetProbe`,
  `RelativeSkew`, `AttributionVerdict` and the attribution SQL live in
  `tests/Integration.Tests/AuditObservability/`. A script would either reimplement
  the division — a second copy of the thing most worth getting right — or shell
  out to the test anyway.
- **The trap is specific and avoidable.** `[Collection(AspireCollection.Name)]` is
  what injects `AspireFixture`, and the fixture boots its own stack. A class
  without that attribute never touches it. This is a property of the code, not a
  convention: no attribute, no fixture, no stack.
- It inherits the existing exclusion mechanism — `Category=Measurement` already
  keeps its sibling out of CI.

**What happens with no stack up**: the run must **fail fast with a message naming
what it could not reach**, and must never fall back to booting anything. Absent
configuration is a refusal, not a default. This is the single most important
failure mode, because a silent fallback to the fixture would reproduce exactly the
defect this feature exists to remove — and would do so while reporting success.

**Alternatives considered**:

- *A standalone script.* Rejected: duplicates the division, or wraps the test.
- *An Aspire resource in the AppHost.* Rejected: a load generator wired into the
  composition root is a thing every developer boots, and it would measure the
  stack it is part of.

---

## 5. Reuse, which requires an extraction

**Decision**: lift the run body and the attribution SQL out of
`NFR001_AuditIngestLatencyTests` into a component parameterised by *how to get an
authenticated client* and *how to get a database context*. Both the fixture test
and the run-mode test then call it.

**Rationale**: "reuse, do not reimplement" cannot be honoured by copying, and the
two runs must be **the same code** rather than two codebases held in agreement by
prose. This is also the mechanism FR-011 asks for (see §7).

**Cost, stated plainly**: this touches merged, working code that spec 053 verified.
The extraction must be behaviour-preserving, and the fixture run must produce the
same figures afterwards — which is checkable, because those figures are recorded.

---

## 6. Authentication

**Decision**: mint the token by the same password grant the fixture uses, against
an operator-supplied Keycloak base address.

**The recorded trap**: tokens must be minted from **Aspire's proxied endpoint**,
not the container's mapped port, or every call 401s. The fixture avoids this by
using `App.GetEndpoint("keycloak")`; run mode has no `App`, so the operator
supplies the proxied address and the run reports which address it used.

**Consequence for the quickstart**: the address to hand the driver is the one the
Aspire dashboard shows for the `keycloak` resource — not the one `docker ps`
shows. That distinction has cost this repository time before and belongs in the
runbook rather than in someone's memory.

---

## 7. Keeping the comparison honest — a mechanism, not a promise

**Decision**: **one shared constant block plus a reported conditions record.**

- The run shape — generator, warm-up count, measured count, writer count, target
  rate, pacing — becomes a single set of constants that *both* runs read. Neither
  run can change shape without changing the other, so silent drift is not
  expressible.
- Each run emits a **conditions block**: environment, endpoint actually connected
  to, achieved rate beside intended, logging level, measurement-switch state, rows
  measured and rows missing stamps.

**Rationale**: FR-011's requirement is that differences are nil or named. Shared
constants make the shape differences *nil by construction*; the conditions block
**names** the ones that remain, which are the environment and the address. A prose
promise in the record would be neither.

**Alternatives considered**: asserting equality between two recorded condition
blocks. Rejected as over-engineering for two runs a human sets side by side — and
it would compare what was reported, not what was executed.

---

## 8. Logging and the switch

**Decision**: operator sets both in the environment before launching the AppHost;
the run reports the logging level and refuses Debug or Trace.

**Evidence, verified today**: `Logging__LogLevel__Default` and
`AuditObservability__Measurement__RecordIngestBreakdown` propagate from the
launching shell through the AppHost into child service processes. At Debug this
stack sustains 60–83 ev/s — below the rate the requirement names — so a run there
measures the logging as much as the pipeline. The refusal already exists and is
verified in both directions.

**Not changed**: `appsettings.Development.json`. What Development logs at is a
developer-experience trade-off, and a measurement should not settle it for
everyone.

---

## 9. Repetition, and which side is noisy

**Decision**: three runs minimum, spread reported, no effect size from a single
pair.

**Evidence**: at Debug the same configuration gave 60.0 / 79.1 / 82.5 ev/s; at
Warning it gave 169.8 / 173.7 / 244.4. **The asymmetry has a cause worth carrying
into the record**: at Debug the logging is the bottleneck, so the figure
reproduces; at Warning the bottleneck is the machine, so it does not. A single
pair can therefore land anywhere from 2.1× to 3.0× with nothing having changed —
which is how an earlier note in this repository came to overstate an effect size.

---

## 10. What no automated check can prove

**That the driver hit run mode**, rather than some other stack that happened to
answer on the configured address.

Nothing in the test can establish this: an endpoint is an endpoint. What
establishes it is a human reading the conditions block against the stack they
started — the address, and the fact that the row count in a persistent audit store
grew by exactly the measured count.

Stated here because the alternative is a check that *looks* like it proves this
and does not, which is the failure mode spec 053 documented eight times over.
