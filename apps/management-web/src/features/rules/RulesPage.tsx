import { useState } from 'react';
import {
  useArchiveRuleMutation,
  useListRulesQuery,
  usePublishRuleMutation,
  RULE_ACTION_SET_VARIABLE_VALUE,
  type Rule,
  type RuleState,
} from '@smart-sentinel-eye/shared/api/rules.api';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { DataTable, type DataTableColumn } from '@smart-sentinel-eye/shared/ui/composites/DataTable';
import { RuleDialog } from './RuleDialog';
import { DryRunPanel } from './DryRunPanel';

const STATE_FILTERS: ReadonlyArray<{ label: string; value: RuleState | undefined }> = [
  { label: 'All', value: undefined },
  { label: 'Draft', value: 'Draft' },
  { label: 'Active', value: 'Active' },
  { label: 'Archived', value: 'Archived' },
];

export function RulesPage() {
  const [stateFilter, setStateFilter] = useState<RuleState | undefined>(undefined);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [dryRunFor, setDryRunFor] = useState<string | null>(null);

  const { data: rules, isLoading, isError, refetch } = useListRulesQuery(
    stateFilter === undefined ? undefined : { state: stateFilter },
  );
  const [publishRule] = usePublishRuleMutation();
  const [archiveRule] = useArchiveRuleMutation();

  const columns: DataTableColumn<Rule>[] = [
    { id: 'name', header: 'Name', cell: (rule) => <span className="font-medium">{rule.name}</span> },
    {
      id: 'trigger',
      header: 'Trigger',
      cell: (rule) => (
        <span className="font-mono text-xs">
          {rule.triggerSource}/{rule.triggerKind}
        </span>
      ),
    },
    {
      id: 'predicate',
      header: 'Predicate',
      cell: (rule) => <span className="font-mono text-xs">{rule.predicate}</span>,
    },
    { id: 'action', header: 'Action', cell: (rule) => <span className="text-xs">{describeAction(rule)}</span> },
    { id: 'state', header: 'State', cell: (rule) => <StateBadge state={rule.state} /> },
    {
      id: 'actions',
      header: 'Actions',
      cell: (rule) => (
        <div className="flex gap-1">
          {rule.state === 'Draft' && (
            <Button type="button" variant="secondary" onClick={() => void publishRule({ name: rule.name, version: rule.version })}>
              Publish
            </Button>
          )}
          {rule.state !== 'Archived' && (
            <Button type="button" variant="ghost" onClick={() => void archiveRule({ name: rule.name, version: rule.version })}>
              Archive
            </Button>
          )}
          <Button
            type="button"
            variant="ghost"
            onClick={() => setDryRunFor(dryRunFor === rule.name ? null : rule.name)}
          >
            {dryRunFor === rule.name ? 'Hide dry run' : 'Dry run'}
          </Button>
        </div>
      ),
    },
  ];

  return (
    <section className="space-y-4">
      <header className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Rules</h1>
        <Button type="button" onClick={() => setDialogOpen(true)}>
          New rule
        </Button>
      </header>

      <div className="flex gap-2" role="group" aria-label="Filter by state">
        {STATE_FILTERS.map((filter) => (
          <Button
            key={filter.label}
            type="button"
            variant={stateFilter === filter.value ? 'primary' : 'ghost'}
            onClick={() => setStateFilter(filter.value)}
          >
            {filter.label}
          </Button>
        ))}
      </div>

      {isError ? (
        <div role="alert" className="space-y-2 rounded-md border border-accent-fault/40 p-3 text-sm">
          <p>Could not load rules.</p>
          <Button type="button" variant="secondary" onClick={() => void refetch()}>
            Retry
          </Button>
        </div>
      ) : (
        <DataTable
          columns={columns}
          rows={rules ?? []}
          getRowKey={(rule) => rule.ruleIdentifier}
          isLoading={isLoading}
          caption="Automation rules"
          emptyMessage="No rules yet. Create one to start reacting to fab events."
        />
      )}

      {dryRunFor !== null && <DryRunPanel ruleName={dryRunFor} />}

      <RuleDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </section>
  );
}

function describeAction(rule: Rule): string {
  return rule.action.kind === RULE_ACTION_SET_VARIABLE_VALUE
    ? `Set ${rule.action.variableName} = ${rule.action.valueExpression}`
    : `Highlight overlay for ${rule.action.durationMs} ms`;
}

function StateBadge({ state }: { state: RuleState }) {
  const tone =
    state === 'Active'
      ? 'text-accent-ok'
      : state === 'Archived'
        ? 'text-fg-muted'
        : 'text-accent-warn';
  return <span className={`text-xs font-medium ${tone}`}>{state}</span>;
}
