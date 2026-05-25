import { useRegisterCameraMutation } from '@smart-sentinel-eye/shared/api/cameras.api';
import { registerCameraSchema, type RegisterCameraInput } from '@smart-sentinel-eye/shared/api/cameras.schema';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';

export interface RegisterCameraDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RegisterCameraDialog({ open, onOpenChange }: RegisterCameraDialogProps) {
  const [registerCamera, { isLoading, error }] = useRegisterCameraMutation();

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<RegisterCameraInput>({
    resolver: zodResolver(registerCameraSchema),
    defaultValues: { name: '', rtspUrl: '' },
  });

  const onSubmit = handleSubmit(async (input) => {
    const result = await registerCamera(input);
    if (!('error' in result)) {
      reset();
      onOpenChange(false);
    }
  });

  const backendError = serverProblemMessage(error);

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          reset();
        }
        onOpenChange(next);
      }}
      title="Register a camera"
      description="Provide a unique name and the camera's RTSP URL."
    >
      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        <FormField label="Name" htmlFor="register-camera-name" error={errors.name?.message}>
          <Input id="register-camera-name" autoFocus {...register('name')} />
        </FormField>
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

function serverProblemMessage(error: unknown): string | null {
  if (error === undefined || error === null) {
    return null;
  }
  if (typeof error === 'object' && 'data' in error) {
    const data = (error as { data: unknown }).data;
    if (typeof data === 'object' && data !== null && 'detail' in data) {
      const detail = (data as { detail: unknown }).detail;
      if (typeof detail === 'string') {
        return detail;
      }
    }
  }
  return 'Could not register the camera. Try again.';
}
