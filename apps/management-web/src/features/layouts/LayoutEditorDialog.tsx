import { useListCamerasQuery } from '@smart-sentinel-eye/shared/api/cameras.api';
import { useCreateLayoutDraftMutation } from '@smart-sentinel-eye/shared/api/layouts.api';
import {
  createLayoutDraftSchema,
  type CreateLayoutDraftInput,
} from '@smart-sentinel-eye/shared/api/layouts.schema';
import { useListOverlaysQuery } from '@smart-sentinel-eye/shared/api/overlays.api';
import { problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';

export interface LayoutEditorDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function LayoutEditorDialog({ open, onOpenChange }: LayoutEditorDialogProps) {
  const [createLayoutDraft, { isLoading, error }] = useCreateLayoutDraftMutation();
  const { data: cameras, isLoading: camerasLoading } = useListCamerasQuery({ limit: 50 });
  const { data: overlays, isLoading: overlaysLoading } = useListOverlaysQuery('Published');

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<CreateLayoutDraftInput>({
    resolver: zodResolver(createLayoutDraftSchema),
    defaultValues: { name: '', cameraIdentifier: '', overlayIdentifier: '' },
  });

  const onSubmit = handleSubmit(async (input) => {
    const result = await createLayoutDraft(input);
    if (!('error' in result)) {
      reset();
      onOpenChange(false);
    }
  });

  const backendError = problemDetail(error, 'Could not save the layout. Try again.');
  const cameraItems = cameras?.items ?? [];
  const overlayItems = overlays?.published ?? [];

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          reset();
        }
        onOpenChange(next);
      }}
      title="New layout"
      description="Pick a name and one registered camera. The layout starts as a draft."
    >
      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        <FormField label="Name" htmlFor="layout-name" error={errors.name?.message}>
          <Input id="layout-name" autoFocus {...register('name')} />
        </FormField>
        <FormField label="Camera" htmlFor="layout-camera" error={errors.cameraIdentifier?.message}>
          <select
            id="layout-camera"
            className="w-full rounded-md border border-fg-muted/40 bg-bg-base px-3 py-2 text-fg-primary"
            {...register('cameraIdentifier')}
          >
            <option value="">
              {camerasLoading ? 'Loading cameras…' : 'Select a camera'}
            </option>
            {cameraItems.map((camera) => (
              <option key={camera.cameraIdentifier} value={camera.cameraIdentifier}>
                {camera.name}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Overlay" htmlFor="layout-overlay" error={errors.overlayIdentifier?.message}>
          <select
            id="layout-overlay"
            className="w-full rounded-md border border-fg-muted/40 bg-bg-base px-3 py-2 text-fg-primary"
            {...register('overlayIdentifier')}
          >
            <option value="">{overlaysLoading ? 'Loading overlays…' : '(none)'}</option>
            {overlayItems.map((overlay) => (
              <option key={overlay.overlayIdentifier} value={overlay.overlayIdentifier}>
                {overlay.name}
              </option>
            ))}
          </select>
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
          <Button type="submit" disabled={isLoading || cameraItems.length === 0}>
            {isLoading ? 'Saving…' : 'Save as draft'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

