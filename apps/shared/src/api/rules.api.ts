import { createApi } from '@reduxjs/toolkit/query/react';
import { gatewayBaseQuery, ifMatch } from './gateway.js';
import type { CreateRuleInput } from './rules.schema.js';

export type { CreateRuleInput };

export type RuleState = 'Draft' | 'Active' | 'Archived';

export const RULE_ACTION_SET_VARIABLE_VALUE = 'SetVariableValue';
export const RULE_ACTION_HIGHLIGHT_OVERLAY = 'HighlightOverlay';

/**
 * Discriminated wire shape mirroring the server's `RuleActionDto`: `kind` is
 * the tag and exactly one variant's fields are populated, so a caller cannot
 * express a combination that means nothing.
 */
export interface RuleAction {
  kind: typeof RULE_ACTION_SET_VARIABLE_VALUE | typeof RULE_ACTION_HIGHLIGHT_OVERLAY;
  variableName: string | null;
  valueExpression: string | null;
  overlay: string | null;
  durationMs: number | null;
}

export interface Rule {
  /** Optimistic-concurrency version; echo it back via If-Match to mutate (ADR-0113). */
  version: number;
  ruleIdentifier: string;
  /**
   * The fab this rule belongs to (spec 013). A rule never arrives here unless
   * the caller is assigned to its fab, so this is always one of theirs — which
   * is what makes it safe to echo straight back as `fabId` on a mutation.
   */
  fab: string;
  name: string;
  triggerSource: string;
  triggerKind: string;
  /** Raw AEL, exactly as authored — round-trips into the editor unchanged. */
  predicate: string;
  action: RuleAction;
  state: RuleState;
  createdAt: string;
  createdBy: string;
  publishedAt: string | null;
  archivedAt: string | null;
}

export interface ListRulesFilters {
  state?: RuleState;
  triggerSource?: string;
  triggerKind?: string;
  /** Omit to span every fab the caller holds; name one to narrow to it. */
  fabId?: string;
}

export interface RuleRouteInput {
  name: string;
  version: number;
  /**
   * The rule's own fab, taken from the row. A name is unique per fab, not
   * globally, so a caller holding several can match the same name twice —
   * sending the fab is what keeps publish and archive unambiguous.
   */
  fabId?: string;
}

export interface DryRunInput {
  name: string;
  sampleEvent: string;
  fabId?: string;
}

export interface RuleReadInput {
  name: string;
  /**
   * Omit and the server resolves the name across every fab the caller holds,
   * which is 400 RULE_FAB_AMBIGUOUS when two of them use it. Name the fab to
   * settle it.
   */
  fabId?: string;
}

/** `fabId` rides the query string; everything else is the body. */
export type CreateRuleArgs = CreateRuleInput & { fabId?: string };

export interface DryRunResult {
  matched: boolean;
  /** Present only when the predicate matched AND the action sets a variable. */
  evaluatedValue: string | null;
}

export const rulesApi = createApi({
  reducerPath: 'rulesApi',
  baseQuery: gatewayBaseQuery('automation/rules'),
  tagTypes: ['Rule', 'RuleList'],
  endpoints: (build) => ({
    listRules: build.query<Rule[], ListRulesFilters | undefined>({
      query: (filters) => ({
        url: '',
        method: 'GET',
        // Omit empty filters entirely: the server rejects an unrecognised
        // state rather than ignoring it, so sending '' would 400.
        params: filters === undefined ? undefined : stripEmpty(filters),
      }),
      providesTags: () => [{ type: 'RuleList', id: 'ALL' }],
    }),
    getRule: build.query<Rule, RuleReadInput>({
      query: ({ name, fabId }) => ({ url: withFab(`/${encodeURIComponent(name)}`, fabId), method: 'GET' }),
      providesTags: (_r, _e, { name }) => [{ type: 'Rule', id: name }],
    }),
    createRule: build.mutation<string, CreateRuleArgs>({
      // fabId is destructured out so it rides the query string rather than
      // landing in the body, where the server would ignore it.
      query: ({ fabId, ...body }) => ({ url: withFab('', fabId), method: 'POST', body }),
      invalidatesTags: [{ type: 'RuleList', id: 'ALL' }],
    }),
    publishRule: build.mutation<string, RuleRouteInput>({
      query: ({ name, version, fabId }) => ({
        url: withFab(`/${encodeURIComponent(name)}/publish`, fabId),
        method: 'POST',
        headers: ifMatch(version),
      }),
      invalidatesTags: (_r, _e, { name }) => [{ type: 'Rule', id: name }, { type: 'RuleList', id: 'ALL' }],
    }),
    archiveRule: build.mutation<string, RuleRouteInput>({
      query: ({ name, version, fabId }) => ({
        url: withFab(`/${encodeURIComponent(name)}/archive`, fabId),
        method: 'POST',
        headers: ifMatch(version),
      }),
      invalidatesTags: (_r, _e, { name }) => [{ type: 'Rule', id: name }, { type: 'RuleList', id: 'ALL' }],
    }),
    // A POST that is a read: it carries a sample-event body but persists
    // nothing, so it is a mutation only in RTK's HTTP-verb sense and
    // deliberately invalidates no tags.
    dryRunRule: build.mutation<DryRunResult, DryRunInput>({
      query: ({ name, sampleEvent, fabId }) => ({
        url: withFab(`/${encodeURIComponent(name)}/dry-run`, fabId),
        method: 'POST',
        body: { sampleEvent },
      }),
    }),
  }),
});

function stripEmpty(filters: ListRulesFilters): Record<string, string> {
  const params: Record<string, string> = {};
  if (filters.state) params['state'] = filters.state;
  if (filters.triggerSource) params['triggerSource'] = filters.triggerSource;
  if (filters.triggerKind) params['triggerKind'] = filters.triggerKind;
  if (filters.fabId) params['fabId'] = filters.fabId;
  return params;
}

function withFab(path: string, fabId: string | undefined): string {
  return fabId ? `${path}?fabId=${encodeURIComponent(fabId)}` : path;
}

export const {
  useListRulesQuery,
  useGetRuleQuery,
  useCreateRuleMutation,
  usePublishRuleMutation,
  useArchiveRuleMutation,
  useDryRunRuleMutation,
} = rulesApi;
