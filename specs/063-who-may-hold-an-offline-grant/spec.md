# Feature Specification: Who may hold an offline grant

**Feature Branch**: `fix/1995-a-hand-made-account-inherits-offline`

**Created**: 2026-09-04

**Status**: **BLOCKED at the phase-1 gate — the honest answer is a new ADR.**
Phases 2 and 3 are deliberately not written; see *Why there is no plan.md*.

**Issues**: #1995

**Lane**: ADR-0144 autonomous. `agent:ready` present, `agent:blocked` absent,
board status *In Progress* at the time of writing. This document is the reason
the lane stops rather than the work it would have done.

**ADRs**: ADR-0134 (the containment this would extend, and which **considered
and declined** exactly this fix), ADR-0132 (the withdrawn attempt),
ADR-0131, ADR-0133, ADR-0130 (`deploy/` provisions no realm), ADR-0144
(*What the lane may not do*), constitution §Availability, §Security.

**Input (#1995)**: "An account created by hand in Keycloak still inherits the
offline privilege. […] The provider composes `default-roles-smart-sentinel-eye`,
and that composite includes `offline_access` — the privilege that mints
credentials which never expire."

---

## The question that decides the run, answered first

ADR-0144 draws one boundary the lane may not cross: it **implements**
architectural decisions, it does not **make** them. So the whole run turns on
one question.

> **Is closing #1995 a realm configuration change, or a decision about who may
> hold an offline grant?**

**It is a decision, on two independent grounds, and either alone is sufficient.**

1. **It cannot be expressed as configuration at all.** The realm file physically
   cannot remove `offline_access` from the default composite. Measured three
   ways below, against Keycloak 26.6 — the version the stack runs.
2. **Every mechanism that can do it either takes authority ADR-0134 explicitly
   declined four days ago, or invents a policy this repository has never
   written.**

The lane therefore blocks, and this specification exists to make the ADR cheap
to write rather than to design past it.

---

## What was verified, and how

Nothing here is taken from the issue, ADR-0134 or the realm file on trust. Every
claim below was produced by a command against a running Keycloak 26.6. The two
issues before this one in the queue both carried premises that did not survive
checking, and the realm file is the specific artefact this repository has
already been burned by reading (ADR-0134: *"an architecture guard reading that
file stayed green for the whole of spec 050 while the claim it stood for was
false"*).

### 1. The premise is true, and more precisely true than the issue states

Against the running realm (`keycloak-18bcf406`, Keycloak 26.6):

`default-roles-smart-sentinel-eye` composes exactly:

| Member | Container |
|---|---|
| `user` | realm |
| **`offline_access`** | realm |
| `uma_authorization` | realm |
| `view-profile` | `account` client |
| `manage-account` | `account` client |

So `offline_access` **is** in the composite. Confirmed.

**The control, without which that proves nothing.** An account was created by
hand through the admin API, its effective realm roles read, and the account
deleted:

- direct mapping: `default-roles-smart-sentinel-eye`
- **effective**: `default-roles-smart-sentinel-eye`, `user`, **`offline_access`**,
  `uma_authorization`

**Which accounts are affected, measured rather than assumed.** All twelve users
in the running realm, with their direct realm-role mappings:

| Account | Realm roles held | Holds `offline_access`? |
|---|---|---|
| `admin`, `admin@munich.test` | `user`, `admin` | no |
| `operator`, `op-3@…`, `op-berlin@…`, `op-dresden@…`, `op-hamburg@…`, `op-multi@…` | `user` | no |
| `wall-munich`, `wall-dresden`, `wall-berlin`, `wall-hamburg` | `user`, **`offline_access`** | **yes, directly** |

**Not one declared account carries `default-roles-smart-sentinel-eye` at all.**
ADR-0134's claim that accounts declared in the realm file receive exactly the
roles they name is confirmed. The four wall displays hold the privilege by
explicit grant, not by inheritance — which is what makes the containment real
and what makes the residual gap exactly as narrow as ADR-0134 says.

### 2. It cannot be fixed in the realm file — three experiments

A throwaway Keycloak 26.6 container was booted against modified copies of
`src/AppHost/Realms/smart-sentinel-eye-realm.json`, one variant at a time. The
repository's realm was not touched.

| Variant | What it declared | Result |
|---|---|---|
| **A** | `default-roles-smart-sentinel-eye` as a composite naming only `user`, plus a top-level `defaultRole` | **Import succeeded. Narrowing silently discarded.** Composite came back with all five members; a hand-made account still inherited `offline_access`. |
| **B** | as A, and additionally declared `offline_access` and `uma_authorization` as realm roles so the composite could name them | **Import succeeded. `uma_authorization` was dropped. `offline_access` was re-added.** Composite: `user`, `view-profile`, `manage-account`, **`offline_access`**. |
| **C** | composite naming `uma_authorization` without declaring it (what the issue describes) | **The whole server failed to start**: `ERROR: Unable to find composite realm role: uma_authorization`. |

Variant B is the finding that settles it. Of the five members of that composite,
**`offline_access` is precisely the one the realm file cannot remove** — Keycloak
re-attaches it unconditionally when it sets up the realm's default role. The
file can shape everything else about that composite and not this.

**Correction to the issue.** #1995 says a declared composite "is discarded on
import, and […] the import fails outright". Both halves are true but of
*different* variants: A is discarded and succeeds, C fails. The distinction
matters, because **A is the failure mode a reader would actually ship** — a
realm-file change that imports cleanly, looks like the fix, and is a no-op. That
is the same shape as spec 050's green guard and as #2054's green guard over a
diagnostic that had never worked.

### 3. The admin API can do it, and the effect is retroactive

On the variant-B realm, deleting `offline_access` from the composite through
`DELETE /admin/realms/{realm}/roles/default-roles-{realm}/composites` returned
**204**, and:

- the composite became `user`, `view-profile`, `manage-account`;
- the **already-existing** hand-made account's effective roles dropped to
  `default-roles-smart-sentinel-eye`, `user` — the privilege was gone without
  touching the account, confirming #1995's "applies retroactively, since the
  composite resolves at evaluation time";
- `wall-munich`, which holds the role **directly**, kept `offline_access`.

The authority that call needs was **not** re-measured here; #1995 and ADR-0134
both measured it one permission at a time and agree: nothing `identity-admin`
holds suffices, `view-realm` does not, **`manage-realm` does**.

---

## Blast radius

The outcome is small. The mechanism is not. Both statements are load-bearing and
they point in opposite directions, which is why this needs a decision rather than
a judgement call.

### What removing `offline_access` from the default composite would change

Measured against the running realm — every client's default and optional scopes:

| Scope exposure | Clients |
|---|---|
| `offline_access` as a **default** client scope | **none** |
| `offline_access` as an **optional** client scope | **`kiosk-wall`, and only `kiosk-wall`** |
| neither | `kiosk-web`, `management-web`, `smart-sentinel-eye-web`, `account`, `account-console`, `admin-cli`, `security-admin-console`, `broker`, `realm-management`, `identity-admin`, `migration-runner`, `event-ingestion`, `scenario-simulator`, `stream-distribution-attribution` |

At the realm level `offline_access` sits in *default-optional-client-scopes* and
in no default list, so nothing acquires it implicitly either.

Therefore:

- **No operator is affected.** An operator signs in through `management-web` or
  `kiosk-web`. Neither client offers the scope, so neither can request it, so no
  operator sign-in can be refused for lacking the privilege. **Nobody is logged
  out.** This is the failure mode that made spec 050 unshippable (a *default*
  scope refuses the entire sign-in for an account without the privilege) and it
  does not arise here, because nothing makes it default.
- **No wall display is affected.** All four hold `offline_access` by direct
  grant, and direct grants are untouched by narrowing the composite — observed,
  not reasoned: `wall-munich` kept it in the variant-B experiment.
- **No enrolled kiosk is affected.** Spec 052 already strips the inherited
  privilege at enrolment (`IKeycloakAdminClient.StripInheritedRealmRolesAsync`),
  so those accounts hold nothing to lose.
- **No session is ended.** Removing a composite member changes role *evaluation*;
  it revokes no token and closes no session. `kiosk-wall` currently reports
  **0 offline sessions**, so there is nothing live to disturb in this
  environment in any case.
- **The single behavioural change** is that an account created by hand, signing
  in through `kiosk-wall` and requesting `offline_access`, would be refused —
  which is the entire point of the issue.

**So: it does not silently log out every operator.** It is at the small end of
the range the brief asks about. That is the honest finding and it argues *for*
the fix.

### What it would cost to get there

| Mechanism | New authority | New policy | Where it works |
|---|---|---|---|
| **1. Startup step narrowing the composite** | **`manage-realm`** — authority over session lifetimes, roles and authentication flows, held by nothing today, and **broader than the privilege it contains** | no | everywhere the step runs |
| **2. Narrow at realm provisioning** (e.g. a `kcadm` step in AppHost using the bootstrap admin, which already holds everything) | **none** | no | **dev and CI only** — `deploy/` provisions no realm at all (ADR-0130), so production is not covered because production provisioning does not exist |
| **3. Periodic reconciliation** stripping the privilege from accounts outside the wall-display set | none (`manage-users`, already held) | **yes, and it is the whole problem** | everywhere, and it also catches drift |

Mechanism 2 is the one the issue does not list in this form and the one this
investigation would put in front of a human first: it costs no grant to any
service, it is retroactive, and it is honest about covering only the environments
this repository actually provisions. It is still a decision.

---

## Why this is a decision and not an application

ADR-0144: *"An issue whose honest answer is a new architectural decision is
blocked with that as the reason. This is the boundary the loop must not cross."*

**Ground 1 — ADR-0134 considered this fix and declined it, four days ago.**
Under *Alternatives Considered*: *"Narrow the provider's default privilege set.
Total, and needs authority broader than the privilege it contains. Filed."* And
under *Consequences*, as an accepted **Negative**: *"an account created by hand
is not covered."* Closing #1995 reverses a recorded alternative and retires an
accepted consequence of an Accepted ADR. Neither is applying that ADR; both
amend it.

**Ground 2 — the mechanism is unchosen, and the choice is the decision.** Three
mechanisms with materially different costs, and ADR-0134 chose between none of
them because it declined the whole branch. Mechanism 1 grants a startup worker
power over the realm's authentication flows to remove one role. Mechanism 2
covers dev and CI and leaves production — which does not exist — uncovered, and
requires saying in writing that this is acceptable. Mechanism 3 needs no new
authority but needs a rule nobody has written: **which accounts outside this
system's own creation path may hold an offline grant, and what a background job
may take away from an account a human administrator deliberately configured.**
ADR-0134's answer — *"accounts this system creates: wall displays only"* — is
scoped, in its own words, to accounts this system creates. It is silent on the
others, which is exactly the population #1995 is about.

**Ground 3 — the existing tool is documented as unsafe for this population.**
`IKeycloakAdminClient.StripInheritedRealmRolesAsync` carries the comment *"Only
ever call this for an account enrolment created. It removes every
directly-assigned realm privilege; against a person's account that would be
destructive."* Mechanism 3 would point that capability, or a narrower sibling of
it, at human accounts. That is a new trust boundary, not a new caller.

**What would *not* have been a decision.** Had the answer to the deciding
question been "remove it from the default composite in the realm file, and the
where-it-is-needed set is ADR-0134's four wall displays", the lane could have
proceeded: the set is established and the edit is configuration. Experiment A
is precisely why that path does not exist.

---

## Security note

**What an offline grant buys the holder, for a hand-made account versus for a
wall display.** ADR-0134 characterised the wall-display grant as *"a
never-expiring, view-only grant in one fab"*. That description is exact for a
wall display because of a property the grant itself does not carry: **authority
lives on the client, not on the grant.** `kiosk-wall` is a separate client whose
scopes are five read scopes and no write scope, and the account is in one fab
group. Take the same privilege on a hand-made account and **the read-only half
does not follow**. The grant's authority is whatever the client it signs in
through offers and whatever groups the account is in — so a hand-made account
placed in the `admin` role, or in several fab groups, and signed in through
`kiosk-wall` would hold a grant that does not expire and is **not** view-only and
**not** confined to one fab.

**The exposure is therefore larger in principle and inert in practice, today.**
Inert because `kiosk-wall` is the only client offering the scope and it grants
five read scopes to whoever signs in through it; larger in principle because
nothing structurally ties "holds `offline_access`" to "is read-only in one fab" —
that tie is a property of one client's configuration, and the next client to
offer the scope inherits none of it. **This is the strongest argument for
closing the issue**, and it belongs in the ADR rather than in an unreviewed
change.

**The 30-day bound — the brief's note needs correcting.** The brief records a
previous investigation as finding the unused-offline-session default *not* set in
this realm, "so an offline grant may be bounded by nothing". Read from the
running realm:

| Setting | Value |
|---|---|
| `offlineSessionIdleTimeout` | **2592000** (30 days) |
| `offlineSessionMaxLifespanEnabled` | **false** |
| `offlineSessionMaxLifespan` | 5184000 (inactive, because the flag above is false) |
| `ssoSessionIdleTimeout` / `ssoSessionMaxLifespan` | 1800 / 36000 |

So the bound **is** in force: an offline session unused for 30 days is removed.
What is absent is the *absolute* cap — `offlineSessionMaxLifespanEnabled` is
false, so a grant used at least once every 30 days never expires. Both halves
match ADR-0134 exactly (*"an unused offline session is removed after thirty
days"*, and *"a grant with no expiry while it is used"*).

The realm **file** sets none of these — it sets `accessTokenLifespan` and
nothing else about sessions. Every figure above is a Keycloak default, which is
ADR-0134's *"Neither figure is set by this repository"*, confirmed. The
difference between "not set in the file" and "not in force" is what the brief's
note collapsed, and it is worth keeping apart: the protection is real, and it is
real by accident of a vendor default that no test asserts and no upgrade is
obliged to preserve. **That is itself worth a line in the ADR.**

---

## Requirements *(what an ADR would have to settle)*

Written as questions rather than as `FR-`s, because the lane may not answer them.

- **FR-Q1** — May an account this system did not create hold `offline_access`?
  ADR-0134 answers only for accounts the system creates.
- **FR-Q2** — Which mechanism: startup narrowing with `manage-realm`, narrowing
  at realm provisioning, or periodic reconciliation? The costs differ in kind,
  not degree.
- **FR-Q3** — If `manage-realm` is granted, to what, for how long, and is a
  control that needs authority broader than the thing it controls acceptable
  here? ADR-0134 said no; the reversal needs its own reasoning.
- **FR-Q4** — If the fix covers only dev and CI (mechanism 2), is that
  acceptable given `deploy/` provisions no realm (ADR-0130), and what records
  the residue so it is not lost a second time?
- **FR-Q5** — Should the realm pin the session timings it currently inherits
  from Keycloak defaults, given that the only bound on an offline grant is one
  of them?

## Non-requirements

- Changing what an operator may do. Nothing here proposes that, and the blast
  radius shows nothing here would.
- Rotating or expiring wall-display credentials. ADR-0134 records that as an
  open negative; it is a different issue.
- Provisioning a realm in `deploy/`. ADR-0130 records its absence.

---

## Independent end-to-end test procedure *(for whoever implements the ADR)*

Recorded now because the evidence is in hand, and because the honest test here
is not obvious.

**Do not write a guard that reads `smart-sentinel-eye-realm.json` and asserts
`offline_access` is absent from a declared composite.** Experiment A proves such
a guard would be green over a no-op: the file can declare the narrowing, the
import can accept it, and the running realm can ignore it. This is the exact
defect ADR-0134 records against spec 050 and the repository records against
#2054.

**The honest test mints against the running provider, and it is available.**
`tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs`
already does this shape — it creates a client against the live Keycloak via
`HttpKeycloakAdminClient`, reads **effective** realm roles, and deletes it in a
`finally`. The red test for #1995 is its sibling:

1. Create a user directly through the admin API — the *hand-made* path, not
   enrolment. This is the control ADR-0134 names.
2. Read that user's **effective** realm role mappings
   (`/role-mappings/realm/composite`).
3. Assert `offline_access` is **absent**. Red today: it is present.
4. Delete the user in a `finally`.

A second assertion should pin the blast radius rather than assume it: an
operator account must still obtain a token through `management-web` after the
change. `aspire.GetAccessTokenAsync(...)` covers that.

**Realm-freshness trap.** If the chosen mechanism touches the imported realm,
the red test needs a **fresh** realm: a reused `keycloak-data` volume keeps the
old one and `WithRealmImport` is silently ignored, so the volume must be
**dropped**, not the container restarted. If the chosen mechanism is a runtime
admin-API call, this does not apply — which is one more reason mechanism 2 and
mechanism 1 differ in cost.

**Client-description trap.** Any realm-file edit must keep every client
description under 255 characters or the import fails and the whole fixture
hangs. There is a test for it (ADR-0134).

---

## Latency budget (constitution §IV)

**N/A — no leg.** This is realm role composition, evaluated at sign-in and token
refresh. It is not on the `event arrival → overlay rendered` path: it touches
neither camera→SFU, SFU→decode, the presentation buffer, event→overlay state,
nor composite-and-render.

---

## Why there is no plan.md and no tasks.md

Writing them would require choosing among FR-Q2's three mechanisms, and that
choice **is** the decision the lane may not make. A plan that picked one and a
task list that decomposed it would present a settled design over an open
question — which is worse than stopping, because the next reader would take the
choice as made. ADR-0037's phase-1 gate is *"no `[NEEDS CLARIFICATION]` left"*,
and the ones here are not clarifiable by asking better; they are decidable only
by a human writing an ADR.

**`[NEEDS ADR]`** — FR-Q1 through FR-Q5.

---

## Assumptions, marked

- **The running realm is the imported realm.** Measurements were taken against a
  Keycloak container up for 14 hours whose realm was not re-imported during this
  investigation. Its composite, its users' role mappings and its per-client
  scopes are all consistent with `smart-sentinel-eye-realm.json` (four wall
  displays with `offline_access`, `kiosk-wall` alone offering the scope as
  optional), so the stale-volume trap does not appear to have bitten here — but
  this was inferred from agreement, not from a fresh import.
- **The `manage-realm` measurement is inherited, not re-run.** #1995 and ADR-0134
  measured it independently and agree. This investigation confirmed only that the
  narrowing *is possible* through the admin API and *is retroactive*, using the
  bootstrap admin.
- **Keycloak 26.6.** Variant A and B behaviour is version-specific; the running
  container and the probe container were both 26.6.
