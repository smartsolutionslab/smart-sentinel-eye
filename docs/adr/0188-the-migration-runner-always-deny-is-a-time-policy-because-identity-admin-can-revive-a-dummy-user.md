# ADR-0188: The migration-runner always-deny is a time policy, because identity-admin can revive a dummy user

**Status:** Proposed
**Date:** 2026-10-24
**Amends:** ADR-0187 (Decision 1 — the second policy of the migration-runner deny; Decision 2 — what the realm-file guard asserts about it). ADR-0187 Decision 3 (multi-principal probes), ADR-0186 Decisions 1-6 as amended, and the accepted residuals (#3120, #3121) stand unchanged.
**Relates to:** ADR-0186, ADR-0144, spec 440 (branch `fix/1995-narrow-default-roles`, `research.md` § *Probes*, commit `7560a8ef`, and § *T150 / T151*, commit `6c5ee456`), issue 1995

## Context

ADR-0187 Decision 1 closes the deny for every principal but `identity-admin` with a `POSITIVE` user
policy naming one seeded, disabled, credential-less dummy user. Probes on 2026-10-24 (Keycloak 26.6.4,
throwaway container, the committed realm file plus that variant imported through `--import-realm`)
confirmed the two-policy deny holds for the five probed principals — and then broke it with the
`identity-admin` token alone (H-1). `identity-admin` keeps `manage-users`, because enrolment needs it:

| Step (as `identity-admin`) | Status |
|---|---|
| `PUT users/{dummy}` `enabled: true` | 204 |
| `PUT users/{dummy}/reset-password`; set email, names, `emailVerified` | 204 |
| password grant as the dummy | 200 |
| dummy: `GET` / `PUT` migration-runner, `GET` / `POST` its client-secret | 200 / 204 / 200 / 200 |

The dummy is the one user the positive policy names and is not `identity-admin`, so it satisfies both
policies. A `PUT` replacing `jwks.string` is a takeover of the `manage-realm` holder in three hops.
The disabled, credential-less state ADR-0187 Decision 2 asserts is only as strong as `identity-admin`'s
inability to change it, and `manage-users` is exactly that ability.

Deleting the dummy (H-2) is **not** a hole: Keycloak keeps the policy with `users: []`, both policies
stay associated, the deny holds. The dummy's absence is harmless; its presence is the vulnerability.

Three replacements were measured on the throwaway:

| Second policy | Five-principal matrix, enrolment | Holes |
|---|---|---|
| (a) `time` policy with only `noa: 2000-01-01 00:00:00` | = baseline (403 all; `identity-admin` list filter keeps migration-runner out) | none found: no user, nothing `identity-admin` can edit, enable or delete (authz endpoints 403); migration-runner's own token 403 |
| (b) `client` policy naming migration-runner | = baseline | migration-runner's own token `GET`s itself 200 — a self-grant on the schema-owner credential |
| (c) keep the dummy; drop `manage-users`/`view-users` from `identity-admin`, add FGAP Users/Groups/Roles permissions without `manage` | = baseline | closes H-1 only while `manage` on Users stays absent; three extra permissions and a role change |

For (a), the counterfactual `noa: 2099-01-01 00:00:00` (the policy matches) reopened the client to
`scenario-simulator` (200/204/200), so the time policy is what closes the deny. The round trip was
observed: a second throwaway imported the realm file with that policy, `associatedPolicies` listed both,
the matrix and enrolment equalled the baseline, and both held after `docker restart`.

## Decision

**1. The deny's second policy is a never-matching time policy; no dummy user is seeded** (replaces
ADR-0187 Decision 1's second bullet and its dummy-user paragraph). The migration-runner-only permission
stays `UNANIMOUS` and carries:
- the existing `NEGATIVE` user policy naming `service-account-identity-admin` (unchanged); and
- a `POSITIVE` `time` policy whose only constraint is a past `noa` (not-on-or-after), as measured:

```json
{"name":"deny-migration-runner-never","type":"time","logic":"POSITIVE","decisionStrategy":"UNANIMOUS",
 "config":{"noa":"2000-01-01 00:00:00"}}
```

with `deny-migration-runner.config.applyPolicies` =
`"[\"identity-admin-user-deny\",\"deny-migration-runner-never\"]"`. Names are an implementation choice;
the shape is not.

**2. The guard requires an always-deny no principal can satisfy and nobody with `manage-users` can
alter** (replaces ADR-0187 Decision 2's dummy-user assertions; the NEGATIVE-only ban stays). The
realm-file test asserts that every FGAP deny permission is `UNANIMOUS` and applies the `NEGATIVE`
`identity-admin` user policy **and** a `time` policy whose only config key is a `noa` in the past. It
fails any always-deny built on a user, group, role or client policy — every subject someone holding
`manage-users` (or the denied client itself) can alter or present. Prove each by counterfactual, all
red: drop the time policy; set `noa` in the future; add a second time constraint; replace it with a
user policy naming a dummy; drop the `NEGATIVE` policy.

**3. Lesson, recorded as a rule:** the subject of a deny must not be an object the confined principal
can mutate. ADR-0187 Decision 3's probes check principals; a deny proposal also lists every object its
policies name and who can write to each.

## Consequences

**Positive:** no load-bearing user in the realm file; nothing for `identity-admin` to enable, repoint or
delete; the file keeps the same two-policy block shape ADR-0187 already put in spec 440.

**Negative:** the deny now depends on the Keycloak clock reading after 2000-01-01. A clock that far
wrong breaks token validation first.

**Negative: not verified — the import round trip on the shipped bytes.** T151 observed the round trip
for this policy shape added to the committed file, not for the byte-identical file the implementation
writes. The implementing PR repeats it on that file (Aspire fixture), as ADR-0187 already required.

**Unchanged residuals:** `identity-admin` can disable or delete `service-account-migration-runner`
(#3120); `admin-permissions`' service account and rotatable secret are kept and stay in the probe set
(#3121).

## Alternatives Considered

**ADR-0187 as written (dummy user).** Rejected: H-1 confirmed, a three-hop `manage-realm` takeover.

**(b) Client policy naming migration-runner.** Rejected: measured to grant migration-runner `view`/
`manage` on itself.

**(c) Narrow `identity-admin` to FGAP Users permissions without `manage`.** Rejected as heavier: a role
change plus three permissions, and granting `manage` on Users later — the obvious fix for any user-edit
need — silently reopens H-1.

## Implementation Notes

Spec 440's `plan.md` and `tasks.md` take this amendment in before T160, T161, T165 and T171 resume;
phase 4a stays **red** (the guard and the H-1 probe, as a
multi-principal test, against the branch's current realm file). `security-reviewer` mandatory at phase 6. On acceptance, ADR-0187 gains an
`Amended by: ADR-0188` line.

The REST create used `"notOnOrAfter"` in the request body; Keycloak stores and exports it as
`config.noa`. The realm file carries the exported form.
