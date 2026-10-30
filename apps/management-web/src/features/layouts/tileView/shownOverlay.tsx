import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import type { Overlay, OverlayElement, OverlayRevision } from '@smart-sentinel-eye/shared/api/overlays.api';
import { useResolveOverlayTextQuery } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import type { CameraViewerOverlay } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';

export interface Shown {
  revision: OverlayRevision | undefined;
  notice: string | null;
}

/** What the wall would show for this overlay, or the draft with a warning, or nothing. */
export function revisionToShow(overlay: Overlay | undefined): Shown {
  if (overlay === undefined) return { revision: undefined, notice: null };
  const published = overlay.revisions.find((revision) => revision.state === 'Published');
  if (published !== undefined) return { revision: published, notice: null };
  const draft = overlay.revisions
    .filter((revision) => revision.state === 'Draft')
    .sort((a, b) => b.revisionNumber - a.revisionNumber)[0];
  if (draft !== undefined) {
    return {
      revision: draft,
      notice: 'This overlay is not published, so the wall shows nothing for it. Shown here: its draft.',
    };
  }
  return { revision: undefined, notice: 'This overlay was archived and is not shown on the tile.' };
}

/** Resolves one text with the editor's own resolver and reports it upwards, keyed by the raw text. */
function TextResolver({ text, onResolved }: { text: string; onResolved: (text: string, resolved: string) => void }) {
  const { data, isError } = useResolveOverlayTextQuery({ text });
  const resolved = data?.resolvedText;
  useEffect(() => {
    if (resolved !== undefined) onResolved(text, resolved);
    else if (isError) onResolved(text, text);
  }, [text, resolved, isError, onResolved]);
  return null;
}

/**
 * The elements with their placeholders resolved, and the resolvers that do it. The caller renders
 * `resolvers` once, anywhere: one child per distinct placeholder text.
 */
export function useResolvedElements(raw: readonly OverlayElement[]): {
  elements: CameraViewerOverlay[];
  resolvers: ReactNode;
} {
  const [resolvedTexts, setResolvedTexts] = useState<Record<string, string>>({});
  const onResolved = useCallback((text: string, resolved: string) => {
    setResolvedTexts((previous) => (previous[text] === resolved ? previous : { ...previous, [text]: resolved }));
  }, []);

  const elements = useMemo<CameraViewerOverlay[]>(
    () =>
      raw.map((element) =>
        element.kind === 'Text'
          ? { ...element, text: element.text.includes('{{') ? (resolvedTexts[element.text] ?? '') : element.text }
          : element,
      ),
    [raw, resolvedTexts],
  );
  const placeholderTexts = useMemo(
    () => [
      ...new Set(
        raw.flatMap((element) => (element.kind === 'Text' && element.text.includes('{{') ? [element.text] : [])),
      ),
    ],
    [raw],
  );

  return {
    elements,
    resolvers: placeholderTexts.map((text) => <TextResolver key={text} text={text} onResolved={onResolved} />),
  };
}
