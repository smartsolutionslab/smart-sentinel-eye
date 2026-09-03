import { createApi } from '@reduxjs/toolkit/query/react';
import { gatewayBaseQuery, ifMatch } from './gateway.js';
import type { DefineVariableInput } from './systemVariables.schema.js';

export type { DefineVariableInput };

export type VariableType = 'String' | 'Number' | 'Boolean';
export type VariableState = 'Defined' | 'Archived';

export interface Variable {
  variableIdentifier: string;
  /** Optimistic-concurrency version; echo it back via If-Match to mutate (ADR-0113). */
  version: number;
  /**
   * The fab this variable belongs to (spec 014). A variable never arrives here
   * unless the caller is assigned to its fab, so this is always one of theirs —
   * which is what makes it safe to echo straight back as `fabId` on a mutation.
   */
  fab: string;
  name: string;
  type: VariableType;
  state: VariableState;
  /** Wire-string per FR-007. `null` when the variable is `Unset`. */
  value: string | null;
  truthyLabel: string | null;
  falsyLabel: string | null;
  createdAt: string;
  createdBy: string;
}

export interface SetVariableValueInput {
  name: string;
  value: string;
  /** The version this edit was built on (ADR-0113). */
  version: number;
  /**
   * The variable's own fab, taken from the row. A name is unique per fab, not
   * globally, so a caller holding several can match the same name twice —
   * sending the fab is what keeps the write unambiguous.
   */
  fabId?: string;
}

export interface ArchiveVariableInput {
  name: string;
  version: number;
  /** The variable's own fab, as for {@link SetVariableValueInput}. */
  fabId?: string;
}

export interface VariableReadInput {
  name: string;
  /**
   * Omit and the server resolves the name across every fab the caller holds,
   * which is 400 VARIABLE_FAB_AMBIGUOUS when two of them use it. Name the fab
   * to settle it.
   */
  fabId?: string;
}

export interface ListVariablesInput {
  /**
   * An exact state to list. When given it wins outright, so `Archived` reads
   * the archived ones back and `includeArchived` does not enter into it.
   */
  state?: VariableState;
  /**
   * Widens the default listing to every state. Archived variables are excluded
   * otherwise (#2015) — the same shape `GET /cameras` uses for retired ones.
   */
  includeArchived?: boolean;
  /** Omit to span every fab the caller holds; name one to narrow to it. */
  fabId?: string;
}

export interface ResolvedOverlaySnapshot {
  overlayIdentifier: string;
  resolvedText: string;
  version: number;
}

export const systemVariablesApi = createApi({
  reducerPath: 'systemVariablesApi',
  baseQuery: gatewayBaseQuery('system-variables/system-variables'),
  tagTypes: ['Variable', 'VariableList', 'OverlaySnapshot'],
  endpoints: (build) => ({
    defineVariable: build.mutation<string, DefineVariableInput & { fabId?: string }>({
      query: (body) => ({
        url: '',
        method: 'POST',
        // fabId travels as a query parameter, not in the body: an operator in
        // one fab has it inferred and is never asked (ADR-0114), and
        // defineVariableSchema mirrors the body alone.
        ...(body.fabId !== undefined && body.fabId !== '' ? { params: { fabId: body.fabId } } : {}),
        body: {
          name: body.name,
          type: body.type,
          ...(body.initialValue !== undefined && body.initialValue !== '' ? { initialValue: body.initialValue } : {}),
          ...(body.truthyLabel !== undefined ? { truthyLabel: body.truthyLabel } : {}),
          ...(body.falsyLabel !== undefined ? { falsyLabel: body.falsyLabel } : {}),
        },
      }),
      invalidatesTags: [{ type: 'VariableList', id: 'ALL' }],
    }),
    getVariable: build.query<Variable, VariableReadInput>({
      query: ({ name, fabId }) => ({
        url: `/${encodeURIComponent(name)}`,
        method: 'GET',
        params: fabId === undefined || fabId === '' ? undefined : { fabId },
      }),
      providesTags: (_r, _e, { name }) => [{ type: 'Variable', id: name }],
    }),
    listVariables: build.query<Variable[], ListVariablesInput | undefined>({
      query: (input) => ({
        url: '',
        method: 'GET',
        params: {
          ...(input?.state === undefined ? {} : { state: input.state }),
          ...(input?.includeArchived === undefined ? {} : { includeArchived: input.includeArchived }),
          ...(input?.fabId === undefined || input?.fabId === '' ? {} : { fabId: input.fabId }),
        },
      }),
      providesTags: () => [{ type: 'VariableList', id: 'ALL' }],
    }),
    setVariableValue: build.mutation<string, SetVariableValueInput>({
      query: ({ name, value, version, fabId }) => ({
        url: `/${encodeURIComponent(name)}/value`,
        method: 'PUT',
        headers: ifMatch(version),
        params: fabId === undefined || fabId === '' ? undefined : { fabId },
        body: { value },
      }),
      invalidatesTags: (_r, _e, { name }) => [
        { type: 'Variable', id: name },
        { type: 'VariableList', id: 'ALL' },
        // Resolved snapshots may change for any overlay referencing
        // this variable — the SignalR push will refresh them; we
        // also invalidate the cache to cover the cold-load case.
        { type: 'OverlaySnapshot', id: 'ALL' },
      ],
    }),
    getOverlaySnapshot: build.query<ResolvedOverlaySnapshot, string>({
      query: (overlayIdentifier) => ({
        url: '/snapshot',
        method: 'GET',
        params: { overlayIdentifier },
      }),
      // The 'ALL' sentinel mirrors the *List tag pattern: without it the
      // id:'ALL' invalidations (mutations + reconnect reconciliation,
      // spec 011 FR-008) never match a mounted snapshot query.
      providesTags: (_r, _e, id) => [
        { type: 'OverlaySnapshot', id },
        { type: 'OverlaySnapshot', id: 'ALL' },
      ],
    }),
    archiveVariable: build.mutation<string, ArchiveVariableInput>({
      query: ({ name, version, fabId }) => ({
        url: `/${encodeURIComponent(name)}/archive`,
        method: 'POST',
        headers: ifMatch(version),
        params: fabId === undefined || fabId === '' ? undefined : { fabId },
      }),
      invalidatesTags: (_r, _e, { name }) => [
        { type: 'Variable', id: name },
        { type: 'VariableList', id: 'ALL' },
        { type: 'OverlaySnapshot', id: 'ALL' },
      ],
    }),
  }),
});

export const {
  useDefineVariableMutation,
  useGetVariableQuery,
  useListVariablesQuery,
  useSetVariableValueMutation,
  useGetOverlaySnapshotQuery,
  useArchiveVariableMutation,
} = systemVariablesApi;
