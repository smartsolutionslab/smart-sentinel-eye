import { useDefineVariableMutation } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import {
  defineVariableSchema,
  type DefineVariableInput,
} from '@smart-sentinel-eye/shared/api/systemVariables.schema';
import { problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { zodResolver } from '@hookform/resolvers/zod';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';

export interface SystemVariableDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const DEFAULT_INPUT: DefineVariableInput = {
  name: '',
  type: 'String',
};

export function SystemVariableDialog({ open, onOpenChange }: SystemVariableDialogProps) {
  const [defineVariable, { isLoading, error, reset: resetMutationState }] = useDefineVariableMutation();

  // Drop any prior backend error when the dialog closes so a stale banner
  // doesn't greet the operator on the next open.
  useEffect(() => {
    if (!open) resetMutationState();
  }, [open, resetMutationState]);

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
    reset,
  } = useForm<DefineVariableInput>({
    resolver: zodResolver(defineVariableSchema),
    defaultValues: DEFAULT_INPUT,
  });

  const selectedType = watch('type');

  const onSubmit = handleSubmit(async (input) => {
    const result = await defineVariable(input);
    if (!('error' in result)) {
      reset(DEFAULT_INPUT);
      onOpenChange(false);
    }
  });

  const backendError = problemDetail(error, 'Could not save the variable. Try again.');

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) reset(DEFAULT_INPUT);
        onOpenChange(next);
      }}
      title="New variable"
      description="Pick a name, type, and (optionally) an initial value."
    >
      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        <FormField label="Name" htmlFor="variable-name" error={errors.name?.message}>
          <Input id="variable-name" autoFocus {...register('name')} />
        </FormField>
        <FormField label="Type" htmlFor="variable-type" error={errors.type?.message}>
          <select
            id="variable-type"
            className="w-full rounded-md border border-fg-muted/40 bg-bg-base px-3 py-2 text-fg-primary"
            {...register('type')}
          >
            <option value="String">String</option>
            <option value="Number">Number</option>
            <option value="Boolean">Boolean</option>
          </select>
        </FormField>
        <FormField label="Initial value" htmlFor="variable-initial-value" error={errors.initialValue?.message}>
          <Input
            id="variable-initial-value"
            placeholder="(unset)"
            {...register('initialValue')}
          />
        </FormField>
        {selectedType === 'Boolean' && (
          <>
            <FormField label="Truthy label" htmlFor="variable-truthy" error={errors.truthyLabel?.message}>
              <Input id="variable-truthy" defaultValue="Yes" {...register('truthyLabel')} />
            </FormField>
            <FormField label="Falsy label" htmlFor="variable-falsy" error={errors.falsyLabel?.message}>
              <Input id="variable-falsy" defaultValue="No" {...register('falsyLabel')} />
            </FormField>
          </>
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
            {isLoading ? 'Saving…' : 'Define'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
