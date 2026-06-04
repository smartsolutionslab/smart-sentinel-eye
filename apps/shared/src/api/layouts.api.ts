import { createApi } from '@reduxjs/toolkit/query/react';
import { gatewayBaseQuery } from './gateway.js';
import type { CreateLayoutDraftInput, EditDraftRevisionInput } from './layouts.schema.js';

export type { CreateLayoutDraftInput, EditDraftRevisionInput };

export type LayoutRevisionState = 'Draft' | 'Published' | 'Archived';

/**
 * One tile of a layout grid (spec 010). Mirrors the backend `TileDto`
 * shape exactly: a required camera, an optional overlay (`null` when
 * unbound), at zero-indexed `(row, col)`. An overlay MAY be reused across
 * tiles (ADR-0112 §2 — highlight-all-matching).
 */
export interface LayoutTile {
  cameraIdentifier: string;
  overlayIdentifier: string | null;
  row: number;
  col: number;
}

export interface LayoutRevision {
  revisionIdentifier: string;
  revisionNumber: number;
  state: LayoutRevisionState;
  gridRows: number;
  gridCols: number;
  tiles: LayoutTile[];
  createdAt: string;
  createdBy: string;
  publishedAt: string | null;
  archivedAt: string | null;
}

export interface Layout {
  layoutIdentifier: string;
  name: string;
  createdAt: string;
  createdBy: string;
  revisions: LayoutRevision[];
}

export interface PublishedLayout {
  layoutIdentifier: string;
  name: string;
  revisionNumber: number;
  gridRows: number;
  gridCols: number;
  tiles: LayoutTile[];
  publishedAt: string;
}

export interface ListLayoutsResponse {
  chains: Layout[];
  published: PublishedLayout[];
}

export interface RevisionRouteInput {
  layoutIdentifier: string;
  revisionNumber: number;
}

export const layoutsApi = createApi({
  reducerPath: 'layoutsApi',
  baseQuery: gatewayBaseQuery('layout-composition/layouts'),
  tagTypes: ['Layout', 'LayoutList'],
  endpoints: (build) => ({
    createLayoutDraft: build.mutation<string, CreateLayoutDraftInput>({
      query: (body) => ({
        url: '',
        method: 'POST',
        body: {
          name: body.name,
          grid: body.grid,
          tiles: body.tiles,
        },
      }),
      invalidatesTags: [{ type: 'LayoutList', id: 'ALL' }],
    }),
    getLayout: build.query<Layout, string>({
      query: (layoutIdentifier) => `/${layoutIdentifier}`,
      providesTags: (_result, _error, layoutIdentifier) => [
        { type: 'Layout', id: layoutIdentifier },
      ],
    }),
    listLayouts: build.query<ListLayoutsResponse, LayoutRevisionState | undefined>({
      query: (state) => ({
        url: '',
        method: 'GET',
        params: state === undefined ? undefined : { state },
      }),
      providesTags: () => [{ type: 'LayoutList', id: 'ALL' }],
    }),
    publishRevision: build.mutation<number, RevisionRouteInput>({
      query: ({ layoutIdentifier, revisionNumber }) => ({
        url: `/${layoutIdentifier}/revisions/${revisionNumber}/publish`,
        method: 'POST',
      }),
      invalidatesTags: (_r, _e, { layoutIdentifier }) => [
        { type: 'Layout', id: layoutIdentifier },
        { type: 'LayoutList', id: 'ALL' },
      ],
    }),
    archiveRevision: build.mutation<number, RevisionRouteInput>({
      query: ({ layoutIdentifier, revisionNumber }) => ({
        url: `/${layoutIdentifier}/revisions/${revisionNumber}/archive`,
        method: 'POST',
      }),
      invalidatesTags: (_r, _e, { layoutIdentifier }) => [
        { type: 'Layout', id: layoutIdentifier },
        { type: 'LayoutList', id: 'ALL' },
      ],
    }),
    branchDraftRevision: build.mutation<number, string>({
      query: (layoutIdentifier) => ({
        url: `/${layoutIdentifier}/draft`,
        method: 'POST',
      }),
      invalidatesTags: (_r, _e, layoutIdentifier) => [
        { type: 'Layout', id: layoutIdentifier },
        { type: 'LayoutList', id: 'ALL' },
      ],
    }),
    editDraftRevision: build.mutation<
      number,
      RevisionRouteInput & EditDraftRevisionInput
    >({
      query: ({ layoutIdentifier, revisionNumber, grid, tiles }) => ({
        url: `/${layoutIdentifier}/revisions/${revisionNumber}`,
        method: 'PATCH',
        body: { grid, tiles },
      }),
      invalidatesTags: (_r, _e, { layoutIdentifier }) => [
        { type: 'Layout', id: layoutIdentifier },
        { type: 'LayoutList', id: 'ALL' },
      ],
    }),
    revertRevision: build.mutation<number, RevisionRouteInput>({
      query: ({ layoutIdentifier, revisionNumber }) => ({
        url: `/${layoutIdentifier}/revisions/${revisionNumber}/revert`,
        method: 'POST',
      }),
      invalidatesTags: (_r, _e, { layoutIdentifier }) => [
        { type: 'Layout', id: layoutIdentifier },
        { type: 'LayoutList', id: 'ALL' },
      ],
    }),
  }),
});

export const {
  useCreateLayoutDraftMutation,
  useGetLayoutQuery,
  useListLayoutsQuery,
  usePublishRevisionMutation,
  useArchiveRevisionMutation,
  useBranchDraftRevisionMutation,
  useEditDraftRevisionMutation,
  useRevertRevisionMutation,
} = layoutsApi;
