import type { CreateOverlayDraftInput } from '@smart-sentinel-eye/shared/api/overlays.schema';
import type { FieldErrors } from 'react-hook-form';

/** The selected label's text error, if the schema refused it. */
export function TextErrorAlert({
  errors,
  selectedIndex,
}: {
  errors: FieldErrors<CreateOverlayDraftInput>;
  selectedIndex: number;
}) {
  const textErrorMessage = (errors.elements?.[selectedIndex] as { text?: { message?: string } } | undefined)?.text
    ?.message;
  if (textErrorMessage === undefined) return null;
  // #2365: a stable testid so a test can address this alert without an
  // unscoped role query, now that OverlayEditor's own OverlayGeometryFields
  // always mounts four `role="alert"` regions in the same tree.
  return (
    <p role="alert" data-testid="overlay-editor-dialog-label-error" className="text-sm text-accent-fault">
      {textErrorMessage}
    </p>
  );
}
