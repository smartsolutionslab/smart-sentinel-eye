import { useChangeCameraAddressMutation } from '@smart-sentinel-eye/shared/api/cameras.api';
import {
  changeCameraAddressSchema,
  type ChangeCameraAddressFormInput,
} from '@smart-sentinel-eye/shared/api/cameras.schema';
import {
  CONFLICT_FALLBACK,
  TERMINAL_REFUSAL_FALLBACK,
  isStaleConflict,
  isTerminalRefusal,
  problemDetail,
} from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { zodResolver } from '@hookform/resolvers/zod';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';

export interface EditCameraAddressDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  cameraIdentifier: string;
  /** The version the operator was shown. Echoed via `If-Match` (ADR-0113). */
  version: number;
  currentUrl: string;
}

/**
 * Corrects a camera's RTSP address (spec 030 FR-004).
 *
 * <p>
 * No name field: the API does not accept one (spec 029 FR-012, tracked as
 * #1850), so offering it would fail on submit.
 * </p>
 */
export function EditCameraAddressDialog({
  open,
  onOpenChange,
  cameraIdentifier,
  version,
  currentUrl,
}: EditCameraAddressDialogProps) {
  const [changeAddress, { isLoading, error, reset: resetMutationState }] = useChangeCameraAddressMutation();

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<ChangeCameraAddressFormInput>({
    resolver: zodResolver(changeCameraAddressSchema),
    defaultValues: { rtspUrl: currentUrl },
  });

  // Reopening starts from what is stored now, not from what a previous attempt
  // left behind — and a stale refusal banner must not greet the next open.
  useEffect(() => {
    if (open) {
      reset({ rtspUrl: currentUrl });
      resetMutationState();
    }
  }, [open, currentUrl, reset, resetMutationState]);

  const onSubmit = handleSubmit(async (input) => {
    const result = await changeAddress({ cameraIdentifier, rtspUrl: input.rtspUrl, version });

    // Left open on failure, deliberately. What the operator typed stays in the
    // field so they are not made to retype it, and the refusal is next to the
    // input it is about.
    if (!('error' in result)) {
      onOpenChange(false);
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Correct the address"
      description="The camera keeps its name, its identifier and its history."
    >
      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        <FormField label="RTSP URL" htmlFor="edit-camera-url" error={errors.rtspUrl?.message}>
          <Input id="edit-camera-url" autoFocus placeholder="rtsp://10.0.5.12/h264" {...register('rtspUrl')} />
        </FormField>

        <RefusalBanner error={error} />

        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" disabled={isLoading}>
            {isLoading ? 'Saving…' : 'Save'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

/**
 * Turns a refusal into advice (FR-006).
 *
 * <p>
 * The order matters, and both branches exist because the alternative is wrong
 * in a way that still renders a message.
 * </p>
 *
 * <p>
 * A <b>terminal</b> refusal is checked first. `CAMERA_RETIRED` is a 409, so
 * generic conflict handling fits it and would say "someone else changed this,
 * reload to see their version" — about a camera nobody changed, where reloading
 * will not help.
 * </p>
 *
 * <p>
 * A <b>stale</b> refusal must say reload, never try again. Resubmitting replays
 * the operator's change over the other writer's, which is the lost update the
 * whole version mechanism exists to prevent — the same point
 * `LayoutEditorDialog` makes.
 * </p>
 */
function RefusalBanner({ error }: { error: unknown }) {
  if (error === undefined || error === null) {
    return null;
  }

  // The two known refusals use *our* wording rather than the server's detail,
  // which is a deliberate divergence from LayoutEditorDialog.
  //
  // CameraCatalog's stale detail reads "Camera '<guid>' is at version 9, not 7.
  // Re-read it and try again." An operator can act on none of that: the
  // identifier is noise, the version numbers are machinery, and "try again" is
  // the phrase most likely to be read alone and acted on — which replays their
  // change over the other writer's. The advice for a lost update is the same in
  // every context, so it belongs here, once.
  //
  // Anything unrecognised still gets the server's detail, because an
  // unrecognised refusal is precisely where the server knows more than we do.
  const message = isTerminalRefusal(error)
    ? TERMINAL_REFUSAL_FALLBACK
    : isStaleConflict(error)
      ? CONFLICT_FALLBACK
      : problemDetail(error, 'Could not save the address. Try again.');

  return (
    <p role="alert" className="text-sm text-accent-fault">
      {message}
    </p>
  );
}
