# ADR-0077: UI System — Custom Design System on Radix UI Primitives + Tailwind CSS

**Status:** Accepted
**Date:** 2026-05-25

## Context

The frontend covers both a dense, data-heavy admin/operator UI and a
high-contrast, low-distraction kiosk wall display. No off-the-shelf
component library fits both aesthetics out of the box, and we want
full ownership of the design language.

## Decision

Build a **custom design system in `apps/shared/ui/`**:

- **Primitives** built on **Radix UI** (`@radix-ui/react-*` headless
  accessible components — Dialog, DropdownMenu, Popover, Tooltip,
  Tabs, Slider, Select, ScrollArea, etc.).
- **Styling** via **Tailwind CSS** referencing design tokens defined
  as CSS custom properties (ADR-0078).
- **All visual code lives in the repo** — no shadcn copy-paste,
  no MUI, no Mantine, no Ant.

```
apps/shared/ui/
  tokens/         (CSS custom properties: colors, spacing, type, motion)
  primitives/     (Button, Input, Dialog, Select, Tooltip on Radix)
  composites/     (DataTable, FormField, CameraCell, OverlayCanvas)
  icons/          (curated icon set, not a full library)
```

## Consequences

- **Positive:** zero accessibility debt (Radix handles ARIA, focus,
  keyboard navigation, portals).
- **Positive:** full visual control; no vendor evolution risk.
- **Positive:** smallest production CSS via Tailwind's JIT.
- **Negative:** 1–2 weeks of bootstrapping primitives before feature
  UI lands. Acknowledged.
- **Negative:** maintenance ownership — bugs in primitives are ours.

## Alternatives Considered

- **shadcn/ui as starting copy** — practical but loses the
  "no external code" purity.
- **Mantine v7** — comprehensive but couples to a vendor.
- **MUI** — Material aesthetic incongruent with control-room UI.
- **From scratch with no Radix** — months of accessibility work.
