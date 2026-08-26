# Specification Quality Checklist: One client scope supplies `sub`

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-26
**Feature**: [spec.md](../spec.md)

## Content Quality

- [ ] No implementation details (languages, frameworks, APIs) — **deliberately not met, see below**
- [x] Focused on user value and business needs
- [ ] Written for non-technical stakeholders — **deliberately not met, see below**
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [ ] Success criteria are technology-agnostic — **deliberately not met, see below**
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover the strategic flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [ ] No implementation details leak into specification — **deliberately not met, see below**

## Notes

### Four items are unticked on purpose, and that is the second rewrite

The template asks for a specification free of implementation detail and readable
by a non-technical stakeholder. **This spec is neither, by decision.**

The first version obeyed the template. It called Keycloak "the sign-in service",
`sub` "the naming piece", a client scope "a permission list entry", and the realm
file "the configuration". It read well and it was harder to act on than the
subject deserved: **the entire subject is a JSON file and a JWT claim.** There is
no user-facing behaviour to describe in user-facing words — the observable
outcome is that 32 log lines stop appearing and one unused client stops returning
401. Abstracting that produced a document a reviewer had to translate back before
they could check whether it was true.

So the four items stay unticked rather than being argued away. A checklist that
gets talked out of its own criteria is worth nothing; one that records a
deliberate exception and its reason is worth something. If a reviewer disagrees,
the item is right there to disagree with.

**What was kept from the template's intent**: every requirement is still testable,
every success criterion still measurable, and the *reasons* are still in
prose — why a permission scope should not decide identifiability, why service
accounts were never affected, why the fail-closed refusal is correct. Those are
the parts a reader cannot reconstruct from the diff.

### The first draft's count was wrong

**It claimed six of eight clients could not mint `sub`. One cannot.**

It was read off the realm file — which clients carry an `oidc-sub-mapper` — and
this checklist then asserted that *"tokens were minted for every identity in
turn"*. They had not been. Four had been minted earlier in the day for spec 041;
the other four were inferred.

Measured properly, one access token per client:

| Client | `sub` | Why |
|---|---|---|
| `smart-sentinel-eye-web` | yes | `sse.management`'s mapper |
| `kiosk-web` | yes | its own mapper (spec 041) |
| **`management-web`** | **no** | nothing supplies it |
| the five service accounts | yes | the `client_credentials` grant |
| a client created via the Admin API | yes | the same |

**`client_credentials` supplies `sub` from the service-account user with no
mapper involved.** Nothing in the repository says so, which is why the file
appeared to say otherwise. FR-011 exists to write it down; without it the six
working clients look like luck, and the next reader recounts them as broken, as
this spec's own author did.

That error is left in the spec's Assumptions rather than tidied away. It is the
same mistake the realm file makes — asserting from configuration what only a
token settles — committed while specifying exactly that.

### Validation calls that survived both rewrites

**`ToOperatorIdentifier`'s refusal is presented as correct, not as the bug.** It
401s rather than attribute a write to a fabricated operator, and says so where it
is enforced. Framing that as the defect would invite a fix that softened it.

**US1 outranks the broken client.** Fixing `management-web` and leaving 32
discarded entries would repair an instance and keep the mechanism — and the
mechanism has now produced two defects in two weeks.

**FR-010 and SC-007 exist because the blast radius is every client at once.**
Nothing here should change what any client may do, and the only way to know is to
compare the `scope` claim per client before and after.
