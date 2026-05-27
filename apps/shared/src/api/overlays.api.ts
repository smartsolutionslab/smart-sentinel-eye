import { createApi, fetchBaseQuery } from '@reduxjs/toolkit/query/react';

export type OverlayRevisionState = 'Draft' | 'Published' | 'Archived';

export interface OverlayLabel {
  text: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
  fontSizePx: number;
}

export interface OverlayRevision extends OverlayLabel {
  revisionIdentifier: string;
  revisionNumber: number;
  state: OverlayRevisionState;
  createdAt: string;
  createdBy: string;
  publishedAt: string | null;
  archivedAt: string | null;
}

export interface Overlay {
  overlayIdentifier: string;
  name: string;
  createdAt: string;
  createdBy: string;
  revisions: OverlayRevision[];
}

export interface PublishedOverlay {
  overlayIdentifier: string;
  name: string;
  revisionNumber: number;
  text: string;
  publishedAt: string;
}

export interface ListOverlaysResponse {
  chains: Overlay[];
  published: PublishedOverlay[];
}

export interface CreateOverlayDraftInput {
  name: string;
  label: OverlayLabel;
}

export interface OverlayRevisionRouteInput {
  overlayIdentifier: string;
  revisionNumber: number;
}

export const overlaysApi = createApi({
  reducerPath: 'overlaysApi',
  baseQuery: fetchBaseQuery({ baseUrl: '/api/overlays' }),
  tagTypes: ['Overlay', 'OverlayList'],
  endpoints: (build) => ({
    createOverlayDraft: build.mutation<string, CreateOverlayDraftInput>({
      query: (body) => ({ url: '', method: 'POST', body }),
      invalidatesTags: [{ type: 'OverlayList', id: 'ALL' }],
    }),
    getOverlay: build.query<Overlay, string>({
      query: (overlayIdentifier) => `/${overlayIdentifier}`,
      providesTags: (_result, _error, overlayIdentifier) => [
        { type: 'Overlay', id: overlayIdentifier },
      ],
    }),
    listOverlays: build.query<ListOverlaysResponse, OverlayRevisionState | undefined>({
      query: (state) => ({
        url: '',
        method: 'GET',
        params: state === undefined ? undefined : { state },
      }),
      providesTags: () => [{ type: 'OverlayList', id: 'ALL' }],
    }),
    publishOverlayRevision: build.mutation<number, OverlayRevisionRouteInput>({
      query: ({ overlayIdentifier, revisionNumber }) => ({
        url: `/${overlayIdentifier}/revisions/${revisionNumber}/publish`,
        method: 'POST',
      }),
      invalidatesTags: (_r, _e, { overlayIdentifier }) => [
        { type: 'Overlay', id: overlayIdentifier },
        { type: 'OverlayList', id: 'ALL' },
      ],
    }),
    archiveOverlayRevision: build.mutation<number, OverlayRevisionRouteInput>({
      query: ({ overlayIdentifier, revisionNumber }) => ({
        url: `/${overlayIdentifier}/revisions/${revisionNumber}/archive`,
        method: 'POST',
      }),
      invalidatesTags: (_r, _e, { overlayIdentifier }) => [
        { type: 'Overlay', id: overlayIdentifier },
        { type: 'OverlayList', id: 'ALL' },
      ],
    }),
  }),
});

export const {
  useCreateOverlayDraftMutation,
  useGetOverlayQuery,
  useListOverlaysQuery,
  usePublishOverlayRevisionMutation,
  useArchiveOverlayRevisionMutation,
} = overlaysApi;
