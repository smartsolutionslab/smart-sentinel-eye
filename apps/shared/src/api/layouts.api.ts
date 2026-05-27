import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

export type LayoutRevisionState = 'Draft' | 'Published' | 'Archived';

export interface LayoutRevision {
  revisionIdentifier: string;
  revisionNumber: number;
  state: LayoutRevisionState;
  cameraIdentifier: string;
  /** Spec 004 binding. `null` when no overlay is attached to the layout. */
  overlayIdentifier: string | null;
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
  cameraIdentifier: string;
  /** Spec 004 binding (current Published revision's overlay). `null` if unbound. */
  overlayIdentifier: string | null;
  publishedAt: string;
}

export interface ListLayoutsResponse {
  chains: Layout[];
  published: PublishedLayout[];
}

export interface CreateLayoutDraftInput {
  name: string;
  cameraIdentifier: string;
  /** Optional overlay binding (spec 004 PR B'). Omitted == no overlay. */
  overlayIdentifier?: string;
}

export interface RevisionRouteInput {
  layoutIdentifier: string;
  revisionNumber: number;
}

export const layoutsApi = createApi({
  reducerPath: 'layoutsApi',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/layouts' }),
  tagTypes: ['Layout', 'LayoutList'],
  endpoints: (build) => ({
    createLayoutDraft: build.mutation<string, CreateLayoutDraftInput>({
      query: (body) => ({
        url: '',
        method: 'POST',
        body: {
          name: body.name,
          cameraIdentifier: body.cameraIdentifier,
          ...(body.overlayIdentifier !== undefined && body.overlayIdentifier !== ''
            ? { overlayIdentifier: body.overlayIdentifier }
            : {}),
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
      RevisionRouteInput & { cameraIdentifier: string }
    >({
      query: ({ layoutIdentifier, revisionNumber, cameraIdentifier }) => ({
        url: `/${layoutIdentifier}/revisions/${revisionNumber}`,
        method: 'PATCH',
        body: { cameraIdentifier },
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
