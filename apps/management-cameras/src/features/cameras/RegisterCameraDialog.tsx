import { useRegisterCameraMutation } from '@smart-sentinel-eye/shared/api/cameras.api';
import { registerCameraSchema, type RegisterCameraInput } from '@smart-sentinel-eye/shared/api/cameras.schema';
import { problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { zodResolver } from '@hookform/resolvers/zod';
import { useAssignedFabs, useClearOnClose, useSubmitGuard } from '@smart-sentinel-eye/shared/hooks';
import { useState } from 'react';
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

  // fabId/fabError are cleared here, during render, rather than from the
  // close effect below: this component (unlike RuleDialog/SystemVariableDialog)
  // never calls react-hook-form's watch(), so nothing makes React Compiler skip
  // it, and react-hooks/set-state-in-effect flags a raw useState setter reached
  // from an effect (even indirectly, through a useEffectEvent-wrapped
  // callback) as a synchronous, cascading-render-inducing setState-in-effect.
  // Comparing against the last-seen `open` and setting state unconditionally
  // in the render body is React's own documented fix for exactly this shape —
  // "Adjusting some state when a prop changes"
  // (https://react.dev/learn/you-might-not-need-an-effect) — and it sidesteps
  // the rule instead of suppressing it.
  const [wasOpen, setWasOpen] = useState(open);
  if (open !== wasOpen) {
    setWasOpen(open);
    if (!open) {
      setFabId('');
      setFabError(null);
    }
  }

  // Runs on every close and once on mount while closed; why it watches `open` and not onOpenChange is in useClearOnClose.
  useClearOnClose(open, () => {
    resetMutationState();
    reset();
  });

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

  // ADR-0151, see useSubmitGuard. It refuses before the missing-fab check too, since nothing should be re-validated
  // mid-request.
  const handleFormSubmit = useSubmitGuard(isLoading, onSubmit);

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Register a camera"
      description="Provide a unique name and the camera's RTSP URL."
    >
      <form onSubmit={handleFormSubmit} className="flex flex-col gap-4">
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
          <Button type="submit" unavailable={isLoading} busy={isLoading}>
            {isLoading ? 'Registering…' : 'Register'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
