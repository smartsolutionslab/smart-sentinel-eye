# ADR-0191: The migration-runner account stays deniable by identity-admin, as an accepted residual

**Status:** Accepted (owner, 2026-10-26 — "accept the residual", on #3120)
**Date:** 2026-10-26
**Supersedes:** —
**Superseded by:** —
**Amends:** —

## Context

ADR-0187 and ADR-0188 confine the migration runner's Keycloak account. Their
matrix leaves one residual open. A live probe on Keycloak 26.6.4 (recorded on
#3120) showed that `identity-admin` can still:

- **disable** the `service-account-migration-runner` user (HTTP 204), and
- **delete** it (HTTP 204), through `manage-users`.

`identity-admin` cannot take the account over. A reset-password request is
refused (400, "You can not manage credentials for this user"), and a password
grant for a service-account user returns `invalid_grant`. The residual is
therefore denial of the migration gate, not escalation. A deleted or disabled
runner blocks every boot, because the migration gate cannot authenticate.

Closing it means taking `manage-users` and `view-users` away from
`identity-admin` and granting fine-grained Users, Groups and Roles permissions
in their place. That is ADR-0188's Alternative (c), which was rejected in
writing. It would widen ADR-0187 Decision 5 (clients only) and the Decision 6
guard, and ADR-0186 Decision 5 records that a realm-management role is not
restricted by FGAP v2 for these probes.

## Decision

1. **The residual is accepted and not closed now.** `identity-admin` keeps
   `manage-users`, and the migration-runner account can still be disabled or
   deleted by that role. This is recorded as an accepted residual, consistent
   with ADR-0187's own acceptance of it (owner, 2026-10-24).
2. **Re-open trigger.** This decision is revisited when any of these holds:
   - identity-admin is given a path to the migration runner's realm or client
     secret, which would convert the residual into escalation;
   - Keycloak changes how `manage-users` interacts with FGAP v2 for users;
   - a boot failure is traced to the runner being disabled or deleted.
3. **Detection, not prevention.** A disabled or deleted runner is a boot
   failure with a clear cause. The migration gate's error names the missing
   principal, so the operator restores it rather than debugging the boot.

## Consequences

- No realm, role or FGAP change is made. ADR-0187 and ADR-0188 stand as
  written.
- An operator with identity-admin can stop a boot. That is a deliberate,
  documented capability, not a hidden one.
- The probe evidence in #3120 is the record of what was tested. A later
  Keycloak version may change the result; re-running the probe is the first
  step when a re-open trigger fires.

## Alternatives Considered

- **Remove `manage-users` and `view-users` from `identity-admin`, grant FGAP
  permissions instead (ADR-0188 Alternative (c)).** Rejected for now: it widens
  two decisions that were already ruled, and the residual is denial, not
  escalation.
- **Give the runner a separate realm.** Rejected: it multiplies the realm
  management surface for one account (ADR-0159 keeps one realm per deployment).
- **Remove the runner's user entirely and gate migrations on a client
  credential only.** Rejected: ADR-0187 chose the user-backed gate, and this
  decision does not reopen that.

## Implementation Notes

None. This is a decision record. The re-open trigger and the probe are the
only follow-up work, and neither is scheduled.
