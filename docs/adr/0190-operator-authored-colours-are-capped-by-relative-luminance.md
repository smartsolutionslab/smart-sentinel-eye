# ADR-0190: Operator-authored overlay colours are capped by relative luminance

**Status:** Accepted (owner, 2026-10-26 — option (c) on #3108)
**Date:** 2026-10-26
**Supersedes:** —
**Superseded by:** —
**Amends:** ADR-0146 (scope of the static-bright-region ban)

## Context

Overlay elements carry a colour in the domain and the contract, but the editor
offered no way to author one (spec 300 US4, never built; #3108). Building it
raises a question ADR-0146 does not answer: its ban on static bright regions
larger than a status chip is written for the console's own chrome. It does not
say whether a colour an operator types in is subject to the same limit.

The hazard predates the editor. The API has accepted any legible hex since
spec 300, so a bright colour can already be stored and painted on the
unattended kiosk wall, which ADR-0146 exists to protect.

## Decision

1. **The limit applies to operator-authored colours.** A colour an operator
   authors must not exceed a WCAG relative luminance of **0.35**.
2. **Enforcement is a domain invariant** on the colour value object. The API and
   the editor both reject a colour above the limit, and no write path can store
   one. Nothing is enforced only at the kiosk.
3. **Existing stored colours that break the limit are migrated** to the nearest
   allowed colour. The migration records each row it changed, so no overlay
   changes colour silently on read.
4. The palette model is unchanged: design tokens plus free hex (owner decision,
   2026-10-23, #3108). Free hex is subject to item 1.

## Consequences

- The wall cannot be given a bright static region by an operator, whichever
  surface they author it on.
- Existing rows may change colour once, at migration. The record of which rows
  changed is the operator's audit of that change.
- The value object gains a rejecting constructor, so every existing caller of
  the colour type is affected. Callers are enumerated in the implementing spec.
- The render leg of constitution §IV (composite and render) is affected, because
  colour changes what the kiosk paints. The implementing spec must cite the leg
  and measure it; this ADR does not claim a figure.

## Alternatives Considered

- **(a) The limit covers UI chrome only.** Rejected: the existing API already
  accepts bright colours, so the wall would stay exposed to colours stored today.
- **(b) An editor warning only.** Rejected: a warning does not stop a colour
  written through the API, and it does not reach stored rows.
- **Enforcement at the API only.** Rejected: a second write path could bypass it,
  and the domain would still hold an invalid value.
- **Clamp at render in the kiosk.** Rejected: the operator would see one colour and
  the wall would show another.
- **Reject stored bright colours on read.** Rejected: it can break a live wall.
- **A flag-only migration (leave existing rows, report them).** Rejected: the
  wall would keep the bright colours the decision exists to remove.

## Implementation Notes

- The nearest-allowed-colour rule for the migration (how to move a colour toward
  the limit while keeping its hue) is not decided here. The implementing spec
  settles it and records the choice.
- The luminance formula and the cap constant live in one place, with a test that
  the limit holds for every token in the palette.
