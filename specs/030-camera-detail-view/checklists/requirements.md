# Specification Quality Checklist: Open one camera, and fix it

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-24
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

**16 of 16 pass, and no clarification markers.** That is unusual and worth
justifying rather than celebrating: the decision that would normally have been
one — whether the detail view gets a real location, which forces a router — was
settled by evidence instead of judgement, and the evidence is in the repository.

### Where the routing decision came from

| Checked | Found |
|---|---|
| Is a router a dependency? | **Yes** — `react-router-dom` 7.1.3, in both apps |
| Does anything use it? | **kiosk-web does**: `createBrowserRouter`, `RouterProvider`, `useNavigate`, `useParams`. management-web does **not** |
| What does the shell say? | *"A real router lands when more than three surfaces exist"* — there are now **six** |

So routing is neither a new dependency nor a new pattern, and the app's own
stated trigger passed two surfaces ago. Recording that in Assumptions with the
evidence is more useful than asking a question whose answer is already sitting
in `package.json`.

### The one decision a reviewer should push on

**Converting the whole shell** rather than routing the cameras surface alone is
recorded as *a decision to overturn rather than a consensus to cite* — the
convention spec 028 and spec 029 both used for a recommendation adopted without
an explicit answer. FR-002 survives either way; overturning it costs only the
coherence argument (one routed surface beside five toggled ones), and shrinks
the feature.

### Deliberately kept out

Framework names appear **only** in Assumptions, where they justify a cost
estimate, never in requirements or success criteria — those are phrased as
"has its own location", "can be linked to, bookmarked and returned from". A
reviewer who rejects the router still has testable requirements.

### The claim most likely to be wrong

**"This is a frontend feature."** Assumptions says so and also says what to do
if it is not: a needed backend change contradicts spec 029's contract and is a
finding to raise. That inversion is deliberate — spec 029's own research caught
spec 028 having inferred "no code needed" from a schema, and the same habit is
worth carrying forward.
