# ADR-0166: Async outcomes stay inline — no toast surface

**Status:** Accepted
**Date:** 2026-10-06
**Supersedes:** —
**Superseded by:** —

**Relates to:** spec 266 (#2335) §3.2, which named this decision and
deliberately left it unmade; issue #2636, which this ADR resolves.

**Drafting note.** Scoped narrowly per the product owner's explicit choice on
#2636: answer only "toast vs. inline, and which app" now. Persistence,
stacking and `aria-live` politeness are not answered here — they are
properties of a toast surface, and this ADR decides not to build one.

## Context

Spec 266 found no notification surface in either app and recorded the absence
as partly deliberate: several specs chose inline `role="alert"` /
`role="status"` so an outcome appears where the operator acted, rather than
introducing a toast that moves it elsewhere. `@radix-ui/react-toast` is not
installed in either app's `package.json`.

A grep of both apps confirms this is not an oversight but the actual,
load-bearing pattern:

- **`apps/management-web`**: `role="alert"`/`role="status"` already renders
  the outcome of every async action with one — `RegisterCameraDialog`,
  `RenameCameraDialog`, `RetireCameraDialog`, `EditCameraAddressDialog`,
  `RuleDialog`, `SystemVariableDialog`, `WallForm`, `OverlayEditorDialog`,
  `GridDesigner`, `DryRunPanel`, `ShellLayout`, plus the shared composites
  `FaultNotice`, `ChainRecoveryNotice`, `FormErrorSummary` — each tied to the
  element the operator just acted on.
- **`apps/kiosk-web`**: the same philosophy, at wall scale — `KioskCrashRecovery`,
  `ReconnectingScreen`, `NotAuthorizedScreen`, `TileAlignmentBadge`,
  `LayoutGrid` all surface state inline, in place, never as a transient
  overlay independent of the tile or screen it describes.

Every async outcome either app has ever needed to show has had a natural
place to render: the dialog or page that triggered it, or the tile the
outcome is about. Nothing in either codebase has a use case — a background
operation with no visible trigger, a cross-page notification, an outcome that
must survive navigation — that inline rendering cannot already satisfy.

## Decision

**No toast surface is introduced, in either app.** Async outcomes continue to
render inline, at the element that triggered them, using the existing
`role="alert"` / `role="status"` convention already established across both
apps.

This resolves the first of spec 266's open questions ("which outcomes toast,
and which stay inline?") by answering: none toast, all stay inline. The
remaining questions it listed — persistence, stacking, `aria-live`
politeness, whether a toast ever reaches kiosk-web — are therefore moot: they
are properties of a surface this ADR decides not to build.

## Consequences

- #2636 is resolved; no `@radix-ui/react-toast` (or equivalent) dependency is
  added to either app.
- Any future feature whose outcome genuinely has no natural inline anchor —
  a background job with no triggering dialog, a cross-page async result — is
  a **new decision**, not covered by this ADR. That decision should state
  concretely what case inline cannot handle before reopening the question;
  this ADR's evidence is that no such case has arisen in either app to date.
- Existing and future dialogs/pages keep using `role="alert"` /
  `role="status"` as the one feedback convention, which is already consistent
  across both apps and needs no new primitive, pattern, or accessibility
  review to adopt elsewhere.

## Alternatives Considered

- **Build a toast primitive now, answer the remaining questions
  (persistence/stacking/politeness) up front.** Rejected: spec 266 explicitly
  found no current need, and this session's grep confirms every existing
  async outcome already has an inline home. Building the infrastructure and
  its policy answers before a real use case exists would mean guessing at
  properties (how long does it persist? how do several stack?) with nothing
  to validate the guess against.
- **Decide per-outcome, case by case, as new features ship.** Rejected: this
  is exactly the ambiguity #2636 was filed to close. A standing "no toast,
  inline only" rule is simpler to apply consistently than a judgment call
  repeated on every new dialog.
- **Toast for kiosk-web only (wall-scale announcements), inline for
  management-web.** Considered because a wall's failure modes are more
  "ambient" than a single operator's dialog interaction. Rejected: kiosk-web's
  existing inline screens (`ReconnectingScreen`, `KioskCrashRecovery`,
  `NotAuthorizedScreen`) already cover exactly that ambient case, at the tile
  or screen the state is about — a toast would duplicate, not add, coverage.
