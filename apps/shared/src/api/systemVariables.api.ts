import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

export type VariableType = 'String' | 'Number' | 'Boolean';
export type VariableState = 'Defined' | 'Archived';

export interface Variable {
  variableIdentifier: string;
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

export interface DefineVariableInput {
  name: string;
  type: VariableType;
  initialValue?: string;
  truthyLabel?: string;
  falsyLabel?: string;
}

export interface SetVariableValueInput {
  name: string;
  value: string;
}

export interface ResolvedOverlaySnapshot {
  overlayIdentifier: string;
  resolvedText: string;
  version: number;
}

export const systemVariablesApi = createApi({
  reducerPath: 'systemVariablesApi',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/system-variables' }),
  tagTypes: ['Variable', 'VariableList', 'OverlaySnapshot'],
  endpoints: (build) => ({
    defineVariable: build.mutation<string, DefineVariableInput>({
      query: (body) => ({
        url: '',
        method: 'POST',
        body: {
          name: body.name,
          type: body.type,
          ...(body.initialValue !== undefined && body.initialValue !== ''
            ? { initialValue: body.initialValue }
            : {}),
          ...(body.truthyLabel !== undefined ? { truthyLabel: body.truthyLabel } : {}),
          ...(body.falsyLabel !== undefined ? { falsyLabel: body.falsyLabel } : {}),
        },
      }),
      invalidatesTags: [{ type: 'VariableList', id: 'ALL' }],
    }),
    getVariable: build.query<Variable, string>({
      query: (name) => `/${encodeURIComponent(name)}`,
      providesTags: (_r, _e, name) => [{ type: 'Variable', id: name }],
    }),
    listVariables: build.query<Variable[], VariableState | undefined>({
      query: (state) => ({
        url: '',
        method: 'GET',
        params: state === undefined ? undefined : { state },
      }),
      providesTags: () => [{ type: 'VariableList', id: 'ALL' }],
    }),
    setVariableValue: build.mutation<string, SetVariableValueInput>({
      query: ({ name, value }) => ({
        url: `/${encodeURIComponent(name)}/value`,
        method: 'PUT',
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
      providesTags: (_r, _e, id) => [{ type: 'OverlaySnapshot', id }],
    }),
    archiveVariable: build.mutation<string, string>({
      query: (name) => ({
        url: `/${encodeURIComponent(name)}/archive`,
        method: 'POST',
      }),
      invalidatesTags: (_r, _e, name) => [
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
