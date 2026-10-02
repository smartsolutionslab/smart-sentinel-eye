import { useState } from 'react';
import {
  useArchiveRuleMutation,
  useListRulesQuery,
  usePublishRuleMutation,
  RULE_ACTION_SET_VARIABLE_VALUE,
  RULE_ACTION_HIGHLIGHT_OVERLAY,
  RULE_ACTION_SWITCH_WALL_SCENE,
  type Rule,
  type RuleState,
} from '@smart-sentinel-eye/shared/api/rules.api';
import { useListWallsQuery } from '@smart-sentinel-eye/shared/api/walls.api';
import { useListLayoutsQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import {
  CONFLICT_FALLBACK,
  isConflict,
  isStaleConflict,
  problemDetail,
} from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Badge, type BadgeTone } from '@smart-sentinel-eye/shared/ui/composites/Badge';
import { DataTable, type DataTableColumn } from '@smart-sentinel-eye/shared/ui/composites/DataTable';
import { FaultNotice } from '@smart-sentinel-eye/shared/ui/composites/FaultNotice';
import { RetryBanner } from '@smart-sentinel-eye/shared/ui/composites/RetryBanner';
import { RuleDialog } from './RuleDialog';
import { ArchiveConfirmation } from '../ArchiveConfirmation';
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
  // Holds the fab as well as the name: a dry run is fab-scoped, and the row
  // is the only place the fab is known without asking again.
  const [dryRunFor, setDryRunFor] = useState<{ name: string; fab: string } | null>(null);
  // Spec 036. Nullable subject rather than a boolean, matching dryRunFor above:
  // this page already holds two other open-states, and a subject carries the
  // name and version the confirmation and the request both need.
  const [archiveFor, setArchiveFor] = useState<{ name: string; version: number; fab: string } | null>(null);

  const {
    data: rules,
    isLoading,
    isError,
    refetch,
  } = useListRulesQuery(stateFilter === undefined ? undefined : { state: stateFilter });
  const [publishRule, publishState] = usePublishRuleMutation();
  const [archiveRule, archiveState] = useArchiveRuleMutation();

  const { isLoading: archiving } = archiveState;

  // SwitchWallScene's wording ("Switch Line 3 rotation to Line 3 fault
  // view") names a wall and a layout the DTO carries only as identifiers —
  // resolved here, the same two lookups RuleDialog and WallsPage already
  // use, and falling back to the raw identifier for a stale rule whose wall
  // or layout no longer resolves (R3) rather than hiding the action.
  const { data: wallsData } = useListWallsQuery();
  const wallName = (wallIdentifier: string): string =>
    wallsData?.find((wall) => wall.wallIdentifier === wallIdentifier)?.name ?? wallIdentifier;

  const { data: layoutsData } = useListLayoutsQuery('Published');
  const layoutName = (layoutIdentifier: string): string =>
    layoutsData?.published.find((layout) => layout.layoutIdentifier === layoutIdentifier)?.name ?? layoutIdentifier;

  // Both of these used to discard their failure. With optimistic concurrency
  // live (ADR-0113) a refusal is routine, and a discarded one is worse than
  // silence on publish: the list still refetches, the row flips to Published —
  // by the *other* operator — and the refusal reads as success.
  const mutationError = publishState.error ?? archiveState.error;

  const columns: DataTableColumn<Rule>[] = [
    { id: 'name', header: 'Name', cell: (rule) => <span className="font-medium">{rule.name}</span> },
    {
      // Always rendered, not only for a multi-fab operator: a name is unique
      // per fab rather than globally, so without it two rows can read
      // identically and there is no way to tell which is which.
      id: 'fab',
      header: 'Fab',
      cell: (rule) => (
        <span data-testid="rule-fab" className="text-xs">
          {rule.fab}
        </span>
      ),
    },
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
    {
      id: 'action',
      header: 'Action',
      cell: (rule) => <span className="text-xs">{describeAction(rule, wallName, layoutName)}</span>,
    },
    { id: 'state', header: 'State', cell: (rule) => <StateBadge state={rule.state} /> },
    {
      id: 'actions',
      header: 'Actions',
      cell: (rule) => (
        <div className="flex gap-1">
          {/*
            Each mutation carries the row's own fab. The rule is already in
            front of us, so there is nothing to choose and nothing to infer —
            and a multi-fab operator would otherwise be refused outright.
          */}
          {rule.state === 'Draft' && (
            <Button
              type="button"
              variant="secondary"
              onClick={() => void publishRule({ name: rule.name, version: rule.version, fabId: rule.fab })}
            >
              Publish
            </Button>
          )}
          {rule.state !== 'Archived' && (
            <Button
              type="button"
              variant="ghost"
              onClick={() => setArchiveFor({ name: rule.name, version: rule.version, fab: rule.fab })}
            >
              Archive
            </Button>
          )}
          <Button
            type="button"
            variant="ghost"
            onClick={() => setDryRunFor(dryRunFor?.name === rule.name ? null : { name: rule.name, fab: rule.fab })}
          >
            {dryRunFor?.name === rule.name ? 'Hide dry run' : 'Dry run'}
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

      {mutationError !== undefined && (
        <FaultNotice>
          {problemDetail(
            mutationError,
            isStaleConflict(mutationError) ? CONFLICT_FALLBACK : 'Could not apply that change.',
          )}{' '}
          {isConflict(mutationError) && (
            // Reload, never retry: retrying replays the same stale intent over
            // whoever wrote in between.
            <button type="button" className="underline" onClick={() => void refetch()}>
              Reload
            </button>
          )}
        </FaultNotice>
      )}

      {isError ? (
        <RetryBanner message="Could not load rules." onRetry={() => void refetch()} />
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

      {dryRunFor !== null && <DryRunPanel ruleName={dryRunFor.name} fabId={dryRunFor.fab} />}

      {/* Spec 036 FR-005. Says what an operator loses, not merely that it is
          permanent: an archived rule cannot be published again, and the only
          way to a replacement is cloning — which produces a new rule with its
          own history, not this one restored. Taken from Rule's own
          documentation. Deliberately silent on whether evaluation stops: that
          was not checked, so it is not claimed. */}
      <ArchiveConfirmation
        subject={archiveFor === null ? null : `rule ${archiveFor.name}`}
        onCancel={() => setArchiveFor(null)}
        pending={archiving}
        onConfirm={() => {
          if (archiveFor === null) {
            return;
          }
          void archiveRule({ name: archiveFor.name, version: archiveFor.version, fabId: archiveFor.fab });
          setArchiveFor(null);
        }}
      >
        <p>This cannot be undone.</p>
        <p>
          The rule cannot be published again. Authoring a replacement means cloning it, which creates a new rule with
          its own history.
        </p>
      </ArchiveConfirmation>

      <RuleDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </section>
  );
}

function describeAction(
  rule: Rule,
  wallName: (wallIdentifier: string) => string,
  layoutName: (layoutIdentifier: string) => string,
): string {
  switch (rule.action.kind) {
    case RULE_ACTION_SET_VARIABLE_VALUE:
      return `Set ${rule.action.variableName} = ${rule.action.valueExpression}`;
    case RULE_ACTION_HIGHLIGHT_OVERLAY:
      return `Highlight overlay for ${rule.action.durationMs} ms`;
    case RULE_ACTION_SWITCH_WALL_SCENE: {
      const wall = wallName(rule.action.wall ?? '');
      return rule.action.sceneTarget === 'Next'
        ? `Switch ${wall} to its next scene`
        : `Switch ${wall} to ${layoutName(rule.action.targetLayout ?? '')}`;
    }
    default: {
      const exhaustive: never = rule.action.kind;
      return exhaustive;
    }
  }
}

const RULE_STATE_TONE: Record<RuleState, BadgeTone> = {
  Active: 'active',
  Draft: 'warning',
  Archived: 'neutral',
};

function StateBadge({ state }: { state: RuleState }) {
  return <Badge tone={RULE_STATE_TONE[state]}>{state}</Badge>;
}
