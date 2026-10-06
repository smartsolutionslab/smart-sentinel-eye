import { useNavigate } from 'react-router-dom';
import { OverlayDraftForm } from './OverlayDraftForm.js';
import { useCanvasFit } from './useCanvasFit.js';

/**
 * Spec 305 (#2350) US2 — `/overlays/new`. The dialog's create-mode body,
 * unchanged, now hosted on its own page so the canvas is not capped at the
 * dialog's 448px width (FR-006). `OVERLAY_NAME_TAKEN` keeps the operator here
 * with the existing message — `OverlayDraftForm`'s own conflict handling,
 * untouched — rather than navigating away on a refusal.
 */
export function OverlayCreatePage() {
  const navigate = useNavigate();
  const { fit, ref: containerRef } = useCanvasFit();

  return (
    <section ref={containerRef} className="p-6">
      <header className="mb-6">
        <h1 className="text-2xl font-semibold">New overlay</h1>
        <p className="text-sm text-fg-muted">
          Pick a name, type the label, and drag it to position. The overlay starts as a draft.
        </p>
      </header>
      <OverlayDraftForm
        onDone={() => navigate('/overlays')}
        onCancel={() => navigate('/overlays')}
        canvasWidthPx={fit?.widthPx}
        canvasHeightPx={fit?.heightPx}
      />
    </section>
  );
}
