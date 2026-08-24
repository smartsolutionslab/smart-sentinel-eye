import { useRetireCameraMutation } from '@smart-sentinel-eye/shared/api/cameras.api';
import { problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { ConfirmDialog } from '@smart-sentinel-eye/shared/ui/primitives/ConfirmDialog';

export interface RetireCameraDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  cameraIdentifier: string;
  /** Named in the confirmation (FR-003) — not "this camera". */
  name: string;
}

/**
 * Confirms retiring a camera (spec 032 US2).
 *
 * No React Hook Form and no Zod: a confirmation carries no fields, and ADR-0079
 * governs forms. Applying it here would be ceremony around a single button.
 *
 * <p>
 * Every sentence below is a requirement, and two of them describe consequences
 * an operator cannot see from this camera's page: the live stream stops
 * (FR-006, spec 028 FR-008) and the name is freed for reuse within the fab
 * (FR-007, spec 028 FR-006). The second is a payoff spec 028 built that nothing
 * has ever surfaced.
 * </p>
 *
 * The wording is **future tense throughout** — what confirming will do. Nothing
 * here or on the page afterwards says the camera *was* retired by this
 * operator, because the endpoint answers `204` whether or not it was (FR-012).
 */
export function RetireCameraDialog({ open, onOpenChange, cameraIdentifier, name }: RetireCameraDialogProps) {
  const [retireCamera, { isLoading, error, reset }] = useRetireCameraMutation();

  async function confirm() {
    const outcome = await retireCamera({ cameraIdentifier });

    // Closing only on success leaves the refusal visible where the operator is
    // looking. A dialog that closes either way reports failure to an empty
    // screen.
    if (!('error' in outcome)) {
      onOpenChange(false);
    }
  }

  function change(next: boolean) {
    if (!next) {
      reset();
    }
    onOpenChange(next);
  }

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={change}
      title={`Retire ${name}?`}
      confirmLabel="Retire camera"
      pending={isLoading}
      onConfirm={confirm}
    >
      <p>
        Retiring <strong className="text-fg-primary">{name}</strong> is permanent. It cannot be
        undone, from here or anywhere else.
      </p>
      <p>Its live stream will stop, and anyone watching it will lose the feed.</p>
      <p>
        The name <strong className="text-fg-primary">{name}</strong> will become available again, so
        a replacement camera can be registered under it.
      </p>
      <p>The record itself is kept, and stays readable at this address.</p>

      {error !== undefined && (
        <p role="alert" className="text-accent-fault">
          {problemDetail(error, 'The camera could not be retired. Nothing has changed.')}
        </p>
      )}
    </ConfirmDialog>
  );
}
