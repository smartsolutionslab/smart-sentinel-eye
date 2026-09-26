// Spec 266 (issue #2335) plan.md §7 — jsdom does not implement every DOM API
// Radix's popper and Select call. Without these, mounting a Select/Popover/
// DropdownMenu/CommandPalette trigger under Vitest throws
// `TypeError: element.hasPointerCapture is not a function` (or
// `scrollIntoView`, or `ResizeObserver is not defined`) before a single
// assertion runs.
//
// Every implementation here is a **no-op**, installed only when the API is
// absent — real-browser behaviour (does the popper actually reposition, does
// pointer capture actually happen) is never asserted from a unit test; it is
// covered by the e2e specs that already exercise each consumer in Chromium
// (spec 266 §5.4/§5.4a). This module exists purely so Radix's own internals
// stop throwing under jsdom, not to model what those APIs do.
//
// This workspace's `setup.ts` runs for every test file regardless of its
// `@vitest-environment` — several files here declare none and run under
// Vitest's default `node` environment, where `Element` does not exist at
// all. Everything below is meaningless outside a DOM, so it is skipped
// there rather than throwing `ReferenceError: Element is not defined`
// before those files' own tests get a chance to run.
if (typeof Element !== 'undefined') {
  // `Element.prototype.hasPointerCapture` / `setPointerCapture` /
  // `releasePointerCapture` — called by the Select trigger
  // (`@radix-ui/react-select`, `dist/index.mjs` line 211).
  if (typeof Element.prototype.hasPointerCapture !== 'function') {
    Element.prototype.hasPointerCapture = () => false;
  }
  if (typeof Element.prototype.setPointerCapture !== 'function') {
    Element.prototype.setPointerCapture = () => {};
  }
  if (typeof Element.prototype.releasePointerCapture !== 'function') {
    Element.prototype.releasePointerCapture = () => {};
  }

  // `Element.prototype.scrollIntoView` — called by the Select content
  // (`dist/index.mjs` lines 341 and 1041) to keep the highlighted item in view.
  if (typeof Element.prototype.scrollIntoView !== 'function') {
    Element.prototype.scrollIntoView = () => {};
  }
}

// `ResizeObserver` — used by every Radix popper via
// `@radix-ui/react-use-size`, which every floating primitive here (Select,
// DropdownMenu, Popover) depends on. Also DOM-only, guarded the same way.
if (typeof globalThis.ResizeObserver !== 'function' && typeof window !== 'undefined') {
  class NoopResizeObserver {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  }

  globalThis.ResizeObserver = NoopResizeObserver as unknown as typeof ResizeObserver;
}
