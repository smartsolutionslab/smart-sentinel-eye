import { useRenameCameraMutation } from '@smart-sentinel-eye/shared/api/cameras.api';
import { renameCameraSchema, type RenameCameraFormInput } from '@smart-sentinel-eye/shared/api/cameras.schema';
import {
  CONFLICT_FALLBACK,
  TERMINAL_REFUSAL_FALLBACK,
  isStaleConflict,
  isTerminalRefusal,
  problemCode,
  problemDetail,
} from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { zodResolver } from '@hookform/resolvers/zod';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';

export interface RenameCameraDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  cameraIdentifier: string;
  /** The version the operator was shown. Echoed via `If-Match` (ADR-0113). */
  version: number;
  currentName: string;
}

/**
 * Corrects a camera's name (spec 035 FR-001).
 *
 * <p>
 * Its own dialog rather than a second field on {@link EditCameraAddressDialog},
 * because the endpoint applies exactly one field per request under its own
 * version — a combined form's second request would quote a version its own
 * first request had just advanced (spec 033).
 * </p>
 *
 * <p>
 * Mirrored from that dialog rather than extracted from it. What the two share
 * is a shape, not a behaviour: the field, the schema, the mutation and — below
 * — the refusal branching all differ. Revisit at a third caller.
 * </p>
 */
export function RenameCameraDialog({
  open,
  onOpenChange,
  cameraIdentifier,
  version,
  currentName,
}: RenameCameraDialogProps) {
  const [renameCamera, { isLoading, error, reset: resetMutationState }] = useRenameCameraMutation();

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<RenameCameraFormInput>({
    resolver: zodResolver(renameCameraSchema),
    // FR-003. A correction is an edit, not a retype: a blank field would make
    // the operator reconstruct the name they are fixing before they can fix it.
    defaultValues: { name: currentName },
  });

  // Reopening starts from what is stored now, not from what a previous attempt
  // left behind — and a stale refusal banner must not greet the next open.
  useEffect(() => {
    if (open) {
      reset({ name: currentName });
      resetMutationState();
    }
  }, [open, currentName, reset, resetMutationState]);

  const onSubmit = handleSubmit(async (input) => {
    // FR-010. `input.name` reaches the server exactly as typed. The schema
    // trims surrounding whitespace and does nothing else — no lower-casing,
    // ever. `Line-4-Inlet` and `line-4-inlet` normalise identically, so
    // normalising here would turn a real correction into a silent no-op, which
    // is the trap spec 033 found in three separate server-side layers.
    const result = await renameCamera({ cameraIdentifier, name: input.name, version });

    // Left open on failure, deliberately. What the operator typed stays in the
    // field so they are not made to retype it, and the refusal is next to the
    // input it is about (FR-011).
    if (!('error' in result)) {
      onOpenChange(false);
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Rename camera"
      description="The camera keeps its address, its identifier and its history."
    >
      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        <FormField label="Name" htmlFor="rename-camera-name" error={errors.name?.message}>
          <Input id="rename-camera-name" autoFocus placeholder="line-4-inlet" {...register('name')} />
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
 * Three refusals, three remedies — one more than the address correction has to
 * tell apart, and the reason this feature is not purely mechanical.
 *
 * <p>
 * A <b>taken name</b> and a <b>retired camera</b> are both `409`, and a stale
 * version is `412`. None of that is what decides the wording: per ADR-0119 the
 * code is what a caller keys on, and the statuses are overloaded in both
 * directions.
 * </p>
 *
 * <p>
 * The dangerous one is the taken name. It is a conflict, so a check shaped like
 * "is this a conflict?" hands it {@link CONFLICT_FALLBACK} — <i>"someone else
 * changed this, reload to see their version"</i> — which is wrong in both
 * halves: nobody changed this camera, and reloading will not release the name.
 * Ordering the branches so the specific case is asked first is the whole
 * safeguard.
 * </p>
 */
function RefusalBanner({ error }: { error: unknown }) {
  if (error === undefined || error === null) {
    return null;
  }

  const message = nameTakenMessage(error) ?? fallbackMessage(error);

  return (
    <p role="alert" className="text-sm text-accent-fault">
      {message}
    </p>
  );
}

/**
 * The server's own sentence, plus the one thing it does not say.
 *
 * <p>
 * CameraCatalog answers <i>"Another camera in fab 'munich' is already called
 * 'line-4-inlet'. Names are unique per fab, ignoring case."</i> — which names
 * the actual conflict and the actual fab, and is better than any generic
 * sentence this dialog could write. What it never says is <b>what to do</b>,
 * and FR-005 requires that. So the detail is kept and the action is appended,
 * rather than the whole thing being replaced.
 * </p>
 *
 * <p>
 * Keyed on the code at this call site, following `OverlayEditorDialog`. No
 * shared predicate in `problemDetail.ts`: one caller does not earn one, and
 * `isTerminalRefusal` only became shared when a second dialog needed it.
 * </p>
 */
function nameTakenMessage(error: unknown): string | null {
  if (problemCode(error) !== 'CAMERA_NAME_TAKEN') {
    return null;
  }

  const detail = problemDetail(error, 'That camera name is already taken.');

  return `${detail} Choose a different one.`;
}

function fallbackMessage(error: unknown): string | null {
  // The two shared refusals use *our* wording rather than the server's, which
  // EditCameraAddressDialog records the reason for: CameraCatalog's stale
  // detail talks about version numbers an operator cannot act on.
  //
  // Anything unrecognised still gets the server's detail, because an
  // unrecognised refusal is precisely where the server knows more than we do.
  if (isTerminalRefusal(error)) {
    return TERMINAL_REFUSAL_FALLBACK;
  }

  if (isStaleConflict(error)) {
    return CONFLICT_FALLBACK;
  }

  return problemDetail(error, 'Could not save the name. Try again.');
}
