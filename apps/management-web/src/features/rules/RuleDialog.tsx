import { useEffect, useEffectEvent, useState, type FormEvent } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useCreateRuleMutation } from '@smart-sentinel-eye/shared/api/rules.api';
import { useListWallsQuery } from '@smart-sentinel-eye/shared/api/walls.api';
import { useListLayoutsQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import { useAssignedFabs } from '../../app/useAssignedFabs';
import { createRuleSchema, type CreateRuleInput } from '@smart-sentinel-eye/shared/api/rules.schema';
import { problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { Select, type SelectOption } from '@smart-sentinel-eye/shared/ui/primitives/Select';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { FormErrorSummary } from '@smart-sentinel-eye/shared/ui/composites/FormErrorSummary';
import { AelHelpPanel } from './AelHelpPanel';

export interface RuleDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

type ActionType = CreateRuleInput['actionType'];

const DEFAULT_INPUT: CreateRuleInput = {
  name: '',
  triggerSource: 'plc',
  triggerKind: '',
  predicate: '',
  actionType: 'SetVariableValue',
} as CreateRuleInput;

const ACTION_OPTIONS: readonly SelectOption[] = [
  { value: 'SetVariableValue', label: 'Set a system variable' },
  { value: 'HighlightOverlay', label: 'Highlight an overlay' },
  { value: 'SwitchWallScene', label: "Switch a wall's scene" },
];

// One source for which fields belong to which action — read by the
// unregister effect and by renderedFields, so the two cannot drift apart
// (the same property the two-way `setsVariable` boolean used to guarantee).
const ACTION_FIELDS: Record<ActionType, readonly (keyof CreateRuleInput)[]> = {
  SetVariableValue: ['variableName', 'valueExpression'],
  HighlightOverlay: ['overlayIdentifier', 'durationMs'],
  SwitchWallScene: ['wallIdentifier', 'sceneTarget', 'targetLayoutIdentifier'],
};

const TARGET_NEXT = 'next';

const NEXT_TARGET_OPTION: SelectOption = { value: TARGET_NEXT, label: 'Next scene' };

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
  // The fab a SwitchWallScene rule's wall choices are narrowed to (A4): the
  // chosen fab for a multi-fab operator, or the single fab that is inferred
  // for everyone else. Empty until a multi-fab operator picks one, which is
  // exactly when the wall select should offer nothing yet.
  const currentFab = mustChooseFab ? fabId : (fabs[0] ?? '');

  const {
    register,
    handleSubmit,
    watch,
    unregister,
    setValue,
    control,
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

  // The action tag decides which part of the form is live — the same
  // discriminant the wire shape and the domain use. wallIdentifier and
  // sceneTarget/targetLayoutIdentifier are watched alongside it so the wall
  // and target selects can read the live selection back (react-hook-form
  // Controller only hands a field its own value, not a sibling's).
  // react-hook-form's watch() is opaque to React Compiler, so it reports
  // "Compilation Skipped" rather than a defect. Nothing is incorrect at
  // runtime; the component forgoes an optimisation from a compiler this repo
  // does not enable. Working around it would mean working around ADR-0079.
  // eslint-disable-next-line react-hooks/incompatible-library -- see above
  const [actionType, wallIdentifier, sceneTarget, targetLayoutIdentifier] = watch([
    'actionType',
    'wallIdentifier',
    'sceneTarget',
    'targetLayoutIdentifier',
  ]);

  useEffect(() => {
    const otherActionFields = (Object.keys(ACTION_FIELDS) as ActionType[])
      .filter((type) => type !== actionType)
      .flatMap((type) => ACTION_FIELDS[type]);
    unregister(otherActionFields);
  }, [actionType, unregister]);

  // One mapping, read by both the visibility conditionals below and
  // renderedFields — a second, separately-maintained field list per branch
  // could drift out of step with what is actually rendered, and
  // FormErrorSummary would then filter out exactly the error it exists to
  // catch, silently.
  const renderedFields: readonly (keyof CreateRuleInput)[] = [
    'name',
    'triggerSource',
    'triggerKind',
    'predicate',
    'actionType',
    ...ACTION_FIELDS[actionType],
  ];

  const { data: wallsData, isLoading: wallsLoading, isError: wallsError } = useListWallsQuery();
  const walls = (wallsData ?? []).filter((wall) => wall.fab === currentFab);
  const wallOptions: readonly SelectOption[] = walls.map((wall) => ({ value: wall.wallIdentifier, label: wall.name }));
  const selectedWall = walls.find((wall) => wall.wallIdentifier === wallIdentifier);

  // A multi-fab operator who hasn't chosen a fab yet always sees an empty
  // `walls` (currentFab is '' until they pick), which otherwise reads
  // identically to "loading", "fetch failed" and "this fab genuinely has
  // none" — four states that need four different placeholders.
  //
  // Only `wallsLoading` disables the trigger. A protected test (`'Offers no
  // walls to a multi-fab operator until a fab is chosen'`) opens the wall
  // listbox before any fab is picked and asserts it is empty — Radix's
  // `disabled` suppresses the open itself (no native click, no listbox at
  // all), which would make that assertion unreachable rather than true. The
  // placeholder still tells the operator why the list is empty; it is just
  // not paired with `disabled` for this one state.
  const noFabChosenYet = mustChooseFab && fabId === '';
  const wallSelectDisabled = wallsLoading;
  const wallPlaceholder = noFabChosenYet
    ? 'Choose a fab first…'
    : wallsLoading
      ? 'Loading walls…'
      : walls.length === 0
        ? 'No walls in this fab'
        : 'Choose a wall…';

  const { data: layoutsData } = useListLayoutsQuery('Published');
  const layoutName = (layoutIdentifier: string): string =>
    layoutsData?.published.find((layout) => layout.layoutIdentifier === layoutIdentifier)?.name ?? layoutIdentifier;

  const targetOptions: readonly SelectOption[] = [
    NEXT_TARGET_OPTION,
    ...(selectedWall?.scenes.map((sceneId) => ({ value: sceneId, label: layoutName(sceneId) })) ?? []),
  ];
  const targetValue =
    sceneTarget === 'Next' ? TARGET_NEXT : sceneTarget === 'Layout' ? targetLayoutIdentifier : undefined;

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

  // ADR-0151: `unavailable` keeps Create draft focusable and clickable, and no
  // longer suppresses implicit submission (Enter in a field), so this is what
  // refuses a second submit while the first is in flight.
  function handleFormSubmit(event: FormEvent) {
    if (isLoading) {
      event.preventDefault();
      return;
    }
    void onSubmit(event);
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="New rule"
      description="Rules are created as drafts. Publish when you are ready for them to fire."
    >
      <form onSubmit={handleFormSubmit} className="space-y-3" data-testid="rule-form">
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
                // The wall list is narrowed to the chosen fab (A4): a wall
                // (and the target it named) picked under the previous fab is
                // not necessarily in the new one's list, and submitting it
                // unseen is exactly the FR-010(b) silent-drop this guards
                // against — mirrors the wall-change handler below.
                setValue('wallIdentifier', undefined);
                setValue('sceneTarget', undefined);
                setValue('targetLayoutIdentifier', undefined);
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
          <Controller
            name="actionType"
            control={control}
            render={({ field }) => (
              <Select
                id="rule-action-type"
                value={field.value}
                onValueChange={field.onChange}
                onBlur={field.onBlur}
                ref={field.ref}
                options={ACTION_OPTIONS}
                aria-invalid={errors.actionType !== undefined}
              />
            )}
          />
        </FormField>

        {/* Each branch below keeps its own key so a toggle remounts fresh,
            empty inputs rather than React reusing the DOM node across
            branches (pairs with the unregister() effect above) — a typed
            value from one branch must never ride along into another's
            differently-named field. */}
        {actionType === 'SetVariableValue' && (
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
        )}
        {actionType === 'HighlightOverlay' && (
          <div key="highlight-overlay" className="grid grid-cols-2 gap-3">
            <FormField label="Overlay" htmlFor="rule-overlay" error={errors.overlayIdentifier?.message}>
              <Input id="rule-overlay" placeholder="overlay identifier" {...register('overlayIdentifier')} />
            </FormField>
            <FormField label="Duration (ms)" htmlFor="rule-duration" error={errors.durationMs?.message}>
              <Input
                id="rule-duration"
                type="number"
                placeholder="5000"
                {...register('durationMs', {
                  setValueAs: (value: string) => (value === '' ? undefined : Number(value)),
                })}
              />
            </FormField>
          </div>
        )}
        {actionType === 'SwitchWallScene' && (
          <div key="switch-wall-scene" className="grid grid-cols-2 gap-3">
            <FormField label="Wall" htmlFor="rule-wall" error={errors.wallIdentifier?.message}>
              <Controller
                name="wallIdentifier"
                control={control}
                render={({ field }) => (
                  <Select
                    id="rule-wall"
                    value={field.value}
                    onValueChange={(value) => {
                      field.onChange(value);
                      // A target chosen for the previous wall names a scene
                      // (or "Next") that may not exist on the new one — drop
                      // it rather than carry a stale selection forward.
                      setValue('sceneTarget', undefined);
                      setValue('targetLayoutIdentifier', undefined);
                    }}
                    onBlur={field.onBlur}
                    ref={field.ref}
                    options={wallOptions}
                    placeholder={wallPlaceholder}
                    disabled={wallSelectDisabled}
                    aria-invalid={errors.wallIdentifier !== undefined}
                  />
                )}
              />
              {wallsError && (
                // Not role="alert" (e2e's management-rules.ts relies on no
                // alert surfacing from a read failure on this page) — a
                // quiet inline hint, not a banner.
                <p className="text-xs text-fg-muted">Could not load walls. Try again shortly.</p>
              )}
            </FormField>
            <FormField
              label="Target"
              htmlFor="rule-target"
              error={errors.sceneTarget?.message ?? errors.targetLayoutIdentifier?.message}
            >
              <Controller
                name="sceneTarget"
                control={control}
                render={({ field }) => (
                  <Select
                    id="rule-target"
                    value={targetValue}
                    onValueChange={(value) => {
                      if (value === TARGET_NEXT) {
                        field.onChange('Next');
                        setValue('targetLayoutIdentifier', undefined);
                      } else {
                        field.onChange('Layout');
                        setValue('targetLayoutIdentifier', value);
                      }
                    }}
                    onBlur={field.onBlur}
                    ref={field.ref}
                    options={targetOptions}
                    placeholder="Choose a target…"
                    aria-invalid={errors.sceneTarget !== undefined || errors.targetLayoutIdentifier !== undefined}
                  />
                )}
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
          <Button type="submit" unavailable={isLoading} busy={isLoading}>
            {isLoading ? 'Creating…' : 'Create draft'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
