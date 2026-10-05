// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

/**
 * Spec 146 (issue #2339). `CameraViewer.OverlayLabel` is the wall's paint —
 * this refactor's entire proof is that folding its style into a shared
 * function does not move a single one of these values. Captured green
 * against today's (unfolded) shape and left unmodified after the fold
 * (plan.md "Phase 4a", T002/T008).
 *
 * Per-property assertions only: React serialises inline styles in object
 * key-insertion order, and the fold changes that order (shared properties
 * arrive via a spread). Asserting the whole `style` attribute as one string
 * would go red on the reorder alone — a false red on a characterisation
 * test that must not move.
 */
describe('CameraViewer.OverlayLabel style (characterisation — must not move)', () => {
  afterEach(() => {
    cleanup();
  });

  function renderLabel() {
    useGetStreamQueryMock.mockReturnValue({ data: undefined, isLoading: false, error: undefined });

    render(
      <CameraViewer
        cameraIdentifier="cam-42"
        getToken={async () => null}
        overlays={[
          {
            kind: 'Text',
            color: '#FFFFFFD9',
            text: 'Production Line 1',
            normalizedX: 0.25,
            normalizedY: 0.05,
            normalizedWidth: 0.5,
            normalizedHeight: 0.1,
            fontSizePx: 48,
          },
        ]}
      />,
    );

    return screen.getByTestId('camera-viewer-overlay-label');
  }

  it('Positions the label at the overlay normalized coordinates', () => {
    const label = renderLabel();

    expect(label.style.position).toBe('absolute');
    expect(label.style.left).toBe('25%');
    expect(label.style.top).toBe('5%');
    expect(label.style.width).toBe('50%');
    expect(label.style.height).toBe('10%');
  });

  it('Centres its content with a flex box', () => {
    const label = renderLabel();

    expect(label.style.display).toBe('flex');
    expect(label.style.alignItems).toBe('center');
    expect(label.style.justifyContent).toBe('center');
  });

  it('Paints the wall background at 0.85 alpha with no border', () => {
    const label = renderLabel();

    // Spec 293 §6 (US2): background is now var(--color-bg-label) —
    // color-mix(in oklch, var(--white) 85%, transparent), the same 0.85
    // alpha, pinned in :root only (spec §5) — not a raw rgba() literal.
    expect(label.style.background).toBe('var(--color-bg-label)');
    expect(label.style.border).toBe('');
  });

  it('Sets the ink colour and weight', () => {
    const label = renderLabel();

    // Spec 293 §6 (US2): color is now var(--color-fg-on-label) —
    // var(--gray-900). A declared change, not "the same as before": the ink
    // moves from the old literal `#111827` to `--gray-900` (`#14171c`).
    expect(label.style.color).toBe('var(--color-fg-on-label)');
    expect(label.style.fontWeight).toBe('var(--font-weight-semibold)');
  });

  it('Sizes the type proportionally to its container (cqw), not the vw-derived clamp formula (spec 294)', () => {
    const label = renderLabel();

    // Supersedes "Sizes the type with the vw-derived clamp formula" (spec
    // 294, issue #2353, plan.md §6 (2), risk R2). jsdom 30.1.1's cssstyle
    // does NOT preserve `calc(48cqw / 19.2)` verbatim the way it preserves
    // `clamp(...)` — it arithmetically simplifies the division at parse
    // time (confirmed with a throwaway jsdom probe against this exact
    // string: `max(12px, calc(48cqw / 19.2))` -> `max(12px, 2.5cqw)`).
    // That is a *different* failure mode than the one R2 anticipated (an
    // empty string), so this case is adjusted to jsdom's own deterministic
    // normalization rather than deleted — an exact match, not a substring
    // or `toBeTruthy` weakening. `overlayLabelStyle.test.ts` pins the
    // authored `calc(${f}cqw / 19.2)` string unsimplified (no DOM
    // involved there); `e2e/kiosk-label-scales-with-its-tile.spec.ts` pins
    // the wall's real, browser-computed pixel value.
    expect(label.style.fontSize).toBe('max(12px, 2.5cqw)');
  });

  it('Pads the label at 4px and ignores pointer events', () => {
    const label = renderLabel();

    // Spec 293 §6 (US2): padding is now '0 var(--space-1)' — the same 4px
    // (--space-1 = 0.25rem). Not one of spec §5's pinned content roles —
    // that section covers colour roles only — just an ordinary :root token.
    // jsdom cannot parse the var() inside the shorthand, so it preserves the
    // authored string verbatim rather than normalising it to '0px ...' the
    // way it does a fully literal value.
    expect(label.style.padding).toBe('0 var(--space-1)');
    expect(label.style.pointerEvents).toBe('none');
  });
});
