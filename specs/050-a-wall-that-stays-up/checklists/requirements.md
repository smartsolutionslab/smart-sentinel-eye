# Specification Quality Checklist: A wall that stays up

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-30
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

**This spec had to avoid naming the mechanism, which was harder than usual.**
The decision behind it is already made and recorded on the issue, so the
temptation was to write the plan and call it a spec. Every draft that named the
grant type, the privilege or the provider read as a configuration change rather
than as *a wall that stops going dark twice a day*. The final text says what a
fab experiences and leaves the mechanism to planning, where the one genuinely
uncertain part of it lives.

**US2 is deliberately P1 alongside US1**, and that ranking is the substance of
the feature rather than a formality. The whole change is "let this credential
live longer", so what the credential can do is the trade, not a detail. Spec 049
refused this feature once on exactly that ground, and a version of it that
quietly widened operator authority would be a worse outcome than the twice-daily
prompt it fixes. FR-006 states it as a requirement so it has to be shown, not
argued.

**No [NEEDS CLARIFICATION] markers**, because the decision they would have asked
about was taken before the spec was written. What remains open is recorded as
*open for planning* — three questions, one of which matters more than the other
two: how the privilege reaches the screen **without the application naming it**.
Naming a permission the provider has not granted fails the entire sign-in, which
took every kiosk down during spec 049. The spec flags that this must be verified
rather than assumed, because assuming is precisely what caused that outage.

**Verified rather than recalled**, since a spec resting on a misremembered number
would mis-scope the work:

| Claim | How it was established |
|---|---|
| Idle cut-off 30 minutes, hard ceiling 10 hours | queried from the running identity provider during spec 049 |
| Long-lived grants kept 30 days, no ceiling | same query |
| Fab scoping comes from the account, not the client | read from the realm — an existing account is scoped `/fabs/munich` |
| No wall-display account exists today | read from the realm: every human account carries the same base role, and a screen signs in as an operator |
| The privilege cannot be minted by the system | the identity admin client creates clients and rotates secrets; it has no user operations |

The last two are why the account is declared rather than created by enrolment,
and why this does not become a device-management feature.
