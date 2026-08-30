# Specification Quality Checklist: A wall comes back on its own

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
- [x] Success criteria are technology-agnostic (no implementation details)
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

**The first draft was a protocol document, and had to be rewritten.** It named
the flow, the library, the grant type and the token endpoint throughout. Those
belong in the plan: naming them in a spec picks the shape before anyone weighs
the three options, and two of the three would have been ruled out by vocabulary
rather than by argument. The rewrite says what a person in a fab experiences —
*a screen comes back by itself, and does not drop out twice a day*.

**One thing kept its precision deliberately.** "A browser cannot keep a secret"
is stated plainly rather than softened, because it is the constraint that makes
the obvious plan wrong, and a reader who misses it will propose that plan. It is
phrased as a property of web pages rather than as a framework detail.

**A second user story was added that the source issue does not mention.** Issue
1976 is about reboots. Reading the running system showed a hard ten-hour session
ceiling, so a wall that never reboots still drops out roughly twice a day. That
is the more frequent failure, and a spec covering only reboots would have let
the record claim the target was met while the worse problem remained. It is
ranked P1 alongside the reboot story rather than below it.

**No [NEEDS CLARIFICATION] markers**, and that is deliberate. The real open
question — which of the three shapes to build — is not a clarification: all
three are coherent, they differ in what the kiosk is allowed to become, and the
spec presents them with a cost table so planning can decide. Blocking here would
be asking for an answer the requirements do not need.

**Verified by reading the system rather than recalled**, because a spec built on
a half-remembered number would mis-scope the work:

| Claim | How it was established |
|---|---|
| The kiosk signs in interactively as a public client | `apps/kiosk-web/src/app/auth.ts` |
| Failure lands on a full-screen prompt needing a person | `useSessionExpiry.ts` |
| Per-device confidential credentials are minted, secret revealed once | `EnrollKioskCommandHandler`, `KioskCredentialsDto` |
| Nothing consumes them | searched both front-end packages for the flow, the secret, and the loader named in the decision |
| Session ceiling **10 hours**, idle cut-off **30 minutes** | queried the running identity service's realm |
| Long-lived grants kept **30 days**, with no hard ceiling | same query — which is what makes the middle option worth weighing |

The last two matter most: they were **observed on the running system**, not taken
from documentation or defaults, and the ten-hour figure is the reason this spec
has two P1 stories instead of one.
