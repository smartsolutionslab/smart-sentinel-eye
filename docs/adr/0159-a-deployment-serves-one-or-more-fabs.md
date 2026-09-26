# ADR-0159: A Deployment Serves One or More Fabs

**Status:** **Accepted** (maintainer sign-off 2026-09-26, issue 2080)
**Date:** 2026-09-26
**Amends:** `docs/adr/0000-initial-decisions.md` rows 006, 007, 025;
constitution §I, §IV, §VI, §Operations, §Scale
**Relates to:** ADR-0114, ADR-0116, ADR-0130, ADR-0145, ADR-0153, specs
008, 013–019, issues 2069, 2080

## Context

The founding decisions use **"per fab"** for three different things and never
say which is meant:

| Sense | What it constrains | Where it appears |
|---|---|---|
| **Deployment unit** | How many fabs one installation — one AppHost composition, one Keycloak realm, one instance of each service — holds | Rows 006, 007, 025; constitution §VI, §Operations |
| **Data scope** | Which fab a record belongs to, and which principals may read or change it | Specs 008, 013–019; ADR-0114, 0115, 0116, 0145 |
| **Physical site** | Things bound to where the cameras are: the OT VLAN, the PTP grandmaster, the camera→SFU network path | Rows 013, 014, 021; constitution §Streaming |

Read as a deployment unit, rows 006, 007 and 025 say one installation holds
exactly one fab, so **a principal holding two fabs cannot exist**. Read as a
data scope, the fab-scoping specs say the opposite, and they are built:

- **One realm, four fabs.** `src/AppHost/Realms/smart-sentinel-eye-realm.json`
  declares the single realm `smart-sentinel-eye` with groups `/fabs/munich`,
  `/fabs/dresden`, `/fabs/berlin` and `/fabs/hamburg`, and seeds
  `op-multi@smart-sentinel-eye.test` in two of them.
- **The single realm was a decision, recorded where nobody looked.** Spec 008
  lists *"Per-fab realms — single shared realm for v1"* under Out of Scope
  (`specs/008-identity/spec.md`, `plan.md`), and its tasks T057/T058 test and
  document the multi-fab guard. Row 007 was never updated.
- **The guard assumes several fabs.** `IFabAuthorizationGuard` checks the
  requested fab against the caller's group list; ADR-0114 builds its
  single-fab inference *and* its multi-fab refusal on that.
- **Storage is divided per fab inside one database.** Spec 019 provisions an
  events partition per fab by reading `/fabs` from the realm — a mechanism
  that only has work to do if one installation holds more than one fab.
- **Services key their state on fab.** Automation's `InMemoryRuleCache` is
  keyed on `(fab, source, kind)` so that lookup cost does not grow with the
  rules of *other* fabs (spec 013 SC-007); the gateway's rate limiter
  partitions on `X-Fab`; ADR-0116's attribution service reads every plant's
  camera list, and names attributing everything to one fab as *"wrong
  precisely for a multi-fab deployment."*
- **The maintainer decided it.** On 2026-09-04, during issue 2069, multi-fab
  was confirmed as real: one deployment can serve several fabs, and a
  principal can legitimately hold more than one (recorded in ADR-0145).

Nothing in `src/` assumes the reverse. The one statement that does is a stale
comment: `InMemoryRuleCache`'s *"For v1 we run one Automation instance per
fab"*, which contradicts the fab-keyed dictionary two lines below it.

**The spec 047 audit recorded row 007 as *Holds*.** Its evidence — Keycloak is
declared in `AppHost.cs` and imports a realm — establishes *Keycloak,
self-hosted*, and says nothing about *per fab*. The qualifier was the claim
the code contradicts, and it was the one part not checked. This is ADR-0130's
own lesson applied to itself: one verdict for a row that makes two claims.

**What the repository cannot settle** is where a deployment runs relative to
the fabs it serves. There is no production deployment (ADR-0118); the Aspire
Kubernetes publisher has never been run and `deploy/helm/` holds one
hand-written Mosquitto chart (ADR-0130, issue 1015). Whether a multi-fab
deployment sits on one campus or reaches plants in different cities is a
statement about sites nobody here can see — ADR-0130's *unverifiable here*.

## Decision

**1. A deployment serves one or more fabs.** One composition, one Keycloak
realm, one instance of each service (ADR-0153), holding every fab its realm
declares under `/fabs`. A single-fab deployment is the case of one, not a
different architecture.

**2. "Fab" is a data scope, and that scope is binding.** Every fab-scoped
record carries its fab; access is decided by the caller's fab-group
membership; a principal may hold several fabs. The rules for choosing among
them are ADR-0114 (writes: infer for one, refuse for several) and ADR-0145
(kiosk reads: derived from the wall). This ADR adds no new rule — it removes
the topology claim that said those rules could never be exercised.

**3. "Per fab" no longer constrains deployment topology.** Rows 006, 007 and
025 and constitution §VI and §Operations are annotated to say *per
deployment* where they meant the installation, keeping their original text
(ADR-0130 §5).

**4. Physical-site constraints are unchanged.** The OT VLAN (013), the PTP
grandmaster (014, 021) and the camera→SFU leg of §IV are properties of the
place the cameras are. Where the record says "per fab" for these it means
*per site*, and nothing here relaxes them.

**5. A deployment may span sites.** The fabs one deployment serves may be at
different physical locations — the seed realm's Munich and Dresden are. Two
consequences follow, and both are recorded where they bind:

- **§I's self-containment is on premises, not on one site.** A deployment
  needs nothing outside the fabs it serves and no outbound internet, but a
  site-spanning deployment does cross the operator's own WAN between sites.
  That is inside the self-contained boundary; the internet is not.
- **The camera → SFU leg (≤ 80 ms, §IV) assumes a site-local camera** — the
  camera and the SFU serving it on the same site. A camera reaching its SFU
  across the inter-site WAN is outside what that budget was set for, and must
  be justified against §IV like any other breach, not absorbed silently.

**6. The 250-camera target is per deployment**, not per fab. A multi-fab
deployment shares that capacity across its fabs; nothing here raises it.

## Consequences

**Positive — the record matches the realm.** A reader of row 007 no longer
concludes that `op-multi` is a dev-only fiction, and so no longer concludes
that a defect like issue 2069's is unreachable in production.

**Positive — the fab-scoping specs stand on a recorded decision** rather than
on an XML comment (ADR-0114 found none) and a spec's Out-of-Scope list.

**Negative — fabs share capacity.** The 250-camera target is per deployment
(Decision 6), so four fabs on one deployment divide 250 cameras between them
rather than each getting 250. That keeps ADR-0153's one instance per service
sized as it was; it also means adding a fab to a deployment is a capacity
decision, not just a new group in the realm.

**Negative — a site-spanning deployment puts a WAN inside the system.** Links
between sites carry service traffic, and any camera whose SFU sits on another
site starts outside the ≤ 80 ms camera → SFU budget (Decision 5).

**Negative — isolation between fabs is logical, not physical.** A defect in a
fab filter now leaks across plants that share a database, a broker and a
cache, rather than failing inside one plant's installation. The guard and the
per-fab partitions are what stand between them — which is why ADR-0145 fails
closed on a frame with no fab.

**Neutral — v2 federation is per deployment.** The cloud control plane
federates to each deployment's Keycloak, and GitOps keeps one values file per
deployment. Nothing of v2 exists to change.

## Alternatives Considered

**Per-fab realms — REJECTED**, as spec 008 already rejected it for v1. It
would make the multi-fab principal impossible, which the maintainer has
decided is real, and would unbuild the fab-group guard every fab-scoped
endpoint uses.

**Leave rows 006/007/025 as Locked and document the exception in each
spec — REJECTED.** That is the state issue 2080 describes: two incompatible
records, and the reader picks up whichever one they meet first.

**Supersede row 007 outright — REJECTED.** Keycloak, self-hosted OIDC and v2
federation all hold; only the qualifier is wrong. Amending keeps the three
correct claims and the evidence that the fourth drifted (ADR-0130 §5).

**Co-located fabs only — REJECTED** by the maintainer. It would have kept
§I's single-site wording intact, but the seed realm already pairs fabs in
different cities, and forbidding that would have been a topology claim the
data model never enforced.

**250 cameras per fab — REJECTED** by the maintainer. It would have made a
four-fab deployment a 1 000-camera system on single service instances
(ADR-0153), which nobody has sized.

## Implementation Notes

- ADR-0000 rows 006, 007, 025 annotated `**Amended by ADR-0159.**` with
  `Originally:` kept, as `FoundingDecisionRecordTests.Every_amended_row_still_shows_what_was_originally_decided` requires.
- Constitution §I, §IV (a note under the sub-budget table; the table rows
  are untouched), §VI, §Operations, §Scale; version 1.9.0.
- `CLAUDE.md` stack table's Identity row.
- `specs/047-the-decisions-we-made/audit.md` — row 007's verdict annotated,
  not rewritten.
- **Not in this change:** the stale `InMemoryRuleCache` comment (a code
  change, so a phase-4 issue of its own); `specs/006-event-ingestion/plan.md`
  (a historical plan, left as the commitment it was); a
  `FoundingDecisionRecordTests` consistency check that fails when the realm
  holds more than one fab and row 007 claims otherwise — offered, not built.
