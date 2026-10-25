import type { OverlayElement } from '../../api/overlays.api.js';

/** Spec 457. A decorative glyph for an element's kind; the chip's text already names it. */
export function OverlayKindIcon({ kind }: { kind: OverlayElement['kind'] }) {
  return (
    <svg
      aria-hidden="true"
      focusable="false"
      data-kind={kind}
      viewBox="0 0 16 16"
      width={16}
      height={16}
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      className="shrink-0"
    >
      {kind === 'Text' && <path d="M3 4h10M8 4v9" />}
      {kind === 'Box' && <rect x="2.5" y="3.5" width="11" height="9" />}
      {kind === 'Ellipse' && <ellipse cx="8" cy="8" rx="5.5" ry="4.5" />}
    </svg>
  );
}
