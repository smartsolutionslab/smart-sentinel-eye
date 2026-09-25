import { useRegisterCameraMutation } from '@smart-sentinel-eye/shared/api/cameras.api';
import { registerCameraSchema, type RegisterCameraInput } from '@smart-sentinel-eye/shared/api/cameras.schema';
import { problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { zodResolver } from '@hookform/resolvers/zod';
import { useAssignedFabs } from '../../app/useAssignedFabs';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';

export interface RegisterCameraDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RegisterCameraDialog({ open, onOpenChange }: RegisterCameraDialogProps) {
  const [registerCamera, { isLoading, error, reset: resetMutationState }] = useRegisterCameraMutation();

  // An operator in one fab has it inferred and is never asked (ADR-0114); one
  // in several must choose, because any tie-break would file the camera under
  // a fab they did not pick. `fabId` is deliberately not part of the form: it
  // travels as a query parameter, and registerCameraSchema mirrors the body.
  const fabs = useAssignedFabs();
  const mustChooseFab = fabs.length > 1;
  const [fabId, setFabId] = useState('');
  const [fabError, setFabError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<RegisterCameraInput>({
    resolver: zodResolver(registerCameraSchema),
    defaultValues: { name: '', rtspUrl: '' },
  });

  // Drop any prior backend error and typed input when the dialog closes so a
  // stale banner or value doesn't greet the operator on the next open (the
  // mutation result and the form values both live outside the unmounted
  // dialog's DOM — the parent renders this dialog unconditionally).
  useEffect(() => {
    if (!open) {
      // The obvious rewrite is wrong here. Moving these into the Dialog's
      // onOpenChange handler would catch only Radix-initiated closes (Esc,
      // overlay click): Cancel and the submit-success path call the *parent's*
      // onOpenChange and close by flipping the `open` prop, which that handler
      // never sees. Watching `open` catches every close path. The cost is one
      // extra render of an already-closed dialog.
      //
      // Deps are deliberately just [open], not exhaustive. Radix has already
      // unmounted the form's inputs by the time this runs (open is false), and
      // calling RHF's reset() against unmounted fields forces a formState
      // broadcast on every call, which re-renders this component and hands
      // useRegisterCameraMutation() a new (unstable) `reset` function identity
      // on each pass. Listing that function as a dep re-fires this effect on
      // that very re-render, calling reset() again — a self-sustaining loop
      // that starves the process rather than erroring, since nothing here
      // ever throws. Closing over the latest resetMutationState/reset via the
      // effect body (not the dep list) breaks the cycle; open is the only
      // signal this effect should react to.
      resetMutationState();
      reset();
      // eslint-disable-next-line react-hooks/set-state-in-effect -- see above
      setFabId('');
      setFabError(null);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- see comment above
  }, [open]);

  const onSubmit = handleSubmit(async (input) => {
    if (mustChooseFab && fabId === '') {
      // Caught here rather than sent: the server answers this with
      // 400 CAMERA_FAB_REQUIRED, which is the right answer to the wrong
      // question when the operator can simply be asked.
      setFabError('Choose which fab this camera belongs to.');
      return;
    }
    setFabError(null);

    const result = await registerCamera(mustChooseFab ? { ...input, fabId } : input);
    if (!('error' in result)) {
      reset();
      setFabId('');
      onOpenChange(false);
    }
  });

  const backendError = problemDetail(error, 'Could not register the camera. Try again.');

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Register a camera"
      description="Provide a unique name and the camera's RTSP URL."
    >
      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        <FormField label="Name" htmlFor="register-camera-name" error={errors.name?.message}>
          <Input id="register-camera-name" autoFocus {...register('name')} />
        </FormField>

        {mustChooseFab && (
          <FormField label="Fab" htmlFor="camera-fab-id" error={fabError ?? undefined}>
            <select
              id="camera-fab-id"
              className="w-full rounded-md border border-fg-muted/30 bg-transparent p-2 text-sm"
              value={fabId}
              onChange={(event) => {
                setFabId(event.target.value);
                setFabError(null);
              }}
            >
              <option value="">Choose a fab…</option>
              {fabs.map((fab) => (
                <option key={fab} value={fab}>
                  {fab}
                </option>
              ))}
            </select>
          </FormField>
        )}
        <FormField label="RTSP URL" htmlFor="register-camera-url" error={errors.rtspUrl?.message}>
          <Input id="register-camera-url" placeholder="rtsp://10.0.5.12/h264" {...register('rtspUrl')} />
        </FormField>
        {backendError !== null && (
          <p role="alert" className="text-sm text-accent-fault">
            {backendError}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" disabled={isLoading}>
            {isLoading ? 'Registering…' : 'Register'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
