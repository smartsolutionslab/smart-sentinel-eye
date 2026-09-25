import { useEffect, useEffectEvent, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useCreateRuleMutation } from '@smart-sentinel-eye/shared/api/rules.api';
import { useAssignedFabs } from '../../app/useAssignedFabs';
import { createRuleSchema, type CreateRuleInput } from '@smart-sentinel-eye/shared/api/rules.schema';
import { problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { FormErrorSummary } from '@smart-sentinel-eye/shared/ui/composites/FormErrorSummary';
import { AelHelpPanel } from './AelHelpPanel';

export interface RuleDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const DEFAULT_INPUT: CreateRuleInput = {
  name: '',
  triggerSource: 'plc',
  triggerKind: '',
  predicate: '',
  actionType: 'SetVariableValue',
} as CreateRuleInput;

export function RuleDialog({ open, onOpenChange }: RuleDialogProps) {
  const [createRule, { isLoading, error, reset: resetMutationState }] = useCreateRuleMutation();

  // An operator in one fab has it inferred and is never asked (ADR-0114); one
  // in several must choose, because any tie-break would file the rule under a
  // fab they did not pick. `fabId` is deliberately not part of the form: it
  // travels as a query parameter, and createRuleSchema mirrors the body.
  const fabs = useAssignedFabs();
  const mustChooseFab = fabs.length > 1;
  const [fabId, setFabId] = useState('');
  const [fabError, setFabError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    watch,
    unregister,
    formState: { errors },
    reset,
  } = useForm<CreateRuleInput>({
    resolver: zodResolver(createRuleSchema),
    defaultValues: DEFAULT_INPUT,
  });

  // clearOnClose always sees the latest reset/resetMutationState via
  // useEffectEvent; the effect itself only re-fires when `open` changes, so
  // Cancel/Esc/overlay-click (which all flip `open`, not call this directly)
  // all land here exactly once per close.
  const clearOnClose = useEffectEvent(() => {
    resetMutationState();
    reset(DEFAULT_INPUT);
    setFabId('');
    setFabError(null);
  });

  // Drop any prior backend error and typed input when the dialog closes so a
  // stale banner or value doesn't greet the operator on the next open (the
  // mutation result and the form values both live outside the unmounted
  // dialog's DOM — the parent renders this dialog unconditionally).
  useEffect(() => {
    if (!open) clearOnClose();
  }, [open]);

  // The action tag decides which half of the form is live — the same
  // discriminant the wire shape and the domain use.
  // react-hook-form's watch() is opaque to React Compiler, so it reports
  // "Compilation Skipped" rather than a defect. Nothing is incorrect at
  // runtime; the component forgoes an optimisation from a compiler this repo
  // does not enable. Working around it would mean working around ADR-0079.
  // eslint-disable-next-line react-hooks/incompatible-library -- see above
  const actionType = watch('actionType');

  const setsVariable = actionType === 'SetVariableValue';

  useEffect(() => {
    if (setsVariable) {
      unregister(['overlayIdentifier', 'durationMs']);
    } else {
      unregister(['variableName', 'valueExpression']);
    }
  }, [setsVariable, unregister]);

  // One boolean, read by both the visibility ternary below and
  // renderedFields — a second textual copy of `actionType ===
  // 'SetVariableValue'` could drift out of step with the branch it's meant
  // to describe, and FormErrorSummary would then filter out exactly the
  // error it exists to catch, silently.
  const renderedFields: readonly (keyof CreateRuleInput)[] = setsVariable
    ? ['name', 'triggerSource', 'triggerKind', 'predicate', 'actionType', 'variableName', 'valueExpression']
    : ['name', 'triggerSource', 'triggerKind', 'predicate', 'actionType', 'overlayIdentifier', 'durationMs'];

  const onSubmit = handleSubmit(async (values) => {
    if (mustChooseFab && fabId === '') {
      // Caught here rather than sent: the server answers this with
      // 400 RULE_FAB_REQUIRED, which is the right answer to the wrong
      // question when the operator can simply be asked.
      setFabError('Choose which fab this rule belongs to.');
      return;
    }
    setFabError(null);

    const result = await createRule(mustChooseFab ? { ...values, fabId } : values);
    if (!('error' in result)) {
      reset(DEFAULT_INPUT);
      setFabId('');
      onOpenChange(false);
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="New rule"
      description="Rules are created as drafts. Publish when you are ready for them to fire."
    >
      <form onSubmit={onSubmit} className="space-y-3" data-testid="rule-form">
        <FormField label="Name" htmlFor="rule-name" error={errors.name?.message}>
          <Input id="rule-name" placeholder="high-oee-on-fast-cycle" {...register('name')} />
        </FormField>

        {mustChooseFab && (
          <FormField label="Fab" htmlFor="rule-fab-id" error={fabError ?? undefined}>
            <select
              id="rule-fab-id"
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

        <div className="grid grid-cols-2 gap-3">
          <FormField label="Trigger source" htmlFor="rule-source" error={errors.triggerSource?.message}>
            <Input id="rule-source" placeholder="plc" {...register('triggerSource')} />
          </FormField>
          <FormField label="Trigger kind" htmlFor="rule-kind" error={errors.triggerKind?.message}>
            <Input id="rule-kind" placeholder="PlcCycleStart" {...register('triggerKind')} />
          </FormField>
        </div>

        <FormField label="Predicate (AEL)" htmlFor="rule-predicate" error={errors.predicate?.message}>
          <textarea
            id="rule-predicate"
            className="h-20 w-full rounded-md border border-fg-muted/30 bg-transparent p-2 font-mono text-xs"
            placeholder="$.payload.cycleTime <= 30"
            {...register('predicate')}
          />
        </FormField>

        <AelHelpPanel />

        <FormField label="Action" htmlFor="rule-action-type" error={errors.actionType?.message}>
          <select
            id="rule-action-type"
            className="w-full rounded-md border border-fg-muted/30 bg-transparent p-2 text-sm"
            {...register('actionType')}
          >
            <option value="SetVariableValue">Set a system variable</option>
            <option value="HighlightOverlay">Highlight an overlay</option>
          </select>
        </FormField>

        {setsVariable ? (
          // key forces a remount on toggle so a typed value can't carry into the other branch's field (pairs with unregister() above),
          // so a round trip returns empty fields.
          <div key="set-variable" className="grid grid-cols-2 gap-3">
            <FormField label="Variable name" htmlFor="rule-variable" error={errors.variableName?.message}>
              <Input id="rule-variable" placeholder="oeeLine1" {...register('variableName')} />
            </FormField>
            <FormField
              label="Value expression (AEL)"
              htmlFor="rule-value-expression"
              error={errors.valueExpression?.message}
            >
              <Input
                id="rule-value-expression"
                placeholder="100 - $.payload.cycleTime * 2"
                {...register('valueExpression')}
              />
            </FormField>
          </div>
        ) : (
          <div key="highlight-overlay" className="grid grid-cols-2 gap-3">
            <FormField label="Overlay" htmlFor="rule-overlay" error={errors.overlayIdentifier?.message}>
              <Input id="rule-overlay" placeholder="overlay identifier" {...register('overlayIdentifier')} />
            </FormField>
            <FormField label="Duration (ms)" htmlFor="rule-duration" error={errors.durationMs?.message}>
              <Input
                id="rule-duration"
                type="number"
                placeholder="5000"
                {...register('durationMs', { valueAsNumber: true })}
              />
            </FormField>
          </div>
        )}

        {error !== undefined && (
          <p role="alert" className="text-xs text-accent-fault">
            {problemDetail(error, 'Could not create the rule.')}
          </p>
        )}
        <FormErrorSummary errors={errors} renderedFields={renderedFields} />

        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" disabled={isLoading}>
            {isLoading ? 'Creating…' : 'Create draft'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
