# ADR-0187: The migration-runner deny is two policies, because a negative policy alone permits everyone else

**Status:** **Accepted** by the owner on PR #3119, 2026-10-24, as written (Decisions 1-3; records the owner's ruling of the same day on #1995 — amend ADR-0186 to the two-policy deny, then implement — and the owner's two rulings at acceptance on the items left open, see *Consequences* and *Implementation Notes*)
**Date:** 2026-10-24
**Amends:** ADR-0186 (Decision 5 — the mechanism of the migration-runner deny; Decision 6 — what the realm-file guard asserts). Decisions 1-4, the FGAP all-clients grant and the narrowing of `identity-admin` stand unchanged.
**Relates to:** ADR-0181, ADR-0144, spec 440 (branch `fix/1995-narrow-default-roles`, `research.md` § *Probes*, commit `7560a8ef`), issue 1995

## Context

ADR-0186 Decision 5 denies the migration-runner client through an FGAP v2 permission whose only policy
is a user policy naming `service-account-identity-admin` with **`NEGATIVE`** logic. On the branch that
implements it (not on `develop`), live probes on 2026-10-24 — Keycloak 26.6.4, throwaway container, the
committed realm file imported byte-identical — showed that the policy means *permit everyone except
`identity-admin`*:

| Principal (no admin role) | GET client | PUT client | GET secret | POST secret |
|---|---|---|---|---|
| `identity-admin` | 403 | 403 | 403 | 403 |
| `operator` (human), `wall-munich` (kiosk user) | 200 | 204 | 200 | 200 |
| `scenario-simulator`, `event-ingestion`, `revocation-list-reader` | 200 | 204 | 200 | 200 |
| `admin-permissions`' own service account (secret rotatable by `identity-admin`) | 200 | — | — | — |

A `PUT` that replaces `jwks.string` is a takeover of a `manage-realm` holder, so any authenticated
principal reached what the decision existed to deny. **The ADR-0186 spike probed only `identity-admin`**,
and its counterfactual (remove the deny, `identity-admin` gets through) cannot detect a deny that admits
everyone else. The flaw is in the method, not only in the policy.

Two fixes were applied over REST on the throwaway (master admin) and compared:

| Permission policies (`UNANIMOUS`) | Direct access, every probed principal | `identity-admin` `GET /clients`, `?search=true&clientId=` |
|---|---|---|
| ADR-0186: NEGATIVE `identity-admin` only | open to all but `identity-admin` | migration-runner absent |
| Always-deny only: POSITIVE user policy with `users: []`, or naming a disabled dummy user | 403 | **migration-runner listed, full representation** |
| **Both**: the NEGATIVE `identity-admin` policy **and** a POSITIVE policy naming one disabled dummy user | **403** (GET, PUT, secret GET, secret POST) | migration-runner absent (19 clients, 0 hits; `?clientId=` `[]`) |

An empty-users policy also regressed the list filter, so the always-deny policy must name a real user.

Also measured, unchanged by this amendment: `identity-admin` cannot self-grant `manage-clients` /
`view-clients` (5 routes, all 403); cannot read, edit or delete the FGAP permission or policies, nor
switch FGAP off (403/405/400); enrolment (create 201, read and regenerate secret, `PUT`, read the
service-account user) still works without `manage-clients`/`view-clients`. **Residual:** `identity-admin`
can disable (204) and delete (204) `service-account-migration-runner` — a denial of the migration gate,
not a takeover (reset-password 400; password grant `invalid_grant`).

## Decision

**1. Decision 5's deny is two policies, applied unanimously** (replaces ADR-0186 Decision 5's third
bullet). The migration-runner-only permission (`view`, `manage`, `map-roles`, `map-roles-composite`,
`map-roles-client-scope`, `decisionStrategy: UNANIMOUS`) carries:
- the existing user policy naming `service-account-identity-admin` with `NEGATIVE` logic, which keeps
  migration-runner out of `identity-admin`'s client list; and
- a second user policy with `POSITIVE` logic naming exactly one **seeded dummy user** that nothing can
  satisfy, which denies every other principal.

The dummy user is declared in the realm file: `enabled: false`, no credentials, no realm or client roles,
no groups, not a service-account user. Its name is an implementation choice.

**2. Decision 6's guard bans a deny built on NEGATIVE logic alone** (extends ADR-0186 Decision 6). The
realm-file test additionally asserts that every FGAP deny permission includes a `POSITIVE` user policy
naming exactly one user, under `UNANIMOUS`; that this user exists, is disabled, and holds no credentials,
roles or groups; and that a deny whose policies are all `NEGATIVE` fails. Prove it by counterfactual
(drop the second policy, enable the dummy user, give it a role: each red). ADR-0186's existing assertions
stay.

**3. Every confinement probe runs as more than the principal it confines.** A test or spike claiming
that a principal is denied something — this deny and any later one — runs the same requests as, at
least: the confined principal; a human operator; a kiosk/wall user; a device service-account token; a
webhook-style service-account token; and any credential the mechanism itself creates (here
`admin-permissions`' service account). It checks both direct access **and** the list/search filters,
because the two regressed independently above. A probe of one principal does not support a claim about
the rest.

## Consequences

**Positive:** the deny holds against every probed principal and keeps the client out of
`identity-admin`'s list (measured over REST). The confinement-probe rule closes the method gap that let
an inverted policy pass a spike, a counterfactual and a review.

**Negative:** the realm file gains a user that exists only as a policy target; a reader must know why a
disabled, role-less user is load-bearing. Enabling it, or giving it a role, silently re-opens the
client to that user — hence Decision 2's assertions.

**Negative: not verified — import round-trip.** The two-policy variant was applied over REST, never
imported. The implementing PR must prove that the realm-file JSON imports and the deny holds on a fresh
import (Aspire fixture), not assume it from the REST result.

**Negative, accepted: `identity-admin` can disable or delete `service-account-migration-runner`.**
Accepted in writing by the owner at acceptance (PR #3119, 2026-10-24) as a denial-of-migration risk, not
a takeover: the migration gate fails closed, and nothing reaches the `manage-realm` credential this way.
The fix is ticketed as follow-up #3120; it does not block #1995.

**Negative, kept for now: `admin-permissions`' service account and its rotatable secret.** Kept by the
owner's ruling at acceptance (PR #3119, 2026-10-24); its removal or disabling is ticketed as #3121. Until
#3121 lands it is one of the principals Decision 3's probes cover, and the implementing PR measures that
the two-policy deny holds against it rather than relying on the logic.

**Negative: not verified — the full matrix under the fix.** Under the two-policy variant, direct access
was probed as `scenario-simulator`, `wall-munich` and `identity-admin`; `operator`, `event-ingestion`,
`revocation-list-reader` and `admin-permissions`' credential are expected denied by the unanimous
always-deny policy but were not re-probed. Self-grant, policy-edit, service-account-user and enrolment
probes were run before the fix and not repeated. The implementing PR's tests (Decision 3) measure all of
them.

## Alternatives Considered

**Always-deny policy alone** (empty users or a disabled dummy). Rejected: measured to expose
migration-runner's full representation through `identity-admin`'s `GET /clients` and search.

**Keep ADR-0186's NEGATIVE-only policy.** Rejected: measured to open the client to every authenticated
principal.

## Implementation Notes

Delivered on `fix/1995-narrow-default-roles` with ADR-0186 Decisions 1-6; spec 440's `plan.md` and
`tasks.md` carry this amendment before phase 4 resumes. **Accepted on PR #3119, 2026-10-24: spec 440's
`plan.md` and `tasks.md` may now take this ADR in, and #1995 may resume** — from those updated artefacts,
not at phase 4. Behaviour-changing, phase 4a **red**: the
multi-principal probe test is red against the branch's current realm file (200/204 for non-admin
principals). `security-reviewer` mandatory at phase 6. On acceptance, ADR-0186 gains an `Amended by:
ADR-0187` line (added 2026-10-24).

**Ruled by the owner at acceptance** (PR #3119, 2026-10-24; both were open in the proposed text):
- **Residual: `identity-admin` can disable or delete `service-account-migration-runner`.** **Accepted in
  writing** as a denial-of-migration risk; the fix is ticketed as follow-up **#3120** and is not part of
  #1995.
- **`admin-permissions`' service account and its rotatable secret.** **Kept for now**; its removal or
  disabling is ticketed as **#3121**. Until then it stays in Decision 3's probe set.
