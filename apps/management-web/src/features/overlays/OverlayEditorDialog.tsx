import { useCreateOverlayDraftMutation } from '@smart-sentinel-eye/shared/api/overlays.api';
import {
  createOverlayDraftSchema,
  type CreateOverlayDraftInput,
} from '@smart-sentinel-eye/shared/api/overlays.schema';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { OverlayEditor } from '@smart-sentinel-eye/shared/ui/composites/OverlayEditor';
import { problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { zodResolver } from '@hookform/resolvers/zod';
import { Controller, useForm } from 'react-hook-form';

export interface OverlayEditorDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const DEFAULT_INPUT: CreateOverlayDraftInput = {
  name: '',
  label: {
    text: 'Overlay text',
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    fontSizePx: 32,
  },
};

export function OverlayEditorDialog({ open, onOpenChange }: OverlayEditorDialogProps) {
  const [createOverlayDraft, { isLoading, error }] = useCreateOverlayDraftMutation();

  const {
    control,
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<CreateOverlayDraftInput>({
    resolver: zodResolver(createOverlayDraftSchema),
    defaultValues: DEFAULT_INPUT,
  });

  const onSubmit = handleSubmit(async (input) => {
    const result = await createOverlayDraft(input);
    if (!('error' in result)) {
      reset(DEFAULT_INPUT);
      onOpenChange(false);
    }
  });

  const backendError = problemDetail(error, 'Could not save the overlay. Try again.');

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          reset(DEFAULT_INPUT);
        }
        onOpenChange(next);
      }}
      title="New overlay"
      description="Pick a name, type the label, and drag it to position. The overlay starts as a draft."
    >
      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        <FormField label="Name" htmlFor="overlay-name" error={errors.name?.message}>
          <Input id="overlay-name" autoFocus {...register('name')} />
        </FormField>
        <Controller
          control={control}
          name="label"
          render={({ field }) => (
            <OverlayEditor value={field.value} onChange={field.onChange} />
          )}
        />
        {errors.label?.text?.message !== undefined && (
          <p role="alert" className="text-sm text-accent-fault">
            {errors.label.text.message}
          </p>
        )}
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
            {isLoading ? 'Saving…' : 'Save as draft'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
